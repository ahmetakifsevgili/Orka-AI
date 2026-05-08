namespace Orka.Core.Entities;

public class DiagnosticProfile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? TopicId { get; set; }
    public Topic? Topic { get; set; }
    public Guid? QuizRunId { get; set; }
    public QuizRun? QuizRun { get; set; }
    public Guid? PlanRequestId { get; set; }
    public Guid? ConceptGraphSnapshotId { get; set; }
    public ConceptGraphSnapshot? ConceptGraphSnapshot { get; set; }
    public int AnsweredCount { get; set; }
    public int CorrectCount { get; set; }
    public int AccuracyPercent { get; set; }
    public string MeasuredLevel { get; set; } = "beginner";
    public string ProfileJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
