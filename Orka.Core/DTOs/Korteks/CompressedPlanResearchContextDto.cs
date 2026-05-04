using System;
using System.Collections.Generic;
using Orka.Core.Enums;

namespace Orka.Core.DTOs.Korteks;

public sealed class CompressedPlanResearchContextDto
{
    public string Topic { get; set; } = string.Empty;
    public GroundingMode GroundingMode { get; set; }
    public int SourceCount { get; set; }
    public List<SourceEvidenceDto> TopSources { get; set; } = [];
    public List<string> KeyFacts { get; set; } = [];
    public List<string> WebFreshnessFacts { get; set; } = [];
    public List<string> YouTubeLearningReferences { get; set; } = [];
    public List<string> CurriculumMapHints { get; set; } = [];
    public List<string> PrerequisiteHints { get; set; } = [];
    public List<string> LikelyMisconceptions { get; set; } = [];
    public List<string> ProviderWarnings { get; set; } = [];
    public string? FallbackWarning { get; set; }
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}
