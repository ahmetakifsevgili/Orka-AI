using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class LearningRuntimeTelemetryService : ILearningRuntimeTelemetryService
{
    private const int MetadataLimit = 4000;
    private readonly OrkaDbContext _db;
    private readonly ILogger<LearningRuntimeTelemetryService> _logger;

    public LearningRuntimeTelemetryService(OrkaDbContext db, ILogger<LearningRuntimeTelemetryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<LearningRuntimeTraceDto> RecordEventAsync(Guid userId, LearningRuntimeEventRequestDto request, CancellationToken ct = default)
    {
        var privacy = TelemetryPrivacyGuard.Validate(null, request.SafeMetadata);
        var metadata = new Dictionary<string, string>(privacy.SafeMetadata, StringComparer.OrdinalIgnoreCase)
        {
            ["category"] = Normalize(request.Category, "learning_event"),
            ["severity"] = Normalize(request.Severity, "info"),
            ["safeMessage"] = Clip(request.SafeMessage ?? UserSafeMessage(request.Status), 240)
        };

        if (request.PromptTokens.HasValue) metadata["promptTokens"] = Math.Max(0, request.PromptTokens.Value).ToString();
        if (request.CompletionTokens.HasValue) metadata["completionTokens"] = Math.Max(0, request.CompletionTokens.Value).ToString();
        if (request.TotalTokens.HasValue) metadata["totalTokens"] = Math.Max(0, request.TotalTokens.Value).ToString();
        if (request.EstimatedCostUsd.HasValue) metadata["estimatedCostUsd"] = Math.Max(0m, request.EstimatedCostUsd.Value).ToString("0.########");

        var entity = new ToolTelemetryEvent
        {
            Id = Guid.NewGuid(),
            OccurredAt = DateTime.UtcNow,
            UserId = userId,
            SessionId = request.SessionId,
            TopicId = request.TopicId,
            ToolId = Clip($"{Normalize(request.Category, "learning_event")}.{Normalize(request.Operation, "runtime_event")}", 128),
            CapabilityStatus = Clip(Normalize(request.Status, "succeeded"), 64),
            Provider = Clip(request.Provider, 128),
            Model = Clip(request.Model, 256),
            LatencyMs = Math.Max(0, request.LatencyMs ?? 0),
            Success = !IsFailure(request.Status),
            ErrorCode = Clip(request.ErrorCode, 128),
            FallbackUsed = !string.IsNullOrWhiteSpace(request.FallbackReason) || IsFallback(request.Status),
            CorrelationId = Clip(request.CorrelationId, 128),
            MetadataJson = JsonSerializer.Serialize(metadata)
        };

        try
        {
            _db.ToolTelemetryEvents.Add(entity);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LearningRuntimeTelemetry] Runtime trace write failed. Category={Category}", request.Category);
        }

        return MapToolTelemetry(entity);
    }

    public async Task<LearningRuntimeTraceDto?> GetTraceAsync(Guid userId, Guid traceId, CancellationToken ct = default)
    {
        var toolRuntime = await _db.ToolRuntimeTraces.AsNoTracking()
            .Where(t => t.UserId == userId && !t.IsDeleted && t.Id == traceId)
            .FirstOrDefaultAsync(ct);
        if (toolRuntime is not null)
            return MapToolRuntime(toolRuntime);

        var eventTrace = await _db.ToolTelemetryEvents.AsNoTracking()
            .Where(t => t.UserId == userId && t.Id == traceId)
            .FirstOrDefaultAsync(ct);
        if (eventTrace is not null)
            return MapToolTelemetry(eventTrace);

        var cost = await _db.CostRecords.AsNoTracking()
            .Where(c => c.UserId == userId && c.Id == traceId)
            .FirstOrDefaultAsync(ct);
        return cost is null ? null : MapCost(cost);
    }

    public async Task<IReadOnlyList<LearningRuntimeTraceDto>> GetRecentTracesAsync(Guid userId, Guid? topicId = null, Guid? sessionId = null, int take = 25, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 100);
        var toolRuntime = await _db.ToolRuntimeTraces.AsNoTracking()
            .Where(t => t.UserId == userId && !t.IsDeleted)
            .Where(t => topicId == null || t.TopicId == topicId)
            .Where(t => sessionId == null || t.SessionId == sessionId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

        var toolEvents = await _db.ToolTelemetryEvents.AsNoTracking()
            .Where(t => t.UserId == userId)
            .Where(t => topicId == null || t.TopicId == topicId)
            .Where(t => sessionId == null || t.SessionId == sessionId)
            .OrderByDescending(t => t.OccurredAt)
            .Take(take)
            .ToListAsync(ct);

        var costs = await _db.CostRecords.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Where(c => topicId == null || c.TopicId == topicId)
            .Where(c => sessionId == null || c.SessionId == sessionId)
            .OrderByDescending(c => c.OccurredAt)
            .Take(take)
            .ToListAsync(ct);

        return toolRuntime.Select(MapToolRuntime)
            .Concat(toolEvents.Select(MapToolTelemetry))
            .Concat(costs.Select(MapCost))
            .OrderByDescending(t => t.CreatedAt)
            .Take(take)
            .ToArray();
    }

    public async Task<LearningRuntimeCorrelationDto> GetCorrelationSummaryAsync(Guid userId, string correlationId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return new LearningRuntimeCorrelationDto
            {
                Status = "unknown",
                UserSafeWarnings = ["Correlation id is missing."]
            };
        }

        var traces = (await GetRecentTracesAsync(userId, null, null, 100, ct))
            .Where(t => string.Equals(t.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.CreatedAt)
            .ToArray();

        return new LearningRuntimeCorrelationDto
        {
            CorrelationId = correlationId,
            Status = Overall(traces),
            ParticipatedServices = traces.Select(t => t.Category).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
            DegradedServices = traces.Where(t => t.IsDegraded || t.FallbackUsed).Select(t => t.Category).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
            FallbackReasons = traces.Select(t => t.FallbackReason).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).Cast<string>().ToArray(),
            CostSummary = CostSummary(traces),
            Traces = traces,
            UserSafeWarnings = CorrelationWarnings(traces)
        };
    }

    public async Task<LearningRuntimeHealthDto> GetLearningRuntimeHealthAsync(Guid userId, Guid? topicId = null, Guid? sessionId = null, CancellationToken ct = default)
    {
        var traces = await GetRecentTracesAsync(userId, topicId, sessionId, 100, ct);
        var services = traces
            .GroupBy(t => t.Category)
            .Select(g => new LearningRuntimeServiceHealthDto
            {
                Service = g.Key,
                Status = Overall(g),
                TraceCount = g.Count(),
                DegradedCount = g.Count(t => t.IsDegraded || t.FallbackUsed),
                FailedCount = g.Count(t => t.Status is "failed" or "error"),
                AverageLatencyMs = g.Where(t => t.LatencyMs.HasValue).Select(t => t.LatencyMs!.Value).DefaultIfEmpty(0).Average() is var avg ? (long)Math.Round(avg) : null,
                UserSafeMessage = ServiceMessage(g.Key, Overall(g))
            })
            .OrderBy(s => s.Service)
            .ToArray();

        var warnings = new List<string>();
        var missingCorrelation = traces.Count(t => string.IsNullOrWhiteSpace(t.CorrelationId));
        if (missingCorrelation > 0) warnings.Add("Some runtime records do not have a correlation id yet.");
        if (traces.Any(t => t.FallbackUsed)) warnings.Add("One or more runtime paths used fallback or degraded behavior.");
        if (traces.Count == 0) warnings.Add("No recent runtime telemetry is available for this scope.");

        return new LearningRuntimeHealthDto
        {
            Status = Overall(traces),
            TraceCount = traces.Count,
            CorrelatedTraceCount = traces.Count(t => !string.IsNullOrWhiteSpace(t.CorrelationId)),
            MissingCorrelationCount = missingCorrelation,
            DegradedCount = traces.Count(t => t.IsDegraded),
            DeniedCount = traces.Count(t => t.IsDenied),
            FailedCount = traces.Count(t => t.Status is "failed" or "error"),
            FallbackCount = traces.Count(t => t.FallbackUsed),
            CostSummary = CostSummary(traces),
            Services = services,
            UserSafeWarnings = warnings
        };
    }

    public async Task<LearningRuntimeFlowSummaryDto> GetTopicSummaryAsync(Guid userId, Guid topicId, Guid? sessionId = null, CancellationToken ct = default)
    {
        var traces = await GetRecentTracesAsync(userId, topicId, sessionId, 100, ct);
        var latestCorrelation = traces.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.CorrelationId))?.CorrelationId ?? string.Empty;
        var traceServices = traces.Select(t => t.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var syncStatus = traceServices.Contains("tutor_policy") && traceServices.Contains("plan_quality") && traceServices.Contains("assessment_blueprint")
            ? "covered"
            : "partial";

        return new LearningRuntimeFlowSummaryDto
        {
            TopicId = topicId,
            SessionId = sessionId,
            CorrelationId = latestCorrelation,
            LatestTraceAt = traces.FirstOrDefault()?.CreatedAt,
            Status = Overall(traces),
            ParticipatedServices = traceServices.Order().ToArray(),
            DegradedServices = traces.Where(t => t.IsDegraded || t.FallbackUsed).Select(t => t.Category).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray(),
            FallbackReasons = traces.Select(t => t.FallbackReason).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).Cast<string>().ToArray(),
            CostSummary = CostSummary(traces),
            PlanQuizTutorSyncStatus = syncStatus,
            EvidenceCount = traces.Sum(t => t.EvidenceCount),
            ToolCount = traces.Sum(t => t.ToolCount),
            ArtifactCount = traces.Sum(t => t.ArtifactCount),
            SourceCount = traces.Sum(t => t.SourceCount),
            UserSafeWarnings = syncStatus == "covered" ? Array.Empty<string>() : ["Plan/quiz/tutor runtime traces are not fully correlated yet."]
        };
    }

    public LearningRuntimePrivacyCheckDto ValidateTelemetryPrivacy(LearningRuntimePrivacyCheckRequestDto request)
    {
        var result = TelemetryPrivacyGuard.Validate(request.MetadataJson, request.Metadata);
        return new LearningRuntimePrivacyCheckDto
        {
            Status = result.IsSafe ? "ready" : "blocked",
            IsSafe = result.IsSafe,
            BlockedTerms = result.BlockedTerms,
            SafeMetadata = result.SafeMetadata,
            UserSafeMessage = result.IsSafe
                ? "Telemetry metadata is bounded and public-safe."
                : "Telemetry metadata contains raw or sensitive fields and must be summarized before exposure."
        };
    }

    private static LearningRuntimeTraceDto MapToolRuntime(ToolRuntimeTrace trace)
    {
        var evidenceCount = SafeCountJsonArray(trace.EvidenceJson);
        return new LearningRuntimeTraceDto
        {
            Id = trace.Id,
            TopicId = trace.TopicId,
            SessionId = trace.SessionId,
            Category = "tool_runtime",
            Operation = trace.ToolId,
            Status = Normalize(trace.Status, "unknown"),
            Severity = trace.Status is "failed" ? "error" : trace.Status is "degraded" or "denied" ? "warning" : "info",
            SafeMessage = trace.SafeResultSummary ?? trace.FallbackReason ?? "Tool runtime decision was recorded.",
            LatencyMs = trace.LatencyMs,
            FallbackReason = trace.FallbackReason,
            ErrorCode = trace.ErrorCode,
            IsDegraded = trace.Status is "degraded" or "failed",
            IsDenied = trace.Decision == "deny" || trace.Status == "denied",
            FallbackUsed = !string.IsNullOrWhiteSpace(trace.FallbackReason),
            EvidenceCount = evidenceCount,
            ToolCount = 1,
            TraceLinks = new Dictionary<string, string>
            {
                ["toolRuntimeTraceId"] = trace.Id.ToString(),
                ["toolId"] = trace.ToolId,
                ["caller"] = trace.Caller
            },
            SafeMetadata = new Dictionary<string, string>
            {
                ["purpose"] = Clip(trace.Purpose, 240),
                ["decision"] = trace.Decision,
                ["riskLevel"] = trace.RiskLevel,
                ["canGroundClaims"] = trace.CanGroundClaims.ToString().ToLowerInvariant()
            },
            CreatedAt = trace.CreatedAt,
            CompletedAt = trace.CompletedAt
        };
    }

    private static LearningRuntimeTraceDto MapToolTelemetry(ToolTelemetryEvent trace)
    {
        var metadata = TelemetryPrivacyGuard.FromJson(trace.MetadataJson);
        var category = metadata.TryGetValue("category", out var metaCategory)
            ? Normalize(metaCategory, "tool_runtime")
            : metadata.TryGetValue("caller", out var caller) && caller == "semantic_kernel_plugin"
                ? "semantic_kernel_plugin"
                : trace.ToolId.StartsWith("learning_runtime.", StringComparison.OrdinalIgnoreCase)
                    ? "learning_runtime"
                    : "tool_runtime";
        var severity = metadata.TryGetValue("severity", out var metaSeverity) ? Normalize(metaSeverity, "info") : trace.Success ? "info" : "warning";
        var safeMessage = metadata.TryGetValue("safeMessage", out var metaMessage) ? metaMessage : UserSafeMessage(trace.CapabilityStatus);
        var promptTokens = ParseInt(metadata, "promptTokens");
        var completionTokens = ParseInt(metadata, "completionTokens");
        var totalTokens = ParseInt(metadata, "totalTokens");
        var estimatedCost = ParseDecimal(metadata, "estimatedCostUsd");
        var operation = trace.ToolId.Contains('.', StringComparison.Ordinal)
            ? trace.ToolId.Split('.').Last()
            : trace.ToolId;

        return new LearningRuntimeTraceDto
        {
            Id = trace.Id,
            CorrelationId = trace.CorrelationId ?? string.Empty,
            TopicId = trace.TopicId,
            SessionId = trace.SessionId,
            Category = category,
            Operation = Normalize(operation, trace.ToolId),
            Status = Normalize(trace.CapabilityStatus, trace.Success ? "succeeded" : "failed"),
            Severity = severity,
            SafeMessage = safeMessage,
            LatencyMs = trace.LatencyMs,
            Provider = trace.Provider,
            Model = trace.Model,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens,
            EstimatedCostUsd = estimatedCost,
            CostStatus = estimatedCost.HasValue ? "available" : "not_available",
            FallbackReason = trace.FallbackUsed ? trace.ErrorCode ?? "fallback_used" : null,
            ErrorCode = trace.ErrorCode,
            IsDegraded = trace.FallbackUsed || !trace.Success,
            IsDenied = trace.CapabilityStatus.Contains("denied", StringComparison.OrdinalIgnoreCase) || trace.CapabilityStatus.Contains("deny", StringComparison.OrdinalIgnoreCase),
            FallbackUsed = trace.FallbackUsed,
            ToolCount = category.Contains("tool", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
            TraceLinks = new Dictionary<string, string> { ["toolTelemetryEventId"] = trace.Id.ToString(), ["toolId"] = trace.ToolId },
            SafeMetadata = metadata,
            CreatedAt = trace.OccurredAt,
            CompletedAt = trace.OccurredAt
        };
    }

    private static LearningRuntimeTraceDto MapCost(CostRecord cost)
    {
        var metadata = TelemetryPrivacyGuard.FromJson(cost.MetadataJson);
        return new LearningRuntimeTraceDto
        {
            Id = cost.Id,
            TopicId = cost.TopicId,
            SessionId = cost.SessionId,
            Category = "provider_call",
            Operation = cost.AgentRole,
            Status = cost.Success ? "succeeded" : "failed",
            Severity = cost.Success ? "info" : "warning",
            SafeMessage = cost.Success ? "Provider/model cost telemetry was recorded." : "Provider/model call failed with a safe error code.",
            Provider = cost.Provider,
            Model = cost.Model,
            TotalTokens = cost.EstimatedTokens,
            EstimatedCostUsd = cost.EstimatedCostUsd,
            CostStatus = "available",
            ErrorCode = cost.ErrorCode,
            IsDegraded = !cost.Success,
            TraceLinks = new Dictionary<string, string>
            {
                ["costRecordId"] = cost.Id.ToString(),
                ["agentRole"] = cost.AgentRole
            },
            SafeMetadata = metadata,
            CreatedAt = cost.OccurredAt,
            CompletedAt = cost.OccurredAt
        };
    }

    private static LearningRuntimeCostDto CostSummary(IEnumerable<LearningRuntimeTraceDto> traces)
    {
        var list = traces.Where(t => t.EstimatedCostUsd.HasValue || t.TotalTokens.HasValue).ToArray();
        if (list.Length == 0)
            return new LearningRuntimeCostDto();

        return new LearningRuntimeCostDto
        {
            Status = "available",
            TotalTokens = list.Sum(t => t.TotalTokens ?? 0),
            EstimatedCostUsd = list.Sum(t => t.EstimatedCostUsd ?? 0m),
            UserSafeMessage = "Cost is estimated from available provider telemetry only."
        };
    }

    private static string Overall(IEnumerable<LearningRuntimeTraceDto> traces)
    {
        var list = traces.ToArray();
        if (list.Length == 0) return "unknown";
        if (list.Any(t => t.Status is "failed" or "error")) return "degraded";
        if (list.Any(t => t.IsDenied || t.IsDegraded || t.FallbackUsed)) return "ready_with_warnings";
        return "ready";
    }

    private static IReadOnlyList<string> CorrelationWarnings(IReadOnlyCollection<LearningRuntimeTraceDto> traces)
    {
        var warnings = new List<string>();
        if (traces.Count == 0) warnings.Add("No runtime traces were found for this correlation id.");
        if (traces.Any(t => t.FallbackUsed)) warnings.Add("This flow used fallback or degraded runtime behavior.");
        return warnings;
    }

    private static string ServiceMessage(string service, string status) =>
        status switch
        {
            "ready" => $"{service} telemetry looks healthy.",
            "ready_with_warnings" => $"{service} telemetry has warnings or degraded fallback.",
            "degraded" => $"{service} telemetry contains failed runtime records.",
            _ => $"{service} telemetry has limited recent evidence."
        };

    private static bool IsFailure(string? status) =>
        Normalize(status, string.Empty) is "failed" or "error";

    private static bool IsFallback(string? status) =>
        Normalize(status, string.Empty) is "degraded" or "skipped";

    private static string UserSafeMessage(string? status) =>
        Normalize(status, "unknown") switch
        {
            "denied" => "Runtime policy denied this operation safely.",
            "degraded" => "Runtime operation degraded and used safe fallback.",
            "failed" => "Runtime operation failed with bounded telemetry.",
            "skipped" => "Runtime operation was skipped safely.",
            _ => "Runtime operation was recorded safely."
        };

    private static string Normalize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return value.Trim().ToLowerInvariant().Replace(' ', '_');
    }

    private static string Clip(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static int? ParseInt(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && int.TryParse(value, out var number) ? number : null;

    private static decimal? ParseDecimal(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && decimal.TryParse(value, out var number) ? number : null;

    private static int SafeCountJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return 0;

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.GetArrayLength() : 0;
        }
        catch
        {
            return 0;
        }
    }
}
