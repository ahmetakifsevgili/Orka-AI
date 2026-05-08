namespace Orka.Core.DTOs;

public sealed record ProviderCitationDto(
    string Label,
    string? Url,
    string? SourceName,
    DateTime? PublishedAt,
    double? Confidence = null);

public sealed record ProviderToolResultDto(
    bool Success,
    string ToolId,
    string Provider,
    string Status,
    object? Data,
    IReadOnlyList<ProviderCitationDto> Citations,
    DateTime Timestamp,
    long LatencyMs,
    bool FallbackUsed,
    string? ErrorCode,
    string SafeMessage,
    double? Confidence = null,
    int? SourceCount = null)
{
    public static ProviderToolResultDto Fallback(
        string toolId,
        string provider,
        string status,
        string errorCode,
        string safeMessage,
        long latencyMs = 0) =>
        new(false, toolId, provider, status, null, [], DateTime.UtcNow, Math.Max(0, latencyMs), true, errorCode, safeMessage, null, 0);
}

public sealed record WolframComputationDto(
    string Query,
    string Answer);

public sealed record NewsArticleDto(
    string Title,
    string SourceName,
    string? Url,
    DateTime? PublishedAt,
    string Summary);

public sealed record WeatherContextDto(
    string Location,
    DateTime ObservationTime,
    double? TemperatureC,
    string? Conditions,
    string? Source,
    double? Latitude = null,
    double? Longitude = null,
    string? ClimateSummary = null,
    string? GeographySummary = null,
    string? TeachingUse = null);

public sealed record GeocodingResultDto(
    string Location,
    double Latitude,
    double Longitude,
    string Provider);

public sealed record MarketDataDto(
    string Asset,
    string Symbol,
    decimal? PriceUsd,
    decimal? Change24hPercent,
    DateTime Timestamp,
    string Source,
    string SafeSummary);

public sealed record VisualArtifactResultDto(
    string Prompt,
    string? ExternalUrl,
    string Provider,
    string RenderFormat,
    string FallbackText);

public sealed class TeachingEvidenceRequestDto
{
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public Guid? TutorActionTraceId { get; set; }
    public string EvidenceType { get; set; } = "knowledge_entity";
    public string Query { get; set; } = string.Empty;
    public string ConceptKey { get; set; } = string.Empty;
    public string UserMessage { get; set; } = string.Empty;
}

public sealed class TeachingEvidenceCardDto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public Guid? TutorActionTraceId { get; set; }
    public Guid? TutorToolCallId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string EvidenceType { get; set; } = "knowledge_entity";
    public string ConceptKey { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string FactualClaim { get; set; } = string.Empty;
    public string AnalogyCandidate { get; set; } = string.Empty;
    public string ClassroomUse { get; set; } = string.Empty;
    public string? CitationUrl { get; set; }
    public string CitationLabel { get; set; } = string.Empty;
    public double Confidence { get; set; } = 0.50;
    public string Freshness { get; set; } = "static";
    public string RiskLevel { get; set; } = "low";
    public string RawPayloadHash { get; set; } = string.Empty;
    public string Status { get; set; } = "ready";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record TeachingEvidenceResultDto(
    bool Success,
    string EvidenceType,
    string Provider,
    string Status,
    IReadOnlyList<TeachingEvidenceCardDto> Cards,
    string SafeMessage,
    string? ErrorCode,
    double? Confidence,
    int SourceCount,
    long LatencyMs,
    bool FallbackUsed = false);

public sealed record MistakeClassificationRequest(
    string? Question,
    string? ExpectedAnswer,
    string? StudentAnswer,
    string? Explanation,
    Guid? TopicId,
    string? SkillTag,
    string? ConceptTag,
    string? CodePhase = null,
    string? CompileError = null,
    string? RuntimeError = null,
    string? SourceOrWikiContext = null);

public sealed record MistakeClassificationResult(
    string Category,
    string CategoryLabel,
    double Confidence,
    string Reason,
    string? SkillTag,
    string? ConceptTag,
    string RemediationHint,
    int SuggestedReviewPressure,
    bool SuggestFlashcard,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record YouTubeTranscriptRequest(
    string VideoId,
    string? Language = "tr",
    string? Topic = null);

public sealed record YouTubeTranscriptResult(
    bool Success,
    string Status,
    string VideoId,
    string? Title,
    string Transcript,
    IReadOnlyList<YouTubeTranscriptChunkDto> Chunks,
    string? ErrorCode,
    string SafeMessage);

public sealed record YouTubeTranscriptChunkDto(
    int Index,
    string Text,
    int StartOffset,
    int Length);

public sealed record YouTubeTeachingReferenceDto(
    string Status,
    string VideoId,
    string TeachingFlow,
    IReadOnlyList<string> Examples,
    IReadOnlyList<string> Analogies,
    IReadOnlyList<string> CommonMistakes,
    IReadOnlyList<string> PracticeIdeas,
    IReadOnlyList<YouTubeTranscriptChunkDto> EvidenceChunks);
