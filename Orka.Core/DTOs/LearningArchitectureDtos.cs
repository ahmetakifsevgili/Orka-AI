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
    public LearningMemoryHygieneDto Hygiene { get; set; } = new();
    public DateTimeOffset LastUpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool HasEnoughSignals { get; set; }
}

public sealed class LearningMemoryHygieneDto
{
    public string MemoryStatus { get; set; } = "observed_only";
    public int RetainedSignalCount { get; set; }
    public int MergedWeakConceptCount { get; set; }
    public int RepairPendingCount { get; set; }
    public int StaleSignalCount { get; set; }
    public IReadOnlyList<string> RetainedSignals { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> MergedSignals { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public string StudentVisibleSummary { get; set; } = "Ogrenme hafizasi guvenli ozetlerle tutuluyor.";
    public string NextAction { get; set; } = "continue_learning";
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

public sealed class OrkaLearningStateDto
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string ScopeStatus { get; set; } = "global";
    public OrkaLearningSignalSummaryDto SignalSummary { get; set; } = new();
    public DashboardSourceHealthDto SourceHealth { get; set; } = new();
    public LongTermLearningProfileDto LongTermLearningProfile { get; set; } = new();
    public ExamLearningProfileDto? ExamLearningProfile { get; set; }
    public SourceWikiIntelligenceProfileDto? SourceWikiIntelligenceProfile { get; set; }
    public OrkaUnifiedNextActionDto PrimaryNextAction { get; set; } = new();
    public IReadOnlyList<OrkaUnifiedNextActionDto> SecondaryNextActions { get; set; } = Array.Empty<OrkaUnifiedNextActionDto>();
    public IReadOnlyList<OrkaFeatureReadinessDto> FeatureReadiness { get; set; } = Array.Empty<OrkaFeatureReadinessDto>();
    public IReadOnlyList<OrkaLearningStateConflictDto> ConflictWarnings { get; set; } = Array.Empty<OrkaLearningStateConflictDto>();
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> SafetyWarnings { get; set; } = Array.Empty<string>();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OrkaUnifiedNextActionDto
{
    public string ActionType { get; set; } = "continue_plan";
    public string Label { get; set; } = "Plana devam et";
    public string Reason { get; set; } = "Mevcut kanitlarla plana devam etmek uygun.";
    public string Priority { get; set; } = "normal";
    public Guid? TopicId { get; set; }
    public string? ConceptKey { get; set; }
    public string Source { get; set; } = "orka_state";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> AppliesTo { get; set; } = Array.Empty<string>();
}

public sealed class OrkaLearningSignalSummaryDto
{
    public int EvidenceCount { get; set; }
    public int QuizAttemptCount { get; set; }
    public int CorrectAttemptCount { get; set; }
    public int WrongAttemptCount { get; set; }
    public int BlankOrSkippedAttemptCount { get; set; }
    public int DueReviewCount { get; set; }
    public int LearningSignalCount { get; set; }
    public int SourceCount { get; set; }
    public int ReadySourceCount { get; set; }
    public int WikiPageCount { get; set; }
    public int StudyRoomSessionCount { get; set; }
    public int StudyRoomQuestionCount { get; set; }
    public bool HasRealLearningData { get; set; }
}

public sealed class OrkaFeatureReadinessDto
{
    public string FeatureKey { get; set; } = string.Empty;
    public string Status { get; set; } = "not_available";
    public string UserSafeSummary { get; set; } = string.Empty;
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class OrkaLearningStateConflictDto
{
    public string ConflictCode { get; set; } = string.Empty;
    public string Severity { get; set; } = "warning";
    public string UserSafeSummary { get; set; } = string.Empty;
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class OrkaMissionControlDto
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string ScopeStatus { get; set; } = "global";
    public OrkaTodayMissionDto PrimaryMission { get; set; } = new();
    public string PrimaryEntryPoint { get; set; } = "ask_tutor";
    public IReadOnlyList<OrkaMissionActionDto> SecondaryActions { get; set; } = Array.Empty<OrkaMissionActionDto>();
    public IReadOnlyList<OrkaMissionWarningDto> UrgentWarnings { get; set; } = Array.Empty<OrkaMissionWarningDto>();
    public string TodayFocus { get; set; } = "Kisa tani ile basla";
    public string ReviewLoad { get; set; } = "none";
    public string RepairLoad { get; set; } = "none";
    public string ExamLoad { get; set; } = "none";
    public string SourceWikiLoad { get; set; } = "none";
    public OrkaMissionActionDto? StudyRoomSuggestion { get; set; }
    public IReadOnlyList<OrkaMissionModuleCardDto> ModuleCards { get; set; } = Array.Empty<OrkaMissionModuleCardDto>();
    public IReadOnlyList<OrkaMissionSectionDto> Sections { get; set; } = Array.Empty<OrkaMissionSectionDto>();
    public string EvidenceConfidence { get; set; } = "thin_evidence";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public string UserSafeSummary { get; set; } = "Orka bugunku en guvenli calisma adimini hazirladi.";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OrkaTodayMissionDto
{
    public string MissionKey { get; set; } = "start_here";
    public string ActionType { get; set; } = "start_diagnostic";
    public string Label { get; set; } = "Kisa tani ile basla";
    public string Reason { get; set; } = "Henuz yeterli ogrenme kaniti yok.";
    public string Priority { get; set; } = "high";
    public string EntryPoint { get; set; } = "ask_tutor";
    public string TargetRoute { get; set; } = "chat";
    public Guid? TopicId { get; set; }
    public string? ConceptKey { get; set; }
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class OrkaMissionActionDto
{
    public string ActionType { get; set; } = "continue_plan";
    public string Label { get; set; } = "Plana devam et";
    public string Reason { get; set; } = "Mevcut kanitlarla plana devam etmek uygun.";
    public string Priority { get; set; } = "normal";
    public string EntryPoint { get; set; } = "ask_tutor";
    public string TargetRoute { get; set; } = "chat";
    public Guid? TopicId { get; set; }
    public string? ConceptKey { get; set; }
    public bool IsPrimary { get; set; }
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class OrkaMissionSectionDto
{
    public string SectionKey { get; set; } = "start_here";
    public string Status { get; set; } = "empty";
    public string Label { get; set; } = "Baslangic";
    public int Priority { get; set; }
    public string TargetRoute { get; set; } = "chat";
    public IReadOnlyList<OrkaMissionActionDto> Actions { get; set; } = Array.Empty<OrkaMissionActionDto>();
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<OrkaMissionWarningDto> Warnings { get; set; } = Array.Empty<OrkaMissionWarningDto>();
}

public sealed class OrkaMissionModuleCardDto
{
    public string ModuleKey { get; set; } = string.Empty;
    public string Status { get; set; } = "empty";
    public string Label { get; set; } = string.Empty;
    public string EntryPoint { get; set; } = "ask_tutor";
    public string TargetRoute { get; set; } = "chat";
    public string Priority { get; set; } = "normal";
    public string UserSafeSummary { get; set; } = string.Empty;
    public int ActionCount { get; set; }
    public int WarningCount { get; set; }
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class OrkaMissionWarningDto
{
    public string WarningCode { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string Label { get; set; } = string.Empty;
    public string TargetRoute { get; set; } = "dashboard";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class OrkaStudyCoachDto
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string ScopeStatus { get; set; } = "global";
    public string RhythmStatus { get; set; } = "thin_evidence";
    public string RecommendedPace { get; set; } = "light";
    public string TodayPlan { get; set; } = "Kisa kontrol ile basla";
    public string WeeklyPlan { get; set; } = "Ogrenme kaniti biriktir";
    public OrkaStudyLoadDto Workload { get; set; } = new();
    public OrkaFocusPlanDto FocusPlan { get; set; } = new();
    public OrkaComebackPlanDto ComebackPlan { get; set; } = new();
    public IReadOnlyList<OrkaStudyCoachActionDto> Actions { get; set; } = Array.Empty<OrkaStudyCoachActionDto>();
    public IReadOnlyList<OrkaStudyCoachWarningDto> Warnings { get; set; } = Array.Empty<OrkaStudyCoachWarningDto>();
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public string UserSafeSummary { get; set; } = "Orka bugunku calisma ritmini guvenli ogrenme kanitlarindan hazirladi.";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OrkaStudyLoadDto
{
    public string ReviewLoad { get; set; } = "none";
    public string RepairLoad { get; set; } = "none";
    public string ExamLoad { get; set; } = "none";
    public string SourceWikiLoad { get; set; } = "none";
    public string NewLearningLoad { get; set; } = "light";
    public string OverallLoad { get; set; } = "light";
    public int LoadScore { get; set; }
}

public sealed class OrkaStudyCoachActionDto
{
    public string ActionType { get; set; } = "continue_plan";
    public string Label { get; set; } = "Plana devam et";
    public string Reason { get; set; } = "Mevcut ogrenme ritmine uygun.";
    public string Priority { get; set; } = "normal";
    public string EntryPoint { get; set; } = "ask_tutor";
    public string TargetRoute { get; set; } = "chat";
    public string DurationBand { get; set; } = "short";
    public Guid? TopicId { get; set; }
    public string? ConceptKey { get; set; }
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class OrkaStudyCoachWarningDto
{
    public string WarningCode { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string Label { get; set; } = string.Empty;
    public string TargetRoute { get; set; } = "dashboard";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class OrkaFocusPlanDto
{
    public string FocusMode { get; set; } = "quick_start";
    public string DurationBand { get; set; } = "short";
    public string EntryPoint { get; set; } = "ask_tutor";
    public string TargetRoute { get; set; } = "chat";
    public IReadOnlyList<string> Steps { get; set; } = Array.Empty<string>();
    public string StopCondition { get; set; } = "after short diagnostic";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class OrkaComebackPlanDto
{
    public string ComebackStatus { get; set; } = "thin_evidence";
    public string FirstStep { get; set; } = "Kisa kontrol ile basla";
    public string SecondStep { get; set; } = "Sonuca gore tek sonraki adimi sec";
    public string AvoidToday { get; set; } = "Ayni anda tum kuyrugu kapatmaya calisma.";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public string UserSafeSummary { get; set; } = "Donus plani pratik calisma adimlariyla sinirlidir.";
}

public sealed class LongTermLearningProfileDto
{
    public string Summary { get; set; } = "Henüz uzun vadeli öğrenme ritmi için yeterli kanıt yok.";
    public int WindowDays { get; set; } = 7;
    public bool HasEnoughEvidence { get; set; }
    public int EvidenceCount { get; set; }
    public IReadOnlyList<LongTermLearningConceptDto> Concepts { get; set; } = Array.Empty<LongTermLearningConceptDto>();
    public IReadOnlyList<AdaptiveReviewPressureDto> ReviewPressure { get; set; } = Array.Empty<AdaptiveReviewPressureDto>();
    public AdaptiveLearningRhythmDto WeeklyRhythm { get; set; } = new();
    public IReadOnlyList<AdaptiveNextStudyActionDto> NextActions { get; set; } = Array.Empty<AdaptiveNextStudyActionDto>();
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LongTermLearningConceptDto
{
    public Guid? TopicId { get; set; }
    public string ConceptKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string State { get; set; } = "new";
    public decimal? MasteryProbability { get; set; }
    public decimal? Confidence { get; set; }
    public string ConfidenceStatus { get; set; } = "observed_only";
    public int EvidenceCount { get; set; }
    public int CorrectCount { get; set; }
    public int WrongCount { get; set; }
    public int BlankOrSkippedCount { get; set; }
    public int RepairCount { get; set; }
    public DateTimeOffset? LastPracticedAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public DateTimeOffset? LastFailureAt { get; set; }
    public string ReviewPriority { get; set; } = "none";
    public string RecommendedAction { get; set; } = "continue_plan";
    public string UserSafeReason { get; set; } = "Bu kavram izleniyor.";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> EvidenceBasis { get; set; } = Array.Empty<string>();
}

public sealed class AdaptiveReviewPressureDto
{
    public Guid? TopicId { get; set; }
    public string ConceptKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Priority { get; set; } = "none";
    public string RecommendedAction { get; set; } = "continue_plan";
    public string UserSafeReason { get; set; } = "Tekrar baskısı düşük.";
    public int DaysOverdue { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public string ConfidenceStatus { get; set; } = "observed_only";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> EvidenceBasis { get; set; } = Array.Empty<string>();
}

public sealed class AdaptiveLearningRhythmDto
{
    public string TodayFocus { get; set; } = "Kısa seviye tespiti";
    public string ThisWeekFocus { get; set; } = "Öğrenme kanıtı biriktir";
    public string ReviewLoad { get; set; } = "none";
    public string NewLearningLoad { get; set; } = "light";
    public string RepairLoad { get; set; } = "none";
    public IReadOnlyList<string> WeakConcepts { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> DueConcepts { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> StableConcepts { get; set; } = Array.Empty<string>();
    public AdaptiveNextStudyActionDto NextBestAction { get; set; } = new();
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

public sealed class AdaptiveNextStudyActionDto
{
    public string ActionType { get; set; } = "continue_plan";
    public string Label { get; set; } = "Plana devam et";
    public string Reason { get; set; } = "Mevcut kanıtlarla plana devam etmek uygun.";
    public Guid? TopicId { get; set; }
    public string? ConceptKey { get; set; }
    public string Priority { get; set; } = "normal";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class AdaptiveStudyPlanItemDto
{
    public string Title { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public Guid? TopicId { get; set; }
    public string? ConceptKey { get; set; }
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

public sealed class OrkaNotebookStudioProDto
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? WikiPageId { get; set; }
    public string? ExamCode { get; set; }
    public string? VariantCode { get; set; }
    public string ReadinessStatus { get; set; } = "thin_evidence";
    public string PackReadiness { get; set; } = "limited";
    public IReadOnlyList<NotebookStudioPackDto> RecommendedPacks { get; set; } = Array.Empty<NotebookStudioPackDto>();
    public NotebookStudioPackDto? ActivePack { get; set; }
    public IReadOnlyList<NotebookStudioArtifactDto> ArtifactQueue { get; set; } = Array.Empty<NotebookStudioArtifactDto>();
    public IReadOnlyList<NotebookStudioExportPreviewDto> ExportPreviews { get; set; } = Array.Empty<NotebookStudioExportPreviewDto>();
    public IReadOnlyList<NotebookStudioEvidenceLinkDto> SourceEvidenceLinks { get; set; } = Array.Empty<NotebookStudioEvidenceLinkDto>();
    public IReadOnlyList<NotebookStudioEvidenceLinkDto> WikiEvidenceLinks { get; set; } = Array.Empty<NotebookStudioEvidenceLinkDto>();
    public IReadOnlyList<NotebookStudioEvidenceLinkDto> ConceptLinks { get; set; } = Array.Empty<NotebookStudioEvidenceLinkDto>();
    public IReadOnlyList<NotebookStudioEvidenceLinkDto> ExamOutcomeLinks { get; set; } = Array.Empty<NotebookStudioEvidenceLinkDto>();
    public IReadOnlyList<NotebookStudioEvidenceLinkDto> StudyRoomTraceLinks { get; set; } = Array.Empty<NotebookStudioEvidenceLinkDto>();
    public IReadOnlyList<NotebookStudioPackActionDto> TutorHandoffs { get; set; } = Array.Empty<NotebookStudioPackActionDto>();
    public IReadOnlyList<NotebookStudioPackActionDto> ReviewHandoffs { get; set; } = Array.Empty<NotebookStudioPackActionDto>();
    public IReadOnlyList<NotebookStudioPackActionDto> SourceWikiHandoffs { get; set; } = Array.Empty<NotebookStudioPackActionDto>();
    public IReadOnlyList<NotebookStudioPackActionDto> ExamWarRoomHandoffs { get; set; } = Array.Empty<NotebookStudioPackActionDto>();
    public IReadOnlyList<NotebookStudioPackActionDto> StudyRoomHandoffs { get; set; } = Array.Empty<NotebookStudioPackActionDto>();
    public IReadOnlyList<NotebookStudioPackWarningDto> MissionControlWarnings { get; set; } = Array.Empty<NotebookStudioPackWarningDto>();
    public IReadOnlyList<NotebookStudioPackWarningDto> Warnings { get; set; } = Array.Empty<NotebookStudioPackWarningDto>();
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public string UserSafeSummary { get; set; } = "Notebook Studio Pro artifact calisma alanini guvenli metadata ile hazirladi.";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class NotebookStudioPackDto
{
    public Guid? PackId { get; set; }
    public string PackType { get; set; } = "artifact_collection";
    public string Status { get; set; } = "suggested";
    public string Title { get; set; } = "Artifact paketi";
    public string Summary { get; set; } = "Mevcut ogrenme kanitindan guvenli paket onerisi.";
    public string Priority { get; set; } = "normal";
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? WikiPageId { get; set; }
    public IReadOnlyList<string> ConceptKeys { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> WarningCodes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<NotebookStudioPackActionDto> Actions { get; set; } = Array.Empty<NotebookStudioPackActionDto>();
}

public sealed class NotebookStudioArtifactDto
{
    public Guid? ArtifactId { get; set; }
    public Guid? PackId { get; set; }
    public string ArtifactType { get; set; } = "study_guide";
    public string Status { get; set; } = "suggested";
    public string Origin { get; set; } = "notebook_studio_pro";
    public string RenderFormat { get; set; } = "metadata";
    public string Title { get; set; } = "Artifact";
    public string SourceBasis { get; set; } = "evidence_insufficient";
    public bool PreviewOnly { get; set; } = true;
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

public sealed class NotebookStudioPackActionDto
{
    public string ActionType { get; set; } = "continue_learning";
    public string Label { get; set; } = "Ogrenmeye devam et";
    public string Reason { get; set; } = "Mevcut kanitla guvenli devam edilebilir.";
    public string Priority { get; set; } = "normal";
    public string EntryPoint { get; set; } = "continue_learning";
    public string TargetRoute { get; set; } = "dashboard";
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? WikiPageId { get; set; }
    public Guid? PackId { get; set; }
    public Guid? ArtifactId { get; set; }
    public string? ConceptKey { get; set; }
    public string? ExamOutcomeKey { get; set; }
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class NotebookStudioPackWarningDto
{
    public string WarningCode { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string Label { get; set; } = string.Empty;
    public string Source { get; set; } = "notebook_studio_pro";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class NotebookStudioExportPreviewDto
{
    public string PreviewType { get; set; } = "manifest";
    public string ReadinessStatus { get; set; } = "preview_only";
    public Guid? PackId { get; set; }
    public Guid? ArtifactId { get; set; }
    public int ArtifactCount { get; set; }
    public string SourceWarning { get; set; } = "none";
    public string AccessibilityWarning { get; set; } = "review_required";
    public IReadOnlyList<string> ExportLimitations { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class NotebookStudioEvidenceLinkDto
{
    public string LinkType { get; set; } = "concept";
    public string Status { get; set; } = "limited";
    public string Label { get; set; } = string.Empty;
    public Guid? TopicId { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? WikiPageId { get; set; }
    public Guid? PackId { get; set; }
    public Guid? ArtifactId { get; set; }
    public string? ConceptKey { get; set; }
    public string? ExamOutcomeKey { get; set; }
    public string SourceBasis { get; set; } = "evidence_insufficient";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class OrkaStudyRoomDto
{
    public Guid? ClassroomSessionId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? WikiPageId { get; set; }
    public string? ExamCode { get; set; }
    public string? VariantCode { get; set; }
    public string SessionReadiness { get; set; } = "limited";
    public string StudyRoomMode { get; set; } = "quick_start";
    public string? SelectedTopic { get; set; }
    public string? SelectedConcept { get; set; }
    public string? SelectedExamOutcome { get; set; }
    public string SourceReadiness { get; set; } = "unknown";
    public string WikiReadiness { get; set; } = "unknown";
    public string RhythmStatus { get; set; } = "thin_evidence";
    public string RecommendedPace { get; set; } = "light";
    public OrkaStudyRoomPlanDto LessonPlan { get; set; } = new();
    public IReadOnlyList<OrkaStudyRoomRoleDto> Roles { get; set; } = Array.Empty<OrkaStudyRoomRoleDto>();
    public OrkaStudyRoomCheckpointDto CheckpointPlan { get; set; } = new();
    public OrkaStudyRoomTurnDto CurrentTurn { get; set; } = new();
    public string SafeStudentSummary { get; set; } = "Study Room guvenli calisma akisini hazirladi.";
    public IReadOnlyList<OrkaStudyRoomActionDto> NextActions { get; set; } = Array.Empty<OrkaStudyRoomActionDto>();
    public IReadOnlyList<OrkaStudyRoomActionDto> TutorHandoffs { get; set; } = Array.Empty<OrkaStudyRoomActionDto>();
    public IReadOnlyList<OrkaStudyRoomActionDto> QuizHandoffs { get; set; } = Array.Empty<OrkaStudyRoomActionDto>();
    public IReadOnlyList<OrkaStudyRoomActionDto> ReviewHandoffs { get; set; } = Array.Empty<OrkaStudyRoomActionDto>();
    public IReadOnlyList<OrkaStudyRoomActionDto> SourceWikiHandoffs { get; set; } = Array.Empty<OrkaStudyRoomActionDto>();
    public IReadOnlyList<OrkaStudyRoomActionDto> NotebookHandoffs { get; set; } = Array.Empty<OrkaStudyRoomActionDto>();
    public IReadOnlyList<OrkaStudyRoomWarningDto> Warnings { get; set; } = Array.Empty<OrkaStudyRoomWarningDto>();
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OrkaStudyRoomPlanDto
{
    public string PlanKey { get; set; } = "quick_start";
    public string Title { get; set; } = "Kisa baslangic";
    public string Objective { get; set; } = "Kanit biriktir ve tek sonraki adimi sec.";
    public string DurationBand { get; set; } = "short";
    public IReadOnlyList<string> Steps { get; set; } = Array.Empty<string>();
    public string StopCondition { get; set; } = "after short check";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class OrkaStudyRoomRoleDto
{
    public string RoleKey { get; set; } = "student";
    public string Label { get; set; } = "Student";
    public string Responsibility { get; set; } = "Cevap verir ve checkpoint sonucunu olusturur.";
}

public sealed class OrkaStudyRoomCheckpointDto
{
    public string CheckpointStatus { get; set; } = "not_started";
    public string Prompt { get; set; } = "Kisa checkpoint hazir.";
    public string ResponseSignal { get; set; } = "needs_review";
    public string PostSubmitFeedback { get; set; } = string.Empty;
    public bool KeyVisible { get; set; }
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class OrkaStudyRoomTurnDto
{
    public string TurnStatus { get; set; } = "planned";
    public string SpeakerRole { get; set; } = "ai_teacher";
    public string UserSafeSummary { get; set; } = "Ders plani hazir.";
    public string ResponseSignal { get; set; } = "needs_review";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class OrkaStudyRoomActionDto
{
    public string ActionType { get; set; } = "continue_plan";
    public string Label { get; set; } = "Plana devam et";
    public string Reason { get; set; } = "Mevcut kanitlarla normal akisa devam edilebilir.";
    public string Priority { get; set; } = "normal";
    public string EntryPoint { get; set; } = "continue_plan";
    public string TargetRoute { get; set; } = "dashboard";
    public Guid? TopicId { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? WikiPageId { get; set; }
    public string? ConceptKey { get; set; }
    public string? ExamOutcomeCode { get; set; }
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class OrkaStudyRoomWarningDto
{
    public string WarningCode { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string Label { get; set; } = string.Empty;
    public string TargetRoute { get; set; } = "classroom";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class OrkaStudyRoomStartRequestDto
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string? ExamCode { get; set; } = "KPSS";
    public string? VariantCode { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? WikiPageId { get; set; }
    public string? Mode { get; set; }
}

public sealed class OrkaStudyRoomCheckpointRequestDto
{
    public Guid ClassroomSessionId { get; set; }
    public string? ResponseSignal { get; set; }
    public string? AnswerText { get; set; }
    public bool Skipped { get; set; }
    public string? ConceptKey { get; set; }
}

public sealed class OrkaCodeLearningIdeDto
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string ReadinessStatus { get; set; } = "thin_evidence";
    public string Mode { get; set; } = "quick_start";
    public string ActiveLanguage { get; set; } = "csharp";
    public string? ActiveTopic { get; set; }
    public string? ActiveSkill { get; set; }
    public CodeLearningRuntimeReadinessDto RuntimeReadiness { get; set; } = new();
    public CodeLearningSessionDto Session { get; set; } = new();
    public CodeLearningExerciseDto ActiveExercise { get; set; } = new();
    public CodeLearningAttemptDto LastAttemptSummary { get; set; } = new();
    public CodeLearningErrorSummaryDto RepeatedErrorSummary { get; set; } = new();
    public string CheckpointStatus { get; set; } = "not_started";
    public string RepairStatus { get; set; } = "not_needed";
    public IReadOnlyList<CodeLearningActionDto> RecommendedActions { get; set; } = Array.Empty<CodeLearningActionDto>();
    public IReadOnlyList<CodeLearningHandoffDto> TutorHandoffs { get; set; } = Array.Empty<CodeLearningHandoffDto>();
    public IReadOnlyList<CodeLearningHandoffDto> QuizHandoffs { get; set; } = Array.Empty<CodeLearningHandoffDto>();
    public IReadOnlyList<CodeLearningHandoffDto> ReviewHandoffs { get; set; } = Array.Empty<CodeLearningHandoffDto>();
    public IReadOnlyList<CodeLearningHandoffDto> WikiHandoffs { get; set; } = Array.Empty<CodeLearningHandoffDto>();
    public IReadOnlyList<CodeLearningHandoffDto> NotebookHandoffs { get; set; } = Array.Empty<CodeLearningHandoffDto>();
    public IReadOnlyList<CodeLearningWarningDto> MissionControlWarnings { get; set; } = Array.Empty<CodeLearningWarningDto>();
    public IReadOnlyList<CodeLearningWarningDto> RuntimeWarnings { get; set; } = Array.Empty<CodeLearningWarningDto>();
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public string UserSafeSummary { get; set; } = "Code Learning IDE durumu guvenli metadata ile hazirlandi.";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CodeLearningRuntimeReadinessDto
{
    public string Status { get; set; } = "limited";
    public string ToolId { get; set; } = "ide_execution";
    public string Decision { get; set; } = "CORE_ENABLED_BEHIND_AUTH_AND_SANDBOX";
    public string RiskLevel { get; set; } = "High";
    public int TimeoutMs { get; set; }
    public IReadOnlyList<string> SupportedLanguages { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class CodeLearningSessionDto
{
    public string SessionStatus { get; set; } = "thin_evidence";
    public int SignalCount { get; set; }
    public int SuccessCount { get; set; }
    public int CompileErrorCount { get; set; }
    public int RuntimeErrorCount { get; set; }
    public int TimeoutCount { get; set; }
    public int TestFailureCount { get; set; }
    public int BlankAttemptCount { get; set; }
    public DateTimeOffset? LastSignalAt { get; set; }
}

public sealed class CodeLearningExerciseDto
{
    public string? ExerciseId { get; set; }
    public string ExerciseStatus { get; set; } = "suggested";
    public string ExerciseType { get; set; } = "checkpoint_challenge";
    public string SourceBasis { get; set; } = "learning_metadata";
    public string? ConceptKey { get; set; }
    public bool PreSubmitKeyVisible { get; set; }
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class CodeLearningAttemptDto
{
    public string Status { get; set; } = "not_started";
    public string Phase { get; set; } = "none";
    public bool Success { get; set; }
    public string Language { get; set; } = "csharp";
    public string SafeErrorCategory { get; set; } = "none";
    public string SafeTutorSummary { get; set; } = "Kod denemesi henuz yok.";
    public long DurationMs { get; set; }
    public bool OutputTruncated { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class CodeLearningErrorSummaryDto
{
    public string DominantErrorType { get; set; } = "none";
    public int RepetitionCount { get; set; }
    public string RepairSuggestion { get; set; } = "Kisa checkpoint ile basla.";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class CodeLearningActionDto
{
    public string ActionType { get; set; } = "start_code_diagnostic";
    public string Label { get; set; } = "Kisa kod tanisi baslat";
    public string Reason { get; set; } = "Kod ogrenme kaniti sinirli.";
    public string Priority { get; set; } = "normal";
    public string EntryPoint { get; set; } = "Code IDE";
    public string TargetRoute { get; set; } = "code-learning";
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string? Language { get; set; }
    public string? ConceptKey { get; set; }
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class CodeLearningHandoffDto
{
    public string HandoffType { get; set; } = "ask_tutor";
    public string Label { get; set; } = "Tutor ile acikla";
    public string TargetRoute { get; set; } = "chat";
    public string Priority { get; set; } = "normal";
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string? Language { get; set; }
    public string? ConceptKey { get; set; }
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class CodeLearningWarningDto
{
    public string WarningCode { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string Label { get; set; } = string.Empty;
    public string TargetRoute { get; set; } = "code-learning";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
}

public sealed class OrkaUnifiedEvaluationDto
{
    public string OverallStatus { get; set; } = "blocked";
    public IReadOnlyList<OrkaEvaluationScenarioResultDto> ScenarioResults { get; set; } = Array.Empty<OrkaEvaluationScenarioResultDto>();
    public OrkaEvaluationScorecardDto Scorecard { get; set; } = new();
    public IReadOnlyList<OrkaEvaluationCheckDto> ModuleConsistency { get; set; } = Array.Empty<OrkaEvaluationCheckDto>();
    public OrkaEvaluationSafetySweepDto SafetySweep { get; set; } = new();
    public OrkaEvaluationReleaseGateSummaryDto ReleaseGateSummary { get; set; } = new();
    public IReadOnlyList<OrkaEvaluationCheckDto> FailingChecks { get; set; } = Array.Empty<OrkaEvaluationCheckDto>();
    public IReadOnlyList<OrkaEvaluationCheckDto> WarningChecks { get; set; } = Array.Empty<OrkaEvaluationCheckDto>();
    public IReadOnlyList<string> RecommendedFixes { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public string UserSafeSummary { get; set; } = "Unified evaluation release harness hazirlandi.";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OrkaEvaluationScenarioResultDto
{
    public string ScenarioKey { get; set; } = string.Empty;
    public string Status { get; set; } = "blocked";
    public string ModuleKey { get; set; } = string.Empty;
    public string PrimaryAction { get; set; } = "continue_plan";
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public string UserSafeSummary { get; set; } = string.Empty;
}

public sealed class OrkaEvaluationScorecardDto
{
    public string OverallStatus { get; set; } = "blocked";
    public IReadOnlyList<OrkaEvaluationCheckDto> Checks { get; set; } = Array.Empty<OrkaEvaluationCheckDto>();
}

public sealed class OrkaEvaluationCheckDto
{
    public string CheckKey { get; set; } = string.Empty;
    public string Status { get; set; } = "blocked";
    public string ReasonCode { get; set; } = string.Empty;
    public string RelatedScenarioKey { get; set; } = string.Empty;
    public string UserSafeSummary { get; set; } = string.Empty;
}

public sealed class OrkaEvaluationWarningDto
{
    public string WarningCode { get; set; } = string.Empty;
    public string Severity { get; set; } = "warning";
    public string RelatedModule { get; set; } = string.Empty;
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public string UserSafeSummary { get; set; } = string.Empty;
}

public sealed class OrkaEvaluationSafetySweepDto
{
    public string Status { get; set; } = "blocked";
    public int ScannedPayloadCount { get; set; }
    public int UnsafeMarkerHitCount { get; set; }
    public IReadOnlyList<OrkaEvaluationWarningDto> Warnings { get; set; } = Array.Empty<OrkaEvaluationWarningDto>();
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public string UserSafeSummary { get; set; } = "Public payload safety sweep henuz calismadi.";
}

public sealed class OrkaEvaluationReleaseGateSummaryDto
{
    public string Status { get; set; } = "blocked";
    public IReadOnlyList<string> LocalCommands { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> RequiredTestGroups { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ReasonCodes { get; set; } = Array.Empty<string>();
    public string UserSafeSummary { get; set; } = "Release gate ozeti hazir degil.";
}
