using System;
using Orka.Core.Interfaces;

namespace Orka.Core.Entities;

public class QuizAttempt : IMustHaveTenant
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public Guid? QuizRunId { get; set; }
    public string? QuestionId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid UserId { get; set; }

    public string Question { get; set; } = string.Empty;
    public string UserAnswer { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string? SkillTag { get; set; }
    public Guid? AssessmentItemId { get; set; }
    public string? TopicPath { get; set; }
    public string? Difficulty { get; set; }
    public string? CognitiveType { get; set; }
    public string? QuestionHash { get; set; }
    public string? SourceRefsJson { get; set; }
    public int? ResponseTimeMs { get; set; }
    public bool WasSkipped { get; set; }
    public decimal? ConfidenceSelfRating { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public QuizRun? QuizRun { get; set; }
    public AssessmentItem? AssessmentItem { get; set; }
    public Session? Session { get; set; }
    public Topic? Topic { get; set; }
    public User User { get; set; } = null!;
}
