namespace Orka.Core.Entities;

public sealed class QuestionItemAnalyticsSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionItemId { get; set; }
    public Guid ExamDefinitionId { get; set; }
    public Guid? ExamVariantId { get; set; }
    public Guid? ExamSectionId { get; set; }
    public Guid? ExamSubjectId { get; set; }
    public Guid? ExamTopicId { get; set; }
    public Guid? ExamOutcomeId { get; set; }
    public int AttemptCount { get; set; }
    public int AnsweredCount { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public int BlankCount { get; set; }
    public decimal CorrectnessRate { get; set; }
    public decimal BlankRate { get; set; }
    public string DifficultyEstimate { get; set; } = "insufficient_data";
    public string DiscriminationStatus { get; set; } = "not_available";
    public string QualitySignal { get; set; } = "insufficient_data";
    public string SampleSizeStatus { get; set; } = "none";
    public DateTime LastCalculatedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public QuestionItem QuestionItem { get; set; } = null!;
    public ExamDefinition ExamDefinition { get; set; } = null!;
    public ExamVariant? ExamVariant { get; set; }
    public ExamSection? ExamSection { get; set; }
    public ExamSubject? ExamSubject { get; set; }
    public ExamTopic? ExamTopic { get; set; }
    public ExamOutcome? ExamOutcome { get; set; }
    public List<QuestionOptionAnalyticsSnapshot> OptionSnapshots { get; set; } = [];
}

public sealed class QuestionOptionAnalyticsSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionItemAnalyticsSnapshotId { get; set; }
    public Guid QuestionItemId { get; set; }
    public string OptionKey { get; set; } = string.Empty;
    public int SelectionCount { get; set; }
    public int CorrectSelectionCount { get; set; }
    public int WrongSelectionCount { get; set; }
    public decimal SelectionRate { get; set; }
    public bool IsCorrectOption { get; set; }
    public string DistractorSignal { get; set; } = "not_available";
    public DateTime LastCalculatedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public QuestionItemAnalyticsSnapshot QuestionItemAnalyticsSnapshot { get; set; } = null!;
    public QuestionItem QuestionItem { get; set; } = null!;
}

public sealed class QuestionQualityReviewSignal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionItemId { get; set; }
    public string SignalType { get; set; } = "low_sample_size";
    public string Severity { get; set; } = "info";
    public string Message { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public bool IsDeleted { get; set; }

    public QuestionItem QuestionItem { get; set; } = null!;
}
