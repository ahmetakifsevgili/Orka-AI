using Orka.Core.DTOs.Chat;

namespace Orka.Core.DTOs;

public record LearningSourceSummaryDto(
    Guid Id,
    Guid? TopicId,
    Guid? SessionId,
    string SourceType,
    string Title,
    string FileName,
    int PageCount,
    int ChunkCount,
    string Status,
    DateTime CreatedAt,
    bool IsDeleted = false,
    int Version = 1);

public record SourcePageDto(
    Guid SourceId,
    int PageNumber,
    string Title,
    IReadOnlyList<SourceChunkDto> Chunks);

public record SourceChunkDto(
    Guid Id,
    int PageNumber,
    int ChunkIndex,
    string Text,
    string? HighlightHint);

public record SourceAskResultDto(
    string Answer,
    IReadOnlyList<SourceChunkDto> Citations,
    SourceMetadataDto? Metadata = null);

public sealed class SourceQuestionRequestDto
{
    public Guid? TopicId { get; set; }
    public Guid? SourceId { get; set; }
    public IReadOnlyList<Guid> SourceIds { get; set; } = Array.Empty<Guid>();
    public Guid? WikiPageId { get; set; }
    public Guid? NotebookPackId { get; set; }
    public string Question { get; set; } = string.Empty;
    public string Mode { get; set; } = "selected_source";
    public bool IncludeLearnerContext { get; set; } = true;
    public bool WriteWikiTrace { get; set; }
}

public sealed class SourceQuestionCitationDto
{
    public string CitationId { get; set; } = string.Empty;
    public Guid? SourceId { get; set; }
    public Guid? SourceChunkId { get; set; }
    public int? PageNumber { get; set; }
    public int? ChunkIndex { get; set; }
    public string Label { get; set; } = string.Empty;
    public string SourceTitle { get; set; } = string.Empty;
    public string SupportStatus { get; set; } = "unknown";
    public double? Confidence { get; set; }
}

