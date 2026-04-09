using Orka.Core.Enums;
using MediatR;

namespace Orka.Core.Entities;

public class Session
{
    public Guid Id { get; set; }
    public Guid? TopicId { get; set; }
    public Topic? Topic { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public int SessionNumber { get; set; }
    public string? Summary { get; set; }
    public int TotalTokensUsed { get; set; }
    public decimal TotalCostUSD { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public SessionState CurrentState { get; set; } = SessionState.Learning;
    public string? PendingQuiz { get; set; }

    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
