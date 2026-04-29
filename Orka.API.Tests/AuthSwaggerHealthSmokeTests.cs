using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Orka.API.Tests;

public sealed class AuthSwaggerHealthSmokeTests : IClassFixture<ApiSmokeFactory>
{
    private readonly HttpClient _client;

    public AuthSwaggerHealthSmokeTests(ApiSmokeFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SwaggerJson_ContainsAuthEndpoints()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();

        var swagger = await response.Content.ReadAsStringAsync();

        Assert.Contains("/api/auth/register", swagger);
        Assert.Contains("/api/auth/login", swagger);
        Assert.Contains("/api/auth/refresh", swagger);
        Assert.Contains("/api/auth/logout", swagger);
    }

    [Fact]
    public async Task HealthEndpoints_ReturnStructuredJson()
    {
        var live = await _client.GetAsync("/health/live");
        live.EnsureSuccessStatusCode();

        using var liveJson = await JsonDocument.ParseAsync(await live.Content.ReadAsStreamAsync());
        Assert.Equal("alive", liveJson.RootElement.GetProperty("status").GetString());

        var ready = await _client.GetAsync("/health/ready");
        Assert.True(
            ready.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable,
            $"Unexpected readiness status: {ready.StatusCode}");

        using var readyJson = await JsonDocument.ParseAsync(await ready.Content.ReadAsStreamAsync());
        Assert.True(readyJson.RootElement.TryGetProperty("status", out _));
        Assert.True(readyJson.RootElement.TryGetProperty("checks", out _));
    }

    [Fact]
    public async Task RegisterLoginAndMe_RoundTripsWithBearerToken()
    {
        var email = $"smoke-{Guid.NewGuid():N}@orka.local";
        var password = "SmokePass123!";

        var register = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Smoke",
            lastName = "User",
            email,
            password
        });
        register.EnsureSuccessStatusCode();

        var registerBody = await register.Content.ReadFromJsonAsync<AuthBody>();
        Assert.False(string.IsNullOrWhiteSpace(registerBody?.Token));
        Assert.Equal("Smoke", registerBody!.User.FirstName);
        Assert.Equal("User", registerBody.User.LastName);
        Assert.NotNull(registerBody.User.Settings);

        var login = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password
        });
        login.EnsureSuccessStatusCode();

        var loginBody = await login.Content.ReadFromJsonAsync<AuthBody>();
        Assert.False(string.IsNullOrWhiteSpace(loginBody?.RefreshToken));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/user/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginBody!.Token);

        var me = await _client.SendAsync(request);
        me.EnsureSuccessStatusCode();

        var meBody = await me.Content.ReadFromJsonAsync<UserBody>();
        Assert.Equal(email, meBody?.Email);
        Assert.Equal("Smoke", meBody?.FirstName);
        Assert.False(string.IsNullOrWhiteSpace(meBody?.Plan));
    }

    [Fact]
    public async Task InvalidRefresh_ReturnsUnauthorizedWithoutLooping()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/refresh", new
        {
            refreshToken = "invalid-refresh-token"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record AuthBody(string Token, string RefreshToken, UserBody User);

    private sealed record UserBody(
        string Id,
        string FirstName,
        string LastName,
        string Email,
        string Plan,
        int DailyMessageCount,
        int DailyLimit,
        bool IsAdmin,
        UserSettingsBody? Settings);

    private sealed record UserSettingsBody(
        string Theme,
        string Language,
        string FontSize,
        bool QuizReminders,
        bool WeeklyReport,
        bool NewContentAlerts,
        bool SoundsEnabled);
}
