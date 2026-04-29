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

    Task<string> BuildTopicGroundingContextAsync(
        Guid userId,
        Guid? topicId,
        string question,
        CancellationToken ct = default);
}

public interface IAudioOverviewService
{
    Task<AudioOverviewJobDto> CreateOverviewAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        CancellationToken ct = default);

    Task<(byte[] Bytes, string ContentType, string FileName)?> GetAudioAsync(
        Guid userId,
        Guid jobId,
        CancellationToken ct = default);
}

public interface IClassroomService
{
    Task<ClassroomSessionDto> StartSessionAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        Guid? audioOverviewJobId,
        string transcript,
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
