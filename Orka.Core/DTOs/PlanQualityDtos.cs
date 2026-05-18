namespace Orka.Core.DTOs;

public sealed class PlanQualityEvaluationRequestDto
{
    public Guid TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? PlanRequestId { get; set; }
    public Guid? ActiveLessonSnapshotId { get; set; }
    public Guid? StudentContextSnapshotId { get; set; }
    public string? PlanTitle { get; set; }
    public string? PlanSummary { get; set; }
    public IReadOnlyList<PlanStepContractDto> ProposedSteps { get; set; } = Array.Empty<PlanStepContractDto>();
}

public sealed class PlanQualityEvaluationDto
{
    public Guid? SnapshotId { get; set; }
    public Guid TopicId { get; set; }
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
    public IReadOnlyList<PlanQualityIssueDto> BlockingIssues { get; set; } = Array.Empty<PlanQualityIssueDto>();
    public IReadOnlyList<PlanQualityIssueDto> WarningIssues { get; set; } = Array.Empty<PlanQualityIssueDto>();
    public PlanCurriculumSequenceDto PlanContract { get; set; } = new();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PlanQualityIssueDto
{
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = "warning";
    public string Message { get; set; } = string.Empty;
    public string? StepId { get; set; }
}

public sealed class PlanCurriculumSequenceDto
{
    public Guid TopicId { get; set; }
    public string TopicTitle { get; set; } = string.Empty;
    public string ConfidenceStatus { get; set; } = "observed_only";
    public string SequenceStatus { get; set; } = "needs_revision";
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public IReadOnlyList<PlanStepContractDto> Steps { get; set; } = Array.Empty<PlanStepContractDto>();
    public PlanSequencingGraphDto SequencingGraph { get; set; } = new();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PlanSequencingGraphDto
{
    public IReadOnlyList<PlanSequencingNodeDto> Nodes { get; set; } = Array.Empty<PlanSequencingNodeDto>();
    public IReadOnlyList<PlanSequencingEdgeDto> Edges { get; set; } = Array.Empty<PlanSequencingEdgeDto>();
}

public sealed class PlanSequencingNodeDto
{
    public string ConceptKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Order { get; set; }
    public string DifficultyBand { get; set; } = "core";
}

public sealed class PlanSequencingEdgeDto
{
    public string SourceConceptKey { get; set; } = string.Empty;
    public string TargetConceptKey { get; set; } = string.Empty;
    public string RelationType { get; set; } = "prerequisite";
    public decimal Weight { get; set; } = 1m;
}

public sealed class PlanStepContractDto
{
    public string StepId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public string ConceptKey { get; set; } = string.Empty;
    public string ConceptLabel { get; set; } = string.Empty;
    public IReadOnlyList<string> PrerequisiteConceptKeys { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> TargetMisconceptions { get; set; } = Array.Empty<string>();
    public string MasteryTarget { get; set; } = "understand";
    public int EstimatedMinutes { get; set; } = 20;
    public string LearnerState { get; set; } = "unknown";
    public string RemediationNeed { get; set; } = "none";
    public string DifficultyBand { get; set; } = "core";
    public string SequenceReason { get; set; } = string.Empty;
    public PlanStepEvidenceDto Evidence { get; set; } = new();
    public PlanStepAssessmentHookDto QuizHook { get; set; } = new();
    public PlanStepTutorHookDto TutorHook { get; set; } = new();
    public PlanStepWikiHookDto WikiHook { get; set; } = new();
    public IReadOnlyList<string> SuccessCriteria { get; set; } = Array.Empty<string>();
    public string NextStepTrigger { get; set; } = "micro_check_passed";
    public string FallbackIfEvidenceWeak { get; set; } = "Run a short diagnostic check before claiming mastery.";
}

public sealed class PlanStepEvidenceDto
{
    public IReadOnlyList<string> EvidenceBasis { get; set; } = Array.Empty<string>();
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public Guid? SourceEvidenceBundleId { get; set; }
    public string? WikiNotebookSectionKey { get; set; }
    public Guid? KorteksWorkflowId { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

public sealed class PlanStepAssessmentHookDto
{
    public string HookType { get; set; } = "diagnostic_check";
    public string ConceptKey { get; set; } = string.Empty;
    public IReadOnlyList<string> TargetMisconceptions { get; set; } = Array.Empty<string>();
    public string DifficultyBand { get; set; } = "core";
    public string UserSafeReason { get; set; } = "Bu adim kisa bir kontrol sorusuyla olculebilir.";
}

public sealed class PlanStepTutorHookDto
{
    public string TutorMove { get; set; } = "explain";
    public string ActiveConceptKey { get; set; } = string.Empty;
    public string? TargetMisconception { get; set; }
    public string UserSafeReason { get; set; } = "Tutor bu kavrami adim adim anlatir.";
}

public sealed class PlanStepWikiHookDto
{
    public string? SectionKey { get; set; }
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string? UserSafeWarning { get; set; }
}

public sealed class PlanReadinessDto
{
    public Guid TopicId { get; set; }
    public string TopicTitle { get; set; } = string.Empty;
    public bool HasConceptGraph { get; set; }
    public bool HasKorteksSynthesis { get; set; }
    public bool HasSourceEvidence { get; set; }
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string LearnerEvidenceStatus { get; set; } = "observed_only";
    public string RecommendedFirstAction { get; set; } = "diagnostic_check";
    public Guid? LatestQualitySnapshotId { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}
