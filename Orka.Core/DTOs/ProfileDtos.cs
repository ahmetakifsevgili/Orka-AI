namespace Orka.Core.DTOs;

public sealed record BadgeDto(
    Guid Id,
    string Code,
    string Name,
    string Description,
    string IconKey,
    string RuleType,
    int Threshold,
    DateTime? EarnedAt);

public sealed record XpProfileDto(
    int TotalXP,
    int CurrentStreak,
    DateTime? LastActiveDate,
    int Level,
    int XpInLevel,
    int XpToNextLevel,
    string LevelLabel,
    IReadOnlyList<BadgeDto> Badges,
    IReadOnlyList<BadgeDto> AvailableBadges);

public sealed record XpAwardResult(
    bool Awarded,
    int XpAwarded,
    int TotalXP,
    int CurrentStreak,
    IReadOnlyList<BadgeDto> NewlyEarnedBadges);
