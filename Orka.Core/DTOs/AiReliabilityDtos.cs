using Orka.Core.Enums;

namespace Orka.Core.DTOs;

public sealed record AiProviderTelemetryEvent(
    DateTime OccurredAt,
    string Provider,
    string? Model,
    string Role,
    string CallKind,
    bool Success,
    AiProviderFailureKind? FailureKind,
    int? StatusCode,
    long LatencyMs,
    int AttemptIndex,
    bool FallbackUsed,
    int EstimatedInputTokens,
    int EstimatedOutputTokens,
    int EstimatedTotalTokens,
    decimal EstimatedCostUsd,
    bool QuotaHit,
    string CircuitState);

public sealed record AiProviderTelemetrySummary(
    int FallbackCount24h,
    int QuotaHitCount24h,
    IReadOnlyDictionary<string, int> FailureKinds24h,
    IReadOnlyDictionary<string, int> CircuitStates);

public sealed record AiUsageBudgetRequest(
    Guid? UserId,
    string Role,
    string Provider,
    string? Model,
    string InputText,
    int MaxOutputTokens);

public sealed record AiUsageBudgetDecision(
    bool Allowed,
    string Reason,
    int EstimatedInputTokens,
    int EstimatedOutputTokens,
    int EstimatedTotalTokens,
    decimal EstimatedCostUsd);

public sealed record AiRequestContext(
    Guid? UserId = null,
    Guid? SessionId = null,
    Guid? TopicId = null,
    string? CorrelationId = null,
    string? Source = null,
    bool IsBackground = false)
{
    public static AiRequestContext Empty { get; } = new();
}
