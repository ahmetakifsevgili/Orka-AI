using System;

namespace Orka.Core.Entities;

public class UserBadge
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid BadgeId { get; set; }
    public Badge Badge { get; set; } = null!;
    public DateTime EarnedAt { get; set; }
    public Guid? SourceEventId { get; set; }
    public XpEvent? SourceEvent { get; set; }
    public string? MetadataJson { get; set; }
}
