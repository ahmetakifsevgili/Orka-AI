namespace Orka.Core.DTOs;

public sealed class ActiveLessonSnapshotDto
{
    public Guid Id { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? PlanRequestId { get; set; }
    public Guid? QuizRunId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string? SourceBundleHash { get; set; }
    public int SnapshotVersion { get; set; }
    public string Status { get; set; } = "active";
    public string? ActiveConceptKey { get; set; }
    public string? ActiveConceptLabel { get; set; }
    public string? ApprovedIntent { get; set; }
    public string? ApprovedMainTopic { get; set; }
    public string? ApprovedFocusArea { get; set; }
    public string? ApprovedStudyGoal { get; set; }
    public string? GroundingMode { get; set; }
    public LearningSnapshotEvidenceSummaryDto EvidenceSummary { get; set; } = new();
    public string RemediationNeed { get; set; } = "none";
    public string LearnerState { get; set; } = "unknown";
    public decimal? Confidence { get; set; }
    public decimal? MasteryProbability { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public sealed class StudentContextSnapshotDto
{
    public Guid Id { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public int SnapshotVersion { get; set; }
    public string ConfidenceStatus { get; set; } = "none";
    public IReadOnlyList<LearningSnapshotConceptDto> StrongConcepts { get; set; } = Array.Empty<LearningSnapshotConceptDto>();
    public IReadOnlyList<LearningSnapshotConceptDto> WeakConcepts { get; set; } = Array.Empty<LearningSnapshotConceptDto>();
    public IReadOnlyList<LearningSnapshotConceptDto> RecentMisconceptions { get; set; } = Array.Empty<LearningSnapshotConceptDto>();
    public IReadOnlyList<LearningSnapshotRemediationDto> RemediationReady { get; set; } = Array.Empty<LearningSnapshotRemediationDto>();
    public IReadOnlyList<string> ReviewPressure { get; set; } = Array.Empty<string>();
    public string SourceReadiness { get; set; } = "unknown";
    public GoalReadinessDto GoalReadiness { get; set; } = new();
    public string LearningMemorySummary { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public sealed class ActiveLessonSnapshotRequestDto
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? PlanRequestId { get; set; }
    public Guid? QuizRunId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string? SourceBundleHash { get; set; }
    public string? ApprovedIntent { get; set; }
    public string? ApprovedMainTopic { get; set; }
    public string? ApprovedFocusArea { get; set; }
    public string? ApprovedStudyGoal { get; set; }
    public string? GroundingMode { get; set; }
}

public sealed class StudentContextSnapshotRequestDto
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
}

public sealed class LearningSnapshotEvidenceSummaryDto
{
    public int SourceEvidenceCount { get; set; }
    public int WikiEvidenceCount { get; set; }
    public int ToolEvidenceCount { get; set; }
    public int RecentAttemptCount { get; set; }
    public int WeakConceptCount { get; set; }
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
}

public sealed class LearningSnapshotConceptDto
{
    public Guid? TopicId { get; set; }
    public string ConceptKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal? MasteryProbability { get; set; }
    public decimal? Confidence { get; set; }
    public string ConfidenceStatus { get; set; } = "observed_only";
    public string UserSafeReason { get; set; } = "Bu kavram icin sinyal izleniyor.";
    public IReadOnlyList<string> EvidenceBasis { get; set; } = Array.Empty<string>();
}

public sealed class LearningSnapshotRemediationDto
{
    public Guid? TopicId { get; set; }
    public string ConceptKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Reason { get; set; } = "Kisa telafi onerilir.";
    public decimal? Confidence { get; set; }
    public string ConfidenceStatus { get; set; } = "observed_only";
    public string FirstAction { get; set; } = "tutor_explain";
    public IReadOnlyList<string> SecondaryActions { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> EvidenceBasis { get; set; } = Array.Empty<string>();
}
