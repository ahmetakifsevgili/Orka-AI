namespace Orka.Core.Entities;

public class LearningOutcome
{
    public Guid Id { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public ConceptGraphSnapshot? ConceptGraphSnapshot { get; set; }
    public string StableKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? StandardUri { get; set; }
    public string CognitiveLevel { get; set; } = "understand";
    public string? MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
