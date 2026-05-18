namespace Orka.Core.DTOs;

public sealed class ToolRuntimeCallerContextDto
{
    public string Caller { get; set; } = "internal";
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? ActiveLessonSnapshotId { get; set; }
    public Guid? StudentContextSnapshotId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public Guid? TutorActionTraceId { get; set; }
}

public sealed class ToolRuntimeRequestDto
{
    public string ToolId { get; set; } = string.Empty;
    public string Caller { get; set; } = "internal";
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? ActiveLessonSnapshotId { get; set; }
    public Guid? StudentContextSnapshotId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public Guid? TutorActionTraceId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = "low";
    public string? InputSummary { get; set; }
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ToolRuntimeDecisionDto
{
    public Guid? TraceId { get; set; }
    public string ToolId { get; set; } = string.Empty;
    public bool Allowed { get; set; }
    public string Decision { get; set; } = "deny";
    public string ReasonCode { get; set; } = "unknown";
    public string UserSafeReason { get; set; } = "Tool is not available for this learning context.";
    public string RequiredEvidenceMode { get; set; } = "none";
    public int? MaxResultCount { get; set; }
    public int? TimeoutMs { get; set; }
    public bool CanGroundClaims { get; set; }
    public bool ShouldWriteTutorMemory { get; set; }
    public bool ShouldWriteEvidence { get; set; }
    public bool ShouldRecordTelemetry { get; set; } = true;
}

public sealed class ToolRuntimeEvidenceDto
{
    public string EvidenceType { get; set; } = "summary";
    public string Label { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? Provider { get; set; }
    public double? Confidence { get; set; }
}

public sealed class ToolRuntimeResultDto
{
    public Guid? TraceId { get; set; }
    public string ToolId { get; set; } = string.Empty;
    public string Caller { get; set; } = "internal";
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? ActiveLessonSnapshotId { get; set; }
    public Guid? StudentContextSnapshotId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public Guid? TutorActionTraceId { get; set; }
    public string Status { get; set; } = "skipped";
    public bool Success { get; set; }
    public string SafeMessage { get; set; } = string.Empty;
    public IReadOnlyList<ToolRuntimeEvidenceDto> EvidenceItems { get; set; } = Array.Empty<ToolRuntimeEvidenceDto>();
    public IReadOnlyList<ProviderCitationDto> Citations { get; set; } = Array.Empty<ProviderCitationDto>();
    public string? FallbackReason { get; set; }
    public double? Confidence { get; set; }
    public int? SourceCount { get; set; }
    public long LatencyMs { get; set; }
    public Guid? TelemetryId { get; set; }
}

public sealed class ToolRuntimeTraceDto
{
    public Guid Id { get; set; }
    public string ToolId { get; set; } = string.Empty;
    public string Caller { get; set; } = "internal";
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? ActiveLessonSnapshotId { get; set; }
    public Guid? StudentContextSnapshotId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public Guid? TutorActionTraceId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string Decision { get; set; } = "deny";
    public string Status { get; set; } = "decided";
    public string RiskLevel { get; set; } = "low";
    public bool CanGroundClaims { get; set; }
    public string? InputSummary { get; set; }
    public string? SafeResultSummary { get; set; }
    public IReadOnlyList<ToolRuntimeEvidenceDto> EvidenceItems { get; set; } = Array.Empty<ToolRuntimeEvidenceDto>();
    public string? FallbackReason { get; set; }
    public string? ErrorCode { get; set; }
    public long LatencyMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class ToolGovernanceSummaryDto
{
    public int TraceCount { get; set; }
    public int AllowedCount { get; set; }
    public int DeniedCount { get; set; }
    public int DegradedCount { get; set; }
    public int EvidenceProducingCount { get; set; }
    public IReadOnlyList<string> LegacyToolPlanes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<ToolRuntimeTraceDto> RecentTraces { get; set; } = Array.Empty<ToolRuntimeTraceDto>();
}
