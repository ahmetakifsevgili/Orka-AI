namespace Orka.Core.DTOs;

public sealed class AssessmentBlueprintRequestDto
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? PlanQualitySnapshotId { get; set; }
    public string? PlanStepId { get; set; }
    public string AssessmentMode { get; set; } = "diagnostic_check";
    public string? ConceptKey { get; set; }
    public string? MisconceptionKey { get; set; }
    public int? ItemCountTarget { get; set; }
}

public sealed class AssessmentBlueprintDto
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? PlanQualitySnapshotId { get; set; }
    public string? PlanStepId { get; set; }
    public string AssessmentMode { get; set; } = "diagnostic_check";
    public string UserSafeModeLabel { get; set; } = "Kisa olcum";
    public IReadOnlyList<AssessmentBlueprintConceptDto> TargetConcepts { get; set; } = Array.Empty<AssessmentBlueprintConceptDto>();
    public IReadOnlyList<string> PrerequisiteConceptKeys { get; set; } = Array.Empty<string>();
    public IReadOnlyList<AssessmentMisconceptionTargetDto> MisconceptionTargets { get; set; } = Array.Empty<AssessmentMisconceptionTargetDto>();
    public string DifficultyBand { get; set; } = "core";
    public int ItemCountTarget { get; set; } = 5;
    public IReadOnlyList<string> CognitiveSkillMix { get; set; } = Array.Empty<string>();
    public string EvidenceMode { get; set; } = "evidence_insufficient";
    public string ExplanationRequirement { get; set; } = "required_after_submit";
    public string RemediationRequirement { get; set; } = "safe_hint_after_submit";
    public IReadOnlyList<string> LeakageSafetyRequirements { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

public sealed class AssessmentBlueprintConceptDto
{
    public string ConceptKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Role { get; set; } = "target";
    public string DifficultyBand { get; set; } = "core";
    public string ConfidenceStatus { get; set; } = "observed_only";
}

public sealed class AssessmentMisconceptionTargetDto
{
    public string MisconceptionKey { get; set; } = string.Empty;
    public string UserSafeLabel { get; set; } = "Yanilgi sinyali belirsiz";
    public string ConceptKey { get; set; } = string.Empty;
    public string ConfidenceStatus { get; set; } = "observed_only";
    public string RationaleRequirement { get; set; } = "distractor_rationale_required";
}

public sealed class AssessmentDistractorRationaleDto
{
    public string OptionId { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;
    public string? MisconceptionKey { get; set; }
}

public sealed class AssessmentItemContractDto
{
    public string ItemId { get; set; } = string.Empty;
    public string Stem { get; set; } = string.Empty;
    public string ConceptKey { get; set; } = string.Empty;
    public string CognitiveSkill { get; set; } = "conceptual";
    public string DifficultyBand { get; set; } = "core";
    public string Explanation { get; set; } = string.Empty;
    public IReadOnlyList<string> OptionTexts { get; set; } = Array.Empty<string>();
    public IReadOnlyList<AssessmentDistractorRationaleDto> DistractorRationales { get; set; } = Array.Empty<AssessmentDistractorRationaleDto>();
    public bool PublicDtoContainsCorrectAnswer { get; set; }
}

public sealed class AssessmentQualityEvaluationRequestDto
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? QuizRunId { get; set; }
    public Guid? AssessmentDraftId { get; set; }
    public Guid? PlanQualitySnapshotId { get; set; }
    public Guid? ActiveLessonSnapshotId { get; set; }
    public Guid? StudentContextSnapshotId { get; set; }
    public AssessmentBlueprintDto Blueprint { get; set; } = new();
    public IReadOnlyList<AssessmentItemContractDto> Items { get; set; } = Array.Empty<AssessmentItemContractDto>();
}

public sealed class AssessmentQualityEvaluationDto
{
    public Guid SnapshotId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string QualityStatus { get; set; } = "insufficient";
    public decimal ConceptCoverageScore { get; set; }
    public decimal MisconceptionTargetingScore { get; set; }
    public decimal DistractorQualityScore { get; set; }
    public decimal LeakageSafetyScore { get; set; }
    public decimal RemediationAlignmentScore { get; set; }
    public IReadOnlyList<AssessmentQualityIssueDto> BlockingIssues { get; set; } = Array.Empty<AssessmentQualityIssueDto>();
    public IReadOnlyList<AssessmentQualityIssueDto> WarningIssues { get; set; } = Array.Empty<AssessmentQualityIssueDto>();
    public AssessmentBlueprintDto Blueprint { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AssessmentQualityIssueDto
{
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = "warning";
    public string UserSafeMessage { get; set; } = string.Empty;
    public string? ItemId { get; set; }
}

public sealed class AssessmentRemediationSignalDto
{
    public string ConceptKey { get; set; } = string.Empty;
    public string RemediationNeed { get; set; } = "none";
    public string NextTutorMove { get; set; } = "micro_check";
    public string ConfidenceStatus { get; set; } = "observed_only";
}

public sealed class QuizLearningOutcomeDto
{
    public string ConceptKey { get; set; } = string.Empty;
    public string Result { get; set; } = "unknown";
    public decimal? MasteryProbability { get; set; }
    public string RemediationNeed { get; set; } = "none";
}

public sealed class QuizResultLearningImpactDto
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? QuizRunId { get; set; }
    public Guid? QuizAttemptId { get; set; }
    public Guid? AssessmentItemId { get; set; }
    public string AssessmentMode { get; set; } = "diagnostic_check";
    public string TargetConceptKey { get; set; } = string.Empty;
    public string Result { get; set; } = "unknown";
    public MisconceptionSignalDto? MisconceptionSignal { get; set; }
    public string MisconceptionConfidence { get; set; } = "none";
    public string RemediationNeed { get; set; } = "none";
    public decimal? MasteryDelta { get; set; }
    public decimal? MasteryProbability { get; set; }
    public string NextTutorMove { get; set; } = "continue";
    public string NextPlanAction { get; set; } = "continue_current_step";
    public string? WikiReviewHint { get; set; }
    public string SourceReadiness { get; set; } = "unknown";
    public IReadOnlyList<string> EvidenceBasis { get; set; } = Array.Empty<string>();
}
