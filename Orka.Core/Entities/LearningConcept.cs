namespace Orka.Core.Entities;

public class LearningConcept
{
    public Guid Id { get; set; }
    public Guid ConceptGraphSnapshotId { get; set; }
    public ConceptGraphSnapshot ConceptGraphSnapshot { get; set; } = null!;
    public string StableKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DifficultyBand { get; set; } = "core";
    public int Order { get; set; }
    public string PrerequisitesJson { get; set; } = "[]";
    public string MisconceptionsJson { get; set; } = "[]";
    public string LearningOutcomeKeysJson { get; set; } = "[]";
    public string SourceEvidenceJson { get; set; } = "[]";
    public string? MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
