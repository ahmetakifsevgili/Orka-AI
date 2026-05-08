namespace Orka.Core.Entities;

public class ConceptGraphSnapshot
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? TopicId { get; set; }
    public Topic? Topic { get; set; }
    public Guid? PlanRequestId { get; set; }
    public string IntentHash { get; set; } = string.Empty;
    public string ApprovedResearchIntent { get; set; } = string.Empty;
    public string TopicTitle { get; set; } = string.Empty;
    public string Domain { get; set; } = "general";
    public string SourceConfidence { get; set; } = "low";
    public string SourceBundleHash { get; set; } = string.Empty;
    public string GraphJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<LearningConcept> Concepts { get; set; } = new List<LearningConcept>();
    public ICollection<ConceptRelation> Relations { get; set; } = new List<ConceptRelation>();
    public ICollection<LearningOutcome> Outcomes { get; set; } = new List<LearningOutcome>();
    public ICollection<AssessmentItem> AssessmentItems { get; set; } = new List<AssessmentItem>();
}
