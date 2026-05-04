namespace Orka.Core.DTOs;

public sealed record ReviewScheduleResult(
    int IntervalDays,
    double EaseFactor,
    int RepetitionCount,
    DateTime NextReviewAt);

public sealed record ReviewItemDto(
    Guid Id,
    Guid? SkillMasteryId,
    string SkillTitle,
    DateTime NextReviewAt,
    int IntervalDays,
    double EaseFactor,
    int RepetitionCount,
    int LastReviewQuality,
    Guid? FlashcardId = null,
    string? FlashcardFront = null,
    string? FlashcardBack = null);

public sealed record CompleteReviewRequest(int Quality);
