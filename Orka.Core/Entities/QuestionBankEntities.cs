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
    public ICollection<QuestionOption> Options { get; set; } = [];
    public ICollection<QuestionExplanation> Explanations { get; set; } = [];
    public ICollection<QuestionTag> Tags { get; set; } = [];
    public ICollection<QuestionOutcomeLink> OutcomeLinks { get; set; } = [];
}

public sealed class QuestionOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionItemId { get; set; }
    public string OptionKey { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public int SortOrder { get; set; }

    public QuestionItem QuestionItem { get; set; } = null!;
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
