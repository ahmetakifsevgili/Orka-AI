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

public sealed class OrkaMissionControlTests
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

    private static readonly HashSet<string> AllowedEntryPoints = new(StringComparer.OrdinalIgnoreCase)
    {
        "ask_tutor",
        "open_study_room",
        "review_due_concept",
        "practice_exam_outcome",
        "review_deneme_mistakes",
        "source_review",
        "citation_review",
        "update_wiki_note",
        "open_notebook_pack",
        "create_flashcards",
        "take_checkpoint_quiz",
        "continue_plan"
    };

    private static readonly HashSet<string> AllowedRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "chat",
        "classroom",
        "learning",
        "central-exams",
        "sources",
        "wiki",
        "notebook-studio",
        "dashboard"
    };

    private static readonly HashSet<string> AllowedPriorities = new(StringComparer.OrdinalIgnoreCase)
    {
        "urgent",
        "high",
        "medium",
        "normal",
        "low"
    };

    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ready",
        "available",
        "limited",
        "blocked",
        "empty"
    };

    private static readonly HashSet<string> AllowedLoads = new(StringComparer.OrdinalIgnoreCase)
    {
        "none",
        "medium",
        "high"
    };

    private static readonly HashSet<string> AllowedEvidenceConfidence = new(StringComparer.OrdinalIgnoreCase)
    {
        "limited_evidence",
        "thin_evidence",
        "enough_evidence"
    };

    private static readonly HashSet<string> AllowedWarningSeverities = new(StringComparer.OrdinalIgnoreCase)
    {
        "info",
        "warning",
        "critical"
    };

    private static readonly HashSet<string> AllowedModuleKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "tutor",
        "study_room",
        "review",
        "exam",
        "sources",
        "wiki",
        "notebook_studio",
        "quiz_checkpoint",
        "progress"
    };

    private static readonly HashSet<string> AllowedSectionKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "start_here",
        "repair_today",
        "review_due",
        "exam_focus",
        "source_wiki_attention",
        "continue_learning",
        "study_room",
        "notebook_artifacts",
        "progress_snapshot"
    };

    [Fact]
    public async Task MissionControl_NewLearnerDegradesSafelyWithoutMasteryClaim()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "mission-new");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Mission New Learner");

        var mission = await GetMissionAsync(user, topicId);

        Assert.Equal("start_diagnostic", mission.PrimaryMission.ActionType);
        Assert.Equal("ask_tutor", mission.PrimaryEntryPoint);
        Assert.Equal("thin_evidence", mission.EvidenceConfidence);
        Assert.Contains(mission.Sections, s => s.SectionKey == "start_here" && s.Status is "ready" or "limited");
        Assert.Contains(mission.ModuleCards, c => c.ModuleKey == "tutor" && c.Status == "ready");
        Assert.DoesNotContain("stable", JsonSerializer.Serialize(mission, JsonOptions), StringComparison.OrdinalIgnoreCase);
        AssertSafePayload(JsonSerializer.Serialize(mission, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task MissionControl_RepeatedWrongAndBlankAnswersCreateRepairMissionAndStudyRoomHandoff()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "mission-repair");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Mission Repair Topic");
        await SeedRepairSignalsAsync(factory, user.UserId, topicId, includeStudyRoom: true);

        var mission = await GetMissionAsync(user, topicId);

        Assert.Contains(mission.PrimaryMission.ActionType, new[] { "repair_concept", "repair_prerequisite" });
        Assert.Equal("high", mission.RepairLoad);
        Assert.Contains(mission.PrimaryMission.ReasonCodes, r => r is "repeated_wrong" or "prerequisite_gap" or "repeated_blank");
        Assert.Contains(mission.Sections, s => s.SectionKey == "repair_today" && s.Actions.Count > 0);
        Assert.NotNull(mission.StudyRoomSuggestion);
        Assert.Equal("open_study_room", mission.StudyRoomSuggestion!.ActionType);
        Assert.Contains(mission.ModuleCards, c => c.ModuleKey == "study_room" && c.Status == "ready");
        Assert.DoesNotContain("misconception", JsonSerializer.Serialize(mission, JsonOptions), StringComparison.OrdinalIgnoreCase);
        AssertSafePayload(JsonSerializer.Serialize(mission, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task MissionControl_OneWrongAnswerDoesNotOverreactToUrgentRepair()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "mission-one-wrong");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Mission Single Wrong Topic");
        await SeedSingleWrongAsync(factory, user.UserId, topicId);

        var mission = await GetMissionAsync(user, topicId);

        Assert.NotEqual("urgent", mission.PrimaryMission.Priority);
        Assert.NotEqual("repair_prerequisite", mission.PrimaryMission.ActionType);
        Assert.DoesNotContain(mission.SecondaryActions, a => a.Priority == "urgent");
        AssertSafePayload(JsonSerializer.Serialize(mission, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task MissionControl_DueReviewCreatesReviewSection()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "mission-review");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Mission Review Topic");
        await SeedDueReviewAsync(factory, user.UserId, topicId);

        var mission = await GetMissionAsync(user, topicId);

        Assert.Contains(mission.PrimaryMission.ActionType, new[] { "review_due_concept", "source_review" });
        Assert.NotEqual("none", mission.ReviewLoad);
        Assert.Contains(mission.Sections, s => s.SectionKey == "review_due" && s.Actions.Any(a => a.ActionType == "review_due_concept"));
        Assert.Contains(mission.ModuleCards, c => c.ModuleKey == "review" && c.Status == "ready");
        AssertSafePayload(JsonSerializer.Serialize(mission, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task MissionControl_SourceInsufficientCreatesWarningAndBlocksGroundedOverclaim()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "mission-source");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Mission Source Topic");
        await SeedSourceWikiWarningAsync(factory, user.UserId, topicId);

        var mission = await GetMissionAsync(user, topicId);

        Assert.Equal("high", mission.SourceWikiLoad);
        Assert.Contains(mission.UrgentWarnings, w => w.WarningCode is "source_grounding_blocked" or "source_grounded_claim_blocked");
        Assert.Contains(mission.ModuleCards, c => c.ModuleKey == "sources" && c.Status is "blocked" or "limited");
        Assert.Contains(mission.Sections, s => s.SectionKey == "source_wiki_attention" && (s.Actions.Count > 0 || s.Warnings.Count > 0));
        Assert.DoesNotContain("source-grounded answer", JsonSerializer.Serialize(mission, JsonOptions), StringComparison.OrdinalIgnoreCase);
        AssertSafePayload(JsonSerializer.Serialize(mission, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task MissionControl_ExamWeakOutcomeActivatesExamFocusWithoutGuarantee()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "mission-exam");
        await SeedExamWeakOutcomeAsync(factory, user);

        var mission = await GetMissionAsync(user, topicId: null);

        Assert.Contains(mission.PrimaryMission.ActionType, new[] { "practice_exam_outcome", "review_deneme_mistakes", "start_diagnostic" });
        Assert.Contains(mission.Sections, s => s.SectionKey == "exam_focus" && s.Actions.Any(a => a.ActionType is "practice_exam_outcome" or "review_deneme_mistakes"));
        Assert.NotEqual("none", mission.ExamLoad);
        Assert.Contains(mission.ModuleCards, c => c.ModuleKey == "exam" && c.Status is "ready" or "limited");
        var json = JsonSerializer.Serialize(mission, JsonOptions);
        Assert.DoesNotContain("guarantee", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%100", json, StringComparison.OrdinalIgnoreCase);
        AssertSafePayload(json, user.UserId);
    }

    [Fact]
    public async Task MissionControl_DashboardTodayCarriesSamePrimaryMissionContract()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "mission-dashboard");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Mission Dashboard Topic");
        await SeedRepairSignalsAsync(factory, user.UserId, topicId, includeStudyRoom: true);

        var mission = await GetMissionAsync(user, topicId);
        var dashboard = await user.Client.GetFromJsonAsync<DashboardTodayDto>("/api/dashboard/today")
            ?? throw new InvalidOperationException("Dashboard payload missing.");

        Assert.NotNull(dashboard.MissionControl);
        Assert.NotNull(dashboard.OrkaLearningState);
        Assert.Equal(dashboard.MissionControl!.PrimaryMission.ActionType, dashboard.OrkaLearningState!.PrimaryNextAction.ActionType);
        Assert.Equal(mission.PrimaryMission.ActionType, dashboard.MissionControl.PrimaryMission.ActionType);
        Assert.Equal(mission.ScopeStatus, dashboard.MissionControl.ScopeStatus);
        Assert.Equal(mission.PrimaryMission.EntryPoint, dashboard.MissionControl.PrimaryMission.EntryPoint);
        Assert.Equal(mission.PrimaryMission.TargetRoute, dashboard.MissionControl.PrimaryMission.TargetRoute);
        Assert.Equal(mission.PrimaryEntryPoint, dashboard.MissionControl.PrimaryEntryPoint);
        Assert.Equal(mission.EvidenceConfidence, dashboard.MissionControl.EvidenceConfidence);
        Assert.True(mission.ModuleCards.Select(c => c.ModuleKey).Order().SequenceEqual(
            dashboard.MissionControl.ModuleCards.Select(c => c.ModuleKey).Order()));
        Assert.True(mission.Sections.Select(s => s.SectionKey).Order().SequenceEqual(
            dashboard.MissionControl.Sections.Select(s => s.SectionKey).Order()));
        AssertSafePayload(JsonSerializer.Serialize(new { dashboard.MissionControl, dashboard.OrkaLearningState }, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task MissionControl_ProjectionContract_EmitsStableShapeAndAllowedValues()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "mission-contract");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Mission Projection Contract");
        await SeedRepairSignalsAsync(factory, user.UserId, topicId, includeStudyRoom: true);
        await SeedSourceWikiWarningAsync(factory, user.UserId, topicId);

        var mission = await GetMissionAsync(user, topicId);
        var json = JsonSerializer.Serialize(mission, JsonOptions);

        Assert.Equal(topicId, mission.TopicId);
        Assert.Equal("topic", mission.ScopeStatus);
        Assert.NotNull(mission.PrimaryMission);
        Assert.False(string.IsNullOrWhiteSpace(mission.PrimaryMission.MissionKey));
        Assert.Contains(mission.EvidenceConfidence, AllowedEvidenceConfidence);
        Assert.Equal(mission.PrimaryEntryPoint, mission.PrimaryMission.EntryPoint);
        Assert.Contains(mission.PrimaryMission.ActionType, AllowedActionTypes);
        Assert.Contains(mission.PrimaryMission.EntryPoint, AllowedEntryPoints);
        Assert.Contains(mission.PrimaryMission.TargetRoute, AllowedRoutes);
        Assert.Contains(mission.PrimaryMission.Priority, AllowedPriorities);
        Assert.Contains(mission.ReviewLoad, AllowedLoads);
        Assert.Contains(mission.RepairLoad, AllowedLoads);
        Assert.Contains(mission.ExamLoad, AllowedLoads);
        Assert.Contains(mission.SourceWikiLoad, AllowedLoads);
        Assert.NotEmpty(mission.ModuleCards);
        Assert.NotEmpty(mission.Sections);

        Assert.Contains(mission.PrimaryMission.ActionType, AllowedActionTypes);
        Assert.Contains(mission.PrimaryMission.EntryPoint, AllowedEntryPoints);
        Assert.Contains(mission.PrimaryMission.TargetRoute, AllowedRoutes);
        Assert.Contains(mission.PrimaryMission.Priority, AllowedPriorities);
        foreach (var action in mission.SecondaryActions)
        {
            Assert.Contains(action.ActionType, AllowedActionTypes);
            Assert.Contains(action.EntryPoint, AllowedEntryPoints);
            Assert.Contains(action.TargetRoute, AllowedRoutes);
            Assert.Contains(action.Priority, AllowedPriorities);
        }

        foreach (var warning in mission.UrgentWarnings)
        {
            Assert.False(string.IsNullOrWhiteSpace(warning.WarningCode));
            Assert.Contains(warning.Severity, AllowedWarningSeverities);
            Assert.Contains(warning.TargetRoute, AllowedRoutes);
        }

        foreach (var card in mission.ModuleCards)
        {
            Assert.Contains(card.ModuleKey, AllowedModuleKeys);
            Assert.Contains(card.Status, AllowedStatuses);
            Assert.Contains(card.EntryPoint, AllowedEntryPoints);
            Assert.Contains(card.TargetRoute, AllowedRoutes);
            Assert.Contains(card.Priority, AllowedPriorities);
            Assert.False(string.IsNullOrWhiteSpace(card.UserSafeSummary));
        }

        foreach (var section in mission.Sections)
        {
            Assert.Contains(section.SectionKey, AllowedSectionKeys);
            Assert.Contains(section.Status, AllowedStatuses);
            Assert.Contains(section.TargetRoute, AllowedRoutes);
            foreach (var action in section.Actions)
            {
                Assert.Contains(action.ActionType, AllowedActionTypes);
                Assert.Contains(action.EntryPoint, AllowedEntryPoints);
                Assert.Contains(action.TargetRoute, AllowedRoutes);
                Assert.Contains(action.Priority, AllowedPriorities);
            }

            foreach (var warning in section.Warnings)
            {
                Assert.False(string.IsNullOrWhiteSpace(warning.WarningCode));
                Assert.Contains(warning.Severity, AllowedWarningSeverities);
                Assert.Contains(warning.TargetRoute, AllowedRoutes);
            }
        }

        AssertSafePayload(json, user.UserId);
    }

    [Fact]
    public async Task MissionControl_BlocksCrossUserTopicAccess()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "mission-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "mission-other");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, owner.UserId, "Mission Private Topic");

        var response = await other.Client.GetAsync($"/api/learning/mission-control?topicId={topicId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<OrkaMissionControlDto> GetMissionAsync(CoordinationTestUser user, Guid? topicId)
    {
        var path = topicId.HasValue
            ? $"/api/learning/mission-control?topicId={topicId.Value}"
            : "/api/learning/mission-control";
        var response = await user.Client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrkaMissionControlDto>())!;
    }

    private static async Task SeedSingleWrongAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;

        db.QuizAttempts.Add(Attempt(userId, topicId, "single-wrong", correct: false, skipped: false, now.AddMinutes(-3)));
        await db.SaveChangesAsync();
    }

    private static async Task SeedDueReviewAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;

        db.ReviewItems.Add(new ReviewItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ReviewKey = $"mission-review:{topicId}",
            SkillTag = "Mission Due Review",
            ConceptTag = "mission-due-review",
            LearningObjective = "Mission Due Review",
            DueAt = now.AddDays(-2),
            Status = "active",
            CreatedAt = now.AddDays(-10),
            UpdatedAt = now.AddDays(-2)
        });

        await db.SaveChangesAsync();
    }

    private static async Task SeedRepairSignalsAsync(ApiSmokeFactory factory, Guid userId, Guid topicId, bool includeStudyRoom)
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
            Attempt(userId, topicId, "repeated-wrong", correct: false, skipped: false, now.AddMinutes(-12)),
            Attempt(userId, topicId, "repeated-wrong", correct: false, skipped: false, now.AddMinutes(-8)),
            Attempt(userId, topicId, "repeated-wrong", correct: false, skipped: false, now.AddMinutes(-4)),
            Attempt(userId, topicId, "blank-gap", correct: false, skipped: true, now.AddMinutes(-6)),
            Attempt(userId, topicId, "blank-gap", correct: false, skipped: true, now.AddMinutes(-3)));

        if (includeStudyRoom)
        {
            db.ClassroomSessions.Add(new ClassroomSession
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                Transcript = "[HOCA]: Safe personal study room context.",
                LastSegment = "Safe personal study room context.",
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now
            });
        }

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
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(factory, userId, topicId, "Mission Source", "safe source fixture");
        var pageId = await CoordinationTestHelpers.SeedWikiPageAsync(factory, userId, topicId, "Mission Wiki", "manual safe note");

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
        await lifecycle.MarkSourceStaleAsync(userId, sourceId, "mission_control_test_stale");
        await db.SaveChangesAsync();
    }

    private static async Task SeedExamWeakOutcomeAsync(ApiSmokeFactory factory, CoordinationTestUser user)
    {
        var ids = await GetKpssIdsAsync(factory);
        for (var i = 0; i < 5; i++)
        {
            await SeedQuestionAsync(factory, ids, $"Mission exam stem {i}");
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
