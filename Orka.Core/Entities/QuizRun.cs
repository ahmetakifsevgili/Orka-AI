using System;
using Orka.Core.Interfaces;

namespace Orka.Core.Entities;

public class QuizRun : IMustHaveTenant
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string QuizType { get; set; } = "lesson";
    public string Status { get; set; } = "active";
    public int TotalQuestions { get; set; }
    public int CorrectCount { get; set; }
    public string? FailedSkillsJson { get; set; }
    public string? MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public User User { get; set; } = null!;
    public Topic? Topic { get; set; }
    public Session? Session { get; set; }
    public ICollection<QuizAttempt> Attempts { get; set; } = new List<QuizAttempt>();
}
