using System;

namespace Orka.Core.Entities;

public class Flashcard
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? TopicId { get; set; }
    public Topic? Topic { get; set; }
    public Guid? LearningSourceId { get; set; }
    public LearningSource? LearningSource { get; set; }
    public Guid? WikiPageId { get; set; }
    public WikiPage? WikiPage { get; set; }
    public Guid? QuizAttemptId { get; set; }
    public QuizAttempt? QuizAttempt { get; set; }
    public string Front { get; set; } = string.Empty;
    public string Back { get; set; } = string.Empty;
    public string? Hint { get; set; }
    public string? SkillTag { get; set; }
    public string? ConceptTag { get; set; }
    public string? LearningObjective { get; set; }
    public string? Difficulty { get; set; }
    public string Status { get; set; } = "active";
    public string CreatedFrom { get; set; } = "manual";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastReviewedAt { get; set; }
    public string? MetadataJson { get; set; }
}
