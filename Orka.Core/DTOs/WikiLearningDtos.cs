using Orka.Core.DTOs.Chat;

namespace Orka.Core.DTOs;

public sealed class WikiLearningRequestDto
{
    public Guid UserId { get; set; }
    public Guid TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string Question { get; set; } = string.Empty;
    public string Mode { get; set; } = "wiki";
    public Guid? SourceId { get; set; }
    public Guid? ActivePageId { get; set; }
}

public sealed class TopicSourceEvidenceDto
{
    public Guid? RetrievalRunId { get; set; }
    public Guid? ChunkId { get; set; }
    public Guid SourceId { get; set; }
    public string SourceTitle { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public Guid? SourceTopicId { get; set; }
    public string? SourceTopicTitle { get; set; }
    public string ScopeRelation { get; set; } = "unknown";
    public string RetrievalScope { get; set; } = "unknown";
    public int PageNumber { get; set; }
    public int ChunkIndex { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? HighlightHint { get; set; }
    public double Score { get; set; }
    public decimal EmbeddingScore { get; set; }
    public decimal LexicalScore { get; set; }
    public decimal FusedScore { get; set; }
    public int Rank { get; set; }
    public string QualityStatus { get; set; } = "unknown";
    public string CitationId => $"[doc:{SourceId}:p{PageNumber}]";
}

public sealed class WikiBlockEvidenceDto
{
    public Guid PageId { get; set; }
    public Guid BlockId { get; set; }
    public string PageTitle { get; set; } = string.Empty;
    public string BlockTitle { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
    public string CitationId => $"[wiki:{PageId}:{BlockId}]";
}

public sealed class WikiEvidenceBundleDto
{
    public Guid UserId { get; set; }
    public Guid TopicId { get; set; }
    public string TopicTitle { get; set; } = string.Empty;
    public Guid? ConceptGraphSnapshotId { get; set; }
    public IReadOnlyList<TopicSourceEvidenceDto> SourceChunks { get; set; } = Array.Empty<TopicSourceEvidenceDto>();
    public IReadOnlyList<WikiBlockEvidenceDto> WikiBlocks { get; set; } = Array.Empty<WikiBlockEvidenceDto>();
    public IReadOnlyList<CitationDto> Citations { get; set; } = Array.Empty<CitationDto>();
    public IReadOnlyList<string> ActiveConcepts { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> WeakConcepts { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Recommendations { get; set; } = Array.Empty<string>();
    public string LearnerState { get; set; } = "unknown";
    public decimal? MasteryProbability { get; set; }
    public decimal? Confidence { get; set; }
    public int SourceCount { get; set; }
    public int ReadySourceCount { get; set; }
    public string CitationHealth { get; set; } = "unknown";
    public Guid? LatestRetrievalRunId { get; set; }
    public string RetrievalHealth { get; set; } = "unknown";
    public string RagQualityStatus { get; set; } = "unknown";
    public decimal CitationCoverage { get; set; }
    public int UnsupportedCitationCount { get; set; }
}

public sealed class WikiAnswerPolicyDto
{
    public bool CanAnswer { get; set; }
    public bool RequiresCitation { get; set; }
    public string GroundingStatus { get; set; } = "no_source";
    public string? FallbackReason { get; set; }
    public string UserSafeMessage { get; set; } = "Bu bilgi mevcut kaynaklarda net görünmüyor.";
    public string PromptBlock { get; set; } = string.Empty;
}

public sealed class WikiCitationGuardResultDto
{
    public string Answer { get; set; } = string.Empty;
    public bool IsHealthy { get; set; }
    public bool Repaired { get; set; }
    public string CitationCoverageStatus { get; set; } = "unknown";
    public ChatResponseMetadata Metadata { get; set; } = new();
}

public sealed class WikiStreamEventDto
{
    public string Type { get; set; } = "token";
    public string? Content { get; set; }
    public IReadOnlyList<CitationDto>? Citations { get; set; }
    public Guid? ArtifactId { get; set; }
    public string? ArtifactType { get; set; }
    public ChatResponseMetadata? Metadata { get; set; }
    public Guid? MessageId { get; set; }
    public string? GroundingStatus { get; set; }
}

public sealed class WikiWorkspaceStateDto
{
    public Guid TopicId { get; set; }
    public string TopicTitle { get; set; } = string.Empty;
    public int WikiPageCount { get; set; }
    public int WikiBlockCount { get; set; }
    public int SourceCount { get; set; }
    public int ReadySourceCount { get; set; }
    public string CitationHealth { get; set; } = "unknown";
    public string RagQualityStatus { get; set; } = "unknown";
    public string RetrievalHealth { get; set; } = "unknown";
    public decimal CitationCoverage { get; set; }
    public int UnsupportedCitationCount { get; set; }
    public IReadOnlyList<string> ActiveConcepts { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> WeakConcepts { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> RecommendedActions { get; set; } = Array.Empty<string>();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}
