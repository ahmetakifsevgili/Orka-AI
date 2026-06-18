namespace Orka.Core.DTOs;

public sealed class LearningContextPackDto
{
    public string SchemaVersion { get; set; } = "orka.learning-context-pack.v1.1";
    public string LearningStateVersion { get; set; } = "lsv_unknown";
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string ScopeStatus { get; set; } = "unknown";
    public string ContextWatermark { get; set; } = string.Empty;
    public int EstimatedTokenCount { get; set; }
    public IReadOnlyList<LearningContextPackBlockDto> Blocks { get; set; } = Array.Empty<LearningContextPackBlockDto>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public LearningContextPackTraceDto Trace { get; set; } = new();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LearningContextPackBlockDto
{
    public string BlockType { get; set; } = string.Empty;
    public string Status { get; set; } = "unknown";
    public string Summary { get; set; } = string.Empty;
    public int Priority { get; set; }
    public Guid? SnapshotId { get; set; }
    public LearningContextPackRefDto? SnapshotRef { get; set; }
    public LearningContextPackRefDto? SourceRef { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public IReadOnlyDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
}

public sealed class LearningContextPackRefDto
{
    public string Kind { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Status { get; set; } = "unknown";
    public string EvidenceStatus { get; set; } = "unknown";
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public sealed class LearningContextPackTraceDto
{
    public string SchemaVersion { get; set; } = "orka.learning-context-pack.trace.v1";
    public int TokenBudget { get; set; }
    public int InitialEstimatedTokenCount { get; set; }
    public int EstimatedTokenCount { get; set; }
    public IReadOnlyList<LearningContextPackTraceBlockDto> SelectedBlocks { get; set; } = Array.Empty<LearningContextPackTraceBlockDto>();
    public IReadOnlyList<LearningContextPackDroppedBlockDto> DroppedBlocks { get; set; } = Array.Empty<LearningContextPackDroppedBlockDto>();
    public IReadOnlyList<LearningContextPackDroppedWarningDto> DroppedWarnings { get; set; } = Array.Empty<LearningContextPackDroppedWarningDto>();
}

public sealed class LearningContextPackTraceBlockDto
{
    public string BlockType { get; set; } = string.Empty;
    public string Status { get; set; } = "unknown";
    public int Priority { get; set; }
    public int EstimatedTokenCount { get; set; }
    public string? RefKind { get; set; }
    public string? RefId { get; set; }
    public string? RefVersion { get; set; }
}

public sealed class LearningContextPackDroppedBlockDto
{
    public string BlockType { get; set; } = string.Empty;
    public string Reason { get; set; } = "token_budget";
    public int Priority { get; set; }
    public int EstimatedTokenCount { get; set; }
}

public sealed class LearningContextPackDroppedWarningDto
{
    public string Warning { get; set; } = string.Empty;
    public string Reason { get; set; } = "token_budget";
}
