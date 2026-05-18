namespace Orka.Core.Entities;

public sealed class AssessmentQualitySnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? QuizRunId { get; set; }
    public Guid? AssessmentDraftId { get; set; }
    public Guid? PlanQualitySnapshotId { get; set; }
    public Guid? ActiveLessonSnapshotId { get; set; }
    public Guid? StudentContextSnapshotId { get; set; }
    public string QualityStatus { get; set; } = "insufficient";
    public decimal ConceptCoverageScore { get; set; }
    public decimal MisconceptionTargetingScore { get; set; }
    public decimal DistractorQualityScore { get; set; }
    public decimal LeakageSafetyScore { get; set; }
    public decimal RemediationAlignmentScore { get; set; }
    public string BlockingIssuesJson { get; set; } = "[]";
    public string WarningIssuesJson { get; set; } = "[]";
    public string AssessmentContractJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public User User { get; set; } = null!;
    public Topic? Topic { get; set; }
    public Session? Session { get; set; }
    public QuizRun? QuizRun { get; set; }
    public LearningPlanQualitySnapshot? PlanQualitySnapshot { get; set; }
    public ActiveLessonSnapshot? ActiveLessonSnapshot { get; set; }
    public StudentContextSnapshot? StudentContextSnapshot { get; set; }
}
