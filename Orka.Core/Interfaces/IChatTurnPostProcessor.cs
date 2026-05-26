using Orka.Core.Enums;

namespace Orka.Core.Interfaces;

public sealed record ChatTurnPostProcessRequest(
    Guid UserId,
    Guid SessionId,
    Guid? TopicId,
    Guid AssistantMessageId,
    string UserContent,
    string AssistantContent,
    string AgentRole,
    string? CorrelationId,
    SessionState EntryState,
    bool IsStream);

public interface IChatTurnPostProcessor
{
    ValueTask ScheduleAsync(ChatTurnPostProcessRequest request, CancellationToken ct = default);
    Task ProcessSynchronouslyAsync(ChatTurnPostProcessRequest request, CancellationToken ct = default);
}
