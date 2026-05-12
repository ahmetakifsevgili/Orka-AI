using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Orka.API.Services;
using Orka.Core.Entities;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Security;
using Xunit;

namespace Orka.API.Tests;

public sealed class AuthTokenContractTests : IClassFixture<ApiSmokeFactory>
{
    private static readonly Regex Sha256HexRegex = new("^[0-9a-f]{64}$", RegexOptions.Compiled);

    private readonly ApiSmokeFactory _factory;
    private readonly HttpClient _client;

    public AuthTokenContractTests(ApiSmokeFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsAccessRefreshTokensAndUser()
    {
        var credentials = await RegisterUserAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = credentials.Email.ToUpperInvariant(),
            password = credentials.Password
        });

        response.EnsureSuccessStatusCode();
        using var body = await ParseJsonAsync(response);

        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("token").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("jwt").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("access_token").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("refreshToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("refresh_token").GetString()));
        Assert.Equal(credentials.Email, body.RootElement.GetProperty("user").GetProperty("email").GetString());
    }

    [Fact]
    public async Task Login_WithValidCredentials_SetsHttpOnlyRefreshCookie()
    {
        var credentials = await RegisterUserAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = credentials.Email,
            password = credentials.Password
        });

        response.EnsureSuccessStatusCode();
        var cookie = ReadRefreshCookie(response);

        Assert.False(string.IsNullOrWhiteSpace(cookie.Value));
        Assert.Contains("httponly", cookie.Header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"path={RefreshTokenCookie.DefaultPath}", cookie.Header, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_WithWrongPassword_CurrentlyReturnsUnauthorized()
    {
        var credentials = await RegisterUserAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = credentials.Email,
            password = "WrongPass123!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertErrorBodyAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithUnknownEmail_CurrentlyReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = $"missing-{Guid.NewGuid():N}@orka.local",
            password = "AnyPass123!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertErrorBodyAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_WithValidRefreshToken_ReturnsNewAccessAndRefreshTokens()
    {
        var credentials = await RegisterUserAsync();
        var login = await LoginAsync(credentials);

        var response = await PostRefreshAsync(login.RefreshToken);

        response.EnsureSuccessStatusCode();
        using var body = await ParseJsonAsync(response);

        var newAccessToken = body.RootElement.GetProperty("token").GetString();
        var newRefreshToken = body.RootElement.GetProperty("refreshToken").GetString();

        Assert.False(string.IsNullOrWhiteSpace(newAccessToken));
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("jwt").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("access_token").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(newRefreshToken));
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("refresh_token").GetString()));
        Assert.NotEqual(login.RefreshToken, newRefreshToken);
    }

    [Fact]
    public async Task Refresh_WithHttpOnlyCookie_ReturnsNewTokensAndRotatesCookie()
    {
        var credentials = await RegisterUserAsync();

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = credentials.Email,
            password = credentials.Password
        });
        loginResponse.EnsureSuccessStatusCode();
        var loginCookie = ReadRefreshCookie(loginResponse);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add("Cookie", $"{RefreshTokenCookie.DefaultName}={loginCookie.Value}");

        var response = await _client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var rotatedCookie = ReadRefreshCookie(response);
        using var body = await ParseJsonAsync(response);

        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("token").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("refreshToken").GetString()));
        Assert.NotEqual(loginCookie.Value, rotatedCookie.Value);
    }

    [Fact]
    public async Task Refresh_ReusingOldRefreshToken_CurrentlyReturnsUnauthorized()
    {
        var credentials = await RegisterUserAsync();
        var login = await LoginAsync(credentials);

        var firstRefresh = await PostRefreshAsync(login.RefreshToken);
        firstRefresh.EnsureSuccessStatusCode();

        var replay = await PostRefreshAsync(login.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        await AssertErrorBodyAsync(replay, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_ThenRefreshWithSameToken_CurrentlyReturnsUnauthorized()
    {
        var credentials = await RegisterUserAsync();
        var login = await LoginAsync(credentials);

        var logout = await _client.PostAsJsonAsync("/api/auth/logout", new
        {
            refreshToken = login.RefreshToken
        });
        logout.EnsureSuccessStatusCode();

        var refreshAfterLogout = await PostRefreshAsync(login.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.StatusCode);
        await AssertErrorBodyAsync(refreshAfterLogout, HttpStatusCode.Unauthorized);

        var stored = await GetRefreshTokensForUserAsync(credentials.Email);
        Assert.Contains(stored, token => token.IsRevoked && token.RevokedReason == "Logout");
    }

    [Fact]
    public async Task Logout_WithHttpOnlyCookie_ClearsRefreshCookieAndRevokesToken()
    {
        var credentials = await RegisterUserAsync();
        var login = await LoginAsync(credentials);

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new { })
        };
        logoutRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", login.AccessToken);
        logoutRequest.Headers.Add("Cookie", $"{RefreshTokenCookie.DefaultName}={login.RefreshToken}");

        var logout = await _client.SendAsync(logoutRequest);

        logout.EnsureSuccessStatusCode();
        var setCookie = Assert.Single(logout.Headers.GetValues("Set-Cookie"), header =>
            header.StartsWith($"{RefreshTokenCookie.DefaultName}=", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);

        var refreshAfterLogout = await PostRefreshAsync(login.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.StatusCode);
    }

    [Fact]
    public async Task RefreshToken_IsStoredOnlyAsHash()
    {
        var credentials = await RegisterUserAsync();
        var login = await LoginAsync(credentials);

        Assert.Null(typeof(RefreshToken).GetProperty("Token"));

        var stored = await GetRefreshTokensForUserAsync(credentials.Email);
        var latest = stored
            .Where(token => !token.IsRevoked)
            .OrderByDescending(token => token.CreatedAt)
            .First();

        Assert.Matches(Sha256HexRegex, latest.TokenHash);
        Assert.NotEqual(login.RefreshToken, latest.TokenHash);
        Assert.False(string.IsNullOrWhiteSpace(latest.TokenHash));
        Assert.NotEqual(Guid.Empty, latest.TokenFamilyId);
        Assert.NotEmpty(latest.RowVersion);
    }

    [Fact]
    public async Task Refresh_ReplayedRotatedToken_ShouldRevokeTokenFamily()
    {
        var credentials = await RegisterUserAsync();
        var login = await LoginAsync(credentials);

        var firstRefresh = await PostRefreshAsync(login.RefreshToken);
        firstRefresh.EnsureSuccessStatusCode();
        var replacement = await ReadTokensAsync(firstRefresh);

        var replay = await PostRefreshAsync(login.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        var replacementUse = await PostRefreshAsync(replacement.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, replacementUse.StatusCode);

        var stored = await GetRefreshTokensForUserAsync(credentials.Email);
        var replayedFamilyId = stored.Single(token => token.RevokedReason == "Rotated").TokenFamilyId;
        var replayedFamily = stored.Where(token => token.TokenFamilyId == replayedFamilyId).ToArray();

        Assert.All(replayedFamily, token => Assert.True(token.IsRevoked));
        Assert.Contains(replayedFamily, token => token.RevokedReason == "ReplayDetected");
    }

    [Fact]
    public async Task Refresh_ParallelUseOfSameToken_ShouldAllowOnlyOneRotation()
    {
        var credentials = await RegisterUserAsync();
        var login = await LoginAsync(credentials);

        var responses = await Task.WhenAll(
            PostRefreshAsync(login.RefreshToken),
            PostRefreshAsync(login.RefreshToken));

        var successes = responses.Where(response => response.IsSuccessStatusCode).ToArray();
        var unauthorized = responses.Where(response => response.StatusCode == HttpStatusCode.Unauthorized).ToArray();

        Assert.Single(successes);
        Assert.Single(unauthorized);

        var replacement = await ReadTokensAsync(successes[0]);
        var replacementUse = await PostRefreshAsync(replacement.RefreshToken);
        replacementUse.EnsureSuccessStatusCode();
    }

    [Fact]
    public void RefreshTokenHashSecret_UsesDevelopmentFallbackWhenMissing()
    {
        var configuration = new ConfigurationBuilder().Build();
        var secret = RefreshTokenHashSecretResolver.Resolve(configuration, new TestEnvironment("Development"));

        Assert.True(secret.Length >= 32);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void RefreshTokenHashSecret_IsRequiredOutsideDevelopment(string environmentName)
    {
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RefreshTokenHashSecretResolver.Resolve(configuration, new TestEnvironment(environmentName)));

        Assert.DoesNotContain("JWT:RefreshTokenHashSecret", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefreshTokenHashSecret_DoesNotLeakConfiguredSecretInErrors()
    {
        const string configuredSecret = "short-secret";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT:RefreshTokenHashSecret"] = configuredSecret
            })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RefreshTokenHashSecretResolver.Resolve(configuration, new TestEnvironment("Production")));

        Assert.DoesNotContain(configuredSecret, exception.Message, StringComparison.Ordinal);
    }

    private async Task<AuthCredentials> RegisterUserAsync()
    {
        var email = $"auth-contract-{Guid.NewGuid():N}@orka.local";
        const string password = "ContractPass123!";

        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Contract",
            lastName = "User",
            email,
            password
        });

        response.EnsureSuccessStatusCode();
        return new AuthCredentials(email, password);
    }

    private async Task<AuthTokens> LoginAsync(AuthCredentials credentials)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = credentials.Email,
            password = credentials.Password
        });

        response.EnsureSuccessStatusCode();
        return await ReadTokensAsync(response);
    }

    private Task<HttpResponseMessage> PostRefreshAsync(string refreshToken) =>
        _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });

    private async Task<AuthTokens> ReadTokensAsync(HttpResponseMessage response)
    {
        using var body = await ParseJsonAsync(response);

        return new AuthTokens(
            body.RootElement.GetProperty("token").GetString()
                ?? throw new InvalidOperationException("Auth response did not include token."),
            body.RootElement.GetProperty("refreshToken").GetString()
                ?? throw new InvalidOperationException("Auth response did not include refreshToken."));
    }

    private async Task<List<RefreshToken>> GetRefreshTokensForUserAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        return await db.RefreshTokens
            .Include(token => token.User)
            .Where(token => token.User.Email == email)
            .OrderBy(token => token.CreatedAt)
            .ToListAsync();
    }

    private static async Task<JsonDocument> ParseJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

    private static async Task AssertErrorBodyAsync(HttpResponseMessage response, HttpStatusCode expectedStatusCode)
    {
        using var body = await ParseJsonAsync(response);

        Assert.Equal((int)expectedStatusCode, body.RootElement.GetProperty("statusCode").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("message").GetString()));
    }

    private static (string Header, string Value) ReadRefreshCookie(HttpResponseMessage response)
    {
        var header = Assert.Single(response.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith($"{RefreshTokenCookie.DefaultName}=", StringComparison.OrdinalIgnoreCase));
        var pair = header.Split(';', 2)[0];
        var value = pair[(RefreshTokenCookie.DefaultName.Length + 1)..];
        return (header, value);
    }

    private sealed record AuthCredentials(string Email, string Password);

    private sealed record AuthTokens(string AccessToken, string RefreshToken);

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
