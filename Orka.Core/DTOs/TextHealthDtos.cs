namespace Orka.Core.DTOs;

public sealed class TextHealthFindingDto
{
    public string Table { get; set; } = string.Empty;
    public string Column { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Marker { get; set; } = string.Empty;
    public string Sample { get; set; } = string.Empty;
    public int OccurrenceCount { get; set; }
    public bool Repairable { get; set; }
}

public sealed class TextHealthReportDto
{
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool HasIssues { get; set; }
    public int ScannedValueCount { get; set; }
    public IReadOnlyList<TextHealthFindingDto> Findings { get; set; } = Array.Empty<TextHealthFindingDto>();
    public string Mode { get; set; } = "dry-run";
}

public sealed class TextHealthRepairResultDto
{
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public int ScannedValueCount { get; set; }
    public int RepairedValueCount { get; set; }
    public IReadOnlyList<TextHealthFindingDto> RemainingFindings { get; set; } = Array.Empty<TextHealthFindingDto>();
    public bool RepairEnabled { get; set; }
}
