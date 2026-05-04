using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.DTOs.PlanDiagnostic;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class PlanDiagnosticService : IPlanDiagnosticService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan StateTtl = TimeSpan.FromHours(2);

    private readonly OrkaDbContext _db;
    private readonly IKorteksAgent _korteks;
    private readonly IPlanResearchCompressor _compressor;
    private readonly IAIAgentFactory _factory;
    private readonly IPlanDiagnosticStateStore _stateStore;
    private readonly IQuizAttemptRecorder _quizRecorder;
    private readonly IDeepPlanAgent _deepPlan;
    private readonly ILogger<PlanDiagnosticService> _logger;

    public PlanDiagnosticService(
        OrkaDbContext db,
        IKorteksAgent korteks,
        IPlanResearchCompressor compressor,
        IAIAgentFactory factory,
        IPlanDiagnosticStateStore stateStore,
        IQuizAttemptRecorder quizRecorder,
        IDeepPlanAgent deepPlan,
        ILogger<PlanDiagnosticService> logger)
    {
        _db = db;
        _korteks = korteks;
        _compressor = compressor;
        _factory = factory;
        _stateStore = stateStore;
        _quizRecorder = quizRecorder;
        _deepPlan = deepPlan;
        _logger = logger;
    }

    public async Task<StartPlanDiagnosticResponse> StartAsync(
        Guid userId,
        StartPlanDiagnosticRequest request,
        CancellationToken ct = default)
    {
        var topic = await _db.Topics.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TopicId && t.UserId == userId, ct);

        if (topic == null)
        {
            throw new InvalidOperationException("Topic not found for plan diagnostic start.");
        }

        var topicTitle = string.IsNullOrWhiteSpace(request.TopicTitle) ? topic.Title : request.TopicTitle.Trim();
        var planRequestId = Guid.NewGuid();
        var quizRunId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var state = new PlanDiagnosticStateDto
        {
            PlanRequestId = planRequestId,
            UserId = userId,
            TopicId = request.TopicId,
            SessionId = request.SessionId,
            TopicTitle = topicTitle,
            UserLevel = string.IsNullOrWhiteSpace(request.UserLevel) ? topic.LanguageLevel ?? "Bilinmiyor" : request.UserLevel.Trim(),
            Status = PlanDiagnosticStatus.Researching,
            QuizRunId = quizRunId,
            CreatedAt = now,
            ExpiresAt = now.Add(StateTtl)
        };

        await _stateStore.SaveAsync(state, ct);

        try
        {
            var research = await _korteks.RunResearchWithEvidenceAsync(topicTitle, userId, request.TopicId, ct: ct);
            var compressed = _compressor.Compress(research);
            var compressedBlock = _compressor.BuildPromptBlock(compressed);

            state.Status = PlanDiagnosticStatus.ResearchReady;
            state.CompressedResearchContextJson = JsonSerializer.Serialize(compressed, JsonOptions);
            state.CompressedResearchPromptBlock = compressedBlock;
            state.GroundingMode = compressed.GroundingMode;
            state.SourceCount = compressed.SourceCount;
            await _stateStore.SaveAsync(state, ct);

            var quizJson = await GenerateDiagnosticQuizFromStoredContextAsync(topicTitle, compressedBlock, ct);
            var questionCount = CountQuestions(quizJson);

            _db.QuizRuns.Add(new QuizRun
            {
                Id = quizRunId,
                UserId = userId,
                TopicId = request.TopicId,
                SessionId = request.SessionId,
                QuizType = "baseline",
                Status = "active",
                TotalQuestions = questionCount,
                MetadataJson = JsonSerializer.Serialize(new { planRequestId }, JsonOptions),
                CreatedAt = now
            });
            await _db.SaveChangesAsync(ct);

            state.Status = PlanDiagnosticStatus.QuizPending;
            state.QuizQuestionCount = questionCount;
            await _stateStore.SaveAsync(state, ct);

            return new StartPlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                QuizRunId = state.QuizRunId,
                TopicId = state.TopicId,
                TopicTitle = state.TopicTitle,
                Status = state.Status,
                QuestionsJson = quizJson,
                GroundingMode = state.GroundingMode,
                SourceCount = state.SourceCount,
                QuizQuestionCount = state.QuizQuestionCount
            };
        }
        catch (Exception ex)
        {
            state.Status = PlanDiagnosticStatus.Failed;
            state.ErrorMessage = ex.Message;
            await _stateStore.SaveAsync(state, ct);
            _logger.LogWarning(ex, "[PlanDiagnostic] Start failed. PlanRequestId={PlanRequestId}", planRequestId);
            throw;
        }
    }

    public async Task<PlanDiagnosticAnswerResponse> RecordAnswerAsync(
        Guid userId,
        Guid planRequestId,
        RecordQuizAttemptRequest request,
        CancellationToken ct = default)
    {
        var state = await RequireStateAsync(userId, planRequestId, ct);
        request.QuizRunId = state.QuizRunId;
        request.TopicId = state.TopicId;
        request.SessionId ??= state.SessionId;

        await _quizRecorder.RecordAsync(userId, request, ct);

        state.AnsweredQuestionCount = await _db.QuizAttempts
            .CountAsync(a => a.UserId == userId && a.QuizRunId == state.QuizRunId, ct);

        if (state.QuizQuestionCount > 0 && state.AnsweredQuestionCount >= state.QuizQuestionCount)
        {
            state.Status = PlanDiagnosticStatus.QuizCompleted;
            state.QuizCompletedAt ??= DateTime.UtcNow;
        }

        await _stateStore.SaveAsync(state, ct);

        return new PlanDiagnosticAnswerResponse
        {
            PlanRequestId = state.PlanRequestId,
            QuizRunId = state.QuizRunId,
            Status = state.Status,
            AnsweredQuestionCount = state.AnsweredQuestionCount,
            QuizQuestionCount = state.QuizQuestionCount
        };
    }

    public async Task<FinalizePlanDiagnosticResponse> FinalizeAsync(
        Guid userId,
        FinalizePlanDiagnosticRequest request,
        CancellationToken ct = default)
    {
        var state = await RequireStateAsync(userId, request.PlanRequestId, ct);

        state.AnsweredQuestionCount = await _db.QuizAttempts
            .CountAsync(a => a.UserId == userId && a.QuizRunId == state.QuizRunId, ct);

        if (state.QuizQuestionCount <= 0 || state.AnsweredQuestionCount < state.QuizQuestionCount)
        {
            state.Status = state.AnsweredQuestionCount > 0 ? PlanDiagnosticStatus.QuizPending : state.Status;
            await _stateStore.SaveAsync(state, ct);
            return new FinalizePlanDiagnosticResponse
            {
                PlanRequestId = state.PlanRequestId,
                Status = state.Status,
                PlanGenerated = false,
                Message = "Diagnostic quiz is incomplete."
            };
        }

        state.Status = PlanDiagnosticStatus.PlanGenerating;
        await _stateStore.SaveAsync(state, ct);

        var diagnosticSummary = await BuildCurrentDiagnosticSummaryAsync(userId, state.QuizRunId, ct);
        var planResult = await _deepPlan.GenerateAndSaveDeepPlanFromDiagnosticAsync(
            state.TopicId,
            state.TopicTitle,
            userId,
            state.CompressedResearchPromptBlock,
            diagnosticSummary,
            state.UserLevel);

        state.Status = PlanDiagnosticStatus.PlanGenerated;
        state.GeneratedPlanRootTopicId = state.TopicId;
        await _stateStore.SaveAsync(state, ct);

        return new FinalizePlanDiagnosticResponse
        {
            PlanRequestId = state.PlanRequestId,
            Status = state.Status,
            PlanGenerated = true,
            GeneratedPlanRootTopicId = state.GeneratedPlanRootTopicId,
            GeneratedTopicIds = planResult.Topics.Select(t => t.Id).ToList()
        };
    }

    private async Task<string> GenerateDiagnosticQuizFromStoredContextAsync(
        string topicTitle,
        string compressedResearchPromptBlock,
        CancellationToken ct)
    {
        var systemPrompt = $$"""
            Sen profesyonel bir 'Egitim Tanilama Uzmani' botusun.
            Gorevin: '{{topicTitle}}' konusunda 20 soruluk seviye tespit quiz'i uretmek.

            [SIKISTIRILMIS QUIZ ARASTIRMA BAGLAMI]
            {{compressedResearchPromptBlock}}

            KURALLAR:
            - Ham Korteks raporu varsayma; sadece bu sikistirilmis baglami ve konu basligini kullan.
            - conceptual, procedural, application, analysis ve misconception_probe soru tiplerini karisik kullan.
            - kolay, orta ve zor dagilimini dengeli kur.
            - Soru metinleri birbirinin kopyasi veya yakin tekrari olmasin.
            - En az 8 farkli conceptTag kullan.
            - En az 4 farkli questionType kullan.
            - En az 5 soru beklenen kavram yanilgisini hedeflesin.
            - Teknik konularda en az bir soru gercek kod parcasi, kod okuma veya hata ayiklama senaryosu icersin.
            - Her soru su alanlari icersin: question, options, correctAnswer, explanation, skillTag, difficulty, conceptTag, learningObjective, questionType, expectedMisconceptionCategory.

            SADECE JSON array dondur.
            """;

        var rawQuiz = await _factory.CompleteChatAsync(
            AgentRole.DeepPlan,
            systemPrompt,
            $"Konu: \"{topicTitle}\" icin 20 adet tanilayici baseline sorusu uret.",
            ct);
        var validatedQuiz = DiagnosticQuizQualityGate.EnsureQualityOrFallback(rawQuiz, topicTitle, out var quality);
        if (!quality.IsAcceptable)
        {
            _logger.LogWarning(
                "[PlanDiagnostic] Diagnostic quiz quality failed. Topic={Topic} Failures={Failures}",
                topicTitle,
                string.Join(" | ", quality.Failures));
        }

        return validatedQuiz;
    }

    private async Task<string> BuildCurrentDiagnosticSummaryAsync(Guid userId, Guid quizRunId, CancellationToken ct)
    {
        var attempts = await _db.QuizAttempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.QuizRunId == quizRunId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

        if (attempts.Count == 0)
        {
            return "[PLAN DIAGNOSTIC QUIZ SUMMARY]\nNo answers were recorded.";
        }

        var wrong = attempts.Where(a => !a.IsCorrect).ToList();
        var conceptSummary = wrong
            .Select(ExtractWeakConcept)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key}: {g.Count()}")
            .ToList();
        var mistakeSummary = wrong
            .Select(ExtractMistakePattern)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key}: {g.Count()}")
            .ToList();

        return string.Join("\n", new[]
        {
            "[PLAN DIAGNOSTIC QUIZ SUMMARY]",
            $"QuizRunId: {quizRunId}",
            $"Answered: {attempts.Count}",
            $"Correct: {attempts.Count(a => a.IsCorrect)}",
            $"Wrong: {wrong.Count}",
            $"WeakConcepts: {(conceptSummary.Count == 0 ? "none" : string.Join(" | ", conceptSummary))}",
            $"MistakePatterns: {(mistakeSummary.Count == 0 ? "none" : string.Join(" | ", mistakeSummary))}"
        });
    }

    private static string? ExtractWeakConcept(QuizAttempt attempt)
    {
        if (!string.IsNullOrWhiteSpace(attempt.SkillTag))
        {
            return attempt.SkillTag.Trim();
        }

        if (!string.IsNullOrWhiteSpace(attempt.TopicPath))
        {
            var parts = attempt.TopicPath.Split(
                new[] { '/', '>', '|', '\\' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.LastOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(attempt.QuestionHash))
        {
            return attempt.QuestionHash.Trim();
        }

        return null;
    }

    private static string ExtractMistakePattern(QuizAttempt attempt)
    {
        var text = string.Join(" ",
            attempt.Explanation,
            attempt.CognitiveType,
            attempt.SourceRefsJson);

        foreach (var candidate in new[]
                 {
                     "Procedural",
                     "Reading",
                     "MisreadQuestion",
                     "Conceptual",
                     "Application",
                     "Careless"
                 })
        {
            if (text.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return string.IsNullOrWhiteSpace(attempt.CognitiveType)
            ? "IncorrectAnswer"
            : attempt.CognitiveType.Trim();
    }

    private async Task<PlanDiagnosticStateDto> RequireStateAsync(Guid userId, Guid planRequestId, CancellationToken ct)
    {
        var state = await _stateStore.GetAsync(planRequestId, ct);
        if (state == null || state.UserId != userId)
        {
            throw new InvalidOperationException("Plan diagnostic state was not found.");
        }

        return state;
    }

    private static int CountQuestions(string quizJson)
    {
        try
        {
            var count = DiagnosticQuizQualityGate.CountQuestions(quizJson);
            return count > 0 ? count : 20;
        }
        catch
        {
            // The model response will still be returned; default to the intended diagnostic size.
        }

        return 20;
    }
}
