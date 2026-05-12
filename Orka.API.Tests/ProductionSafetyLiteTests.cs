using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Orka.Core.DTOs;
using Orka.API.Services;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.API.Tests;

public sealed class ProductionSafetyLiteTests
{
    [Fact]
    public async Task ProtectedHealthEndpoints_ArePublicButSanitized()
    {
        using var secrets = UseProtectedAuthSecrets();
        using var factory = new ApiSmokeFactory("Production");
        var client = factory.CreateClient();

        var live = await client.GetAsync("/health/live");
        var ready = await client.GetAsync("/health/ready");
        var aggregate = await client.GetAsync("/health");

        live.EnsureSuccessStatusCode();
        Assert.False((await ReadJsonAsync(live)).RootElement.TryGetProperty("checks", out _));
        Assert.False((await ReadJsonAsync(ready)).RootElement.TryGetProperty("checks", out _));
        Assert.False((await ReadJsonAsync(aggregate)).RootElement.TryGetProperty("checks", out _));

        var readyText = await ready.Content.ReadAsStringAsync();
        Assert.DoesNotContain("redis", readyText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sql", readyText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConnectionStrings", readyText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SecurityHeaders_ProtectedEnvironmentAddsLiteHeadersAndDevelopmentKeepsHstsOff()
    {
        using var secrets = UseProtectedAuthSecrets();
        using var productionFactory = new ApiSmokeFactory("Production");
        var production = await productionFactory.CreateClient().GetAsync("/health/live");

        AssertHeader(production, "Strict-Transport-Security", "max-age=31536000; includeSubDomains");
        AssertHeader(production, "X-Content-Type-Options", "nosniff");
        AssertHeader(production, "Referrer-Policy", "strict-origin-when-cross-origin");
        AssertHeader(production, "Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=(), usb=()");
        AssertHeader(production, "X-Frame-Options", "DENY");
        Assert.True(production.Headers.Contains("Content-Security-Policy"));

        using var developmentFactory = new ApiSmokeFactory();
        var development = await developmentFactory.CreateClient().GetAsync("/health/live");

        Assert.False(development.Headers.Contains("Strict-Transport-Security"));
        Assert.False(development.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task SystemHealth_RemainsAdminOnly()
    {
        using var secrets = UseProtectedAuthSecrets();
        using var factory = new ApiSmokeFactory("Production");
        var client = factory.CreateClient();
        var credentials = await RegisterAsync(client);
        var userToken = await LoginAndReadTokenAsync(client, credentials.Email, credentials.Password);

        using var userRequest = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/system-health");
        userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        var userResponse = await client.SendAsync(userRequest);

        Assert.Equal(HttpStatusCode.Forbidden, userResponse.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var user = await db.Users.SingleAsync(u => u.Email == credentials.Email);
            user.IsAdmin = true;
            await db.SaveChangesAsync();
        }

        var adminToken = await LoginAndReadTokenAsync(client, credentials.Email, credentials.Password);
        using var adminRequest = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/system-health");
        adminRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var adminResponse = await client.SendAsync(adminRequest);

        Assert.NotEqual(HttpStatusCode.Forbidden, adminResponse.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, adminResponse.StatusCode);
    }

    [Theory]
    [InlineData("AllowedHosts")]
    [InlineData("ConnectionStrings:DefaultConnection")]
    [InlineData("ConnectionStrings:Redis")]
    [InlineData("AI:Cost:GlobalDailyUsdLimit")]
    [InlineData("AI:Cost:UserDailyUsdLimit")]
    [InlineData("AI:GitHubModels:Token")]
    public void ProductionSafetyPolicy_FailsClosedWhenProtectedConfigIsMissing(string keyToRemove)
    {
        var values = ValidProtectedConfiguration();
        values[keyToRemove] = null;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ProductionSafetyStartupPolicy.Validate(Configuration(values), new TestEnvironment("Production"), useInMemoryDatabase: false));

        Assert.Contains("Production safety validation failed", exception.Message);
        Assert.DoesNotContain("test-github-token", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ORKA_TEST", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionSafetyPolicy_AllowsDevelopmentDefaults()
    {
        ProductionSafetyStartupPolicy.Validate(new ConfigurationBuilder().Build(), new TestEnvironment("Development"), useInMemoryDatabase: true);
    }

    [Fact]
    public async Task AiUsageBudgetService_DeniesUserAndGlobalDailyLimits()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        db.CostRecords.Add(new CostRecord
        {
            Id = Guid.NewGuid(),
            OccurredAt = DateTime.UtcNow,
            UserId = userId,
            AgentRole = "Tutor",
            EstimatedTokens = 10,
            EstimatedCostUsd = 0.01m
        });
        await db.SaveChangesAsync();

        var userLimited = new AiUsageBudgetService(
            db,
            new TokenCostEstimator(),
            Configuration(new Dictionary<string, string?>
            {
                ["AI:Cost:Enabled"] = "true",
                ["AI:Cost:GlobalDailyUsdLimit"] = "100",
                ["AI:Cost:UserDailyUsdLimit"] = "0.000001"
            }));

        var userDecision = await userLimited.CheckAsync(new AiUsageBudgetRequest(
            UserId: userId,
            Role: "Tutor",
            Provider: "GitHubModels",
            Model: "gpt-4o-mini",
            InputText: "hello",
            MaxOutputTokens: 128));

        Assert.False(userDecision.Allowed);
        Assert.Equal("user_daily_cost", userDecision.Reason);

        var globallyLimited = new AiUsageBudgetService(
            db,
            new TokenCostEstimator(),
            Configuration(new Dictionary<string, string?>
            {
                ["AI:Cost:Enabled"] = "true",
                ["AI:Cost:GlobalDailyUsdLimit"] = "0.000001",
                ["AI:Cost:UserDailyUsdLimit"] = "100"
            }));

        var globalDecision = await globallyLimited.CheckAsync(new AiUsageBudgetRequest(
            UserId: Guid.NewGuid(),
            Role: "Tutor",
            Provider: "GitHubModels",
            Model: "gpt-4o-mini",
            InputText: "hello",
            MaxOutputTokens: 128));

        Assert.False(globalDecision.Allowed);
        Assert.Equal("global_daily_cost", globalDecision.Reason);
    }

    private static void AssertHeader(HttpResponseMessage response, string name, string expected)
    {
        Assert.True(response.Headers.TryGetValues(name, out var values), $"{name} header missing.");
        Assert.Equal(expected, values.Single());
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

    private static Task<HttpResponseMessage> RegisterRawAsync(
        HttpClient client,
        string email,
        string password = "PublicPass123!") =>
        client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Safety",
            lastName = "User",
            email,
            password
        });

    private static async Task<AuthCredentials> RegisterAsync(HttpClient client)
    {
        var email = $"production-safety-{Guid.NewGuid():N}@orka.local";
        const string password = "PublicPass123!";
        var response = await RegisterRawAsync(client, email, password);
        response.EnsureSuccessStatusCode();
        return new AuthCredentials(email, password);
    }

    private static async Task<string> LoginAndReadTokenAsync(HttpClient client, string email, string password)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();

        using var body = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("token").GetString()
               ?? throw new InvalidOperationException("Login token missing.");
    }

    private static Dictionary<string, string?> ValidProtectedConfiguration() => new()
    {
        ["AllowedHosts"] = "app.example.com",
        ["JWT:Secret"] = "ORKA_TEST_JWT_SECRET_64_CHARS_2026_01",
        ["JWT:RefreshTokenHashSecret"] = "ORKA_TEST_REFRESH_HASH_SECRET_64_CHARS_2026_01",
        ["ConnectionStrings:DefaultConnection"] = "Server=sql.example.com;Database=Orka;User Id=orka;Password=Secret123!;TrustServerCertificate=True;",
        ["ConnectionStrings:Redis"] = "redis.example.com:6379,abortConnect=false",
        ["Cors:AllowedOrigins:0"] = "https://app.example.com",
        ["RateLimits:Auth:Backend"] = "Redis",
        ["RateLimits:Auth:AllowInMemoryFallback"] = "false",
        ["AI:Cost:Enabled"] = "true",
        ["AI:Cost:GlobalDailyUsdLimit"] = "100",
        ["AI:Cost:UserDailyUsdLimit"] = "10",
        ["AI:AgentRouting:Tutor:Provider"] = "GitHubModels",
        ["AI:GitHubModels:Token"] = "test-github-token"
    };

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private static OrkaDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<OrkaDbContext>()
            .UseInMemoryDatabase($"production-safety-{Guid.NewGuid():N}")
            .Options;

        return new OrkaDbContext(options);
    }

    private static IDisposable UseProtectedAuthSecrets() =>
        new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["JWT__Secret"] = "ORKA_TEST_JWT_SECRET_64_CHARS_2026_01",
            ["JWT__RefreshTokenHashSecret"] = "ORKA_TEST_REFRESH_HASH_SECRET_64_CHARS_2026_01"
        });

    private sealed record AuthCredentials(string Email, string Password);

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = new(StringComparer.OrdinalIgnoreCase);

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach (var (key, value) in values)
            {
                _previous[key] = Environment.GetEnvironmentVariable(key);
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public void Dispose()
        {
            foreach (var (key, value) in _previous)
                Environment.SetEnvironmentVariable(key, value);
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
