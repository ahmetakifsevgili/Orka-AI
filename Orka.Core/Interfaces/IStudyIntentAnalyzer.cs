using Orka.Core.DTOs.PlanDiagnostic;

namespace Orka.Core.Interfaces;

public interface IStudyIntentAnalyzer
{
    Task<StudyIntentPreviewResponse> AnalyzeAsync(
        Guid userId,
        AnalyzeStudyIntentRequest request,
        CancellationToken ct = default);
}
