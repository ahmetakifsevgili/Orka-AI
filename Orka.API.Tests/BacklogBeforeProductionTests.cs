using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orka.API.Middleware;
using Orka.API.Services;
using Orka.Infrastructure.Security;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.API.Tests;

public sealed class BacklogBeforeProductionTests
{
    [Fact]
    public void AuthRateLimitPolicy_UsesRedisByDefaultOutsideDevelopment()
    {
        var policy = AuthRateLimitStartupPolicy.Resolve(
            new ConfigurationBuilder().Build(),
            new TestEnvironment("Production"));

        Assert.Equal(AuthRateLimitStartupPolicy.BackendRedis, policy.Backend);
        Assert.False(policy.AllowInMemoryFallback);
    }

    [Fact]
    public void AuthRateLimitPolicy_BlocksInMemoryOutsideDevelopmentUnlessExplicitlyAllowed()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimits:Auth:Backend"] = "InMemory",
                ["RateLimits:Auth:AllowInMemoryFallback"] = "false"
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            AuthRateLimitStartupPolicy.Resolve(configuration, new TestEnvironment("Production")));

        Assert.DoesNotContain("Redis", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionStrings", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RedisAuthLimiter_DeniesAfterConfiguredLimit()
    {
        var store = new FakeRedisAuthAttemptStore();
        var limiter = new RedisAuthAttemptLimiter(
            store,
            new AuthAttemptRateLimiter(),
            NullLogger<RedisAuthAttemptLimiter>.Instance,
            new TestEnvironment("Production"),
            allowInMemoryFallback: false);

        var first = await limiter.TryConsumeAsync("auth:login:hashed-client:hashed-email", 2, TimeSpan.FromMinutes(5));
        var second = await limiter.TryConsumeAsync("auth:login:hashed-client:hashed-email", 2, TimeSpan.FromMinutes(5));
        var third = await limiter.TryConsumeAsync("auth:login:hashed-client:hashed-email", 2, TimeSpan.FromMinutes(5));

        Assert.True(first.Allowed);
        Assert.True(second.Allowed);
        Assert.False(third.Allowed);
        Assert.False(third.LimiterUnavailable);
        Assert.Equal("auth:login:hashed-client:hashed-email", store.Keys.Single());
    }

    [Fact]
    public async Task RedisAuthLimiter_FailsClosedWhenRedisUnavailableAndFallbackDisabled()
    {
        var limiter = new RedisAuthAttemptLimiter(
            new FailingRedisAuthAttemptStore(),
            new AuthAttemptRateLimiter(),
            NullLogger<RedisAuthAttemptLimiter>.Instance,
            new TestEnvironment("Production"),
            allowInMemoryFallback: false);

        var result = await limiter.TryConsumeAsync("auth:login:hashed-client:hashed-email", 5, TimeSpan.FromMinutes(5));

        Assert.False(result.Allowed);
        Assert.True(result.LimiterUnavailable);
    }

    [Fact]
    public async Task RedisAuthLimiter_UsesInMemoryFallbackWhenExplicitlyAllowed()
    {
        var limiter = new RedisAuthAttemptLimiter(
            new FailingRedisAuthAttemptStore(),
            new AuthAttemptRateLimiter(),
            NullLogger<RedisAuthAttemptLimiter>.Instance,
            new TestEnvironment("Development"),
            allowInMemoryFallback: true);

        var first = await limiter.TryConsumeAsync("auth:register:hashed-client", 1, TimeSpan.FromMinutes(5));
        var second = await limiter.TryConsumeAsync("auth:register:hashed-client", 1, TimeSpan.FromMinutes(5));

        Assert.True(first.Allowed);
        Assert.False(second.Allowed);
        Assert.False(second.LimiterUnavailable);
    }

    [Fact]
    public async Task AuthEndpoint_ReturnsServiceUnavailableWhenLimiterFailsClosed()
    {
        using var factory = new ApiSmokeFactory(
            "Development",
            configureServices: services =>
            {
                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IAuthAttemptLimiter)).ToList())
                    services.Remove(descriptor);
                services.AddSingleton<IAuthAttemptLimiter>(new AlwaysUnavailableAuthAttemptLimiter());
            });
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Rate",
            lastName = "Limit",
            email = $"limiter-unavailable-{Guid.NewGuid():N}@orka.local",
            password = "RedisPass123!"
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Redis", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionStrings", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthRateLimitKeys_DoNotContainRawEmailOrIp()
    {
        var limiter = new CapturingAuthAttemptLimiter();
        using var factory = new ApiSmokeFactory(
            "Development",
            configureServices: services =>
            {
                foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IAuthAttemptLimiter)).ToList())
                    services.Remove(descriptor);
                services.AddSingleton<IAuthAttemptLimiter>(limiter);
            });
        var client = factory.CreateClient();
        const string rawIp = "203.0.113.44";
        var email = $"key-capture-{Guid.NewGuid():N}@orka.local";

        using var register = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register");
        register.Headers.Add("X-Forwarded-For", rawIp);
        register.Content = JsonContent.Create(new
        {
            firstName = "Key",
            lastName = "Capture",
            email,
            password = "KeyPass123!"
        });
        var registerResponse = await client.SendAsync(register);
        registerResponse.EnsureSuccessStatusCode();

        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login");
        login.Headers.Add("X-Forwarded-For", rawIp);
        login.Content = JsonContent.Create(new { email, password = "WrongPass123!" });
        _ = await client.SendAsync(login);

        Assert.NotEmpty(limiter.Keys);
        Assert.All(limiter.Keys, key =>
        {
            Assert.DoesNotContain(email, key, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(rawIp, key, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void SensitiveDataRedactor_MasksEmailTokensSecretsAndFileNames()
    {
        var redacted = SensitiveDataRedactor.Redact(
            "user test.user@example.com token=eyJaaaaaaaaaaaaaaaa.eyJbbbbbbbbbbbbbbbb.cccccccccccccccccccc JWT:Secret=super-secret-value");
        var file = SensitiveDataRedactor.MaskFileName("student-private-notes.pdf");

        Assert.DoesNotContain("test.user@example.com", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("eyJaaaaaaaaaaaaaaaa", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-value", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("student-private-notes", file, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".pdf", file);
    }

    [Fact]
    public async Task ExceptionMiddleware_ProductionProviderConfigResponseDoesNotLeakConfigPath()
    {
        var logger = new CapturingLogger<ExceptionMiddleware>();
        var middleware = new ExceptionMiddleware(
            _ => throw new ProviderConfigurationException("GitHubModels", "AI:GitHubModels:Token"),
            logger,
            new TestEnvironment("Production"));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        var body = await ReadResponseBodyAsync(context);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.DoesNotContain("AI:GitHubModels:Token", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Provider config missing", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AI:GitHubModels:Token", logger.MessagesText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExceptionMiddleware_ProductionUnhandledResponseAndLogAreRedacted()
    {
        var logger = new CapturingLogger<ExceptionMiddleware>();
        var middleware = new ExceptionMiddleware(
            _ => throw new InvalidOperationException("boom test.user@example.com JWT:Secret=super-secret-value"),
            logger,
            new TestEnvironment("Production"));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        var body = await ReadResponseBodyAsync(context);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.DoesNotContain("test.user@example.com", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("super-secret-value", body, StringComparison.Ordinal);
        Assert.DoesNotContain("test.user@example.com", logger.MessagesText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("super-secret-value", logger.MessagesText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExceptionMiddleware_DevelopmentProviderConfigResponseKeepsDebugDetail()
    {
        var middleware = new ExceptionMiddleware(
            _ => throw new ProviderConfigurationException("GitHubModels", "AI:GitHubModels:Token"),
            NullLogger<ExceptionMiddleware>.Instance,
            new TestEnvironment("Development"));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        var body = await ReadResponseBodyAsync(context);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Contains("GitHubModels", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AI:GitHubModels:Token", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadResponseBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }

    private sealed class FakeRedisAuthAttemptStore : IRedisAuthAttemptStore
    {
        private readonly Dictionary<string, long> _counts = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> Keys => _counts.Keys;

        public Task<long> IncrementAsync(string key, TimeSpan window, CancellationToken cancellationToken = default)
        {
            _counts[key] = _counts.TryGetValue(key, out var count) ? count + 1 : 1;
            return Task.FromResult(_counts[key]);
        }
    }

    private sealed class FailingRedisAuthAttemptStore : IRedisAuthAttemptStore
    {
        public Task<long> IncrementAsync(string key, TimeSpan window, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Redis unavailable. ConnectionStrings:Redis=secret");
    }

    private sealed class CapturingAuthAttemptLimiter : IAuthAttemptLimiter
    {
        public List<string> Keys { get; } = [];

        public Task<AuthAttemptLimitResult> TryConsumeAsync(
            string key,
            int permitLimit,
            TimeSpan window,
            CancellationToken cancellationToken = default)
        {
            Keys.Add(key);
            return Task.FromResult(new AuthAttemptLimitResult(true));
        }
    }

    private sealed class AlwaysUnavailableAuthAttemptLimiter : IAuthAttemptLimiter
    {
        public Task<AuthAttemptLimitResult> TryConsumeAsync(
            string key,
            int permitLimit,
            TimeSpan window,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthAttemptLimitResult(false, LimiterUnavailable: true));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<string> _messages = [];

        public string MessagesText => string.Join(Environment.NewLine, _messages);

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose()
            {
            }
        }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public TestEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Orka.API.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
