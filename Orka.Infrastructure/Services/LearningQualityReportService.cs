using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class LearningQualityReportService : ILearningQualityReportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OrkaDbContext _db;
    private readonly IConceptGraphQualityService _graphQuality;
    private readonly IAssessmentQualityService _assessmentQuality;
    private readonly IKnowledgeTracingService _knowledgeTracing;
    private readonly ITutorPolicyTraceService _policyTrace;
    private readonly IResourceConceptAlignmentService _resourceAlignment;
    private readonly IRagEvaluationService _ragEvaluation;
    private readonly ILearningSourceService _sources;
    private readonly IStandardsAlignmentService _standards;
    private readonly IRedisMemoryService? _redis;
    private readonly ILogger<LearningQualityReportService> _logger;

    public LearningQualityReportService(
        OrkaDbContext db,
        IConceptGraphQualityService graphQuality,
        IAssessmentQualityService assessmentQuality,
        IKnowledgeTracingService knowledgeTracing,
        ITutorPolicyTraceService policyTrace,
        IResourceConceptAlignmentService resourceAlignment,
        IRagEvaluationService ragEvaluation,
        ILearningSourceService sources,
        IStandardsAlignmentService standards,
        IRedisMemoryService? redis,
        ILogger<LearningQualityReportService> logger)
    {
        _db = db;
        _graphQuality = graphQuality;
        _assessmentQuality = assessmentQuality;
        _knowledgeTracing = knowledgeTracing;
        _policyTrace = policyTrace;
        _resourceAlignment = resourceAlignment;
        _ragEvaluation = ragEvaluation;
        _sources = sources;
        _standards = standards;
        _redis = redis;
        _logger = logger;
    }

    public async Task<LearningQualityReportDto> BuildTopicReportAsync(
        Guid userId,
        Guid? topicId,
        Guid? planRequestId = null,
        CancellationToken ct = default)
    {
        var snapshot = await LatestSnapshotAsync(userId, topicId, planRequestId, ct);
        var graphQuality = await _graphQuality.GetLatestAsync(userId, topicId, snapshot?.Id, ct);
        var assessmentQuality = await _assessmentQuality.GetLatestAsync(userId, topicId, planRequestId, ct);
        var masteryStates = (await _knowledgeTracing.GetRecentStatesAsync(userId, topicId, 20, ct)).ToList();
        var traces = (await _policyTrace.GetRecentAsync(userId, topicId, null, 12, ct)).ToList();
        var alignments = (await _resourceAlignment.GetRecentAsync(userId, topicId, snapshot?.Id, 20, ct)).ToList();
        var latestRag = await _ragEvaluation.GetLatestAsync(userId, topicId, ct)
            ?? await _ragEvaluation.EvaluateTopicAsync(userId, topicId, snapshot?.Id, ct);
        var assessmentCalibration = await LatestAssessmentCalibrationAsync(userId, topicId, ct);
        var standardsSummary = await _standards.GetSummaryAsync(userId, topicId, ct);
        SourceQualityReportDto? sourceQuality = null;
        if (topicId.HasValue)
        {
            sourceQuality = await _sources.GetTopicQualityAsync(userId, topicId.Value, ct);
        }
        var recentToolCalls = await _db.TutorToolCalls
            .AsNoTracking()
            .Where(t => t.UserId == userId && (!topicId.HasValue || t.TopicId == topicId.Value))
            .OrderByDescending(t => t.CreatedAt)
            .Take(12)
            .ToListAsync(ct);
        var recentArtifacts = await _db.TeachingArtifacts
            .AsNoTracking()
            .Where(a => a.UserId == userId && (!topicId.HasValue || a.TopicId == topicId.Value))
            .OrderByDescending(a => a.CreatedAt)
            .Take(12)
            .ToListAsync(ct);
        var recentEvidence = await _db.TeachingEvidenceItems
            .AsNoTracking()
            .Where(e => e.UserId == userId && (!topicId.HasValue || e.TopicId == topicId.Value))
            .OrderByDescending(e => e.CreatedAt)
            .Take(30)
            .ToListAsync(ct);
        var recentProviderHealth = await _db.TeachingEvidenceProviderHealth
            .AsNoTracking()
            .OrderByDescending(h => h.CheckedAt)
            .Take(30)
            .ToListAsync(ct);
        var recentTraceEvents = await _db.TutorTraceProjections
            .AsNoTracking()
            .Where(p => p.UserId == userId && (!topicId.HasValue || p.TopicId == topicId.Value))
            .OrderByDescending(p => p.OccurredAt)
            .Take(20)
            .ToListAsync(ct);

        var violationCount = await _db.LearningEventSchemaViolations
            .AsNoTracking()
            .CountAsync(v => v.UserId == userId && (!topicId.HasValue || v.TopicId == topicId.Value), ct);
        var policyViolationCount = await _db.TutorPolicyViolationsV2
            .AsNoTracking()
            .CountAsync(v => v.UserId == userId && (!topicId.HasValue || v.TopicId == topicId.Value), ct);
        var recentPedagogyRuns = await _db.TutorPedagogyEvaluationRuns
            .AsNoTracking()
            .Where(r => r.UserId == userId && (!topicId.HasValue || r.TopicId == topicId.Value))
            .OrderByDescending(r => r.CreatedAt)
            .Take(12)
            .ToListAsync(ct);
        var effectivePedagogyRuns = recentPedagogyRuns
            .GroupBy(r => r.TutorActionTraceId ?? r.Id)
            .Select(g => g.OrderByDescending(r => r.CreatedAt).First())
            .ToList();
        var recentPedagogyRunIds = effectivePedagogyRuns.Select(r => r.Id).ToArray();
        var recentPedagogyScores = await _db.TutorPedagogyRubricScores
            .AsNoTracking()
            .Where(s => recentPedagogyRunIds.Contains(s.EvaluationRunId))
            .OrderByDescending(s => s.CreatedAt)
            .Take(30)
            .Select(s => new TutorPedagogyRubricScoreDto(
                s.RubricKey,
                s.Score,
                s.Severity,
                s.IsCritical,
                s.Evidence,
                s.Recommendation))
            .ToListAsync(ct);

        var graphStatus = graphQuality?.QualityStatus ?? "unknown";
        var assessmentStatus = assessmentQuality?.QualityStatus ?? "unknown";
        var masteryStatus = MasteryStatus(masteryStates);
        var policyStatus = PolicyStatus(traces);
        var eventStatus = violationCount == 0 ? "healthy" : violationCount < 10 ? "degraded" : "critical";
        var sourceStatus = sourceQuality != null
            ? sourceQuality.RetrievalHealthStatus
            : alignments.Count == 0
            ? "unverified"
            : alignments.Any(a => a.AlignmentStatus == "strong") ? "grounded" : "weak";
        var toolStatus = ToolExecutionStatus(recentToolCalls);
        var artifactStatus = ArtifactRenderStatus(recentArtifacts);
        var learnerEvidenceStatus = masteryStates.Any(s => s.EvidenceCount >= 3 && s.Confidence >= 0.60m)
            ? "sufficient"
            : "evidence_insufficient";
        var ragStatus = latestRag?.QualityStatus ?? "unknown";
        var evidenceCoverage = EvidenceCoverageStatus(recentEvidence);
        var providerHealth = EvidenceProviderHealthStatus(recentProviderHealth);
        var evidenceFreshness = EvidenceFreshnessStatus(recentEvidence);
        var forumUsage = ForumSignalUsageStatus(recentEvidence);
        var evidenceCitation = EvidenceCitationCoverageStatus(recentEvidence);
        var pedagogyStatus = PedagogyStatus(effectivePedagogyRuns);
        var pedagogyScore = effectivePedagogyRuns.Count == 0 ? (decimal?)null : Math.Round(effectivePedagogyRuns.Average(r => r.OverallScore), 4);
        var criticalPedagogyViolations = effectivePedagogyRuns.Sum(r => r.CriticalViolationCount);
        var calibrationStatus = assessmentCalibration?.CalibrationStatus ?? "unknown";
        var adaptiveReadiness = assessmentCalibration?.AdaptiveReadiness ?? "unknown";
        var itemBankHealth = assessmentCalibration?.ItemBankHealth ?? "unknown";
        var traceHealth = recentTraceEvents.Count > 0 ? "healthy" : "empty";
        var standardsStatus = standardsSummary.StandardsAlignmentStatus;
        var overall = OverallStatus(
            graphStatus,
            assessmentStatus,
            masteryStatus,
            policyStatus,
            eventStatus,
            sourceStatus,
            toolStatus,
            artifactStatus,
            learnerEvidenceStatus,
            ragStatus,
            evidenceCoverage,
            providerHealth,
            evidenceFreshness,
            evidenceCitation,
            pedagogyStatus,
            calibrationStatus,
            adaptiveReadiness,
            itemBankHealth,
            standardsStatus);

        var dto = new LearningQualityReportDto
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ConceptGraphSnapshotId = snapshot?.Id,
            PlanRequestId = planRequestId,
            QualityStatus = overall,
            GraphQualityStatus = graphStatus,
            AssessmentQualityStatus = assessmentStatus,
            MasteryConfidenceStatus = masteryStatus,
            TutorPolicyComplianceStatus = policyStatus,
            EventHealthStatus = eventStatus,
            SourceGroundingStatus = sourceStatus,
            ToolExecutionHealthStatus = toolStatus,
            ArtifactRenderHealthStatus = artifactStatus,
            LearnerEvidenceStatus = learnerEvidenceStatus,
            RagQualityStatus = ragStatus,
            EvidenceCoverageStatus = evidenceCoverage,
            EvidenceProviderHealthStatus = providerHealth,
            EvidenceFreshnessStatus = evidenceFreshness,
            ForumSignalUsageStatus = forumUsage,
            EvidenceCitationCoverageStatus = evidenceCitation,
            TutorPedagogyStatus = pedagogyStatus,
            AssessmentCalibrationStatus = calibrationStatus,
            AdaptiveReadiness = adaptiveReadiness,
            ItemBankHealth = itemBankHealth,
            TraceHealth = traceHealth,
            StandardsAlignmentStatus = standardsStatus,
            CaseLikeCoverage = standardsSummary.CaseCoverage,
            QtiLikeCoverage = standardsSummary.QtiCoverage,
            CaliperXapiCoverage = standardsSummary.CaliperXapiCoverage,
            TutorPedagogyScore = pedagogyScore,
            CriticalPedagogyViolationCount = criticalPedagogyViolations,
            GraphQuality = graphQuality,
            AssessmentQuality = assessmentQuality,
            MasteryStates = masteryStates,
            RecentTutorPolicyTraces = traces,
            RecentPedagogyRubricScores = recentPedagogyScores,
            EventSchemaViolationCount = violationCount,
            PolicyViolationCount = policyViolationCount,
            RecentToolCalls = recentToolCalls.Select(ToToolDto).ToArray(),
            RecentArtifacts = recentArtifacts.Select(ToArtifactDto).ToArray(),
            RecentEvidenceCards = recentEvidence.Take(12).Select(ToEvidenceDto).ToArray(),
            LatestRagEvaluation = latestRag,
            SourceQuality = sourceQuality,
            AssessmentCalibration = assessmentCalibration,
            RecentTutorTraceEvents = recentTraceEvents.Select(ToTraceDto).ToArray(),
            StandardsSummary = standardsSummary,
            ResourceAlignments = alignments,
            GeneratedAt = DateTimeOffset.UtcNow
        };

        var entity = new LearningQualityReport
        {
            Id = dto.Id,
            UserId = userId,
            TopicId = topicId,
            ConceptGraphSnapshotId = snapshot?.Id,
            PlanRequestId = planRequestId,
            QualityStatus = dto.QualityStatus,
            GraphQualityStatus = dto.GraphQualityStatus,
            AssessmentQualityStatus = dto.AssessmentQualityStatus,
            MasteryConfidenceStatus = dto.MasteryConfidenceStatus,
            TutorPolicyComplianceStatus = dto.TutorPolicyComplianceStatus,
            EventHealthStatus = dto.EventHealthStatus,
            SourceGroundingStatus = dto.SourceGroundingStatus,
            ToolExecutionHealthStatus = dto.ToolExecutionHealthStatus,
            ArtifactRenderHealthStatus = dto.ArtifactRenderHealthStatus,
            LearnerEvidenceStatus = dto.LearnerEvidenceStatus,
            RagQualityStatus = dto.RagQualityStatus,
            EvidenceCoverageStatus = dto.EvidenceCoverageStatus,
            EvidenceProviderHealthStatus = dto.EvidenceProviderHealthStatus,
            EvidenceFreshnessStatus = dto.EvidenceFreshnessStatus,
            ForumSignalUsageStatus = dto.ForumSignalUsageStatus,
            EvidenceCitationCoverageStatus = dto.EvidenceCitationCoverageStatus,
            TutorPedagogyStatus = dto.TutorPedagogyStatus,
            AssessmentCalibrationStatus = dto.AssessmentCalibrationStatus,
            AdaptiveReadiness = dto.AdaptiveReadiness,
            ItemBankHealth = dto.ItemBankHealth,
            TraceHealth = dto.TraceHealth,
            StandardsAlignmentStatus = dto.StandardsAlignmentStatus,
            CaseLikeCoverage = dto.CaseLikeCoverage,
            QtiLikeCoverage = dto.QtiLikeCoverage,
            CaliperXapiCoverage = dto.CaliperXapiCoverage,
            TutorPedagogyScore = dto.TutorPedagogyScore,
            CriticalPedagogyViolationCount = dto.CriticalPedagogyViolationCount,
            ReportJson = JsonSerializer.Serialize(dto, JsonOptions),
            GeneratedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _db.LearningQualityReports.Add(entity);
        await _db.SaveChangesAsync(ct);

        await TryCacheAsync(userId, topicId, dto);
        return dto;
    }

    public async Task<LearningQualityReportDto> GetTopicReportAsync(
        Guid userId,
        Guid? topicId,
        CancellationToken ct = default)
    {
        var existing = await _db.LearningQualityReports
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.TopicId == topicId)
            .OrderByDescending(r => r.GeneratedAt)
            .FirstOrDefaultAsync(ct);
        if (existing != null && TryDeserialize(existing.ReportJson, out var dto))
        {
            return dto;
        }

        return await BuildTopicReportAsync(userId, topicId, null, ct);
    }

    private async Task<ConceptGraphSnapshot?> LatestSnapshotAsync(Guid userId, Guid? topicId, Guid? planRequestId, CancellationToken ct)
    {
        var query = _db.ConceptGraphSnapshots.AsNoTracking().Where(s => s.UserId == userId);
        if (topicId.HasValue) query = query.Where(s => s.TopicId == topicId.Value);
        if (planRequestId.HasValue) query = query.Where(s => s.PlanRequestId == planRequestId.Value);
        return await query.OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync(ct);
    }

    private async Task<AssessmentCalibrationRunDto?> LatestAssessmentCalibrationAsync(Guid userId, Guid? topicId, CancellationToken ct)
    {
        var run = await _db.AssessmentCalibrationRuns
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.TopicId == topicId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (run == null) return null;

        var items = await _db.AssessmentCalibrationItems
            .AsNoTracking()
            .Where(i => i.AssessmentCalibrationRunId == run.Id)
            .OrderByDescending(i => i.DiscriminationEstimate)
            .Take(40)
            .Select(i => new AssessmentCalibrationItemDto
            {
                Id = i.Id,
                AssessmentItemId = i.AssessmentItemId,
                ConceptKey = i.ConceptKey,
                DifficultyEstimate = i.DifficultyEstimate,
                DiscriminationEstimate = i.DiscriminationEstimate,
                ExposureCount = i.ExposureCount,
                EvidenceCount = i.EvidenceCount,
                CalibrationStatus = i.CalibrationStatus,
                Reason = i.Reason
            })
            .ToListAsync(ct);

        return new AssessmentCalibrationRunDto
        {
            Id = run.Id,
            UserId = run.UserId,
            TopicId = run.TopicId,
            ConceptGraphSnapshotId = run.ConceptGraphSnapshotId,
            CalibrationStatus = run.CalibrationStatus,
            AdaptiveReadiness = run.AdaptiveReadiness,
            ItemBankHealth = run.ItemBankHealth,
            ItemCount = run.ItemCount,
            HealthyItemCount = run.HealthyItemCount,
            ConceptCount = run.ConceptCount,
            ReadyConceptCount = run.ReadyConceptCount,
            AverageDifficulty = run.AverageDifficulty,
            AverageDiscrimination = run.AverageDiscrimination,
            AverageExposure = run.AverageExposure,
            Items = items,
            CreatedAt = run.CreatedAt
        };
    }

    private async Task TryCacheAsync(Guid userId, Guid? topicId, LearningQualityReportDto dto)
    {
        if (_redis == null) return;
        try
        {
            var key = $"orka:v2:quality-report:{userId:N}:{(topicId.HasValue ? topicId.Value.ToString("N") : "global")}";
            await _redis.SetJsonAsync(key, JsonSerializer.Serialize(dto, JsonOptions), TimeSpan.FromHours(6));
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[LearningQualityReport] Redis write skipped. UserRef={UserRef} TopicRef={TopicRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeId(topicId, "topic"),
                LogPrivacyGuard.SafeExceptionType(ex));
        }
    }

    private static string MasteryStatus(IReadOnlyList<KnowledgeTracingStateDto> states)
    {
        if (states.Count == 0) return "evidence_insufficient";
        var avgConfidence = states.Average(s => s.Confidence);
        if (avgConfidence < 0.45m) return "evidence_insufficient";
        if (states.Any(s => s.RemediationNeed == "high")) return "degraded";
        return "healthy";
    }

    private static string PolicyStatus(IReadOnlyList<TutorPolicyTraceDto> traces)
    {
        if (traces.Count == 0) return "unknown";
        var violationCount = traces.Sum(t => t.PolicyViolations.Count);
        if (violationCount == 0) return "healthy";
        return violationCount < 5 ? "degraded" : "critical";
    }

    private static string ToolExecutionStatus(IReadOnlyList<TutorToolCall> tools)
    {
        if (tools.Count == 0) return "unknown";
        var blocked = tools.Count(t => t.Status is "blocked" or "timeout" or "degraded");
        var ready = tools.Count(t => t.Success && t.Status == "ready");
        if (blocked >= 3) return "critical";
        if (ready == 0 && tools.Any(t => t.Status == "needs_input")) return "degraded";
        return blocked == 0 ? "healthy" : "degraded";
    }

    private static string ArtifactRenderStatus(IReadOnlyList<TeachingArtifact> artifacts)
    {
        if (artifacts.Count == 0) return "unknown";
        if (artifacts.Any(a => a.Status == "render_degraded" || !string.IsNullOrWhiteSpace(a.RenderError))) return "degraded";
        return artifacts.Any(a => a.Status is "ready" or "rendered") ? "healthy" : "unknown";
    }

    private static string EvidenceCoverageStatus(IReadOnlyList<TeachingEvidenceItem> evidence)
    {
        if (evidence.Count == 0) return "unknown";
        var readyTypes = evidence.Where(e => e.Status == "ready").Select(e => e.EvidenceType).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return readyTypes >= 3 ? "healthy" : readyTypes >= 1 ? "degraded" : "unverified";
    }

    private static string EvidenceProviderHealthStatus(IReadOnlyList<TeachingEvidenceProviderHealth> health)
    {
        if (health.Count == 0) return "unknown";
        var recent = health.Take(12).ToList();
        var failures = recent.Count(h => !h.Success || h.Status is "timeout" or "degraded" or "blocked");
        if (failures >= 6) return "critical";
        return failures == 0 ? "healthy" : "degraded";
    }

    private static string EvidenceFreshnessStatus(IReadOnlyList<TeachingEvidenceItem> evidence)
    {
        if (evidence.Count == 0) return "unknown";
        var now = DateTime.UtcNow;
        if (evidence.Any(e => !e.ExpiresAt.HasValue || e.ExpiresAt.Value >= now)) return "fresh";
        return "stale";
    }

    private static string ForumSignalUsageStatus(IReadOnlyList<TeachingEvidenceItem> evidence)
    {
        var forum = evidence.Where(e => e.EvidenceType == "forum_signal").ToList();
        if (forum.Count == 0) return "none";
        return forum.All(e => e.RiskLevel == "medium" &&
                              (e.ClassroomUse.Contains("dogru bilgi", StringComparison.OrdinalIgnoreCase) ||
                               e.AnalogyCandidate.Contains("not", StringComparison.OrdinalIgnoreCase)))
            ? "signal_only"
            : "review_needed";
    }

    private static string EvidenceCitationCoverageStatus(IReadOnlyList<TeachingEvidenceItem> evidence)
    {
        var ready = evidence.Where(e => e.Status == "ready").ToList();
        if (ready.Count == 0) return "unknown";
        var cited = ready.Count(e => !string.IsNullOrWhiteSpace(e.CitationLabel) || !string.IsNullOrWhiteSpace(e.CitationUrl));
        var ratio = cited / (decimal)ready.Count;
        return ratio >= 0.90m ? "healthy" : ratio >= 0.60m ? "degraded" : "critical";
    }

    private static string PedagogyStatus(IReadOnlyList<TutorPedagogyEvaluationRun> runs)
    {
        if (runs.Count == 0) return "unknown";
        if (runs.Any(r => r.HasCriticalViolation || r.Status == "degraded")) return "degraded";
        var average = runs.Average(r => r.OverallScore);
        if (average < 0.60m) return "degraded";
        return average < 0.80m || runs.Any(r => r.Status == "watch") ? "watch" : "healthy";
    }

    private static string OverallStatus(params string[] statuses)
    {
        if (statuses.Any(s => string.Equals(s, "critical", StringComparison.OrdinalIgnoreCase))) return "critical";
        if (statuses.Any(s =>
                string.Equals(s, "degraded", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "weak", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "limited", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "not_ready", StringComparison.OrdinalIgnoreCase)))
        {
            return "degraded";
        }

        if (statuses.Any(s => string.Equals(s, "watch", StringComparison.OrdinalIgnoreCase))) return "watch";
        if (statuses.Any(s =>
                string.Equals(s, "thin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "unverified", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "empty", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "evidence_insufficient", StringComparison.OrdinalIgnoreCase)))
        {
            return "thin_evidence";
        }

        if (statuses.Any(s => string.Equals(s, "unknown", StringComparison.OrdinalIgnoreCase))) return "unknown";
        return "healthy";
    }

    private static bool TryDeserialize(string json, out LearningQualityReportDto dto)
    {
        try
        {
            dto = JsonSerializer.Deserialize<LearningQualityReportDto>(json, JsonOptions) ?? new LearningQualityReportDto();
            return dto.Id != Guid.Empty;
        }
        catch
        {
            dto = new LearningQualityReportDto();
            return false;
        }
    }

    private static TutorToolCallDto ToToolDto(TutorToolCall entity) => new()
    {
        Id = entity.Id,
        ToolId = entity.ToolId,
        Provider = entity.Provider,
        Status = entity.Status,
        Success = entity.Success,
        RiskLevel = entity.RiskLevel,
        Evidence = entity.Evidence,
        FallbackReason = entity.FallbackReason,
        ErrorCode = entity.ErrorCode,
        SafeMessage = entity.SafeMessage,
        Confidence = entity.Confidence,
        SourceCount = entity.SourceCount,
        LatencyMs = entity.LatencyMs,
        StartedAt = entity.StartedAt,
        FinishedAt = entity.FinishedAt
    };

    private static TeachingArtifactDto ToArtifactDto(TeachingArtifact entity) => new()
    {
        Id = entity.Id,
        UserId = entity.UserId,
        TopicId = entity.TopicId,
        SessionId = entity.SessionId,
        TutorActionTraceId = entity.TutorActionTraceId,
        ArtifactType = entity.ArtifactType,
        Title = entity.Title,
        Content = entity.Content,
        RenderFormat = entity.RenderFormat,
        Status = entity.Status,
        Provider = entity.Provider,
        ExternalUrl = entity.ExternalUrl,
        RenderError = entity.RenderError,
        RenderedAt = entity.RenderedAt,
        CreatedAt = entity.CreatedAt
    };

    private static TeachingEvidenceCardDto ToEvidenceDto(TeachingEvidenceItem entity) => new()
    {
        Id = entity.Id,
        UserId = entity.UserId,
        TopicId = entity.TopicId,
        SessionId = entity.SessionId,
        TutorTurnStateId = entity.TutorTurnStateId,
        TutorActionTraceId = entity.TutorActionTraceId,
        TutorToolCallId = entity.TutorToolCallId,
        Provider = entity.Provider,
        EvidenceType = entity.EvidenceType,
        ConceptKey = entity.ConceptKey,
        Query = entity.Query,
        Title = entity.Title,
        Summary = entity.Summary,
        FactualClaim = entity.FactualClaim,
        AnalogyCandidate = entity.AnalogyCandidate,
        ClassroomUse = entity.ClassroomUse,
        CitationUrl = entity.CitationUrl,
        CitationLabel = entity.CitationLabel,
        Confidence = (double)entity.Confidence,
        Freshness = entity.Freshness,
        RiskLevel = entity.RiskLevel,
        RawPayloadHash = entity.RawPayloadHash,
        Status = entity.Status,
        CreatedAt = entity.CreatedAt
    };

    private static TutorTraceTimelineEventDto ToTraceDto(TutorTraceProjection entity) => new()
    {
        Id = entity.Id,
        StreamId = entity.StreamId,
        EventType = entity.EventType,
        EventGroup = entity.EventGroup,
        UserSafeLabel = entity.UserSafeLabel,
        UserSafeDetail = entity.UserSafeDetail,
        Severity = entity.Severity,
        Values = DeserializeTraceValues(entity.PayloadJson),
        OccurredAt = entity.OccurredAt
    };

    private static Dictionary<string, string> DeserializeTraceValues(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }
}
