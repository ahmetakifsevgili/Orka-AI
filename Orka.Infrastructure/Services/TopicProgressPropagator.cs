using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Events;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class TopicProgressPropagator : ITopicProgressPropagator
{
    private readonly OrkaDbContext _db;
    private readonly ITopicService _topicService;
    private readonly ITutorAgent _tutorAgent;
    private readonly IMediator _mediator;
    private readonly IAIAgentFactory _agentFactory;
    private readonly ITokenCostEstimator _tokenEstimator;
    private readonly ILogger<TopicProgressPropagator> _logger;

    public TopicProgressPropagator(
        OrkaDbContext db,
        ITopicService topicService,
        ITutorAgent tutorAgent,
        IMediator mediator,
        IAIAgentFactory agentFactory,
        ITokenCostEstimator tokenEstimator,
        ILogger<TopicProgressPropagator> logger)
    {
        _db = db;
        _topicService = topicService;
        _tutorAgent = tutorAgent;
        _mediator = mediator;
        _agentFactory = agentFactory;
        _tokenEstimator = tokenEstimator;
        _logger = logger;
    }

    public async Task PropagateLessonCompletionAsync(
        Guid userId,
        Guid lessonTopicId,
        int? scorePercent,
        bool isMastered,
        CancellationToken ct = default)
    {
        var lesson = await _db.Topics
            .FirstOrDefaultAsync(t => t.Id == lessonTopicId && t.UserId == userId, ct);
        if (lesson == null)
            return;

        lesson.ProgressPercentage = Math.Max(lesson.ProgressPercentage, 100);
        if (scorePercent.HasValue)
            lesson.SuccessScore = Math.Max(lesson.SuccessScore, Math.Clamp(scorePercent.Value, 0, 100));
        if (isMastered)
            lesson.IsMastered = true;
        lesson.LastAccessedAt = DateTime.UtcNow;

        await RecalculateAncestorsAsync(userId, lesson.ParentTopicId, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task HandleCompletionAnalysisProgressionAsync(
        Guid userId,
        Guid sessionId,
        Guid topicId,
        CancellationToken ct = default)
    {
        var topic = await _db.Topics
            .FirstOrDefaultAsync(t => t.Id == topicId && t.UserId == userId, ct);
        if (topic == null)
            return;

        var lastAiMsg = await _db.Messages
            .Where(m => m.SessionId == sessionId && m.UserId == userId && m.Role == "assistant")
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (lastAiMsg?.Content?.Contains("[TOPIC_COMPLETE:", StringComparison.OrdinalIgnoreCase) == true)
        {
            _logger.LogInformation("[Auto-Progression] Quiz path already marked completion. TopicId={TopicId}", topicId);
            return;
        }

        var orderedLessons = await _topicService.GetOrderedLessonsAsync(topicId, userId);
        if (orderedLessons.Count == 0)
        {
            await PropagateLessonCompletionAsync(userId, topic.Id, 100, true, ct);
            await _mediator.Publish(new TopicCompletedEvent
            {
                SessionId = sessionId,
                TopicId = topic.Id,
                UserId = userId
            }, ct);
            return;
        }

        var completedCount = orderedLessons.Count(t => t.ProgressPercentage >= 100 || t.IsMastered);
        var lessonToComplete = orderedLessons
            .Skip(completedCount)
            .FirstOrDefault();
        if (lessonToComplete != null)
        {
            await PropagateLessonCompletionAsync(userId, lessonToComplete.Id, 100, true, ct);
            completedCount += 1;
        }

        if (completedCount < orderedLessons.Count)
        {
            var nextTopic = orderedLessons[completedCount];
            _logger.LogInformation("[Auto-Progression] Next lesson selected. Topic={Topic}", nextTopic.Title);

            var curriculumTitles = orderedLessons.Select(t => t.Title).ToList();
            var autoLesson = await _tutorAgent.GetFirstLessonAsync(topic.Title, nextTopic.Title, curriculumTitles);
            var session = await _db.Sessions
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
            if (session != null)
            {
                var autoModel = _agentFactory.GetModel(AgentRole.Tutor);
                var (autoTokens, autoCost) = _tokenEstimator.Estimate(autoModel, string.Empty, autoLesson);

                _db.Messages.Add(new Message
                {
                    Id = Guid.NewGuid(),
                    SessionId = sessionId,
                    UserId = userId,
                    Role = "assistant",
                    Content = autoLesson,
                    ModelUsed = autoModel,
                    TokensUsed = autoTokens,
                    CostUSD = autoCost,
                    CreatedAt = DateTime.UtcNow,
                    MessageType = MessageType.General
                });

                session.TotalTokensUsed += autoTokens;
                session.TotalCostUSD += autoCost;
                await _db.SaveChangesAsync(ct);
            }

            return;
        }

        _logger.LogInformation("[Auto-Progression] All lessons completed. TopicId={TopicId}", topicId);
        await _mediator.Publish(new TopicCompletedEvent
        {
            SessionId = sessionId,
            TopicId = topicId,
            UserId = userId
        }, ct);
    }

    private async Task RecalculateAncestorsAsync(Guid userId, Guid? parentTopicId, CancellationToken ct)
    {
        var seen = new HashSet<Guid>();
        var cursor = parentTopicId;

        while (cursor.HasValue && seen.Add(cursor.Value))
        {
            var parent = await _db.Topics
                .FirstOrDefaultAsync(t => t.Id == cursor.Value && t.UserId == userId, ct);
            if (parent == null)
                return;

            var children = await _db.Topics
                .Where(t => t.ParentTopicId == parent.Id && t.UserId == userId && !t.IsArchived)
                .OrderBy(t => t.Order)
                .ThenBy(t => t.CreatedAt)
                .ToListAsync(ct);

            if (children.Count > 0)
            {
                var completed = children.Count(t => t.ProgressPercentage >= 100 || t.IsMastered);
                parent.TotalSections = children.Count;
                parent.CompletedSections = completed;
                parent.ProgressPercentage = Math.Round((double)completed / children.Count * 100, 2);
                parent.IsMastered = completed == children.Count;
                parent.LastAccessedAt = DateTime.UtcNow;
            }

            cursor = parent.ParentTopicId;
        }
    }
}
