using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs.Korteks;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Xunit;

namespace Orka.API.Tests;

public sealed class KorteksContractTests
{
    [Theory]
    [InlineData("/api/korteks/research")]
    [InlineData("/api/korteks/research-sync")]
    public async Task SyncResearchEndpoints_ReturnStableJsonContract(string path)
    {
        using var factory = CreateFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "korteks-sync");

        var response = await user.Client.PostAsJsonAsync(path, new { topic = "Contract topic" });

        response.EnsureSuccessStatusCode();
        Assert.StartsWith("application/json", response.Content.Headers.ContentType?.MediaType);

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("Contract topic", root.GetProperty("topic").GetString());
        Assert.Equal("Smoke Korteks report", root.GetProperty("report").GetString());
        Assert.Equal("Smoke Korteks report", root.GetProperty("answer").GetString());
        Assert.Equal("Smoke Korteks report", root.GetProperty("research").GetString());
        Assert.Equal("SourceGrounded", root.GetProperty("groundingMode").GetString());
        Assert.Equal(1, root.GetProperty("sourceCount").GetInt32());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("sources").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("providerWarnings").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("providerCalls").ValueKind);
        Assert.False(root.GetProperty("isFallback").GetBoolean());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("legacySources").ValueKind);
    }

    [Fact]
    public async Task StreamResearchEndpoint_ReturnsSseChunksAndDoneMarker()
    {
        using var factory = CreateFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "korteks-stream");

        var response = await user.Client.PostAsJsonAsync("/api/korteks/research-stream", new { topic = "Stream topic" });

        response.EnsureSuccessStatusCode();
        Assert.StartsWith("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("data: Smoke Korteks chunk", content);
        Assert.Contains("data: [DONE]", content);
    }

    [Fact]
    public void FrontendKorteksStreamClient_UsesStreamEndpoint()
    {
        var repoRoot = FindRepoRoot();
        var apiTs = Path.Combine(repoRoot, "Orka-Front", "src", "services", "api.ts");
        var text = File.ReadAllText(apiTs);

        Assert.Contains("authenticatedFetch(\"/api/korteks/research-stream\"", text);
        Assert.Contains("authenticatedFetch(\"/api/korteks/research-file\"", text);
        Assert.DoesNotContain("buildApiUrl(\"/api/korteks/research\")", text);
    }

    private static ApiSmokeFactory CreateFactory() =>
        new("Development", configureServices: services =>
        {
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IKorteksAgent)).ToList())
                services.Remove(descriptor);

            services.AddSingleton<IKorteksAgent, FakeKorteksAgent>();
        });

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Orka.sln")) &&
                Directory.Exists(Path.Combine(directory.FullName, "Orka-Front")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root could not be located.");
    }

    private sealed class FakeKorteksAgent : IKorteksAgent
    {
        public async IAsyncEnumerable<string> RunResearchAsync(
            string topic,
            Guid userId,
            Guid? topicId = null,
            string? fileContext = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return "Smoke Korteks chunk";
        }

        public Task<KorteksResearchResultDto> RunResearchWithEvidenceAsync(
            string topic,
            Guid userId,
            Guid? topicId = null,
            string? fileContext = null,
            CancellationToken ct = default) =>
            Task.FromResult(new KorteksResearchResultDto
            {
                Topic = topic,
                TopicId = topicId,
                Report = "Smoke Korteks report",
                GroundingMode = GroundingMode.SourceGrounded,
                Sources =
                [
                    new SourceEvidenceDto(
                        "smoke-provider",
                        "SmokeSearch",
                        "https://example.test/source",
                        "Smoke Source",
                        "Smoke snippet",
                        null,
                        DateTimeOffset.UtcNow,
                        0.9,
                        "web",
                        "smoke-1",
                        null)
                ],
                ProviderCalls =
                [
                    new ToolCallEvidenceDto(
                        "SmokeSearch",
                        "smoke-provider",
                        true,
                        true,
                        null,
                        1,
                        3,
                        null,
                        DateTimeOffset.UtcNow)
                ],
                IsFallback = false
            });
    }
}
