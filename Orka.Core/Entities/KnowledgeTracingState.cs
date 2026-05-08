namespace Orka.Core.Entities;

public sealed class KnowledgeTracingState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string ConceptKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal PriorMastery { get; set; } = 0.35m;
    public decimal LearnRate { get; set; } = 0.18m;
    public decimal Slip { get; set; } = 0.10m;
    public decimal Guess { get; set; } = 0.20m;
    public decimal Decay { get; set; } = 0.02m;
    public int EvidenceCount { get; set; }
    public int CorrectCount { get; set; }
    public int IncorrectCount { get; set; }
    public decimal MasteryProbability { get; set; } = 0.35m;
    public decimal Confidence { get; set; } = 0.20m;
    public string RemediationNeed { get; set; } = "none";
    public string PracticeReadiness { get; set; } = "guided";
    public DateTime? LastEvidenceAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Topic? Topic { get; set; }
    public ConceptGraphSnapshot? ConceptGraphSnapshot { get; set; }
}
