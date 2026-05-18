namespace Orka.Core.Entities;

public sealed class SourceEvidenceBundle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string BundleHash { get; set; } = string.Empty;
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public int SourceCount { get; set; }
    public int ReadySourceCount { get; set; }
    public int ChunkCount { get; set; }
    public decimal CitationCoverage { get; set; }
    public int UnsupportedCitationCount { get; set; }
    public int StaleEvidenceCount { get; set; }
    public int DeletedEvidenceCount { get; set; }
    public string EvidenceJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public bool IsDeleted { get; set; }

    public User User { get; set; } = null!;
    public Topic? Topic { get; set; }
    public Session? Session { get; set; }
}

public sealed class SourceLifecycleEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SourceId { get; set; }
    public string EventType { get; set; } = "updated";
    public string? Reason { get; set; }
    public string? SafeSummary { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public User User { get; set; } = null!;
    public Topic? Topic { get; set; }
    public LearningSource? Source { get; set; }
}

public sealed class WikiKnowledgeNotebookSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid TopicId { get; set; }
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public string SourceCoverage { get; set; } = "no_sources";
    public string ConceptCoverage { get; set; } = "unknown";
    public string SectionsJson { get; set; } = "[]";
    public string SourceWarningsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public User User { get; set; } = null!;
    public Topic Topic { get; set; } = null!;
}
