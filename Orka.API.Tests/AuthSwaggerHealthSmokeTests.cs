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
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

        using var readyJson = await JsonDocument.ParseAsync(await ready.Content.ReadAsStreamAsync());
        Assert.True(readyJson.RootElement.TryGetProperty("status", out _));
        Assert.True(readyJson.RootElement.TryGetProperty("checks", out _));
    }

    [Fact]
    public async Task RegisterLoginAndMe_RoundTripsWithBearerToken()
    {
        var email = $"Smoke-{Guid.NewGuid():N}@Orka.Local";
        var normalizedEmail = email.ToLowerInvariant();
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
        if (registerBody != null && register.Headers.TryGetValues("Set-Cookie", out var regCookies))
        {
            var cookie = regCookies.FirstOrDefault(c => c.Contains("orka_refresh="));
            if (cookie != null)
            {
                var match = System.Text.RegularExpressions.Regex.Match(cookie, @"orka_refresh=([^;]+)");
                if (match.Success) registerBody.RefreshToken = match.Groups[1].Value;
            }
        }

        Assert.False(string.IsNullOrWhiteSpace(registerBody?.Token));
        Assert.Equal("Smoke", registerBody!.User.FirstName);
        Assert.Equal("User", registerBody.User.LastName);
        Assert.NotNull(registerBody.User.Settings);

        var login = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = normalizedEmail,
            password
        });
        login.EnsureSuccessStatusCode();

        var loginBody = await login.Content.ReadFromJsonAsync<AuthBody>();
        if (loginBody != null && login.Headers.TryGetValues("Set-Cookie", out var logCookies))
        {
            var cookie = logCookies.FirstOrDefault(c => c.Contains("orka_refresh="));
            if (cookie != null)
            {
                var match = System.Text.RegularExpressions.Regex.Match(cookie, @"orka_refresh=([^;]+)");
                if (match.Success) loginBody.RefreshToken = match.Groups[1].Value;
            }
        }

        Assert.False(string.IsNullOrWhiteSpace(loginBody?.RefreshToken));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/user/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginBody!.Token);

        var me = await _client.SendAsync(request);
        me.EnsureSuccessStatusCode();

        var meBody = await me.Content.ReadFromJsonAsync<UserBody>();
        Assert.Equal(normalizedEmail, meBody?.Email);
        Assert.Equal("Smoke", meBody?.FirstName);
        Assert.False(string.IsNullOrWhiteSpace(meBody?.Plan));
    }

    [Fact]
    public async Task AuthController_RejectsInvalidInputAndSupportsLegacyName()
    {
        var badRegister = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "",
            password = "short"
        });
        Assert.Equal(HttpStatusCode.BadRequest, badRegister.StatusCode);

        var badLogin = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "",
            password = ""
        });
        Assert.Equal(HttpStatusCode.BadRequest, badLogin.StatusCode);

        var email = $"legacy-{Guid.NewGuid():N}@orka.local";
        var register = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            name = "Legacy Person",
            email,
            password = "LegacyPass123!"
        });
        register.EnsureSuccessStatusCode();

        var body = await register.Content.ReadFromJsonAsync<AuthBody>();
        if (body != null && register.Headers.TryGetValues("Set-Cookie", out var regCookies))
        {
            var cookie = regCookies.FirstOrDefault(c => c.Contains("orka_refresh="));
            if (cookie != null)
            {
                var match = System.Text.RegularExpressions.Regex.Match(cookie, @"orka_refresh=([^;]+)");
                if (match.Success) body.RefreshToken = match.Groups[1].Value;
            }
        }

        Assert.Equal("Legacy", body?.User.FirstName);
        Assert.Equal("Person", body?.User.LastName);
        Assert.Equal(email, body?.User.Email);
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

    private sealed class AuthBody
    {
        public string Token { get; set; } = string.Empty;
        public string? RefreshToken { get; set; }
        public UserBody User { get; set; } = null!;
    }

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
