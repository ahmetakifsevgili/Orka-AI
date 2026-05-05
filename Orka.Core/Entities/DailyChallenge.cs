using System;
using System.Collections.Generic;

namespace Orka.Core.Entities;

public class DailyChallenge
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? TopicId { get; set; }
    public Topic? Topic { get; set; }
    public DateTime Date { get; set; }
    public string? SourceType { get; set; }
    public string? SourceSkillTag { get; set; }
    public string? SourceConceptTag { get; set; }
    public Guid? ReviewItemId { get; set; }
    public ReviewItem? ReviewItem { get; set; }
    public string QuestionsJson { get; set; } = "[]";
    public string Status { get; set; } = "active";
    public int? Score { get; set; }
    public int? CorrectCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? MetadataJson { get; set; }

    public ICollection<DailyChallengeSubmission> Submissions { get; set; } = new List<DailyChallengeSubmission>();
}
