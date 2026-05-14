namespace Orka.Core.DTOs;

public sealed class QuestionItemDto
{
    public Guid Id { get; set; }
    public string OwnershipState { get; set; } = "user";
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
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<QuestionOptionDto> Options { get; set; } = [];
    public List<QuestionExplanationDto> Explanations { get; set; } = [];
    public List<QuestionTagDto> Tags { get; set; } = [];
    public List<QuestionOutcomeLinkDto> OutcomeLinks { get; set; } = [];
    public QuestionValidationResultDto Validation { get; set; } = new();
}

public sealed class QuestionOptionDto
{
    public Guid? Id { get; set; }
    public string OptionKey { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public int SortOrder { get; set; }
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
}

public sealed class UpdateQuestionDto
{
    public Guid? ExamVariantId { get; set; }
    public Guid? ExamSectionId { get; set; }
    public Guid? ExamSubjectId { get; set; }
    public Guid? ExamTopicId { get; set; }
    public Guid? ExamOutcomeId { get; set; }
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
}

public sealed class QuestionBankFilterDto
{
    public Guid? ExamDefinitionId { get; set; }
    public Guid? ExamVariantId { get; set; }
    public Guid? ExamSectionId { get; set; }
    public Guid? ExamSubjectId { get; set; }
    public Guid? ExamTopicId { get; set; }
    public Guid? ExamOutcomeId { get; set; }
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
}
