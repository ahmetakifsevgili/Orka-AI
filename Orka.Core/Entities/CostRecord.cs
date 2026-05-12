namespace Orka.Core.Entities;

public class CostRecord
{
    public Guid Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public Guid? UserId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? MessageId { get; set; }
    public string AgentRole { get; set; } = "unknown";
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public int EstimatedTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public bool Success { get; set; } = true;
    public string? ErrorCode { get; set; }
    public string? MetadataJson { get; set; }
}
