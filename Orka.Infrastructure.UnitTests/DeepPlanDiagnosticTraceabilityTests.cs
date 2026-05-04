using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orka.Core.DTOs;
using Orka.Core.DTOs.Korteks;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public sealed class DeepPlanDiagnosticTraceabilityTests
{
    [Fact]
    public async Task DiagnosticFinalize_PreservesWeakConceptsInSavedPlanOutput()
    {
        var harness = await CreateHarnessAsync();
        var summary = Summary(
            ["blocking-vs-async", "deadlock", "Task.Wait/Result", "code-reading"],
            ["Procedural", "Reading", "MisreadQuestion"]);

        var result = await harness.Agent.GenerateAndSaveDeepPlanFromDiagnosticAsync(
            harness.TopicId,
            "Korteks Runtime Proof",
            harness.UserId,
            "[SIKISTIRILMIS PLAN ARASTIRMA BAGLAMI]\nsame stored context",
            summary,
            "intermediate");

        var diagnosticLessons = result.Topics
            .Where(t => t.PhaseMetadata?.Contains("PlanDiagnostic", StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(t => t.Order)
            .ToList();

        Assert.Equal(0, harness.Korteks.CallCount);
        Assert.NotEmpty(diagnosticLessons);
        Assert.Contains(diagnosticLessons, t => t.PhaseMetadata?.Contains("blocking-vs-async", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(diagnosticLessons, t => t.PhaseMetadata?.Contains("deadlock", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(diagnosticLessons, t => t.PhaseMetadata?.Contains("Task.Wait/Result", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(diagnosticLessons, t => t.PhaseMetadata?.Contains("code-reading", StringComparison.OrdinalIgnoreCase) == true);
        Assert.All(result.Topics, topic => Assert.StartsWith("Plan:", topic.Category));
        Assert.All(diagnosticLessons, topic => Assert.Contains("diagnosticWeakConcepts", topic.PhaseMetadata));
        Assert.Contains(diagnosticLessons, t => t.Title.Contains("Blocking vs Async", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DiagnosticFinalize_DifferentProfilesProduceDifferentTraceablePlanOutput()
    {
        var profileA = await RunProfileAsync(
            ["blocking-vs-async", "deadlock", "Task.Wait/Result", "code-reading"],
            ["Procedural", "Reading", "MisreadQuestion"]);
        var profileB = await RunProfileAsync(
            ["cancellation", "error-handling", "async-void", "task-model"],
            ["Conceptual", "Application", "Careless"]);

        Assert.NotEqual(profileA.TraceMetadata, profileB.TraceMetadata);
        Assert.NotEqual(profileA.TraceTitles, profileB.TraceTitles);
        Assert.Contains("blocking-vs-async", profileA.TraceMetadata);
        Assert.Contains("cancellation", profileB.TraceMetadata);
        Assert.DoesNotContain("cancellation", profileA.TraceMetadata);
        Assert.DoesNotContain("blocking-vs-async", profileB.TraceMetadata);
        Assert.Contains("Procedural", profileA.TraceMetadata);
        Assert.Contains("Conceptual", profileB.TraceMetadata);
    }

    private static async Task<TraceProfile> RunProfileAsync(string[] concepts, string[] mistakes)
    {
        var harness = await CreateHarnessAsync();
        var result = await harness.Agent.GenerateAndSaveDeepPlanFromDiagnosticAsync(
            harness.TopicId,
            "Korteks Runtime Proof",
            harness.UserId,
            "[SIKISTIRILMIS PLAN ARASTIRMA BAGLAMI]\nsame stored context",
            Summary(concepts, mistakes),
            "intermediate");

        var traced = result.Topics
            .Where(t => t.PhaseMetadata?.Contains("PlanDiagnostic", StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(t => t.Order)
            .ToList();

        return new TraceProfile(
            "",
            string.Join("|", traced.Select(t => t.Title)),
            string.Join("|", traced.Select(t => t.PhaseMetadata)));
    }

    private static string Summary(string[] concepts, string[] mistakes) =>
        string.Join('\n',
        [
            "[PLAN DIAGNOSTIC QUIZ SUMMARY]",
            $"QuizRunId: {Guid.NewGuid()}",
            "Answered: 20",
            "Correct: 0",
            "Wrong: 20",
            $"WeakConcepts: {string.Join(" | ", concepts.Select(c => $"{c}: 5"))}",
            $"MistakePatterns: {string.Join(" | ", mistakes.Select((m, i) => $"{m}: {(i == 2 ? 6 : 7)}"))}"
        ]);

    private static async Task<Harness> CreateHarnessAsync()
    {
        var options = new DbContextOptionsBuilder<OrkaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new OrkaDbContext(options);
        var userId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        db.Topics.Add(new Topic
        {
            Id = topicId,
            UserId = userId,
            Title = "Korteks Runtime Proof",
            Category = "Programming",
            LanguageLevel = "intermediate",
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton(db);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var korteks = new FakeKorteksAgent();
        var agent = new DeepPlanAgent(
            new GenericModuleFactory(),
            scopeFactory,
            new FakeSupervisor(),
            new FakeGrader(),
            korteks,
            new FakeCompressor(),
            new EmptyAdaptiveBuilder(),
            provider,
            NullLogger<DeepPlanAgent>.Instance);

        return new Harness(userId, topicId, db, korteks, agent);
    }

    private sealed record Harness(
        Guid UserId,
        Guid TopicId,
        OrkaDbContext Db,
        FakeKorteksAgent Korteks,
        DeepPlanAgent Agent);

    private sealed record TraceProfile(string TraceSkillTags, string TraceTitles, string TraceMetadata);

    private sealed class GenericModuleFactory : IAIAgentFactory
    {
        public string GetModel(AgentRole role) => "fake";
        public string GetProvider(AgentRole role) => "fake";
        public Task<string> CompleteChatAsync(AgentRole role, string systemPrompt, string userMessage, CancellationToken ct = default) =>
            Task.FromResult("""
            {
              "modules": [
                {
                  "title": "Generic Parent",
                  "emoji": "📘",
                  "lessons": [
                    { "title": "Generic Lesson", "skillTag": "generic", "intent": "Core" }
                  ]
                },
                {
                  "title": "Generic Practice",
                  "emoji": "🧪",
                  "lessons": [
                    { "title": "Generic Practice", "skillTag": "generic-practice", "intent": "PracticeLab" }
                  ]
                }
              ]
            }
            """);
        public async IAsyncEnumerable<string> StreamChatAsync(AgentRole role, string systemPrompt, string userMessage, CancellationToken ct = default)
        {
            yield return "fake";
            await Task.CompletedTask;
        }
        public Task<string> CompleteChatWithHistoryAsync(AgentRole role, string systemPrompt, IEnumerable<(string Role, string Content)> messages, CancellationToken ct = default) =>
            Task.FromResult("fake");
    }

    private sealed class FakeSupervisor : ISupervisorAgent
    {
        public Task<string> ClassifyIntentAsync(string userMessage, CancellationToken ct = default) => Task.FromResult("IT_SOFTWARE");
        public Task<string> DetermineActionRouteAsync(string userMessage, IEnumerable<Message>? recentMessages = null, CancellationToken ct = default) => Task.FromResult("plan");
        public Task<OrkaDecision> DetermineDecisionAsync(string userMessage, IEnumerable<Message>? recentMessages = null, CancellationToken ct = default) =>
            Task.FromResult(OrkaDecision.Continue("fake"));
    }

    private sealed class FakeGrader : IGraderAgent
    {
        public Task<bool> IsContextRelevantAsync(string topic, string retrievedContext, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> EvaluateAnswerAsync(string question, string answer, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class EmptyAdaptiveBuilder : IAdaptiveLearningContextBuilder
    {
        public Task<AdaptiveLearningContext> BuildAsync(Guid userId, Guid? topicId, string topicTitle, string userLevel = "Bilinmiyor", CancellationToken ct = default) =>
            Task.FromResult(new AdaptiveLearningContext(topicId, topicTitle, userLevel, [], [], [], null, [], null, null, DateTime.UtcNow));
    }

    private sealed class FakeKorteksAgent : IKorteksAgent
    {
        public int CallCount { get; private set; }
        public async IAsyncEnumerable<string> RunResearchAsync(string topic, Guid userId, Guid? topicId = null, string? fileContext = null, CancellationToken ct = default)
        {
            yield return "legacy";
            await Task.CompletedTask;
        }
        public Task<KorteksResearchResultDto> RunResearchWithEvidenceAsync(string topic, Guid userId, Guid? topicId = null, string? fileContext = null, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(new KorteksResearchResultDto { Topic = topic, TopicId = topicId });
        }
    }

    private sealed class FakeCompressor : IPlanResearchCompressor
    {
        public CompressedPlanResearchContextDto Compress(KorteksResearchResultDto researchResult, PlanResearchCompressionOptions? options = null) =>
            new() { Topic = researchResult.Topic, GroundingMode = GroundingMode.SourceGrounded, SourceCount = 1 };
        public string BuildPromptBlock(CompressedPlanResearchContextDto context, PlanResearchCompressionOptions? options = null) =>
            "[SIKISTIRILMIS PLAN ARASTIRMA BAGLAMI]\nsame stored context";
    }
}
