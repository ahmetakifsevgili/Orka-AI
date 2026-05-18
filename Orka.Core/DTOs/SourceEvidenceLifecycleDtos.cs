namespace Orka.Core.DTOs;

public sealed class SourceEvidenceBundleDto
{
    public Guid Id { get; set; }
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
    public IReadOnlyList<SourceEvidenceItemDto> EvidenceItems { get; set; } = Array.Empty<SourceEvidenceItemDto>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public sealed class SourceEvidenceItemDto
{
    public Guid? SourceId { get; set; }
    public Guid? ChunkId { get; set; }
    public string SourceType { get; set; } = "document";
    public string Title { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Url { get; set; }
    public int? PageNumber { get; set; }
    public string? Section { get; set; }
    public string SnippetSummary { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string ScopeRelation { get; set; } = "unknown";
    public string RetrievalScope { get; set; } = "unknown";
    public string Status { get; set; } = "active";
    public string? UserSafeWarning { get; set; }
}

public sealed class SourceEvidenceBundleRequestDto
{
    public Guid? SessionId { get; set; }
    public string? Question { get; set; }
}

public sealed class SourceLifecycleSummaryDto
{
    public Guid TopicId { get; set; }
    public int SourceCount { get; set; }
    public int ReadySourceCount { get; set; }
    public int StaleSourceCount { get; set; }
    public int DeletedSourceCount { get; set; }
    public int FailedSourceCount { get; set; }
    public int ActiveChunkCount { get; set; }
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

public sealed class MarkSourceStaleRequestDto
{
    public string? Reason { get; set; }
}

public sealed class ValidateSourceCitationSetRequestDto
{
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public IReadOnlyList<ValidateSourceCitationDto> Citations { get; set; } = Array.Empty<ValidateSourceCitationDto>();
}

public sealed class ValidateSourceCitationDto
{
    public string CitationId { get; set; } = string.Empty;
    public Guid? SourceId { get; set; }
    public Guid? ChunkId { get; set; }
    public int? PageNumber { get; set; }
    public int? ChunkIndex { get; set; }
}

public sealed class SourceCitationValidationResultDto
{
    public string CitationId { get; set; } = string.Empty;
    public bool Supported { get; set; }
    public string Status { get; set; } = "unknown";
    public string SourceType { get; set; } = "document";
    public string? UserSafeWarning { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? ChunkId { get; set; }
    public int? PageNumber { get; set; }
}

public sealed class SourceCitationSetValidationDto
{
    public int TotalCount { get; set; }
    public int SupportedCount { get; set; }
    public int UnsupportedCount { get; set; }
    public IReadOnlyList<SourceCitationValidationResultDto> Results { get; set; } = Array.Empty<SourceCitationValidationResultDto>();
}

public sealed class WikiKnowledgeNotebookDto
{
    public Guid TopicId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public string SourceCoverage { get; set; } = "no_sources";
    public string ConceptCoverage { get; set; } = "unknown";
    public IReadOnlyList<WikiNotebookSectionDto> Sections { get; set; } = Array.Empty<WikiNotebookSectionDto>();
    public IReadOnlyList<string> SourceWarnings { get; set; } = Array.Empty<string>();
    public DateTime LastUpdatedAt { get; set; }
}

public sealed class WikiNotebookSectionDto
{
    public string SectionKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? ConceptKey { get; set; }
    public IReadOnlyList<SourceEvidenceItemDto> EvidenceItems { get; set; } = Array.Empty<SourceEvidenceItemDto>();
    public IReadOnlyList<Guid> WikiBlockIds { get; set; } = Array.Empty<Guid>();
    public IReadOnlyList<Guid> SourceIds { get; set; } = Array.Empty<Guid>();
    public string Status { get; set; } = "evidence_insufficient";
}

public sealed class SourceNotebookDto
{
    public Guid? TopicId { get; set; }
    public Guid? SourceId { get; set; }
    public string Surface { get; set; } = "source_collection";
    public string Title { get; set; } = string.Empty;
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public int SourceCount { get; set; }
    public int ReadySourceCount { get; set; }
    public int ChunkCount { get; set; }
    public decimal CitationCoverage { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public IReadOnlyList<SourceNotebookSourceDto> Sources { get; set; } = Array.Empty<SourceNotebookSourceDto>();
    public IReadOnlyList<SourceNotebookWikiPageDto> LinkedWikiPages { get; set; } = Array.Empty<SourceNotebookWikiPageDto>();
    public IReadOnlyList<SourceNotebookPackRefDto> Packs { get; set; } = Array.Empty<SourceNotebookPackRefDto>();
    public IReadOnlyList<NotebookStudioNextActionDto> NextActions { get; set; } = Array.Empty<NotebookStudioNextActionDto>();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SourceNotebookSourceDto
{
    public Guid Id { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public int PageCount { get; set; }
    public int ChunkCount { get; set; }
    public decimal CitationCoverage { get; set; }
    public Guid? LinkedWikiPageId { get; set; }
    public string? LinkedWikiPageTitle { get; set; }
    public Guid? LatestPackId { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class SourceNotebookWikiPageDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string PageKey { get; set; } = string.Empty;
    public string PageType { get; set; } = "orkalm_source";
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
}

public sealed class SourceNotebookPackRefDto
{
    public Guid Id { get; set; }
    public string PackType { get; set; } = "source_digest";
    public string PackStatus { get; set; } = "draft";
    public string Title { get; set; } = string.Empty;
    public Guid? SourceId { get; set; }
    public Guid? WikiPageId { get; set; }
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public DateTimeOffset UpdatedAt { get; set; }
}

public class SourceConceptLinkDto
{
    public Guid? SourceId { get; set; }
    public string SourceTitle { get; set; } = string.Empty;
    public Guid? SourcePageId { get; set; }
    public string ConceptKey { get; set; } = string.Empty;
    public string ConceptTitle { get; set; } = string.Empty;
    public Guid? WikiPageId { get; set; }
    public string LinkType { get; set; } = "source_mentions";
    public string Confidence { get; set; } = "low";
    public decimal? ConfidenceScore { get; set; }
    public string Basis { get; set; } = "title_match";
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public bool IsSuggestion { get; set; } = true;
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class SourceConceptLinkCandidateDto : SourceConceptLinkDto
{
}

public sealed class SourceConceptLinkSummaryDto
{
    public Guid? TopicId { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? WikiPageId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public int ConfirmedLinkCount { get; set; }
    public int SuggestedLinkCount { get; set; }
    public IReadOnlyList<SourceConceptLinkDto> Links { get; set; } = Array.Empty<SourceConceptLinkDto>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SourceConceptGraphDto
{
    public Guid TopicId { get; set; }
    public string GraphStatus { get; set; } = "ready";
    public IReadOnlyList<SourceConceptGraphNodeDto> Nodes { get; set; } = Array.Empty<SourceConceptGraphNodeDto>();
    public IReadOnlyList<SourceConceptGraphEdgeDto> Edges { get; set; } = Array.Empty<SourceConceptGraphEdgeDto>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SourceConceptGraphNodeDto
{
    public string Id { get; set; } = string.Empty;
    public string NodeType { get; set; } = "concept_page";
    public string Label { get; set; } = string.Empty;
    public Guid? SourceId { get; set; }
    public Guid? WikiPageId { get; set; }
    public string? ConceptKey { get; set; }
    public string Status { get; set; } = "ready";
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
}

public sealed class SourceConceptGraphEdgeDto
{
    public string SourceNodeId { get; set; } = string.Empty;
    public string TargetNodeId { get; set; } = string.Empty;
    public string LinkType { get; set; } = "source_mentions";
    public string Confidence { get; set; } = "low";
    public decimal? ConfidenceScore { get; set; }
    public string Basis { get; set; } = "existing_wiki_link";
    public bool IsSuggestion { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}
