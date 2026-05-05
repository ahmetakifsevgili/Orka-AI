namespace Orka.Core.DTOs.Chat;

public sealed class ChatResponseMetadata
{
    public IReadOnlyList<CitationDto> Citations { get; set; } = Array.Empty<CitationDto>();
    public IReadOnlyList<UsedToolDto> UsedTools { get; set; } = Array.Empty<UsedToolDto>();
    public string GroundingMode { get; set; } = "model_fallback";
    public string? FallbackReason { get; set; }
    public double? SourceConfidence { get; set; }
    public IReadOnlyList<string> ProviderWarnings { get; set; } = Array.Empty<string>();
}

public sealed record CitationDto(
    string CitationId,
    string SourceType,
    Guid? SourceId,
    int? PageNumber,
    string? Label,
    string? Url,
    double? Confidence);

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
