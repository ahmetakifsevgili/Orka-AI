namespace Orka.Core.DTOs;

public sealed class QuestionItemDto
{
    public Guid Id { get; set; }
    public string OwnershipState { get; set; } = "user";
    public string QuestionBankSource { get; set; } = "curated_question_item";
    public Guid ExamDefinitionId { get; set; }
    public Guid? ExamVariantId { get; set; }
    public Guid? ExamSectionId { get; set; }
    public Guid? ExamSubjectId { get; set; }
    public Guid? ExamTopicId { get; set; }
    public Guid? ExamOutcomeId { get; set; }
    public Guid? LearningTopicId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public Guid? LearningConceptId { get; set; }
    public Guid? AssessmentItemId { get; set; }
    public Guid? QuizRunId { get; set; }
    public Guid? PlanRequestId { get; set; }
    public string? ConceptKey { get; set; }
    public string? ConceptLabel { get; set; }
    public string? MisconceptionTarget { get; set; }
    public string? EvidenceExpected { get; set; }
    public string? ScoringRuleJson { get; set; }
    public string? CalibrationStatus { get; set; }
    public string VisualReadinessStatus { get; set; } = "not_required";
    public string QuestionType { get; set; } = "multiple_choice";
    public string Stem { get; set; } = string.Empty;
    public string Difficulty { get; set; } = "medium";
    public string CognitiveSkill { get; set; } = "conceptual";
    public string QualityStatus { get; set; } = "draft";
    public string LicenseStatus { get; set; } = "unknown";
    public string SourceOrigin { get; set; } = "manual";
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<QuestionOptionDto> Options { get; set; } = [];
    public List<QuestionExplanationDto> Explanations { get; set; } = [];
    public List<QuestionTagDto> Tags { get; set; } = [];
    public List<QuestionOutcomeLinkDto> OutcomeLinks { get; set; } = [];
    public List<QuestionContentBlockDto> ContentBlocks { get; set; } = [];
    public List<QuestionStimulusDto> Stimuli { get; set; } = [];
    public QuestionValidationResultDto Validation { get; set; } = new();
}

