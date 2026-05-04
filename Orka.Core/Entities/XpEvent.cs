using System;

namespace Orka.Core.Entities;

public class XpEvent
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string EventKey { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public int XpDelta { get; set; }
    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? MetadataJson { get; set; }
}
