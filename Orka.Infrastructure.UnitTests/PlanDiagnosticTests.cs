using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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
    public async Task PlanDiagnostic_Start_RequiresApprovedIntentBeforeLearningResearch()
    {
        var harness = await CreateHarnessAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.StartAsync(harness.UserId, new StartPlanDiagnosticRequest { TopicId = harness.TopicId }));

        Assert.Equal(0, harness.Korteks.CallCount);
    }

    [Fact]
    public async Task PlanDiagnostic_Start_CreatesStateWithCompressedContext()
    {
        var harness = await CreateHarnessAsync();

        var response = await harness.Service.StartAsync(harness.UserId, StartRequest(harness.TopicId));
        var state = await harness.Store.GetAsync(response.PlanRequestId);

        Assert.NotEqual(Guid.Empty, response.PlanRequestId);
        Assert.NotNull(state);
        Assert.False(string.IsNullOrWhiteSpace(state!.CompressedResearchContextJson));
        Assert.False(string.IsNullOrWhiteSpace(state.LearningBlueprintJson));
        Assert.False(string.IsNullOrWhiteSpace(state.LearningBlueprintHash));
        Assert.False(string.IsNullOrWhiteSpace(state.LearningBlueprintDomain));
        Assert.Equal(PlanDiagnosticStatus.QuizPending, state.Status);
        Assert.Equal(GroundingMode.FallbackInternalKnowledge, state.GroundingMode);
        Assert.Equal(0, state.SourceCount);
    }

    [Fact]
    public async Task PlanDiagnostic_Start_ForHistoryUsesBlueprintAndNoProgrammingScaffold()
    {
        var harness = await CreateHarnessAsync();
        harness.Factory.ReturnInvalidQuiz = true;

        var response = await harness.Service.StartAsync(
            harness.UserId,
            new StartPlanDiagnosticRequest
            {
                TopicId = harness.TopicId,
                TopicTitle = "Selcuklu tarihi: tarih",
                RawStudyRequest = "Selcuklu tarihi calismak istiyorum",
                ApprovedMainTopic = "Selcuklu tarihi",
                ApprovedFocusArea = "tarih",
                ApprovedStudyGoal = "ogrenme ve analiz",
                ApprovedResearchIntent = "Seljuk Empire history learning path"
            });
        var state = await harness.Store.GetAsync(response.PlanRequestId);

        Assert.Equal("history", state!.LearningBlueprintDomain);
        Assert.Contains("BlueprintLearningRoute", state.CompressedResearchPromptBlock);
        Assert.Contains("Dandanakan", response.QuestionsJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Malazgirt", response.QuestionsJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nizam", response.QuestionsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("debugging", response.QuestionsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api-shape", response.QuestionsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Orka IDE", response.QuestionsJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlanDiagnostic_Start_GeneratesQuizFromStoredContext()
    {
        var harness = await CreateHarnessAsync();

        var response = await harness.Service.StartAsync(harness.UserId, StartRequest(harness.TopicId));

        Assert.Equal(0, harness.Korteks.CallCount);
        Assert.Equal(1, harness.Compressor.CompressCount);
        Assert.Equal(1, harness.Compressor.BuildPromptBlockCount);
        Assert.Contains("PLAN INTELLIGENCE BRIEF", harness.Factory.LastSystemPrompt);
        Assert.DoesNotContain("stored compressed context", harness.Factory.LastSystemPrompt);
        Assert.Contains(response.QuizRunId.ToString(), (await harness.Store.GetAsync(response.PlanRequestId))!.QuizRunId.ToString());
    }

    [Fact]
    public async Task PlanDiagnostic_Start_UsesDomainAwareFallbackWhenQuizProviderFails()
    {
        var harness = await CreateHarnessAsync();
        harness.Factory.ReturnInvalidQuiz = true;

        var response = await harness.Service.StartAsync(
            harness.UserId,
            new StartPlanDiagnosticRequest
            {
                TopicId = harness.TopicId,
                TopicTitle = "Java programlama: algoritmalar",
                RawStudyRequest = "java programlamada algoritmalar calismak istiyorum",
                ApprovedMainTopic = "Java programlama",
                ApprovedFocusArea = "algoritmalar",
                ApprovedStudyGoal = "ogrenme ve pratik",
                ApprovedResearchIntent = "Java programming algorithms learning path"
            });

        Assert.Equal(20, response.QuizQuestionCount);
        Assert.Equal(20, DiagnosticQuizQualityGate.CountQuestions(response.QuestionsJson));
        Assert.Contains("```java", response.QuestionsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("```csharp", response.QuestionsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Visual Studio", response.QuestionsJson, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(
        "java programlamada algoritmalar ve veri yapilari calismak istiyorum",
        "Java programlama",
        "algoritmalar ve veri yapilari",
        "Java programming algorithms and data structures learning path",
        25)]
    [InlineData(
        "KPSS paragraf sorularinda hizlanmak istiyorum",
        "KPSS",
        "paragraf sorularinda hizlanmak",
        "KPSS exam paragraph questions speed practice learning path",
        20)]
    [InlineData(
        "SQL veritabani indeksleri ve sorgu optimizasyonu calismak istiyorum",
        "SQL programlama",
        "index ve sorgu optimizasyonu",
        "SQL programming index and query optimization learning path",
        24)]
    public async Task LifeTest_IntentLearningResearchQuizPlanPipeline_UsesApprovedIntentOnly(
        string rawRequest,
        string expectedMainTopic,
        string expectedFocus,
        string expectedResearchIntent,
        int expectedQuestionCount)
    {
        var harness = await CreateHarnessAsync();
        var analyzer = new StudyIntentAnalyzer(new ThrowingIntentAgentFactory(), NullLogger<StudyIntentAnalyzer>.Instance);

        var intent = await analyzer.AnalyzeAsync(
            harness.UserId,
            new AnalyzeStudyIntentRequest { RawRequest = rawRequest });

        Assert.Equal(expectedMainTopic, intent.MainTopic);
        Assert.Equal(expectedFocus, intent.FocusArea);
        Assert.Equal(expectedResearchIntent, intent.ResearchIntent);
        Assert.True(intent.RequiresUserConfirmation);

        var start = await harness.Service.StartAsync(harness.UserId, new StartPlanDiagnosticRequest
        {
            TopicId = harness.TopicId,
            TopicTitle = $"{intent.MainTopic}: {intent.FocusArea}",
            RawStudyRequest = rawRequest,
            IntentRequestId = intent.IntentRequestId,
            ApprovedMainTopic = intent.MainTopic,
            ApprovedFocusArea = intent.FocusArea,
            ApprovedStudyGoal = intent.StudyGoal,
            ApprovedResearchIntent = intent.ResearchIntent
        });

        Assert.Equal(0, harness.Korteks.CallCount);
        Assert.Equal(intent.ResearchIntent, start.ApprovedResearchIntent);
        Assert.NotEqual(rawRequest, start.ApprovedResearchIntent);
        Assert.InRange(start.QuizQuestionCount, 15, 25);
        Assert.Equal(expectedQuestionCount, start.QuizQuestionCount);
        Assert.Equal(start.QuizQuestionCount, DiagnosticQuizQualityGate.CountQuestions(start.QuestionsJson));
        Assert.DoesNotContain("Dogru secenek", start.QuestionsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Yanlis secenek", start.QuestionsJson, StringComparison.OrdinalIgnoreCase);

        var half = start.QuizQuestionCount / 2;
        for (var i = 1; i <= start.QuizQuestionCount; i++)
        {
            await harness.Service.RecordAnswerAsync(
                harness.UserId,
                start.PlanRequestId,
                Answer($"q{i}", isCorrect: i <= half, conceptTag: i <= half ? $"known-{i}" : $"weak-{i}"));
        }

        var result = await harness.Service.FinalizeAsync(
            harness.UserId,
            new FinalizePlanDiagnosticRequest { PlanRequestId = start.PlanRequestId });

        Assert.True(result.PlanGenerated);
        Assert.Contains("AccuracyPercent:", harness.DeepPlan.LastDiagnosticQuizSummary);
        Assert.Contains("MeasuredLevel:", harness.DeepPlan.LastDiagnosticQuizSummary);
        Assert.Contains("KnownConcepts:", harness.DeepPlan.LastDiagnosticQuizSummary);
        Assert.Contains("WeakConcepts:", harness.DeepPlan.LastDiagnosticQuizSummary);
        Assert.Contains("weak-", harness.DeepPlan.LastDiagnosticQuizSummary);
    }

    [Fact]
    public async Task PlanDiagnostic_Start_ReturnsPlanRequestIdAndQuizRunId()
    {
        var harness = await CreateHarnessAsync();

        var response = await harness.Service.StartAsync(harness.UserId, StartRequest(harness.TopicId));

        Assert.NotEqual(Guid.Empty, response.PlanRequestId);
        Assert.NotEqual(Guid.Empty, response.QuizRunId);
        Assert.Contains("question", response.QuestionsJson);
        Assert.Equal(20, response.QuizQuestionCount);
    }

    [Fact]
    public async Task PlanDiagnostic_Start_NormalizesRawStudyRequestTitleFromApprovedIntent()
    {
        var harness = await CreateHarnessAsync();
        var request = StartRequest(harness.TopicId);
        request.TopicTitle = "java programlamada algoritmalar calismak istiyorum";
        request.ApprovedMainTopic = "Java programlama";
        request.ApprovedFocusArea = "algoritmalar";

        var response = await harness.Service.StartAsync(harness.UserId, request);

        Assert.Equal("Java programlama: algoritmalar", response.TopicTitle);
        Assert.DoesNotContain("calismak istiyorum", response.QuestionsJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Java programlama: algoritmalar", response.QuestionsJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlanDiagnostic_RecordAnswer_UpdatesAnsweredCount()
    {
        var harness = await CreateHarnessAsync();
        var start = await harness.Service.StartAsync(harness.UserId, StartRequest(harness.TopicId));

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
        var start = await harness.Service.StartAsync(harness.UserId, StartRequest(harness.TopicId));

        var result = await harness.Service.FinalizeAsync(harness.UserId, new FinalizePlanDiagnosticRequest { PlanRequestId = start.PlanRequestId });

        Assert.False(result.PlanGenerated);
        Assert.Equal(0, harness.DeepPlan.FinalizeCallCount);
        Assert.Contains("incomplete", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlanDiagnostic_Finalize_UsesStoredContextAndCurrentQuizRunSummary()
    {
        var harness = await CreateHarnessAsync();
        var start = await harness.Service.StartAsync(harness.UserId, StartRequest(harness.TopicId));
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
        Assert.Equal(0, harness.Korteks.CallCount);
        Assert.Equal(1, harness.Compressor.CompressCount);
    }

    [Fact]
    public async Task PlanDiagnostic_Finalize_MarksPlanGenerated()
    {
        var harness = await CreateHarnessAsync();
        var start = await harness.Service.StartAsync(harness.UserId, StartRequest(harness.TopicId));
        await CompleteQuizAsync(harness, start.PlanRequestId);

        var result = await harness.Service.FinalizeAsync(harness.UserId, new FinalizePlanDiagnosticRequest { PlanRequestId = start.PlanRequestId });
        var state = await harness.Store.GetAsync(start.PlanRequestId);

        Assert.Equal(PlanDiagnosticStatus.PlanGenerated, result.Status);
        Assert.Equal(PlanDiagnosticStatus.PlanGenerated, state!.Status);
        Assert.Equal(harness.TopicId, state.GeneratedPlanRootTopicId);
    }

    [Fact]
    public async Task PlanDiagnostic_Skip_GeneratesBeginnerPlanWithoutFakeAttempts()
    {
        var harness = await CreateHarnessAsync();
        var start = await harness.Service.StartAsync(harness.UserId, StartRequest(harness.TopicId));

        var result = await harness.Service.SkipAndGenerateAsync(harness.UserId, start.PlanRequestId);
        var attemptCount = await harness.Db.QuizAttempts.CountAsync(a => a.UserId == harness.UserId && a.QuizRunId == start.QuizRunId);

        Assert.True(result.PlanGenerated);
        Assert.Equal(PlanDiagnosticStatus.PlanGenerated, result.Status);
        Assert.Equal(0, attemptCount);
        Assert.Contains("StartFromZero", harness.DeepPlan.LastDiagnosticQuizSummary);
        Assert.Contains("do not infer weak skills", harness.DeepPlan.LastDiagnosticQuizSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlanDiagnostic_Finalize_IsIdempotentAfterPlanGenerated()
    {
        var harness = await CreateHarnessAsync();
        var start = await harness.Service.StartAsync(harness.UserId, StartRequest(harness.TopicId));
        await CompleteQuizAsync(harness, start.PlanRequestId);

        var first = await harness.Service.FinalizeAsync(harness.UserId, new FinalizePlanDiagnosticRequest { PlanRequestId = start.PlanRequestId });
        var second = await harness.Service.FinalizeAsync(harness.UserId, new FinalizePlanDiagnosticRequest { PlanRequestId = start.PlanRequestId });

        Assert.True(first.PlanGenerated);
        Assert.True(second.PlanGenerated);
        Assert.Equal(PlanDiagnosticStatus.PlanGenerated, second.Status);
        Assert.Equal(1, harness.DeepPlan.FinalizeCallCount);
        Assert.Contains("already generated", second.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlanDiagnostic_Skip_IsIdempotentAfterPlanGenerated()
    {
        var harness = await CreateHarnessAsync();
        var start = await harness.Service.StartAsync(harness.UserId, StartRequest(harness.TopicId));

        var first = await harness.Service.SkipAndGenerateAsync(harness.UserId, start.PlanRequestId);
        var second = await harness.Service.SkipAndGenerateAsync(harness.UserId, start.PlanRequestId);

        Assert.True(first.PlanGenerated);
        Assert.True(second.PlanGenerated);
        Assert.Equal(PlanDiagnosticStatus.PlanGenerated, second.Status);
        Assert.Equal(1, harness.DeepPlan.FinalizeCallCount);
        Assert.Contains("already generated", second.Message, StringComparison.OrdinalIgnoreCase);
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

        var start = await harness.Service.StartAsync(harness.UserId, StartRequest(harness.TopicId));
        await CompleteQuizAsync(harness, start.PlanRequestId);
        await harness.Service.FinalizeAsync(harness.UserId, new FinalizePlanDiagnosticRequest { PlanRequestId = start.PlanRequestId });

        var unchanged = await harness.Db.Topics.FindAsync(existing.Id);
        Assert.Equal(originalCategory, unchanged!.Category);
    }

    [Fact]
    public async Task PlanDiagnostic_DoesNotChangeTopicCategoryBehavior()
    {
        var harness = await CreateHarnessAsync();
        var start = await harness.Service.StartAsync(harness.UserId, StartRequest(harness.TopicId));
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
            LanguageLevel = "BaÅŸlangÄ±Ã§",
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
            null,
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
            SourceRefsJson = isCorrect || string.IsNullOrWhiteSpace(conceptTag)
                ? null
                : $$"""{"conceptTag":"{{conceptTag}}","learningObjective":"{{conceptTag}}","mistakeCategory":"Conceptual"}"""
        };

    private static StartPlanDiagnosticRequest StartRequest(Guid topicId) =>
        new()
        {
            TopicId = topicId,
            TopicTitle = "C#: async await",
            RawStudyRequest = "C# async await calismak istiyorum",
            ApprovedMainTopic = "C# programming",
            ApprovedFocusArea = "async await",
            ApprovedStudyGoal = "ogrenme ve pratik",
            ApprovedResearchIntent = "C# programming async await learning path"
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
        public string LastTopic { get; private set; } = string.Empty;
        public async IAsyncEnumerable<string> RunResearchAsync(string topic, Guid userId, Guid? topicId = null, string? fileContext = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return "legacy";
            await Task.CompletedTask;
        }
        public Task<KorteksResearchResultDto> RunResearchWithEvidenceAsync(string topic, Guid userId, Guid? topicId = null, string? fileContext = null, CancellationToken ct = default)
        {
            CallCount++;
            LastTopic = topic;
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
        public bool ReturnInvalidQuiz { get; set; }
        public string LastSystemPrompt { get; private set; } = "";
        public string GetModel(AgentRole role) => "fake";
        public string GetProvider(AgentRole role) => "fake";
        public Task<string> CompleteChatAsync(AgentRole role, string systemPrompt, string userMessage, CancellationToken ct = default)
        {
            LastSystemPrompt = systemPrompt;
            if (ReturnInvalidQuiz)
            {
                return Task.FromResult("Z provider unavailable");
            }

            var topic = ExtractQuotedTopic(userMessage) ?? "C# async await";
            var count = ExtractRequestedCount(systemPrompt) ?? ExtractRequestedCount(userMessage) ?? 20;
            return Task.FromResult(BuildValidQuiz(topic, count));
        }
        public async IAsyncEnumerable<string> StreamChatAsync(AgentRole role, string systemPrompt, string userMessage, [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return "fake";
            await Task.CompletedTask;
        }
        public Task<string> CompleteChatWithHistoryAsync(AgentRole role, string systemPrompt, IEnumerable<(string Role, string Content)> messages, CancellationToken ct = default) =>
            Task.FromResult("fake");
    }

    private sealed class ThrowingIntentAgentFactory : IAIAgentFactory
    {
        public string GetModel(AgentRole role) => "throwing";
        public string GetProvider(AgentRole role) => "throwing";
        public Task<string> CompleteChatAsync(AgentRole role, string systemPrompt, string userMessage, CancellationToken ct = default) =>
            throw new InvalidOperationException("Intent lifetest uses deterministic fallback.");
        public async IAsyncEnumerable<string> StreamChatAsync(AgentRole role, string systemPrompt, string userMessage, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }
        public Task<string> CompleteChatWithHistoryAsync(AgentRole role, string systemPrompt, IEnumerable<(string Role, string Content)> messages, CancellationToken ct = default) =>
            throw new InvalidOperationException("Intent lifetest uses deterministic fallback.");
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

    private static string? ExtractQuotedTopic(string text)
    {
        var match = Regex.Match(text, "Konu:\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static int? ExtractRequestedCount(string text)
    {
        var match = Regex.Match(text, @"(?:tam olarak|tam)\s+(\d+)\s+(?:adet\s+)?(?:soru|tanilayici)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var count) ? count : null;
    }

    private static string BuildCodeSnippet(string topic)
    {
        if (topic.Contains("Java", StringComparison.OrdinalIgnoreCase))
        {
            return """
                ```java
                int[] values = {5, 2, 8};
                Arrays.sort(values);
                System.out.println(values[0]);
                ```
                """;
        }

        if (topic.Contains("SQL", StringComparison.OrdinalIgnoreCase))
        {
            return """
                ```sql
                SELECT * FROM Orders WHERE CustomerId = 42 ORDER BY CreatedAt DESC;
                ```
                """;
        }

        if (topic.Contains("KPSS", StringComparison.OrdinalIgnoreCase))
        {
            return """
                Paragraf: Bir metinde ana düşünce, yazarın bütün cümleleri bağladığı temel yargıdır.
                """;
        }

        return """
            ```csharp
            var result = await LoadAsync();
            Console.WriteLine(result.Count);
            ```
            """;
    }

    private static string BuildValidQuiz(string topic, int count)
    {
        var types = new[] { "conceptual", "procedural", "application", "analysis", "misconception_probe" };
        var difficulties = new[] { "kolay", "orta", "zor" };
        var questions = Enumerable.Range(1, count).Select(i =>
        {
            var code = i % 4 == 0 ? $"\n{BuildCodeSnippet(topic)}" : "";
            return new
            {
                type = "multiple_choice",
                question = $"{topic} seviye sorusu {i}: bu konuda hangi karar veya risk once incelenmelidir?{code}",
                options = new[]
                {
                    new { text = "Kavrami, senaryoyu ve beklenen sonucu birlikte kontrol etmek.", isCorrect = true },
                    new { text = "Metni okumadan ilk gorunen satiri silmek.", isCorrect = false },
                    new { text = "Kavram yerine benzer gorunen terimi secmek.", isCorrect = false },
                    new { text = "Sonucu yalnizca ilk kelimeye bakarak tahmin etmek.", isCorrect = false }
                },
                correctAnswer = "Kavrami, senaryoyu ve beklenen sonucu birlikte kontrol etmek.",
                explanation = $"Aciklama {i}",
                skillTag = $"skill-{i}",
                difficulty = difficulties[(i - 1) % difficulties.Length],
                conceptTag = $"concept-{i}",
                learningObjective = $"Hedef {i}",
                questionType = types[(i - 1) % types.Length],
                expectedMisconceptionCategory = i <= 5 ? "Conceptual" : "Careless",
                topic
            };
        });

        return System.Text.Json.JsonSerializer.Serialize(questions, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    }
}

