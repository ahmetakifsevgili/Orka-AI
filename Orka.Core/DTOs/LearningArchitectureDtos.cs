using Orka.Core.DTOs.Korteks;

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
}

public sealed class KnowledgeTracingStateDto
{
    public Guid Id { get; set; }
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
    public List<ResourceConceptAlignmentDto> ResourceAlignments { get; set; } = [];
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RagEvaluationRunDto
{
    public Guid Id { get; set; }
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
