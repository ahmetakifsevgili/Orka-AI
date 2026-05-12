using Orka.Core.DTOs.Chat;

namespace Orka.Core.DTOs;

public sealed class TutorTurnStateDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public Guid? WorkingMemorySnapshotId { get; set; }
    public string UserMessage { get; set; } = string.Empty;
    public string ActiveConceptKey { get; set; } = string.Empty;
    public string ActiveConceptLabel { get; set; } = string.Empty;
    public string LearnerState { get; set; } = "unknown";
    public decimal? MasteryProbability { get; set; }
    public decimal? Confidence { get; set; }
    public string RemediationNeed { get; set; } = "unknown";
    public string PracticeReadiness { get; set; } = "guided";
    public string StyleMode { get; set; } = "step_by_step";
    public string AffectiveState { get; set; } = "neutral";
    public string CognitiveLoad { get; set; } = "normal";
    public string GroundingStatus { get; set; } = "model_only";
    public int SourceEvidenceCount { get; set; }
    public EvidenceQualityDto? EvidenceQuality { get; set; }
    public string? TutorResponseMode { get; set; }
    public string? EvidencePolicy { get; set; }
    public string? PersonalizationMode { get; set; }
    public string? MasteryBasis { get; set; }
    public IReadOnlyList<string> WeakConceptHints { get; set; } = Array.Empty<string>();
    public bool DirectAnswerRisk { get; set; }
    public bool HasIdeContext { get; set; }
    public bool HasNotebookContext { get; set; }
    public bool HasWikiContext { get; set; }
    public List<string> RecentMistakes { get; set; } = [];
    public List<string> SourceEvidence { get; set; } = [];
    public string PromptBlock { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TutorActionPlanDto
{
    public Guid Id { get; set; }
    public Guid TutorTurnStateId { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string TeachingMode { get; set; } = "explain";
    public string ActiveConceptKey { get; set; } = string.Empty;
    public IReadOnlyList<string> TargetOutcomeKeys { get; set; } = Array.Empty<string>();
    public string LearnerState { get; set; } = "unknown";
    public string StyleMode { get; set; } = "step_by_step";
    public string DirectAnswerPolicy { get; set; } = "scaffold";
    public string GroundingPolicy { get; set; } = "model_ok_no_source_claim";
    public string? TutorResponseMode { get; set; }
    public string? PersonalizationMode { get; set; }
    public string? MasteryBasis { get; set; }
    public IReadOnlyList<string> WeakConceptHints { get; set; } = Array.Empty<string>();
    public IReadOnlyList<TutorToolPlanDto> ToolPlans { get; set; } = Array.Empty<TutorToolPlanDto>();
    public IReadOnlyList<TeachingArtifactPlanDto> ArtifactPlans { get; set; } = Array.Empty<TeachingArtifactPlanDto>();
    public string NextCheckPrompt { get; set; } = "Bu adımı kendi cümlenle özetler misin?";
    public string PromptBlock { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record TutorToolPlanDto(
    string ToolId,
    string Reason,
    bool Required,
    string RiskLevel);

public sealed record TeachingArtifactPlanDto(
    string ArtifactType,
    string Reason,
    string RenderFormat);

public sealed class TeachingArtifactDto
{
    public Guid Id { get; set; }
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
    public string PromptBlock { get; set; } = string.Empty;
    public DateTimeOffset? RenderedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TutorMemoryPatchDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string PatchType { get; set; } = "turn";
    public string PatchJson { get; set; } = "{}";
    public string Source { get; set; } = "tutor";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LearnerProfileDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public string PreferredStyleMode { get; set; } = "step_by_step";
    public decimal StyleConfidence { get; set; }
    public string AffectiveState { get; set; } = "neutral";
    public string CognitiveLoad { get; set; } = "normal";
    public int EvidenceCount { get; set; }
    public bool IsLowEvidence => EvidenceCount < 3 || StyleConfidence < 0.55m;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record LearningStyleSignalDto(
    Guid Id,
    string StyleMode,
    string EvidenceType,
    decimal Weight,
    decimal Confidence,
    DateTimeOffset CreatedAt);

public sealed record AffectiveSignalDto(
    Guid Id,
    string AffectiveState,
    string EvidenceType,
    decimal Confidence,
    DateTimeOffset CreatedAt);

public sealed record CognitiveLoadSignalDto(
    Guid Id,
    string CognitiveLoad,
    string EvidenceType,
    decimal Confidence,
    DateTimeOffset CreatedAt);

public sealed class TutorToolCallDto
{
    public Guid Id { get; set; }
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
    public IReadOnlyList<ProviderCitationDto> Citations { get; set; } = Array.Empty<ProviderCitationDto>();
    public long LatencyMs { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

public sealed class TutorReflectionUpdateDto
{
    public Guid Id { get; set; }
    public Guid? TutorActionTraceId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public bool PolicyApplied { get; set; }
    public bool SourceClaimWithoutSource { get; set; }
    public bool DirectAnswerRiskHandled { get; set; }
    public bool ArtifactRendered { get; set; }
    public bool MicroCheckAsked { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RedisStreamEventDto
{
    public string Id { get; set; } = string.Empty;
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TutorTraceTimelineDto
{
    public Guid SessionId { get; set; }
    public string After { get; set; } = "0-0";
    public string LastEventId { get; set; } = "0-0";
    public string Source { get; set; } = "redis";
    public string TraceHealth { get; set; } = "unknown";
    public IReadOnlyList<TutorTraceTimelineEventDto> Events { get; set; } = Array.Empty<TutorTraceTimelineEventDto>();
}

public sealed class TutorTraceTimelineEventDto
{
    public Guid Id { get; set; }
    public string StreamId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string EventGroup { get; set; } = "state";
    public string UserSafeLabel { get; set; } = string.Empty;
    public string UserSafeDetail { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TutorMemoryFragmentDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string FragmentType { get; set; } = "source_note";
    public string ConceptKey { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Source { get; set; } = "tutor";
    public decimal Importance { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TutorPedagogyEvaluationRequestDto
{
    public TutorTurnStateDto TurnState { get; set; } = new();
    public TutorActionPlanDto ActionPlan { get; set; } = new();
    public TutorReflectionUpdateDto? Reflection { get; set; }
    public IReadOnlyList<TutorToolCallDto> ToolCalls { get; set; } = Array.Empty<TutorToolCallDto>();
    public IReadOnlyList<TeachingArtifactDto> Artifacts { get; set; } = Array.Empty<TeachingArtifactDto>();
    public string AssistantAnswer { get; set; } = string.Empty;
    public bool AllowLlmJudge { get; set; }
}

public sealed class TutorPedagogyEvaluationRunDto
{
    public Guid Id { get; set; }
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
    public IReadOnlyList<TutorPedagogyRubricScoreDto> RubricScores { get; set; } = Array.Empty<TutorPedagogyRubricScoreDto>();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record TutorPedagogyRubricScoreDto(
    string RubricKey,
    decimal Score,
    string Severity,
    bool IsCritical,
    string Evidence,
    string Recommendation);

public sealed class TutorPedagogyTopicSummaryDto
{
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public string Status { get; set; } = "unknown";
    public decimal AverageScore { get; set; }
    public int RunCount { get; set; }
    public int CriticalViolationCount { get; set; }
    public IReadOnlyList<TutorPedagogyEvaluationRunDto> RecentRuns { get; set; } = Array.Empty<TutorPedagogyEvaluationRunDto>();
}

public sealed record TutorGoldenScenarioDto(
    string ScenarioKey,
    string Title,
    string DomainHint,
    string UserMessage,
    string ExpectedTeachingMode,
    string ExpectedBehavior,
    IReadOnlyList<string> RequiredRubrics);
