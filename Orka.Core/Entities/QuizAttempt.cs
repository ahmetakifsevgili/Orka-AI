using System;

namespace Orka.Core.Entities;

public class QuizAttempt
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Guid TopicId { get; set; }
    public Guid UserId { get; set; }

    public string Question { get; set; } = string.Empty;
    public string UserAnswer { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public string Explanation { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Session Session { get; set; } = null!;
    public Topic Topic { get; set; } = null!;
    public User User { get; set; } = null!;
}
