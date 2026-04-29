using System;

namespace Orka.Core.Entities;

public class ClassroomSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? AudioOverviewJobId { get; set; }
    public string Transcript { get; set; } = string.Empty;
    public string LastSegment { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Topic? Topic { get; set; }
    public Session? Session { get; set; }
    public AudioOverviewJob? AudioOverviewJob { get; set; }
}
