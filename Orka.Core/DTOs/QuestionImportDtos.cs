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
    public int TotalCount { get; set; }
    public int AcceptedCount { get; set; }
    public int RejectedCount { get; set; }
    public int WarningCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
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
