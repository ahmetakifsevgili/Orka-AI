namespace Orka.Core.DTOs;

public sealed record ToolCapabilityDto(
    string ToolId,
    string DisplayName,
    string Category,
    string Status,
    string RiskLevel,
    bool RequiresAuth,
    bool RequiresAdmin,
    bool RequiresExternalProvider,
    string? ConfigKey,
    int TimeoutMs,
    bool CostTracked,
    bool TelemetryEnabled,
    string FallbackMode,
    string InputSchema,
    string OutputSchema,
    string Decision,
    string Notes);

public sealed record ToolExecutionResultDto(
    bool Success,
    string ToolId,
    string Status,
    string ResultType,
    string? Content,
    IReadOnlyList<string> Citations,
    IReadOnlyDictionary<string, string> Metadata,
    string? ErrorCode,
    string? SafeUserMessage,
    long LatencyMs,
    string? Provider,
    string CorrelationId);

public sealed record GroundingContractDto(
    string GroundingMode,
    IReadOnlyList<string> SourcePriority,
    IReadOnlyList<string> Citations,
    bool UnsupportedClaims,
    bool CitationMissing,
    bool FallbackUsed);
