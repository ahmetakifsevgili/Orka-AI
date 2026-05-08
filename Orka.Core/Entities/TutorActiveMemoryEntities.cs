namespace Orka.Core.Entities;

public sealed class TutorWorkingMemorySnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public int WorkingMemoryVersion { get; set; } = 3;
    public string ActiveConceptKey { get; set; } = string.Empty;
    public string TeachingMode { get; set; } = "explain";
    public string StyleMode { get; set; } = "step_by_step";
    public string AffectiveState { get; set; } = "neutral";
    public string CognitiveLoad { get; set; } = "normal";
    public string Source { get; set; } = "redis";
    public bool IsDegraded { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
}

public sealed class TutorTurnState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? WorkingMemorySnapshotId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string UserMessageHash { get; set; } = string.Empty;
    public string ActiveConceptKey { get; set; } = string.Empty;
    public string TeachingMode { get; set; } = "explain";
    public string StyleMode { get; set; } = "step_by_step";
    public string AffectiveState { get; set; } = "neutral";
    public string CognitiveLoad { get; set; } = "normal";
    public string GroundingStatus { get; set; } = "model_only";
    public string StateJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class TutorMemoryPatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string PatchType { get; set; } = "turn";
    public string PatchJson { get; set; } = "{}";
    public string Source { get; set; } = "tutor";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class LearnerProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public string PreferredStyleMode { get; set; } = "step_by_step";
    public decimal StyleConfidence { get; set; } = 0.35m;
    public string AffectiveState { get; set; } = "neutral";
    public string CognitiveLoad { get; set; } = "normal";
    public int EvidenceCount { get; set; }
    public string ProfileJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class LearningStyleSignal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string StyleMode { get; set; } = "step_by_step";
    public string EvidenceType { get; set; } = "message";
    public decimal Weight { get; set; } = 0.5m;
    public decimal Confidence { get; set; } = 0.35m;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AffectiveSignal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string AffectiveState { get; set; } = "neutral";
    public string EvidenceType { get; set; } = "message";
    public decimal Confidence { get; set; } = 0.35m;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class CognitiveLoadSignal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string CognitiveLoad { get; set; } = "normal";
    public string EvidenceType { get; set; } = "message";
    public decimal Confidence { get; set; } = 0.35m;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class TutorActionTrace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public string TeachingMode { get; set; } = "explain";
    public string ActiveConceptKey { get; set; } = string.Empty;
    public string StyleMode { get; set; } = "step_by_step";
    public string DirectAnswerPolicy { get; set; } = "scaffold";
    public string GroundingPolicy { get; set; } = "model_ok_no_source_claim";
    public string ToolPlanJson { get; set; } = "[]";
    public string ArtifactPlanJson { get; set; } = "[]";
    public string NextCheckPrompt { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class TutorToolCall
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? TutorActionTraceId { get; set; }
    public string ToolId { get; set; } = string.Empty;
    public string Provider { get; set; } = "orka";
    public string Status { get; set; } = "skipped";
    public bool Success { get; set; }
    public string RiskLevel { get; set; } = "low";
    public string? Evidence { get; set; }
    public string? FallbackReason { get; set; }
    public string? ErrorCode { get; set; }
    public string? SafeMessage { get; set; }
    public double? Confidence { get; set; }
    public int? SourceCount { get; set; }
    public string ResultJson { get; set; } = "{}";
    public long LatencyMs { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class TeachingArtifact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? TutorActionTraceId { get; set; }
    public string ArtifactType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string RenderFormat { get; set; } = "markdown";
    public string Status { get; set; } = "ready";
    public string Provider { get; set; } = "orka";
    public string? ExternalUrl { get; set; }
    public string? RenderError { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTime? RenderedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class TutorMemoryFragment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public Guid? TutorActionTraceId { get; set; }
    public string FragmentType { get; set; } = "source_note";
    public string ConceptKey { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? EmbeddingJson { get; set; }
    public string Source { get; set; } = "tutor";
    public decimal Importance { get; set; } = 0.50m;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
}

public sealed class RagEvaluationRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public string QualityStatus { get; set; } = "unknown";
    public decimal FaithfulnessScore { get; set; }
    public decimal ContextRelevanceScore { get; set; }
    public decimal AnswerRelevanceScore { get; set; }
    public decimal CitationCoverageScore { get; set; }
    public int ItemCount { get; set; }
    public string ReportJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class RagEvaluationItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RagEvaluationRunId { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public string Query { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string ContextJson { get; set; } = "[]";
    public int ExpectedCitationCount { get; set; }
    public int CitationCount { get; set; }
    public decimal FaithfulnessScore { get; set; }
    public decimal ContextRelevanceScore { get; set; }
    public decimal AnswerRelevanceScore { get; set; }
    public decimal CitationCoverageScore { get; set; }
    public string Status { get; set; } = "unknown";
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public RagEvaluationRun Run { get; set; } = null!;
}

public sealed class TutorReflectionUpdate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? TutorActionTraceId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public bool PolicyApplied { get; set; }
    public bool SourceClaimWithoutSource { get; set; }
    public bool DirectAnswerRiskHandled { get; set; }
    public bool ArtifactRendered { get; set; }
    public bool MicroCheckAsked { get; set; }
    public string ReflectionJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class TutorPolicyViolationV2
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? TutorActionTraceId { get; set; }
    public string ViolationType { get; set; } = string.Empty;
    public string Severity { get; set; } = "warning";
    public string Evidence { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class TutorPedagogyEvaluationRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public Guid? TutorActionTraceId { get; set; }
    public Guid? TutorReflectionUpdateId { get; set; }
    public string Status { get; set; } = "unknown";
    public decimal OverallScore { get; set; }
    public bool HasCriticalViolation { get; set; }
    public int WarningCount { get; set; }
    public int CriticalViolationCount { get; set; }
    public bool LlmJudgeUsed { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public string RunJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class TutorPedagogyEvaluationItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EvaluationRunId { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public Guid? TutorActionTraceId { get; set; }
    public string UserMessage { get; set; } = string.Empty;
    public string AssistantAnswer { get; set; } = string.Empty;
    public string TeachingMode { get; set; } = "explain";
    public string DirectAnswerPolicy { get; set; } = "scaffold";
    public string GroundingPolicy { get; set; } = "model_ok_no_source_claim";
    public string ActiveConceptKey { get; set; } = string.Empty;
    public string ItemJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public TutorPedagogyEvaluationRun Run { get; set; } = null!;
}

public sealed class TutorPedagogyRubricScore
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EvaluationRunId { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? TutorActionTraceId { get; set; }
    public string RubricKey { get; set; } = string.Empty;
    public decimal Score { get; set; }
    public string Severity { get; set; } = "info";
    public bool IsCritical { get; set; }
    public string Evidence { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public TutorPedagogyEvaluationRun Run { get; set; } = null!;
}

public sealed class TutorGoldenScenario
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ScenarioKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string DomainHint { get; set; } = "general";
    public string UserMessage { get; set; } = string.Empty;
    public string ExpectedTeachingMode { get; set; } = string.Empty;
    public string ExpectedBehavior { get; set; } = string.Empty;
    public string RequiredRubricsJson { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class TutorPedagogyFeedbackPatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? TutorPedagogyEvaluationRunId { get; set; }
    public string PatchType { get; set; } = "pedagogy_feedback";
    public string Status { get; set; } = "active";
    public string Feedback { get; set; } = string.Empty;
    public string PatchJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
}
