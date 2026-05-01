using Orka.Core.DTOs;

namespace Orka.Core.Interfaces;

public interface IEducatorCoreService
{
    Task<TeacherContext> BuildTeacherContextAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string question,
        string notebookContext,
        string wikiContext,
        string learningSignalContext,
        string? rawYouTubeContext,
        CancellationToken ct = default);

    Task<TeachingReference?> NormalizeTeachingReferenceAsync(
        Guid topicId,
        string? rawYouTubeContext,
        CancellationToken ct = default);

    Task RecordAnswerQualitySignalsAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string answer,
        TeacherContext context,
        CancellationToken ct = default);
}
