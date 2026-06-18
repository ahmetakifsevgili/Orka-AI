using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.RegularExpressions;
using Orka.Core.DTOs;
using Orka.Core.DTOs.Korteks;
using Orka.Core.DTOs.PlanDiagnostic;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Exceptions;
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
    public async Task PlanDiagnostic_Start_ForHistoryUsesConceptGraphAdapterAndNoProgrammingScaffold()
    {
        var harness = await CreateHarnessAsync();

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
        Assert.Contains("AdapterLearningRoute", state.CompressedResearchPromptBlock);
        Assert.Contains("[CONCEPT GRAPH]", state.CompressedResearchPromptBlock);
        Assert.Contains("assessmentItemId", response.QuestionsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dandanakan", response.QuestionsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Malazgirt", response.QuestionsJson, StringComparison.OrdinalIgnoreCase);
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
    public async Task PlanDiagnostic_Start_UsesAssessmentGrammarFallbackWhenQuizProviderFails()
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

        Assert.Equal(response.QuizQuestionCount, DiagnosticQuizQualityGate.CountQuestions(response.QuestionsJson));
        Assert.Contains("assessmentItemId", response.QuestionsJson);
        Assert.DoesNotContain("correctAnswer", response.QuestionsJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlanDiagnostic_Start_ConceptGraphUsesDomainLearningConceptsNotResearchScaffold()
    {
        var harness = await CreateHarnessAsync();

        var response = await harness.Service.StartAsync(
            harness.UserId,
            new StartPlanDiagnosticRequest
            {
                TopicId = harness.TopicId,
                TopicTitle = "SQL index and query optimization",
                RawStudyRequest = "I want to learn SQL indexing and query optimization professionally.",
                ApprovedMainTopic = "SQL",
                ApprovedFocusArea = "index and query optimization",
                ApprovedStudyGoal = "learning and practice",
                ApprovedResearchIntent = "SQL index and query optimization learning path"
            });

        Assert.Contains("index", response.QuestionsJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("query", response.QuestionsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("learning-path", response.QuestionsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("practiceorder", response.QuestionsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider-backed", response.QuestionsJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(response.QuizQuestionCount, DiagnosticQuizQualityGate.CountQuestions(response.QuestionsJson));
    }

    [Fact]
    public async Task PlanDiagnostic_Start_UsesAssessmentGrammarFallbackWhenQuizProviderThrows()
    {
        var harness = await CreateHarnessAsync();
        harness.Factory.ThrowOnQuiz = true;

        var response = await harness.Service.StartAsync(harness.UserId, StartRequest(harness.TopicId));

        Assert.Equal(response.QuizQuestionCount, DiagnosticQuizQualityGate.CountQuestions(response.QuestionsJson));
        Assert.Contains("assessmentItemId", response.QuestionsJson);
        Assert.DoesNotContain("correctAnswer", response.QuestionsJson, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("C# programlama", "temel syntax", "C# syntax single topic intro", 15)]
    [InlineData("KPSS", "paragraf sorularinda hizlanmak", "KPSS exam paragraph practice", 20)]
    [InlineData("SQL programlama", "index ve sorgu optimizasyonu", "SQL index query optimization learning path", 24)]
    [InlineData("Java programlama", "algoritmalar ve veri yapilari", "Java algorithms and data structures learning path", 25)]
    public void DiagnosticQuestionCountPolicy_UsesProductHeuristic(
        string mainTopic,
        string focusArea,
        string researchIntent,
        int expectedQuestionCount)
    {
        var method = typeof(PlanDiagnosticService).GetMethod(
            "DetermineDiagnosticQuestionCount",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var count = (int)method!.Invoke(null, [mainTopic, focusArea, researchIntent])!;

        Assert.Equal(expectedQuestionCount, count);
    }

    [Fact]
    public void BuildPlanStepsFromGeneratedTopics_PreservesMaterializedModuleOrder()
    {
        var method = typeof(PlanDiagnosticService).GetMethod(
            "BuildPlanStepsFromGeneratedTopics",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var firstModuleFirstLesson = new Topic
        {
            Id = Guid.NewGuid(),
            Title = "Module A first lesson",
            Order = 2,
            PlanIntent = "Lesson",
            MetadataJson = "{}"
        };
        var secondModuleFirstLesson = new Topic
        {
            Id = Guid.NewGuid(),
            Title = "Module B first lesson",
            Order = 1,
            PlanIntent = "Lesson",
            MetadataJson = "{}"
        };
        var state = new PlanDiagnosticStateDto
        {
            PlanRequestId = Guid.NewGuid(),
            TopicId = Guid.NewGuid(),
            TopicTitle = "Module order"
        };

        var steps = (IReadOnlyList<PlanStepContractDto>)method!.Invoke(
            null,
            [new[] { firstModuleFirstLesson, secondModuleFirstLesson }, state])!;

        Assert.Equal("Module A first lesson", steps[0].Title);
        Assert.Equal("Module B first lesson", steps[1].Title);
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
        Assert.Equal(expectedQuestionCount, start.QuizQuestionCount);
        Assert.InRange(start.QuizQuestionCount, 15, 25);
        Assert.Equal(start.QuizQuestionCount, DiagnosticQuizQualityGate.CountQuestions(start.QuestionsJson));
        Assert.DoesNotContain("Dogru secenek", start.QuestionsJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Yanlis secenek", start.QuestionsJson, StringComparison.OrdinalIgnoreCase);

        var half = start.QuizQuestionCount / 2;
        var assessmentItems = await DiagnosticItemsAsync(harness, start.PlanRequestId);
        for (var i = 1; i <= start.QuizQuestionCount; i++)
        {
            await harness.Service.RecordAnswerAsync(
                harness.UserId,
                start.PlanRequestId,
                Answer($"q{i}", isCorrect: i <= half, conceptTag: i <= half ? $"known-{i}" : $"weak-{i}", assessmentItemId: assessmentItems[i - 1].Id));
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
        Assert.InRange(response.QuizQuestionCount, 15, 25);
        Assert.Equal(response.QuizQuestionCount, DiagnosticQuizQualityGate.CountQuestions(response.QuestionsJson));
    }

    [Fact]
    public async Task PlanDiagnostic_Start_IgnoresStaleSessionIdFromAnotherTopic()
    {
        var harness = await CreateHarnessAsync();
        var otherTopicId = Guid.NewGuid();
        var staleSessionId = Guid.NewGuid();
        harness.Db.Topics.Add(new Topic
        {
            Id = otherTopicId,
            UserId = harness.UserId,
            Title = "Old topic",
            Category = "Plan",
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow
        });
        harness.Db.Sessions.Add(new Session
        {
            Id = staleSessionId,
            UserId = harness.UserId,
            TopicId = otherTopicId,
            SessionNumber = 1,
            CreatedAt = DateTime.UtcNow
        });
        await harness.Db.SaveChangesAsync();

        var request = StartRequest(harness.TopicId);
        request.SessionId = staleSessionId;
        var response = await harness.Service.StartAsync(harness.UserId, request);
        var state = await harness.Store.GetAsync(response.PlanRequestId);
        var quizRun = await harness.Db.QuizRuns.SingleAsync(q => q.Id == response.QuizRunId);

        Assert.Null(state!.SessionId);
        Assert.Null(quizRun.SessionId);
    }

    [Fact]
    public async Task PlanDiagnostic_Start_NormalizesRawStudyRequestTitleFromApprovedIntent()
    {
        var harness = await CreateHarnessAsync();
        var request = StartRequest(harness.TopicId);
        request.TopicTitle = "java programlamada algoritmalar calismak istiyorum";
        request.ApprovedMainTopic = "Java programlama";
        request.ApprovedFocusArea = "algoritmalar";
        request.ApprovedResearchIntent = "Java programming algorithms learning path";

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
        var assessmentItems = await DiagnosticItemsAsync(harness, start.PlanRequestId);

        var first = await harness.Service.RecordAnswerAsync(harness.UserId, start.PlanRequestId, Answer("q1", assessmentItemId: assessmentItems[0].Id));
        PlanDiagnosticAnswerResponse second = first;
        for (var i = 2; i <= start.QuizQuestionCount; i++)
        {
            second = await harness.Service.RecordAnswerAsync(harness.UserId, start.PlanRequestId, Answer($"q{i}", assessmentItemId: assessmentItems[i - 1].Id));
        }

        Assert.Equal(1, first.AnsweredQuestionCount);
        Assert.Equal(PlanDiagnosticStatus.QuizCompleted, second.Status);
        Assert.Equal(start.QuizQuestionCount, second.AnsweredQuestionCount);
    }

    [Fact]
    public async Task PlanDiagnostic_RecordAnswer_RequiresServerAssessmentItemId()
    {
        var harness = await CreateHarnessAsync();
        var start = await harness.Service.StartAsync(harness.UserId, StartRequest(harness.TopicId));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.RecordAnswerAsync(harness.UserId, start.PlanRequestId, Answer("client-only-q1")));

        Assert.Contains("assessmentItemId", ex.Message, StringComparison.OrdinalIgnoreCase);
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
        var assessmentItems = await DiagnosticItemsAsync(harness, start.PlanRequestId);
        await harness.Service.RecordAnswerAsync(harness.UserId, start.PlanRequestId, Answer("q1", isCorrect: false, conceptTag: "variables", assessmentItemId: assessmentItems[0].Id));
        for (var i = 2; i <= start.QuizQuestionCount; i++)
        {
            await harness.Service.RecordAnswerAsync(harness.UserId, start.PlanRequestId, Answer($"q{i}", isCorrect: true, assessmentItemId: assessmentItems[i - 1].Id));
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
        var deepPlan = new FakeDeepPlanAgent(db);
        var conceptMastery = new ConceptMasteryService(db, NullLogger<ConceptMasteryService>.Instance);
        var conceptGraph = new ConceptGraphBuilder(db, null, NullLogger<ConceptGraphBuilder>.Instance);
        var assessmentGrammar = new AssessmentGrammarEngine(db, null, NullLogger<AssessmentGrammarEngine>.Instance);
        var diagnosticProfile = new DiagnosticProfileBuilder(db, null, conceptMastery, NullLogger<DiagnosticProfileBuilder>.Instance);
        var planSequencing = new FakePlanSequencingService();
        var service = new PlanDiagnosticService(
            db,
            korteks,
            compressor,
            factory,
            store,
            recorder,
            deepPlan,
            conceptGraph,
            assessmentGrammar,
            diagnosticProfile,
            NullLogger<PlanDiagnosticService>.Instance,
            planSequencing: planSequencing);

        return new Harness(userId, topicId, db, store, korteks, compressor, factory, deepPlan, service);
    }

    private static RecordQuizAttemptRequest Answer(string questionId, bool isCorrect = true, string? conceptTag = null, Guid? assessmentItemId = null) =>
        new()
        {
            QuestionId = questionId,
            Question = $"Question {questionId}",
            SelectedOptionId = "opt-1",
            IsCorrect = isCorrect,
            SkillTag = conceptTag ?? "skill",
            AssessmentItemId = assessmentItemId,
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
        var assessmentItems = await DiagnosticItemsAsync(harness, planRequestId);
        for (var i = 1; i <= assessmentItems.Count; i++)
        {
            await harness.Service.RecordAnswerAsync(harness.UserId, planRequestId, Answer($"q{i}", assessmentItemId: assessmentItems[i - 1].Id));
        }
    }

    private static async Task<List<AssessmentItem>> DiagnosticItemsAsync(Harness harness, Guid planRequestId) =>
        await harness.Db.AssessmentItems
            .Where(item => item.UserId == harness.UserId && item.PlanRequestId == planRequestId)
            .OrderBy(item => item.Order)
            .ToListAsync();

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
                KeyFacts = BuildConceptHints(researchResult.Topic),
                CurriculumMapHints = BuildConceptHints(researchResult.Topic),
                PrerequisiteHints =
                [
                    "method calls and return values",
                    "basic control flow",
                    "exception handling"
                ],
                LikelyMisconceptions =
                [
                    "await blocks the thread",
                    "Task.Result is always safe",
                    "cancellation stops work immediately"
                ],
                GeneratedAt = DateTimeOffset.UtcNow
            };
        }

        private static List<string> BuildConceptHints(string topic)
        {
            if (topic.Contains("KPSS", StringComparison.OrdinalIgnoreCase) ||
                topic.Contains("paragraph", StringComparison.OrdinalIgnoreCase) ||
                topic.Contains("paragraf", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    "paragraph main idea identification",
                    "paragraph supporting detail evidence",
                    "paragraph inference boundary",
                    "paragraph author purpose objective",
                    "paragraph distractor elimination",
                    "question stem negative wording attention",
                    "timed paragraph reading strategy",
                    "wrong answer evidence review"
                ];
            }

            if (topic.Contains("history", StringComparison.OrdinalIgnoreCase) ||
                topic.Contains("tarih", StringComparison.OrdinalIgnoreCase) ||
                topic.Contains("Seljuk", StringComparison.OrdinalIgnoreCase) ||
                topic.Contains("Selcuk", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    "chronology evidence",
                    "empire institution roles",
                    "cause effect chain",
                    "geography context",
                    "source perspective",
                    "continuity change interpretation",
                    "comparative evidence",
                    "legacy consequence analysis"
                ];
            }

            if (topic.Contains("SQL", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    "index selectivity",
                    "composite index order",
                    "execution plan reading",
                    "cardinality estimate",
                    "query predicate shape",
                    "join strategy",
                    "covering index tradeoff",
                    "rewrite without semantic drift"
                ];
            }

            if (topic.Contains("Java", StringComparison.OrdinalIgnoreCase) ||
                topic.Contains("algorithm", StringComparison.OrdinalIgnoreCase) ||
                topic.Contains("algoritma", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    "array traversal",
                    "loop invariant",
                    "time complexity",
                    "data structure choice",
                    "recursion base case",
                    "sorting comparison",
                    "hash lookup",
                    "edge case testing"
                ];
            }

            return
            [
                "await continuation",
                "task lifecycle",
                "scheduler behavior",
                "blocking wait",
                "async exception propagation",
                "cancellation boundary",
                "concurrency limit",
                "async behavior evidence"
            ];
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
        public bool ThrowOnQuiz { get; set; }
        public string LastSystemPrompt { get; private set; } = "";
        public string GetModel(AgentRole role) => "fake";
        public string GetProvider(AgentRole role) => "fake";
        public Task<string> CompleteChatAsync(AgentRole role, string systemPrompt, string userMessage, CancellationToken ct = default)
        {
            LastSystemPrompt = systemPrompt;
            if (ThrowOnQuiz && systemPrompt.Contains("Egitim Tanilama Uzmani", StringComparison.OrdinalIgnoreCase))
            {
                throw new AiProviderCallException(
                    "fake",
                    "fake-quiz",
                    AgentRole.Quiz.ToString(),
                    AiProviderFailureKind.TransientNetwork,
                    "Diagnostic quiz provider unavailable.",
                    isRetryable: true,
                    isFallbackable: true);
            }

            if (ReturnInvalidQuiz)
            {
                return Task.FromResult("Z provider unavailable");
            }

            var topic = ExtractQuotedTopic(userMessage) ?? "C# async await";
            var promptForSpecs = userMessage.Contains("assessmentItemId=", StringComparison.OrdinalIgnoreCase)
                ? userMessage
                : systemPrompt;
            var specCount = ExtractAssessmentSpecs(promptForSpecs).Count();
            var count = specCount > 0
                ? specCount
                : ExtractRequestedCount(systemPrompt) ?? ExtractRequestedCount(userMessage) ?? 20;
            return Task.FromResult(BuildValidQuiz(topic, count, promptForSpecs));
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
                IsCorrect = request.IsCorrect ?? false,
                Explanation = request.Explanation ?? "",
                SkillTag = request.SkillTag,
                AssessmentItemId = request.AssessmentItemId,
                SourceRefsJson = request.SourceRefsJson,
                CreatedAt = DateTime.UtcNow
            };
            _db.QuizAttempts.Add(attempt);
            await _db.SaveChangesAsync(ct);
            return new QuizAttemptRecordResult(attempt, null, null, null);
        }
    }

    private sealed class FakePlanSequencingService : IPlanSequencingService
    {
        private PlanQualityEvaluationDto? _latest;

        public Task<PlanCurriculumSequenceDto> BuildPlanSequenceAsync(
            Guid userId,
            PlanQualityEvaluationRequestDto request,
            CancellationToken ct = default) =>
            Task.FromResult(BuildPlanContract(request));

        public Task<PlanQualityEvaluationDto> EvaluatePlanSequenceAsync(
            Guid userId,
            PlanQualityEvaluationRequestDto request,
            CancellationToken ct = default)
        {
            _latest = new PlanQualityEvaluationDto
            {
                SnapshotId = Guid.NewGuid(),
                TopicId = request.TopicId,
                SessionId = request.SessionId,
                PlanRequestId = request.PlanRequestId,
                ActiveLessonSnapshotId = request.ActiveLessonSnapshotId,
                StudentContextSnapshotId = request.StudentContextSnapshotId,
                QualityStatus = "ready",
                SpecificityScore = 0.92m,
                SequencingScore = 0.91m,
                EvidenceAlignmentScore = 0.86m,
                AssessmentAlignmentScore = 0.9m,
                TutorAlignmentScore = 0.9m,
                BlockingIssues = [],
                WarningIssues = [],
                PlanContract = BuildPlanContract(request),
                CoursePlanQuality = BuildCourseQuality(request),
                AdaptiveDiagnostic = new AdaptiveDiagnosticDto
                {
                    DiagnosticId = request.PlanRequestId,
                    TopicId = request.TopicId,
                    Intent = "professional_plan_unit_test",
                    Confidence = 0.86m,
                    LearnerLevel = "diagnostic_profile_available",
                    PlanReadiness = "ready",
                    NextAction = "continue_plan"
                }
            };

            return Task.FromResult(_latest);
        }

        public Task<PlanReadinessDto> GetPlanReadinessAsync(
            Guid userId,
            Guid topicId,
            Guid? sessionId = null,
            CancellationToken ct = default) =>
            Task.FromResult(new PlanReadinessDto
            {
                TopicId = topicId,
                TopicTitle = "Unit test plan",
                HasConceptGraph = true,
                HasKorteksSynthesis = false,
                HasSourceEvidence = false,
                SourceReadiness = "evidence_insufficient",
                LearnerEvidenceStatus = "diagnostic_profile",
                PlanReadinessStatus = "ready",
                RecommendedFirstAction = "continue_plan",
                LatestQualitySnapshotId = _latest?.SnapshotId,
                AdaptiveDiagnostic = new AdaptiveDiagnosticDto
                {
                    TopicId = topicId,
                    Confidence = 0.86m,
                    LearnerLevel = "diagnostic_profile_available",
                    PlanReadiness = "ready",
                    NextAction = "continue_plan"
                },
                CoursePlanQuality = BuildCourseQuality(new PlanQualityEvaluationRequestDto { TopicId = topicId }),
                Warnings = []
            });

        public Task<PlanStepContractDto> BuildPlanStepContractAsync(
            Guid userId,
            Guid topicId,
            string conceptKey,
            CancellationToken ct = default) =>
            Task.FromResult(new PlanStepContractDto
            {
                StepId = $"step-{conceptKey}",
                Title = $"Practice {conceptKey}",
                Objective = $"Apply {conceptKey} with a micro-check.",
                ConceptKey = conceptKey,
                ConceptLabel = conceptKey,
                SequenceReason = "Unit test plan step follows prerequisite order.",
                QuizHook = new PlanStepAssessmentHookDto { ConceptKey = conceptKey },
                TutorHook = new PlanStepTutorHookDto { ActiveConceptKey = conceptKey },
                SuccessCriteria = [$"Explain {conceptKey}.", $"Answer a checkpoint for {conceptKey}."]
            });

        public Task<PlanQualityEvaluationDto?> GetPlanQualitySnapshotAsync(
            Guid userId,
            Guid snapshotId,
            CancellationToken ct = default) =>
            Task.FromResult(_latest?.SnapshotId == snapshotId ? _latest : null);

        public Task<PlanQualityEvaluationDto?> GetLatestPlanQualitySnapshotAsync(
            Guid userId,
            Guid topicId,
            Guid? sessionId = null,
            CancellationToken ct = default) =>
            Task.FromResult(_latest);

        private static PlanCurriculumSequenceDto BuildPlanContract(PlanQualityEvaluationRequestDto request) =>
            new()
            {
                TopicId = request.TopicId,
                TopicTitle = request.PlanTitle ?? "Unit test plan",
                ConfidenceStatus = "ready",
                SequenceStatus = "coherent",
                SourceReadiness = "evidence_insufficient",
                Steps = request.ProposedSteps,
                CoursePlanQuality = BuildCourseQuality(request),
                AdaptiveDiagnostic = new AdaptiveDiagnosticDto
                {
                    DiagnosticId = request.PlanRequestId,
                    TopicId = request.TopicId,
                    Intent = "professional_plan_unit_test",
                    Confidence = 0.86m,
                    LearnerLevel = "diagnostic_profile_available",
                    PlanReadiness = "ready",
                    NextAction = "continue_plan"
                },
                SequencingGraph = new PlanSequencingGraphDto
                {
                    Nodes = request.ProposedSteps
                        .Select((step, index) => new PlanSequencingNodeDto
                        {
                            ConceptKey = step.ConceptKey,
                            Label = step.Title,
                            Order = index,
                            DifficultyBand = step.DifficultyBand
                        })
                        .ToArray(),
                    Edges = request.ProposedSteps
                        .Zip(request.ProposedSteps.Skip(1), (source, target) => new PlanSequencingEdgeDto
                        {
                            SourceConceptKey = source.ConceptKey,
                            TargetConceptKey = target.ConceptKey,
                            RelationType = "prerequisite",
                            Weight = 1m
                        })
                        .ToArray()
                }
            };

        private static CoursePlanQualityDto BuildCourseQuality(PlanQualityEvaluationRequestDto request) =>
            new()
            {
                ReadinessStatus = "ready",
                GoalClarity = "clear",
                LearnerLevelBasis = "diagnostic_profile",
                PrerequisiteCoverage = "covered",
                SequenceCoherence = "coherent",
                MilestoneCount = 6,
                CheckpointCoverage = 1m,
                RepairLoopCount = 1,
                AssessmentAlignment = "aligned",
                SourceEvidenceStatus = "evidence_insufficient",
                OverclaimRisk = "low",
                RecommendedNextAction = "continue_plan",
                Milestones = request.ProposedSteps
                    .Take(6)
                    .Select((step, index) => new CoursePlanMilestoneDto
                    {
                        MilestoneId = $"milestone-{index + 1}",
                        Title = step.Title,
                        Objective = step.Objective,
                        StepIds = [step.StepId],
                        Checkpoint = "micro_check",
                        EstimatedMinutes = 40,
                        Status = "planned"
                    })
                    .ToArray(),
                RepairLoops =
                [
                    new CoursePlanRepairLoopDto
                    {
                        ConceptKey = request.ProposedSteps.FirstOrDefault()?.ConceptKey ?? "unit-test-repair",
                        Label = "Unit test repair loop",
                        Trigger = "diagnostic_gap",
                        RepairMode = "guided_repair",
                        NextAction = "guided_repair_then_check"
                    }
                ],
                Warnings = []
            };
    }

        private sealed class FakeDeepPlanAgent : IDeepPlanAgent
    {
        private readonly OrkaDbContext _db;
        public int FinalizeCallCount { get; private set; }
        public string LastCompressedResearchPromptBlock { get; private set; } = "";
        public string LastDiagnosticQuizSummary { get; private set; } = "";
        public List<Topic> GeneratedTopics { get; } = [];
        public FakeDeepPlanAgent(OrkaDbContext db) => _db = db;
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
            for (var moduleIndex = 0; moduleIndex < 6; moduleIndex++)
            {
                var moduleLabel = ModuleLabel(moduleIndex);
                var module = new Topic
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    ParentTopicId = parentTopicId,
                    Title = $"{topicTitle} {moduleLabel}",
                    Category = "Plan",
                    PlanIntent = "Module",
                    Order = moduleIndex,
                    TotalSections = 4,
                    CreatedAt = DateTime.UtcNow,
                    LastAccessedAt = DateTime.UtcNow
                };
                _db.Topics.Add(module);

                for (var lessonIndex = 0; lessonIndex < 4; lessonIndex++)
                {
                    var lesson = new Topic
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        ParentTopicId = module.Id,
                        Title = $"{topicTitle} {moduleLabel} applied lesson {lessonIndex + 1}",
                        Category = "Plan:Core",
                        PlanIntent = "Core",
                        Order = lessonIndex,
                        TotalSections = 1,
                        MetadataJson = BuildLessonMetadata(topicTitle, moduleIndex, lessonIndex, moduleLabel),
                        CreatedAt = DateTime.UtcNow,
                        LastAccessedAt = DateTime.UtcNow
                    };
                    _db.Topics.Add(lesson);
                    GeneratedTopics.Add(lesson);
                }
            }
            _db.SaveChanges();
            return Task.FromResult(new DeepPlanGenerationWithGroundingResultDto { Topics = GeneratedTopics });
        }
        public Task<string> GenerateBaselineQuizAsync(string topicTitle, Guid topicId, string language, int questionCount) => Task.FromResult("[]");

        private static string ModuleLabel(int moduleIndex) =>
            moduleIndex switch
            {
                0 => "foundation and prerequisite map",
                1 => "core concepts and vocabulary",
                2 => "worked examples and application",
                3 => "misconception repair and contrast",
                4 => "mixed practice and transfer",
                _ => "mastery checkpoint and next route"
            };

        private static string BuildLessonMetadata(string topicTitle, int moduleIndex, int lessonIndex, string moduleLabel)
        {
            var conceptKey = $"topic-{moduleIndex + 1}-lesson-{lessonIndex + 1}";
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                contractVersion = "professional-plan-v1",
                source = "unit-test",
                conceptKey,
                skillTag = conceptKey,
                learningObjective = $"Learner explains and applies {topicTitle} {moduleLabel} step {lessonIndex + 1} in a short scenario.",
                sequenceReason = $"Lesson {lessonIndex + 1} follows the {topicTitle} prerequisite sequence for {moduleLabel}.",
                prerequisiteConceptKeys = lessonIndex == 0 ? Array.Empty<string>() : [$"module-{moduleIndex + 1}-lesson-{lessonIndex}"],
                quizHook = new { hookType = "retrieval_practice", conceptKey, difficultyBand = "core" },
                tutorHook = new { tutorMove = "explain_then_check", activeConceptKey = conceptKey },
                successCriteria = new[]
                {
                    $"Explain {topicTitle} {moduleLabel} in learner-safe language.",
                    $"Answer one micro-check for {topicTitle} {moduleLabel} without pre-submit answer leakage."
                }
            }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        }
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

    private static string BuildValidQuiz(string topic, int count, string systemPrompt)
    {
        var types = new[] { "conceptual", "procedural", "application", "analysis", "misconception_probe" };
        var difficulties = new[] { "kolay", "orta", "zor" };
        var specs = ExtractAssessmentSpecs(systemPrompt).ToList();
        var questions = Enumerable.Range(1, count).Select(i =>
        {
            (Guid AssessmentItemId, string AssessmentItemKey, string ConceptKey, string CognitiveSkill, string Difficulty, string MisconceptionTarget, string EvidenceExpected)? spec =
                specs.Count == 0 ? null : specs[(i - 1) % specs.Count];
            var conceptKey = spec?.ConceptKey ?? $"concept-{i}";
            var globalIndex = ExtractTrailingNumber(conceptKey) ?? i;
            var cognitiveSkill = spec?.CognitiveSkill ?? types[(i - 1) % types.Length];
            var code = i % 4 == 0 ? $"\n{BuildCodeSnippet(topic)}" : "";
            var itemKey = (spec?.AssessmentItemId.ToString("N") ?? Guid.NewGuid().ToString("N"))[..8];
            return new
            {
                type = "multiple_choice",
                assessmentItemId = spec?.AssessmentItemId.ToString() ?? Guid.NewGuid().ToString(),
                assessmentItemKey = spec?.AssessmentItemKey ?? $"test-item-{i}",
                conceptKey,
                cognitiveSkill,
                misconceptionTarget = spec?.MisconceptionTarget ?? (cognitiveSkill == "misconception_probe" ? $"specific-misconception-{i}" : "evidence-insufficient"),
                evidenceExpected = spec?.EvidenceExpected ?? $"Evidence {i}",
                scoringRule = "selected_option_exact_match",
                learningOutcomeIds = new[] { $"{conceptKey}-outcome" },
                question = $"item-{itemKey} {conceptKey} icin {topic} seviye sorusu {i}: hangi karar, kanit veya risk once incelenmelidir?{code}",
                options = BuildOptions(i, globalIndex),
                correctAnswer = "Kavrami, senaryoyu ve beklenen sonucu birlikte kontrol etmek.",
                explanation = $"Aciklama {i}",
                skillTag = conceptKey,
                difficulty = spec?.Difficulty ?? difficulties[(i - 1) % difficulties.Length],
                conceptTag = conceptKey,
                learningObjective = $"Hedef {i}",
                questionType = cognitiveSkill,
                expectedMisconceptionCategory = i <= 5 ? "Conceptual" : "Careless",
                topic
            };
        });

        return System.Text.Json.JsonSerializer.Serialize(questions, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    }

    private static object[] BuildOptions(int i, int globalIndex)
    {
        var correct = new { text = "Kavrami, senaryoyu ve beklenen sonucu birlikte kontrol etmek.", isCorrect = true, rationale = "Matches the target evidence.", misconceptionKey = "" };
        var distractors = new object[]
        {
            new { text = "Metni okumadan ilk gorunen satiri silmek.", isCorrect = false, rationale = "Signals skipping the provided evidence.", misconceptionKey = $"skip-evidence-{i}" },
            new { text = "Kavram yerine benzer gorunen terimi secmek.", isCorrect = false, rationale = "Signals nearby concept confusion.", misconceptionKey = $"nearby-concept-{i}" },
            new { text = "Sonucu yalnizca ilk kelimeye bakarak tahmin etmek.", isCorrect = false, rationale = "Signals surface clue guessing.", misconceptionKey = $"surface-clue-{i}" }
        };

        var options = new List<object>(distractors);
        options.Insert((globalIndex - 1) % 4, correct);
        return options.ToArray();
    }

    private static int? ExtractTrailingNumber(string value)
    {
        var match = Regex.Match(value, @"(\d+)$");
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : null;
    }

    private static IEnumerable<(Guid AssessmentItemId, string AssessmentItemKey, string ConceptKey, string CognitiveSkill, string Difficulty, string MisconceptionTarget, string EvidenceExpected)> ExtractAssessmentSpecs(string prompt)
    {
        foreach (var line in prompt.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.Contains("assessmentItemId=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!Guid.TryParse(ReadField(line, "assessmentItemId"), out var id))
            {
                continue;
            }

            var conceptKey = ReadField(line, "conceptKey");
            yield return (
                id,
                $"grammar:{conceptKey}:{id:N}",
                conceptKey,
                ReadField(line, "cognitiveSkill", "conceptual"),
                ReadField(line, "difficulty", "medium"),
                ReadField(line, "misconceptionTarget"),
                ReadField(line, "evidenceExpected", $"Evidence for {conceptKey}"));
        }
    }

    private static string ReadField(string line, string field, string fallback = "")
    {
        var match = Regex.Match(line, $@"{Regex.Escape(field)}=([^;]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : fallback;
    }
}
