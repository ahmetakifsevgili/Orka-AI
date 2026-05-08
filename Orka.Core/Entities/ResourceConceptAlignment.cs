namespace Orka.Core.Entities;

public sealed class ResourceConceptAlignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid ConceptGraphSnapshotId { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string SourceTitle { get; set; } = string.Empty;
    public string SourceUri { get; set; } = string.Empty;
    public string ConceptKey { get; set; } = string.Empty;
    public string OutcomeKey { get; set; } = string.Empty;
    public decimal AlignmentScore { get; set; }
    public string EvidenceSnippet { get; set; } = string.Empty;
    public string AlignmentStatus { get; set; } = "weak";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Topic? Topic { get; set; }
    public ConceptGraphSnapshot ConceptGraphSnapshot { get; set; } = null!;
}
