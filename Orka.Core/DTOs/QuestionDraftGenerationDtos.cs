namespace Orka.Core.DTOs;

public sealed class QuestionDraftGenerationRequestDto
{
    public QuestionDraftGenerationContextDto Context { get; set; } = new();
    public QuestionDraftGenerationSourceDto Source { get; set; } = new();
    public string QuestionType { get; set; } = "multiple_choice";
    public int DesiredCount { get; set; } = 3;
    public string Difficulty { get; set; } = "medium";
    public string CognitiveSkill { get; set; } = "reading_comprehension";
}

public sealed class QuestionDraftGenerationContextDto
{
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
}

public sealed class QuestionDraftGenerationSourceDto
{
    public string SourceTitle { get; set; } = string.Empty;
    public string? SourceUrl { get; set; }
    public string SourceOrigin { get; set; } = "source_grounded_draft";
    public string LicenseStatus { get; set; } = "unknown";
    public string? SourceText { get; set; }
    public List<string> StructuredSourceContext { get; set; } = [];
}

public sealed class QuestionDraftCandidateDto
{
    public string? ExternalId { get; set; }
    public string QuestionType { get; set; } = "multiple_choice";
    public string Stem { get; set; } = string.Empty;
    public List<QuestionDraftOptionDto> Options { get; set; } = [];
    public string Explanation { get; set; } = string.Empty;
    public string Difficulty { get; set; } = "medium";
    public string CognitiveSkill { get; set; } = "reading_comprehension";
    public List<string> Tags { get; set; } = [];
    public string SourceOrigin { get; set; } = "source_grounded_draft";
    public string LicenseStatus { get; set; } = "unknown";
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
}

public sealed class QuestionDraftOptionDto
{
    public string OptionKey { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public int SortOrder { get; set; }
}

public sealed class QuestionDraftPreviewDto
{
    public Guid Id { get; set; }
    public Guid ImportPreviewId { get; set; }
    public string Status { get; set; } = "pending";
    public int TotalRequested { get; set; }
    public int GeneratedCount { get; set; }
    public int AcceptedDraftCount { get; set; }
    public int RejectedCount { get; set; }
    public int WarningCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public List<QuestionDraftPreviewItemDto> Items { get; set; } = [];
    public List<QuestionDraftGenerationIssueDto> Issues { get; set; } = [];
}

public sealed class QuestionDraftPreviewItemDto
{
    public Guid Id { get; set; }
    public int RowIndex { get; set; }
    public string? ExternalId { get; set; }
    public string Status { get; set; } = "rejected";
    public bool IsDuplicate { get; set; }
    public Guid? DuplicateQuestionId { get; set; }
    public Guid? CreatedQuestionId { get; set; }
    public QuestionDraftCandidateDto? Candidate { get; set; }
    public List<QuestionDraftGenerationIssueDto> Issues { get; set; } = [];
}

public sealed class QuestionDraftGenerationIssueDto
{
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = "error";
    public string Message { get; set; } = string.Empty;
}

public sealed class QuestionDraftApprovalDto
{
    public Guid DraftPreviewId { get; set; }
}

public sealed class QuestionDraftApprovalResultDto
{
    public Guid DraftPreviewId { get; set; }
    public Guid ImportPreviewId { get; set; }
    public string Status { get; set; } = "pending";
    public int CreatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int RejectedCount { get; set; }
    public List<Guid> CreatedQuestionIds { get; set; } = [];
    public List<QuestionDraftGenerationIssueDto> Issues { get; set; } = [];
}
