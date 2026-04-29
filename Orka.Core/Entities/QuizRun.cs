using System;

namespace Orka.Core.Entities;

public class QuizRun
{
    public Guid Id { get; set; }
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
