using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class CaseLikeOutcomeMapper
{
    public object Map(LearningOutcome outcome, IReadOnlyDictionary<string, IReadOnlyList<string>> prerequisitesByConcept)
    {
        var conceptKey = FindConceptKey(outcome);
        prerequisitesByConcept.TryGetValue(conceptKey, out var prerequisites);
        return new
        {
            learningOutcomeId = outcome.Id,
            stableKey = outcome.StableKey,
            label = outcome.Label,
            description = outcome.Description,
            standardUri = outcome.StandardUri,
            conceptKey,
            cognitiveLevel = outcome.CognitiveLevel,
            prerequisites = prerequisites ?? Array.Empty<string>()
        };
    }

    private static string FindConceptKey(LearningOutcome outcome)
    {
        if (!string.IsNullOrWhiteSpace(outcome.MetadataJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(outcome.MetadataJson);
                if (doc.RootElement.TryGetProperty("conceptKey", out var conceptKey) && conceptKey.ValueKind == JsonValueKind.String)
                    return conceptKey.GetString() ?? string.Empty;
            }
            catch
            {
                // Metadata is best-effort.
            }
        }

        return string.Empty;
    }
}

public sealed class QtiLikeAssessmentMapper
{
    public object Map(AssessmentItem item, AssessmentItemStat? stat) => new
    {
        assessmentItemId = item.Id,
        itemKey = item.AssessmentItemKey,
        conceptKey = item.ConceptKey,
        learningOutcomeKeys = SafeArray(item.LearningOutcomeKeysJson),
        cognitiveSkill = item.CognitiveSkill,
        difficulty = item.Difficulty,
        misconceptionTarget = item.MisconceptionTarget,
        evidenceExpected = item.EvidenceExpected,
        scoringRule = item.ScoringRuleJson,
        questionType = item.QuestionType,
        stats = stat == null
            ? null
            : new
            {
                stat.Attempts,
                stat.CorrectRate,
                stat.SkipRate,
                stat.AverageTimeSeconds,
                stat.DifficultyEstimate,
                stat.DiscriminationEstimate,
                stat.CalibrationStatus
            }
    };

    private static IReadOnlyList<string> SafeArray(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}

public sealed class CaliperXapiEventMapper
{
    public object Map(LearningEvent evt) => new
    {
        eventId = evt.Id,
        actor = evt.Actor,
        verb = evt.Verb,
        @object = new
        {
            type = evt.ObjectType,
            id = evt.ObjectId
        },
        eventType = evt.EventType,
        conceptKey = evt.ConceptKey,
        assessmentItemId = evt.AssessmentItemId,
        schemaVersion = ExtractSchemaVersion(evt.PayloadJson),
        occurredAt = evt.OccurredAt
    };

