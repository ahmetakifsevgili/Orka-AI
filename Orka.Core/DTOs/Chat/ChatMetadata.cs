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
    public Guid? TutorActionTraceId { get; set; }
    public string? TeachingMode { get; set; }
    public string? StyleMode { get; set; }
    public string? ActiveConceptKey { get; set; }
    public string? NextPedagogicalMove { get; set; }
    public string? GroundingStatus { get; set; }
    public decimal? MasteryProbability { get; set; }
    public decimal? Confidence { get; set; }
    public IReadOnlyList<Guid> ToolCallIds { get; set; } = Array.Empty<Guid>();
    public IReadOnlyList<Guid> ArtifactIds { get; set; } = Array.Empty<Guid>();
    public IReadOnlyList<ToolStatusDto> ToolStatuses { get; set; } = Array.Empty<ToolStatusDto>();
    public IReadOnlyList<ArtifactSummaryDto> ArtifactSummaries { get; set; } = Array.Empty<ArtifactSummaryDto>();
    public EvidenceSummaryDto? EvidenceSummary { get; set; }
    public int? PolicyViolationCount { get; set; }
    public string? RagQualityStatus { get; set; }
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