public sealed class QuestionOptionDto
{
    public Guid? Id { get; set; }
    public string OptionKey { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public string? Rationale { get; set; }
    public string? MisconceptionKey { get; set; }
    public string? DiagnosticSignalJson { get; set; }
    public int SortOrder { get; set; }
    public List<QuestionOptionContentBlockDto> ContentBlocks { get; set; } = [];
}

public sealed class QuestionExplanationDto
{
    public Guid? Id { get; set; }
    public string ExplanationText { get; set; } = string.Empty;
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public string Visibility { get; set; } = "authoring";
    public bool IsSafeForLearners { get; set; } = true;
}

public sealed class QuestionTagDto
{
    public Guid? Id { get; set; }
    public string Tag { get; set; } = string.Empty;
}

public sealed class QuestionOutcomeLinkDto
{
    public Guid? Id { get; set; }
    public Guid ExamOutcomeId { get; set; }
    public bool IsPrimary { get; set; }
    public decimal LinkStrength { get; set; } = 1.0m;
}

public sealed class CreateQuestionDto
{
    public Guid ExamDefinitionId { get; set; }
    public Guid? ExamVariantId { get; set; }
    public Guid? ExamSectionId { get; set; }
    public Guid? ExamSubjectId { get; set; }
    public Guid? ExamTopicId { get; set; }
    public Guid? ExamOutcomeId { get; set; }
    public Guid? LearningTopicId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public Guid? LearningConceptId { get; set; }
    public Guid? AssessmentItemId { get; set; }
    public Guid? QuizRunId { get; set; }
    public Guid? PlanRequestId { get; set; }
    public string? ConceptKey { get; set; }
    public string? ConceptLabel { get; set; }
    public string? MisconceptionTarget { get; set; }
    public string? EvidenceExpected { get; set; }
    public string? ScoringRuleJson { get; set; }
    public string? CalibrationStatus { get; set; }
    public string? VisualReadinessStatus { get; set; }
    public string? QuestionBankSource { get; set; }
    public string QuestionType { get; set; } = "multiple_choice";
    public string Stem { get; set; } = string.Empty;
    public string Difficulty { get; set; } = "medium";
    public string CognitiveSkill { get; set; } = "conceptual";
    public string LicenseStatus { get; set; } = "unknown";
    public string SourceOrigin { get; set; } = "manual";
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public List<QuestionOptionDto> Options { get; set; } = [];
    public List<QuestionExplanationDto> Explanations { get; set; } = [];
    public List<QuestionTagDto> Tags { get; set; } = [];
    public List<QuestionOutcomeLinkDto> OutcomeLinks { get; set; } = [];
    public List<CreateQuestionContentBlockDto> ContentBlocks { get; set; } = [];
    public List<QuestionStimulusLinkDto> Stimuli { get; set; } = [];
}

public sealed class UpdateQuestionDto
{
    public Guid? ExamVariantId { get; set; }
    public Guid? ExamSectionId { get; set; }
    public Guid? ExamSubjectId { get; set; }
    public Guid? ExamTopicId { get; set; }
    public Guid? ExamOutcomeId { get; set; }
    public Guid? LearningTopicId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public Guid? LearningConceptId { get; set; }
    public Guid? AssessmentItemId { get; set; }
    public Guid? QuizRunId { get; set; }
    public Guid? PlanRequestId { get; set; }
    public string? ConceptKey { get; set; }
    public string? ConceptLabel { get; set; }
    public string? MisconceptionTarget { get; set; }
    public string? EvidenceExpected { get; set; }
    public string? ScoringRuleJson { get; set; }
    public string? CalibrationStatus { get; set; }
    public string? VisualReadinessStatus { get; set; }
    public string? QuestionBankSource { get; set; }
    public string? QuestionType { get; set; }
    public string? Stem { get; set; }
    public string? Difficulty { get; set; }
    public string? CognitiveSkill { get; set; }
    public string? QualityStatus { get; set; }
    public string? LicenseStatus { get; set; }
    public string? SourceOrigin { get; set; }
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public string? Explanation { get; set; }
    public List<QuestionOptionDto>? Options { get; set; }
    public List<QuestionExplanationDto>? Explanations { get; set; }
    public List<QuestionTagDto>? Tags { get; set; }
    public List<QuestionOutcomeLinkDto>? OutcomeLinks { get; set; }
    public List<CreateQuestionContentBlockDto>? ContentBlocks { get; set; }
    public List<QuestionStimulusLinkDto>? Stimuli { get; set; }
}

public sealed class QuestionBankFilterDto
{
    public Guid? ExamDefinitionId { get; set; }
    public Guid? ExamVariantId { get; set; }
    public Guid? ExamSectionId { get; set; }
    public Guid? ExamSubjectId { get; set; }
    public Guid? ExamTopicId { get; set; }
    public Guid? ExamOutcomeId { get; set; }
    public Guid? LearningTopicId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public Guid? LearningConceptId { get; set; }
    public Guid? AssessmentItemId { get; set; }
    public Guid? QuizRunId { get; set; }
    public Guid? PlanRequestId { get; set; }
    public string? ConceptKey { get; set; }
    public bool IncludeDiagnosticItems { get; set; } = true;
    public string? QualityStatus { get; set; }
    public string? QuestionType { get; set; }
    public string? Difficulty { get; set; }
    public int Take { get; set; } = 50;
}

public sealed class QuestionValidationResultDto
{
    public bool IsValid { get; set; } = true;
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<QuestionAccessibilityValidationDto> Accessibility { get; set; } = [];
}

public sealed class QuestionAssetDto
{
    public Guid Id { get; set; }
    public string OwnershipState { get; set; } = "user";
    public string AssetType { get; set; } = "image";
    public string StorageKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;
    public Guid? SourceRegistryItemId { get; set; }
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public string LicenseStatus { get; set; } = "unknown";
    public string VerificationStatus { get; set; } = "unverified";
    public string? AltText { get; set; }
    public string? Caption { get; set; }
    public string? LongDescription { get; set; }
    public string? GenerationProvider { get; set; }
    public string? GenerationModel { get; set; }
    public string? RenderStrategy { get; set; }
    public string? GenerationPromptHash { get; set; }
    public string? ValidationReportJson { get; set; }
    public string VisualReadinessStatus { get; set; } = "needs_validation";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class CreateQuestionAssetDto
{
    public string AssetType { get; set; } = "image";
    public string StorageKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;
    public Guid? SourceRegistryItemId { get; set; }
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public string LicenseStatus { get; set; } = "unknown";
    public string VerificationStatus { get; set; } = "unverified";
    public string? AltText { get; set; }
    public string? Caption { get; set; }
    public string? LongDescription { get; set; }
    public string? GenerationProvider { get; set; }
    public string? GenerationModel { get; set; }
    public string? RenderStrategy { get; set; }
    public string? GenerationPromptHash { get; set; }
    public string? ValidationReportJson { get; set; }
    public string VisualReadinessStatus { get; set; } = "needs_validation";
}

public sealed class QuestionContentBlockDto
{
    public Guid? Id { get; set; }
    public string BlockType { get; set; } = "text";
    public string? Text { get; set; }
    public string? ContentJson { get; set; }
    public Guid? AssetId { get; set; }
    public QuestionAssetDto? Asset { get; set; }
    public int SortOrder { get; set; }
    public string? AltText { get; set; }
    public string? Caption { get; set; }
    public string? LongDescription { get; set; }
}

public sealed class CreateQuestionContentBlockDto
{
    public string BlockType { get; set; } = "text";
    public string? Text { get; set; }
    public string? ContentJson { get; set; }
    public Guid? AssetId { get; set; }
    public int SortOrder { get; set; }
    public string? AltText { get; set; }
    public string? Caption { get; set; }
    public string? LongDescription { get; set; }
}

public sealed class QuestionOptionContentBlockDto
{
    public Guid? Id { get; set; }
    public string BlockType { get; set; } = "text";
    public string? Text { get; set; }
    public string? ContentJson { get; set; }
    public Guid? AssetId { get; set; }
    public QuestionAssetDto? Asset { get; set; }
    public int SortOrder { get; set; }
    public string? AltText { get; set; }
    public string? Caption { get; set; }
}

public sealed class CreateQuestionOptionContentBlockDto
{
    public string BlockType { get; set; } = "text";
    public string? Text { get; set; }
    public string? ContentJson { get; set; }
    public Guid? AssetId { get; set; }
    public int SortOrder { get; set; }
    public string? AltText { get; set; }
    public string? Caption { get; set; }
}

public sealed class QuestionStimulusDto
{
    public Guid Id { get; set; }
    public string OwnershipState { get; set; } = "user";
    public string Title { get; set; } = string.Empty;
    public string StimulusType { get; set; } = "passage";
    public string? ContentText { get; set; }
    public string? ContentJson { get; set; }
    public Guid? SourceRegistryItemId { get; set; }
    public Guid? CurriculumNodeId { get; set; }
    public string VerificationStatus { get; set; } = "unverified";
    public string LicenseStatus { get; set; } = "unknown";
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class CreateQuestionStimulusDto
{
    public string Title { get; set; } = string.Empty;
    public string StimulusType { get; set; } = "passage";
    public string? ContentText { get; set; }
    public string? ContentJson { get; set; }
    public Guid? SourceRegistryItemId { get; set; }
    public Guid? CurriculumNodeId { get; set; }
    public string VerificationStatus { get; set; } = "unverified";
    public string LicenseStatus { get; set; } = "unknown";
}

public sealed class QuestionStimulusLinkDto
{
    public Guid QuestionStimulusId { get; set; }
    public int SortOrder { get; set; }
}

public sealed class QuestionPracticeStartRequestDto
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public Guid? PlanRequestId { get; set; }
    public Guid? QuizRunId { get; set; }
    public List<Guid> LearningConceptIds { get; set; } = [];
    public List<Guid> AssessmentItemIds { get; set; } = [];
    public List<string> ConceptKeys { get; set; } = [];
    public string? QuestionBankSource { get; set; }
    public string Mode { get; set; } = "weak_concept_drill";
    public int Count { get; set; } = 8;
}

public sealed class QuestionPracticeSessionDto
{
    public Guid PracticeSetId { get; set; } = Guid.NewGuid();
    public string Status { get; set; } = "ready";
    public string EmptyState { get; set; } = string.Empty;
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string Mode { get; set; } = "weak_concept_drill";
    public List<string> ConceptKeys { get; set; } = [];
    public int TotalQuestions { get; set; }
    public List<QuestionPracticeQuestionDto> Questions { get; set; } = [];
}

public sealed class QuestionPracticeQuestionDto
{
    public Guid QuestionItemId { get; set; }
    public Guid? AssessmentItemId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public Guid? LearningConceptId { get; set; }
    public Guid? QuizRunId { get; set; }
    public Guid? PlanRequestId { get; set; }
    public Guid? TopicId { get; set; }
    public string? ConceptKey { get; set; }
    public string? ConceptLabel { get; set; }
    public string QuestionBankSource { get; set; } = "curated_question_item";
    public string QuestionType { get; set; } = "multiple_choice";
    public string Stem { get; set; } = string.Empty;
    public string Difficulty { get; set; } = "medium";
    public string CognitiveSkill { get; set; } = "conceptual";
    public string? MisconceptionTarget { get; set; }
    public string? EvidenceExpected { get; set; }
    public string VisualReadinessStatus { get; set; } = "not_required";
    public List<QuestionOptionDto> Options { get; set; } = [];
    public List<QuestionContentBlockDto> ContentBlocks { get; set; } = [];
    public List<QuestionStimulusDto> Stimuli { get; set; } = [];
}

public sealed class QuestionPracticeSubmitRequestDto
{
    public Guid? PracticeSetId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string Mode { get; set; } = "weak_concept_drill";
    public List<QuestionPracticeAnswerDto> Answers { get; set; } = [];
}

public sealed class QuestionPracticeAnswerDto
{
    public Guid QuestionItemId { get; set; }
    public string? SelectedOptionKey { get; set; }
    public int? ResponseTimeMs { get; set; }
    public bool WasSkipped { get; set; }
    public decimal? ConfidenceSelfRating { get; set; }
}

public sealed class QuestionPracticeSubmitResponseDto
{
    public Guid PracticeSetId { get; set; }
    public string Status { get; set; } = "submitted";
    public int TotalQuestions { get; set; }
    public int AnsweredCount { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public int BlankCount { get; set; }
    public List<QuestionPracticeResultDto> Results { get; set; } = [];
    public List<QuizResultLearningImpactDto> LearningImpacts { get; set; } = [];
}

public sealed class QuestionPracticeResultDto
{
    public Guid QuestionItemId { get; set; }
    public Guid? AssessmentItemId { get; set; }
    public string? ConceptKey { get; set; }
    public string SelectedOptionKey { get; set; } = string.Empty;
    public bool IsBlank { get; set; }
    public bool IsCorrect { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public QuizResultLearningImpactDto? LearningImpact { get; set; }
}

public sealed class QuestionAccessibilityValidationDto
{
    public string TargetType { get; set; } = string.Empty;
    public Guid? TargetId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = "warning";
}
