using System.Collections.Generic;
using Orka.Core.Enums;

namespace Orka.Core.DTOs.Korteks;

public sealed class DeepPlanGroundingMetadataDto
{
    public GroundingMode GroundingMode { get; set; }
    public int SourceCount { get; set; }
    public List<SourceEvidenceDto> Sources { get; set; } = [];
    public List<string> ProviderWarnings { get; set; } = [];
    public bool IsFallback { get; set; }
}
