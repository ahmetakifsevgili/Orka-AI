using System.Collections.Concurrent;

namespace Orka.API.Services;

public sealed record AuthAttemptLimitResult(bool Allowed, bool LimiterUnavailable = false);

public interface IAuthAttemptLimiter
{
    Task<AuthAttemptLimitResult> TryConsumeAsync(
        string key,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken = default);
}

public sealed class AuthAttemptRateLimiter : IAuthAttemptLimiter
{
    private readonly ConcurrentDictionary<string, AttemptWindow> _attempts = new(StringComparer.Ordinal);

    public AuthAttemptRateLimiter()
    {
        _ = CleanupLoopAsync();
    }

    private async Task CleanupLoopAsync()
    {
        using var timer = new System.Threading.PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var kvp in _attempts)
                {
                    if (now - kvp.Value.WindowStart >= kvp.Value.WindowDuration)
                    {
                        _attempts.TryRemove(kvp.Key, out _);
                    }
                }
            }
            catch
            {
                // Shield background task from crashes
            }
        }
    }

    public bool TryConsume(string key, int permitLimit, TimeSpan window)
    {
        if (permitLimit <= 0)
            return false;

        var now = DateTimeOffset.UtcNow;
        var entry = _attempts.GetOrAdd(key, _ => new AttemptWindow(now, window));

        lock (entry.Gate)
        {
            entry.WindowDuration = window;

            if (now - entry.WindowStart >= window)
            {
                entry.WindowStart = now;
                entry.Count = 0;
            }

            if (entry.Count >= permitLimit)
                return false;

            entry.Count++;
            return true;
        }
    }

    public Task<AuthAttemptLimitResult> TryConsumeAsync(
        string key,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new AuthAttemptLimitResult(TryConsume(key, permitLimit, window)));

    private sealed class AttemptWindow(DateTimeOffset windowStart, TimeSpan windowDuration)
    {
        public object Gate { get; } = new();
        public DateTimeOffset WindowStart { get; set; } = windowStart;
        public TimeSpan WindowDuration { get; set; } = windowDuration;
        public int Count { get; set; }
    }
}
