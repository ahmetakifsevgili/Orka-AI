using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class UnifiedToolRuntimeService : IUnifiedToolRuntimeService
{
    private const int SummaryLimit = 1000;
    private const int EvidenceLimit = 20;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly OrkaDbContext _db;
    private readonly IToolCapabilityService _capabilities;
    private readonly IRuntimeTelemetryService _telemetry;
    private readonly ILogger<UnifiedToolRuntimeService> _logger;

    public UnifiedToolRuntimeService(
        OrkaDbContext db,
        IToolCapabilityService capabilities,
        IRuntimeTelemetryService telemetry,
        ILogger<UnifiedToolRuntimeService> logger)
    {
        _db = db;
        _capabilities = capabilities;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<ToolRuntimeDecisionDto> DecideAsync(
        Guid userId,
        ToolRuntimeRequestDto request,
        CancellationToken ct = default)
    {
        var normalized = NormalizeToolId(request.ToolId);
        var caller = NormalizeCaller(request.Caller);
        var policy = BuildPolicy(normalized, caller, request);
        var now = DateTime.UtcNow;

        var trace = new ToolRuntimeTrace
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = request.TopicId,
            SessionId = request.SessionId,
            ActiveLessonSnapshotId = request.ActiveLessonSnapshotId,
            StudentContextSnapshotId = request.StudentContextSnapshotId,
            TutorTurnStateId = request.TutorTurnStateId,
            TutorActionTraceId = request.TutorActionTraceId,
            ToolId = normalized,
            Caller = caller,
            Purpose = Clean(request.Purpose, 512) ?? string.Empty,
            Decision = policy.Decision,
            Status = policy.Allowed ? "allowed" : policy.Decision,
            RiskLevel = NormalizeRisk(request.RiskLevel),
            CanGroundClaims = policy.CanGroundClaims,
            InputSummary = Clean(request.InputSummary, SummaryLimit),
            EvidenceJson = "[]",
            FallbackReason = policy.Allowed ? null : policy.ReasonCode,
            ErrorCode = policy.Allowed ? null : policy.ReasonCode,
            CreatedAt = now
        };

        _db.ToolRuntimeTraces.Add(trace);
        await _db.SaveChangesAsync(ct);

        await _telemetry.RecordToolEventAsync(new ToolTelemetryEventRequest(
            UserId: userId,
            SessionId: request.SessionId,
            TopicId: request.TopicId,
            ToolId: normalized,
            CapabilityStatus: policy.Decision,
            Provider: "orka_runtime",
            Model: null,
            LatencyMs: 0,
            Success: policy.Allowed,
            ErrorCode: policy.Allowed ? null : policy.ReasonCode,
            FallbackUsed: !policy.Allowed,
            CorrelationId: trace.Id.ToString(),
            MetadataJson: JsonSerializer.Serialize(new
            {
                caller,
                policy.RequiredEvidenceMode,
                policy.CanGroundClaims,
                safe = true
            }, JsonOptions)), ct);

        policy.TraceId = trace.Id;
        policy.ToolId = normalized;
        return policy;
    }

    public async Task<ToolRuntimeResultDto> RecordResultAsync(
        Guid userId,
        ToolRuntimeResultDto result,
        CancellationToken ct = default)
    {
        var normalized = NormalizeToolId(result.ToolId);
        ToolRuntimeTrace? trace = null;
        if (result.TraceId.HasValue)
        {
            trace = await _db.ToolRuntimeTraces
                .FirstOrDefaultAsync(t => t.Id == result.TraceId.Value && t.UserId == userId && !t.IsDeleted, ct);
        }

        trace ??= new ToolRuntimeTrace
        {
            Id = result.TraceId ?? Guid.NewGuid(),
            UserId = userId,
            ToolId = normalized,
            Caller = NormalizeCaller(result.Caller),
            TopicId = result.TopicId,
            SessionId = result.SessionId,
            ActiveLessonSnapshotId = result.ActiveLessonSnapshotId,
            StudentContextSnapshotId = result.StudentContextSnapshotId,
            TutorTurnStateId = result.TutorTurnStateId,
            TutorActionTraceId = result.TutorActionTraceId,
            Decision = "allow",
            RiskLevel = "low",
            CreatedAt = DateTime.UtcNow
        };

        if (_db.Entry(trace).State == EntityState.Detached)
            _db.ToolRuntimeTraces.Add(trace);

        trace.Status = NormalizeStatus(result.Status, result.Success);
        trace.SafeResultSummary = Clean(result.SafeMessage, SummaryLimit);
        trace.EvidenceJson = SerializeEvidence(result.EvidenceItems, result.Citations);
        trace.FallbackReason = Clean(result.FallbackReason, 256);
        trace.ErrorCode = Clean(result.FallbackReason, 128);
        trace.LatencyMs = Math.Max(0, result.LatencyMs);
        trace.CompletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _telemetry.RecordToolEventAsync(new ToolTelemetryEventRequest(
            UserId: userId,
            SessionId: trace.SessionId,
            TopicId: trace.TopicId,
            ToolId: normalized,
            CapabilityStatus: trace.Status,
            Provider: "orka_runtime",
            Model: null,
            LatencyMs: trace.LatencyMs,
            Success: result.Success,
            ErrorCode: result.Success ? null : trace.ErrorCode,
            FallbackUsed: !result.Success || !string.IsNullOrWhiteSpace(result.FallbackReason),
            CorrelationId: trace.Id.ToString(),
            MetadataJson: JsonSerializer.Serialize(new
            {
                trace.Caller,
                trace.Decision,
                trace.CanGroundClaims,
                evidenceCount = result.EvidenceItems.Count,
                citationCount = result.Citations.Count
            }, JsonOptions)), ct);

        result.TraceId = trace.Id;
        return result;
    }

    public async Task<ToolRuntimeTraceDto?> GetToolRuntimeTraceAsync(
        Guid userId,
        Guid traceId,
        CancellationToken ct = default)
    {
        var trace = await _db.ToolRuntimeTraces
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == traceId && t.UserId == userId && !t.IsDeleted, ct);
        return trace is null ? null : ToDto(trace);
    }

    public async Task<IReadOnlyList<ToolRuntimeTraceDto>> GetRecentToolRuntimeTracesAsync(
        Guid userId,
        Guid? topicId = null,
        Guid? sessionId = null,
        int take = 20,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);
        var traces = await _db.ToolRuntimeTraces
            .AsNoTracking()
            .Where(t => t.UserId == userId && !t.IsDeleted)
            .Where(t => !topicId.HasValue || t.TopicId == topicId)
            .Where(t => !sessionId.HasValue || t.SessionId == sessionId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

        return traces.Select(ToDto).ToList();
    }

    public async Task<ToolGovernanceSummaryDto> GetToolGovernanceSummaryAsync(
        Guid userId,
        Guid? topicId = null,
        Guid? sessionId = null,
        CancellationToken ct = default)
    {
        var traces = await GetRecentToolRuntimeTracesAsync(userId, topicId, sessionId, 50, ct);
        return new ToolGovernanceSummaryDto
        {
            TraceCount = traces.Count,
            AllowedCount = traces.Count(t => t.Decision is "allow" or "allow_with_limits"),
            DeniedCount = traces.Count(t => t.Decision is "deny" or "require_human_action"),
            DegradedCount = traces.Count(t => t.Status is "degraded" or "failed" or "denied"),
            EvidenceProducingCount = traces.Count(t => t.EvidenceItems.Count > 0),
            LegacyToolPlanes =
            [
                "Semantic Kernel plugin auto-invocation is still an adapter plane; plugin telemetry is bounded but not yet a full policy owner.",
                "Controller-direct tools such as code execution remain explicit user actions and are not autonomous Tutor tools."
            ],
            RecentTraces = traces
        };
    }

    private ToolRuntimeDecisionDto BuildPolicy(string toolId, string caller, ToolRuntimeRequestDto request)
    {
        var capability = _capabilities.GetCapability(MapCapabilityId(toolId));
        if (capability is null && !KnownRuntimeTools.Contains(toolId))
        {
            return Deny(toolId, "unknown_tool", "This tool is not registered in Orka's governed tool runtime.");
        }

        if (capability is { RequiresAdmin: true })
        {
            return Deny(toolId, "admin_tool_not_available", "This tool is not available in the student learning runtime.");
        }

        if (capability is { Status: "Disabled" })
        {
            return new ToolRuntimeDecisionDto
            {
                Allowed = false,
                Decision = "degrade",
                ReasonCode = "capability_disabled",
                UserSafeReason = "The tool is currently unavailable, so Orka will continue with a safe fallback.",
                RequiredEvidenceMode = "none",
                CanGroundClaims = false,
                ShouldRecordTelemetry = true
            };
        }

        return toolId switch
        {
            "source_search" or "sources_query" => Allow("source_cited", true, true, true, 5, capability?.TimeoutMs),
            "wiki_search" or "wiki_query" => Allow("wiki_cited", true, true, true, 5, capability?.TimeoutMs),
            "ide_last_result" or "ide_execution" => Allow("execution_summary", false, true, false, 1, capability?.TimeoutMs),
            "review_query" or "flashcard_query" or "flashcards" => Allow("learning_memory", false, true, false, 5, capability?.TimeoutMs),
            "mermaid_graph" or "mermaid" => Allow("pedagogy_artifact", false, false, false, 1, capability?.TimeoutMs),
            "youtube_pedagogy" or "visual_generation" => AllowWithLimits("pedagogy_reference", false, false, false, 3, capability?.TimeoutMs),
            "weather" or "news" or "crypto" => AllowWithLimits("external_reference", false, true, true, 5, capability?.TimeoutMs),
            "wolfram_alpha" => AllowWithLimits("computed_reference", true, true, true, 3, capability?.TimeoutMs),
            "knowledge_entity" or "geo_context" or "socioeconomic_context" or "science_context" or "research_context" => AllowWithLimits("external_evidence", false, true, true, 5, capability?.TimeoutMs),
            "forum_signal" => AllowWithLimits("misconception_signal", false, true, true, 5, capability?.TimeoutMs),
            _ => Deny(toolId, "runtime_policy_missing", "The tool has no safe runtime policy yet.")
        };
    }

    private static ToolRuntimeDecisionDto Allow(string evidenceMode, bool canGround, bool writeTutorMemory, bool writeEvidence, int? maxResultCount, int? timeoutMs) => new()
    {
        Allowed = true,
        Decision = "allow",
        ReasonCode = "policy_allowed",
        UserSafeReason = "Tool is allowed for this learning context.",
        RequiredEvidenceMode = evidenceMode,
        MaxResultCount = maxResultCount,
        TimeoutMs = timeoutMs,
        CanGroundClaims = canGround,
        ShouldWriteTutorMemory = writeTutorMemory,
        ShouldWriteEvidence = writeEvidence,
        ShouldRecordTelemetry = true
    };

    private static ToolRuntimeDecisionDto AllowWithLimits(string evidenceMode, bool canGround, bool writeTutorMemory, bool writeEvidence, int? maxResultCount, int? timeoutMs)
    {
        var decision = Allow(evidenceMode, canGround, writeTutorMemory, writeEvidence, maxResultCount, timeoutMs);
        decision.Decision = "allow_with_limits";
        decision.ReasonCode = "policy_allowed_with_limits";
        decision.UserSafeReason = "Tool is allowed with bounded evidence and safe fallback rules.";
        return decision;
    }

    private static ToolRuntimeDecisionDto Deny(string toolId, string reasonCode, string userSafeReason) => new()
    {
        ToolId = toolId,
        Allowed = false,
        Decision = "deny",
        ReasonCode = reasonCode,
        UserSafeReason = userSafeReason,
        RequiredEvidenceMode = "none",
        CanGroundClaims = false,
        ShouldRecordTelemetry = true
    };

    private static readonly HashSet<string> KnownRuntimeTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "source_search", "wiki_search", "ide_last_result", "review_query", "flashcard_query",
        "mermaid_graph", "knowledge_entity", "geo_context", "socioeconomic_context", "science_context",
        "research_context", "forum_signal"
    };

    private static string MapCapabilityId(string toolId) => toolId switch
    {
        "source_search" => "sources_query",
        "wiki_search" => "sources_query",
        "flashcard_query" => "flashcards",
        "mermaid_graph" => "mermaid",
        "ide_last_result" => "ide_execution",
        _ => toolId
    };

    private static string NormalizeToolId(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant();

    private static string NormalizeCaller(string? value)
    {
        var caller = string.IsNullOrWhiteSpace(value) ? "internal" : value.Trim().ToLowerInvariant();
        return caller is "tutor" or "korteks" or "plan" or "wiki" or "quiz" or "frontend" or "internal"
            ? caller
            : "internal";
    }

    private static string NormalizeRisk(string? value)
    {
        var risk = string.IsNullOrWhiteSpace(value) ? "low" : value.Trim().ToLowerInvariant();
        return risk is "low" or "medium" or "high" ? risk : "low";
    }

    private static string NormalizeStatus(string status, bool success)
    {
        if (!string.IsNullOrWhiteSpace(status))
            return status.Trim().ToLowerInvariant();
        return success ? "succeeded" : "failed";
    }

    private static string SerializeEvidence(
        IReadOnlyList<ToolRuntimeEvidenceDto> evidence,
        IReadOnlyList<ProviderCitationDto> citations)
    {
        var safeEvidence = evidence
            .Take(EvidenceLimit)
            .Select(e => new ToolRuntimeEvidenceDto
            {
                EvidenceType = Clean(e.EvidenceType, 64) ?? "summary",
                Label = Clean(e.Label, 256) ?? string.Empty,
                Url = Clean(e.Url, 512),
                Provider = Clean(e.Provider, 128),
                Confidence = e.Confidence
            })
            .ToList();

        safeEvidence.AddRange(citations
            .Take(EvidenceLimit - safeEvidence.Count)
            .Select(c => new ToolRuntimeEvidenceDto
            {
                EvidenceType = "citation",
                Label = Clean(c.Label, 256) ?? string.Empty,
                Url = Clean(c.Url, 512),
                Provider = Clean(c.SourceName, 128),
                Confidence = c.Confidence
            }));

        return JsonSerializer.Serialize(safeEvidence.Take(EvidenceLimit), JsonOptions);
    }

    private static ToolRuntimeTraceDto ToDto(ToolRuntimeTrace trace) => new()
    {
        Id = trace.Id,
        ToolId = trace.ToolId,
        Caller = trace.Caller,
        TopicId = trace.TopicId,
        SessionId = trace.SessionId,
        ActiveLessonSnapshotId = trace.ActiveLessonSnapshotId,
        StudentContextSnapshotId = trace.StudentContextSnapshotId,
        TutorTurnStateId = trace.TutorTurnStateId,
        TutorActionTraceId = trace.TutorActionTraceId,
        Purpose = trace.Purpose,
        Decision = trace.Decision,
        Status = trace.Status,
        RiskLevel = trace.RiskLevel,
        CanGroundClaims = trace.CanGroundClaims,
        InputSummary = trace.InputSummary,
        SafeResultSummary = trace.SafeResultSummary,
        EvidenceItems = ParseEvidence(trace.EvidenceJson),
        FallbackReason = trace.FallbackReason,
        ErrorCode = trace.ErrorCode,
        LatencyMs = trace.LatencyMs,
        CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(trace.CreatedAt, DateTimeKind.Utc)),
        CompletedAt = trace.CompletedAt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(trace.CompletedAt.Value, DateTimeKind.Utc)) : null
    };

    private static IReadOnlyList<ToolRuntimeEvidenceDto> ParseEvidence(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<ToolRuntimeEvidenceDto>();

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<ToolRuntimeEvidenceDto>>(json, JsonOptions) ?? Array.Empty<ToolRuntimeEvidenceDto>();
        }
        catch
        {
            return Array.Empty<ToolRuntimeEvidenceDto>();
        }
    }

    private static string? Clean(string? value, int max = 512)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
