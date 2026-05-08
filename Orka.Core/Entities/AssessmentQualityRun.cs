namespace Orka.Core.Entities;

public sealed class AssessmentQualityRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? AssessmentDraftId { get; set; }
    public Guid? PlanRequestId { get; set; }
    public Guid? QuizRunId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string QualityStatus { get; set; } = "unknown";
    public decimal ConceptCoverage { get; set; }
    public decimal LearningOutcomeCoverage { get; set; }
    public int CognitiveSkillSpread { get; set; }
    public int DifficultySpread { get; set; }
    public decimal MisconceptionTargetingRatio { get; set; }
    public decimal OptionQualityRatio { get; set; }
    public decimal ScoringRulePresenceRatio { get; set; }
    public string FailuresJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Topic? Topic { get; set; }
    public QuizRun? QuizRun { get; set; }
    public ConceptGraphSnapshot? ConceptGraphSnapshot { get; set; }
}
