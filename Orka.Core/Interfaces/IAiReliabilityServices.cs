using Orka.Core.DTOs;

namespace Orka.Core.Interfaces;

public interface IAiProviderTelemetryService
{
    Task RecordAsync(AiProviderTelemetryEvent telemetryEvent, CancellationToken ct = default);
    Task<AiProviderTelemetrySummary> GetSummaryAsync(CancellationToken ct = default);
}

public interface IAiUsageBudgetService
{
    Task<AiUsageBudgetDecision> CheckAsync(AiUsageBudgetRequest request, CancellationToken ct = default);
}

public interface IAiProviderCircuitBreaker
{
    bool IsOpen(string provider);
    string GetState(string provider);
    void RecordSuccess(string provider);
    void RecordFailure(string provider, TimeSpan cooldown, int failureThreshold = 1);
}

public interface IAiRequestContextAccessor
{
    AiRequestContext Current { get; }
    IDisposable Push(AiRequestContext context);
}
