namespace Orka.Core.Entities;

public class OutcomeAlignment
{
    public Guid Id { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public ConceptGraphSnapshot? ConceptGraphSnapshot { get; set; }
    public Guid? LearningOutcomeId { get; set; }
    public LearningOutcome? LearningOutcome { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string? EntityKey { get; set; }
    public string AlignmentType { get; set; } = "addresses";
    public double Weight { get; set; } = 1.0;
    public string? MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