public sealed class SourceQuestionContextDto
{
    public Guid? SourceId { get; set; }
    public string? SourceTitle { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? WikiPageId { get; set; }
    public string? WikiPageTitle { get; set; }
    public IReadOnlyList<SourceConceptLinkDto> RelatedConcepts { get; set; } = Array.Empty<SourceConceptLinkDto>();
    public IReadOnlyList<SourceConceptLinkDto> RelatedWikiPages { get; set; } = Array.Empty<SourceConceptLinkDto>();
}

public sealed class SourceQuestionSafetyDto
{
    public string Status { get; set; } = "safe";
    public IReadOnlyList<string> BlockedTerms { get; set; } = Array.Empty<string>();
    public bool RawPayloadRemoved { get; set; }
}

public sealed class SourceQuestionResponseDto
{
    public string Answer { get; set; } = string.Empty;
    public string SourceBasis { get; set; } = "evidence_insufficient";
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public IReadOnlyList<SourceQuestionCitationDto> Citations { get; set; } = Array.Empty<SourceQuestionCitationDto>();
    public IReadOnlyList<SourceConceptLinkDto> RelatedConcepts { get; set; } = Array.Empty<SourceConceptLinkDto>();
    public IReadOnlyList<SourceConceptLinkDto> RelatedWikiPages { get; set; } = Array.Empty<SourceConceptLinkDto>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public SourceQuestionSafetyDto Safety { get; set; } = new();
    public SourceQuestionContextDto Context { get; set; } = new();
    public Guid? TraceBlockId { get; set; }
    public IReadOnlyList<string> NextActions { get; set; } = Array.Empty<string>();
}

public sealed class SourceQuestionThreadRequestDto
{
    public Guid? TopicId { get; set; }
    public Guid? SourceId { get; set; }
    public IReadOnlyList<Guid> SourceIds { get; set; } = Array.Empty<Guid>();
    public Guid? WikiPageId { get; set; }
    public string? ConceptKey { get; set; }
    public string? Title { get; set; }
    public string? InitialQuestion { get; set; }
    public string Mode { get; set; } = "selected_source";
    public bool IncludeLearnerContext { get; set; } = true;
    public bool WriteWikiTrace { get; set; }
}

public sealed class SourceQuestionFollowUpRequestDto
{
    public string Question { get; set; } = string.Empty;
    public bool IncludeLearnerContext { get; set; } = true;
    public bool WriteWikiTrace { get; set; }
}

public sealed class SourceQuestionReviewStateDto
{
    public Guid? TurnId { get; set; }
    public string ReviewStatus { get; set; } = "needs_review";
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

public sealed class SourceQuestionThreadListDto
{
    public int Count { get; set; }
    public IReadOnlyList<SourceQuestionThreadDto> Items { get; set; } = Array.Empty<SourceQuestionThreadDto>();
}

public sealed class SourceQuestionThreadDto
{
    public Guid ThreadId { get; set; }
    public Guid? TopicId { get; set; }
    public IReadOnlyList<Guid> SourceIds { get; set; } = Array.Empty<Guid>();
    public Guid? WikiPageId { get; set; }
    public string? ConceptKey { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public string SourceBasis { get; set; } = "evidence_insufficient";
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string CitationReviewStatus { get; set; } = "not_checked";
    public IReadOnlyList<SourceConceptLinkDto> LinkedConcepts { get; set; } = Array.Empty<SourceConceptLinkDto>();
    public IReadOnlyList<SourceConceptLinkDto> LinkedWikiPages { get; set; } = Array.Empty<SourceConceptLinkDto>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public IReadOnlyList<SourceQuestionTurnDto> Turns { get; set; } = Array.Empty<SourceQuestionTurnDto>();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SourceQuestionTurnDto
{
    public Guid TurnId { get; set; }
    public string Question { get; set; } = string.Empty;
    public string SafeAnswerSummary { get; set; } = string.Empty;
    public string SourceBasis { get; set; } = "evidence_insufficient";
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public IReadOnlyList<SourceQuestionCitationDto> Citations { get; set; } = Array.Empty<SourceQuestionCitationDto>();
    public IReadOnlyList<SourceConceptLinkDto> RelatedConcepts { get; set; } = Array.Empty<SourceConceptLinkDto>();
    public IReadOnlyList<SourceConceptLinkDto> RelatedWikiPages { get; set; } = Array.Empty<SourceConceptLinkDto>();
    public string ReviewStatus { get; set; } = "not_checked";
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public Guid? TraceBlockId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SourceQuestionMemorySummaryDto
{
    public int ThreadCount { get; set; }
    public int TurnCount { get; set; }
    public int NeedsReviewCount { get; set; }
    public int DegradedCount { get; set; }
    public IReadOnlyList<string> RecentQuestions { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

public sealed class SourceStudySummaryDto
{
    public Guid? TopicId { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? WikiPageId { get; set; }
    public int SourceCount { get; set; }
    public int ThreadCount { get; set; }
    public int TurnCount { get; set; }
    public int ReviewedCount { get; set; }
    public int NeedsReviewCount { get; set; }
    public int DegradedCount { get; set; }
    public int CitationWarningCount { get; set; }
    public int RelatedConceptCount { get; set; }
    public int ComparedSourceCount { get; set; }
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public string StudyStatus { get; set; } = "empty";
    public string RecommendedNextAction { get; set; } = "add_source";
    public IReadOnlyList<string> NextActions { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> RecentQuestions { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MultiSourceCompareRequestDto
{
    public Guid? TopicId { get; set; }
    public IReadOnlyList<Guid> SourceIds { get; set; } = Array.Empty<Guid>();
    public Guid? WikiPageId { get; set; }
    public string? ConceptKey { get; set; }
    public bool IncludeConceptLinks { get; set; } = true;
    public bool IncludeCitationReview { get; set; } = true;
    public bool WriteWikiTrace { get; set; }
}

public sealed class MultiSourceCompareResultDto
{
    public Guid? TopicId { get; set; }
    public int ComparedSourceCount { get; set; }
    public string CompareStatus { get; set; } = "ready";
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public IReadOnlyList<MultiSourceCompareSourceDto> SourceSummaries { get; set; } = Array.Empty<MultiSourceCompareSourceDto>();
    public IReadOnlyList<MultiSourceConceptOverlapDto> SharedConcepts { get; set; } = Array.Empty<MultiSourceConceptOverlapDto>();
    public IReadOnlyList<MultiSourceConceptOverlapDto> SourceOnlyConcepts { get; set; } = Array.Empty<MultiSourceConceptOverlapDto>();
    public MultiSourceCitationCoverageDto CitationCoverage { get; set; } = new();
    public IReadOnlyList<CitationReviewItemDto> CitationReviewItems { get; set; } = Array.Empty<CitationReviewItemDto>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> NextActions { get; set; } = Array.Empty<string>();
    public Guid? TraceBlockId { get; set; }
    public string SafetyStatus { get; set; } = "safe";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MultiSourceCompareSourceDto
{
    public Guid SourceId { get; set; }
    public string SourceTitle { get; set; } = string.Empty;
    public string Status { get; set; } = "unknown";
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public int PageCount { get; set; }
    public int ChunkCount { get; set; }
    public decimal CitationCoverage { get; set; }
    public int CitationCheckCount { get; set; }
    public int SupportedCitationCount { get; set; }
    public int UnsupportedCitationCount { get; set; }
    public int MissingCitationCount { get; set; }
    public int NeedsReviewCitationCount { get; set; }
    public int LinkedConceptCount { get; set; }
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

public sealed class MultiSourceConceptOverlapDto
{
    public string ConceptKey { get; set; } = string.Empty;
    public string ConceptTitle { get; set; } = string.Empty;
    public Guid? WikiPageId { get; set; }
    public IReadOnlyList<Guid> SourceIds { get; set; } = Array.Empty<Guid>();
    public IReadOnlyList<string> SourceTitles { get; set; } = Array.Empty<string>();
    public string LinkConfidence { get; set; } = "low";
    public bool IsSuggestion { get; set; } = true;
    public string Basis { get; set; } = "coverage_overlap";
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
}

public sealed class MultiSourceCitationCoverageDto
{
    public int TotalCitationChecks { get; set; }
    public int SupportedCount { get; set; }
    public int UnsupportedCount { get; set; }
    public int MissingCount { get; set; }
    public int StaleCount { get; set; }
    public int NeedsReviewCount { get; set; }
    public decimal CoverageRatio { get; set; }
    public string CoverageStatus { get; set; } = "not_checked";
}

public sealed class CitationReviewItemDto
{
    public Guid Id { get; set; }
    public string CitationId { get; set; } = string.Empty;
    public Guid? SourceId { get; set; }
    public string SourceTitle { get; set; } = string.Empty;
    public Guid? SourceChunkId { get; set; }
    public int? PageNumber { get; set; }
    public int? ChunkIndex { get; set; }
    public string SourceReadiness { get; set; } = "evidence_insufficient";
    public string EvidenceStatus { get; set; } = "evidence_insufficient";
    public string CitationStatus { get; set; } = "not_checked";
    public decimal? Confidence { get; set; }
    public string UserSafeWarning { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class CitationReviewResultDto
{
    public Guid? TopicId { get; set; }
    public Guid? SourceId { get; set; }
    public string ReviewStatus { get; set; } = "ready";
    public MultiSourceCitationCoverageDto Coverage { get; set; } = new();
    public IReadOnlyList<CitationReviewItemDto> Items { get; set; } = Array.Empty<CitationReviewItemDto>();
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public record SourceMetadataDto(
    IReadOnlyList<CitationDto> Citations,
    string GroundingMode,
    string? FallbackReason,
    double? SourceConfidence,
    Guid? RetrievalRunId = null,
    string? SourceQualityStatus = null,
    int UnsupportedCitationCount = 0,
    int CitationMissingCount = 0,
    EvidenceQualityDto? EvidenceQuality = null);

public sealed class SourceRetrievalRunDto
{
    public Guid Id { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public Guid? SourceId { get; set; }
    public string Query { get; set; } = string.Empty;
    public string RetrievalScope { get; set; } = "topic";
    public int RequestedTopK { get; set; }
    public int RetrievedCount { get; set; }
    public bool IsEmpty { get; set; }
    public decimal MaxScore { get; set; }
    public decimal AverageScore { get; set; }
    public string QualityStatus { get; set; } = "unknown";
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<SourceRetrievalItemDto> Items { get; set; } = Array.Empty<SourceRetrievalItemDto>();
}

public sealed class SourceRetrievalItemDto
{
    public Guid Id { get; set; }
    public Guid SourceRetrievalRunId { get; set; }
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
}

public sealed class SourceCitationCheckDto
{
    public Guid Id { get; set; }
    public Guid? SourceRetrievalRunId { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? SourceChunkId { get; set; }
    public string CitationId { get; set; } = string.Empty;
    public string SourceType { get; set; } = "document";
    public int? PageNumber { get; set; }
    public int? ChunkIndex { get; set; }
    public string CheckStatus { get; set; } = "unknown";
    public decimal Confidence { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class SourceQualityReportDto
{
    public Guid Id { get; set; }
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
    public EvidenceQualityDto? EvidenceQuality { get; set; }
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public IReadOnlyList<SourceRetrievalRunDto> RecentRetrievalRuns { get; set; } = Array.Empty<SourceRetrievalRunDto>();
    public IReadOnlyList<SourceCitationCheckDto> RecentCitationChecks { get; set; } = Array.Empty<SourceCitationCheckDto>();
}

public record GlossaryItemDto(
    string Term,
    string SimpleExplanation,
    string? SourceHint = null,
    string? GroundingStatus = null,
    string? CitationCoverageStatus = null,
    string? SourceQualityStatus = null,
    Guid? RetrievalRunId = null);

public record TimelineItemDto(
    string Year,
    string Event,
    string? SourceHint = null,
    string? GroundingStatus = null,
    string? CitationCoverageStatus = null,
    string? SourceQualityStatus = null,
    Guid? RetrievalRunId = null);

public record MindMapNodeDto(string Id, string Label, string? ParentId, int Depth);

public record MindMapDto(
    string Mermaid,
    IReadOnlyList<MindMapNodeDto> Nodes,
    string? GroundingStatus = null,
    string? CitationCoverageStatus = null,
    string? SourceQualityStatus = null,
    Guid? RetrievalRunId = null,
    IReadOnlyList<CitationDto>? Citations = null);

public record StudyCardDto(
    string Front,
    string Back,
    string? SourceHint,
    string? GroundingStatus = null,
    string? CitationCoverageStatus = null,
    string? SourceQualityStatus = null,
    Guid? RetrievalRunId = null);

public record AudioOverviewJobDto(
    Guid Id,
    string Status,
    string Script,
    IReadOnlyList<string> Speakers,
    string? ErrorMessage,
    DateTime CreatedAt,
    string? ContentType = null,
    string? FileName = null,
    string? DownloadUrl = null,
    string? FallbackReason = null,
    DateTime? UpdatedAt = null,
    string Surface = "wiki",
    string ContextType = "wiki_page",
    Guid? WikiPageId = null,
    Guid? SourceId = null,
    string AudioMode = "brief",
    string DialogueFormat = "hoca_asistan_konuk",
    string TtsQuality = "standard",
    string Transcript = "",
    string CaptionTrack = "",
    IReadOnlyList<AudioCaptionCueDto>? Captions = null,
    bool ClassroomReady = true,
    bool CrossSurfaceSync = false,
    DateTime? AudioExpiresAt = null,
    DateTime? AudioPurgedAt = null,
    long AudioByteLength = 0,
    IReadOnlyList<string>? RetentionNotes = null);

public record AudioCaptionCueDto(
    int CueId,
    string Speaker,
    string Text,
    string Start,
    string End);
