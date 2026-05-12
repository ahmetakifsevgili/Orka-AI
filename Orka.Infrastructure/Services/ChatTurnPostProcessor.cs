using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class ChatTurnPostProcessor : IChatTurnPostProcessor
{
    private readonly IBackgroundTaskQueue _backgroundQueue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChatTurnPostProcessor> _logger;

    public ChatTurnPostProcessor(
        IBackgroundTaskQueue backgroundQueue,
        IServiceScopeFactory scopeFactory,
        ILogger<ChatTurnPostProcessor> logger)
    {
        _backgroundQueue = backgroundQueue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public ValueTask ScheduleAsync(ChatTurnPostProcessRequest request, CancellationToken ct = default)
    {
        if (request.UserId == Guid.Empty ||
            request.SessionId == Guid.Empty ||
            request.AssistantMessageId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.AssistantContent))
        {
            return ValueTask.CompletedTask;
        }

        return _backgroundQueue.QueueAsync(new BackgroundTaskItem(
            "agent-feedback-loop",
            request.UserId,
            request.CorrelationId,
            workCt => ProcessAsync(request, workCt),
            MaxAttempts: 1,
            Timeout: TimeSpan.FromSeconds(120)), ct);
    }

    private async Task ProcessAsync(ChatTurnPostProcessRequest request, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        if (!await db.Messages.AnyAsync(m =>
                m.Id == request.AssistantMessageId &&
                m.UserId == request.UserId &&
                m.SessionId == request.SessionId,
                ct))
        {
            _logger.LogWarning(
                "[ChatPostProcess] Assistant message missing. MessageId={MessageId} Session={SessionId}",
                request.AssistantMessageId,
                request.SessionId);
            return;
        }

        await RunEvaluatorAsync(scope.ServiceProvider, db, request, ct);
        await RunAnalyzerWikiAndProgressionAsync(scope.ServiceProvider, db, request, ct);
    }

    private async Task RunEvaluatorAsync(
        IServiceProvider services,
        OrkaDbContext db,
        ChatTurnPostProcessRequest request,
        CancellationToken ct)
    {
        try
        {
            var alreadyEvaluated = await db.AgentEvaluations.AnyAsync(e =>
                e.UserId == request.UserId &&
                e.MessageId == request.AssistantMessageId &&
                e.AgentRole == request.AgentRole,
                ct);
            if (alreadyEvaluated)
                return;

            var evaluator = services.GetRequiredService<IEvaluatorAgent>();
            var (score, feedback) = await evaluator.EvaluateInteractionAsync(
                request.SessionId,
                request.UserContent,
                request.AssistantContent,
                request.AgentRole,
                request.TopicId,
                ct);

            var stillMissing = !await db.AgentEvaluations.AnyAsync(e =>
                e.UserId == request.UserId &&
                e.MessageId == request.AssistantMessageId &&
                e.AgentRole == request.AgentRole,
                ct);
            if (stillMissing)
            {
                db.AgentEvaluations.Add(new AgentEvaluation
                {
                    SessionId = request.SessionId,
                    UserId = request.UserId,
                    MessageId = request.AssistantMessageId,
                    AgentRole = request.AgentRole,
                    UserInput = request.UserContent,
                    AgentResponse = request.AssistantContent,
                    EvaluationScore = score,
                    EvaluatorFeedback = feedback,
                    CreatedAt = DateTime.UtcNow
                });
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex) when (IsAgentEvaluationDuplicate(ex))
                {
                    db.ChangeTracker.Clear();
                    _logger.LogInformation(
                        "[ChatPostProcess] Duplicate evaluator insert ignored. MessageId={MessageId} AgentRole={AgentRole}",
                        request.AssistantMessageId,
                        request.AgentRole);
                    return;
                }
            }

            if (score < 7 && request.AgentRole == "TutorAgent")
            {
                var redis = services.GetRequiredService<IRedisMemoryService>();
                await redis.SetLowQualityFeedbackAsync(request.SessionId, score, feedback);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[ChatPostProcess] Evaluator failed. MessageId={MessageId} Correlation={CorrelationId}",
                request.AssistantMessageId,
                request.CorrelationId);
        }
    }

    private async Task RunAnalyzerWikiAndProgressionAsync(
        IServiceProvider services,
        OrkaDbContext db,
        ChatTurnPostProcessRequest request,
        CancellationToken ct)
    {
        try
        {
            var analyzer = services.GetRequiredService<IAnalyzerAgent>();
            var msgs = await db.Messages
                .Where(m => m.SessionId == request.SessionId && m.UserId == request.UserId)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync(ct);

            var analyzerResult = await analyzer.AnalyzeCompletionAsync(msgs);

            if (request.TopicId.HasValue && analyzerResult.IntentData != null)
            {
                var redis = services.GetRequiredService<IRedisMemoryService>();
                await redis.RecordStudentProfileAsync(
                    request.TopicId.Value,
                    analyzerResult.IntentData.UnderstandingScore,
                    analyzerResult.IntentData.Weaknesses);
            }

            var assistantMessageCount = msgs.Count(m => m.Role == "assistant");
            var shouldSummarize = analyzerResult.IsComplete ||
                                  (assistantMessageCount >= 3 && msgs.Count >= 6 && msgs.Count % 6 == 0);

            if (!shouldSummarize || !request.TopicId.HasValue)
                return;

            var summarizer = services.GetRequiredService<ISummarizerAgent>();
            await summarizer.SummarizeAndSaveWikiAsync(request.SessionId, request.TopicId.Value, request.UserId);

            var progress = services.GetRequiredService<ITopicProgressPropagator>();
            await progress.HandleCompletionAnalysisProgressionAsync(
                request.UserId,
                request.SessionId,
                request.TopicId.Value,
                ct);

            await EvaluateSummarizerOutputAsync(services, db, request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[ChatPostProcess] Analyzer/wiki pipeline failed. MessageId={MessageId} Correlation={CorrelationId}",
                request.AssistantMessageId,
                request.CorrelationId);

            await MarkPendingWikiFailedAsync(db, request, ct);
        }
    }

    private async Task EvaluateSummarizerOutputAsync(
        IServiceProvider services,
        OrkaDbContext db,
        ChatTurnPostProcessRequest request,
        CancellationToken ct)
    {
        try
        {
            var alreadyEvaluated = await db.AgentEvaluations.AnyAsync(e =>
                e.UserId == request.UserId &&
                e.MessageId == request.AssistantMessageId &&
                e.AgentRole == "SummarizerAgent",
                ct);
            if (alreadyEvaluated || !request.TopicId.HasValue)
                return;

            var wikiService = services.GetRequiredService<IWikiService>();
            var wikiContent = await wikiService.GetWikiFullContentAsync(request.TopicId.Value, request.UserId);
            if (string.IsNullOrWhiteSpace(wikiContent))
                return;

            var topicTitle = await db.Topics
                .Where(t => t.Id == request.TopicId.Value && t.UserId == request.UserId)
                .Select(t => t.Title)
                .FirstOrDefaultAsync(ct) ?? "Konu";

            var evaluator = services.GetRequiredService<IEvaluatorAgent>();
            var (wikiScore, wikiFeedback) = await evaluator.EvaluateInteractionAsync(
                request.SessionId,
                topicTitle,
                wikiContent,
                "SummarizerAgent",
                request.TopicId,
                ct);

            db.AgentEvaluations.Add(new AgentEvaluation
            {
                SessionId = request.SessionId,
                UserId = request.UserId,
                MessageId = request.AssistantMessageId,
                AgentRole = "SummarizerAgent",
                UserInput = topicTitle,
                AgentResponse = wikiContent.Length > 500 ? wikiContent[..500] + "..." : wikiContent,
                EvaluationScore = wikiScore,
                EvaluatorFeedback = wikiFeedback,
                CreatedAt = DateTime.UtcNow
            });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsAgentEvaluationDuplicate(ex))
            {
                db.ChangeTracker.Clear();
                _logger.LogInformation(
                    "[ChatPostProcess] Duplicate summarizer evaluation ignored. MessageId={MessageId}",
                    request.AssistantMessageId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[ChatPostProcess] Summarizer evaluation failed. MessageId={MessageId}",
                request.AssistantMessageId);
        }
    }

    private static async Task MarkPendingWikiFailedAsync(
        OrkaDbContext db,
        ChatTurnPostProcessRequest request,
        CancellationToken ct)
    {
        if (!request.TopicId.HasValue)
            return;

        var failedPage = await db.WikiPages
            .FirstOrDefaultAsync(p =>
                p.UserId == request.UserId &&
                p.TopicId == request.TopicId.Value &&
                (p.Status == "pending" || p.Status == "learning"),
                ct);
        if (failedPage != null)
        {
            failedPage.Status = "failed";
            await db.SaveChangesAsync(ct);
        }
    }

    private static bool IsAgentEvaluationDuplicate(DbUpdateException ex)
    {
        var message = ex.ToString();
        return message.Contains("IX_AgentEvaluations_MessageId_AgentRole", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unique", StringComparison.OrdinalIgnoreCase);
    }
}
