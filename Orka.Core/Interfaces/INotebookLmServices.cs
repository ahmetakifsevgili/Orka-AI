using Orka.Core.DTOs;

namespace Orka.Core.Interfaces;

public interface ILearningSourceService
{
    Task<LearningSourceSummaryDto> UploadAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string fileName,
        string contentType,
        Stream content,
        CancellationToken ct = default);

    Task<IReadOnlyList<LearningSourceSummaryDto>> GetTopicSourcesAsync(
        Guid userId,
        Guid topicId,
        CancellationToken ct = default);

    Task<SourceNotebookDto?> GetTopicSourceNotebookAsync(
        Guid userId,
        Guid topicId,
        CancellationToken ct = default);

    Task<SourceNotebookDto?> GetSourceNotebookAsync(
        Guid userId,
        Guid sourceId,
        CancellationToken ct = default);

    Task<SourcePageDto?> GetPageAsync(
        Guid userId,
        Guid sourceId,
        int pageNumber,
        CancellationToken ct = default);

    Task<SourceAskResultDto> AskAsync(
        Guid userId,
        Guid sourceId,
        string question,
        CancellationToken ct = default);

    Task<IReadOnlyList<TopicSourceEvidenceDto>> RetrieveTopicEvidenceAsync(
        Guid userId,
        Guid topicId,
        string question,
        int take = 8,
        Guid? sourceId = null,
        CancellationToken ct = default);

    Task<SourceQualityReportDto> GetTopicQualityAsync(
        Guid userId,
        Guid topicId,
        CancellationToken ct = default);

    Task<LearningSourceSummaryDto?> UpdateSourceAsync(
        Guid userId,
        Guid sourceId,
        string? title,
        CancellationToken ct = default);

    Task<bool> DeleteSourceAsync(
        Guid userId,
        Guid sourceId,
        CancellationToken ct = default);

    Task<string> BuildTopicGroundingContextAsync(
        Guid userId,
        Guid? topicId,
        string question,
        CancellationToken ct = default);
}

public interface ISourceConceptLinkingService
{
    Task<SourceConceptLinkSummaryDto?> GetSourceConceptLinksAsync(
        Guid userId,
        Guid sourceId,
        CancellationToken ct = default);

    Task<SourceConceptLinkSummaryDto?> SyncSourceConceptLinksAsync(
        Guid userId,
        Guid sourceId,
        CancellationToken ct = default);

    Task<SourceConceptGraphDto?> GetTopicSourceConceptGraphAsync(
        Guid userId,
        Guid topicId,
        CancellationToken ct = default);

    Task<SourceConceptLinkSummaryDto?> GetWikiPageSourceLinksAsync(
        Guid userId,
        Guid wikiPageId,
        CancellationToken ct = default);
}

public interface ISourceQuestionService
{
    Task<SourceQuestionResponseDto?> AskSourceAsync(
        Guid userId,
        Guid sourceId,
        SourceQuestionRequestDto request,
        CancellationToken ct = default);

    Task<SourceQuestionResponseDto?> AskTopicSourcesAsync(
        Guid userId,
        Guid topicId,
        SourceQuestionRequestDto request,
        CancellationToken ct = default);

    Task<SourceQuestionResponseDto?> AskAsync(
        Guid userId,
        SourceQuestionRequestDto request,
        CancellationToken ct = default);
}

public interface ISourceQuestionThreadService
{
    Task<SourceStudySummaryDto> GetStudySummaryAsync(
        Guid userId,
        Guid? topicId = null,
        Guid? sourceId = null,
        Guid? wikiPageId = null,
        CancellationToken ct = default);

    Task<SourceQuestionThreadListDto> ListThreadsAsync(
        Guid userId,
        Guid? topicId = null,
        Guid? sourceId = null,
        Guid? wikiPageId = null,
        CancellationToken ct = default);

    Task<SourceQuestionThreadDto?> GetThreadAsync(
        Guid userId,
        Guid threadId,
        CancellationToken ct = default);

    Task<SourceQuestionThreadDto?> CreateThreadAsync(
        Guid userId,
        SourceQuestionThreadRequestDto request,
        CancellationToken ct = default);

    Task<SourceQuestionThreadDto?> AskFollowUpAsync(
        Guid userId,
        Guid threadId,
        SourceQuestionFollowUpRequestDto request,
        CancellationToken ct = default);

