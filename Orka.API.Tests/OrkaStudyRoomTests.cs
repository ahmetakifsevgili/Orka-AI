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

public sealed class OrkaStudyRoomTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task StudyRoom_NewLearnerDegradesSafelyToQuickStart()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "study-room-new");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Study Room New");

        var room = await GetStudyRoomAsync(user, topicId);

        Assert.Equal("quick_start", room.StudyRoomMode);
        Assert.Contains(room.SessionReadiness, new[] { "ready", "limited" });
        Assert.Contains(room.Warnings, w => w.WarningCode == "thin_evidence");
        Assert.Contains(room.Roles, r => r.RoleKey == "ai_teacher");
        Assert.False(room.CheckpointPlan.KeyVisible);
        Assert.DoesNotContain("stable", JsonSerializer.Serialize(room, JsonOptions), StringComparison.OrdinalIgnoreCase);
        AssertSafePayload(JsonSerializer.Serialize(room, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task StudyRoom_OneWrongAnswerDoesNotCreateRepairLesson()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "study-room-one-wrong");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Study Room One Wrong");
        await SeedAttemptAsync(factory, user.UserId, topicId, "single-wrong", correct: false, skipped: false, DateTime.UtcNow.AddMinutes(-3));

        var room = await GetStudyRoomAsync(user, topicId);

        Assert.NotEqual("repair_lesson", room.StudyRoomMode);
        Assert.DoesNotContain(room.NextActions, a => a.Priority == "urgent" && a.ActionType == "start_repair_lesson");
        AssertSafePayload(JsonSerializer.Serialize(room, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task StudyRoom_RepeatedWrongStartsRepairLessonAndWritesSafeTrace()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "study-room-repair");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Study Room Repair");
        await SeedRepairSignalsAsync(factory, user.UserId, topicId);

        var room = await GetStudyRoomAsync(user, topicId);
        var started = await StartStudyRoomAsync(user, topicId);

        Assert.Equal("repair_lesson", room.StudyRoomMode);
        Assert.Contains(room.NextActions, a => a.ActionType == "start_repair_lesson");
        Assert.NotNull(started.ClassroomSessionId);
        Assert.Equal("repair_lesson", started.StudyRoomMode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var session = await db.ClassroomSessions.SingleAsync(s => s.Id == started.ClassroomSessionId);
        Assert.DoesNotContain("rawPrompt", session.Transcript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawSourceChunk", session.Transcript, StringComparison.OrdinalIgnoreCase);
        AssertSafePayload(JsonSerializer.Serialize(started, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task StudyRoom_BlankSkippedUsesGuidedRepairWithoutFakeMisconception()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "study-room-blank");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Study Room Blank");
        await SeedBlankSignalsAsync(factory, user.UserId, topicId);

        var room = await GetStudyRoomAsync(user, topicId);

        Assert.Equal("repair_lesson", room.StudyRoomMode);
        Assert.Contains(room.ReasonCodes, r => r is "repeated_blank" or "prerequisite_gap");
        Assert.DoesNotContain("misconception", JsonSerializer.Serialize(room, JsonOptions), StringComparison.OrdinalIgnoreCase);
        AssertSafePayload(JsonSerializer.Serialize(room, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task StudyRoom_DueReviewCreatesReviewLesson()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "study-room-review");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Study Room Review");
        await SeedDueReviewAsync(factory, user.UserId, topicId);

        var room = await GetStudyRoomAsync(user, topicId);

        Assert.Equal("review_lesson", room.StudyRoomMode);
        Assert.Contains(room.ReviewHandoffs, a => a.ActionType == "start_review_lesson" || a.ActionType == "review_due");
        AssertSafePayload(JsonSerializer.Serialize(room, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task StudyRoom_ExamWeakOutcomeCreatesExamPracticeWithoutGuarantee()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "study-room-exam");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Study Room Exam");
        await SeedExamWeakOutcomeAsync(factory, user);

        var room = await GetStudyRoomAsync(user, topicId);
        var json = JsonSerializer.Serialize(room, JsonOptions);

        Assert.Equal("exam_outcome_practice", room.StudyRoomMode);
        Assert.Contains(room.NextActions, a => a.ActionType == "start_exam_outcome_practice");
        Assert.DoesNotContain("guarantee", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%100", json, StringComparison.OrdinalIgnoreCase);
        AssertSafePayload(json, user.UserId);
    }

    [Fact]
    public async Task StudyRoom_SourceInsufficientBlocksSourceGroundedLesson()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "study-room-source");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Study Room Source");
        var sourceId = await SeedSourceWarningAsync(factory, user.UserId, topicId);

        var room = await GetStudyRoomAsync(user, topicId, sourceId: sourceId, mode: "source_grounded");

        Assert.Equal("source_review_lesson", room.StudyRoomMode);
        Assert.Equal("blocked", room.SessionReadiness);
        Assert.Contains(room.Warnings, w => w.WarningCode == "source_grounding_blocked");
        Assert.Contains(room.SourceWikiHandoffs, a => a.TargetRoute is "sources" or "wiki");
        AssertSafePayload(JsonSerializer.Serialize(room, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task StudyRoom_WikiRepairPendingCreatesWikiRepairLesson()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "study-room-wiki");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Study Room Wiki");
        var pageId = await SeedWikiRepairAsync(factory, user.UserId, topicId);

        var room = await GetStudyRoomAsync(user, topicId, wikiPageId: pageId, mode: "wiki_repair");

        Assert.Equal("wiki_repair_lesson", room.StudyRoomMode);
        Assert.Contains(room.NextActions, a => a.ActionType is "start_wiki_repair_lesson" or "update_wiki_note");
        Assert.Contains(room.SourceWikiHandoffs, a => a.TargetRoute == "wiki");
        AssertSafePayload(JsonSerializer.Serialize(room, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task StudyRoom_CheckpointHidesKeyAndStoresOnlyBoundedSafeSummary()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "study-room-checkpoint");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Study Room Checkpoint");
        await SeedRepairSignalsAsync(factory, user.UserId, topicId);
        var started = await StartStudyRoomAsync(user, topicId);

        var checkpoint = await user.Client.PostAsJsonAsync("/api/classroom/study-room/checkpoint", new OrkaStudyRoomCheckpointRequestDto
        {
            ClassroomSessionId = started.ClassroomSessionId!.Value,
            ResponseSignal = "wrong",
            AnswerText = "student private rawPrompt rawSourceChunk token_marker_secret_value C:\\secret\\answer.txt",
            ConceptKey = "repair-concept"
        });
        checkpoint.EnsureSuccessStatusCode();
        var room = (await checkpoint.Content.ReadFromJsonAsync<OrkaStudyRoomDto>())!;

        Assert.Equal("needs_repair", room.CheckpointPlan.CheckpointStatus);
        Assert.False(room.CheckpointPlan.KeyVisible);
        Assert.Contains(room.CheckpointPlan.ReasonCodes, r => r is "recent_wrong_answer" or "repair_pending");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var interaction = await db.ClassroomInteractions.SingleAsync(i => i.ClassroomSessionId == started.ClassroomSessionId);
        Assert.DoesNotContain("student private", interaction.AnswerScript, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawPrompt", interaction.AnswerScript, StringComparison.OrdinalIgnoreCase);
        AssertSafePayload(JsonSerializer.Serialize(room, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task StudyRoom_DashboardCarriesContractAndCrossUserAccessIsBlocked()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "study-room-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "study-room-other");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, owner.UserId, "Study Room Private");

        var dashboard = await owner.Client.GetFromJsonAsync<DashboardTodayDto>("/api/dashboard/today")
            ?? throw new InvalidOperationException("Dashboard payload missing.");
        var denied = await other.Client.GetAsync($"/api/classroom/study-room?topicId={topicId}");

        Assert.NotNull(dashboard.StudyRoom);
        Assert.Contains(dashboard.StudyRoom!.StudyRoomMode, new[] { "quick_start", "continue_plan" });
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        AssertSafePayload(JsonSerializer.Serialize(dashboard.StudyRoom, JsonOptions), owner.UserId);
    }

    private static async Task<OrkaStudyRoomDto> GetStudyRoomAsync(
        CoordinationTestUser user,
        Guid? topicId,
        Guid? sourceId = null,
        Guid? wikiPageId = null,
        string? mode = null)
    {
        var query = new List<string>();
        if (topicId.HasValue) query.Add($"topicId={topicId.Value}");
        if (sourceId.HasValue) query.Add($"sourceId={sourceId.Value}");
        if (wikiPageId.HasValue) query.Add($"wikiPageId={wikiPageId.Value}");
        if (!string.IsNullOrWhiteSpace(mode)) query.Add($"mode={Uri.EscapeDataString(mode)}");
        var response = await user.Client.GetAsync("/api/classroom/study-room" + (query.Count == 0 ? string.Empty : "?" + string.Join("&", query)));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrkaStudyRoomDto>())!;
    }

    private static async Task<OrkaStudyRoomDto> StartStudyRoomAsync(CoordinationTestUser user, Guid topicId)
    {
        var response = await user.Client.PostAsJsonAsync("/api/classroom/study-room/start", new OrkaStudyRoomStartRequestDto
        {
            TopicId = topicId
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrkaStudyRoomDto>())!;
    }

    private static async Task SeedAttemptAsync(ApiSmokeFactory factory, Guid userId, Guid topicId, string conceptKey, bool correct, bool skipped, DateTime createdAt)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        db.QuizAttempts.Add(Attempt(userId, topicId, conceptKey, correct, skipped, createdAt));
        await db.SaveChangesAsync();
    }

    private static async Task SeedRepairSignalsAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;
        db.KnowledgeTracingStates.Add(new KnowledgeTracingState
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ConceptKey = "repair-concept",
            Label = "Repair Concept",
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
        });
        db.QuizAttempts.AddRange(
            Attempt(userId, topicId, "repair-concept", false, false, now.AddMinutes(-12)),
            Attempt(userId, topicId, "repair-concept", false, false, now.AddMinutes(-8)),
            Attempt(userId, topicId, "repair-concept", false, false, now.AddMinutes(-4)));
        await db.SaveChangesAsync();
    }

    private static async Task SeedBlankSignalsAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;
        db.KnowledgeTracingStates.Add(new KnowledgeTracingState
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
            Attempt(userId, topicId, "blank-gap", false, true, now.AddMinutes(-6)),
            Attempt(userId, topicId, "blank-gap", false, true, now.AddMinutes(-3)));
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
            ReviewKey = $"study-room-review:{topicId}",
            SkillTag = "Study Room Due Review",
            ConceptTag = "study-room-due-review",
            LearningObjective = "Study Room Due Review",
            DueAt = now.AddDays(-2),
            Status = "active",
            CreatedAt = now.AddDays(-10),
            UpdatedAt = now.AddDays(-2)
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedSourceWarningAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(factory, userId, topicId, "Study Room Source", "safe source fixture rawSourceChunk secret");
        using var scope = factory.Services.CreateScope();
        var lifecycle = scope.ServiceProvider.GetRequiredService<ISourceEvidenceLifecycleService>();
        await lifecycle.BuildSourceEvidenceBundleAsync(userId, topicId, question: "safe source study room");
        await lifecycle.MarkSourceStaleAsync(userId, sourceId, "study_room_test_stale");
        return sourceId;
    }

    private static async Task<Guid> SeedWikiRepairAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        var pageId = await CoordinationTestHelpers.SeedWikiPageAsync(factory, userId, topicId, "Study Room Wiki", "manual note");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var page = await db.WikiPages.SingleAsync(p => p.Id == pageId);
        page.ConceptKey = "wiki-repair";
        page.SourceReadiness = "evidence_insufficient";
        page.EvidenceStatus = "evidence_insufficient";
        var block = await db.WikiBlocks.SingleAsync(b => b.WikiPageId == pageId);
        block.BlockType = WikiBlockType.RepairNote;
        block.ConceptKey = "wiki-repair";
        block.SourceBasis = "evidence_insufficient";
        block.SafetyWarningsJson = "[\"source_limited\"]";
        await db.SaveChangesAsync();
        return pageId;
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

    private static async Task SeedExamWeakOutcomeAsync(ApiSmokeFactory factory, CoordinationTestUser user)
    {
        var ids = await GetKpssIdsAsync(factory);
        for (var i = 0; i < 5; i++)
        {
            await SeedQuestionAsync(factory, ids, $"Study room exam stem {i}");
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
        foreach (var marker in new[]
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
            "therapy",
            "therapist",
            "psychologist",
            "medical",
            "diagnosis",
            "ADHD",
            "burnout",
            "mental health",
            "wellbeing"
        })
        {
            Assert.DoesNotContain(marker, json, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(userId.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("success guarantee", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("official curriculum complete", json, StringComparison.OrdinalIgnoreCase);
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
