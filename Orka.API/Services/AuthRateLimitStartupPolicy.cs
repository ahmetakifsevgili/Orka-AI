using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Orka.API.Services;

public sealed record AuthRateLimitStartupOptions(string Backend, bool AllowInMemoryFallback);

public static class AuthRateLimitStartupPolicy
{
    public const string BackendInMemory = "InMemory";
    public const string BackendRedis = "Redis";
    public const string BackendAuto = "Auto";

    public static AuthRateLimitStartupOptions Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredBackend = configuration["RateLimits:Auth:Backend"];
        var backend = string.IsNullOrWhiteSpace(configuredBackend)
            ? BackendAuto
            : configuredBackend.Trim();

        if (backend.Equals(BackendAuto, StringComparison.OrdinalIgnoreCase))
            backend = environment.IsDevelopment() ? BackendInMemory : BackendRedis;

        if (!backend.Equals(BackendInMemory, StringComparison.OrdinalIgnoreCase) &&
            !backend.Equals(BackendRedis, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unsupported auth rate limiter backend.");
        }

        var fallbackValue = configuration["RateLimits:Auth:AllowInMemoryFallback"];
        var allowFallback = bool.TryParse(fallbackValue, out var parsed)
            ? parsed
            : environment.IsDevelopment();

        if (backend.Equals(BackendInMemory, StringComparison.OrdinalIgnoreCase) &&
            !environment.IsDevelopment() &&
            !allowFallback)
        {
            throw new InvalidOperationException(
                "In-memory auth rate limiter is disabled outside Development unless explicitly allowed.");
        }

        return new AuthRateLimitStartupOptions(backend, allowFallback);
    }
}
