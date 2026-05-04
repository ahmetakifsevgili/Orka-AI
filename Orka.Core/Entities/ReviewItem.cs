using System;
using System.Collections.Generic;

namespace Orka.Core.Entities;

public class ReviewItem
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? TopicId { get; set; }
    public Topic? Topic { get; set; }
    public string ReviewKey { get; set; } = string.Empty;
    public string? SkillTag { get; set; }
    public string? ConceptTag { get; set; }
    public string? LearningObjective { get; set; }
    public string? MistakeCategory { get; set; }
    public string? SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public DateTime DueAt { get; set; }
    public DateTime? LastReviewedAt { get; set; }
    public int IntervalDays { get; set; }
    public double EaseFactor { get; set; } = 2.5;
    public int RepetitionCount { get; set; }
    public int LapseCount { get; set; }
    public int SuccessStreak { get; set; }
    public string Status { get; set; } = "active";
    public Guid? QuizAttemptId { get; set; }
    public QuizAttempt? QuizAttempt { get; set; }
    public Guid? LearningSignalId { get; set; }
    public LearningSignal? LearningSignal { get; set; }
    public Guid? FlashcardId { get; set; }
    public Flashcard? Flashcard { get; set; }
    public Guid? RemediationPlanId { get; set; }
    public RemediationPlan? RemediationPlan { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? MetadataJson { get; set; }

    public ICollection<DailyChallenge> DailyChallenges { get; set; } = new List<DailyChallenge>();
}