    Task<SourceQuestionThreadDto?> UpdateReviewAsync(
        Guid userId,
        Guid threadId,
        SourceQuestionReviewStateDto request,
        CancellationToken ct = default);

    Task<WikiBlockDto?> WriteWikiTraceAsync(
        Guid userId,
        Guid threadId,
        CancellationToken ct = default);
}

public interface ISourceCompareService
{
    Task<MultiSourceCompareResultDto?> CompareAsync(
        Guid userId,
        MultiSourceCompareRequestDto request,
        CancellationToken ct = default);

    Task<MultiSourceCompareResultDto?> CompareTopicAsync(
        Guid userId,
        Guid topicId,
        MultiSourceCompareRequestDto request,
        CancellationToken ct = default);

    Task<CitationReviewResultDto?> GetSourceCitationReviewAsync(
        Guid userId,
        Guid sourceId,
        CancellationToken ct = default);

    Task<CitationReviewResultDto?> GetTopicCitationReviewAsync(
        Guid userId,
        Guid topicId,
        CancellationToken ct = default);
}

public interface ISourceWikiIntelligenceService
{
    Task<SourceWikiIntelligenceProfileDto?> BuildProfileAsync(
        Guid userId,
        Guid? topicId = null,
        Guid? sourceId = null,
        Guid? wikiPageId = null,
        CancellationToken ct = default);
}

public interface ISourceEvidenceLifecycleService
{
    Task<SourceEvidenceBundleDto> BuildSourceEvidenceBundleAsync(
        Guid userId,
        Guid topicId,
        Guid? sessionId = null,
        string? question = null,
        CancellationToken ct = default);

    Task<SourceEvidenceBundleDto?> GetLatestSourceEvidenceBundleAsync(
        Guid userId,
        Guid topicId,
        Guid? sessionId = null,
        CancellationToken ct = default);

    Task<SourceLifecycleSummaryDto> GetSourceLifecycleSummaryAsync(
        Guid userId,
        Guid topicId,
        CancellationToken ct = default);

    Task<bool> MarkSourceStaleAsync(
        Guid userId,
        Guid sourceId,
        string reason,
        CancellationToken ct = default);

    Task<bool> InvalidateEvidenceForSourceAsync(
        Guid userId,
        Guid sourceId,
        string reason,
        CancellationToken ct = default);

    Task<SourceCitationSetValidationDto> ValidateCitationSetAsync(
        Guid userId,
        ValidateSourceCitationSetRequestDto request,
        CancellationToken ct = default);

    Task<WikiKnowledgeNotebookDto> BuildWikiKnowledgeNotebookAsync(
        Guid userId,
        Guid topicId,
        CancellationToken ct = default);

    Task<WikiKnowledgeNotebookDto?> GetLatestWikiKnowledgeNotebookAsync(
        Guid userId,
        Guid topicId,
        CancellationToken ct = default);
}

public interface IAudioOverviewService
{
    Task<AudioOverviewJobDto> CreateOverviewAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string? surface = null,
        Guid? wikiPageId = null,
        Guid? sourceId = null,
        string? audioMode = null,
        string? ttsQuality = null,
        CancellationToken ct = default);

    Task<AudioOverviewJobDto?> GetOverviewAsync(
        Guid userId,
        Guid jobId,
        CancellationToken ct = default);

    Task<(byte[] Bytes, string ContentType, string FileName)?> GetAudioAsync(
        Guid userId,
        Guid jobId,
        CancellationToken ct = default);

    Task ProcessOverviewJobAsync(Guid jobId, CancellationToken ct);
}

public interface IClassroomService
{
    Task<ClassroomSessionDto> StartSessionAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        Guid? audioOverviewJobId,
        string transcript,
        string? surface = null,
        Guid? wikiPageId = null,
        Guid? sourceId = null,
        string? audioMode = null,
        CancellationToken ct = default);

    Task<ClassroomAskResultDto> AskAsync(
        Guid userId,
        Guid classroomSessionId,
        string question,
        string? activeSegment,
        CancellationToken ct = default);

    Task<(byte[] Bytes, string ContentType)?> GetInteractionAudioAsync(
        Guid userId,
        Guid interactionId,
        CancellationToken ct = default);
}
