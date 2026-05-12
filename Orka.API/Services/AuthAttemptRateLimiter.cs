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

    public bool TryConsume(string key, int permitLimit, TimeSpan window)
    {
        if (permitLimit <= 0)
            return false;

        var now = DateTimeOffset.UtcNow;
        var entry = _attempts.GetOrAdd(key, _ => new AttemptWindow(now));

        lock (entry.Gate)
        {
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

    private sealed class AttemptWindow(DateTimeOffset windowStart)
    {
        public object Gate { get; } = new();
        public DateTimeOffset WindowStart { get; set; } = windowStart;
        public int Count { get; set; }
    }
}
