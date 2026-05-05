using System;

namespace Orka.Core.Entities;

public class PushSubscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Endpoint { get; set; } = string.Empty;
    public string? P256dh { get; set; }
    public string? Auth { get; set; }
    public string? DeviceLabel { get; set; }
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
