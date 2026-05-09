using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class LearningEventSchemaService : ILearningEventSchemaService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> KnownEventTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "assessment.item.answered",
        "concept.mastery.updated",
        "resource.read",
        "wiki.opened",
        "ide.run",
        "ide.error",
        "review.completed",
        "tutor.policy.applied",
        "tutor.pedagogy.evaluated",
        "tutor.pedagogy.violation.detected",
        "tutor.feedback.patch.created",
        "assessment.adaptive.started",
        "assessment.item.selected",
        "assessment.calibration.updated",
        "assessment.adaptive.completed",
        "plan.node.completed",
        "learning.activity"
    };

    private readonly OrkaDbContext _db;
    private readonly ILogger<LearningEventSchemaService> _logger;

    public LearningEventSchemaService(
        OrkaDbContext db,
        ILogger<LearningEventSchemaService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public string NormalizeEventType(string rawEventType)
    {
        var lower = (rawEventType ?? string.Empty).Trim().ToLowerInvariant();
        if (lower.StartsWith("assessment.item.answered", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("quizanswered") ||
            lower.Contains("quiz_answered") ||
            lower.Contains("answered"))
        {
            return "assessment.item.answered";
        }
        if (lower.Contains("assessment.item.selected")) return "assessment.item.selected";
        if (lower.Contains("assessment.calibration")) return "assessment.calibration.updated";
        if (lower.Contains("assessment.adaptive.started")) return "assessment.adaptive.started";
        if (lower.Contains("assessment.adaptive.completed")) return "assessment.adaptive.completed";
        if (lower.Contains("concept.mastery") || lower.Contains("mastery.updated")) return "concept.mastery.updated";
        if (lower.Contains("wiki")) return "wiki.opened";
        if (lower.Contains("review")) return "review.completed";
        if (lower.Contains("ide") && lower.Contains("error")) return "ide.error";
        if (lower.Contains("piston") || lower.Contains("code") || lower.Contains("ide")) return "ide.run";
        if (lower.Contains("plan") && lower.Contains("completed")) return "plan.node.completed";
        if (lower.Contains("tutor.pedagogy") && lower.Contains("violation")) return "tutor.pedagogy.violation.detected";
        if (lower.Contains("tutor.pedagogy")) return "tutor.pedagogy.evaluated";
        if (lower.Contains("tutor.feedback")) return "tutor.feedback.patch.created";
        if (lower.Contains("tutor.policy")) return "tutor.policy.applied";
        if (lower.Contains("source") || lower.Contains("notebook") || lower.Contains("resource") || lower.Contains("read")) return "resource.read";
        return KnownEventTypes.Contains(lower) ? lower : "learning.activity";
    }

    public async Task<LearningEventSchemaValidationDto> ValidateAndLogAsync(
        LearningEvent learningEvent,
        CancellationToken ct = default)
    {
        var normalized = NormalizeEventType(learningEvent.EventType);
        var violations = new List<string>();

        if (!KnownEventTypes.Contains(normalized))
        {
            violations.Add("unknown_event_type");
        }
        if (learningEvent.UserId == Guid.Empty)
        {
            violations.Add("missing_user_id");
        }
        if (string.IsNullOrWhiteSpace(learningEvent.Actor))
        {
            violations.Add("missing_actor");
        }
        if (string.IsNullOrWhiteSpace(learningEvent.Verb))
        {
            violations.Add("missing_verb");
        }
        if (string.IsNullOrWhiteSpace(learningEvent.ObjectType))
        {
            violations.Add("missing_object_type");
        }
        if (!HasPayloadProperty(learningEvent.PayloadJson, "schemaVersion"))
        {
            violations.Add("missing_schema_version");
        }
        if (normalized == "assessment.item.answered" && learningEvent.AssessmentItemId == null)
        {
            violations.Add("missing_assessment_item_id");
        }

        if (!string.Equals(learningEvent.EventType, normalized, StringComparison.OrdinalIgnoreCase))
        {
            learningEvent.EventType = normalized;
        }

        if (violations.Count > 0)
        {
            foreach (var violation in violations)
            {
                _db.LearningEventSchemaViolations.Add(new LearningEventSchemaViolation
                {
                    Id = Guid.NewGuid(),
                    LearningEventId = learningEvent.Id,
                    UserId = learningEvent.UserId,
                    TopicId = learningEvent.TopicId,
                    EventType = normalized,
                    ViolationCode = violation,
                    ViolationDetail = $"Learning event failed schema check: {violation}",
                    PayloadJson = learningEvent.PayloadJson ?? "{}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            _logger.LogDebug(
                "[LearningEventSchema] Logged {Count} schema violations for event {EventId}",
                violations.Count,
                learningEvent.Id);
        }

        await _db.SaveChangesAsync(ct);
        return new LearningEventSchemaValidationDto
        {
            IsValid = violations.Count == 0,
            NormalizedEventType = normalized,
            Violations = violations
        };
    }

    private static bool HasPayloadProperty(string? payloadJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty(propertyName, out _);
        }
        catch
        {
            return false;
        }
    }
}
