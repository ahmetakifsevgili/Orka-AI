using Orka.Core.DTOs.Chat;

namespace Orka.Core.DTOs;

public record LearningSourceSummaryDto(
    Guid Id,
    Guid? TopicId,
    Guid? SessionId,
    string SourceType,
    string Title,
    string FileName,
    int PageCount,
    int ChunkCount,
    string Status,
    DateTime CreatedAt,
    bool IsDeleted = false,
    int Version = 1);

public record SourcePageDto(
    Guid SourceId,
    int PageNumber,
    string Title,
    IReadOnlyList<SourceChunkDto> Chunks);

public record SourceChunkDto(
    Guid Id,
    int PageNumber,
    int ChunkIndex,
    string Text,
    string? HighlightHint);

public record SourceAskResultDto(
    string Answer,
    IReadOnlyList<SourceChunkDto> Citations,
    SourceMetadataDto? Metadata = null);

public record SourceMetadataDto(
    IReadOnlyList<CitationDto> Citations,
    string GroundingMode,
    string? FallbackReason,
    double? SourceConfidence,
    Guid? RetrievalRunId = null,
    string? SourceQualityStatus = null,
    int UnsupportedCitationCount = 0,
    int CitationMissingCount = 0,
    EvidenceQualityDto? EvidenceQuality = null);

public sealed class SourceRetrievalRunDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? SourceId { get; set; }
    public string Query { get; set; } = string.Empty;
    public string RetrievalScope { get; set; } = "topic";
    public int RequestedTopK { get; set; }
    public int RetrievedCount { get; set; }
    public bool IsEmpty { get; set; }
    public decimal MaxScore { get; set; }
    public decimal AverageScore { get; set; }
    public string QualityStatus { get; set; } = "unknown";
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<SourceRetrievalItemDto> Items { get; set; } = Array.Empty<SourceRetrievalItemDto>();
}

public sealed class SourceRetrievalItemDto
{
    public Guid Id { get; set; }
    public Guid SourceRetrievalRunId { get; set; }
    public Guid SourceId { get; set; }
    public Guid? SourceChunkId { get; set; }
    public int PageNumber { get; set; }
    public int ChunkIndex { get; set; }
    public int Rank { get; set; }
    public decimal EmbeddingScore { get; set; }
    public decimal LexicalScore { get; set; }
    public decimal FusedScore { get; set; }
    public string QualityStatus { get; set; } = "unknown";
    public string? Reason { get; set; }
    public string Snippet { get; set; } = string.Empty;
}

public sealed class SourceCitationCheckDto
{
    public Guid Id { get; set; }
    public Guid? SourceRetrievalRunId { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? SourceChunkId { get; set; }
    public string CitationId { get; set; } = string.Empty;
    public string SourceType { get; set; } = "document";
    public int? PageNumber { get; set; }
    public int? ChunkIndex { get; set; }
    public string CheckStatus { get; set; } = "unknown";
    public decimal Confidence { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SourceQualityReportDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SourceId { get; set; }
    public string QualityStatus { get; set; } = "unknown";
    public string RetrievalHealthStatus { get; set; } = "unknown";
    public string CitationCoverageStatus { get; set; } = "unknown";
    public string CitationSupportStatus { get; set; } = "unknown";
    public int RetrievalRunCount { get; set; }
    public int EmptyRunCount { get; set; }
    public int CitationCheckCount { get; set; }
    public int UnsupportedCitationCount { get; set; }
    public int CitationMissingCount { get; set; }
    public decimal AverageContextRelevance { get; set; }
    public decimal CitationCoverage { get; set; }
    public EvidenceQualityDto? EvidenceQuality { get; set; }
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<SourceRetrievalRunDto> RecentRetrievalRuns { get; set; } = Array.Empty<SourceRetrievalRunDto>();
    public IReadOnlyList<SourceCitationCheckDto> RecentCitationChecks { get; set; } = Array.Empty<SourceCitationCheckDto>();
}

public record GlossaryItemDto(
    string Term,
    string SimpleExplanation,
    string? SourceHint = null,
    string? GroundingStatus = null,
    string? CitationCoverageStatus = null,
    string? SourceQualityStatus = null,
    Guid? RetrievalRunId = null);

public record TimelineItemDto(
    string Year,
    string Event,
    string? SourceHint = null,
    string? GroundingStatus = null,
    string? CitationCoverageStatus = null,
    string? SourceQualityStatus = null,
    Guid? RetrievalRunId = null);

public record MindMapNodeDto(string Id, string Label, string? ParentId, int Depth);

public record MindMapDto(
    string Mermaid,
    IReadOnlyList<MindMapNodeDto> Nodes,
    string? GroundingStatus = null,
    string? CitationCoverageStatus = null,
    string? SourceQualityStatus = null,
    Guid? RetrievalRunId = null,
    IReadOnlyList<CitationDto>? Citations = null);

public record StudyCardDto(
    string Front,
    string Back,
    string? SourceHint,
    string? GroundingStatus = null,
    string? CitationCoverageStatus = null,
    string? SourceQualityStatus = null,
    Guid? RetrievalRunId = null);

public record AudioOverviewJobDto(
    Guid Id,
    string Status,
    string Script,
    IReadOnlyList<string> Speakers,
    string? ErrorMessage,
    DateTime CreatedAt,
    string? ContentType = null,
    string? FileName = null,
    string? DownloadUrl = null,
    string? FallbackReason = null,
    DateTime? UpdatedAt = null);