    private static string ExtractSchemaVersion(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.TryGetProperty("schemaVersion", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}

public sealed class StandardsAlignmentService : IStandardsAlignmentService
{
    private readonly OrkaDbContext _db;

    public StandardsAlignmentService(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<StandardsSummaryDto> GetSummaryAsync(Guid userId, Guid? topicId, CancellationToken ct = default)
    {
        var snapshot = await LatestSnapshotAsync(userId, topicId, ct);
        var snapshotId = snapshot?.Id;
        var outcomes = await _db.LearningOutcomes
            .AsNoTracking()
            .Where(o => !snapshotId.HasValue || o.ConceptGraphSnapshotId == snapshotId.Value)
            .ToListAsync(ct);
        var concepts = await _db.LearningConcepts
            .AsNoTracking()
            .Where(c => snapshotId.HasValue && c.ConceptGraphSnapshotId == snapshotId.Value)
            .ToListAsync(ct);
        var items = await _db.AssessmentItems
            .AsNoTracking()
            .Where(i => i.UserId == userId && (!topicId.HasValue || i.TopicId == topicId.Value))
            .ToListAsync(ct);
        var events = await _db.LearningEvents
            .AsNoTracking()
            .Where(e => e.UserId == userId && (!topicId.HasValue || e.TopicId == topicId.Value))
            .OrderByDescending(e => e.OccurredAt)
            .Take(300)
            .ToListAsync(ct);
        var latestRun = await _db.StandardsValidationRuns
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.TopicId == topicId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
        var recentIssues = latestRun == null
            ? new List<StandardsValidationItemDto>()
            : await _db.StandardsValidationItems
                .AsNoTracking()
                .Where(i => i.StandardsValidationRunId == latestRun.Id)
                .OrderByDescending(i => i.CreatedAt)
                .Take(12)
                .Select(i => new StandardsValidationItemDto
                {
                    Id = i.Id,
                    StandardFamily = i.StandardFamily,
                    EntityType = i.EntityType,
                    EntityKey = i.EntityKey,
                    Severity = i.Severity,
                    IssueCode = i.IssueCode,
                    UserSafeMessage = i.UserSafeMessage
                })
                .ToListAsync(ct);

        var caseCoverage = Coverage(concepts.Count + outcomes.Count, concepts.Count(c => HasText(c.StableKey) && HasText(c.Label)) + outcomes.Count(o => HasText(o.StableKey) && HasText(o.Label)));
        var qtiCoverage = Coverage(items.Count, items.Count(IsQtiReady));
        var caliperCoverage = Coverage(events.Count, events.Count(IsEventReady));
        return new StandardsSummaryDto
        {
            UserId = userId,
            TopicId = topicId,
            ConceptGraphSnapshotId = snapshotId,
            StandardsAlignmentStatus = Status(caseCoverage, qtiCoverage, caliperCoverage, latestRun?.IssueCount ?? 0),
            CaseCoverage = caseCoverage,
            QtiCoverage = qtiCoverage,
            CaliperXapiCoverage = caliperCoverage,
            OutcomeCount = outcomes.Count,
            ConceptCount = concepts.Count,
            AssessmentItemCount = items.Count,
            LearningEventCount = events.Count,
            IssueCount = latestRun?.IssueCount ?? 0,
            RecentIssues = recentIssues
        };
    }

    internal static bool IsQtiReady(AssessmentItem item) =>
        HasText(item.ConceptKey) &&
        HasText(item.CognitiveSkill) &&
        HasText(item.Difficulty) &&
        HasText(item.ScoringRuleJson) &&
        item.ScoringRuleJson != "{}" &&
        HasText(item.LearningOutcomeKeysJson) &&
        item.LearningOutcomeKeysJson != "[]";

    internal static bool IsEventReady(LearningEvent evt) =>
        HasText(evt.Actor) &&
        HasText(evt.Verb) &&
        HasText(evt.ObjectType) &&
        HasText(evt.EventType) &&
        HasSchemaVersion(evt.PayloadJson);

    internal static decimal Coverage(int total, int ready) => total <= 0 ? 0m : Math.Round((decimal)ready / total, 4);

    internal static string Status(decimal caseCoverage, decimal qtiCoverage, decimal eventCoverage, int issueCount)
    {
        if (issueCount > 20 || (issueCount > 0 && (caseCoverage < 0.60m || qtiCoverage < 0.60m || eventCoverage < 0.60m))) return "degraded";
        if (caseCoverage == 0m && qtiCoverage == 0m && eventCoverage == 0m) return "unknown";
        if (caseCoverage < 0.60m || qtiCoverage < 0.60m || eventCoverage < 0.60m) return "degraded";
        if (issueCount > 0 || caseCoverage < 0.80m || qtiCoverage < 0.80m || eventCoverage < 0.80m) return "watch";
        return "healthy";
    }

    internal async Task<ConceptGraphSnapshot?> LatestSnapshotAsync(Guid userId, Guid? topicId, CancellationToken ct) =>
        await _db.ConceptGraphSnapshots
            .AsNoTracking()
            .Where(s => s.UserId == userId && (!topicId.HasValue || s.TopicId == topicId.Value))
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool HasSchemaVersion(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.TryGetProperty("schemaVersion", out _);
        }
        catch
        {
            return false;
        }
    }
}

public sealed class StandardsValidationService : IStandardsValidationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OrkaDbContext _db;
    private readonly StandardsAlignmentService _alignment;

    public StandardsValidationService(OrkaDbContext db, IStandardsAlignmentService alignment)
    {
        _db = db;
        _alignment = (StandardsAlignmentService)alignment;
    }

    public async Task<StandardsValidationRunDto> ValidateAsync(Guid userId, Guid? topicId, CancellationToken ct = default)
    {
        var summary = await _alignment.GetSummaryAsync(userId, topicId, ct);
        var snapshotId = summary.ConceptGraphSnapshotId;
        var issues = new List<StandardsValidationItem>();
        var concepts = await _db.LearningConcepts.AsNoTracking().Where(c => snapshotId.HasValue && c.ConceptGraphSnapshotId == snapshotId.Value).ToListAsync(ct);
        var outcomes = await _db.LearningOutcomes.AsNoTracking().Where(o => !snapshotId.HasValue || o.ConceptGraphSnapshotId == snapshotId.Value).ToListAsync(ct);
        var items = await _db.AssessmentItems.AsNoTracking().Where(i => i.UserId == userId && (!topicId.HasValue || i.TopicId == topicId.Value)).Take(300).ToListAsync(ct);
        var events = await _db.LearningEvents.AsNoTracking().Where(e => e.UserId == userId && (!topicId.HasValue || e.TopicId == topicId.Value)).OrderByDescending(e => e.OccurredAt).Take(300).ToListAsync(ct);

        foreach (var concept in concepts.Where(c => string.IsNullOrWhiteSpace(c.StableKey) || string.IsNullOrWhiteSpace(c.Label)))
            issues.Add(Issue(userId, topicId, "case", "concept", concept.Id, concept.StableKey, "missing_concept_identity", "Kavram stable key veya etiket taşımıyor."));
        foreach (var outcome in outcomes.Where(o => string.IsNullOrWhiteSpace(o.StableKey) || string.IsNullOrWhiteSpace(o.Label)))
            issues.Add(Issue(userId, topicId, "case", "learning_outcome", outcome.Id, outcome.StableKey, "missing_outcome_identity", "Kazanım stable key veya etiket taşımıyor."));
        foreach (var item in items.Where(i => !StandardsAlignmentService.IsQtiReady(i)))
            issues.Add(Issue(userId, topicId, "qti", "assessment_item", item.Id, item.AssessmentItemKey, "missing_assessment_metadata", "Soru ölçüm metadata'sı, outcome bağı veya scoring rule açısından eksik."));
        foreach (var evt in events.Where(e => !StandardsAlignmentService.IsEventReady(e)))
            issues.Add(Issue(userId, topicId, "caliper_xapi", "learning_event", evt.Id, evt.EventType, "missing_event_profile", "Öğrenme olayı actor/verb/object/schemaVersion profilini tamamlamıyor."));

        var run = new StandardsValidationRun
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ConceptGraphSnapshotId = snapshotId,
            CaseCoverage = summary.CaseCoverage,
            QtiCoverage = summary.QtiCoverage,
            CaliperXapiCoverage = summary.CaliperXapiCoverage,
            CheckedItemCount = concepts.Count + outcomes.Count + items.Count + events.Count,
            IssueCount = issues.Count,
            Status = StandardsAlignmentService.Status(summary.CaseCoverage, summary.QtiCoverage, summary.CaliperXapiCoverage, issues.Count),
            CreatedAt = DateTime.UtcNow
        };
        run.SummaryJson = JsonSerializer.Serialize(new { run.Status, run.CheckedItemCount, run.IssueCount, summary.CaseCoverage, summary.QtiCoverage, summary.CaliperXapiCoverage }, JsonOptions);
        foreach (var issue in issues)
            issue.StandardsValidationRunId = run.Id;

        _db.StandardsValidationRuns.Add(run);
        _db.StandardsValidationItems.AddRange(issues);
        await _db.SaveChangesAsync(ct);
        return ToDto(run, issues);
    }

