using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class LearningRuntimeTelemetryTests : IClassFixture<ApiSmokeFactory>
{
    private readonly ApiSmokeFactory _factory;

    public LearningRuntimeTelemetryTests(ApiSmokeFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RuntimeTrace_IsUserScopedAndCorrelationSafe()
    {
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "learning-runtime-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "learning-runtime-other");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, owner.UserId, "Learning Runtime");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(_factory, owner.UserId, topicId, DateTime.UtcNow);
        Guid traceId;

        using (var scope = _factory.Services.CreateScope())
        {
            var runtime = scope.ServiceProvider.GetRequiredService<ILearningRuntimeTelemetryService>();
            var trace = await runtime.RecordEventAsync(owner.UserId, new LearningRuntimeEventRequestDto
            {
                TopicId = topicId,
                SessionId = sessionId,
                CorrelationId = "corr-learning-runtime",
                Category = "tutor_policy",
                Operation = "policy_built",
                Status = "succeeded",
                SafeMessage = "Tutor policy was built with bounded context.",
                SafeMetadata = new Dictionary<string, string>
                {
                    ["teachingMove"] = "scaffold",
                    ["rawPrompt"] = "must not survive"
                }
            });
            traceId = trace.Id;
        }

        var ownerTrace = await owner.Client.GetAsync($"/api/learning-runtime/traces/{traceId}");
        ownerTrace.EnsureSuccessStatusCode();
        var json = await ownerTrace.Content.ReadAsStringAsync();
        Assert.Contains("tutor_policy", json);
        Assert.DoesNotContain(owner.UserId.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawPrompt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("must not survive", json, StringComparison.OrdinalIgnoreCase);

        var crossUser = await other.Client.GetAsync($"/api/learning-runtime/traces/{traceId}");
        Assert.Equal(HttpStatusCode.NotFound, crossUser.StatusCode);

        var correlation = await owner.Client.GetAsync("/api/learning-runtime/correlation/corr-learning-runtime");
        correlation.EnsureSuccessStatusCode();
        var correlationJson = await correlation.Content.ReadAsStringAsync();
        Assert.Contains("tutor_policy", correlationJson);
        Assert.DoesNotContain(owner.UserId.ToString(), correlationJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RuntimeTrace_DegradedProviderFallbackKeepsOnlySafeMetadata()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "learning-runtime-provider-fallback");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Runtime Provider Fallback");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(_factory, user.UserId, topicId, DateTime.UtcNow);

        using (var scope = _factory.Services.CreateScope())
        {
            var runtime = scope.ServiceProvider.GetRequiredService<ILearningRuntimeTelemetryService>();
            await runtime.RecordEventAsync(user.UserId, new LearningRuntimeEventRequestDto
            {
                TopicId = topicId,
                SessionId = sessionId,
                CorrelationId = "corr-provider-fallback",
                Category = "provider_call",
                Operation = "tutor_completion",
                Status = "degraded",
                Severity = "warning",
                SafeMessage = "Tutor provider degraded and used a safe fallback.",
                Provider = "GitHubModels",
                Model = "smoke-model",
                FallbackReason = "rate_limited",
                ErrorCode = "provider_rate_limited",
                SafeMetadata = new Dictionary<string, string>
                {
                    ["module"] = "tutor",
                    ["fallbackProvider"] = "Cohere",
                    ["degradeReason"] = "rate_limited",
                    ["rawProviderPayload"] = "must not survive",
                    ["apiKey"] = "secret"
                }
            });
        }

        var correlation = await user.Client.GetAsync("/api/learning-runtime/correlation/corr-provider-fallback");
        correlation.EnsureSuccessStatusCode();
        var correlationJson = await correlation.Content.ReadAsStringAsync();
        Assert.Contains("provider_call", correlationJson);
        Assert.Contains("rate_limited", correlationJson);
        Assert.Contains("Cohere", correlationJson);
        Assert.DoesNotContain("rawProviderPayload", correlationJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("must not survive", correlationJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", correlationJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", correlationJson, StringComparison.OrdinalIgnoreCase);

        var health = await user.Client.GetAsync($"/api/learning-runtime/health?topicId={topicId}&sessionId={sessionId}");
        health.EnsureSuccessStatusCode();
        using var healthJson = await JsonDocument.ParseAsync(await health.Content.ReadAsStreamAsync());
        Assert.Equal("ready_with_warnings", healthJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, healthJson.RootElement.GetProperty("fallbackCount").GetInt32());
    }

    [Fact]
    public async Task RuntimeHealth_NormalizesToolCostAndFallbackSignals()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "learning-runtime-health");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Runtime Health");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(_factory, user.UserId, topicId, DateTime.UtcNow);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            db.ToolRuntimeTraces.Add(new ToolRuntimeTrace
            {
                Id = Guid.NewGuid(),
                UserId = user.UserId,
                TopicId = topicId,
                SessionId = sessionId,
                ToolId = "source_search",
                Caller = "tutor",
                Purpose = "source evidence",
                Decision = "allow",
                Status = "degraded",
                RiskLevel = "low",
                CanGroundClaims = false,
                SafeResultSummary = "Source search degraded safely.",
                EvidenceJson = "[]",
                FallbackReason = "source_unavailable",
                LatencyMs = 12,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            });
            db.CostRecords.Add(new CostRecord
            {
                Id = Guid.NewGuid(),
                UserId = user.UserId,
                TopicId = topicId,
                SessionId = sessionId,
                AgentRole = "Tutor",
                Provider = "test-provider",
                Model = "test-model",
                EstimatedTokens = 42,
                EstimatedCostUsd = 0.02m,
                Success = true,
                MetadataJson = "{\"rawProviderPayload\":\"blocked\",\"mode\":\"safe\"}",
                OccurredAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await user.Client.GetAsync($"/api/learning-runtime/health?topicId={topicId}&sessionId={sessionId}");
        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;

        Assert.Equal("ready_with_warnings", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("fallbackCount").GetInt32() >= 1);
        Assert.Equal("available", root.GetProperty("costSummary").GetProperty("status").GetString());
        Assert.DoesNotContain("rawProviderPayload", root.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrivacyCheck_BlocksRawPromptSecretsStackTracesAndLocalPaths()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "learning-runtime-privacy");

        var response = await user.Client.PostAsJsonAsync("/api/learning-runtime/privacy-check", new
        {
            metadata = new Dictionary<string, string>
            {
                ["rawPrompt"] = "hidden",
                ["apiKey"] = "secret",
                ["stackTrace"] = "boom",
                ["localPath"] = "D:\\Orka\\secret.txt",
                ["safeStatus"] = "degraded"
            }
        });

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;

        Assert.False(root.GetProperty("isSafe").GetBoolean());
        Assert.Equal("blocked", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("safeMetadata").TryGetProperty("safeStatus", out _));
        Assert.False(root.GetProperty("safeMetadata").TryGetProperty("rawPrompt", out _));
    }

    [Fact]
    public async Task RuntimeTelemetryService_SanitizesStoredMetadata()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var telemetry = scope.ServiceProvider.GetRequiredService<IRuntimeTelemetryService>();
        var userId = Guid.NewGuid();

        await telemetry.RecordToolEventAsync(new ToolTelemetryEventRequest(
            UserId: userId,
            SessionId: null,
            TopicId: null,
            ToolId: "semantic_kernel_plugin",
            CapabilityStatus: "succeeded",
            Provider: "kernel",
            Model: null,
            LatencyMs: 5,
            Success: true,
            ErrorCode: null,
            FallbackUsed: false,
            CorrelationId: "corr-safe",
            MetadataJson: "{\"rawToolPayload\":\"do not store\",\"caller\":\"semantic_kernel_plugin\"}"));

        var row = await db.ToolTelemetryEvents.AsNoTracking().SingleAsync(t => t.UserId == userId);
        Assert.DoesNotContain("rawToolPayload", row.MetadataJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("semantic_kernel_plugin", row.MetadataJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
