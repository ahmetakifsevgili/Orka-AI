namespace Orka.Core.DTOs;

public sealed class CentralExamDto
{
    public string ExamCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AvailabilityStatus { get; set; } = "coming_soon";
    public string VerificationStatus { get; set; } = "unverified";
    public bool CanClaimOfficial { get; set; }
    public string UserSafeVerificationLabel { get; set; } = string.Empty;
    public List<CentralExamVariantDto> SupportedVariants { get; set; } = [];
    public CentralExamCapabilityDto Capabilities { get; set; } = new();
}

public sealed class CentralExamVariantDto
{
    public string VariantCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string AvailabilityStatus { get; set; } = "available";
}

public sealed class CentralExamCapabilityDto
{
    public bool HasQuestionBank { get; set; }
    public bool HasPractice { get; set; }
    public bool HasMiniDeneme { get; set; }
    public bool HasCountdown { get; set; }
    public bool HasStudyPlan { get; set; }
}

public sealed class CentralExamStudyHomeDto
{
    public string ExamCode { get; set; } = "KPSS";
    public string DisplayName { get; set; } = "KPSS hazırlık iskeleti";
    public string Description { get; set; } = string.Empty;
    public string VerificationStatus { get; set; } = "unverified";
    public bool CanClaimOfficial { get; set; }
    public string UserSafeVerificationLabel { get; set; } = string.Empty;
    public CentralExamCountdownDto Countdown { get; set; } = new();
    public List<CentralExamVariantDto> SupportedVariants { get; set; } = [];
    public List<CentralExamSectionDto> Sections { get; set; } = [];
    public CentralExamQuestionCountDto PracticeReadyCounts { get; set; } = new();
    public CentralExamPracticeEntryDto? RecommendedEntryPoint { get; set; }
    public CentralExamCapabilityDto Capabilities { get; set; } = new();
    public string EmptyState { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CentralExamCountdownDto
{
    public string ExamCode { get; set; } = "KPSS";
    public DateTimeOffset? ExamDate { get; set; }
    public int? DaysRemaining { get; set; }
    public string VerificationStatus { get; set; } = "not_configured";
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public string UserSafeLabel { get; set; } = "Sınav tarihi doğrulanmış kaynakla yapılandırılmadı.";
}

public sealed class CentralExamSectionDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<CentralExamSubjectDto> Subjects { get; set; } = [];
}

public sealed class CentralExamSubjectDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<CentralExamTopicDto> Topics { get; set; } = [];
}

public sealed class CentralExamTopicDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int PracticeReadyCount { get; set; }
    public List<CentralExamTopicDto> Children { get; set; } = [];
}

