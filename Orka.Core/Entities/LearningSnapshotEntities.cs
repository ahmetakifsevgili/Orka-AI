namespace Orka.Core.Entities;

public sealed class ActiveLessonSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? PlanRequestId { get; set; }
    public Guid? QuizRunId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string? SourceBundleHash { get; set; }
    public int SnapshotVersion { get; set; } = 1;
    public string Status { get; set; } = "active";
    public string? ActiveConceptKey { get; set; }
    public string? ActiveConceptLabel { get; set; }
    public string? ApprovedIntent { get; set; }
    public string? ApprovedMainTopic { get; set; }
    public string? ApprovedFocusArea { get; set; }
    public string? ApprovedStudyGoal { get; set; }
    public string? GroundingMode { get; set; }
    public int SourceEvidenceCount { get; set; }
    public int WikiEvidenceCount { get; set; }
    public int ToolEvidenceCount { get; set; }
    public int RecentAttemptCount { get; set; }
    public int WeakConceptCount { get; set; }
    public string RemediationNeed { get; set; } = "none";
    public string LearnerState { get; set; } = "unknown";
    public decimal? Confidence { get; set; }
    public decimal? MasteryProbability { get; set; }
    public string? EvidenceSummaryJson { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public bool IsDeleted { get; set; }

    public User User { get; set; } = null!;
    public Topic? Topic { get; set; }
    public Session? Session { get; set; }
    public QuizRun? QuizRun { get; set; }
    public ConceptGraphSnapshot? ConceptGraphSnapshot { get; set; }
}

public sealed class StudentContextSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public int SnapshotVersion { get; set; } = 1;
    public string ConfidenceStatus { get; set; } = "none";
    public string StrongConceptsJson { get; set; } = "[]";
    public string WeakConceptsJson { get; set; } = "[]";
    public string RecentMisconceptionsJson { get; set; } = "[]";
    public string RemediationReadyJson { get; set; } = "[]";
    public string ReviewPressureJson { get; set; } = "[]";
    public string? SourceReadiness { get; set; }
    public string? GoalReadinessJson { get; set; }
    public string? LearningMemoryJson { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public bool IsDeleted { get; set; }

    public User User { get; set; } = null!;
    public Topic? Topic { get; set; }
    public Session? Session { get; set; }
}
