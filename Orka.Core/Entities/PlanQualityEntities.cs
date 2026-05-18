namespace Orka.Core.Entities;

public sealed class LearningPlanQualitySnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? PlanRequestId { get; set; }
    public Guid? ActiveLessonSnapshotId { get; set; }
    public Guid? StudentContextSnapshotId { get; set; }
    public string QualityStatus { get; set; } = "insufficient";
    public decimal SpecificityScore { get; set; }
    public decimal SequencingScore { get; set; }
    public decimal EvidenceAlignmentScore { get; set; }
    public decimal AssessmentAlignmentScore { get; set; }
    public decimal TutorAlignmentScore { get; set; }
    public string BlockingIssuesJson { get; set; } = "[]";
    public string WarningIssuesJson { get; set; } = "[]";
    public string PlanContractJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public User User { get; set; } = null!;
    public Topic? Topic { get; set; }
    public Session? Session { get; set; }
    public ActiveLessonSnapshot? ActiveLessonSnapshot { get; set; }
    public StudentContextSnapshot? StudentContextSnapshot { get; set; }
}
