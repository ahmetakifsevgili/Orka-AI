using Orka.Core.DTOs;
using Orka.Core.DTOs.PlanDiagnostic;

namespace Orka.Core.Interfaces;

public interface IPlanDiagnosticService
{
    Task<StartPlanDiagnosticResponse> StartAsync(
        Guid userId,
        StartPlanDiagnosticRequest request,
        CancellationToken ct = default);

    Task<PlanDiagnosticAnswerResponse> RecordAnswerAsync(
        Guid userId,
        Guid planRequestId,
        RecordQuizAttemptRequest request,
        CancellationToken ct = default);

    Task<FinalizePlanDiagnosticResponse> FinalizeAsync(
        Guid userId,
        FinalizePlanDiagnosticRequest request,
        CancellationToken ct = default);
}
