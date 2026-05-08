namespace Orka.Core.Entities;

public class ConceptRelation
{
    public Guid Id { get; set; }
    public Guid ConceptGraphSnapshotId { get; set; }
    public ConceptGraphSnapshot ConceptGraphSnapshot { get; set; } = null!;
    public string SourceConceptKey { get; set; } = string.Empty;
    public string TargetConceptKey { get; set; } = string.Empty;
    public string RelationType { get; set; } = "prerequisite";
    public double Weight { get; set; } = 1.0;
    public string? MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
