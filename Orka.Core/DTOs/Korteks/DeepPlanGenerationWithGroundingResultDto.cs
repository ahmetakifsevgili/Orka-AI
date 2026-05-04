using System.Collections.Generic;

namespace Orka.Core.DTOs.Korteks;

public sealed class DeepPlanGenerationWithGroundingResultDto
{
    public List<Orka.Core.Entities.Topic> Topics { get; set; } = [];
    public DeepPlanGroundingMetadataDto? Grounding { get; set; }
}
