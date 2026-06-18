using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class OrkaLearningStateCoherenceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AllowedActionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "start_diagnostic",
        "repair_concept",
        "repair_prerequisite",
        "review_due_concept",
        "create_flashcards",
        "practice_exam_outcome",
        "review_deneme_mistakes",
        "source_review",
        "citation_review",
        "update_wiki_note",
        "open_study_room",
        "open_notebook_pack",
        "take_checkpoint_quiz",
        "continue_plan"
    };

    private static readonly HashSet<string> AllowedPriorities = new(StringComparer.OrdinalIgnoreCase)
    {
        "urgent",
        "high",
        "medium",
        "normal",
        "low"
    };

    private static readonly HashSet<string> AllowedFeatureStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ready",
        "available",
        "limited",
        "not_available"
    };

    private static readonly HashSet<string> AllowedScopeStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "global",
        "topic",
        "session"
    };

    private static readonly HashSet<string> AllowedConflictSeverities = new(StringComparer.OrdinalIgnoreCase)
    {
        "info",
        "warning",
        "blocker"
    };

    [Fact]
    public async Task UnifiedState_RepeatedWrongAndBlankAnswersChooseRepairWithoutFakeMisconception()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "orka-state-repair");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Unified Repair Topic");
        await SeedRepairSignalsAsync(factory, user.UserId, topicId);

        var state = await GetStateAsync(user, topicId);

        Assert.Contains(state.PrimaryNextAction.ActionType, new[] { "repair_concept", "repair_prerequisite" });
        Assert.Contains(state.SecondaryNextActions, a => a.ActionType == "open_study_room");
        Assert.Contains(state.ReasonCodes, r => r is "repeated_wrong" or "weak_concept");

        var blankConcept = Assert.Single(state.LongTermLearningProfile.Concepts.Where(c => c.ConceptKey == "blank-gap"));
        Assert.Equal("repair", blankConcept.RecommendedAction);
        Assert.Contains("repeated_blank", blankConcept.ReasonCodes);
        Assert.DoesNotContain("misconception", blankConcept.ReasonCodes, StringComparer.OrdinalIgnoreCase);

        AssertSafePayload(JsonSerializer.Serialize(state, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task UnifiedState_SourceEvidenceInsufficientBlocksGroundedClaimAndDashboardConsumesState()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "orka-state-source");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Unified Source Topic");
        await SeedSourceWikiWarningAsync(factory, user.UserId, topicId);

        var state = await GetStateAsync(user, topicId);
        var dashboard = await user.Client.GetFromJsonAsync<DashboardTodayDto>("/api/dashboard/today")
            ?? throw new InvalidOperationException("Dashboard payload missing.");

        Assert.Contains(state.ConflictWarnings, w => w.ConflictCode == "source_grounding_blocked");
        Assert.Contains(state.SafetyWarnings, w => w == "source_grounded_claim_blocked");
        Assert.Contains(state.PrimaryNextAction.ActionType, new[] { "source_review", "repair_concept", "citation_review" });
        Assert.NotNull(dashboard.OrkaLearningState);
        Assert.Equal(dashboard.OrkaLearningState!.PrimaryNextAction.ActionType, dashboard.NextAction.View switch
        {
            "sources" => state.PrimaryNextAction.ActionType,
            "wiki" => state.PrimaryNextAction.ActionType,
            "chat" => state.PrimaryNextAction.ActionType,
            _ => dashboard.OrkaLearningState.PrimaryNextAction.ActionType
        });
        AssertSafePayload(JsonSerializer.Serialize(new { state, dashboard.OrkaLearningState }, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task UnifiedState_ExamWeakOutcomeFeedsTutorNextActionsWithoutSuccessGuarantee()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "orka-state-exam");
        await SeedExamWeakOutcomeAsync(factory, user);

        var state = await GetStateAsync(user, topicId: null);
        var tutorActions = await user.Client.GetFromJsonAsync<List<TutorNextLearningActionDto>>("/api/tutor/next-actions")
            ?? [];

        Assert.Contains(state.PrimaryNextAction.ActionType, new[] { "practice_exam_outcome", "review_deneme_mistakes", "start_diagnostic" });
        Assert.True(state.PrimaryNextAction.ActionType != "start_diagnostic" ||
                    state.SecondaryNextActions.Any(a => a.ActionType is "practice_exam_outcome" or "review_deneme_mistakes"));
        Assert.Contains(state.ReasonCodes, r => r is "exam_weak_outcome" or "deneme_mistake_cluster" or "weak_outcome");
        Assert.Contains(tutorActions, a => a.ActionType is "review_exam_mistakes" or "start_micro_quiz");
        Assert.DoesNotContain("guarantee", JsonSerializer.Serialize(state, JsonOptions), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%100", JsonSerializer.Serialize(state, JsonOptions), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnifiedState_BlocksCrossUserTopicAccess()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "orka-state-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "orka-state-other");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, owner.UserId, "Private Orka State Topic");

        var response = await other.Client.GetAsync($"/api/learning/orka-state?topicId={topicId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnifiedState_ProjectionContract_EmitsStableShapeNullabilityAndAllowedValues()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "orka-state-contract");

        var globalBefore = DateTimeOffset.UtcNow.AddSeconds(-1);
        var global = await GetStateAsync(user, topicId: null);
        var globalAfter = DateTimeOffset.UtcNow.AddSeconds(1);
        Assert.Contains(global.ScopeStatus, AllowedScopeStatuses);
        Assert.Equal("global", global.ScopeStatus);
        Assert.Null(global.TopicId);
        Assert.Null(global.SessionId);
        AssertStateProjectionContract(global, user.UserId, globalBefore, globalAfter);

        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Unified Projection Contract");
        var topicBefore = DateTimeOffset.UtcNow.AddSeconds(-1);
        var topic = await GetStateAsync(user, topicId);
        var topicAfter = DateTimeOffset.UtcNow.AddSeconds(1);
        Assert.Equal(topicId, topic.TopicId);
        Assert.Equal("topic", topic.ScopeStatus);
        Assert.Null(topic.SessionId);
        AssertStateProjectionContract(topic, user.UserId, topicBefore, topicAfter);

        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(factory, user.UserId, topicId, DateTime.UtcNow);
        var sessionBefore = DateTimeOffset.UtcNow.AddSeconds(-1);
        var session = await user.Client.GetFromJsonAsync<OrkaLearningStateDto>($"/api/learning/orka-state?topicId={topicId}&sessionId={sessionId}")
            ?? throw new InvalidOperationException("Session-scoped projection missing.");
        var sessionAfter = DateTimeOffset.UtcNow.AddSeconds(1);
        Assert.Equal(topicId, session.TopicId);
        Assert.Equal(sessionId, session.SessionId);
        Assert.Equal("session", session.ScopeStatus);
        AssertStateProjectionContract(session, user.UserId, sessionBefore, sessionAfter);
    }

    private static async Task<OrkaLearningStateDto> GetStateAsync(CoordinationTestUser user, Guid? topicId)
    {
        var path = topicId.HasValue
            ? $"/api/learning/orka-state?topicId={topicId.Value}"
            : "/api/learning/orka-state";
        var response = await user.Client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrkaLearningStateDto>())!;
    }

    private static void AssertStateProjectionContract(
        OrkaLearningStateDto state,
        Guid userId,
        DateTimeOffset? requestStartedAt = null,
        DateTimeOffset? requestEndedAt = null)
    {
        Assert.Contains(state.ScopeStatus, AllowedScopeStatuses);
        Assert.NotNull(state.PrimaryNextAction);
        Assert.Contains(state.PrimaryNextAction.ActionType, AllowedActionTypes);
        Assert.Contains(state.PrimaryNextAction.Priority, AllowedPriorities);
        Assert.False(string.IsNullOrWhiteSpace(state.PrimaryNextAction.Label));
        Assert.False(string.IsNullOrWhiteSpace(state.PrimaryNextAction.Reason));

        foreach (var action in state.SecondaryNextActions)
        {
            Assert.Contains(action.ActionType, AllowedActionTypes);
            Assert.Contains(action.Priority, AllowedPriorities);
            Assert.False(string.IsNullOrWhiteSpace(action.Label));
            Assert.False(string.IsNullOrWhiteSpace(action.Reason));
        }

        Assert.NotEmpty(state.FeatureReadiness);
        foreach (var feature in state.FeatureReadiness)
        {
            Assert.Contains(feature.Status, AllowedFeatureStatuses);
            Assert.False(string.IsNullOrWhiteSpace(feature.FeatureKey));
            Assert.False(string.IsNullOrWhiteSpace(feature.UserSafeSummary));
        }

        foreach (var conflict in state.ConflictWarnings)
        {
            Assert.Contains(conflict.Severity, AllowedConflictSeverities);
            Assert.False(string.IsNullOrWhiteSpace(conflict.ConflictCode));
            Assert.False(string.IsNullOrWhiteSpace(conflict.UserSafeSummary));
        }

        Assert.True(state.GeneratedAt >= (requestStartedAt ?? DateTimeOffset.UtcNow.AddMinutes(-5)));
        Assert.True(state.GeneratedAt <= (requestEndedAt ?? DateTimeOffset.UtcNow.AddMinutes(1)));
        AssertSafePayload(JsonSerializer.Serialize(state, JsonOptions), userId);
    }

    private static async Task SeedRepairSignalsAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;

        db.KnowledgeTracingStates.AddRange(
            new KnowledgeTracingState
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                ConceptKey = "repeated-wrong",
                Label = "Repeated Wrong",
                EvidenceCount = 3,
                CorrectCount = 0,
                IncorrectCount = 3,
                MasteryProbability = 0.22m,
                Confidence = 0.78m,
                RemediationNeed = "high",
                PracticeReadiness = "guided",
                LastEvidenceAt = now.AddMinutes(-2),
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now
            },
            new KnowledgeTracingState
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                ConceptKey = "blank-gap",
                Label = "Blank Gap",
                EvidenceCount = 2,
                CorrectCount = 0,
                IncorrectCount = 0,
                MasteryProbability = 0.40m,
                Confidence = 0.44m,
                RemediationNeed = "medium",
                PracticeReadiness = "guided",
                LastEvidenceAt = now.AddMinutes(-1),
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now
            });

        db.QuizAttempts.AddRange(
            Attempt(userId, topicId, "repeated-wrong", false, false, now.AddMinutes(-12)),
            Attempt(userId, topicId, "repeated-wrong", false, false, now.AddMinutes(-8)),
            Attempt(userId, topicId, "repeated-wrong", false, false, now.AddMinutes(-4)),
            Attempt(userId, topicId, "blank-gap", false, true, now.AddMinutes(-6)),
            Attempt(userId, topicId, "blank-gap", false, true, now.AddMinutes(-3)));

        db.ClassroomSessions.Add(new ClassroomSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            Transcript = "[HOCA]: Safe study room context.",
            LastSegment = "Safe study room context.",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        });

        await db.SaveChangesAsync();
    }

    private static QuizAttempt Attempt(Guid userId, Guid topicId, string conceptKey, bool correct, bool skipped, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TopicId = topicId,
        Question = "Safe question",
        UserAnswer = skipped ? string.Empty : "B",
        IsCorrect = correct,
        WasSkipped = skipped,
        Explanation = "Safe explanation",
        SkillTag = conceptKey,
        CreatedAt = createdAt
    };

    private static async Task SeedSourceWikiWarningAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(factory, userId, topicId, "Unified Source", "safe source fixture");
        var pageId = await CoordinationTestHelpers.SeedWikiPageAsync(factory, userId, topicId, "Unified Wiki", "manual safe note");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var page = await db.WikiPages.SingleAsync(p => p.Id == pageId);
        page.ConceptKey = "source-gap";
        page.SourceReadiness = "evidence_insufficient";
        page.EvidenceStatus = "evidence_insufficient";

        var block = await db.WikiBlocks.SingleAsync(b => b.WikiPageId == pageId);
        block.BlockType = WikiBlockType.RepairNote;
        block.ConceptKey = "source-gap";
        block.SourceBasis = "evidence_insufficient";
        block.SafetyWarningsJson = "[\"source_limited\"]";

        var lifecycle = scope.ServiceProvider.GetRequiredService<ISourceEvidenceLifecycleService>();
        await lifecycle.BuildSourceEvidenceBundleAsync(userId, topicId, question: "safe source state check");
        await lifecycle.MarkSourceStaleAsync(userId, sourceId, "orka_state_test_stale");
        await db.SaveChangesAsync();
    }

    private static async Task SeedExamWeakOutcomeAsync(ApiSmokeFactory factory, CoordinationTestUser user)
    {
        var ids = await GetKpssIdsAsync(factory);
        for (var i = 0; i < 5; i++)
        {
            await SeedQuestionAsync(factory, ids, $"Unified exam stem {i}");
        }

        var sessionResponse = await user.Client.PostAsJsonAsync(
            "/api/central-exams/kpss/denemeler/KPSS_MINI_TURKCE_PARAGRAF/start",
            new CentralExamDenemeStartRequestDto());
        sessionResponse.EnsureSuccessStatusCode();
        var session = (await sessionResponse.Content.ReadFromJsonAsync<CentralExamDenemeSessionDto>())!;

        var submit = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/denemeler/submit", new CentralExamDenemeSubmitRequestDto
        {
            DenemeAttemptId = session.DenemeAttemptId,
            Answers = session.Questions.Select((q, index) => new CentralExamDenemeAnswerDto
            {
                QuestionId = q.QuestionId,
                SelectedOptionKey = index < 3 ? "B" : "A"
            }).ToList()
        });
        submit.EnsureSuccessStatusCode();
    }

    private static async Task<KpssPath> GetKpssIdsAsync(ApiSmokeFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IExamFrameworkService>();
        var tree = await service.CreateSystemSkeletonAsync();
        var variant = tree.Variants.Single(v => v.Code == "KPSS_LISANS");
        var section = variant.Sections.Single(s => s.Code == "GENEL_YETENEK");
        var subject = section.Subjects.Single(s => s.Code == "TURKCE");
        var topic = subject.Topics.Single(t => t.Code == "PARAGRAF");
        return new KpssPath(tree.Id, variant.Id, section.Id, subject.Id, topic.Id, topic.Outcomes.Single().Id);
    }

    private static async Task SeedQuestionAsync(ApiSmokeFactory factory, KpssPath ids, string stem)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        db.QuestionItems.Add(new QuestionItem
        {
            ExamDefinitionId = ids.DefinitionId,
            ExamVariantId = ids.VariantId,
            ExamSectionId = ids.SectionId,
            ExamSubjectId = ids.SubjectId,
            ExamTopicId = ids.TopicId,
            ExamOutcomeId = ids.OutcomeId,
            QuestionType = "multiple_choice",
            Stem = stem,
            Difficulty = "medium",
            CognitiveSkill = "reading_comprehension",
            QualityStatus = "published",
            LicenseStatus = "open",
            SourceOrigin = "test_fixture",
            Explanation = "Post-submit safe explanation.",
            Options =
            [
                new QuestionOption { OptionKey = "A", Text = "Correct option", IsCorrect = true, SortOrder = 0 },
                new QuestionOption { OptionKey = "B", Text = "Wrong option", IsCorrect = false, SortOrder = 1 }
            ],
            OutcomeLinks =
            [
                new QuestionOutcomeLink { ExamOutcomeId = ids.OutcomeId, IsPrimary = true, LinkStrength = 1.0m }
            ]
        });
        await db.SaveChangesAsync();
    }

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
            "answerKey",
            "correctAnswer",
            "stackTrace",
            "ownerId",
            "userId"
        };

        foreach (var marker in unsafeMarkers)
        {
            Assert.DoesNotContain(marker, json, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(userId.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("success guarantee", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%100", json, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record KpssPath(
        Guid DefinitionId,
        Guid VariantId,
        Guid SectionId,
        Guid SubjectId,
        Guid TopicId,
        Guid OutcomeId);
}
