namespace Orka.Core.DTOs.Chat;

public sealed class ChatResponseMetadata
{
    public IReadOnlyList<CitationDto> Citations { get; set; } = Array.Empty<CitationDto>();
    public IReadOnlyList<UsedToolDto> UsedTools { get; set; } = Array.Empty<UsedToolDto>();
    public string GroundingMode { get; set; } = "model_fallback";
    public string? FallbackReason { get; set; }
    public double? SourceConfidence { get; set; }
    public IReadOnlyList<string> ProviderWarnings { get; set; } = Array.Empty<string>();
    public Guid? TutorPolicyTraceId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public Guid? TutorWorkingMemorySnapshotId { get; set; }
    public Guid? ActiveLessonSnapshotId { get; set; }
    public Guid? StudentContextSnapshotId { get; set; }
    public Guid? PlanQualitySnapshotId { get; set; }
    public Guid? TutorActionTraceId { get; set; }
    public string? TeachingMode { get; set; }
    public string? StyleMode { get; set; }
    public string? ActiveConceptKey { get; set; }
    public string? NextPedagogicalMove { get; set; }
    public string? GroundingStatus { get; set; }
    public decimal? MasteryProbability { get; set; }
    public decimal? Confidence { get; set; }
    public string? LessonSnapshotStatus { get; set; }
    public string? StudentContextConfidenceStatus { get; set; }
    public string? CurrentPlanStepId { get; set; }
    public string? CurrentPlanStepTitle { get; set; }
    public string? CurrentPlanTutorMove { get; set; }
    public string? CurrentPlanQuizHook { get; set; }
    public string? PlanSourceReadiness { get; set; }
    public IReadOnlyList<Guid> ToolCallIds { get; set; } = Array.Empty<Guid>();
    public IReadOnlyList<Guid> ArtifactIds { get; set; } = Array.Empty<Guid>();
    public IReadOnlyList<ToolStatusDto> ToolStatuses { get; set; } = Array.Empty<ToolStatusDto>();
    public IReadOnlyList<ArtifactSummaryDto> ArtifactSummaries { get; set; } = Array.Empty<ArtifactSummaryDto>();
    public EvidenceSummaryDto? EvidenceSummary { get; set; }
    public int? PolicyViolationCount { get; set; }
    public string? RagQualityStatus { get; set; }
    public EvidenceQualityDto? EvidenceQuality { get; set; }
    public string? TutorResponseMode { get; set; }
    public string? TutorTeachingMove { get; set; }
    public string? TutorResponseDepth { get; set; }
    public string? TutorGroundingPolicy { get; set; }
    public string? TutorRemediationPolicy { get; set; }
    public string? TutorToolPolicy { get; set; }
    public IReadOnlyList<string> TutorNextLearningActions { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> TutorContextUse { get; set; } = Array.Empty<string>();
    public string? TutorResponseQualityStatus { get; set; }
    public IReadOnlyList<string> TutorResponseQualityWarnings { get; set; } = Array.Empty<string>();
    public string? ActivePlanStepId { get; set; }
    public string? LatestAssessmentMode { get; set; }
    public string? LatestMisconceptionConfidence { get; set; }
    public string? SourceReadiness { get; set; }
    public string? EvidencePolicy { get; set; }
    public string? PersonalizationMode { get; set; }
    public string? MasteryBasis { get; set; }
    public IReadOnlyList<string> WeakConceptHints { get; set; } = Array.Empty<string>();
    public Orka.Core.DTOs.MisconceptionSignalDto? MisconceptionSignal { get; set; }
    public Orka.Core.DTOs.LearningSignalConfidenceDto? LearningSignalConfidence { get; set; }
    public Orka.Core.DTOs.RemediationSeedDto? RemediationSeed { get; set; }
    public string? NextCheckPrompt { get; set; }
    public string? CognitiveLoad { get; set; }
    public string? AffectiveState { get; set; }
    public Guid? TutorPedagogyEvaluationRunId { get; set; }
    public string? TutorPedagogyStatus { get; set; }
    public decimal? TutorPedagogyScore { get; set; }
    public IReadOnlyList<string> PedagogyWarnings { get; set; } = Array.Empty<string>();
}

public sealed record CitationDto(
    string CitationId,
    string SourceType,
    Guid? SourceId,
    int? PageNumber,
    string? Label,
    string? Url,
    double? Confidence,
    Guid? ChunkId = null,
    Guid? SourceTopicId = null,
    string? SourceTopicTitle = null,
    string? ScopeRelation = null,
    string? RetrievalScope = null);

public sealed record UsedToolDto(
    string Name,
    string Status,
    string? Evidence,
    string? FallbackReason,
    string? ToolId = null,
    bool? Success = null,
    bool? FallbackUsed = null,
    string? Provider = null,
    long? LatencyMs = null,
    IReadOnlyList<CitationDto>? Citations = null,
    double? SourceConfidence = null,
    string? ErrorCode = null,
    string? SafeMessage = null,
    string? GroundingMode = null,
    DateTime? Timestamp = null);

public sealed record ToolStatusDto(
    Guid Id,
    string ToolId,
    string Status,
    bool Success,
    string? Provider,
    string? SafeMessage,
    string? ErrorCode,
    double? Confidence,
    int? SourceCount);

public sealed record ArtifactSummaryDto(
    Guid Id,
    string ArtifactType,
    string Title,
    string Status,
    string RenderFormat,
    string? Provider,
    string? ExternalUrl);

public sealed record EvidenceSummaryDto(
    int ReadyToolCount,
    int SourceCount,
    string GroundingStatus,
    string LearnerEvidenceStatus);

public sealed class EvidenceQualityDto
{
    public string Status { get; set; } = "unknown";
    public string UserSafeLabel { get; set; } = "Kaynak durumu bilinmiyor";
    public IReadOnlyList<string> Reasons { get; set; } = Array.Empty<string>();
    public int SourceCount { get; set; }
    public int ReadySourceCount { get; set; }
    public int RetrievedEvidenceCount { get; set; }
    public decimal CitationCoverage { get; set; }
    public int UnsupportedCitationCount { get; set; }
    public int CitationMissingCount { get; set; }
}
