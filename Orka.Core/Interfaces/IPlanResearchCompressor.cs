using Orka.Core.DTOs.Korteks;

namespace Orka.Core.Interfaces;

public interface IPlanResearchCompressor
{
    CompressedPlanResearchContextDto Compress(
        KorteksResearchResultDto researchResult,
        PlanResearchCompressionOptions? options = null);

    string BuildPromptBlock(
        CompressedPlanResearchContextDto context,
        PlanResearchCompressionOptions? options = null);
}
