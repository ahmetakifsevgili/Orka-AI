using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class UnifiedToolRuntimeTests : IClassFixture<ApiSmokeFactory>
{
    private readonly ApiSmokeFactory _factory;

    public UnifiedToolRuntimeTests(ApiSmokeFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Decide_AllowsSourceSearchAndReturnsSafeTraceContract()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "tool-runtime-source");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Tool Runtime Source");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(_factory, user.UserId, topicId, DateTime.UtcNow);

        var response = await user.Client.PostAsJsonAsync("/api/tools/runtime/decide", new
        {
            toolId = "source_search",
            caller = "tutor",
            topicId,
            sessionId,
            purpose = "Use source context before teaching.",
            riskLevel = "low",
            inputSummary = "safe bounded input summary"
        });

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;

        Assert.True(root.GetProperty("allowed").GetBoolean());
        Assert.Equal("allow", root.GetProperty("decision").GetString());
        Assert.True(root.GetProperty("canGroundClaims").GetBoolean());
        Assert.Equal("source_cited", root.GetProperty("requiredEvidenceMode").GetString());
        var traceId = root.GetProperty("traceId").GetGuid();

        var get = await user.Client.GetAsync($"/api/tools/runtime/traces/{traceId}");
        get.EnsureSuccessStatusCode();
        var json = await get.Content.ReadAsStringAsync();

        Assert.Contains("source_search", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(user.UserId.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawProvider", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hiddenPrompt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownTool_IsDeniedAndTraceIsCallerScoped()
    {
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "tool-runtime-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "tool-runtime-other");

        var response = await owner.Client.PostAsJsonAsync("/api/tools/runtime/decide", new
        {
            toolId = "shell_escape_raw",
            caller = "tutor",
            purpose = "unsafe tool",
            riskLevel = "high"
        });

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;

        Assert.False(root.GetProperty("allowed").GetBoolean());
        Assert.Equal("deny", root.GetProperty("decision").GetString());
        Assert.Equal("unknown_tool", root.GetProperty("reasonCode").GetString());

        var traceId = root.GetProperty("traceId").GetGuid();
        var crossUser = await other.Client.GetAsync($"/api/tools/runtime/traces/{traceId}");
        Assert.Equal(HttpStatusCode.NotFound, crossUser.StatusCode);
    }

    [Fact]
    public async Task RecordedEvidenceToolTrace_IsVisibleToActiveLessonSnapshot()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "tool-runtime-snapshot");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Tool Runtime Snapshot");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(_factory, user.UserId, topicId, DateTime.UtcNow);

        using (var scope = _factory.Services.CreateScope())
        {
            var runtime = scope.ServiceProvider.GetRequiredService<IUnifiedToolRuntimeService>();
            var decision = await runtime.DecideAsync(user.UserId, new ToolRuntimeRequestDto
            {
                ToolId = "source_search",
                Caller = "tutor",
                TopicId = topicId,
                SessionId = sessionId,
                Purpose = "Source evidence for snapshot.",
                RiskLevel = "low"
            });

            await runtime.RecordResultAsync(user.UserId, new ToolRuntimeResultDto
            {
                TraceId = decision.TraceId,
                ToolId = "source_search",
                Caller = "tutor",
                TopicId = topicId,
                SessionId = sessionId,
                Status = "ready",
                Success = true,
                SafeMessage = "Source context is available.",
                EvidenceItems =
                [
                    new ToolRuntimeEvidenceDto
                    {
                        EvidenceType = "summary",
                        Label = "source_context_available",
                        Provider = "orka_sql",
                        Confidence = 0.75
                    }
                ],
                LatencyMs = 3
            });
        }

        var snapshot = await user.Client.PostAsJsonAsync("/api/learning-snapshots/active-lesson/refresh", new
        {
            topicId,
            sessionId,
            approvedIntent = "learn",
            approvedMainTopic = "Runtime",
            approvedFocusArea = "Tools"
        });

        snapshot.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await snapshot.Content.ReadAsStreamAsync());
        Assert.True(body.RootElement.GetProperty("evidenceSummary").GetProperty("toolEvidenceCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task TutorToolOrchestrator_PassesPlannedToolThroughRuntimeDecisionAndTrace()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "tool-runtime-tutor");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Tool Runtime Tutor");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(_factory, user.UserId, topicId, DateTime.UtcNow);
        await CoordinationTestHelpers.SeedSourceAsync(_factory, user.UserId, topicId, "Tutor source", "Safe evidence.");

        using var scope = _factory.Services.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ITutorToolOrchestrator>();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var actionTraceId = Guid.NewGuid();
        db.TutorActionTraces.Add(new Orka.Core.Entities.TutorActionTrace
        {
            Id = actionTraceId,
            UserId = user.UserId,
            TopicId = topicId,
            SessionId = sessionId,
            TeachingMode = "source_grounded_answer",
            ToolPlanJson = "[]",
            ArtifactPlanJson = "[]",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await orchestrator.RunAsync(new TutorActionPlanDto
        {
            Id = actionTraceId,
            UserId = user.UserId,
            TopicId = topicId,
            SessionId = sessionId,
            ToolPlans = [new TutorToolPlanDto("source_search", "Use source context.", true, "low")]
        }, new TutorTurnStateDto
        {
            Id = Guid.NewGuid(),
            UserId = user.UserId,
            TopicId = topicId,
            SessionId = sessionId,
            ActiveConceptKey = "tool-runtime",
            ActiveConceptLabel = "Tool runtime",
            UserMessage = "Kaynaklara gore anlat.",
            HasNotebookContext = true,
            SourceEvidenceCount = 1
        });

        Assert.Single(result);
        Assert.Equal("source_search", result[0].ToolId);

        var trace = await db.ToolRuntimeTraces
            .AsNoTracking()
            .SingleAsync(t => t.UserId == user.UserId && t.TutorActionTraceId == actionTraceId && t.ToolId == "source_search");

        Assert.Equal("tutor", trace.Caller);
        Assert.Equal("allow", trace.Decision);
        Assert.Equal("ready", trace.Status);
        Assert.True(trace.CompletedAt.HasValue);
        Assert.DoesNotContain("RawPayload", trace.EvidenceJson, StringComparison.OrdinalIgnoreCase);
    }
}
