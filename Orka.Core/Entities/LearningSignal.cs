using System;

namespace Orka.Core.Entities;

public class LearningSignal
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? QuizAttemptId { get; set; }
    public string SignalType { get; set; } = string.Empty;
    public string? SkillTag { get; set; }
    public string? TopicPath { get; set; }
    public int? Score { get; set; }
    public bool? IsPositive { get; set; }
    public string? PayloadJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Topic? Topic { get; set; }
    public Session? Session { get; set; }
    public QuizAttempt? QuizAttempt { get; set; }
}
