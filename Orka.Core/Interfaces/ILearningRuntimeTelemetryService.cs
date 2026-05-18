using Orka.Core.DTOs;

namespace Orka.Core.Interfaces;

public interface ILearningRuntimeTelemetryService
{
    Task<LearningRuntimeTraceDto> RecordEventAsync(Guid userId, LearningRuntimeEventRequestDto request, CancellationToken ct = default);
    Task<LearningRuntimeTraceDto?> GetTraceAsync(Guid userId, Guid traceId, CancellationToken ct = default);
    Task<IReadOnlyList<LearningRuntimeTraceDto>> GetRecentTracesAsync(Guid userId, Guid? topicId = null, Guid? sessionId = null, int take = 25, CancellationToken ct = default);
    Task<LearningRuntimeCorrelationDto> GetCorrelationSummaryAsync(Guid userId, string correlationId, CancellationToken ct = default);
    Task<LearningRuntimeHealthDto> GetLearningRuntimeHealthAsync(Guid userId, Guid? topicId = null, Guid? sessionId = null, CancellationToken ct = default);
    Task<LearningRuntimeFlowSummaryDto> GetTopicSummaryAsync(Guid userId, Guid topicId, Guid? sessionId = null, CancellationToken ct = default);
    LearningRuntimePrivacyCheckDto ValidateTelemetryPrivacy(LearningRuntimePrivacyCheckRequestDto request);
}
