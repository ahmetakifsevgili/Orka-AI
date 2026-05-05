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
    double? SourceConfidence);

public record GlossaryItemDto(string Term, string SimpleExplanation);

public record TimelineItemDto(string Year, string Event);

public record MindMapNodeDto(string Id, string Label, string? ParentId, int Depth);

public record MindMapDto(string Mermaid, IReadOnlyList<MindMapNodeDto> Nodes);

public record StudyCardDto(string Front, string Back, string? SourceHint);

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
