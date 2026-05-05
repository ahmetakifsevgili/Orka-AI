using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class XpEventService : IXpEventService
{
    private readonly OrkaDbContext _db;

    public XpEventService(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<XpAwardResult> AwardAsync(
        Guid userId,
        string eventKey,
        string eventType,
        int xpDelta,
        string? relatedEntityType,
        Guid? relatedEntityId,
        CancellationToken ct = default)
    {
        eventKey = string.IsNullOrWhiteSpace(eventKey) ? $"{eventType}:{relatedEntityId}" : eventKey.Trim();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user == null) return new XpAwardResult(false, 0, 0, 0, []);

        var existing = await _db.XpEvents.AsNoTracking()
            .AnyAsync(e => e.UserId == userId && e.EventKey == eventKey, ct);
        if (existing)
            return new XpAwardResult(false, 0, user.TotalXP, user.CurrentStreak, []);

        var now = DateTime.UtcNow;
        var xpEvent = new XpEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EventKey = eventKey,
            EventType = eventType,
            XpDelta = xpDelta,
            RelatedEntityType = relatedEntityType,
            RelatedEntityId = relatedEntityId,
            CreatedAt = now
        };

        _db.XpEvents.Add(xpEvent);
        user.TotalXP += xpDelta;
        var today = now.Date;
        if (user.LastActiveDate?.Date == today.AddDays(-1)) user.CurrentStreak += 1;
        else if (user.LastActiveDate?.Date != today) user.CurrentStreak = 1;
        user.LastActiveDate = now;

        var badges = await AwardBadgesAsync(user, xpEvent, ct);
        return new XpAwardResult(true, xpDelta, user.TotalXP, user.CurrentStreak, badges);
    }

    private async Task<IReadOnlyList<BadgeDto>> AwardBadgesAsync(User user, XpEvent sourceEvent, CancellationToken ct)
    {
        await EnsureDefaultBadgesAsync(ct);
        var earned = new List<BadgeDto>();
        var candidates = await _db.Badges.Where(b => b.IsActive).ToListAsync(ct);
        foreach (var badge in candidates)
        {
            var shouldAward = badge.Code switch
            {
                "first_review_completed" => sourceEvent.EventType.Contains("review", StringComparison.OrdinalIgnoreCase),
                "daily_challenge_completed" => sourceEvent.EventType.Contains("daily", StringComparison.OrdinalIgnoreCase),
                "source_learning_started" => sourceEvent.EventType.Contains("source", StringComparison.OrdinalIgnoreCase),
                "xp_100" => user.TotalXP >= 100,
                _ => false
            };
            if (!shouldAward) continue;

            var exists = await _db.UserBadges.AnyAsync(ub => ub.UserId == user.Id && ub.BadgeId == badge.Id, ct);
            if (exists) continue;

            _db.UserBadges.Add(new UserBadge
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                BadgeId = badge.Id,
                EarnedAt = DateTime.UtcNow,
                SourceEventId = sourceEvent.Id
            });
            earned.Add(ToBadgeDto(badge, DateTime.UtcNow));
        }

        return earned;
    }

    private async Task EnsureDefaultBadgesAsync(CancellationToken ct)
    {
        if (await _db.Badges.AnyAsync(ct)) return;
        var now = DateTime.UtcNow;
        _db.Badges.AddRange(
            new Badge { Id = Guid.NewGuid(), Code = "first_review_completed", Name = "First Review", Description = "Completed a review item.", IconKey = "repeat", RuleType = "event", CreatedAt = now },
            new Badge { Id = Guid.NewGuid(), Code = "daily_challenge_completed", Name = "Daily Spark", Description = "Completed a daily challenge.", IconKey = "flame", RuleType = "event", CreatedAt = now },
            new Badge { Id = Guid.NewGuid(), Code = "source_learning_started", Name = "Source Learner", Description = "Started learning from a source.", IconKey = "file-text", RuleType = "event", CreatedAt = now },
            new Badge { Id = Guid.NewGuid(), Code = "xp_100", Name = "100 XP", Description = "Reached 100 XP.", IconKey = "award", RuleType = "threshold", Threshold = 100, CreatedAt = now });
    }

    private static BadgeDto ToBadgeDto(Badge badge, DateTime? earnedAt) =>
        new(badge.Id, badge.Code, badge.Name, badge.Description, badge.IconKey, badge.RuleType, badge.Threshold ?? 0, earnedAt);
}
