using Orka.Core.DTOs;
using Orka.Core.Entities;

namespace Orka.Core.Interfaces;

public interface ILearningSignalService
{
    Task RecordQuizAnsweredAsync(QuizAttempt attempt, CancellationToken ct = default);

    Task RecordSignalAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string signalType,
        string? skillTag = null,
        string? topicPath = null,
        int? score = null,
        bool? isPositive = null,
        string? payloadJson = null,
        CancellationToken ct = default);

    Task<LearningTopicSummaryDto> GetTopicSummaryAsync(Guid userId, Guid topicId, CancellationToken ct = default);

    Task<IReadOnlyList<StudyRecommendationDto>> GetRecommendationsAsync(Guid userId, Guid topicId, CancellationToken ct = default);
}