public sealed class CentralExamPracticeEntryDto
{
    public string ExamCode { get; set; } = "KPSS";
    public string Slug { get; set; } = "kpss-turkce-paragraf";
    public string Title { get; set; } = "KPSS Türkçe Paragraf";
    public string Description { get; set; } = string.Empty;
    public bool HasPracticeReadyQuestions { get; set; }
    public int PracticeReadyCount { get; set; }
    public string EmptyState { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = "practice";
    public ExamLearningContextDto ExamContext { get; set; } = new();
}

public sealed class CentralExamQuestionCountDto
{
    public int PracticeReadyCount { get; set; }
    public int SystemPublishedCount { get; set; }
    public int UserPublishedCount { get; set; }
    public int CallerDraftCount { get; set; }
    public int CallerNeedsReviewCount { get; set; }
}

public sealed class ExamLearningContextDto
{
    public Guid? ExamDefinitionId { get; set; }
    public string? ExamCode { get; set; }
    public Guid? ExamVariantId { get; set; }
    public string? VariantCode { get; set; }
    public Guid? ExamSectionId { get; set; }
    public string? SectionCode { get; set; }
    public Guid? ExamSubjectId { get; set; }
    public string? SubjectCode { get; set; }
    public Guid? ExamTopicId { get; set; }
    public string? TopicCode { get; set; }
    public Guid? ExamOutcomeId { get; set; }
    public string? OutcomeCode { get; set; }
}

public sealed class PracticeStartRequestDto
{
    public string? VariantCode { get; set; }
    public int Limit { get; set; } = 5;
}

public sealed class PracticeSessionDto
{
    public Guid PracticeSetId { get; set; } = Guid.NewGuid();
    public Guid? PracticeAttemptId { get; set; }
    public string Status { get; set; } = "ready";
    public string EmptyState { get; set; } = string.Empty;
    public int TotalQuestions { get; set; }
    public ExamLearningContextDto ExamContext { get; set; } = new();
    public List<PracticeQuestionDto> Questions { get; set; } = [];
}

public sealed class PracticeQuestionDto
{
    public Guid QuestionId { get; set; }
    public string Stem { get; set; } = string.Empty;
    public string Difficulty { get; set; } = "medium";
    public string CognitiveSkill { get; set; } = "conceptual";
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public ExamLearningContextDto ExamContext { get; set; } = new();
    public List<PracticeStimulusDto> Stimuli { get; set; } = [];
    public List<PracticeContentBlockDto> ContentBlocks { get; set; } = [];
    public List<PracticeOptionDto> Options { get; set; } = [];
}

public sealed class PracticeOptionDto
{
    public string OptionKey { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public List<PracticeContentBlockDto> ContentBlocks { get; set; } = [];
}

public sealed class PracticeContentBlockDto
{
    public string BlockType { get; set; } = "text";
    public string? Text { get; set; }
    public string? ContentJson { get; set; }
    public string? AssetType { get; set; }
    public string? FileName { get; set; }
    public string? MimeType { get; set; }
    public int SortOrder { get; set; }
    public string? AltText { get; set; }
    public string? Caption { get; set; }
    public string? LongDescription { get; set; }
}

public sealed class PracticeStimulusDto
{
    public string Title { get; set; } = string.Empty;
    public string StimulusType { get; set; } = "passage";
    public string? ContentText { get; set; }
    public string? ContentJson { get; set; }
    public int SortOrder { get; set; }
}

public sealed class PracticeSubmitRequestDto
{
    public string? VariantCode { get; set; }
    public Guid? PracticeSetId { get; set; }
    public List<PracticeAnswerDto> Answers { get; set; } = [];
}

public sealed class PracticeAnswerDto
{
    public Guid QuestionId { get; set; }
    public string? SelectedOptionKey { get; set; }
}

public sealed class PracticeResultDto
{
    public Guid? PracticeAttemptId { get; set; }
    public string Status { get; set; } = "submitted";
    public int TotalQuestions { get; set; }
    public int AnsweredCount { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public int BlankCount { get; set; }
    public ExamLearningContextDto ExamContext { get; set; } = new();
    public List<PracticeQuestionResultDto> Results { get; set; } = [];
    public List<PracticeTopicBreakdownDto> TopicBreakdown { get; set; } = [];
    public CentralExamNextActionDto? NextAction { get; set; }
    public CentralExamLearningSignalDto? LearningSignal { get; set; }
    public CentralExamStudyContextDto? StudyContext { get; set; }
    public string TutorRemediationContext { get; set; } = string.Empty;
}

public sealed class PracticeQuestionResultDto
{
    public Guid QuestionId { get; set; }
    public string Stem { get; set; } = string.Empty;
    public string? SelectedOptionKey { get; set; }
    public string? CorrectOptionKey { get; set; }
    public bool IsCorrect { get; set; }
    public bool IsBlank { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public ExamLearningContextDto ExamContext { get; set; } = new();
    public List<PracticeStimulusDto> Stimuli { get; set; } = [];
    public List<PracticeContentBlockDto> ContentBlocks { get; set; } = [];
    public List<PracticeOptionDto> Options { get; set; } = [];
}

public sealed class PracticeTopicBreakdownDto
{
    public Guid? ExamTopicId { get; set; }
    public string? TopicCode { get; set; }
    public string Label { get; set; } = string.Empty;
    public int TotalQuestions { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public int BlankCount { get; set; }
}

public sealed class CentralExamPracticeAttemptDto
{
    public Guid Id { get; set; }
    public string Status { get; set; } = "started";
    public ExamLearningContextDto ExamContext { get; set; } = new();
    public CentralExamPracticeSummaryDto Summary { get; set; } = new();
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
}

public sealed class CentralExamPracticeAnswerDto
{
    public Guid QuestionId { get; set; }
    public string? SelectedOptionKey { get; set; }
    public string? CorrectOptionKey { get; set; }
    public bool IsCorrect { get; set; }
    public bool IsBlank { get; set; }
    public ExamLearningContextDto ExamContext { get; set; } = new();
}

public sealed class CentralExamPracticeSummaryDto
{
    public int TotalQuestions { get; set; }
    public int AnsweredCount { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public int BlankCount { get; set; }
    public decimal CorrectnessRatio { get; set; }
}

public sealed class CentralExamLearningSignalDto
{
    public string Status { get; set; } = "observed_only";
    public int SignalCount { get; set; }
    public IReadOnlyList<string> EvidenceBasis { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> WeakAreas { get; set; } = Array.Empty<string>();
}

public sealed class CentralExamNextActionDto
{
    public string ActionType { get; set; } = "practice_quiz";
    public string Title { get; set; } = "Kisa tekrar yap";
    public string Reason { get; set; } = "Bu konuda pratik sinyali izleniyor.";
    public string ConfidenceStatus { get; set; } = "observed_only";
    public ExamLearningContextDto ExamContext { get; set; } = new();
}

public sealed class CentralExamStudyContextDto
{
    public string PathLabel { get; set; } = string.Empty;
    public string SuggestedWikiPath { get; set; } = string.Empty;
    public ExamLearningContextDto ExamContext { get; set; } = new();
    public IReadOnlyList<string> FocusLabels { get; set; } = Array.Empty<string>();
}

public sealed class CentralExamDenemeBlueprintDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Visibility { get; set; } = "system";
    public string VerificationStatus { get; set; } = "unverified";
    public bool CanClaimOfficial { get; set; }
    public string UserSafeVerificationLabel { get; set; } = string.Empty;
    public int? DurationMinutes { get; set; }
    public int TotalQuestionCount { get; set; }
    public int AvailableQuestionCount { get; set; }
    public bool HasEnoughQuestions { get; set; }
    public string EmptyState { get; set; } = string.Empty;
    public ExamLearningContextDto ExamContext { get; set; } = new();
    public List<CentralExamDenemeBlueprintSectionDto> Sections { get; set; } = [];
}

public sealed class CentralExamDenemeBlueprintSectionDto
{
    public Guid Id { get; set; }
    public int SortOrder { get; set; }
    public int QuestionCount { get; set; }
    public int AvailableQuestionCount { get; set; }
    public string Label { get; set; } = string.Empty;
    public ExamLearningContextDto ExamContext { get; set; } = new();
}

public sealed class CentralExamDenemeStartRequestDto
{
    public string? VariantCode { get; set; }
}

public sealed class CentralExamDenemeSessionDto
{
    public Guid DenemeAttemptId { get; set; }
    public string BlueprintCode { get; set; } = string.Empty;
    public string BlueprintName { get; set; } = string.Empty;
    public string Status { get; set; } = "ready";
    public string EmptyState { get; set; } = string.Empty;
    public int? DurationMinutes { get; set; }
    public int TotalQuestions { get; set; }
    public ExamLearningContextDto ExamContext { get; set; } = new();
    public List<CentralExamDenemeQuestionDto> Questions { get; set; } = [];
}

public sealed class CentralExamDenemeQuestionDto
{
    public Guid QuestionId { get; set; }
    public string Stem { get; set; } = string.Empty;
    public string Difficulty { get; set; } = "medium";
    public string CognitiveSkill { get; set; } = "conceptual";
    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public ExamLearningContextDto ExamContext { get; set; } = new();
    public List<CentralExamDenemeOptionDto> Options { get; set; } = [];
}

public sealed class CentralExamDenemeOptionDto
{
    public string OptionKey { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class CentralExamDenemeSubmitRequestDto
{
    public Guid DenemeAttemptId { get; set; }
    public List<CentralExamDenemeAnswerDto> Answers { get; set; } = [];
}

public sealed class CentralExamDenemeAnswerDto
{
    public Guid QuestionId { get; set; }
    public string? SelectedOptionKey { get; set; }
}

public sealed class CentralExamDenemeResultDto
{
    public Guid DenemeAttemptId { get; set; }
    public string BlueprintCode { get; set; } = string.Empty;
    public string BlueprintName { get; set; } = string.Empty;
    public string Status { get; set; } = "submitted";
    public int? DurationMinutes { get; set; }
    public CentralExamDenemeSummaryDto Summary { get; set; } = new();
    public ExamLearningContextDto ExamContext { get; set; } = new();
    public List<PracticeQuestionResultDto> Results { get; set; } = [];
    public List<CentralExamDenemeBreakdownDto> Breakdown { get; set; } = [];
    public CentralExamDenemeNextActionDto? NextAction { get; set; }
    public CentralExamLearningSignalDto? LearningSignal { get; set; }
    public CentralExamStudyContextDto? StudyContext { get; set; }
    public string TutorRemediationContext { get; set; } = string.Empty;
}

public sealed class CentralExamDenemeSummaryDto
{
    public int TotalQuestions { get; set; }
    public int AnsweredCount { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public int BlankCount { get; set; }
    public decimal CorrectnessRatio { get; set; }
}

public sealed class CentralExamDenemeBreakdownDto
{
    public Guid? ExamSectionId { get; set; }
    public string? SectionCode { get; set; }
    public Guid? ExamSubjectId { get; set; }
    public string? SubjectCode { get; set; }
    public Guid? ExamTopicId { get; set; }
    public string? TopicCode { get; set; }
    public string Label { get; set; } = string.Empty;
    public int TotalQuestions { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public int BlankCount { get; set; }
}

public sealed class CentralExamDenemeNextActionDto
{
    public string ActionType { get; set; } = "practice_quiz";
    public string Title { get; set; } = "Mini deneme telafisi";
    public string Reason { get; set; } = "Bu mini deneme sonucuna gore kisa tekrar onerilir.";
    public string ConfidenceStatus { get; set; } = "observed_only";
    public ExamLearningContextDto ExamContext { get; set; } = new();
}

public sealed class ExamLearningProfileDto
{
    public string ExamCode { get; set; } = string.Empty;
    public string? VariantCode { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string VerificationStatus { get; set; } = "unverified";
    public bool CanClaimOfficial { get; set; }
    public string UserSafeVerificationLabel { get; set; } = string.Empty;
    public bool HasEnoughEvidence { get; set; }
    public int EvidenceCount { get; set; }
    public int OutcomeCount { get; set; }
    public int PublishedQuestionCount { get; set; }
    public IReadOnlyList<ExamOutcomeReadinessDto> Outcomes { get; set; } = Array.Empty<ExamOutcomeReadinessDto>();
    public IReadOnlyList<ExamPracticeReadinessDto> PracticeReadiness { get; set; } = Array.Empty<ExamPracticeReadinessDto>();
    public IReadOnlyList<ExamNextActionDto> NextActions { get; set; } = Array.Empty<ExamNextActionDto>();
    public IReadOnlyList<string> WeakOutcomes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> DueOutcomes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> StableOutcomes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ExamOutcomeReadinessDto
{
    public Guid ExamOutcomeId { get; set; }
    public string OutcomeCode { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string TopicCode { get; set; } = string.Empty;
    public string TopicLabel { get; set; } = string.Empty;
    public string ReadinessStatus { get; set; } = "coverage_limited";
    public string ReviewPriority { get; set; } = "low";
    public string RecommendedAction { get; set; } = "run_diagnostic";
    public string EvidenceBasis { get; set; } = "thin";
    public int PublishedQuestionCount { get; set; }
    public int AttemptCount { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public int BlankCount { get; set; }
    public int DenemeMistakeCount { get; set; }
    public decimal CorrectnessRate { get; set; }
    public string QuestionCoverageStatus { get; set; } = "no_content";
    public string SourceEvidenceStatus { get; set; } = "unverified";
    public IReadOnlyList<string> QuestionTypes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public string UserSafeSummary { get; set; } = string.Empty;
}

public sealed class ExamPracticeReadinessDto
{
    public string QuestionType { get; set; } = "multiple_choice";
    public int PublishedQuestionCount { get; set; }
    public int AttemptCount { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public int BlankCount { get; set; }
    public string ReadinessStatus { get; set; } = "coverage_limited";
    public string RecommendedAction { get; set; } = "practice_question_type";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class ExamNextActionDto
{
    public string ActionType { get; set; } = "run_diagnostic";
    public string Label { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Priority { get; set; } = "medium";
    public string? OutcomeCode { get; set; }
    public string? TopicCode { get; set; }
    public string? QuestionType { get; set; }
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public ExamLearningContextDto ExamContext { get; set; } = new();
}

public sealed class OrkaExamWarRoomDto
{
    public ExamWarRoomActiveExamDto ActiveExam { get; set; } = new();
    public string? Variant { get; set; }
    public string ReadinessStatus { get; set; } = "thin_exam_evidence";
    public IReadOnlyList<ExamWarRoomSubjectDto> WeakSubjects { get; set; } = Array.Empty<ExamWarRoomSubjectDto>();
    public IReadOnlyList<ExamWarRoomTopicDto> WeakTopics { get; set; } = Array.Empty<ExamWarRoomTopicDto>();
    public IReadOnlyList<ExamWarRoomOutcomeDto> WeakOutcomes { get; set; } = Array.Empty<ExamWarRoomOutcomeDto>();
    public IReadOnlyList<ExamWarRoomOutcomeDto> DueOutcomes { get; set; } = Array.Empty<ExamWarRoomOutcomeDto>();
    public IReadOnlyList<ExamWarRoomOutcomeDto> StableOutcomes { get; set; } = Array.Empty<ExamWarRoomOutcomeDto>();
    public IReadOnlyList<ExamWarRoomPracticePlanDto> WeakQuestionTypes { get; set; } = Array.Empty<ExamWarRoomPracticePlanDto>();
    public IReadOnlyList<ExamWarRoomDenemeInsightDto> DenemeMistakeClusters { get; set; } = Array.Empty<ExamWarRoomDenemeInsightDto>();
    public IReadOnlyList<ExamWarRoomPracticePlanDto> PracticeReadiness { get; set; } = Array.Empty<ExamWarRoomPracticePlanDto>();
    public ExamWarRoomActionDto TodayExamMission { get; set; } = new();
    public IReadOnlyList<ExamWarRoomActionDto> WeeklyExamPlan { get; set; } = Array.Empty<ExamWarRoomActionDto>();
    public IReadOnlyList<ExamWarRoomActionDto> RecommendedPracticeQueue { get; set; } = Array.Empty<ExamWarRoomActionDto>();
    public IReadOnlyList<ExamWarRoomActionDto> TutorRepairHandoffs { get; set; } = Array.Empty<ExamWarRoomActionDto>();
    public IReadOnlyList<ExamWarRoomActionDto> StudyRoomHandoffs { get; set; } = Array.Empty<ExamWarRoomActionDto>();
    public IReadOnlyList<ExamWarRoomWarningDto> SourceWikiWarnings { get; set; } = Array.Empty<ExamWarRoomWarningDto>();
    public IReadOnlyList<ExamWarRoomWarningDto> CurriculumCoverageWarnings { get; set; } = Array.Empty<ExamWarRoomWarningDto>();
    public IReadOnlyList<ExamWarRoomWarningDto> ConflictWarnings { get; set; } = Array.Empty<ExamWarRoomWarningDto>();
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public string UserSafeSummary { get; set; } = "Exam War Room sinav kanitlarini guvenli sekilde izliyor.";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ExamWarRoomActiveExamDto
{
    public string ExamCode { get; set; } = string.Empty;
    public string? VariantCode { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string VerificationStatus { get; set; } = "unverified";
    public bool CanClaimOfficial { get; set; }
    public string UserSafeVerificationLabel { get; set; } = string.Empty;
}

public sealed class ExamWarRoomSubjectDto
{
    public string SubjectCode { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int WeakOutcomeCount { get; set; }
    public int DueOutcomeCount { get; set; }
    public int DenemeMistakeCount { get; set; }
    public string Priority { get; set; } = "normal";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class ExamWarRoomTopicDto
{
    public string TopicCode { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int WeakOutcomeCount { get; set; }
    public int DueOutcomeCount { get; set; }
    public int DenemeMistakeCount { get; set; }
    public string Priority { get; set; } = "normal";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class ExamWarRoomOutcomeDto
{
    public Guid ExamOutcomeId { get; set; }
    public string OutcomeCode { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string TopicCode { get; set; } = string.Empty;
    public string TopicLabel { get; set; } = string.Empty;
    public string ReadinessStatus { get; set; } = "diagnostic_needed";
    public string ReviewPriority { get; set; } = "medium";
    public string RecommendedAction { get; set; } = "run_exam_diagnostic";
    public int AttemptCount { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public int BlankCount { get; set; }
    public int DenemeMistakeCount { get; set; }
    public int PublishedQuestionCount { get; set; }
    public decimal CorrectnessRate { get; set; }
    public string QuestionCoverageStatus { get; set; } = "no_content";
    public string SourceEvidenceStatus { get; set; } = "unverified";
    public IReadOnlyList<string> QuestionTypes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public string UserSafeSummary { get; set; } = string.Empty;
}

public sealed class ExamWarRoomPracticePlanDto
{
    public string QuestionType { get; set; } = "multiple_choice";
    public string ReadinessStatus { get; set; } = "diagnostic_needed";
    public string RecommendedAction { get; set; } = "practice_question_type";
    public int PublishedQuestionCount { get; set; }
    public int AttemptCount { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public int BlankCount { get; set; }
    public string Priority { get; set; } = "normal";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class ExamWarRoomDenemeInsightDto
{
    public string OutcomeCode { get; set; } = string.Empty;
    public string TopicCode { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int MistakeCount { get; set; }
    public string Priority { get; set; } = "urgent";
    public string RecommendedAction { get; set; } = "review_deneme_mistakes";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class ExamWarRoomActionDto
{
    public string ActionType { get; set; } = "run_exam_diagnostic";
    public string Label { get; set; } = "Kisa sinav tani kontrolu";
    public string Reason { get; set; } = "Sinav kaniti henuz sinirli.";
    public string Priority { get; set; } = "medium";
    public string EntryPoint { get; set; } = "run_exam_diagnostic";
    public string TargetRoute { get; set; } = "central-exams";
    public string? OutcomeCode { get; set; }
    public string? TopicCode { get; set; }
    public string? QuestionType { get; set; }
    public ExamLearningContextDto ExamContext { get; set; } = new();
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class ExamWarRoomWarningDto
{
    public string WarningCode { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string Label { get; set; } = string.Empty;
    public string TargetRoute { get; set; } = "central-exams";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}
