namespace Orka.Core.Interfaces;

public sealed record BackgroundTaskItem(
    string JobType,
    Guid? UserId,
    string? CorrelationId,
    Func<CancellationToken, Task> Work,
    int MaxAttempts = 1,
    TimeSpan? Timeout = null);

public interface IBackgroundTaskQueue
{
    ValueTask QueueAsync(BackgroundTaskItem item, CancellationToken ct = default);
}
