namespace Orka.Core.DTOs;

public sealed record ToolTelemetryEventRequest(
    Guid? UserId,
    Guid? SessionId,
    Guid? TopicId,
    string ToolId,
    string CapabilityStatus,
    string? Provider,
    string? Model,
    long LatencyMs,
    bool Success,
    string? ErrorCode,
    bool FallbackUsed,
    string? CorrelationId,
    string? MetadataJson);

public sealed record CostRecordRequest(
    Guid? UserId,
    Guid? SessionId,
    Guid? TopicId,
    Guid? MessageId,
    string AgentRole,
    string? Provider,
    string? Model,
    int EstimatedTokens,
    decimal EstimatedCostUsd,
    bool Success,
    string? ErrorCode,
    string? MetadataJson);
