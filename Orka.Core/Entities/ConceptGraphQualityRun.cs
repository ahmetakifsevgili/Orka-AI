namespace Orka.Core.Entities;

public sealed class ConceptGraphQualityRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid ConceptGraphSnapshotId { get; set; }
    public Guid? PlanRequestId { get; set; }
    public string QualityStatus { get; set; } = "unknown";
    public int ConceptCount { get; set; }
    public decimal DuplicateRatio { get; set; }
    public bool HasPrerequisiteCycle { get; set; }
    public int OrphanConceptCount { get; set; }
    public decimal OutcomeCoverage { get; set; }
    public decimal MisconceptionCoverage { get; set; }
    public decimal SourceEvidenceRatio { get; set; }
    public decimal RelationDensity { get; set; }
    public string FailuresJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Topic? Topic { get; set; }
    public ConceptGraphSnapshot ConceptGraphSnapshot { get; set; } = null!;
}
