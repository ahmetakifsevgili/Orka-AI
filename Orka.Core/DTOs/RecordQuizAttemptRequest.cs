using System;

namespace Orka.Core.DTOs;

public class RecordQuizAttemptRequest
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string? MessageId { get; set; }
    public string? Question { get; set; }
    public string? SelectedOptionId { get; set; }
    public bool IsCorrect { get; set; }
    public string? Explanation { get; set; }
}
