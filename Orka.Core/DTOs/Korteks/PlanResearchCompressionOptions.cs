namespace Orka.Core.DTOs.Korteks;

public sealed class PlanResearchCompressionOptions
{
    public int MaxSources { get; set; } = 5;
    public int MaxKeyFacts { get; set; } = 8;
    public int MaxFreshnessFacts { get; set; } = 5;
    public int MaxYouTubeReferences { get; set; } = 5;
    public int MaxCurriculumHints { get; set; } = 5;
    public int MaxPrerequisiteHints { get; set; } = 5;
    public int MaxMisconceptions { get; set; } = 5;
    public int MaxItemLength { get; set; } = 280;
    public int MaxTotalChars { get; set; } = 6000;
}
