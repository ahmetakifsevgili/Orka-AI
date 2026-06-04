namespace Orka.Core.DTOs;

public sealed class QuestionImportRequestDto
{
    public List<QuestionImportItemDto> Items { get; set; } = [];
}

public sealed class QuestionImportItemDto
{
    public string? ExternalId { get; set; }
    public Guid? ExamDefinitionId { get; set; }
    public Guid? ExamVariantId { get; set; }
    public Guid? ExamSectionId { get; set; }
    public Guid? ExamSubjectId { get; set; }
    public Guid? ExamTopicId { get; set; }
    public Guid? ExamOutcomeId { get; set; }
    public string? ExamCode { get; set; }
    public string? VariantCode { get; set; }
    public string? SectionCode { get; set; }
    public string? SubjectCode { get; set; }
    public string? TopicCode { get; set; }
    public string? OutcomeCode { get; set; }
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
    public List<QuestionImportOptionDto> Options { get; set; } = [];
    public string Explanation { get; set; } = string.Empty;
    public string Difficulty { get; set; } = "medium";
    public string CognitiveSkill { get; set; } = "conceptual";
    public List<string> Tags { get; set; } = [];
    public string SourceOrigin { get; set; } = "structured_json";
    public string LicenseStatus { get; set; } = "unknown";
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public QuestionImportSourceDto? Source { get; set; }
}

