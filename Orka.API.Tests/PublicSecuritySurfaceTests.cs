using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Orka.API.Tests;

public sealed class PublicSecuritySurfaceTests
{
    private static readonly SemaphoreSlim EnvironmentGate = new(1, 1);

    [Fact]
    public async Task LoginFailure_DoesNotDistinguishUnknownEmailFromWrongPassword()
    {
        using var factory = new ApiSmokeFactory();
        var client = factory.CreateClient();
        var credentials = await RegisterAsync(client);

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = credentials.Email,
            password = "WrongPass123!"
        });

        var unknownEmail = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = $"missing-{Guid.NewGuid():N}@orka.local",
            password = "WrongPass123!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(await MessageOfAsync(wrongPassword), await MessageOfAsync(unknownEmail));
    }

    [Fact]
    public async Task DuplicateRegister_ReturnsGenericConflictMessage()
    {
        using var factory = new ApiSmokeFactory();
        var client = factory.CreateClient();
        var credentials = await RegisterAsync(client);

        var duplicate = await client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Duplicate",
            lastName = "User",
            email = credentials.Email.ToUpperInvariant(),
            password = credentials.Password
        });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var message = await MessageOfAsync(duplicate);
        Assert.DoesNotContain("exists", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("zaten", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(credentials.Email, message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_IsRateLimitedByClientAndEmail()
    {
        using var factory = new ApiSmokeFactory(
            "Development",
            new Dictionary<string, string?>
            {
                ["RateLimits:Auth:Login:PermitLimit"] = "2",
                ["RateLimits:Auth:Login:WindowMinutes"] = "5"
            });
        var client = factory.CreateClient();
        var credentials = await RegisterAsync(client);

        var first = await LoginAsync(client, credentials.Email, "WrongPass123!");
        var second = await LoginAsync(client, credentials.Email, "WrongPass123!");
        var third = await LoginAsync(client, credentials.Email, "WrongPass123!");

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal((HttpStatusCode)429, third.StatusCode);
    }

    [Fact]
    public async Task Register_IsRateLimitedByClient()
    {
        using var factory = new ApiSmokeFactory(
            "Development",
            new Dictionary<string, string?>
            {
                ["RateLimits:Auth:Register:PermitLimit"] = "2",
                ["RateLimits:Auth:Register:WindowMinutes"] = "15"
            });
        var client = factory.CreateClient();

        var first = await RegisterRawAsync(client, $"register-limit-{Guid.NewGuid():N}@orka.local");
        var second = await RegisterRawAsync(client, $"register-limit-{Guid.NewGuid():N}@orka.local");
        var third = await RegisterRawAsync(client, $"register-limit-{Guid.NewGuid():N}@orka.local");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal((HttpStatusCode)429, third.StatusCode);
    }

    [Fact]
    public async Task ChaosHeader_IsActiveInDevelopment()
    {
        await EnvironmentGate.WaitAsync();
        try
        {
            using var factory = new ApiSmokeFactory();
            var client = factory.CreateClient();
            var credentials = await RegisterAsync(client);
            var token = (await LoginAndReadTokenAsync(client, credentials.Email, credentials.Password));
            ApiSmokeFactory.ResetChaosTracking();

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/message");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Chaos-Fail", "Groq");
            request.Content = JsonContent.Create(new { content = "Smoke chaos check" });

            var response = await client.SendAsync(request);

            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains("Groq", ApiSmokeFactory.GetChaosTrackingProviders());
        }
        finally
        {
            EnvironmentGate.Release();
        }
    }

    [Fact]
    public async Task ChaosHeader_IsIgnoredOutsideDevelopmentEvenForAdmin()
    {
        await EnvironmentGate.WaitAsync();
        var previousJwtSecret = Environment.GetEnvironmentVariable("JWT__Secret");
        var previousRefreshSecret = Environment.GetEnvironmentVariable("JWT__RefreshTokenHashSecret");
        Environment.SetEnvironmentVariable("JWT__Secret", "ORKA_TEST_PRODUCTION_JWT_SECRET_64_CHARS_2026_01");
        Environment.SetEnvironmentVariable("JWT__RefreshTokenHashSecret", "ORKA_TEST_PRODUCTION_REFRESH_HASH_SECRET_64_CHARS_2026_01");

        try
        {
            using var factory = new ApiSmokeFactory("Production");
            var client = factory.CreateClient();
            var credentials = await RegisterAsync(client);
            var token = await LoginAndReadTokenAsync(client, credentials.Email, credentials.Password);
            ApiSmokeFactory.ResetChaosTracking();

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/message");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Chaos-Fail", "Groq");
            request.Content = JsonContent.Create(new { content = "Smoke chaos check" });

            var response = await client.SendAsync(request);

            response.EnsureSuccessStatusCode();
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.DoesNotContain("Groq", ApiSmokeFactory.GetChaosTrackingProviders());
        }
        finally
        {
            Environment.SetEnvironmentVariable("JWT__Secret", previousJwtSecret);
            Environment.SetEnvironmentVariable("JWT__RefreshTokenHashSecret", previousRefreshSecret);
            EnvironmentGate.Release();
        }
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password });

    private static async Task<AuthCredentials> RegisterAsync(HttpClient client)
    {
        var email = $"public-security-{Guid.NewGuid():N}@orka.local";
        const string password = "PublicPass123!";
        var response = await RegisterRawAsync(client, email, password);
        response.EnsureSuccessStatusCode();
        return new AuthCredentials(email, password);
    }

    private static Task<HttpResponseMessage> RegisterRawAsync(
        HttpClient client,
        string email,
        string password = "PublicPass123!") =>
        client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Public",
            lastName = "Security",
            email,
            password
        });

    private static async Task<string> LoginAndReadTokenAsync(HttpClient client, string email, string password)
    {
        var login = await LoginAsync(client, email, password);
        login.EnsureSuccessStatusCode();

        using var body = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("token").GetString()
               ?? throw new InvalidOperationException("Login token missing.");
    }

    private static async Task<string> MessageOfAsync(HttpResponseMessage response)
    {
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("message").GetString() ?? string.Empty;
    }

    private sealed record AuthCredentials(string Email, string Password);
}
