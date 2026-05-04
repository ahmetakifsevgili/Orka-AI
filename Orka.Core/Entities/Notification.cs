using System;

namespace Orka.Core.Entities;

public class Notification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Status { get; set; } = "unread";
    public string Severity { get; set; } = "info";
    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public string Channel { get; set; } = "in-app";
    public string? PushStatus { get; set; }
    public string? FirebaseMessageId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? MetadataJson { get; set; }
}
