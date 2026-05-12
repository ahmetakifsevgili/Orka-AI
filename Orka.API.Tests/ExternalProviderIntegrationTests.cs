using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;
using Xunit.Abstractions;

namespace Orka.API.Tests;

[Trait("Category", "External")]
public sealed class ExternalProviderIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public ExternalProviderIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task GitHubModels_MinimalCompletion_WhenExplicitlyEnabled()
    {
        if (!ExternalProviderTestsEnabled())
        {
            _output.WriteLine("Skipped: set ORKA_RUN_EXTERNAL_PROVIDER_TESTS=true to run external provider smoke tests.");
            return;
        }

        var token = GetGitHubModelsToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            _output.WriteLine("Skipped: ORKA_EXTERNAL_GITHUB_MODELS_TOKEN or AI__GitHubModels__Token is required.");
            return;
        }

        using var client = CreateGitHubModelsClient(token);
        var response = await client.PostAsJsonAsync("chat/completions", new
        {
            model = Environment.GetEnvironmentVariable("ORKA_EXTERNAL_GITHUB_MODELS_MODEL") ?? "gpt-4o-mini",
            messages = new[]
            {
                new { role = "user", content = "Reply with exactly: ok" }
            },
            max_tokens = 8,
            temperature = 0
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(token, body, StringComparison.Ordinal);
        Assert.True(response.IsSuccessStatusCode, $"External provider smoke failed with {(int)response.StatusCode}: {TrimDiagnostic(body)}");
    }

    [Fact]
    public async Task GitHubModels_InvalidToken_ReturnsAuthenticationFailure_WhenExplicitlyEnabled()
    {
        if (!ExternalProviderTestsEnabled())
        {
            _output.WriteLine("Skipped: set ORKA_RUN_EXTERNAL_PROVIDER_TESTS=true to run external provider smoke tests.");
            return;
        }

        using var client = CreateGitHubModelsClient("ORKA_INVALID_EXTERNAL_PROVIDER_TOKEN");
        var response = await client.PostAsJsonAsync("chat/completions", new
        {
            model = Environment.GetEnvironmentVariable("ORKA_EXTERNAL_GITHUB_MODELS_MODEL") ?? "gpt-4o-mini",
            messages = new[]
            {
                new { role = "user", content = "auth smoke" }
            },
            max_tokens = 4,
            temperature = 0
        });

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
    }

    private static bool ExternalProviderTestsEnabled() =>
        string.Equals(
            Environment.GetEnvironmentVariable("ORKA_RUN_EXTERNAL_PROVIDER_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    private static string? GetGitHubModelsToken() =>
        Environment.GetEnvironmentVariable("ORKA_EXTERNAL_GITHUB_MODELS_TOKEN") ??
        Environment.GetEnvironmentVariable("AI__GitHubModels__Token");

    private static HttpClient CreateGitHubModelsClient(string token)
    {
        var baseUrl = Environment.GetEnvironmentVariable("ORKA_EXTERNAL_GITHUB_MODELS_BASE_URL") ??
                      "https://models.github.ai/inference";
        var client = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OrkaExternalProviderSmoke/1.0");
        client.Timeout = TimeSpan.FromSeconds(20);
        return client;
    }

    private static string TrimDiagnostic(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Length <= 500 ? value : value[..500];
}
