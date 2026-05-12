namespace Orka.Core.Interfaces;

public interface ITopicProgressPropagator
{
    Task PropagateLessonCompletionAsync(
        Guid userId,
        Guid lessonTopicId,
        int? scorePercent,
        bool isMastered,
        CancellationToken ct = default);

    Task HandleCompletionAnalysisProgressionAsync(
        Guid userId,
        Guid sessionId,
        Guid topicId,
        CancellationToken ct = default);
}