    private static StandardsValidationItem Issue(Guid userId, Guid? topicId, string family, string entityType, Guid? entityId, string entityKey, string code, string message) => new()
    {
        UserId = userId,
        TopicId = topicId,
        StandardFamily = family,
        EntityType = entityType,
        EntityId = entityId,
        EntityKey = entityKey ?? string.Empty,
        Severity = family == "caliper_xapi" ? "warning" : "error",
        IssueCode = code,
        UserSafeMessage = message,
        DetailJson = JsonSerializer.Serialize(new { code, entityType, entityId }, JsonOptions),
        CreatedAt = DateTime.UtcNow
    };

    private static StandardsValidationRunDto ToDto(StandardsValidationRun run, IReadOnlyList<StandardsValidationItem> issues) => new()
    {
        Id = run.Id,
        UserId = run.UserId,
        TopicId = run.TopicId,
        ConceptGraphSnapshotId = run.ConceptGraphSnapshotId,
        Status = run.Status,
        CaseCoverage = run.CaseCoverage,
        QtiCoverage = run.QtiCoverage,
        CaliperXapiCoverage = run.CaliperXapiCoverage,
        CheckedItemCount = run.CheckedItemCount,
        IssueCount = run.IssueCount,
        Issues = issues.Select(i => new StandardsValidationItemDto
        {
            Id = i.Id,
            StandardFamily = i.StandardFamily,
            EntityType = i.EntityType,
            EntityKey = i.EntityKey,
            Severity = i.Severity,
            IssueCode = i.IssueCode,
            UserSafeMessage = i.UserSafeMessage
        }).ToArray(),
        CreatedAt = run.CreatedAt
    };
}

public sealed class StandardsExportService : IStandardsExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OrkaDbContext _db;
    private readonly IStandardsAlignmentService _alignment;
    private readonly CaseLikeOutcomeMapper _caseMapper = new();
    private readonly QtiLikeAssessmentMapper _qtiMapper = new();
    private readonly CaliperXapiEventMapper _eventMapper = new();

    public StandardsExportService(OrkaDbContext db, IStandardsAlignmentService alignment)
    {
        _db = db;
        _alignment = alignment;
    }

