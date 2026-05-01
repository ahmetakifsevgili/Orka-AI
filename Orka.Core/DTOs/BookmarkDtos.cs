using System;

namespace Orka.Core.DTOs;

public record CreateBookmarkRequest(Guid MessageId, string? Note, string? Tag);

public record UpdateBookmarkRequest(string? Note, string? Tag);

public record BookmarkDto(
    Guid Id,
    Guid MessageId,
    Guid? TopicId,
    string? TopicTitle,
    string? Note,
    string? Tag,
    string MessageRole,
    string MessageSnippet,
    DateTime MessageCreatedAt,
    DateTime CreatedAt);
