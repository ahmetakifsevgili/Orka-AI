using System;

namespace Orka.Core.DTOs;

public class QuizAttemptDto
{
    public Guid Id { get; set; }
    public string Question { get; set; } = string.Empty;
    public string UserAnswer { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
