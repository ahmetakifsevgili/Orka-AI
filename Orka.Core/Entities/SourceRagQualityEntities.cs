namespace Orka.Core.Entities;

public sealed class SourceRetrievalRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? SourceId { get; set; }
    public string Query { get; set; } = string.Empty;
    public string RetrievalScope { get; set; } = "topic";
    public string Provider { get; set; } = "orka-source";
    public int RequestedTopK { get; set; }
    public int RetrievedCount { get; set; }
    public bool IsEmpty { get; set; }
    public decimal MaxScore { get; set; }
    public decimal AverageScore { get; set; }
    public string QualityStatus { get; set; } = "unknown";
    public string? Reason { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public User User { get; set; } = null!;
    public Topic? Topic { get; set; }
    public Session? Session { get; set; }
    public LearningSource? Source { get; set; }
    public ICollection<SourceRetrievalItem> Items { get; set; } = new List<SourceRetrievalItem>();
}

public sealed class SourceRetrievalItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceRetrievalRunId { get; set; }
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid SourceId { get; set; }
    public Guid? SourceChunkId { get; set; }
    public int PageNumber { get; set; }
    public int ChunkIndex { get; set; }
    public int Rank { get; set; }
    public decimal EmbeddingScore { get; set; }
    public decimal LexicalScore { get; set; }
    public decimal FusedScore { get; set; }
    public string QualityStatus { get; set; } = "unknown";
    public string? Reason { get; set; }
    public string Snippet { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public SourceRetrievalRun Run { get; set; } = null!;
    public LearningSource Source { get; set; } = null!;
    public SourceChunk? SourceChunk { get; set; }
}

public sealed class SourceCitationCheck
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? SourceRetrievalRunId { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? SourceChunkId { get; set; }
    public string CitationId { get; set; } = string.Empty;
    public string SourceType { get; set; } = "document";
    public int? PageNumber { get; set; }
    public int? ChunkIndex { get; set; }
    public string Answer { get; set; } = string.Empty;
    public string ClaimText { get; set; } = string.Empty;
    public string CheckStatus { get; set; } = "unknown";
    public decimal Confidence { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Topic? Topic { get; set; }
    public Session? Session { get; set; }
    public SourceRetrievalRun? RetrievalRun { get; set; }
    public LearningSource? Source { get; set; }
    public SourceChunk? SourceChunk { get; set; }
}

public sealed class SourceQualityReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SourceId { get; set; }
    public string QualityStatus { get; set; } = "unknown";
    public string RetrievalHealthStatus { get; set; } = "unknown";
    public string CitationCoverageStatus { get; set; } = "unknown";
    public string CitationSupportStatus { get; set; } = "unknown";
    public int RetrievalRunCount { get; set; }
    public int EmptyRunCount { get; set; }
    public int CitationCheckCount { get; set; }
    public int UnsupportedCitationCount { get; set; }
    public int CitationMissingCount { get; set; }
    public decimal AverageContextRelevance { get; set; }
    public decimal CitationCoverage { get; set; }
    public string ReportJson { get; set; } = "{}";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Topic? Topic { get; set; }
    public LearningSource? Source { get; set; }
}