public sealed class QuestionImportOptionDto
{
    public string OptionKey { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public int SortOrder { get; set; }
    public string? Rationale { get; set; }
    public string? MisconceptionKey { get; set; }
    public string? DiagnosticSignalJson { get; set; }
}

public sealed class QuestionImportSourceDto
{
    public string SourceOrigin { get; set; } = "structured_json";
    public string LicenseStatus { get; set; } = "unknown";
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
}

public sealed class QuestionImportPreviewDto
{
    public Guid Id { get; set; }
    public string Status { get; set; } = "pending";
    public string ImportFormat { get; set; } = "structured_json";
    public string? PackageTitle { get; set; }
    public string? PackageVersion { get; set; }
    public int TotalCount { get; set; }
    public int AcceptedCount { get; set; }
    public int RejectedCount { get; set; }
    public int WarningCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public List<QuestionImportAssetDto> Assets { get; set; } = [];
    public List<QuestionImportStimulusDto> Stimuli { get; set; } = [];
    public List<QuestionImportPreviewItemDto> Items { get; set; } = [];
}

public sealed class QuestionImportPreviewItemDto
{
    public Guid Id { get; set; }
    public int RowIndex { get; set; }
    public string? ExternalId { get; set; }
    public string Status { get; set; } = "rejected";
    public List<QuestionImportValidationIssueDto> Issues { get; set; } = [];
    public bool IsDuplicate { get; set; }
    public Guid? DuplicateQuestionId { get; set; }
    public Guid? CreatedQuestionId { get; set; }
    public CreateQuestionDto? NormalizedQuestion { get; set; }
}

public sealed class QuestionImportValidationIssueDto
{
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = "error";
    public string Message { get; set; } = string.Empty;
}

public sealed class QuestionImportApprovalDto
{
    public Guid ImportPreviewId { get; set; }
}

public sealed class QuestionImportResultDto
{
    public Guid ImportPreviewId { get; set; }
    public string Status { get; set; } = "pending";
    public int CreatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int RejectedCount { get; set; }
    public List<Guid> CreatedQuestionIds { get; set; } = [];
    public List<QuestionImportValidationIssueDto> Issues { get; set; } = [];
}

public sealed class QuestionImportPackageDto
{
    public string PackageVersion { get; set; } = "2.0";
    public string PackageTitle { get; set; } = string.Empty;
    public string SourceOrigin { get; set; } = "structured_json_v2";
    public string LicenseStatus { get; set; } = "unknown";
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public Guid? ExamDefinitionId { get; set; }
    public Guid? ExamVariantId { get; set; }
    public Guid? ExamSectionId { get; set; }
    public Guid? ExamSubjectId { get; set; }
    public Guid? ExamTopicId { get; set; }
    public Guid? ExamOutcomeId { get; set; }
    public string? ExamCode { get; set; }
    public string? VariantCode { get; set; }
    public string? SectionCode { get; set; }
    public string? SubjectCode { get; set; }
    public string? TopicCode { get; set; }
    public string? OutcomeCode { get; set; }
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
    public List<QuestionImportAssetDto> Assets { get; set; } = [];
    public List<QuestionImportStimulusDto> Stimuli { get; set; } = [];
    public List<QuestionImportRichQuestionDto> Questions { get; set; } = [];
}

public sealed class QuestionImportAssetDto
{
    public string ExternalAssetId { get; set; } = string.Empty;
    public string AssetType { get; set; } = "image";
    public string StorageKey { get; set; } = string.Empty;
    public string? RelativePath { get; set; }
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
}

public sealed class QuestionImportStimulusDto
{
    public string ExternalStimulusId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string StimulusType { get; set; } = "passage";
    public string? ContentText { get; set; }
    public string? ContentJson { get; set; }
    public Guid? SourceRegistryItemId { get; set; }
    public Guid? CurriculumNodeId { get; set; }
    public string LicenseStatus { get; set; } = "unknown";
    public string VerificationStatus { get; set; } = "unverified";
}

public sealed class QuestionImportRichQuestionDto
{
    public string? ExternalId { get; set; }
    public Guid? ExamDefinitionId { get; set; }
    public Guid? ExamVariantId { get; set; }
    public Guid? ExamSectionId { get; set; }
    public Guid? ExamSubjectId { get; set; }
    public Guid? ExamTopicId { get; set; }
    public Guid? ExamOutcomeId { get; set; }
    public string? ExamCode { get; set; }
    public string? VariantCode { get; set; }
    public string? SectionCode { get; set; }
    public string? SubjectCode { get; set; }
    public string? TopicCode { get; set; }
    public string? OutcomeCode { get; set; }
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
    public string? SourceOrigin { get; set; }
    public string? LicenseStatus { get; set; }
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public List<QuestionImportContentBlockDto> ContentBlocks { get; set; } = [];
    public List<QuestionImportRichOptionDto> Options { get; set; } = [];
    public List<QuestionExplanationDto> Explanations { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public List<QuestionOutcomeLinkDto> OutcomeLinks { get; set; } = [];
    public List<string> ExternalStimulusIds { get; set; } = [];
}

public sealed class QuestionImportContentBlockDto
{
    public string BlockType { get; set; } = "text";
    public string? Text { get; set; }
    public string? ContentJson { get; set; }
    public string? ExternalAssetId { get; set; }
    public int SortOrder { get; set; }
    public string? AltText { get; set; }
    public string? Caption { get; set; }
    public string? LongDescription { get; set; }
}

public sealed class QuestionImportRichOptionDto
{
    public string OptionKey { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public int SortOrder { get; set; }
    public string? Rationale { get; set; }
    public string? MisconceptionKey { get; set; }
    public string? DiagnosticSignalJson { get; set; }
    public List<QuestionImportContentBlockDto> ContentBlocks { get; set; } = [];
}

public sealed class QuestionImportTextAdapterRequestDto
{
    public string Content { get; set; } = string.Empty;
    public string SourceOrigin { get; set; } = "structured_text";
    public string LicenseStatus { get; set; } = "unknown";
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public Guid? ExamDefinitionId { get; set; }
    public Guid? ExamVariantId { get; set; }
    public Guid? ExamSectionId { get; set; }
    public Guid? ExamSubjectId { get; set; }
    public Guid? ExamTopicId { get; set; }
    public Guid? ExamOutcomeId { get; set; }
    public string? ExamCode { get; set; }
    public string? VariantCode { get; set; }
    public string? SectionCode { get; set; }
    public string? SubjectCode { get; set; }
    public string? TopicCode { get; set; }
    public string? OutcomeCode { get; set; }
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
}
