using System;

namespace Orka.Core.Entities;

public class AudioOverviewJob
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? TopicId { get; set; }
    public Topic? Topic { get; set; }
    public Guid? SessionId { get; set; }
    public Session? Session { get; set; }
    public string Status { get; set; } = "pending";
    public string Script { get; set; } = string.Empty;
    public string SpeakersJson { get; set; } = "[]";
    public byte[]? AudioBytes { get; set; }
    public string ContentType { get; set; } = "audio/mpeg";
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
