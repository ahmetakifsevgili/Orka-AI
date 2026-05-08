namespace Orka.Core.Entities;

public sealed class LearningEventSchemaViolation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LearningEventId { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string ViolationCode { get; set; } = string.Empty;
    public string ViolationDetail { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public LearningEvent LearningEvent { get; set; } = null!;
    public User User { get; set; } = null!;
    public Topic? Topic { get; set; }
}
