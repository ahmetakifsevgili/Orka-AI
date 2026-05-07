using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
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

    [Fact]
    public async Task DeepPlan_RejectsThinGeneratedPlanAndUsesProgrammingQualityFallback()
    {
        var harness = await CreateHarnessAsync();

        var result = await harness.Agent.GenerateAndSaveDeepPlanFromDiagnosticAsync(
            harness.TopicId,
            "C# Calismak",
            harness.UserId,
            "[SIKISTIRILMIS PLAN ARASTIRMA BAGLAMI]\nsame stored context",
            "[PLAN DIAGNOSTIC QUIZ SUMMARY]\nAnswered: 0\nCorrect: 0\nWrong: 0\nWeakConcepts: none\nMistakePatterns: none",
            "beginner");

        Assert.True(result.Topics.Count >= 24);
        Assert.Contains(result.Topics, t => t.Title.Contains("C#", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Topics.Take(4), t => t.Title.Contains("Orka IDE", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Topics, t => t.Title.Contains("Tanisal Iyilestirme", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Topics, t => t.Title.Contains("Generic Lesson", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeepPlan_GeneralFallbackMeetsQualityFloorWhenModelReturnsThinPlan()
    {
        var harness = await CreateHarnessAsync();

        var result = await harness.Agent.GenerateAndSaveDeepPlanFromDiagnosticAsync(
            harness.TopicId,
            "Roma tarihi",
            harness.UserId,
            "[SIKISTIRILMIS PLAN ARASTIRMA BAGLAMI]\nsame stored context",
            "[PLAN DIAGNOSTIC QUIZ SUMMARY]\nAnswered: 0\nCorrect: 0\nWrong: 0\nWeakConcepts: none\nMistakePatterns: none",
            "beginner");

        Assert.True(result.Topics.Count >= 15);
        Assert.DoesNotContain(result.Topics, t => t.Title.Contains("Generic Lesson", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeepPlan_AcceptsRichGeneratedPlanWithoutReplacingItWithFallback()
    {
        var harness = await CreateHarnessAsync(new RichProgrammingModuleFactory());

        var result = await harness.Agent.GenerateAndSaveDeepPlanFromDiagnosticAsync(
            harness.TopicId,
            "C# Calismak",
            harness.UserId,
            "[SIKISTIRILMIS PLAN ARASTIRMA BAGLAMI]\nsame stored context",
            "[PLAN DIAGNOSTIC QUIZ SUMMARY]\nAnswered: 0\nCorrect: 0\nWrong: 0\nWeakConcepts: none\nMistakePatterns: none",
            "beginner");

        Assert.True(result.Topics.Count >= 24);
        Assert.Contains(result.Topics, t => t.Title.Contains("Orka IDE sandbox ile custom setup", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Topics, t => t.Title.Contains("Orka IDE sandbox'ta ilk C# programi", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeepPlan_ProgrammingFallbackDoesNotForceCSharpForPythonTopic()
    {
        var harness = await CreateHarnessAsync();

        var result = await harness.Agent.GenerateAndSaveDeepPlanFromDiagnosticAsync(
            harness.TopicId,
            "Python calismak",
            harness.UserId,
            "[SIKISTIRILMIS PLAN ARASTIRMA BAGLAMI]\nsame stored context",
            "[PLAN DIAGNOSTIC QUIZ SUMMARY]\nAnswered: 0\nCorrect: 0\nWrong: 0\nWeakConcepts: none\nMistakePatterns: none",
            "beginner");

        Assert.True(result.Topics.Count >= 24);
        Assert.Contains(result.Topics, t => t.Title.Contains("Python", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Topics, t => t.Title.Contains("C# programi", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Topics, t => t.Title.Contains("LINQ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeepPlan_AlgorithmFallbackKeepsDomainAndMeetsQualityFloor()
    {
        var harness = await CreateHarnessAsync();

        var result = await harness.Agent.GenerateAndSaveDeepPlanFromDiagnosticAsync(
            harness.TopicId,
            "Algoritmalar ve veri yapilari",
            harness.UserId,
            "[SIKISTIRILMIS PLAN ARASTIRMA BAGLAMI]\nsame stored context",
            "[PLAN DIAGNOSTIC QUIZ SUMMARY]\nAnswered: 0\nCorrect: 0\nWrong: 0\nWeakConcepts: none\nMistakePatterns: none",
            "beginner");

        Assert.True(result.Topics.Count >= 24);
        Assert.Contains(result.Topics, t => t.Title.Contains("Two Pointers", StringComparison.OrdinalIgnoreCase) ||
                                            t.Title.Contains("Sliding Window", StringComparison.OrdinalIgnoreCase) ||
                                            t.Title.Contains("Dynamic Programming", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Topics.Take(4), t => t.Title.Contains("Orka IDE", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Topics, t => t.Title.Contains("Birinci temel kavram", StringComparison.OrdinalIgnoreCase));
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

    private static async Task<Harness> CreateHarnessAsync(IAIAgentFactory? agentFactory = null)
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
            agentFactory ?? new GenericModuleFactory(),
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
        public async IAsyncEnumerable<string> StreamChatAsync(AgentRole role, string systemPrompt, string userMessage, [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return "fake";
            await Task.CompletedTask;
        }
        public Task<string> CompleteChatWithHistoryAsync(AgentRole role, string systemPrompt, IEnumerable<(string Role, string Content)> messages, CancellationToken ct = default) =>
            Task.FromResult("fake");
    }

    private sealed class RichProgrammingModuleFactory : IAIAgentFactory
    {
        public string GetModel(AgentRole role) => "fake-rich";
        public string GetProvider(AgentRole role) => "fake";
        public Task<string> CompleteChatAsync(AgentRole role, string systemPrompt, string userMessage, CancellationToken ct = default) =>
            Task.FromResult("""
            {
              "modules": [
                {
                  "title": "Custom Orka IDE Lab 1",
                  "emoji": "💻",
                  "lessons": [
                    { "title": "Orka IDE sandbox ile custom setup", "skillTag": "custom-1", "intent": "PracticeLab" },
                    { "title": "Custom topic map", "skillTag": "custom-2", "intent": "Core" },
                    { "title": "Custom code reading", "skillTag": "custom-3", "intent": "DeepDive" },
                    { "title": "Custom first checkpoint", "skillTag": "custom-4", "intent": "Assessment" }
                  ]
                },
                {
                  "title": "Custom Runtime Lab 2",
                  "emoji": "🧪",
                  "lessons": [
                    { "title": "Custom runtime behavior", "skillTag": "custom-5", "intent": "Core" },
                    { "title": "Custom error reading", "skillTag": "custom-6", "intent": "PracticeLab" },
                    { "title": "Custom refactor", "skillTag": "custom-7", "intent": "PracticeLab" },
                    { "title": "Custom mini quiz", "skillTag": "custom-8", "intent": "Assessment" }
                  ]
                },
                {
                  "title": "Custom Flow Lab 3",
                  "emoji": "🧭",
                  "lessons": [
                    { "title": "Custom branch flow", "skillTag": "custom-9", "intent": "Core" },
                    { "title": "Custom loop flow", "skillTag": "custom-10", "intent": "PracticeLab" },
                    { "title": "Custom method flow", "skillTag": "custom-11", "intent": "Core" },
                    { "title": "Custom flow assessment", "skillTag": "custom-12", "intent": "Assessment" }
                  ]
                },
                {
                  "title": "Custom Data Lab 4",
                  "emoji": "📦",
                  "lessons": [
                    { "title": "Custom data shape", "skillTag": "custom-13", "intent": "Core" },
                    { "title": "Custom collection use", "skillTag": "custom-14", "intent": "PracticeLab" },
                    { "title": "Custom file or json path", "skillTag": "custom-15", "intent": "DeepDive" },
                    { "title": "Custom data checkpoint", "skillTag": "custom-16", "intent": "Assessment" }
                  ]
                },
                {
                  "title": "Custom Project Lab 5",
                  "emoji": "🚀",
                  "lessons": [
                    { "title": "Custom project slice", "skillTag": "custom-17", "intent": "PracticeLab" },
                    { "title": "Custom test cases", "skillTag": "custom-18", "intent": "Assessment" },
                    { "title": "Custom feedback loop", "skillTag": "custom-19", "intent": "Remediation" },
                    { "title": "Custom project review", "skillTag": "custom-20", "intent": "QuickReview" }
                  ]
                },
                {
                  "title": "Custom Mastery Lab 6",
                  "emoji": "🏁",
                  "lessons": [
                    { "title": "Custom mastery task", "skillTag": "custom-21", "intent": "Assessment" },
                    { "title": "Custom weak point replay", "skillTag": "custom-22", "intent": "Remediation" },
                    { "title": "Custom Tutor bridge", "skillTag": "custom-23", "intent": "PracticeLab" },
                    { "title": "Custom final reflection", "skillTag": "custom-24", "intent": "QuickReview" }
                  ]
                }
              ]
            }
            """);
        public async IAsyncEnumerable<string> StreamChatAsync(AgentRole role, string systemPrompt, string userMessage, [EnumeratorCancellation] CancellationToken ct = default)
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
        public async IAsyncEnumerable<string> RunResearchAsync(string topic, Guid userId, Guid? topicId = null, string? fileContext = null, [EnumeratorCancellation] CancellationToken ct = default)
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
