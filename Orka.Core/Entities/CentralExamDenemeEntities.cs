namespace Orka.Core.Entities;

public sealed class CentralExamDenemeBlueprint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExamDefinitionId { get; set; }
    public Guid? ExamVariantId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Visibility { get; set; } = "system";
    public string VerificationStatus { get; set; } = "unverified";
    public bool OfficialClaimAllowed { get; set; }
    public int? DurationMinutes { get; set; }
    public int TotalQuestionCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public ExamDefinition ExamDefinition { get; set; } = null!;
    public ExamVariant? ExamVariant { get; set; }
    public User? OwnerUser { get; set; }
    public ICollection<CentralExamDenemeBlueprintSection> Sections { get; set; } = [];
}

public sealed class CentralExamDenemeBlueprintSection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BlueprintId { get; set; }
    public Guid? ExamSectionId { get; set; }
    public Guid? ExamSubjectId { get; set; }
    public Guid? ExamTopicId { get; set; }
    public string? SectionCode { get; set; }
    public string? SubjectCode { get; set; }
    public string? TopicCode { get; set; }
    public int QuestionCount { get; set; }
    public string DifficultyMixJson { get; set; } = "{}";
    public int SortOrder { get; set; }
    public bool IsDeleted { get; set; }

    public CentralExamDenemeBlueprint Blueprint { get; set; } = null!;
    public ExamSection? ExamSection { get; set; }
    public ExamSubject? ExamSubject { get; set; }
    public ExamTopic? ExamTopic { get; set; }
}

public sealed class CentralExamDenemeAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid BlueprintId { get; set; }
    public Guid ExamDefinitionId { get; set; }
    public string ExamCode { get; set; } = string.Empty;
    public Guid? ExamVariantId { get; set; }
    public string? VariantCode { get; set; }
    public string Status { get; set; } = "started";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    public int? DurationMinutes { get; set; }
    public int TotalQuestions { get; set; }
    public int AnsweredCount { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public int BlankCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public User User { get; set; } = null!;
    public CentralExamDenemeBlueprint Blueprint { get; set; } = null!;
    public ExamDefinition ExamDefinition { get; set; } = null!;
    public ExamVariant? ExamVariant { get; set; }
    public ICollection<CentralExamDenemeAnswer> Answers { get; set; } = [];
}

public sealed class CentralExamDenemeAnswer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DenemeAttemptId { get; set; }
    public Guid QuestionItemId { get; set; }
    public Guid? ExamSectionId { get; set; }
    public Guid? ExamSubjectId { get; set; }
    public Guid? ExamTopicId { get; set; }
    public Guid? ExamOutcomeId { get; set; }
    public string? SectionCode { get; set; }
    public string? SubjectCode { get; set; }
    public string? TopicCode { get; set; }
    public string? OutcomeCode { get; set; }
    public string QuestionType { get; set; } = "multiple_choice";
    public string Difficulty { get; set; } = "medium";
    public string? SelectedOptionKey { get; set; }
    public string? CorrectOptionKey { get; set; }
    public string OptionKeysJson { get; set; } = "[]";
    public bool IsCorrect { get; set; }
    public bool IsBlank { get; set; } = true;
    public string Explanation { get; set; } = string.Empty;
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }

    public CentralExamDenemeAttempt DenemeAttempt { get; set; } = null!;
    public QuestionItem QuestionItem { get; set; } = null!;
    public ExamSection? ExamSection { get; set; }
    public ExamSubject? ExamSubject { get; set; }
    public ExamTopic? ExamTopic { get; set; }
    public ExamOutcome? ExamOutcome { get; set; }
}
