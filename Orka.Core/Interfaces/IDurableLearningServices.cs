using Orka.Core.DTOs;
using Orka.Core.DTOs.Chat;
using Orka.Core.Entities;

namespace Orka.Core.Interfaces;

public interface IReviewSrsService
{
    string BuildReviewKey(Guid? topicId, string? conceptTag, string? skillTag, string? learningObjective, string? topicPath);

    Task<ReviewItem> EnsureReviewItemAsync(
        Guid userId,
        Guid? topicId,
        string? conceptTag,
        string? skillTag,
        string? learningObjective,
        string? mistakeCategory,
        string? topicPath,
        string? sourceType,
        Guid? sourceId,
        Guid? quizAttemptId,
        Guid? learningSignalId,
        Guid? flashcardId,
        Guid? remediationPlanId,
        CancellationToken ct = default);

    Task<IReadOnlyList<DurableReviewItemDto>> GetDueAsync(Guid userId, Guid? topicId, CancellationToken ct = default);

    Task<DurableReviewItemDto?> CompleteAsync(
        Guid userId,
        Guid reviewItemId,
        int quality,
        string? responseMode,
        string? notes,
        CancellationToken ct = default);
}

public interface IFlashcardService
{
    Task<FlashcardDto> CreateAsync(Guid userId, CreateFlashcardRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<FlashcardDto>> ListAsync(Guid userId, Guid? topicId, CancellationToken ct = default);
    Task<FlashcardDto?> ReviewAsync(Guid userId, Guid flashcardId, int quality, string? notes, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid userId, Guid flashcardId, CancellationToken ct = default);
}

public interface IDailyChallengeService
{
    Task<DailyChallengeDto> GetTodayAsync(Guid userId, Guid? topicId, CancellationToken ct = default);
    Task<DailyChallengeSubmissionDto?> SubmitAsync(Guid userId, Guid challengeId, string answer, int quality, CancellationToken ct = default);
}

public interface IXpEventService
{
    Task<XpAwardResult> AwardAsync(
        Guid userId,
        string eventKey,
        string eventType,
        int xpDelta,
        string? relatedEntityType,
        Guid? relatedEntityId,
        CancellationToken ct = default);
}

public interface INotificationService
{
    Task<NotificationDto> CreateAsync(Guid userId, CreateNotificationRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationDto>> ListAsync(Guid userId, bool includeRead = false, CancellationToken ct = default);
    Task<NotificationDto?> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default);
    Task<int> MarkAllReadAsync(Guid userId, CancellationToken ct = default);
}

public interface IChatMetadataService
{
    ChatResponseMetadata Build(string content, string? fallbackReason = null, IEnumerable<UsedToolDto>? usedTools = null);
}
