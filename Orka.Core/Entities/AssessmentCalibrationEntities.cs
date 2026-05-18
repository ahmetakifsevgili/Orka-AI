namespace Orka.Core.Entities;

public sealed class AssessmentCalibrationRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string CalibrationStatus { get; set; } = "unknown";
    public string AdaptiveReadiness { get; set; } = "unknown";
    public string ItemBankHealth { get; set; } = "unknown";
    public int ItemCount { get; set; }
    public int HealthyItemCount { get; set; }
    public int ConceptCount { get; set; }
    public int ReadyConceptCount { get; set; }
    public decimal AverageDifficulty { get; set; }
    public decimal AverageDiscrimination { get; set; }
    public decimal AverageExposure { get; set; }
    public string ReportJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Topic? Topic { get; set; }
    public ConceptGraphSnapshot? ConceptGraphSnapshot { get; set; }
}

public sealed class AssessmentCalibrationItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssessmentCalibrationRunId { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid AssessmentItemId { get; set; }
    public string ConceptKey { get; set; } = string.Empty;
    public decimal DifficultyEstimate { get; set; }
    public decimal DiscriminationEstimate { get; set; }
    public int ExposureCount { get; set; }
    public int EvidenceCount { get; set; }
    public string CalibrationStatus { get; set; } = "uncalibrated";
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AssessmentCalibrationRun Run { get; set; } = null!;
    public AssessmentItem AssessmentItem { get; set; } = null!;
}

public sealed class AdaptiveAssessmentSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? QuizRunId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string Status { get; set; } = "active";
    public string TargetConceptsJson { get; set; } = "[]";
    public string StopReason { get; set; } = string.Empty;
    public int MinItems { get; set; } = 8;
    public int MaxItems { get; set; } = 20;
    public int AnsweredCount { get; set; }
    public int CorrectCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public User User { get; set; } = null!;
    public Topic? Topic { get; set; }
    public Session? Session { get; set; }
    public QuizRun? QuizRun { get; set; }
    public ConceptGraphSnapshot? ConceptGraphSnapshot { get; set; }
}

public sealed class AdaptiveAssessmentDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AdaptiveAssessmentSessionId { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid AssessmentItemId { get; set; }
    public string ConceptKey { get; set; } = string.Empty;
    public decimal SelectionScore { get; set; }
    public decimal MasteryProbability { get; set; }
    public decimal MasteryConfidence { get; set; }
    public decimal ItemQualityScore { get; set; }
    public decimal ExposurePenalty { get; set; }
    public string DecisionReason { get; set; } = string.Empty;
    public string AssessmentMode { get; set; } = "retrieval_practice";
    public string SelectedQuestionJson { get; set; } = "{}";
    public bool WasAnswered { get; set; }
    public Guid? QuizAttemptId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AnsweredAt { get; set; }

    public AdaptiveAssessmentSession AdaptiveAssessmentSession { get; set; } = null!;
    public AssessmentItem AssessmentItem { get; set; } = null!;
    public QuizAttempt? QuizAttempt { get; set; }
}

public sealed class TutorTraceProjection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid SessionId { get; set; }
    public Guid? TopicId { get; set; }
    public string StreamKey { get; set; } = string.Empty;
    public string StreamId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string EventGroup { get; set; } = "state";
    public string UserSafeLabel { get; set; } = string.Empty;
    public string UserSafeDetail { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string PayloadJson { get; set; } = "{}";
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Session Session { get; set; } = null!;
    public Topic? Topic { get; set; }
}
