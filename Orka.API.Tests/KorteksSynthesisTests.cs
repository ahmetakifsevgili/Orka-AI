using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs.Korteks;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class KorteksSynthesisTests : IClassFixture<ApiSmokeFactory>
{
    private readonly ApiSmokeFactory _factory;

    public KorteksSynthesisTests(ApiSmokeFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task BuildAndSave_CreatesSourceAwareConsumerContexts()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "korteks-synthesis-source");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Korteks Synthesis Algebra");

        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IKorteksSynthesisService>();

        var workflow = await service.BuildAndSaveAsync(user.UserId, BuildSourceGroundedResearch(topicId), new KorteksResearchSynthesisContextDto
        {
            TopicId = topicId,
            ApprovedIntent = "learn algebra fractions",
            ApprovedMainTopic = "Algebra",
            ApprovedFocusArea = "Fractions",
            ApprovedStudyGoal = "practice"
        });

        Assert.Equal("completed", workflow.Status);
        Assert.Equal("medium", workflow.SourceConfidence);
        Assert.True(workflow.CanGroundTutorClaims);
        Assert.NotEmpty(workflow.Synthesis.Prerequisites);
        Assert.NotEmpty(workflow.Synthesis.Misconceptions);
        Assert.NotEmpty(workflow.ConsumerContexts.Plan.PromptBlock);
        Assert.Contains("Diagnostic", workflow.ConsumerContexts.Plan.PromptBlock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scope", workflow.ConsumerContexts.Quiz.UsagePolicy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cite", workflow.ConsumerContexts.Tutor.PromptBlock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("notebook", workflow.ConsumerContexts.Wiki.UsagePolicy, StringComparison.OrdinalIgnoreCase);

        var publicJson = JsonSerializer.Serialize(workflow);
        Assert.DoesNotContain(user.UserId.ToString(), publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawProvider", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hiddenPrompt", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", publicJson, StringComparison.OrdinalIgnoreCase);

        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        Assert.True(await db.KorteksResearchWorkflows.AnyAsync(w => w.Id == workflow.Id && w.UserId == user.UserId));
    }

    [Fact]
    public async Task FallbackResearch_IsDegradedAndCannotGroundTutorClaims()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "korteks-synthesis-fallback");

        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IKorteksSynthesisService>();

        var workflow = await service.BuildAndSaveAsync(user.UserId, new KorteksResearchResultDto
        {
            Topic = "Unsourced topic",
            Report = "Internal fallback note. Common mistakes: guessing too early. Prerequisite: base concept.",
            GroundingMode = GroundingMode.FallbackInternalKnowledge,
            Warnings = ["provider disabled"],
            IsFallback = true
        });

        Assert.Equal("degraded", workflow.Status);
        Assert.Equal("low", workflow.SourceConfidence);
        Assert.False(workflow.CanGroundTutorClaims);
        Assert.Contains(workflow.SafetyIssues, issue => issue.Code == "evidence_insufficient");
        Assert.Contains("insufficient", workflow.EvidenceSummary.GroundingStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApiSynthesisWorkflow_IsCallerScopedAndReturnedFromSyncResearch()
    {
        using var factory = CreateFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "korteks-synthesis-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "korteks-synthesis-other");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, owner.UserId, "Korteks API Synthesis");

        var response = await owner.Client.PostAsJsonAsync("/api/korteks/research", new
        {
            topic = "Korteks API Synthesis",
            topicId
        });

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;

        var workflowId = root.GetProperty("synthesisWorkflowId").GetGuid();
        Assert.Equal("completed", root.GetProperty("synthesisStatus").GetString());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("synthesis").ValueKind);
        Assert.Equal(JsonValueKind.Object, root.GetProperty("consumerContexts").ValueKind);
        Assert.Equal(JsonValueKind.Object, root.GetProperty("evidenceSummary").ValueKind);
        Assert.DoesNotContain(owner.UserId.ToString(), root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawProvider", root.GetRawText(), StringComparison.OrdinalIgnoreCase);

        var ownerGet = await owner.Client.GetAsync($"/api/korteks/synthesis/{workflowId}");
        ownerGet.EnsureSuccessStatusCode();

        var otherGet = await other.Client.GetAsync($"/api/korteks/synthesis/{workflowId}");
        Assert.Equal(HttpStatusCode.NotFound, otherGet.StatusCode);
    }

    [Fact]
    public async Task ActiveLessonSnapshot_CountsKorteksWorkflowAsToolEvidenceOnly()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "korteks-synthesis-snapshot");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Korteks Snapshot Evidence");

        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IKorteksSynthesisService>();
            await service.BuildAndSaveAsync(user.UserId, BuildSourceGroundedResearch(topicId), new KorteksResearchSynthesisContextDto
            {
                TopicId = topicId
            });
        }

        var snapshot = await user.Client.PostAsJsonAsync("/api/learning-snapshots/active-lesson/refresh", new
        {
            topicId,
            approvedIntent = "learn",
            approvedMainTopic = "Korteks",
            approvedFocusArea = "Synthesis"
        });

        snapshot.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await snapshot.Content.ReadAsStreamAsync());
        var evidence = body.RootElement.GetProperty("evidenceSummary");

        Assert.Equal(0, evidence.GetProperty("sourceEvidenceCount").GetInt32());
        Assert.True(evidence.GetProperty("toolEvidenceCount").GetInt32() >= 1);
    }

    private static KorteksResearchResultDto BuildSourceGroundedResearch(Guid? topicId = null) => new()
    {
        Topic = "Algebra fractions",
        TopicId = topicId,
        Report = """
            Learning route: start with fraction meaning, then equivalent fractions, then operations.
            Prerequisite: number line and multiplication facts.
            Common mistake: learners cancel terms without preserving equivalence.
            Quiz scope: equivalence, simplification, mixed operation order.
            Practice order: visual model, guided example, independent drill.
            """,
        GroundingMode = GroundingMode.PartialSourceGrounded,
        Sources =
        [
            new SourceEvidenceDto(
                "WebSearch",
                "SearchWebDeep",
                "https://example.test/fractions",
                "Fraction learning source",
                "Prerequisite and misconception evidence.",
                null,
                DateTimeOffset.UtcNow,
                0.85,
                "web",
                "source-1",
                null)
        ],
        ProviderCalls =
        [
            new ToolCallEvidenceDto(
                "SearchWebDeep",
                "WebSearch",
                true,
                true,
                null,
                1,
                4,
                null,
                DateTimeOffset.UtcNow)
        ],
        IsFallback = false
    };

    private static ApiSmokeFactory CreateFactory() =>
        new("Development", configureServices: services =>
        {
            foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IKorteksAgent)).ToList())
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<IKorteksAgent, FakeKorteksAgent>();
        });

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
            yield return "Synthesis test stream";
        }

        public Task<KorteksResearchResultDto> RunResearchWithEvidenceAsync(
            string topic,
            Guid userId,
            Guid? topicId = null,
            string? fileContext = null,
            CancellationToken ct = default)
        {
            var result = BuildSourceGroundedResearch(topicId);
            result.Topic = topic;
            return Task.FromResult(result);
        }
    }
}
