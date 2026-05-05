using System;

namespace Orka.Core.Entities;

public class DailyChallengeSubmission
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid DailyChallengeId { get; set; }
    public DailyChallenge DailyChallenge { get; set; } = null!;
    public string Answer { get; set; } = string.Empty;
    public int Quality { get; set; }
    public int XpAwarded { get; set; }
    public Guid? XpEventId { get; set; }
    public XpEvent? XpEvent { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? MetadataJson { get; set; }
}
