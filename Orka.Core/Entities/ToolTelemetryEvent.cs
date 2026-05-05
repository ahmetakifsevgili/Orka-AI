namespace Orka.Core.Entities;

public class ToolTelemetryEvent
{
    public Guid Id { get; set; }
    public DateTime OccurredAt { get; set; }
    public Guid? UserId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? TopicId { get; set; }
    public string ToolId { get; set; } = string.Empty;
    public string CapabilityStatus { get; set; } = "unknown";
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public long LatencyMs { get; set; }
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public bool FallbackUsed { get; set; }
    public string? CorrelationId { get; set; }
    public string? MetadataJson { get; set; }
}
