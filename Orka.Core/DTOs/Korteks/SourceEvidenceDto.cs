using System;

namespace Orka.Core.DTOs.Korteks;

public sealed record SourceEvidenceDto(
    string Provider,
    string ToolName,
    string Url,
    string Title,
    string? Snippet,
    DateTimeOffset? PublishedAt,
    DateTimeOffset RetrievedAt,
    double? RelevanceScore,
    string? SourceType,
    string? ExternalId,
    string? Warning);
