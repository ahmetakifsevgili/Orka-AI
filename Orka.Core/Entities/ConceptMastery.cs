namespace Orka.Core.Entities;

public class ConceptMastery
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? TopicId { get; set; }
    public Topic? Topic { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public ConceptGraphSnapshot? ConceptGraphSnapshot { get; set; }
    public string ConceptKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal MasteryScore { get; set; }
    public decimal Confidence { get; set; }
    public string RemediationNeed { get; set; } = "none";
    public string PracticeReadiness { get; set; } = "guided";
    public string MisconceptionEvidenceJson { get; set; } = "[]";
    public int Attempts { get; set; }
    public int Correct { get; set; }
    public DateTime? LastEvidenceAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
