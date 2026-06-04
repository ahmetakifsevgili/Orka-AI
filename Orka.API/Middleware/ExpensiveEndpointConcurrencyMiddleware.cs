using System.Collections.Concurrent;
using System.Security.Claims;

namespace Orka.API.Middleware;

public sealed class ExpensiveEndpointConcurrencyMiddleware
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;

    public ExpensiveEndpointConcurrencyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var policy = ResolvePolicy(context.Request.Path);
        if (policy is null)
        {
            await _next(context);
            return;
        }

        var permitLimit = Math.Clamp(
            _configuration.GetValue<int?>($"RateLimits:{policy.Value.ConfigurationArea}:ConcurrencyLimit") ?? policy.Value.DefaultPermitLimit,
            1,
            64);
        var partition = ResolvePartition(context);
        var gate = Gates.GetOrAdd(
            $"{policy.Value.Name}:{partition}:limit:{permitLimit}",
            _ => new SemaphoreSlim(permitLimit, permitLimit));

        if (!await gate.WaitAsync(0, context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = "1";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "concurrency_limited",
                policy = policy.Value.Name
            }, context.RequestAborted);
            return;
        }

        try
        {
            await _next(context);
        }
        finally
        {
            gate.Release();
        }
    }

    private static ExpensiveEndpointPolicy? ResolvePolicy(PathString path)
    {
        var value = path.Value ?? string.Empty;
        if (value.StartsWith("/api/audio/overview", StringComparison.OrdinalIgnoreCase))
        {
            return new ExpensiveEndpointPolicy("audio", "Audio", 2);
        }

        if (value.StartsWith("/api/question-drafts", StringComparison.OrdinalIgnoreCase))
        {
            return new ExpensiveEndpointPolicy("question-draft", "QuestionDraft", 2);
        }

        if (value.StartsWith("/api/question-imports", StringComparison.OrdinalIgnoreCase))
        {
            return new ExpensiveEndpointPolicy("question-import", "QuestionImport", 2);
        }

        return null;
    }

    private static string ResolvePartition(HttpContext context)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? context.User.FindFirstValue("sub");
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip";
        return $"{userId ?? "anonymous"}:{ip}";
    }

    private readonly record struct ExpensiveEndpointPolicy(string Name, string ConfigurationArea, int DefaultPermitLimit);
}
