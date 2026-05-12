using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Orka.API.Services;

public interface IRedisAuthAttemptStore
{
    Task<long> IncrementAsync(string key, TimeSpan window, CancellationToken cancellationToken = default);
}

public sealed class RedisAuthAttemptStore : IRedisAuthAttemptStore
{
    private const string IncrementScript = """
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        return current
        """;

    private readonly IConnectionMultiplexer _redis;

    public RedisAuthAttemptStore(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<long> IncrementAsync(
        string key,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        if (!_redis.IsConnected)
            throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis is not connected.");

        var db = _redis.GetDatabase();
        var ttlMs = Math.Max(1, (long)window.TotalMilliseconds);
        var value = await db.ScriptEvaluateAsync(
            IncrementScript,
            new RedisKey[] { key },
            new RedisValue[] { ttlMs });

        return (long)value;
    }
}

public sealed class RedisAuthAttemptLimiter : IAuthAttemptLimiter
{
    private readonly IRedisAuthAttemptStore _store;
    private readonly AuthAttemptRateLimiter _fallback;
    private readonly ILogger<RedisAuthAttemptLimiter> _logger;
    private readonly IHostEnvironment _environment;
    private readonly bool _allowInMemoryFallback;

    public RedisAuthAttemptLimiter(
        IRedisAuthAttemptStore store,
        AuthAttemptRateLimiter fallback,
        ILogger<RedisAuthAttemptLimiter> logger,
        IHostEnvironment environment,
        bool allowInMemoryFallback)
    {
        _store = store;
        _fallback = fallback;
        _logger = logger;
        _environment = environment;
        _allowInMemoryFallback = allowInMemoryFallback;
    }

    public async Task<AuthAttemptLimitResult> TryConsumeAsync(
        string key,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        if (permitLimit <= 0)
            return new AuthAttemptLimitResult(false);

        try
        {
            var count = await _store.IncrementAsync(key, window, cancellationToken);
            return new AuthAttemptLimitResult(count <= permitLimit);
        }
        catch (Exception ex)
        {
            if (_allowInMemoryFallback)
            {
                LogLimiterFallback(ex);
                return await _fallback.TryConsumeAsync(key, permitLimit, window, cancellationToken);
            }

            LogLimiterFailClosed(ex);
            return new AuthAttemptLimitResult(false, LimiterUnavailable: true);
        }
    }

    private void LogLimiterFallback(Exception ex)
    {
        if (_environment.IsDevelopment())
        {
            _logger.LogWarning(ex, "[AuthRateLimit] Redis limiter unavailable; using in-memory fallback.");
            return;
        }

        _logger.LogWarning("[AuthRateLimit] Redis limiter unavailable; using configured in-memory fallback.");
    }

    private void LogLimiterFailClosed(Exception ex)
    {
        if (_environment.IsDevelopment())
        {
            _logger.LogWarning(ex, "[AuthRateLimit] Redis limiter unavailable; denying auth attempt.");
            return;
        }

        _logger.LogWarning("[AuthRateLimit] Redis limiter unavailable; denying auth attempt.");
    }
}
