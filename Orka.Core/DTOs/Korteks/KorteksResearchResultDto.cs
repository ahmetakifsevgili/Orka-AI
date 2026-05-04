using System;
using System.Collections.Generic;
using Orka.Core.Enums;

namespace Orka.Core.DTOs.Korteks;

public sealed class KorteksResearchResultDto
{
    public string Topic { get; set; } = string.Empty;
    public Guid? TopicId { get; set; }
    public string Report { get; set; } = string.Empty;
    public GroundingMode GroundingMode { get; set; }
    public List<SourceEvidenceDto> Sources { get; set; } = [];
    public List<ToolCallEvidenceDto> ProviderCalls { get; set; } = [];
    public List<string> ProviderFailures { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public bool IsFallback { get; set; }
    public int SourceCount => Sources.Count;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
