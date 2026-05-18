namespace Orka.Core.Entities;

public sealed class ToolRuntimeTrace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? ActiveLessonSnapshotId { get; set; }
    public Guid? StudentContextSnapshotId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public Guid? TutorActionTraceId { get; set; }
    public string ToolId { get; set; } = string.Empty;
    public string Caller { get; set; } = "internal";
    public string Purpose { get; set; } = string.Empty;
    public string Decision { get; set; } = "deny";
    public string Status { get; set; } = "decided";
    public string RiskLevel { get; set; } = "low";
    public bool CanGroundClaims { get; set; }
    public string? InputSummary { get; set; }
    public string? SafeResultSummary { get; set; }
    public string EvidenceJson { get; set; } = "[]";
    public string? FallbackReason { get; set; }
    public string? ErrorCode { get; set; }
    public long LatencyMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public bool IsDeleted { get; set; }
}
