using System;

namespace Orka.Core.Entities;

public class ClassroomInteraction
{
    public Guid Id { get; set; }
    public Guid ClassroomSessionId { get; set; }
    public string Question { get; set; } = string.Empty;
    public string AnswerScript { get; set; } = string.Empty;
    public byte[]? AudioBytes { get; set; }
    public string ContentType { get; set; } = "audio/mpeg";
    public long AudioByteLength { get; set; }
    public DateTime? AudioExpiresAt { get; set; }
    public DateTime? AudioPurgedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ClassroomSession ClassroomSession { get; set; } = null!;
}
