using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class OrkaExamWarRoomTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task WarRoom_NewExamLearnerDegradesSafelyWithoutSuccessClaim()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "war-room-new");

        var warRoom = await GetWarRoomAsync(user);

        Assert.Equal("KPSS", warRoom.ActiveExam.ExamCode);
        Assert.Contains(warRoom.TodayExamMission.ActionType, new[] { "run_exam_diagnostic", "source_review", "continue_exam_plan" });
        Assert.Contains(warRoom.ReasonCodes, r => r is "thin_evidence" or "diagnostic_needed" or "source_unverified" or "question_coverage_limited");
        Assert.Contains(warRoom.CurriculumCoverageWarnings.Concat(warRoom.SourceWikiWarnings), w => w.WarningCode is "thin_exam_evidence" or "source_unverified" or "question_coverage_limited" or "official_claim_blocked");
        AssertSafePayload(JsonSerializer.Serialize(warRoom, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task WarRoom_RepeatedBlankCreatesDiagnosticActionWithoutMisconceptionCertaintyOrAnswerKeyLeak()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "war-room-blank");
        var ids = await GetKpssIdsAsync(factory);
        for (var i = 0; i < 3; i++)
        {
            await SeedQuestionAsync(factory, ids, $"War blank paragraph {i}");
        }

        var session = await StartPracticeAsync(user, 3);
        await SubmitPracticeAsync(user, session, q => null);

        var warRoom = await GetWarRoomAsync(user);
        var outcome = Assert.Single(warRoom.WeakOutcomes.Where(o => o.ExamOutcomeId == ids.OutcomeId));

        Assert.Equal("prerequisite_gap", outcome.ReadinessStatus);
        Assert.Equal("run_exam_diagnostic", outcome.RecommendedAction);
        Assert.Contains("repeated_blank", outcome.ReasonCodes);
        Assert.Equal("run_exam_diagnostic", warRoom.TodayExamMission.ActionType);
        Assert.Contains(warRoom.TutorRepairHandoffs, a => a.EntryPoint == "ask_tutor");
        var json = JsonSerializer.Serialize(warRoom, JsonOptions);
        Assert.DoesNotContain("misconception", json, StringComparison.OrdinalIgnoreCase);
        AssertSafePayload(json, user.UserId);
    }

    [Fact]
    public async Task WarRoom_DenemeMistakeClusterCreatesReviewMissionAndDashboardCarriesWarRoom()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "war-room-deneme");
        var ids = await GetKpssIdsAsync(factory);
        for (var i = 0; i < 5; i++)
        {
            await SeedQuestionAsync(factory, ids, $"War deneme paragraph {i}");
        }

        var session = await StartDenemeAsync(user);
        await SubmitDenemeAsync(user, session, index => index < 3 ? "B" : "A");

        var warRoom = await GetWarRoomAsync(user);

        Assert.Equal("review_deneme_mistakes", warRoom.TodayExamMission.ActionType);
        Assert.Contains(warRoom.DenemeMistakeClusters, c => c.OutcomeCode == warRoom.TodayExamMission.OutcomeCode);
        Assert.Contains(warRoom.WeeklyExamPlan, a => a.ActionType == "review_deneme_mistakes");
        Assert.Contains(warRoom.TutorRepairHandoffs, a => a.ActionType == "review_deneme_mistakes");
        Assert.Contains(warRoom.WeakQuestionTypes, p => p.QuestionType == "multiple_choice");

        var dashboard = await user.Client.GetFromJsonAsync<DashboardTodayDto>("/api/dashboard/today")
            ?? throw new InvalidOperationException("Dashboard payload missing.");
        Assert.NotNull(dashboard.ExamWarRoom);
        Assert.Equal(warRoom.TodayExamMission.ActionType, dashboard.ExamWarRoom!.TodayExamMission.ActionType);
        AssertSafePayload(JsonSerializer.Serialize(new { warRoom, dashboard.ExamWarRoom }, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task WarRoom_StableSuccessAllowsContinuePlanWithoutGuarantee()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "war-room-stable");
        var ids = await GetKpssIdsAsync(factory);
        for (var i = 0; i < 3; i++)
        {
            await SeedQuestionAsync(factory, ids, $"War stable paragraph {i}");
        }

        var session = await StartPracticeAsync(user, 3);
        await SubmitPracticeAsync(user, session, _ => "A");

        var warRoom = await GetWarRoomAsync(user);

        Assert.Contains(warRoom.StableOutcomes, o => o.ExamOutcomeId == ids.OutcomeId);
        Assert.Contains(warRoom.WeeklyExamPlan, a => a.ActionType == "continue_exam_plan");
        Assert.DoesNotContain(warRoom.WeakOutcomes, o => o.ExamOutcomeId == ids.OutcomeId);
        AssertSafePayload(JsonSerializer.Serialize(warRoom, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task WarRoom_SourceUnverifiedCreatesHonestWarningsNotOfficialClaim()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "war-room-source");
        var ids = await GetKpssIdsAsync(factory);
        await SeedQuestionAsync(factory, ids, "War source paragraph one");
        await SeedQuestionAsync(factory, ids, "War source paragraph two");

        var warRoom = await GetWarRoomAsync(user);

        Assert.False(warRoom.ActiveExam.CanClaimOfficial);
        Assert.Contains(warRoom.SourceWikiWarnings, w => w.WarningCode is "source_unverified" or "official_claim_blocked");
        Assert.Contains(warRoom.ReasonCodes, r => r is "source_unverified" or "official_claim_blocked");
        var json = JsonSerializer.Serialize(warRoom, JsonOptions);
        Assert.DoesNotContain("official curriculum complete", json, StringComparison.OrdinalIgnoreCase);
        AssertSafePayload(json, user.UserId);
    }

    [Fact]
    public async Task WarRoom_StudyRoomHandoffRequiresSafeTopicContext()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "war-room-study-room");
        var ids = await GetKpssIdsAsync(factory);
        for (var i = 0; i < 5; i++)
        {
            await SeedQuestionAsync(factory, ids, $"War study room paragraph {i}");
        }

        var withoutTopicSession = await StartDenemeAsync(user);
        await SubmitDenemeAsync(user, withoutTopicSession, index => index < 2 ? "B" : "A");
        var withoutTopic = await GetWarRoomAsync(user);
        Assert.Empty(withoutTopic.StudyRoomHandoffs);

        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "War Room Personal Study Room Topic");
        _ = topicId;
        var withTopic = await GetWarRoomAsync(user);

        Assert.Contains(withTopic.StudyRoomHandoffs, a => a.ActionType == "open_study_room" && a.TargetRoute == "classroom");
        Assert.Contains(withTopic.StudyRoomHandoffs.SelectMany(a => a.ReasonCodes), r => r == "study_room_available");
        AssertSafePayload(JsonSerializer.Serialize(withTopic, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task WarRoom_DoesNotLeakOwnerExamEvidenceAcrossUsers()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "war-room-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "war-room-other");
        var ids = await GetKpssIdsAsync(factory);
        for (var i = 0; i < 5; i++)
        {
            await SeedQuestionAsync(factory, ids, $"War cross user paragraph {i}");
        }

        var session = await StartDenemeAsync(owner);
        await SubmitDenemeAsync(owner, session, index => index < 3 ? "B" : "A");

        var ownerWarRoom = await GetWarRoomAsync(owner);
        var otherWarRoom = await GetWarRoomAsync(other);

        Assert.Contains(ownerWarRoom.DenemeMistakeClusters, c => c.MistakeCount >= 2);
        Assert.Empty(otherWarRoom.DenemeMistakeClusters);
        Assert.DoesNotContain(owner.UserId.ToString(), JsonSerializer.Serialize(otherWarRoom, JsonOptions), StringComparison.OrdinalIgnoreCase);
        AssertSafePayload(JsonSerializer.Serialize(otherWarRoom, JsonOptions), other.UserId);
    }

    private static async Task<OrkaExamWarRoomDto> GetWarRoomAsync(CoordinationTestUser user)
    {
        var response = await user.Client.GetAsync("/api/central-exams/kpss/war-room");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrkaExamWarRoomDto>())!;
    }

    private static async Task<PracticeSessionDto> StartPracticeAsync(CoordinationTestUser user, int limit)
    {
        var response = await user.Client.PostAsJsonAsync(
            "/api/central-exams/kpss/turkce-paragraf/start",
            new PracticeStartRequestDto { Limit = limit });
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("correctOptionKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", json, StringComparison.OrdinalIgnoreCase);
        return JsonSerializer.Deserialize<PracticeSessionDto>(json, JsonOptions)!;
    }

    private static async Task SubmitPracticeAsync(
        CoordinationTestUser user,
        PracticeSessionDto session,
        Func<PracticeQuestionDto, string?> selected)
    {
        var submit = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/turkce-paragraf/submit", new PracticeSubmitRequestDto
        {
            PracticeSetId = session.PracticeSetId,
            Answers = session.Questions.Select(q => new PracticeAnswerDto
            {
                QuestionId = q.QuestionId,
                SelectedOptionKey = selected(q)
            }).ToList()
        });
        submit.EnsureSuccessStatusCode();
    }

    private static async Task<CentralExamDenemeSessionDto> StartDenemeAsync(CoordinationTestUser user)
    {
        var response = await user.Client.PostAsJsonAsync(
            "/api/central-exams/kpss/denemeler/KPSS_MINI_TURKCE_PARAGRAF/start",
            new CentralExamDenemeStartRequestDto());
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("correctOptionKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", json, StringComparison.OrdinalIgnoreCase);
        return JsonSerializer.Deserialize<CentralExamDenemeSessionDto>(json, JsonOptions)!;
    }

    private static async Task SubmitDenemeAsync(
        CoordinationTestUser user,
        CentralExamDenemeSessionDto session,
        Func<int, string> selected)
    {
        var submit = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/denemeler/submit", new CentralExamDenemeSubmitRequestDto
        {
            DenemeAttemptId = session.DenemeAttemptId,
            Answers = session.Questions.Select((q, index) => new CentralExamDenemeAnswerDto
            {
                QuestionId = q.QuestionId,
                SelectedOptionKey = selected(index)
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
        Assert.DoesNotContain("score guarantee", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("percentile", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("placement", json, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record KpssPath(
        Guid DefinitionId,
        Guid VariantId,
        Guid SectionId,
        Guid SubjectId,
        Guid TopicId,
        Guid OutcomeId);
}
