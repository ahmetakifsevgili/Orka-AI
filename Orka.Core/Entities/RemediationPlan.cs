using System;

namespace Orka.Core.Entities;

public class RemediationPlan
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string SkillTag { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string LessonMarkdown { get; set; } = string.Empty;
    public string? MicroQuizJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public User User { get; set; } = null!;
    public Topic Topic { get; set; } = null!;
    public Session? Session { get; set; }
}
