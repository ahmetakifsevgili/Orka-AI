namespace Orka.Core.Entities;

public sealed class TeachingEvidenceItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? TutorTurnStateId { get; set; }
    public Guid? TutorActionTraceId { get; set; }
    public Guid? TutorToolCallId { get; set; }
    public string EvidenceType { get; set; } = "knowledge_entity";
    public string Provider { get; set; } = string.Empty;
    public string ConceptKey { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string FactualClaim { get; set; } = string.Empty;
    public string AnalogyCandidate { get; set; } = string.Empty;
    public string ClassroomUse { get; set; } = string.Empty;
    public string? CitationUrl { get; set; }
    public string CitationLabel { get; set; } = string.Empty;
    public decimal Confidence { get; set; } = 0.50m;
    public string Freshness { get; set; } = "static";
    public string RiskLevel { get; set; } = "low";
    public string RawPayloadHash { get; set; } = string.Empty;
    public string RawPayloadJson { get; set; } = "{}";
    public string Status { get; set; } = "ready";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
}

public sealed class TeachingEvidenceProviderHealth
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = string.Empty;
    public string EvidenceType { get; set; } = string.Empty;
    public string Status { get; set; } = "unknown";
    public bool Success { get; set; }
    public int? LastStatusCode { get; set; }
    public string? LastErrorCode { get; set; }
    public long LatencyMs { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}
