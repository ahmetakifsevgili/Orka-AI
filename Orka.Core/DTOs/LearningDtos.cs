namespace Orka.Core.DTOs;

public record CacheMetaDto(
    bool Hit,
    string Source,
    DateTime GeneratedAt,
    DateTime? CachedAt = null,
    long? Version = null);

public record WeakSkillDto(
    string SkillTag,
    string TopicPath,
    int WrongCount,
    int TotalCount,
    double Accuracy,
    DateTime LastSeenAt);

public record LearningTopicSummaryDto(
    Guid TopicId,
    int TotalAttempts,
    int CorrectAttempts,
    double Accuracy,
    IReadOnlyList<WeakSkillDto> WeakSkills,
    IReadOnlyList<string> RecentSignals,
    CacheMetaDto? Cache = null);

public record StudyRecommendationDto(
    Guid Id,
    string RecommendationType,
    string Title,
    string Reason,
    string? SkillTag,
    string? ActionPrompt,
    bool IsDone,
    DateTime CreatedAt);

public record LearningSignalDto(
    Guid Id,
    string SignalType,
    string? SkillTag,
    string? TopicPath,
    int? Score,
    bool? IsPositive,
    DateTime CreatedAt);

public record RecordLearningSignalRequest(
    Guid? TopicId,
    Guid? SessionId,
    string SignalType,
    string? SkillTag = null,
    string? TopicPath = null,
    int? Score = null,
    bool? IsPositive = null,
    string? PayloadJson = null);

public record ClassroomSessionDto(
    Guid Id,
    Guid? TopicId,
    Guid? SessionId,
    string Transcript,
    string LastSegment,
    string Status,
    DateTime CreatedAt);

public record ClassroomAskResultDto(
    Guid ClassroomSessionId,
    Guid InteractionId,
    string Answer,
    IReadOnlyList<string> Speakers);
