using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Orka.Core.DTOs;
using Orka.Core.DTOs.Korteks;
using Orka.Core.DTOs.PlanDiagnostic;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public sealed class PlanDiagnosticTests
{
    [Fact]
    public async Task PlanDiagnostic_Start_CreatesStateWithCompressedContext()
    {
        var harness = await CreateHarnessAsync();

        var response = await harness.Service.StartAsync(harness.UserId, new StartPlanDiagnosticRequest { TopicId = harness.TopicId });
        var state = await harness.Store.GetAsync(response.PlanRequestId);

        Assert.NotEqual(Guid.Empty, response.PlanRequestId);
        Assert.NotNull(state);
        Assert.False(string.IsNullOrWhiteSpace(state!.CompressedResearchContextJson));
        Assert.Equal(PlanDiagnosticStatus.QuizPending, state.Status);
        Assert.Equal(GroundingMode.SourceGrounded, state.GroundingMode);
        Assert.Equal(2, state.SourceCount);
    }

    [Fact]
    public async Task PlanDiagnostic_Start_GeneratesQuizFromStoredContext()
    {
        var harness = await CreateHarnessAsync();

        var response = await harness.Service.StartAsync(harness.UserId, new StartPlanDiagnosticRequest { TopicId = harness.TopicId });

        Assert.Equal(1, harness.Korteks.CallCount);
        Assert.Equal(1, harness.Compressor.CompressCount);
        Assert.Equal(1, harness.Compressor.BuildPromptBlockCount);
        Assert.Contains("stored compressed context", harness.Factory.LastSystemPrompt);
        Assert.Contains(response.QuizRunId.ToString(), (await harness.Store.GetAsync(response.PlanRequestId))!.QuizRunId.ToString());
    }

    [Fact]
    public async Task PlanDiagnostic_Start_ReturnsPlanRequestIdAndQuizRunId()
    {
        var harness = await CreateHarnessAsync();

        var response = await harness.Service.StartAsync(harness.UserId, new StartPlanDiagnosticRequest { TopicId = harness.TopicId });

        Assert.NotEqual(Guid.Empty, response.PlanRequestId);
        Assert.NotEqual(Guid.Empty, response.QuizRunId);
        Assert.Contains("question", response.QuestionsJson);
        Assert.Equal(20, response.QuizQuestionCount);
    }

    [Fact]
    public async Task PlanDiagnostic_RecordAnswer_UpdatesAnsweredCount()
    {
        var harness = await CreateHarnessAsync();
        var start = await harness.Service.StartAsync(harness.UserId, new StartPlanDiagnosticRequest { TopicId = harness.TopicId });

        var first = await harness.Service.RecordAnswerAsync(harness.UserId, start.PlanRequestId, Answer("q1"));
        PlanDiagnosticAnswerResponse second = first;
        for (var i = 2; i <= 20; i++)
        {
            second = await harness.Service.RecordAnswerAsync(harness.UserId, start.PlanRequestId, Answer($"q{i}"));
        }

        Assert.Equal(1, first.AnsweredQuestionCount);
        Assert.Equal(PlanDiagnosticStatus.QuizCompleted, second.Status);
        Assert.Equal(20, second.AnsweredQuestionCount);
    }

    [Fact]
    public async Task PlanDiagnostic_Finalize_RefusesWhenQuizIncomplete()
    {
        var harness = await CreateHarnessAsync();
        var start = await harness.Service.StartAsync(harness.UserId, new StartPlanDiagnosticRequest { TopicId = harness.TopicId });

        var result = await harness.Service.FinalizeAsync(harness.UserId, new FinalizePlanDiagnosticRequest { PlanRequestId = start.PlanRequestId });

        Assert.False(result.PlanGenerated);
        Assert.Equal(0, harness.DeepPlan.FinalizeCallCount);
        Assert.Contains("incomplete", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlanDiagnostic_Finalize_UsesStoredContextAndCurrentQuizRunSummary()
    {
        var harness = await CreateHarnessAsync();
        var start = await harness.Service.StartAsync(harness.UserId, new StartPlanDiagnosticRequest { TopicId = harness.TopicId });
        await harness.Service.RecordAnswerAsync(harness.UserId, start.PlanRequestId, Answer("q1", isCorrect: false, conceptTag: "variables"));
        for (var i = 2; i <= 20; i++)
        {
            await harness.Service.RecordAnswerAsync(harness.UserId, start.PlanRequestId, Answer($"q{i}", isCorrect: true));
        }

        var result = await harness.Service.FinalizeAsync(harness.UserId, new FinalizePlanDiagnosticRequest { PlanRequestId = start.PlanRequestId });

        Assert.True(result.PlanGenerated);
        Assert.Equal(1, harness.DeepPlan.FinalizeCallCount);
        Assert.Contains("stored compressed context", harness.DeepPlan.LastCompressedResearchPromptBlock);
        Assert.Contains(start.QuizRunId.ToString(), harness.DeepPlan.LastDiagnosticQuizSummary);
        Assert.Contains("variables", harness.DeepPlan.LastDiagnosticQuizSummary);
        Assert.Equal(1, harness.Korteks.CallCount);
        Assert.Equal(1, harness.Compressor.CompressCount);
    }

    [Fact]
    public async Task PlanDiagnostic_Finalize_MarksPlanGenerated()
    {
        var harness = await CreateHarnessAsync();
        var start = await harness.Service.StartAsync(harness.UserId, new StartPlanDiagnosticRequest { TopicId = harness.TopicId });
        await CompleteQuizAsync(harness, start.PlanRequestId);

        var result = await harness.Service.FinalizeAsync(harness.UserId, new FinalizePlanDiagnosticRequest { PlanRequestId = start.PlanRequestId });
        var state = await harness.Store.GetAsync(start.PlanRequestId);

        Assert.Equal(PlanDiagnosticStatus.PlanGenerated, result.Status);
        Assert.Equal(PlanDiagnosticStatus.PlanGenerated, state!.Status);
        Assert.Equal(harness.TopicId, state.GeneratedPlanRootTopicId);
    }

    [Fact]
    public async Task PlanDiagnostic_DoesNotMutateExistingPlans()
    {
        var harness = await CreateHarnessAsync();
        var existing = new Topic
        {
            Id = Guid.NewGuid(),
            UserId = harness.UserId,
            ParentTopicId = harness.TopicId,
            Title = "Existing plan lesson",
            Category = "Plan:Core",
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow
        };
        harness.Db.Topics.Add(existing);
        await harness.Db.SaveChangesAsync();
        var originalCategory = existing.Category;

        var start = await harness.Service.StartAsync(harness.UserId, new StartPlanDiagnosticRequest { TopicId = harness.TopicId });
        await CompleteQuizAsync(harness, start.PlanRequestId);
        await harness.Service.FinalizeAsync(harness.UserId, new FinalizePlanDiagnosticRequest { PlanRequestId = start.PlanRequestId });

        var unchanged = await harness.Db.Topics.FindAsync(existing.Id);
        Assert.Equal(originalCategory, unchanged!.Category);
    }

    [Fact]
    public async Task PlanDiagnostic_DoesNotChangeTopicCategoryBehavior()
    {
        var harness = await CreateHarnessAsync();
        var start = await harness.Service.StartAsync(harness.UserId, new StartPlanDiagnosticRequest { TopicId = harness.TopicId });
        await CompleteQuizAsync(harness, start.PlanRequestId);

        await harness.Service.FinalizeAsync(harness.UserId, new FinalizePlanDiagnosticRequest { PlanRequestId = start.PlanRequestId });

        Assert.All(harness.DeepPlan.GeneratedTopics, topic => Assert.StartsWith("Plan:", topic.Category));
    }

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
            Title = "C#",
            Category = "Programming",
            LanguageLevel = "Başlangıç",
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var store = new FakePlanDiagnosticStateStore();
        var korteks = new FakeKorteksAgent();
        var compressor = new FakePlanResearchCompressor();
        var factory = new FakeAgentFactory();
        var recorder = new FakeQuizAttemptRecorder(db);
        var deepPlan = new FakeDeepPlanAgent();
        var service = new PlanDiagnosticService(
            db,
            korteks,
            compressor,
            factory,
            store,
            recorder,
            deepPlan,
            NullLogger<PlanDiagnosticService>.Instance);

        return new Harness(userId, topicId, db, store, korteks, compressor, factory, deepPlan, service);
    }

    private static RecordQuizAttemptRequest Answer(string questionId, bool isCorrect = true, string? conceptTag = null) =>
        new()
        {
            QuestionId = questionId,
            Question = $"Question {questionId}",
            SelectedOptionId = "opt-1",
            IsCorrect = isCorrect,
            SkillTag = conceptTag ?? "skill",
            Explanation = "explanation",
            SourceRefsJson = isCorrect ? null : """{"mistakeCategory":"Conceptual"}"""
        };

    private static async Task CompleteQuizAsync(Harness harness, Guid planRequestId)
    {
        for (var i = 1; i <= 20; i++)
        {
            await harness.Service.RecordAnswerAsync(harness.UserId, planRequestId, Answer($"q{i}"));
        }
    }

    private sealed record Harness(
        Guid UserId,
        Guid TopicId,
        OrkaDbContext Db,
        FakePlanDiagnosticStateStore Store,
        FakeKorteksAgent Korteks,
        FakePlanResearchCompressor Compressor,
        FakeAgentFactory Factory,
        FakeDeepPlanAgent DeepPlan,
        PlanDiagnosticService Service);

    private sealed class FakePlanDiagnosticStateStore : IPlanDiagnosticStateStore
    {
        private readonly Dictionary<Guid, PlanDiagnosticStateDto> _states = new();
        public Task<PlanDiagnosticStateDto?> GetAsync(Guid planRequestId, CancellationToken ct = default) =>
            Task.FromResult(_states.TryGetValue(planRequestId, out var state) ? state : null);
        public Task SaveAsync(PlanDiagnosticStateDto state, CancellationToken ct = default)
        {
            _states[state.PlanRequestId] = state;
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid planRequestId, CancellationToken ct = default)
        {
            _states.Remove(planRequestId);
            return Task.CompletedTask;
        }
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
            return Task.FromResult(new KorteksResearchResultDto
            {
                Topic = topic,
                TopicId = topicId,
                Report = "source grounded report",
                GroundingMode = GroundingMode.SourceGrounded,
                Sources =
                [
                    Source("WebSearch", "https://example.com/1"),
                    Source("Wikipedia", "https://example.com/2")
                ],
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    private sealed class FakePlanResearchCompressor : IPlanResearchCompressor
    {
        public int CompressCount { get; private set; }
        public int BuildPromptBlockCount { get; private set; }
        public CompressedPlanResearchContextDto Compress(KorteksResearchResultDto researchResult, PlanResearchCompressionOptions? options = null)
        {
            CompressCount++;
            return new CompressedPlanResearchContextDto
            {
                Topic = researchResult.Topic,
                GroundingMode = researchResult.GroundingMode,
                SourceCount = researchResult.SourceCount,
                TopSources = researchResult.Sources.Take(2).ToList(),
                KeyFacts = ["stored fact"],
                GeneratedAt = DateTimeOffset.UtcNow
            };
        }
        public string BuildPromptBlock(CompressedPlanResearchContextDto context, PlanResearchCompressionOptions? options = null)
        {
            BuildPromptBlockCount++;
            return "[SIKISTIRILMIS PLAN ARASTIRMA BAGLAMI]\nstored compressed context";
        }
    }

    private sealed class FakeAgentFactory : IAIAgentFactory
    {
        public string LastSystemPrompt { get; private set; } = "";
        public string GetModel(AgentRole role) => "fake";
        public string GetProvider(AgentRole role) => "fake";
        public Task<string> CompleteChatAsync(AgentRole role, string systemPrompt, string userMessage, CancellationToken ct = default)
        {
            LastSystemPrompt = systemPrompt;
            return Task.FromResult(DiagnosticQuizQualityGate.BuildFallbackDiagnosticBlueprint("C#"));
        }
        public async IAsyncEnumerable<string> StreamChatAsync(AgentRole role, string systemPrompt, string userMessage, CancellationToken ct = default)
        {
            yield return "fake";
            await Task.CompletedTask;
        }
        public Task<string> CompleteChatWithHistoryAsync(AgentRole role, string systemPrompt, IEnumerable<(string Role, string Content)> messages, CancellationToken ct = default) =>
            Task.FromResult("fake");
    }

    private sealed class FakeQuizAttemptRecorder : IQuizAttemptRecorder
    {
        private readonly OrkaDbContext _db;
        public FakeQuizAttemptRecorder(OrkaDbContext db) => _db = db;
        public async Task<QuizAttemptRecordResult> RecordAsync(Guid userId, RecordQuizAttemptRequest request, CancellationToken ct = default)
        {
            var attempt = new QuizAttempt
            {
                Id = Guid.NewGuid(),
                QuizRunId = request.QuizRunId,
                TopicId = request.TopicId,
                SessionId = request.SessionId,
                UserId = userId,
                QuestionId = request.QuestionId,
                Question = request.Question ?? "",
                UserAnswer = request.SelectedOptionId ?? "",
                IsCorrect = request.IsCorrect,
                Explanation = request.Explanation ?? "",
                SkillTag = request.SkillTag,
                SourceRefsJson = request.SourceRefsJson,
                CreatedAt = DateTime.UtcNow
            };
            _db.QuizAttempts.Add(attempt);
            await _db.SaveChangesAsync(ct);
            return new QuizAttemptRecordResult(attempt, null, null, null);
        }
    }

    private sealed class FakeDeepPlanAgent : IDeepPlanAgent
    {
        public int FinalizeCallCount { get; private set; }
        public string LastCompressedResearchPromptBlock { get; private set; } = "";
        public string LastDiagnosticQuizSummary { get; private set; } = "";
        public List<Topic> GeneratedTopics { get; } = [];
        public Task<List<Topic>> GenerateAndSaveDeepPlanAsync(Guid parentTopicId, string topicTitle, Guid userId, string userLevel = "Bilinmiyor", string? researchContext = null, string? failedTopics = null) =>
            Task.FromResult(new List<Topic>());
        public Task<DeepPlanGenerationWithGroundingResultDto> GenerateAndSaveDeepPlanWithGroundingAsync(Guid parentTopicId, string topicTitle, Guid userId, string userLevel = "Bilinmiyor", string? researchContext = null, string? failedTopics = null) =>
            Task.FromResult(new DeepPlanGenerationWithGroundingResultDto());
        public Task<DeepPlanGenerationWithGroundingResultDto> GenerateAndSaveDeepPlanFromDiagnosticAsync(Guid parentTopicId, string topicTitle, Guid userId, string compressedResearchPromptBlock, string diagnosticQuizSummary, string userLevel = "Bilinmiyor")
        {
            FinalizeCallCount++;
            LastCompressedResearchPromptBlock = compressedResearchPromptBlock;
            LastDiagnosticQuizSummary = diagnosticQuizSummary;
            GeneratedTopics.Clear();
            GeneratedTopics.Add(new Topic { Id = Guid.NewGuid(), UserId = userId, ParentTopicId = parentTopicId, Title = "Generated", Category = "Plan:Core" });
            return Task.FromResult(new DeepPlanGenerationWithGroundingResultDto { Topics = GeneratedTopics });
        }
        public Task<string> GenerateBaselineQuizAsync(string topicTitle) => Task.FromResult("[]");
    }

    private static SourceEvidenceDto Source(string provider, string url) =>
        new(provider, $"{provider}Tool", url, $"{provider} source", "snippet", null, DateTimeOffset.UtcNow, 1, "web", null, null);
}
