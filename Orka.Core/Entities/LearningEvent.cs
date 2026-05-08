namespace Orka.Core.Entities;

public class LearningEvent
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? TopicId { get; set; }
    public Topic? Topic { get; set; }
    public Guid? SessionId { get; set; }
    public Session? Session { get; set; }
    public Guid? QuizAttemptId { get; set; }
    public QuizAttempt? QuizAttempt { get; set; }
    public Guid? AssessmentItemId { get; set; }
    public AssessmentItem? AssessmentItem { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Actor { get; set; } = "learner";
    public string Verb { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string? ObjectId { get; set; }
    public string? ConceptKey { get; set; }
    public string? SkillTag { get; set; }
    public int? Score { get; set; }
    public bool? IsPositive { get; set; }
    public string? PayloadJson { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
