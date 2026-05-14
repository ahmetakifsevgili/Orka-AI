namespace Orka.Core.Entities;

public sealed class CentralExamPracticeAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid ExamDefinitionId { get; set; }
    public string ExamCode { get; set; } = string.Empty;
    public Guid? ExamVariantId { get; set; }
    public string? VariantCode { get; set; }
    public Guid? ExamSectionId { get; set; }
    public string? SectionCode { get; set; }
    public Guid? ExamSubjectId { get; set; }
    public string? SubjectCode { get; set; }
    public Guid? ExamTopicId { get; set; }
    public string? TopicCode { get; set; }
    public string Status { get; set; } = "started";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    public int TotalQuestions { get; set; }
    public int AnsweredCount { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public int BlankCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public User User { get; set; } = null!;
    public ExamDefinition ExamDefinition { get; set; } = null!;
    public ExamVariant? ExamVariant { get; set; }
    public ExamSection? ExamSection { get; set; }
    public ExamSubject? ExamSubject { get; set; }
    public ExamTopic? ExamTopic { get; set; }
    public ICollection<CentralExamPracticeAnswer> Answers { get; set; } = [];
}

public sealed class CentralExamPracticeAnswer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PracticeAttemptId { get; set; }
    public Guid QuestionItemId { get; set; }
    public Guid? ExamOutcomeId { get; set; }
    public Guid? ExamTopicId { get; set; }
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

    public CentralExamPracticeAttempt PracticeAttempt { get; set; } = null!;
    public QuestionItem QuestionItem { get; set; } = null!;
    public ExamOutcome? ExamOutcome { get; set; }
    public ExamTopic? ExamTopic { get; set; }
}
