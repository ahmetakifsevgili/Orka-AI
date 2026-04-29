using System;

namespace Orka.Core.DTOs;

public class RecordQuizAttemptRequest
{
    public Guid? QuizRunId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string? MessageId { get; set; }
    public string? QuestionId { get; set; }
    public string? Question { get; set; }
    public string? SelectedOptionId { get; set; }
    public bool IsCorrect { get; set; }
    public string? Explanation { get; set; }
    public string? SkillTag { get; set; }
    public string? TopicPath { get; set; }
    public string? Difficulty { get; set; }
    public string? CognitiveType { get; set; }
    public string? QuestionHash { get; set; }
    public string? SourceRefsJson { get; set; }
}
