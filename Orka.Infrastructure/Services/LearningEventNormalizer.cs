using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class LearningEventNormalizer : ILearningEventNormalizer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OrkaDbContext _db;
    private readonly ILearningEventSchemaService? _schema;
    private readonly ILogger<LearningEventNormalizer> _logger;

    public LearningEventNormalizer(
        OrkaDbContext db,
        ILogger<LearningEventNormalizer> logger,
        ILearningEventSchemaService? schema = null)
    {
        _db = db;
        _schema = schema;
        _logger = logger;
    }

    public async Task<LearningEventDto?> RecordQuizAttemptEventAsync(
        QuizAttempt attempt,
        CancellationToken ct = default)
    {
        var conceptKey = ExtractMetadata(attempt.SourceRefsJson, "conceptKey") ??
                         ExtractMetadata(attempt.SourceRefsJson, "conceptTag") ??
                         attempt.SkillTag;
        var assessmentItemId = attempt.AssessmentItemId ?? TryGuid(ExtractMetadata(attempt.SourceRefsJson, "assessmentItemId"));
        var outcomeKey = ExtractMetadata(attempt.SourceRefsJson, "outcomeKey") ??
                         ExtractMetadata(attempt.SourceRefsJson, "learningOutcomeIds");
        var sourceId = ExtractMetadata(attempt.SourceRefsJson, "sourceId");
        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = "orka.learning-event.v2",
            conceptKey,
            outcomeKey,
            sourceId,
            assessmentItemId,
            attempt.QuestionId,
            attempt.QuestionHash,
            attempt.Difficulty,
            attempt.CognitiveType,
            attempt.ResponseTimeMs,
            attempt.WasSkipped,
            attempt.ConfidenceSelfRating,
            masteryProbability = ExtractMetadata(attempt.SourceRefsJson, "masteryProbability"),
            knowledgeTracingStateId = ExtractMetadata(attempt.SourceRefsJson, "knowledgeTracingStateId"),
            attempt.SourceRefsJson
        }, JsonOptions);

        var entity = new LearningEvent
        {
            Id = Guid.NewGuid(),
            UserId = attempt.UserId,
            TopicId = attempt.TopicId,
            SessionId = attempt.SessionId,
            QuizAttemptId = attempt.Id,
            AssessmentItemId = assessmentItemId,
            EventType = "assessment.item.answered",
            Actor = "learner",
            Verb = "answered",
            ObjectType = "assessment-item",
            ObjectId = assessmentItemId?.ToString("D") ?? attempt.QuestionId,
            ConceptKey = conceptKey,
            SkillTag = attempt.SkillTag,
            Score = attempt.IsCorrect ? 100 : 0,
            IsPositive = attempt.IsCorrect,
            PayloadJson = payload,
            OccurredAt = attempt.CreatedAt,
            CreatedAt = DateTime.UtcNow
        };

        _db.LearningEvents.Add(entity);
        await _db.SaveChangesAsync(ct);
        if (_schema != null)
        {
            await _schema.ValidateAndLogAsync(entity, ct);
        }
        return ToDto(entity);
    }

    public async Task<LearningEventDto?> RecordSignalEventAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string signalType,
        string? skillTag = null,
        string? topicPath = null,
        int? score = null,
        bool? isPositive = null,
        string? payloadJson = null,
        Guid? quizAttemptId = null,
        CancellationToken ct = default)
    {
        try
        {
            var conceptKey = ExtractMetadata(payloadJson, "conceptKey") ??
                             ExtractMetadata(payloadJson, "conceptTag") ??
                             skillTag ??
                             topicPath;
            var assessmentItemId = TryGuid(ExtractMetadata(payloadJson, "assessmentItemId"));
            var normalizedEventType = _schema?.NormalizeEventType($"signal.{signalType}") ?? $"signal.{signalType}";
            var entity = new LearningEvent
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                SessionId = sessionId,
                QuizAttemptId = quizAttemptId,
                AssessmentItemId = assessmentItemId,
                EventType = normalizedEventType,
                Actor = "learner",
                Verb = NormalizeVerb(signalType),
                ObjectType = NormalizeObjectType(signalType),
                ObjectId = quizAttemptId?.ToString("D") ?? assessmentItemId?.ToString("D"),
                ConceptKey = conceptKey,
                SkillTag = skillTag,
                Score = score,
                IsPositive = isPositive,
                PayloadJson = NormalizePayloadJson(payloadJson, normalizedEventType, conceptKey, assessmentItemId),
                OccurredAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            _db.LearningEvents.Add(entity);
            await _db.SaveChangesAsync(ct);
            if (_schema != null)
            {
                await _schema.ValidateAndLogAsync(entity, ct);
            }
            return ToDto(entity);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[LearningEvent] Signal normalization skipped. SignalType={SignalType} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeMessage(signalType, 80),
                LogPrivacyGuard.SafeExceptionType(ex));
            return null;
        }
    }

    private static LearningEventDto ToDto(LearningEvent entity) => new()
    {
        Id = entity.Id,
        UserId = entity.UserId,
        TopicId = entity.TopicId,
        SessionId = entity.SessionId,
        QuizAttemptId = entity.QuizAttemptId,
        AssessmentItemId = entity.AssessmentItemId,
        EventType = entity.EventType,
        Actor = entity.Actor,
        Verb = entity.Verb,
        ObjectType = entity.ObjectType,
        ObjectId = entity.ObjectId,
        ConceptKey = entity.ConceptKey,
        SkillTag = entity.SkillTag,
        Score = entity.Score,
        IsPositive = entity.IsPositive,
        OccurredAt = entity.OccurredAt
    };

    private static string NormalizeVerb(string signalType)
    {
        var lower = signalType.ToLowerInvariant();
        if (lower.Contains("completed")) return "completed";
        if (lower.Contains("opened")) return "viewed";
        if (lower.Contains("asked")) return "asked";
        if (lower.Contains("uploaded")) return "uploaded";
        if (lower.Contains("error")) return "encountered";
        return "experienced";
    }

    private static string NormalizeObjectType(string signalType)
    {
        var lower = signalType.ToLowerInvariant();
        if (lower.Contains("quiz")) return "assessment";
        if (lower.Contains("wiki")) return "wiki";
        if (lower.Contains("source") || lower.Contains("notebook")) return "learning-source";
        if (lower.Contains("ide")) return "code-run";
        if (lower.Contains("review")) return "review";
        if (lower.Contains("remediation")) return "remediation";
        return "learning-activity";
    }

    private static string NormalizePayloadJson(
        string? payloadJson,
        string eventType,
        string? conceptKey,
        Guid? assessmentItemId)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["schemaVersion"] = "orka.learning-event.v2",
            ["eventType"] = eventType,
            ["conceptKey"] = conceptKey,
            ["assessmentItemId"] = assessmentItemId?.ToString("D")
        };

        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(payloadJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in doc.RootElement.EnumerateObject())
                    {
                        payload[property.Name] = property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString()
                            : property.Value.ToString();
                    }
                }
                else
                {
                    payload["rawPayload"] = payloadJson;
                }
            }
            catch
            {
                payload["rawPayload"] = payloadJson;
            }
        }

        return JsonSerializer.Serialize(
            payload.Where(kvp => kvp.Value is not null && !string.IsNullOrWhiteSpace(kvp.Value.ToString()))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            JsonOptions);
    }

    private static Guid? TryGuid(string? value) =>
        Guid.TryParse(value, out var id) ? id : null;

    private static string? ExtractMetadata(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(key, out var value))
            {
                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }
}
