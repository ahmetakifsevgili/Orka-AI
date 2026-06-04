namespace Orka.Core.Entities;

public sealed class QuestionItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? OwnerUserId { get; set; }
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
    public string QuestionBankSource { get; set; } = "curated_question_item";
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public User? OwnerUser { get; set; }
    public ExamDefinition ExamDefinition { get; set; } = null!;
    public ExamVariant? ExamVariant { get; set; }
    public ExamSection? ExamSection { get; set; }
    public ExamSubject? ExamSubject { get; set; }
    public ExamTopic? ExamTopic { get; set; }
    public ExamOutcome? ExamOutcome { get; set; }
    public Topic? LearningTopic { get; set; }
    public ConceptGraphSnapshot? ConceptGraphSnapshot { get; set; }
    public LearningConcept? LearningConcept { get; set; }
    public AssessmentItem? AssessmentItem { get; set; }
    public QuizRun? QuizRun { get; set; }
    public ICollection<QuestionOption> Options { get; set; } = [];
    public ICollection<QuestionExplanation> Explanations { get; set; } = [];
    public ICollection<QuestionTag> Tags { get; set; } = [];
    public ICollection<QuestionOutcomeLink> OutcomeLinks { get; set; } = [];
    public ICollection<QuestionContentBlock> ContentBlocks { get; set; } = [];
    public ICollection<QuestionStimulusLink> StimulusLinks { get; set; } = [];
}

public sealed class QuestionOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionItemId { get; set; }
    public string OptionKey { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public string? Rationale { get; set; }
    public string? MisconceptionKey { get; set; }
    public string? DiagnosticSignalJson { get; set; }
    public int SortOrder { get; set; }

    public QuestionItem QuestionItem { get; set; } = null!;
    public ICollection<QuestionOptionContentBlock> ContentBlocks { get; set; } = [];
}

public sealed class QuestionExplanation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionItemId { get; set; }
    public string ExplanationText { get; set; } = string.Empty;
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public string Visibility { get; set; } = "authoring";
    public bool IsSafeForLearners { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public QuestionItem QuestionItem { get; set; } = null!;
}

public sealed class QuestionTag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionItemId { get; set; }
    public string Tag { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public QuestionItem QuestionItem { get; set; } = null!;
}

public sealed class QuestionOutcomeLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionItemId { get; set; }
    public Guid ExamOutcomeId { get; set; }
    public bool IsPrimary { get; set; }
    public decimal LinkStrength { get; set; } = 1.0m;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public QuestionItem QuestionItem { get; set; } = null!;
    public ExamOutcome ExamOutcome { get; set; } = null!;
}

public sealed class QuestionAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? OwnerUserId { get; set; }
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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public User? OwnerUser { get; set; }
    public SourceRegistryItem? SourceRegistryItem { get; set; }
}

public sealed class QuestionStimulus
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? OwnerUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string StimulusType { get; set; } = "passage";
    public string? ContentText { get; set; }
    public string? ContentJson { get; set; }
    public Guid? SourceRegistryItemId { get; set; }
    public Guid? CurriculumNodeId { get; set; }
    public string VerificationStatus { get; set; } = "unverified";
    public string LicenseStatus { get; set; } = "unknown";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public User? OwnerUser { get; set; }
    public SourceRegistryItem? SourceRegistryItem { get; set; }
    public CurriculumNode? CurriculumNode { get; set; }
    public ICollection<QuestionStimulusLink> QuestionLinks { get; set; } = [];
}

public sealed class QuestionStimulusLink
{
    public Guid QuestionItemId { get; set; }
    public Guid QuestionStimulusId { get; set; }
    public int SortOrder { get; set; }

    public QuestionItem QuestionItem { get; set; } = null!;
    public QuestionStimulus QuestionStimulus { get; set; } = null!;
}

public sealed class QuestionContentBlock
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionItemId { get; set; }
    public string BlockType { get; set; } = "text";
    public string? Text { get; set; }
    public string? ContentJson { get; set; }
    public Guid? AssetId { get; set; }
    public int SortOrder { get; set; }
    public string? AltText { get; set; }
    public string? Caption { get; set; }
    public string? LongDescription { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public QuestionItem QuestionItem { get; set; } = null!;
    public QuestionAsset? Asset { get; set; }
}

public sealed class QuestionOptionContentBlock
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionOptionId { get; set; }
    public string BlockType { get; set; } = "text";
    public string? Text { get; set; }
    public string? ContentJson { get; set; }
    public Guid? AssetId { get; set; }
    public int SortOrder { get; set; }
    public string? AltText { get; set; }
    public string? Caption { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public QuestionOption QuestionOption { get; set; } = null!;
    public QuestionAsset? Asset { get; set; }
}
