using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace Orka.API.Tests;

[Trait("Category", "External")]
public sealed class ExternalProviderIntegrationTests
{
    [GitHubModelsProviderFact]
    public async Task GitHubModels_MinimalCompletion_WhenExplicitlyEnabled()
    {
        var token = GetGitHubModelsToken() ?? throw new InvalidOperationException("GitHub Models token gate did not skip this test.");

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

        Assert.False(
            body.Contains(token, StringComparison.Ordinal),
            "External provider response echoed the configured token.");
        Assert.True(
            response.IsSuccessStatusCode,
            $"External provider smoke failed with {(int)response.StatusCode}; bodyLength={body.Length}");
    }

    [ExternalProviderFact]
    public async Task GitHubModels_InvalidToken_ReturnsAuthenticationFailure_WhenExplicitlyEnabled()
    {
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

    [ExternalProviderCredentialFact("OpenRouter", "ORKA_EXTERNAL_OPENROUTER_API_KEY", "AI__OpenRouter__ApiKey")]
    public Task OpenRouter_MinimalCompletion_WhenExplicitlyEnabled() =>
        SmokeOpenAiCompatibleProviderAsync(
            "OpenRouter",
            GetFirstConfigured("ORKA_EXTERNAL_OPENROUTER_API_KEY", "AI__OpenRouter__ApiKey")!,
            Environment.GetEnvironmentVariable("ORKA_EXTERNAL_OPENROUTER_BASE_URL") ?? "https://openrouter.ai/api/v1",
            Environment.GetEnvironmentVariable("ORKA_EXTERNAL_OPENROUTER_MODEL") ?? "openai/gpt-4o-mini");

    [ExternalProviderCredentialFact("Cohere", "ORKA_EXTERNAL_COHERE_API_KEY", "AI__Cohere__ApiKey")]
    public Task Cohere_MinimalCompletion_WhenExplicitlyEnabled() =>
        SmokeOpenAiCompatibleProviderAsync(
            "Cohere",
            GetFirstConfigured("ORKA_EXTERNAL_COHERE_API_KEY", "AI__Cohere__ApiKey")!,
            Environment.GetEnvironmentVariable("ORKA_EXTERNAL_COHERE_BASE_URL") ?? "https://api.cohere.com/compatibility/v1",
            Environment.GetEnvironmentVariable("ORKA_EXTERNAL_COHERE_MODEL") ?? "command-a-03-2025");

    [ExternalProviderCredentialFact("Groq", "ORKA_EXTERNAL_GROQ_API_KEY", "AI__Groq__ApiKey")]
    public Task Groq_MinimalCompletion_WhenExplicitlyEnabled() =>
        SmokeOpenAiCompatibleProviderAsync(
            "Groq",
            GetFirstConfigured("ORKA_EXTERNAL_GROQ_API_KEY", "AI__Groq__ApiKey")!,
            Environment.GetEnvironmentVariable("ORKA_EXTERNAL_GROQ_BASE_URL") ?? "https://api.groq.com/openai/v1",
            Environment.GetEnvironmentVariable("ORKA_EXTERNAL_GROQ_MODEL") ?? "llama-3.3-70b-versatile");

    [ExternalProviderCredentialFact("Mistral", "ORKA_EXTERNAL_MISTRAL_API_KEY", "AI__Mistral__ApiKey")]
    public Task Mistral_MinimalCompletion_WhenExplicitlyEnabled() =>
        SmokeOpenAiCompatibleProviderAsync(
            "Mistral",
            GetFirstConfigured("ORKA_EXTERNAL_MISTRAL_API_KEY", "AI__Mistral__ApiKey")!,
            Environment.GetEnvironmentVariable("ORKA_EXTERNAL_MISTRAL_BASE_URL") ?? "https://api.mistral.ai/v1",
            Environment.GetEnvironmentVariable("ORKA_EXTERNAL_MISTRAL_MODEL") ?? "mistral-small-latest");

    private static string? GetGitHubModelsToken() =>
        ExternalProviderTestGate.GitHubModelsToken;

    private static string? GetFirstConfigured(params string[] envNames) =>
        envNames
            .Select(Environment.GetEnvironmentVariable)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private async Task SmokeOpenAiCompatibleProviderAsync(string provider, string token, string baseUrl, string model)
    {
        using var client = CreateOpenAiCompatibleClient(baseUrl, token);
        var response = await client.PostAsJsonAsync("chat/completions", new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = "Reply with exactly: ok" },
                new { role = "user", content = "ok" }
            },
            max_tokens = 8,
            temperature = 0
        });
        var body = await response.Content.ReadAsStringAsync();

        Assert.False(
            body.Contains(token, StringComparison.Ordinal),
            $"{provider} response echoed the configured token.");
        Assert.True(
            response.IsSuccessStatusCode,
            $"{provider} smoke failed with {(int)response.StatusCode}; bodyLength={body.Length}");
    }

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

    private static HttpClient CreateOpenAiCompatibleClient(string baseUrl, string token)
    {
        var normalized = baseUrl.TrimEnd('/');
        if (normalized.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^"/chat/completions".Length];
        }

        var client = new HttpClient { BaseAddress = new Uri(normalized + "/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OrkaExternalProviderSmoke/1.0");
        client.Timeout = TimeSpan.FromSeconds(20);
        return client;
    }

}

internal static class ExternalProviderTestGate
{
    public static bool Enabled =>
        string.Equals(
            Environment.GetEnvironmentVariable("ORKA_RUN_EXTERNAL_PROVIDER_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    public static string? GitHubModelsToken =>
        Environment.GetEnvironmentVariable("ORKA_EXTERNAL_GITHUB_MODELS_TOKEN") ??
        Environment.GetEnvironmentVariable("AI__GitHubModels__Token");
}

internal sealed class ExternalProviderFactAttribute : FactAttribute
{
    public ExternalProviderFactAttribute()
    {
        if (!ExternalProviderTestGate.Enabled)
        {
            Skip = "Set ORKA_RUN_EXTERNAL_PROVIDER_TESTS=true to run external provider smoke tests.";
        }
    }
}

internal sealed class GitHubModelsProviderFactAttribute : FactAttribute
{
    public GitHubModelsProviderFactAttribute()
    {
        if (!ExternalProviderTestGate.Enabled)
        {
            Skip = "Set ORKA_RUN_EXTERNAL_PROVIDER_TESTS=true to run external provider smoke tests.";
        }
        else if (string.IsNullOrWhiteSpace(ExternalProviderTestGate.GitHubModelsToken))
        {
            Skip = "ORKA_EXTERNAL_GITHUB_MODELS_TOKEN or AI__GitHubModels__Token is required.";
        }
    }
}

internal sealed class ExternalProviderCredentialFactAttribute : FactAttribute
{
    public ExternalProviderCredentialFactAttribute(string provider, params string[] tokenEnvNames)
    {
        if (!ExternalProviderTestGate.Enabled)
        {
            Skip = "Set ORKA_RUN_EXTERNAL_PROVIDER_TESTS=true to run external provider smoke tests.";
            return;
        }

        if (!tokenEnvNames.Any(name => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
        {
            Skip = $"{provider} token is required in one of: {string.Join(", ", tokenEnvNames)}.";
        }
    }
}