    public async Task<StandardsExportRunDto> ExportAsync(Guid userId, Guid? topicId, string exportType = "combined", CancellationToken ct = default)
    {
        var summary = await _alignment.GetSummaryAsync(userId, topicId, ct);
        var snapshotId = summary.ConceptGraphSnapshotId;
        var outcomes = await _db.LearningOutcomes.AsNoTracking().Where(o => !snapshotId.HasValue || o.ConceptGraphSnapshotId == snapshotId.Value).ToListAsync(ct);
        var relations = await _db.ConceptRelations.AsNoTracking().Where(r => snapshotId.HasValue && r.ConceptGraphSnapshotId == snapshotId.Value && r.RelationType == "prerequisite").ToListAsync(ct);
        var prerequisites = relations
            .GroupBy(r => r.TargetConceptKey)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.SourceConceptKey).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), StringComparer.OrdinalIgnoreCase);
        var items = await _db.AssessmentItems.AsNoTracking().Where(i => i.UserId == userId && (!topicId.HasValue || i.TopicId == topicId.Value)).Take(300).ToListAsync(ct);
        var stats = await _db.AssessmentItemStats.AsNoTracking().Where(s => items.Select(i => i.Id).Contains(s.AssessmentItemId)).ToDictionaryAsync(s => s.AssessmentItemId, ct);
        var events = await _db.LearningEvents.AsNoTracking().Where(e => e.UserId == userId && (!topicId.HasValue || e.TopicId == topicId.Value)).OrderByDescending(e => e.OccurredAt).Take(300).ToListAsync(ct);

        var caseItems = outcomes.Select(o => _caseMapper.Map(o, prerequisites)).ToArray();
        var qtiItems = items.Select(i => _qtiMapper.Map(i, stats.GetValueOrDefault(i.Id))).ToArray();
        var eventItems = events.Select(_eventMapper.Map).ToArray();
        var payload = new { schemaVersion = "orka.standards.v1", exportType, caseLike = caseItems, qtiLike = qtiItems, caliperXapiLike = eventItems };
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var run = new StandardsExportRun
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ConceptGraphSnapshotId = snapshotId,
            ExportType = exportType,
            Status = "ready",
            ItemCount = caseItems.Length + qtiItems.Length + eventItems.Length,
            CaseCoverage = summary.CaseCoverage,
            QtiCoverage = summary.QtiCoverage,
            CaliperXapiCoverage = summary.CaliperXapiCoverage,
            PayloadJson = payloadJson,
            CreatedAt = DateTime.UtcNow
        };
        _db.StandardsExportRuns.Add(run);
        _db.StandardsExportItems.AddRange(BuildItems(run, caseItems, qtiItems, eventItems));
        await _db.SaveChangesAsync(ct);
        return new StandardsExportRunDto
        {
            Id = run.Id,
            UserId = userId,
            TopicId = topicId,
            ConceptGraphSnapshotId = snapshotId,
            ExportType = exportType,
            Status = run.Status,
            ItemCount = run.ItemCount,
            CaseCoverage = run.CaseCoverage,
            QtiCoverage = run.QtiCoverage,
            CaliperXapiCoverage = run.CaliperXapiCoverage,
            PayloadJson = payloadJson,
            CreatedAt = run.CreatedAt
        };
    }

    private static IEnumerable<StandardsExportItem> BuildItems(StandardsExportRun run, IEnumerable<object> caseItems, IEnumerable<object> qtiItems, IEnumerable<object> eventItems)
    {
        foreach (var item in caseItems) yield return ExportItem(run, "case", "learning_outcome", item);
        foreach (var item in qtiItems) yield return ExportItem(run, "qti", "assessment_item", item);
        foreach (var item in eventItems) yield return ExportItem(run, "caliper_xapi", "learning_event", item);
    }

    private static StandardsExportItem ExportItem(StandardsExportRun run, string family, string entityType, object item) => new()
    {
        StandardsExportRunId = run.Id,
        UserId = run.UserId,
        TopicId = run.TopicId,
        StandardFamily = family,
        EntityType = entityType,
        EntityKey = ExtractKey(item),
        PayloadJson = JsonSerializer.Serialize(item, JsonOptions),
        CreatedAt = DateTime.UtcNow
    };

    private static string ExtractKey(object item)
    {
        var json = JsonSerializer.SerializeToElement(item, JsonOptions);
        foreach (var key in new[] { "stableKey", "itemKey", "eventType" })
        {
            if (json.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}

public sealed class ProviderGovernanceService : IProviderGovernanceService
{
    private readonly OrkaDbContext _db;
    private readonly IAiProviderTelemetryService _aiTelemetry;

    public ProviderGovernanceService(OrkaDbContext db, IAiProviderTelemetryService aiTelemetry)
    {
        _db = db;
        _aiTelemetry = aiTelemetry;
    }

    public async Task<ProviderGovernanceSummaryDto> GetSummaryAsync(Guid? userId = null, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddHours(-24);
        var toolEvents = await _db.ToolTelemetryEvents
            .AsNoTracking()
            .Where(e => e.OccurredAt >= since && (!userId.HasValue || e.UserId == userId.Value))
            .ToListAsync(ct);
        var costs = await _db.CostRecords
            .AsNoTracking()
            .Where(c => c.OccurredAt >= DateTime.UtcNow.Date && (!userId.HasValue || c.UserId == userId.Value))
            .ToListAsync(ct);
        var aiTelemetry = await _aiTelemetry.GetSummaryAsync(ct);
        var items = toolEvents
            .GroupBy(e => string.IsNullOrWhiteSpace(e.Provider) ? e.ToolId : e.Provider!)
            .Select(g =>
            {
                var failures = g.Count(e => !e.Success);
                var calls = g.Count();
                var status = calls == 0 ? "unknown" : failures == 0 ? "healthy" : failures * 2 < calls ? "watch" : "degraded";
                return new ProviderGovernanceItemDto
                {
                    Provider = g.Key,
                    Status = status,
                    Calls24h = calls,
                    Failures24h = failures,
                    AverageLatencyMs = calls == 0 ? 0 : (long)g.Average(e => e.LatencyMs),
                    EstimatedCostUsdToday = costs.Where(c => string.Equals(c.Provider, g.Key, StringComparison.OrdinalIgnoreCase)).Sum(c => c.EstimatedCostUsd),
                    FallbackCount24h = aiTelemetry.CircuitStates.Where(kv => kv.Key.StartsWith(g.Key + ":", StringComparison.OrdinalIgnoreCase)).Sum(kv => kv.Value),
                    QuotaHitCount24h = 0,
                    CircuitState = aiTelemetry.CircuitStates.Keys.Any(k => k.StartsWith(g.Key + ":open", StringComparison.OrdinalIgnoreCase)) ? "open" : "closed",
                    UserSafeMessage = status switch
                    {
                        "healthy" => "Sağlayıcı sağlıklı çalışıyor.",
                        "watch" => "Sağlayıcıda ara sıra hata görülüyor.",
                        "degraded" => "Sağlayıcı güvenilir değil; güvenli fallback gerekir.",
                        _ => "Sağlayıcı için yeterli telemetri yok."
                    }
                };
            })
            .OrderBy(i => i.Provider)
            .ToArray();
        var recentFailures = toolEvents.Count(e => !e.Success);
        var status = items.Length == 0 ? "unknown" : items.Any(i => i.Status == "degraded") ? "degraded" : items.Any(i => i.Status == "watch") ? "watch" : "healthy";
        return new ProviderGovernanceSummaryDto
        {
            Status = status,
            ProviderCount = items.Length,
            HealthyProviderCount = items.Count(i => i.Status == "healthy"),
            RecentFailureCount = recentFailures,
            EstimatedCostUsdToday = costs.Sum(c => c.EstimatedCostUsd),
            FallbackCount24h = aiTelemetry.FallbackCount24h,
            QuotaHitCount24h = aiTelemetry.QuotaHitCount24h,
            FailureKinds24h = aiTelemetry.FailureKinds24h,
            Providers = items
        };
    }
}

public sealed class RetentionCleanupService : IRetentionCleanupService
{
    private readonly OrkaDbContext _db;
    private readonly IConfiguration _configuration;

    public RetentionCleanupService(OrkaDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<AudioRetentionSummaryDto> GetAudioRetentionSummaryAsync(CancellationToken ct = default)
    {
        var retentionDays = RetentionDays();
        var now = DateTime.UtcNow;
        var overview = await BuildAudioRetentionAggregateAsync(
            _db.AudioOverviewJobs.AsNoTracking(),
            retentionDays,
            now,
            ct);
        var classroom = await BuildAudioRetentionAggregateAsync(
            _db.ClassroomInteractions.AsNoTracking(),
            retentionDays,
            now,
            ct);
        var readyCount = overview.ReadyCount + classroom.ReadyCount;
        var expired = overview.ExpiredCount + classroom.ExpiredCount;
        var purged = overview.PurgedCount + classroom.PurgedCount;
        var bytes = overview.StoredBytes + classroom.StoredBytes;
        return new AudioRetentionSummaryDto
        {
            Status = expired > 0 ? "watch" : "healthy",
            ReadyAudioCount = readyCount,
            ExpiredAudioCount = expired,
            PurgedAudioCount = purged,
            StoredAudioBytes = bytes,
            RetentionDays = retentionDays
        };
    }

    public async Task<AudioRetentionSummaryDto> PurgeExpiredAudioAsync(CancellationToken ct = default)
    {
        var retentionDays = RetentionDays();
        var now = DateTime.UtcNow;
        var overview = await _db.AudioOverviewJobs
            .Where(j => j.AudioBytes != null && (j.AudioExpiresAt ?? j.CreatedAt.AddDays(retentionDays)) <= now)
            .Take(100)
            .ToListAsync(ct);
        foreach (var job in overview)
        {
            job.AudioByteLength = job.AudioBytes?.LongLength ?? job.AudioByteLength;
            job.AudioBytes = null;
            job.AudioPurgedAt = now;
            job.UpdatedAt = now;
        }

        var classroom = await _db.ClassroomInteractions
            .Where(i => i.AudioBytes != null && (i.AudioExpiresAt ?? i.CreatedAt.AddDays(retentionDays)) <= now)
            .Take(100)
            .ToListAsync(ct);
        foreach (var item in classroom)
        {
            item.AudioByteLength = item.AudioBytes?.LongLength ?? item.AudioByteLength;
            item.AudioBytes = null;
            item.AudioPurgedAt = now;
        }

        await _db.SaveChangesAsync(ct);
        return await GetAudioRetentionSummaryAsync(ct);
    }

    private int RetentionDays() => Math.Clamp(_configuration.GetValue("Retention:AudioBytesDays", 7), 1, 365);

    private static async Task<AudioRetentionAggregate> BuildAudioRetentionAggregateAsync(
        IQueryable<AudioOverviewJob> query,
        int retentionDays,
        DateTime now,
        CancellationToken ct)
    {
        var aggregate = await query
            .GroupBy(_ => 1)
            .Select(g => new AudioRetentionAggregate(
                g.Count(j => j.AudioBytes != null && j.AudioByteLength > 0),
                g.Count(j => j.AudioBytes != null && (j.AudioExpiresAt ?? j.CreatedAt.AddDays(retentionDays)) <= now),
                g.Count(j => j.AudioPurgedAt != null),
                g.Sum(j => j.AudioBytes != null ? j.AudioByteLength : 0)))
            .FirstOrDefaultAsync(ct);

        return aggregate ?? AudioRetentionAggregate.Empty;
    }

    private static async Task<AudioRetentionAggregate> BuildAudioRetentionAggregateAsync(
        IQueryable<ClassroomInteraction> query,
        int retentionDays,
        DateTime now,
        CancellationToken ct)
    {
        var aggregate = await query
            .GroupBy(_ => 1)
            .Select(g => new AudioRetentionAggregate(
                g.Count(i => i.AudioBytes != null && i.AudioByteLength > 0),
                g.Count(i => i.AudioBytes != null && (i.AudioExpiresAt ?? i.CreatedAt.AddDays(retentionDays)) <= now),
                g.Count(i => i.AudioPurgedAt != null),
                g.Sum(i => i.AudioBytes != null ? i.AudioByteLength : 0)))
            .FirstOrDefaultAsync(ct);

        return aggregate ?? AudioRetentionAggregate.Empty;
    }

    private sealed record AudioRetentionAggregate(
        int ReadyCount,
        int ExpiredCount,
        int PurgedCount,
        long StoredBytes)
    {
        public static readonly AudioRetentionAggregate Empty = new(0, 0, 0, 0);
    }
}

public sealed class RedisStreamMaintenanceService : IRedisStreamMaintenanceService
{
    private readonly IRedisMemoryService _redis;
    private readonly IConfiguration _configuration;

    public RedisStreamMaintenanceService(IRedisMemoryService redis, IConfiguration configuration)
    {
        _redis = redis;
        _configuration = configuration;
    }

    public async Task<RedisStreamMaintenanceSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        var keys = await _redis.ScanKeysAsync("orka:v3:tutor-events:*", 100);
        return new RedisStreamMaintenanceSummaryDto
        {
            Status = keys.Count == 0 ? "unknown" : "healthy",
            StreamCount = keys.Count,
            MaxLength = MaxLength(),
            ApproximateTotalLength = 0,
            TrimmedStreamCount = 0,
            Notes = keys.Count == 0 ? "Canlı tutor stream kaydı bulunamadı." : "Tutor event stream anahtarları izleniyor."
        };
    }

    public async Task<RedisStreamMaintenanceSummaryDto> TrimTutorEventStreamsAsync(CancellationToken ct = default)
    {
        var keys = await _redis.ScanKeysAsync("orka:v3:tutor-events:*", 100);
        var trimmed = 0;
        foreach (var key in keys)
        {
            var removed = await _redis.TrimStreamAsync(key, MaxLength(), approximate: true);
            if (removed > 0) trimmed++;
        }

        return new RedisStreamMaintenanceSummaryDto
        {
            Status = keys.Count == 0 ? "unknown" : "healthy",
            StreamCount = keys.Count,
            MaxLength = MaxLength(),
            TrimmedStreamCount = trimmed,
            Notes = trimmed == 0 ? "Trim gerektiren stream bulunmadı." : $"{trimmed} stream kısaltıldı."
        };
    }

    private long MaxLength() => Math.Clamp(_configuration.GetValue("Redis:Streams:TutorEventsMaxLength", 1000), 100, 100000);
}

public sealed class DbIndexAuditService : IDbIndexAuditService
{
    private readonly OrkaDbContext _db;

    public DbIndexAuditService(OrkaDbContext db)
    {
        _db = db;
    }

    public Task<DbIndexAuditSummaryDto> AuditAsync(CancellationToken ct = default)
    {
        (string EntityName, string[] Columns)[] required =
        [
            ("LearningEvent", ["UserId", "TopicId", "EventType", "OccurredAt"]),
            ("AssessmentItem", ["UserId", "TopicId", "ConceptKey"]),
            ("LearningSignal", ["UserId", "TopicId", "SignalType", "CreatedAt"]),
            ("QuizRun", ["UserId", "TopicId", "CreatedAt"]),
            ("QuizAttempt", ["UserId", "TopicId", "QuestionHash"]),
            ("QuizAttempt", ["UserId", "AssessmentItemId"]),
            ("ReviewItem", ["UserId", "Status", "DueAt"]),
            ("ConceptMastery", ["UserId", "TopicId", "ConceptKey"]),
            ("KnowledgeTracingState", ["UserId", "TopicId", "ConceptKey"]),
            ("ActiveLessonSnapshot", ["UserId", "TopicId", "SessionId", "CreatedAt"]),
            ("TutorTraceProjection", ["UserId", "SessionId", "OccurredAt"]),
            ("SourceRetrievalRun", ["UserId", "TopicId", "CreatedAt"]),
            ("LearningSource", ["UserId", "TopicId", "IsDeleted"]),
            ("SourceChunk", ["LearningSourceId", "IsDeleted", "PageNumber", "ChunkIndex"]),
            ("WikiPage", ["UserId", "TopicId", "PageKey", "IsDeleted"]),
            ("LearningNotebookPack", ["UserId", "TopicId", "SessionId", "UpdatedAt"]),
            ("LearningArtifact", ["UserId", "TopicId", "SessionId", "CreatedAt"]),
            ("QuestionItem", ["OwnerUserId", "ExamDefinitionId", "QualityStatus", "IsDeleted"]),
            ("QuestionItem", ["QuestionType", "Difficulty", "QualityStatus", "IsDeleted"]),
            ("CentralExamPracticeAttempt", ["UserId", "Status", "StartedAt", "IsDeleted"]),
            ("CentralExamDenemeAttempt", ["UserId", "Status", "StartedAt", "IsDeleted"]),
            ("CostRecord", ["UserId", "OccurredAt"]),
            ("ToolTelemetryEvent", ["ToolId", "OccurredAt"]),
            ("ToolTelemetryEvent", ["UserId", "OccurredAt"])
        ];
        var missing = new List<string>();
        foreach (var (entityName, columns) in required)
        {
            var entity = _db.Model.GetEntityTypes().FirstOrDefault(e => e.ClrType.Name == entityName);
            var hasIndex = entity?.GetIndexes().Any(i => columns.All(c => i.Properties.Any(p => p.Name == c))) == true;
            if (!hasIndex) missing.Add($"{entityName}({string.Join(",", columns)})");
        }

        return Task.FromResult(new DbIndexAuditSummaryDto
        {
            Status = missing.Count == 0 ? "healthy" : "watch",
            RequiredIndexCount = required.Length,
            MissingIndexCount = missing.Count,
            MissingIndexes = missing
        });
    }
}

public sealed class V1RegressionGateService : IV1RegressionGateService
{
    private readonly OrkaDbContext _db;

    public V1RegressionGateService(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<V1RegressionGateDto> EvaluateAsync(CancellationToken ct = default)
    {
        var scenarios = new[]
        {
            await Scenario("seljuk", "Selçuklu generic öğrenme hattı", _db.ConceptGraphSnapshots.AnyAsync(s => s.ApprovedResearchIntent.ToLower().Contains("selçuk") || s.ApprovedResearchIntent.ToLower().Contains("selcuk"), ct)),
            await Scenario("sql_optimization", "SQL optimization generic assessment hattı", _db.AssessmentItems.AnyAsync(i => i.ConceptKey.ToLower().Contains("sql") || i.ConceptLabel.ToLower().Contains("sql"), ct)),
            await Scenario("java_algorithms", "Java algorithms generic plan/quiz hattı", _db.AssessmentItems.AnyAsync(i => i.ConceptLabel.ToLower().Contains("java") || i.ConceptKey.ToLower().Contains("algorithm"), ct)),
            await Scenario("source_grounded_pdf", "PDF/source grounded Q&A", _db.SourceQualityReports.AnyAsync(ct)),
            await Scenario("audio_overview", "Sesli özet üretimi", _db.AudioOverviewJobs.AnyAsync(ct)),
            await Scenario("classroom_voice", "Sesli sınıf etkileşimi", _db.ClassroomInteractions.AnyAsync(ct)),
            await Scenario("adaptive_quiz", "Adaptif quiz akışı", _db.AdaptiveAssessmentSessions.AnyAsync(ct)),
            await Scenario("live_tutor_trace", "Canlı tutor izi", _db.TutorTraceProjections.AnyAsync(ct)),
            await Scenario("pedagogy_eval", "Tutor pedagogy kalite kapısı", _db.TutorPedagogyEvaluationRuns.AnyAsync(ct)),
            await Scenario("standards_alignment", "Standards validation/export kapısı", StandardsProbeAsync(ct))
        };
        var status = scenarios.Any(s => s.Status == "blocked")
            ? "blocked"
            : scenarios.Any(s => s.Status == "watch")
            ? "ready_with_warnings"
            : "ready";
        return new V1RegressionGateDto { Status = status, Scenarios = scenarios };
    }

    private static async Task<V1RegressionScenarioDto> Scenario(string key, string label, Task<bool> probe)
    {
        var ok = await probe;
        return new V1RegressionScenarioDto
        {
            Key = key,
            Status = ok ? "ready" : "watch",
            UserSafeLabel = label,
            Evidence = ok ? "Bu senaryo için sistem kaydı bulundu." : "Henüz canlı veri yok; smoke/regression test ile doğrulanmalı."
        };
    }

    private async Task<bool> StandardsProbeAsync(CancellationToken ct) =>
        await _db.StandardsValidationRuns.AnyAsync(ct) || await _db.StandardsExportRuns.AnyAsync(ct);
}

public sealed class ProductionReadinessService : IProductionReadinessService
{
    private readonly IProviderGovernanceService _providers;
    private readonly IRetentionCleanupService _retention;
    private readonly IRedisStreamMaintenanceService _redis;
    private readonly IDbIndexAuditService _dbIndex;
    private readonly IV1RegressionGateService _regression;
    private readonly IStandardsAlignmentService _standards;
    private readonly ILearningRuntimeTelemetryService _runtimeTelemetry;

    public ProductionReadinessService(
        IProviderGovernanceService providers,
        IRetentionCleanupService retention,
        IRedisStreamMaintenanceService redis,
        IDbIndexAuditService dbIndex,
        IV1RegressionGateService regression,
        IStandardsAlignmentService standards,
        ILearningRuntimeTelemetryService runtimeTelemetry)
    {
        _providers = providers;
        _retention = retention;
        _redis = redis;
        _dbIndex = dbIndex;
        _regression = regression;
        _standards = standards;
        _runtimeTelemetry = runtimeTelemetry;
    }

    public async Task<ProductionReadinessDto> GetV1ReadinessAsync(Guid? userId = null, CancellationToken ct = default)
    {
        var provider = await _providers.GetSummaryAsync(userId, ct);
        var audio = await _retention.GetAudioRetentionSummaryAsync(ct);
        var redis = await _redis.GetSummaryAsync(ct);
        var db = await _dbIndex.AuditAsync(ct);
        var regression = await _regression.EvaluateAsync(ct);
        var standards = userId.HasValue ? await _standards.GetSummaryAsync(userId.Value, null, ct) : new StandardsSummaryDto { StandardsAlignmentStatus = "unknown" };
        var runtimeTelemetry = userId.HasValue
            ? await _runtimeTelemetry.GetLearningRuntimeHealthAsync(userId.Value, null, null, ct)
            : new LearningRuntimeHealthDto { Status = "unknown", UserSafeWarnings = ["Runtime telemetry is user-scoped."] };
        var sections = new[]
        {
            Section("standards", standards.StandardsAlignmentStatus, "Standards alignment", "Kazanım, soru ve learning event profilleri izleniyor."),
            Section("providers", provider.Status, "Provider governance", "Araç ve provider çağrıları telemetry ile takip ediliyor."),
            Section("runtime_telemetry", runtimeTelemetry.Status, "Learning runtime telemetry", "Tutor, plan, quiz, source, artifact, provider ve tool izleri guvenli runtime ozetiyle izleniyor."),
            Section("audio_retention", audio.Status, "Audio retention", "Ses byte payloadları retention kuralıyla yönetiliyor."),
            Section("redis_streams", redis.Status, "Redis live streams", "Canlı tutor izleri Redis ve SQL projection ile korunuyor."),
            Section("db_indexes", db.Status, "DB index audit", "V1 sorgu yüzeyleri için kritik indexler kontrol edildi."),
            Section("regression", regression.Status, "V1 regression gate", "V1 ana senaryoları readiness kapısında izleniyor.")
        };
        return new ProductionReadinessDto
        {
            Status = Overall(sections.Select(s => s.Status)),
            Sections = sections,
            ProviderGovernance = provider,
            RuntimeTelemetry = runtimeTelemetry,
            AudioRetention = audio,
            RedisStreams = redis,
            DbIndexAudit = db,
            RegressionGate = regression
        };
    }

    private static ProductionReadinessSectionDto Section(string key, string status, string label, string detail) => new()
    {
        Key = key,
        Status = status,
        UserSafeLabel = label,
        UserSafeDetail = detail
    };

    private static string Overall(IEnumerable<string> statuses)
    {
        var list = statuses.ToArray();
        if (list.Any(s => s is "blocked" or "critical" or "degraded")) return "blocked";
        if (list.Any(s => s is "watch" or "unknown" or "ready_with_warnings")) return "ready_with_warnings";
        return "ready";
    }
}
