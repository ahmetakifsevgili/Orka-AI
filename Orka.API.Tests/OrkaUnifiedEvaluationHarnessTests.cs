using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class OrkaUnifiedEvaluationHarnessTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string RawLearnerPhrase = "arbitrary learner phrase rawPrompt token_marker_secret_value";
    private const string RawSourcePhrase = "arbitrary source phrase rawSourceChunk C:\\secret\\source.txt answerKey";

    [Fact]
    public async Task UnifiedEvaluationHarness_BuildsScorecardAcrossPhaseOneToEightModules()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "unified-eval");
        var context = await SeedUnifiedEvaluationJourneyAsync(factory, user.UserId);

        var result = await EvaluateAsync(factory, user.UserId, context.TopicId, context.SessionId);

        Assert.NotNull(result);
        var checkSummary = JsonSerializer.Serialize(result!.Scorecard.Checks, JsonOptions);
        Assert.True(result.OverallStatus == "pass", checkSummary);
        Assert.True(result.Scorecard.OverallStatus == "pass", checkSummary);
        Assert.Contains(result.Scorecard.Checks, c => c.CheckKey == "unifiedStateReady" && c.Status == "pass");
        Assert.Contains(result.Scorecard.Checks, c => c.CheckKey == "missionControlReady" && c.Status == "pass");
        Assert.Contains(result.Scorecard.Checks, c => c.CheckKey == "studyCoachReady" && c.Status == "pass");
        Assert.Contains(result.Scorecard.Checks, c => c.CheckKey == "examWarRoomReady" && c.Status == "pass");
        Assert.Contains(result.Scorecard.Checks, c => c.CheckKey == "sourceWikiProReady" && c.Status == "pass");
        Assert.Contains(result.Scorecard.Checks, c => c.CheckKey == "studyRoomReady" && c.Status == "pass");
        Assert.Contains(result.Scorecard.Checks, c => c.CheckKey == "notebookStudioProReady" && c.Status == "pass");
        Assert.Contains(result.Scorecard.Checks, c => c.CheckKey == "codeLearningIdeReady" && c.Status == "pass");
        Assert.Contains(result.Scorecard.Checks, c => c.CheckKey == "providerFreeReady" && c.Status == "pass");
        Assert.DoesNotContain(result.Scorecard.Checks, c => c.Status is "fail" or "blocked");

        var scenarioKeys = result.ScenarioResults.Select(s => s.ScenarioKey).ToArray();
        Assert.Contains("new_learner", scenarioKeys);
        Assert.Contains("repeated_wrong_learner", scenarioKeys);
        Assert.Contains("blank_skipped_learner", scenarioKeys);
        Assert.Contains("exam_prep_learner", scenarioKeys);
        Assert.Contains("source_wiki_learner", scenarioKeys);
        Assert.Contains("study_room_learner", scenarioKeys);
        Assert.Contains("notebook_artifact_learner", scenarioKeys);
        Assert.Contains("code_learning_learner", scenarioKeys);
        Assert.Contains("mixed_learning_os_learner", scenarioKeys);
    }

    [Fact]
    public async Task UnifiedEvaluationHarness_SweepsPublicPayloadsForUnsafeMarkers()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "unified-eval-safety");
        var context = await SeedUnifiedEvaluationJourneyAsync(factory, user.UserId);

        var result = await EvaluateAsync(factory, user.UserId, context.TopicId, context.SessionId);
        var json = JsonSerializer.Serialize(result, JsonOptions);

        Assert.Equal("pass", result!.SafetySweep.Status);
        Assert.Equal(0, result.SafetySweep.UnsafeMarkerHitCount);
        AssertSafePayload(json, user.UserId);
    }

    [Fact]
    public async Task UnifiedEvaluationHarness_ChecksCrossModuleConsistency()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "unified-eval-consistency");
        var context = await SeedUnifiedEvaluationJourneyAsync(factory, user.UserId);

        var result = await EvaluateAsync(factory, user.UserId, context.TopicId, context.SessionId);

        Assert.Contains(result!.ModuleConsistency, c => c.CheckKey == "missionUnifiedConsistency" && c.Status == "pass");
        Assert.Contains(result.ModuleConsistency, c => c.CheckKey == "studyCoachMissionConsistency" && c.Status == "pass");
        Assert.Contains(result.ModuleConsistency, c => c.CheckKey == "tutorSourceGroundingConsistency" && c.Status == "pass");
        Assert.Contains(result.ModuleConsistency, c => c.CheckKey == "notebookSourceBackingConsistency" && c.Status == "pass");
        Assert.Contains(result.ModuleConsistency, c => c.CheckKey == "codeRuntimeConsistency" && c.Status == "pass");
        Assert.Contains(result.ReasonCodes, r => r is "source_grounding_blocked" or "source_grounded_claim_blocked" or "source_evidence_limited");
    }

    [Fact]
    public void UnifiedEvaluationHarness_ReleaseScriptsCoverProductCoherenceGate()
    {
        var quickBackend = Read("scripts/quick-backend.ps1");
        var regressionTests = Read("Orka.API.Tests/RegressionGateScriptTests.cs");
        var checklist = Read("scripts/CHECKLIST.md");

        Assert.Contains("product coherence release proof", quickBackend, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OrkaUnifiedEvaluationHarnessTests", quickBackend);
        Assert.Contains("StudentSimulationEvaluationTests", quickBackend);
        Assert.Contains("OrkaCodeLearningIdeTests", quickBackend);
        Assert.Contains("OrkaNotebookStudioProTests", quickBackend);
        Assert.Contains("OrkaStudyRoomTests", quickBackend);
        Assert.Contains("OrkaSourceWikiProTests", quickBackend);
        Assert.Contains("OrkaExamWarRoomTests", quickBackend);
        Assert.Contains("OrkaStudyCoachTests", quickBackend);
        Assert.Contains("OrkaMissionControlTests", quickBackend);
        Assert.Contains("OrkaLearningStateCoherenceTests", quickBackend);
        Assert.Contains("LearningSnapshotTests", quickBackend);
        Assert.Contains("MandatoryProductCoherenceTests", regressionTests);
        Assert.Contains("LearningSnapshotTests", regressionTests);
        Assert.Contains("Phase 9 - Unified Evaluation / CI / Release Harness", checklist);
    }

    [Fact]
    public async Task UnifiedEvaluationHarness_PublicEndpointsStillBlockCrossUserScope()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "unified-eval-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "unified-eval-other");
        var context = await SeedUnifiedEvaluationJourneyAsync(factory, owner.UserId);

        var sourceId = context.SourceId;
        var wikiPageId = context.WikiPageId;
        var topicId = context.TopicId;

        var deniedState = await other.Client.GetAsync($"/api/learning/orka-state?topicId={topicId}");
        var deniedMission = await other.Client.GetAsync($"/api/learning/mission-control?topicId={topicId}");
        var deniedCoach = await other.Client.GetAsync($"/api/learning/study-coach?topicId={topicId}");
        var deniedSourcePro = await other.Client.GetAsync($"/api/sources/wiki-pro?sourceId={sourceId}");
        var deniedStudyRoom = await other.Client.GetAsync($"/api/classroom/study-room?topicId={topicId}");
        var deniedNotebook = await other.Client.GetAsync($"/api/notebook-studio/pro?sourceId={sourceId}");
        var deniedCode = await other.Client.GetAsync($"/api/code/learning-ide?topicId={topicId}");
        var deniedWiki = await other.Client.GetAsync($"/api/wiki/page/{wikiPageId}/copilot");

        Assert.Equal(HttpStatusCode.NotFound, deniedState.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deniedMission.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deniedCoach.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deniedSourcePro.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deniedStudyRoom.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deniedNotebook.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deniedCode.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deniedWiki.StatusCode);
    }

    private static async Task<OrkaUnifiedEvaluationDto?> EvaluateAsync(ApiSmokeFactory factory, Guid userId, Guid topicId, Guid sessionId)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IOrkaUnifiedEvaluationService>();
        return await service.EvaluateAsync(userId, topicId, sessionId);
    }

    private static async Task<EvaluationSeedContext> SeedUnifiedEvaluationJourneyAsync(ApiSmokeFactory factory, Guid userId)
    {
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, userId, "Unified Evaluation Learning OS");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(factory, userId, topicId, DateTime.UtcNow.AddDays(-6));
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(factory, userId, topicId, "Unified Evaluation Source", RawSourcePhrase);
        var wikiPageId = await CoordinationTestHelpers.SeedWikiPageAsync(factory, userId, topicId, "Unified Evaluation Wiki", "manual note should stay bounded");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;

        var wikiPage = await db.WikiPages.SingleAsync(p => p.Id == wikiPageId);
        wikiPage.ConceptKey = "unified-repair";
        wikiPage.SourceReadiness = "evidence_insufficient";
        wikiPage.EvidenceStatus = "evidence_insufficient";
        wikiPage.Status = "learning";
        var wikiBlock = await db.WikiBlocks.SingleAsync(b => b.WikiPageId == wikiPageId);
        wikiBlock.BlockType = WikiBlockType.RepairNote;
        wikiBlock.ConceptKey = "unified-repair";
        wikiBlock.SourceBasis = "evidence_insufficient";
        wikiBlock.SafetyWarningsJson = "[\"source_limited\"]";

        db.QuizAttempts.AddRange(
            WrongAttempt(userId, topicId, sessionId, "unified-repair", now.AddMinutes(-35)),
            WrongAttempt(userId, topicId, sessionId, "unified-repair", now.AddMinutes(-30)),
            WrongAttempt(userId, topicId, sessionId, "unified-repair", now.AddMinutes(-25)),
            BlankAttempt(userId, topicId, sessionId, "unified-blank", now.AddMinutes(-20)),
            BlankAttempt(userId, topicId, sessionId, "unified-blank", now.AddMinutes(-15)),
            CorrectAttempt(userId, topicId, sessionId, "unified-stable", now.AddDays(-1)));

        db.ReviewItems.Add(new ReviewItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ReviewKey = $"phase9:{topicId:N}",
            SkillTag = "Unified Due Review",
            ConceptTag = "unified-review",
            LearningObjective = "Review a likely forgotten concept",
            DueAt = now.AddDays(-1),
            IntervalDays = 2,
            Status = "active",
            CreatedAt = now.AddDays(-5),
            UpdatedAt = now.AddDays(-1)
        });

        db.LearningSignals.AddRange(
            Signal(userId, topicId, sessionId, LearningSignalTypes.ClassroomStarted, "unified-repair", now.AddMinutes(-12), isPositive: true),
            Signal(userId, topicId, sessionId, LearningSignalTypes.ClassroomQuestionAsked, "unified-repair", now.AddMinutes(-10), isPositive: false),
            CodeSignal(userId, topicId, sessionId, LearningSignalTypes.IdeRuntimeError, now.AddMinutes(-9)),
            CodeSignal(userId, topicId, sessionId, LearningSignalTypes.IdeRuntimeError, now.AddMinutes(-8)));

        db.LearningNotebookPacks.Add(new LearningNotebookPack
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            WikiPageId = wikiPageId,
            PackType = "repair_pack",
            Title = "Unified repair pack",
            Summary = "Safe repair pack metadata.",
            PackStatus = "ready",
            SourceReadiness = "evidence_insufficient",
            EvidenceStatus = "evidence_insufficient",
            WeakConceptKeysJson = "[\"unified-repair\"]",
            NextActionsJson = "[\"create_repair_pack\",\"review_due\"]",
            WarningsJson = "[\"source_grounding_blocked\"]",
            CreatedAt = now.AddMinutes(-7),
            UpdatedAt = now.AddMinutes(-6)
        });

        await db.SaveChangesAsync();

        var lifecycle = scope.ServiceProvider.GetRequiredService<ISourceEvidenceLifecycleService>();
        await lifecycle.BuildSourceEvidenceBundleAsync(userId, topicId, question: "unified source check");
        await lifecycle.MarkSourceStaleAsync(userId, sourceId, "phase9_unified_evaluation_stale");

        return new EvaluationSeedContext(topicId, sessionId, sourceId, wikiPageId);
    }

    private static QuizAttempt WrongAttempt(Guid userId, Guid topicId, Guid sessionId, string concept, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TopicId = topicId,
        SessionId = sessionId,
        Question = "Unified repair check",
        UserAnswer = RawLearnerPhrase,
        IsCorrect = false,
        Explanation = "Needs repair",
        SkillTag = concept,
        TopicPath = concept,
        CreatedAt = createdAt
    };

    private static QuizAttempt BlankAttempt(Guid userId, Guid topicId, Guid sessionId, string concept, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TopicId = topicId,
        SessionId = sessionId,
        Question = "Unified blank check",
        UserAnswer = string.Empty,
        IsCorrect = false,
        Explanation = "Skipped safely",
        SkillTag = concept,
        TopicPath = concept,
        WasSkipped = true,
        CreatedAt = createdAt
    };

    private static QuizAttempt CorrectAttempt(Guid userId, Guid topicId, Guid sessionId, string concept, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TopicId = topicId,
        SessionId = sessionId,
        Question = "Unified stable check",
        UserAnswer = "safe answer",
        IsCorrect = true,
        Explanation = "Stable evidence",
        SkillTag = concept,
        TopicPath = concept,
        CreatedAt = createdAt
    };

    private static LearningSignal Signal(
        Guid userId,
        Guid topicId,
        Guid sessionId,
        string type,
        string skill,
        DateTime createdAt,
        bool isPositive) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            SignalType = type,
            SkillTag = skill,
            TopicPath = skill,
            IsPositive = isPositive,
            CreatedAt = createdAt
        };

    private static LearningSignal CodeSignal(Guid userId, Guid topicId, Guid sessionId, string type, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TopicId = topicId,
        SessionId = sessionId,
        SignalType = type,
        SkillTag = "python",
        TopicPath = "python-runtime",
        IsPositive = false,
        PayloadJson = JsonSerializer.Serialize(new
        {
            language = "python",
            phase = "run",
            runtimeError = "Traceback at C:\\secret\\app.py rawToolPayload stackTrace ownerId userId",
            safeTutorSummary = "Runtime error category only",
            durationMs = 5
        }),
        CreatedAt = createdAt
    };

    private static void AssertSafePayload(string json, Guid userId)
    {
        var unsafeMarkers = new[]
        {
            "rawPrompt",
            "hiddenPrompt",
            "systemPrompt",
            "developerPrompt",
            "rawProviderPayload",
            "rawSourceChunk",
            "rawToolPayload",
            "debugTrace",
            "localPath",
            "apiKey",
            "secret",
            "token_marker_secret_value",
            "answerKey",
            "correctAnswer",
            "stackTrace",
            "ownerId",
            "userId",
            "rawTranscript"
        };

        foreach (var marker in unsafeMarkers)
        {
            Assert.DoesNotContain(marker, json, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(userId.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(RawLearnerPhrase, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(RawSourcePhrase, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("official curriculum complete", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("success guarantee", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%100", json, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Orka.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }

    private sealed record EvaluationSeedContext(Guid TopicId, Guid SessionId, Guid SourceId, Guid WikiPageId);
}
