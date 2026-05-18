namespace Orka.Core.DTOs;

public sealed class LearningRuntimeTraceDto
{
    public Guid Id { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string Category { get; set; } = "unknown";
    public string Operation { get; set; } = "unknown";
    public string Status { get; set; } = "unknown";
    public string Severity { get; set; } = "info";
    public string SafeMessage { get; set; } = string.Empty;
    public long? LatencyMs { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
    public string CostStatus { get; set; } = "not_available";
    public string? FallbackReason { get; set; }
    public string? ErrorCode { get; set; }
    public bool IsDegraded { get; set; }
    public bool IsDenied { get; set; }
    public bool FallbackUsed { get; set; }
    public int EvidenceCount { get; set; }
    public int ToolCount { get; set; }
    public int ArtifactCount { get; set; }
    public int SourceCount { get; set; }
    public IReadOnlyDictionary<string, string> TraceLinks { get; set; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> SafeMetadata { get; set; } = new Dictionary<string, string>();
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class LearningRuntimeTracesResponseDto
{
    public IReadOnlyList<LearningRuntimeTraceDto> Traces { get; set; } = Array.Empty<LearningRuntimeTraceDto>();
    public int Count { get; set; }
    public string Contract { get; set; } = "learning_runtime_trace_v1";
}

public sealed class LearningRuntimeCostDto
{
    public string Status { get; set; } = "not_available";
    public int? TotalTokens { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
    public string UserSafeMessage { get; set; } = "Cost data is not available for this runtime slice.";
}

public sealed class LearningRuntimeServiceHealthDto
{
    public string Service { get; set; } = "unknown";
    public string Status { get; set; } = "unknown";
    public int TraceCount { get; set; }
    public int DegradedCount { get; set; }
    public int FailedCount { get; set; }
    public long? AverageLatencyMs { get; set; }
    public string UserSafeMessage { get; set; } = string.Empty;
}

public sealed class LearningRuntimeHealthDto
{
    public string Status { get; set; } = "unknown";
    public int TraceCount { get; set; }
    public int CorrelatedTraceCount { get; set; }
    public int MissingCorrelationCount { get; set; }
    public int DegradedCount { get; set; }
    public int DeniedCount { get; set; }
    public int FailedCount { get; set; }
    public int FallbackCount { get; set; }
    public LearningRuntimeCostDto CostSummary { get; set; } = new();
    public IReadOnlyList<LearningRuntimeServiceHealthDto> Services { get; set; } = Array.Empty<LearningRuntimeServiceHealthDto>();
    public IReadOnlyList<string> UserSafeWarnings { get; set; } = Array.Empty<string>();
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

public sealed class LearningRuntimeCorrelationDto
{
    public string CorrelationId { get; set; } = string.Empty;
    public string Status { get; set; } = "unknown";
    public IReadOnlyList<string> ParticipatedServices { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> DegradedServices { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> FallbackReasons { get; set; } = Array.Empty<string>();
    public LearningRuntimeCostDto CostSummary { get; set; } = new();
    public IReadOnlyList<LearningRuntimeTraceDto> Traces { get; set; } = Array.Empty<LearningRuntimeTraceDto>();
    public IReadOnlyList<string> UserSafeWarnings { get; set; } = Array.Empty<string>();
}

public sealed class LearningRuntimeFlowSummaryDto
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime? LatestTraceAt { get; set; }
    public string Status { get; set; } = "unknown";
    public IReadOnlyList<string> ParticipatedServices { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> DegradedServices { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> FallbackReasons { get; set; } = Array.Empty<string>();
    public LearningRuntimeCostDto CostSummary { get; set; } = new();
    public string PlanQuizTutorSyncStatus { get; set; } = "unknown";
    public int EvidenceCount { get; set; }
    public int ToolCount { get; set; }
    public int ArtifactCount { get; set; }
    public int SourceCount { get; set; }
    public IReadOnlyList<string> UserSafeWarnings { get; set; } = Array.Empty<string>();
}

public sealed class LearningRuntimePrivacyCheckRequestDto
{
    public string? MetadataJson { get; set; }
    public IReadOnlyDictionary<string, string>? Metadata { get; set; }
}

public sealed class LearningRuntimePrivacyCheckDto
{
    public string Status { get; set; } = "ready";
    public bool IsSafe { get; set; } = true;
    public IReadOnlyList<string> BlockedTerms { get; set; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> SafeMetadata { get; set; } = new Dictionary<string, string>();
    public string UserSafeMessage { get; set; } = "Telemetry metadata is bounded and public-safe.";
}

public sealed class LearningRuntimeEventRequestDto
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string? CorrelationId { get; set; }
    public string Category { get; set; } = "learning_event";
    public string Operation { get; set; } = "runtime_event";
    public string Status { get; set; } = "succeeded";
    public string Severity { get; set; } = "info";
    public string? SafeMessage { get; set; }
    public long? LatencyMs { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
    public string? FallbackReason { get; set; }
    public string? ErrorCode { get; set; }
    public IReadOnlyDictionary<string, string>? SafeMetadata { get; set; }
}
