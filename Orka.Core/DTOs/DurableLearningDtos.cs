namespace Orka.Core.DTOs;

public sealed record DurableReviewItemDto(
    Guid Id,
    Guid? TopicId,
    string ReviewKey,
    string SkillTitle,
    string? SkillTag,
    string? ConceptTag,
    string? LearningObjective,
    string? MistakeCategory,
    string? SourceType,
    Guid? SourceId,
    DateTime DueAt,
    DateTime? LastReviewedAt,
    int IntervalDays,
    double EaseFactor,
    int RepetitionCount,
    int LapseCount,
    int SuccessStreak,
    string Status,
    Guid? FlashcardId = null,
    string? FlashcardFront = null,
    string? FlashcardBack = null);

public sealed record CompleteDurableReviewRequest(int Quality, string? ResponseMode = null, string? Notes = null);

public sealed record FlashcardDto(
    Guid Id,
    Guid? TopicId,
    Guid? LearningSourceId,
    Guid? WikiPageId,
    string Front,
    string Back,
    string? Hint,
    string? SkillTag,
    string? ConceptTag,
    string? LearningObjective,
    string? Difficulty,
    string Status,
    string CreatedFrom,
    Guid? ReviewItemId,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateFlashcardRequest(
    Guid? TopicId,
    Guid? LearningSourceId,
    Guid? WikiPageId,
    string Front,
    string Back,
    string? Hint,
    string? SkillTag,
    string? ConceptTag,
    string? LearningObjective,
    string? Difficulty,
    string? CreatedFrom);

public sealed record ReviewFlashcardRequest(int Quality, string? Notes = null);

public sealed record DailyChallengeDto(
    Guid Id,
    Guid? TopicId,
    DateTime Date,
    string? SourceType,
    string? SourceSkillTag,
    string? SourceConceptTag,
    string Prompt,
    string Status,
    int? Score,
    DateTime CreatedAt);

public sealed record DailyChallengeSubmissionDto(
    Guid Id,
    Guid DailyChallengeId,
    bool Duplicate,
    int XpAwarded,
    int TotalXP,
    int Quality,
    DateTime CreatedAt);

public sealed record XpEventDto(
    Guid Id,
    string EventKey,
    string EventType,
    int XpDelta,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    DateTime CreatedAt);

public sealed record NotificationDto(
    Guid Id,
    string Type,
    string Title,
    string Body,
    string Status,
    string Severity,
    string? RelatedEntityType,
    Guid? RelatedEntityId,
    string Channel,
    string? PushStatus,
    DateTime CreatedAt,
    DateTime? ReadAt);

public sealed record CreateNotificationRequest(
    string Type,
    string Title,
    string Body,
    string Severity = "info",
    string? RelatedEntityType = null,
    Guid? RelatedEntityId = null,
    DateTime? ExpiresAt = null);
