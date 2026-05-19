using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class TutorPolicyTraceService : ITutorPolicyTraceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OrkaDbContext _db;
    private readonly IRedisMemoryService? _redis;
    private readonly ILogger<TutorPolicyTraceService> _logger;

    public TutorPolicyTraceService(
        OrkaDbContext db,
        IRedisMemoryService? redis,
        ILogger<TutorPolicyTraceService> logger)
    {
        _db = db;
        _redis = redis;
        _logger = logger;
    }

    public async Task<TutorPolicyTraceDto> CreateTraceAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string userMessage,
        TutorPolicyContextDto context,
        CancellationToken ct = default)
    {
        var violations = context.PolicyViolations.ToList();
        if (string.IsNullOrWhiteSpace(context.ActiveConceptKey))
        {
            violations.Add("no_active_concept");
        }
        if (context.SourceEvidenceCount == 0 && UserAsksForSource(userMessage))
        {
            violations.Add("source_claim_without_source_risk");
        }
        if (context.DirectAnswerRisk &&
            !context.NextPedagogicalMove.Contains("hint", StringComparison.OrdinalIgnoreCase) &&
            !context.NextPedagogicalMove.Contains("check", StringComparison.OrdinalIgnoreCase) &&
            !context.NextPedagogicalMove.Contains("repair", StringComparison.OrdinalIgnoreCase))
        {
            violations.Add("answer_before_hint_risk");
        }
        if (context.LearnerState.Contains("no concept mastery yet", StringComparison.OrdinalIgnoreCase) &&
            context.NextPedagogicalMove.Contains("weak", StringComparison.OrdinalIgnoreCase))
        {
            violations.Add("skipped_diagnostic_weakness_inference");
        }

        var remediationNeed = ExtractRemediationNeed(context.LearnerState);
        var entity = new TutorPolicyTrace
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            ConceptGraphSnapshotId = context.ConceptGraphSnapshotId,
            ActiveConceptKey = context.ActiveConceptKey,
            LearnerState = context.LearnerState,
            RemediationNeed = remediationNeed,
            GroundingStatus = context.GroundingStatus,
            SelectedPedagogicalMove = context.NextPedagogicalMove,
            SourceEvidenceCount = context.SourceEvidenceCount,
            DirectAnswerRisk = context.DirectAnswerRisk,
            PolicyViolationsJson = JsonSerializer.Serialize(violations.Distinct(StringComparer.OrdinalIgnoreCase), JsonOptions),
            InputHash = ComputeHash(userMessage),
            CreatedAt = DateTime.UtcNow
        };

        _db.TutorPolicyTraces.Add(entity);
        _db.LearningEvents.Add(new LearningEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            EventType = "tutor.policy.applied",
            Actor = "tutor",
            Verb = "applied",
            ObjectType = "tutor-policy",
            ObjectId = entity.Id.ToString("D"),
            ConceptKey = context.ActiveConceptKey,
            PayloadJson = JsonSerializer.Serialize(new
            {
                schemaVersion = "orka.learning-event.v2",
                tutorPolicyTraceId = entity.Id,
                context.ActiveConceptKey,
                context.NextPedagogicalMove,
                context.GroundingStatus,
                context.SourceEvidenceCount,
                violations
            }, JsonOptions),
            OccurredAt = entity.CreatedAt,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        var dto = ToDto(entity);
        if (_redis != null && sessionId.HasValue)
        {
            try
            {
                await _redis.SetJsonAsync($"orka:v2:tutor-policy:{sessionId.Value:N}", JsonSerializer.Serialize(dto, JsonOptions), TimeSpan.FromHours(2));
            }
            catch (Exception ex)
            {
                _logger.LogDebug("[TutorPolicyTrace] Redis write skipped. SessionRef={SessionRef} ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeId(sessionId, "session"),
                    LogPrivacyGuard.SafeExceptionType(ex));
            }
        }

        return dto;
    }

    public async Task<IReadOnlyList<TutorPolicyTraceDto>> GetRecentAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId = null,
        int take = 10,
        CancellationToken ct = default)
    {
        var query = _db.TutorPolicyTraces.AsNoTracking().Where(t => t.UserId == userId);
        if (topicId.HasValue) query = query.Where(t => t.TopicId == topicId.Value);
        if (sessionId.HasValue) query = query.Where(t => t.SessionId == sessionId.Value);
        var rows = await query.OrderByDescending(t => t.CreatedAt).Take(take).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    private static TutorPolicyTraceDto ToDto(TutorPolicyTrace entity) => new()
    {
        Id = entity.Id,
        UserId = entity.UserId,
        TopicId = entity.TopicId,
        SessionId = entity.SessionId,
        ConceptGraphSnapshotId = entity.ConceptGraphSnapshotId,
        ActiveConceptKey = entity.ActiveConceptKey,
        LearnerState = entity.LearnerState,
        RemediationNeed = entity.RemediationNeed,
        GroundingStatus = entity.GroundingStatus,
        SelectedPedagogicalMove = entity.SelectedPedagogicalMove,
        SourceEvidenceCount = entity.SourceEvidenceCount,
        DirectAnswerRisk = entity.DirectAnswerRisk,
        PolicyViolations = DeserializeList(entity.PolicyViolationsJson),
        CreatedAt = entity.CreatedAt
    };

    private static string ExtractRemediationNeed(string learnerState)
    {
        var parts = learnerState.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.FirstOrDefault(p =>
            string.Equals(p, "high", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p, "medium", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p, "none", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p, "evidence_insufficient", StringComparison.OrdinalIgnoreCase)) ?? "unknown";
    }

    private static bool UserAsksForSource(string message) =>
        message.Contains("kaynak", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("source", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("citation", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("nereden", StringComparison.OrdinalIgnoreCase);

    private static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes)[..24].ToLowerInvariant();
    }

    private static List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
