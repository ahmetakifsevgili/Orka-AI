namespace Orka.Core.DTOs;

public sealed class QuestionItemAnalyticsDto
{
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
    public DateTime LastCalculatedAt { get; set; }
    public List<QuestionOptionAnalyticsDto> Options { get; set; } = [];
    public List<QuestionQualityReviewSignalDto> ReviewSignals { get; set; } = [];
}

public sealed class QuestionOptionAnalyticsDto
{
    public string OptionKey { get; set; } = string.Empty;
    public int SelectionCount { get; set; }
    public int CorrectSelectionCount { get; set; }
    public int WrongSelectionCount { get; set; }
    public decimal SelectionRate { get; set; }
    public bool IsCorrectOption { get; set; }
    public string DistractorSignal { get; set; } = "not_available";
}

public sealed class QuestionQualityReviewSignalDto
{
    public Guid Id { get; set; }
    public Guid QuestionItemId { get; set; }
    public string SignalType { get; set; } = "low_sample_size";
    public string Severity { get; set; } = "info";
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public sealed class CentralExamQualityOverviewDto
{
    public string ExamCode { get; set; } = string.Empty;
    public string? VariantCode { get; set; }
    public string UserSafeLabel { get; set; } = "Orka içerik kapsamı ve kalite sinyalleri; resmi müfredat tamamlama iddiası değildir.";
    public int VisibleQuestionCount { get; set; }
    public int PublishedQuestionCount { get; set; }
    public int AnalyticsSnapshotCount { get; set; }
    public int NeedsReviewSignalCount { get; set; }
    public int LowCoverageTopicCount { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public List<CentralExamQualityTopicCoverageDto> Topics { get; set; } = [];
}

public sealed class CentralExamQualityTopicCoverageDto
{
    public Guid? ExamSubjectId { get; set; }
    public string? SubjectCode { get; set; }
    public Guid? ExamTopicId { get; set; }
    public string? TopicCode { get; set; }
    public Guid? ExamOutcomeId { get; set; }
    public string? OutcomeCode { get; set; }
    public int PublishedQuestionCount { get; set; }
    public int PracticeReadyCount { get; set; }
    public int CallerDraftCount { get; set; }
    public int CallerNeedsReviewCount { get; set; }
    public string CoverageStatus { get; set; } = "no_content";
    public string? AverageDifficultyEstimate { get; set; }
}

public sealed class CentralExamBlueprintCoverageDto
{
    public string ExamCode { get; set; } = string.Empty;
    public string? VariantCode { get; set; }
    public string UserSafeLabel { get; set; } = "Orka içerik kapsamı; resmi sınav kapsamı veya başarı tahmini değildir.";
    public int TopicCount { get; set; }
    public int NoContentCount { get; set; }
    public int LowContentCount { get; set; }
    public int UsableCount { get; set; }
    public int StrongCount { get; set; }
    public List<CentralExamQualityTopicCoverageDto> Topics { get; set; } = [];
}

public sealed class RecalculateQuestionAnalyticsResultDto
{
    public Guid QuestionItemId { get; set; }
    public bool Recalculated { get; set; }
    public QuestionItemAnalyticsDto? Analytics { get; set; }
}

public sealed class RecalculateExamAnalyticsResultDto
{
    public string ExamCode { get; set; } = string.Empty;
    public string? VariantCode { get; set; }
    public int RecalculatedQuestionCount { get; set; }
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
}
