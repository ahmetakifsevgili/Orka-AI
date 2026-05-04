using Orka.Core.DTOs.PlanDiagnostic;

namespace Orka.Core.Interfaces;

public interface IPlanDiagnosticStateStore
{
    Task<PlanDiagnosticStateDto?> GetAsync(Guid planRequestId, CancellationToken ct = default);
    Task SaveAsync(PlanDiagnosticStateDto state, CancellationToken ct = default);
    Task DeleteAsync(Guid planRequestId, CancellationToken ct = default);
}
