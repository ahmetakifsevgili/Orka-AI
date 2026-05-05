using System;

namespace Orka.Core.DTOs;

public sealed record BookmarkDto(
    Guid Id,
    Guid? TopicId,
    Guid? SessionId,
    Guid? MessageId,
    Guid? LearningSourceId,
    Guid? WikiPageId,
    Guid? ReviewItemId,
    Guid? FlashcardId,
    string Title,
    string? Note,
    string? Quote,
    IReadOnlyList<string> Tags,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateBookmarkRequest(
    Guid? TopicId,
    Guid? SessionId,
    Guid? MessageId,
    Guid? LearningSourceId,
    Guid? WikiPageId,
    Guid? ReviewItemId,
    Guid? FlashcardId,
    string? Title,
    string? Note,
    string? Quote,
    IReadOnlyList<string>? Tags);

public sealed record UpdateBookmarkRequest(
    string? Title,
    string? Note,
    string? Quote,
    IReadOnlyList<string>? Tags);
