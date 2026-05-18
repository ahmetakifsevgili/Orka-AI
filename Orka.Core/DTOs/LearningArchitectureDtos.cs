using Orka.Core.DTOs.Korteks;
using System.Text.Json.Serialization;

namespace Orka.Core.DTOs;

public sealed class ConceptGraphDto
{
    public Guid? SnapshotId { get; set; }
    public string IntentHash { get; set; } = string.Empty;
    public string TopicTitle { get; set; } = string.Empty;
    public string ApprovedResearchIntent { get; set; } = string.Empty;
    public string Domain { get; set; } = "general";
    public string SourceConfidence { get; set; } = "low";
    public string SourceBundleHash { get; set; } = string.Empty;
    public List<LearningOutcomeDto> Outcomes { get; set; } = [];
    public List<LearningConceptDto> Concepts { get; set; } = [];
    public List<ConceptRelationDto> Relations { get; set; } = [];
    public List<SourceEvidenceDto> SourceEvidence { get; set; } = [];
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LearningConceptDto
{
    public Guid? Id { get; set; }
    public string StableKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DifficultyBand { get; set; } = "core";
    public int Order { get; set; }
    public List<string> PrerequisiteKeys { get; set; } = [];
    public List<string> Misconceptions { get; set; } = [];
    public List<string> LearningOutcomeKeys { get; set; } = [];
    public List<string> SourceEvidenceLabels { get; set; } = [];
}

public sealed class ConceptRelationDto
{
    public Guid? Id { get; set; }
    public string SourceConceptKey { get; set; } = string.Empty;
    public string TargetConceptKey { get; set; } = string.Empty;
    public string RelationType { get; set; } = "prerequisite";
    public double Weight { get; set; } = 1.0;
}

public sealed class LearningOutcomeDto
{
    public Guid? Id { get; set; }
    public string StableKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? StandardUri { get; set; }
    public string CognitiveLevel { get; set; } = "understand";
}

public sealed class ConceptGraphBuildResultDto
{
    public ConceptGraphDto Graph { get; set; } = new();
    public Guid SnapshotId { get; set; }
    public string CacheKey { get; set; } = string.Empty;
    public string SourceBundleCacheKey { get; set; } = string.Empty;
    public Guid? QualityRunId { get; set; }
    public string QualityStatus { get; set; } = "unknown";
    public string QualityCacheKey { get; set; } = string.Empty;
    public bool CacheHit { get; set; }
}

public sealed class AssessmentGrammarDto
{
    public Guid? DraftId { get; set; }
    public Guid ConceptGraphSnapshotId { get; set; }
    public string IntentHash { get; set; } = string.Empty;
    public int RequestedQuestionCount { get; set; }
    public List<AssessmentItemSpecDto> Items { get; set; } = [];
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AssessmentItemSpecDto
{
    public Guid AssessmentItemId { get; set; }
    public string AssessmentItemKey { get; set; } = string.Empty;
    public string ConceptKey { get; set; } = string.Empty;
    public string ConceptLabel { get; set; } = string.Empty;
    public string CognitiveSkill { get; set; } = "conceptual";
    public string Difficulty { get; set; } = "orta";
    public string MisconceptionTarget { get; set; } = string.Empty;
    public string EvidenceExpected { get; set; } = string.Empty;
    public List<string> LearningOutcomeKeys { get; set; } = [];
    public List<string> OptionQualityRules { get; set; } = [];
    public string ScoringRule { get; set; } = "selected_option_exact_match";
    public int Order { get; set; }
}

public sealed class AssessmentGrammarDraftDto
{
    public AssessmentGrammarDto Grammar { get; set; } = new();
    public Guid DraftId { get; set; }
    public string CacheKey { get; set; } = string.Empty;
    public Guid? QualityRunId { get; set; }
    public string QualityStatus { get; set; } = "unknown";
    public string QualityCacheKey { get; set; } = string.Empty;
    public bool CacheHit { get; set; }
}

public sealed class DiagnosticProfileDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? QuizRunId { get; set; }
    public Guid PlanRequestId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public int AnsweredCount { get; set; }
    public int CorrectCount { get; set; }
    public int AccuracyPercent { get; set; }
    public string MeasuredLevel { get; set; } = "beginner";
    public List<ConceptMasteryDto> ConceptMasteries { get; set; } = [];
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ConceptMasteryDto
{
    public Guid? Id { get; set; }
    public string ConceptKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal MasteryScore { get; set; }
    public decimal Confidence { get; set; }
    public string RemediationNeed { get; set; } = "none";
    public string PracticeReadiness { get; set; } = "guided";
    public List<string> MisconceptionEvidence { get; set; } = [];
    public int Attempts { get; set; }
    public int Correct { get; set; }
}

public sealed class MisconceptionSignalDto
{
    public string Category { get; set; } = "unknown";
    public string UserSafeLabel { get; set; } = "Yanılgı sinyali belirsiz";
    public decimal Confidence { get; set; }
    public string ConfidenceStatus { get; set; } = "observed_only";
    public Guid? TopicId { get; set; }
    public string ConceptKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string SafeHint { get; set; } = "Kısa bir telafi sorusu ile kontrol etmek güvenli olur.";
    public IReadOnlyList<string> EvidenceBasis { get; set; } = Array.Empty<string>();
}

public sealed class LearningSignalConfidenceDto
{
    public string Status { get; set; } = "observed_only";
    public decimal Confidence { get; set; }
    public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();
}

public sealed class RemediationSeedDto
{
    public string ConceptKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public Guid? TopicId { get; set; }
    public string Reason { get; set; } = "Bu kavram için kısa telafi iyi olabilir.";
    public decimal Confidence { get; set; }
    public string ConfidenceStatus { get; set; } = "observed_only";
    public string MisconceptionCategory { get; set; } = "unknown";
    public string UserSafeMisconceptionLabel { get; set; } = "Yanılgı sinyali belirsiz";
    public string FirstAction { get; set; } = "tutor_explain";
    public IReadOnlyList<string> SecondaryActions { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> EvidenceBasis { get; set; } = Array.Empty<string>();
}

public sealed class LearningMemoryLiteDto
{
    public string Summary { get; set; } = "Henüz yeterli öğrenme sinyali yok. Quiz, chat ve Wiki kullandıkça profil oluşur.";
    public string ConfidenceStatus { get; set; } = "observed_only";
    public IReadOnlyList<LearningMemoryTopicDto> StrongTopics { get; set; } = Array.Empty<LearningMemoryTopicDto>();
    public IReadOnlyList<LearningMemoryTopicDto> WeakTopics { get; set; } = Array.Empty<LearningMemoryTopicDto>();
    public IReadOnlyList<LearningMemoryConceptDto> WeakConcepts { get; set; } = Array.Empty<LearningMemoryConceptDto>();
    public IReadOnlyList<LearningMemoryConceptDto> RecentMisconceptions { get; set; } = Array.Empty<LearningMemoryConceptDto>();
    public IReadOnlyList<LearningMemoryConceptDto> RemediationReadyItems { get; set; } = Array.Empty<LearningMemoryConceptDto>();
    public LearningMemoryConfidenceSummaryDto ConfidenceSummary { get; set; } = new();
    public string SourceReadiness { get; set; } = "unknown";
    public IReadOnlyList<string> RecentProgressSignals { get; set; } = Array.Empty<string>();
    public GoalReadinessDto GoalReadiness { get; set; } = new();
    public DateTimeOffset LastUpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool HasEnoughSignals { get; set; }
}

public sealed class LearningMemoryTopicDto
{
    public Guid? TopicId { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal? MasteryProbability { get; set; }
    public decimal? Confidence { get; set; }
    public string ConfidenceStatus { get; set; } = "observed_only";
    public string UserSafeReason { get; set; } = "Öğrenme sinyali izleniyor.";
    public IReadOnlyList<string> EvidenceBasis { get; set; } = Array.Empty<string>();
}

public sealed class LearningMemoryConceptDto
{
    public Guid? TopicId { get; set; }
    public string ConceptKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal? Confidence { get; set; }
    public string ConfidenceStatus { get; set; } = "observed_only";
    public string UserSafeReason { get; set; } = "Bu kavram için daha fazla kanıt gerekiyor.";
    public IReadOnlyList<string> EvidenceBasis { get; set; } = Array.Empty<string>();
    public RemediationSeedDto? RemediationSeed { get; set; }
}

public sealed class LearningMemoryConfidenceSummaryDto
{
    public int UsableSignalCount { get; set; }
    public int ObservedOnlySignalCount { get; set; }
    public int IgnoredSignalCount { get; set; }
    public int StrongAreaCount { get; set; }
    public int WeakAreaCount { get; set; }
    public string UserSafeSummary { get; set; } = "Öğrenme kanıtı oluşuyor.";
}

public sealed class GoalReadinessDto
{
    public string ObservedLevel { get; set; } = "unknown";
    public decimal ObservedLevelConfidence { get; set; }
    public IReadOnlyList<LearningMemoryConceptDto> PlannerReadyWeakAreas { get; set; } = Array.Empty<LearningMemoryConceptDto>();
    public IReadOnlyList<LearningMemoryTopicDto> PlannerReadyStrengths { get; set; } = Array.Empty<LearningMemoryTopicDto>();
    public IReadOnlyList<string> PlannerWarnings { get; set; } = Array.Empty<string>();
    public bool NeedsMoreEvidence { get; set; } = true;
    public IReadOnlyList<string> SuggestedDiagnosticFocus { get; set; } = Array.Empty<string>();
}

public sealed class AdaptiveStudyPlanRequestDto
{
    public string GoalType { get; set; } = "general_learning";
    public DateTimeOffset? TargetDate { get; set; }
    public int WeeklyAvailableMinutes { get; set; } = 180;
    public string CurrentLevel { get; set; } = "unknown";
    public string? ExamName { get; set; }
    public string? CareerTarget { get; set; }
    public IReadOnlyList<Guid> PriorityTopicIds { get; set; } = Array.Empty<Guid>();
    public IReadOnlyList<string> PrioritySkills { get; set; } = Array.Empty<string>();
}

public sealed class AdaptiveStudyPlanDto
{
    public string Summary { get; set; } = "Bugün için kısa ve güvenli bir çalışma rotası hazır.";
    public int WindowDays { get; set; } = 7;
    public IReadOnlyList<AdaptiveStudyPlanItemDto> Items { get; set; } = Array.Empty<AdaptiveStudyPlanItemDto>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public DiagnosticResultDto Diagnostic { get; set; } = new();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool HasEnoughSignals { get; set; }
}

public sealed class AdaptiveStudyPlanItemDto
{
    public string Title { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public Guid? TopicId { get; set; }
    public string ActionType { get; set; } = "continue_lesson";
    public int EstimatedMinutes { get; set; } = 20;
    public int Priority { get; set; }
    public IReadOnlyList<string> EvidenceBasis { get; set; } = Array.Empty<string>();
    public string ConfidenceStatus { get; set; } = "observed_only";
}

public sealed class DiagnosticIntakeDto
{
    public string SelfDeclaredLevel { get; set; } = "unknown";
    public string ObservedLevel { get; set; } = "unknown";
    public decimal ObservedLevelConfidence { get; set; }
    public bool NeedsMoreEvidence { get; set; } = true;
    public IReadOnlyList<string> WeakAreas { get; set; } = Array.Empty<string>();
}

public sealed class DiagnosticResultDto
{
    public DiagnosticIntakeDto Intake { get; set; } = new();
    public string RecommendedStartingPoint { get; set; } = "Kısa seviye tespiti ile başla.";
    public bool ShouldRunDiagnostic { get; set; } = true;
    public string UserSafeReason { get; set; } = "Bu konuda yeterli sinyal yok; kısa seviye tespiti iyi olur.";
}

public sealed class LearningEventDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? QuizAttemptId { get; set; }
    public Guid? AssessmentItemId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Actor { get; set; } = "learner";
    public string Verb { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string? ObjectId { get; set; }
    public string? ConceptKey { get; set; }
    public string? SkillTag { get; set; }
    public int? Score { get; set; }
    public bool? IsPositive { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TutorPolicyContextDto
{
    public Guid? TraceId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string ActiveConceptKey { get; set; } = string.Empty;
    public string ActiveConceptLabel { get; set; } = string.Empty;
    public string LearnerState { get; set; } = "unknown";
    public string NextPedagogicalMove { get; set; } = "ask a short diagnostic check before advancing";
    public string GroundingStatus { get; set; } = "model_only";
    public int SourceEvidenceCount { get; set; }
    public Chat.EvidenceQualityDto? EvidenceQuality { get; set; }
    public bool DirectAnswerRisk { get; set; }
    public List<string> PolicyViolations { get; set; } = [];
    public List<string> SourceEvidence { get; set; } = [];
    public List<string> RecentMistakes { get; set; } = [];
    public string PromptBlock { get; set; } = string.Empty;
}

public sealed class ConceptGraphQualityDto
{
    public Guid Id { get; set; }
    public Guid ConceptGraphSnapshotId { get; set; }
    public Guid? PlanRequestId { get; set; }
    public string QualityStatus { get; set; } = "unknown";
    public int ConceptCount { get; set; }
    public decimal DuplicateRatio { get; set; }
    public bool HasPrerequisiteCycle { get; set; }
    public int OrphanConceptCount { get; set; }
    public decimal OutcomeCoverage { get; set; }
    public decimal MisconceptionCoverage { get; set; }
    public decimal SourceEvidenceRatio { get; set; }
    public decimal RelationDensity { get; set; }
    public List<string> Failures { get; set; } = [];
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AssessmentQualityDto
{
    public Guid Id { get; set; }
    public Guid? AssessmentDraftId { get; set; }
    public Guid? PlanRequestId { get; set; }
    public Guid? QuizRunId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string QualityStatus { get; set; } = "unknown";
    public decimal ConceptCoverage { get; set; }
    public decimal LearningOutcomeCoverage { get; set; }
    public int CognitiveSkillSpread { get; set; }
    public int DifficultySpread { get; set; }
    public decimal MisconceptionTargetingRatio { get; set; }
    public decimal OptionQualityRatio { get; set; }
    public decimal ScoringRulePresenceRatio { get; set; }
    public List<string> Failures { get; set; } = [];
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AssessmentItemStatDto
{
    public Guid Id { get; set; }
    public Guid AssessmentItemId { get; set; }
    public int Attempts { get; set; }
    public int Correct { get; set; }
    public int Incorrect { get; set; }
    public int Skipped { get; set; }
    public decimal CorrectRate { get; set; }
    public decimal DiscriminationProxy { get; set; }
    public decimal TotalTimeSeconds { get; set; }
    public decimal LastResponseTimeSeconds { get; set; }
    public decimal AverageTimeSeconds { get; set; }
    public decimal SkipRate { get; set; }
    public string QualityStatus { get; set; } = "unknown";
    public decimal DifficultyEstimate { get; set; }
    public decimal DiscriminationEstimate { get; set; }
    public int ExposureCount { get; set; }
    public DateTimeOffset? LastSelectedAt { get; set; }
    public string CalibrationStatus { get; set; } = "uncalibrated";
}

public sealed class AssessmentCalibrationRunDto
{
    public Guid Id { get; set; }
    [JsonIgnore]
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string CalibrationStatus { get; set; } = "unknown";
    public string AdaptiveReadiness { get; set; } = "unknown";
    public string ItemBankHealth { get; set; } = "unknown";
    public int ItemCount { get; set; }
    public int HealthyItemCount { get; set; }
    public int ConceptCount { get; set; }
    public int ReadyConceptCount { get; set; }
    public decimal AverageDifficulty { get; set; }
    public decimal AverageDiscrimination { get; set; }
    public decimal AverageExposure { get; set; }
    public IReadOnlyList<AssessmentCalibrationItemDto> Items { get; set; } = Array.Empty<AssessmentCalibrationItemDto>();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AssessmentCalibrationItemDto
{
    public Guid Id { get; set; }
    public Guid AssessmentItemId { get; set; }
    public string ConceptKey { get; set; } = string.Empty;
    public decimal DifficultyEstimate { get; set; }
    public decimal DiscriminationEstimate { get; set; }
    public int ExposureCount { get; set; }
    public int EvidenceCount { get; set; }
    public string CalibrationStatus { get; set; } = "uncalibrated";
    public string Reason { get; set; } = string.Empty;
}

public sealed class AdaptiveAssessmentStartRequest
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public int? MinItems { get; set; }
    public int? MaxItems { get; set; }
    public IReadOnlyList<string>? TargetConceptKeys { get; set; }
    public string? AssessmentMode { get; set; }
}

public sealed class AdaptiveAssessmentAnswerRequest : RecordQuizAttemptRequest
{
    public Guid DecisionId { get; set; }
}

public sealed class AdaptiveAssessmentSessionDto
{
    public Guid Id { get; set; }
    [JsonIgnore]
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? QuizRunId { get; set; }
    public string Status { get; set; } = "active";
    public IReadOnlyList<string> TargetConcepts { get; set; } = Array.Empty<string>();
    public string StopReason { get; set; } = string.Empty;
    public int MinItems { get; set; }
    public int MaxItems { get; set; }
    public int AnsweredCount { get; set; }
    public int CorrectCount { get; set; }
    public string AssessmentMode { get; set; } = "retrieval_practice";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AdaptiveAssessmentNextItemDto
{
    public Guid SessionId { get; set; }
    public string Status { get; set; } = "active";
    public bool IsComplete { get; set; }
    public string StopReason { get; set; } = string.Empty;
    public AdaptiveAssessmentDecisionDto? Decision { get; set; }
    public QuizResultLearningImpactDto? LatestLearningImpact { get; set; }
}

public sealed class AdaptiveAssessmentDecisionDto
{
    public Guid Id { get; set; }
    public Guid AssessmentItemId { get; set; }
    public string ConceptKey { get; set; } = string.Empty;
    public decimal SelectionScore { get; set; }
    public decimal MasteryProbability { get; set; }
    public decimal MasteryConfidence { get; set; }
    public decimal ItemQualityScore { get; set; }
    public decimal ExposurePenalty { get; set; }
    public string DecisionReason { get; set; } = string.Empty;
    public string AssessmentMode { get; set; } = "retrieval_practice";
    public QuizDataDto Question { get; set; } = new();
}

public sealed class QuizDataDto
{
    public string Type { get; set; } = "multiple_choice";
    public Guid? QuizRunId { get; set; }
    public string QuestionId { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public IReadOnlyList<QuizOptionDto> Options { get; set; } = Array.Empty<QuizOptionDto>();
    public string Explanation { get; set; } = string.Empty;
    public string? Topic { get; set; }
    public string? SkillTag { get; set; }
    public string? TopicPath { get; set; }
    public string? Difficulty { get; set; }
    public string? CognitiveType { get; set; }
    public Guid? AssessmentItemId { get; set; }
    public string? AssessmentItemKey { get; set; }
    public string? ConceptKey { get; set; }
    public string? ConceptTag { get; set; }
    public string? CognitiveSkill { get; set; }
    public string? AssessmentMode { get; set; }
    public string? MisconceptionTarget { get; set; }
    public string? EvidenceExpected { get; set; }
    public string? ScoringRule { get; set; }
    public IReadOnlyList<string> LearningOutcomeIds { get; set; } = Array.Empty<string>();
    public string? SourceReadiness { get; set; }
    public string? WikiReviewHint { get; set; }
    public string? QuestionHash { get; set; }
}

public sealed record QuizOptionDto(
    string Id,
    string Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool IsCorrect);

public sealed class KnowledgeTracingStateDto
{
    public Guid Id { get; set; }
    [JsonIgnore]
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string ConceptKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal PriorMastery { get; set; }
    public decimal LearnRate { get; set; }
    public decimal Slip { get; set; }
    public decimal Guess { get; set; }
    public decimal Decay { get; set; }
    public int EvidenceCount { get; set; }
    public int CorrectCount { get; set; }
    public int IncorrectCount { get; set; }
    public decimal MasteryProbability { get; set; }
    public decimal Confidence { get; set; }
    public string RemediationNeed { get; set; } = "none";
    public string PracticeReadiness { get; set; } = "guided";
    public DateTimeOffset? LastEvidenceAt { get; set; }
}

public sealed class TutorPolicyTraceDto
{
    public Guid Id { get; set; }
    [JsonIgnore]
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string ActiveConceptKey { get; set; } = string.Empty;
    public string LearnerState { get; set; } = "unknown";
    public string RemediationNeed { get; set; } = "unknown";
    public string GroundingStatus { get; set; } = "model_only";
    public string SelectedPedagogicalMove { get; set; } = string.Empty;
    public int SourceEvidenceCount { get; set; }
    public bool DirectAnswerRisk { get; set; }
    public List<string> PolicyViolations { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LearningEventSchemaValidationDto
{
    public bool IsValid { get; set; }
    public string NormalizedEventType { get; set; } = string.Empty;
    public List<string> Violations { get; set; } = [];
}

public sealed class ResourceConceptAlignmentDto
{
    public Guid Id { get; set; }
    public Guid ConceptGraphSnapshotId { get; set; }
    public Guid? TopicId { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string SourceTitle { get; set; } = string.Empty;
    public string SourceUri { get; set; } = string.Empty;
    public string ConceptKey { get; set; } = string.Empty;
    public string OutcomeKey { get; set; } = string.Empty;
    public decimal AlignmentScore { get; set; }
    public string EvidenceSnippet { get; set; } = string.Empty;
    public string AlignmentStatus { get; set; } = "weak";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LearningQualityReportDto
{
    public Guid Id { get; set; }
    [JsonIgnore]
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public Guid? PlanRequestId { get; set; }
    public string QualityStatus { get; set; } = "unknown";
    public string GraphQualityStatus { get; set; } = "unknown";
    public string AssessmentQualityStatus { get; set; } = "unknown";
    public string MasteryConfidenceStatus { get; set; } = "unknown";
    public string TutorPolicyComplianceStatus { get; set; } = "unknown";
    public string EventHealthStatus { get; set; } = "unknown";
    public string SourceGroundingStatus { get; set; } = "unknown";
    public string ToolExecutionHealthStatus { get; set; } = "unknown";
    public string ArtifactRenderHealthStatus { get; set; } = "unknown";
    public string LearnerEvidenceStatus { get; set; } = "unknown";
    public string RagQualityStatus { get; set; } = "unknown";
    public string EvidenceCoverageStatus { get; set; } = "unknown";
    public string EvidenceProviderHealthStatus { get; set; } = "unknown";
    public string EvidenceFreshnessStatus { get; set; } = "unknown";
    public string ForumSignalUsageStatus { get; set; } = "none";
    public string EvidenceCitationCoverageStatus { get; set; } = "unknown";
    public string TutorPedagogyStatus { get; set; } = "unknown";
    public string AssessmentCalibrationStatus { get; set; } = "unknown";
    public string AdaptiveReadiness { get; set; } = "unknown";
    public string ItemBankHealth { get; set; } = "unknown";
    public string TraceHealth { get; set; } = "unknown";
    public string StandardsAlignmentStatus { get; set; } = "unknown";
    public decimal CaseLikeCoverage { get; set; }
    public decimal QtiLikeCoverage { get; set; }
    public decimal CaliperXapiCoverage { get; set; }
    public decimal? TutorPedagogyScore { get; set; }
    public int CriticalPedagogyViolationCount { get; set; }
    public ConceptGraphQualityDto? GraphQuality { get; set; }
    public AssessmentQualityDto? AssessmentQuality { get; set; }
    public List<KnowledgeTracingStateDto> MasteryStates { get; set; } = [];
    public List<TutorPolicyTraceDto> RecentTutorPolicyTraces { get; set; } = [];
    public IReadOnlyList<TutorPedagogyRubricScoreDto> RecentPedagogyRubricScores { get; set; } = Array.Empty<TutorPedagogyRubricScoreDto>();
    public int EventSchemaViolationCount { get; set; }
    public int PolicyViolationCount { get; set; }
    public IReadOnlyList<TutorToolCallDto> RecentToolCalls { get; set; } = Array.Empty<TutorToolCallDto>();
    public IReadOnlyList<TeachingArtifactDto> RecentArtifacts { get; set; } = Array.Empty<TeachingArtifactDto>();
    public IReadOnlyList<TeachingEvidenceCardDto> RecentEvidenceCards { get; set; } = Array.Empty<TeachingEvidenceCardDto>();
    public RagEvaluationRunDto? LatestRagEvaluation { get; set; }
    public SourceQualityReportDto? SourceQuality { get; set; }
    public AssessmentCalibrationRunDto? AssessmentCalibration { get; set; }
    public IReadOnlyList<TutorTraceTimelineEventDto> RecentTutorTraceEvents { get; set; } = Array.Empty<TutorTraceTimelineEventDto>();
    public StandardsSummaryDto? StandardsSummary { get; set; }
    public List<ResourceConceptAlignmentDto> ResourceAlignments { get; set; } = [];
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StandardsSummaryDto
{
    [JsonIgnore]
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string StandardsAlignmentStatus { get; set; } = "unknown";
    public decimal CaseCoverage { get; set; }
    public decimal QtiCoverage { get; set; }
    public decimal CaliperXapiCoverage { get; set; }
    public int OutcomeCount { get; set; }
    public int ConceptCount { get; set; }
    public int AssessmentItemCount { get; set; }
    public int LearningEventCount { get; set; }
    public int IssueCount { get; set; }
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<StandardsValidationItemDto> RecentIssues { get; set; } = Array.Empty<StandardsValidationItemDto>();
}

public sealed class StandardsValidationRunDto
{
    public Guid Id { get; set; }
    [JsonIgnore]
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string Status { get; set; } = "unknown";
    public decimal CaseCoverage { get; set; }
    public decimal QtiCoverage { get; set; }
    public decimal CaliperXapiCoverage { get; set; }
    public int CheckedItemCount { get; set; }
    public int IssueCount { get; set; }
    public IReadOnlyList<StandardsValidationItemDto> Issues { get; set; } = Array.Empty<StandardsValidationItemDto>();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StandardsValidationItemDto
{
    public Guid Id { get; set; }
    public string StandardFamily { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityKey { get; set; } = string.Empty;
    public string Severity { get; set; } = "warning";
    public string IssueCode { get; set; } = string.Empty;
    public string UserSafeMessage { get; set; } = string.Empty;
}

public sealed class StandardsExportRunDto
{
    public Guid Id { get; set; }
    [JsonIgnore]
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string ExportType { get; set; } = "combined";
    public string Status { get; set; } = "ready";
    public int ItemCount { get; set; }
    public decimal CaseCoverage { get; set; }
    public decimal QtiCoverage { get; set; }
    public decimal CaliperXapiCoverage { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RagEvaluationRunDto
{
    public Guid Id { get; set; }
    [JsonIgnore]
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string QualityStatus { get; set; } = "unknown";
    public decimal FaithfulnessScore { get; set; }
    public decimal ContextRelevanceScore { get; set; }
    public decimal AnswerRelevanceScore { get; set; }
    public decimal CitationCoverageScore { get; set; }
    public int ItemCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
