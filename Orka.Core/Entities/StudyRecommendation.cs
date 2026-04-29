using System;

namespace Orka.Core.Entities;

public class StudyRecommendation
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TopicId { get; set; }
    public string RecommendationType { get; set; } = "practice";
    public string Title { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? SkillTag { get; set; }
    public string? ActionPrompt { get; set; }
    public bool IsDone { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public User User { get; set; } = null!;
    public Topic Topic { get; set; } = null!;
}
