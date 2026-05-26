using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class ExamCurriculumDepthTests
{
    [Fact]
    public async Task RepeatedBlankAnswersCreateDiagnosticExamNextActionWithoutAnswerKeyLeak()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "exam-depth-blank");
        var ids = await GetKpssIdsAsync(factory);
        for (var i = 0; i < 3; i++)
        {
            await SeedQuestionAsync(factory, ids, $"Blank profile paragraph {i}");
        }

        var sessionResponse = await user.Client.PostAsJsonAsync(
            "/api/central-exams/kpss/turkce-paragraf/start",
            new PracticeStartRequestDto { Limit = 3 });
        sessionResponse.EnsureSuccessStatusCode();
        var sessionJson = await sessionResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("correctOptionKey", sessionJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", sessionJson, StringComparison.OrdinalIgnoreCase);
        var session = JsonSerializer.Deserialize<PracticeSessionDto>(sessionJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var submit = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/turkce-paragraf/submit", new PracticeSubmitRequestDto
        {
            PracticeSetId = session.PracticeSetId,
            Answers =
            [
                new PracticeAnswerDto { QuestionId = session.Questions[0].QuestionId, SelectedOptionKey = "B" },
                new PracticeAnswerDto { QuestionId = session.Questions[1].QuestionId, SelectedOptionKey = null },
                new PracticeAnswerDto { QuestionId = session.Questions[2].QuestionId, SelectedOptionKey = null }
            ]
        });
        submit.EnsureSuccessStatusCode();

        var profile = await GetProfileAsync(user);
        var outcome = profile.Outcomes.Single(o => o.ExamOutcomeId == ids.OutcomeId);

        Assert.Equal("prerequisite_gap", outcome.ReadinessStatus);
        Assert.Equal("run_diagnostic", outcome.RecommendedAction);
        Assert.Contains("repeated_blank", outcome.ReasonCodes);
        Assert.Contains(profile.NextActions, a => a.ActionType == "run_diagnostic" && a.OutcomeCode == outcome.OutcomeCode);
        Assert.False(profile.CanClaimOfficial);
        Assert.Contains("source_unverified", profile.Warnings);
        AssertSafeProfile(profile);
    }

    [Fact]
    public async Task DenemeMistakeClusterCreatesReviewDenemeMistakesAction()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "exam-depth-deneme");
        var ids = await GetKpssIdsAsync(factory);
        for (var i = 0; i < 5; i++)
        {
            await SeedQuestionAsync(factory, ids, $"Deneme profile paragraph {i}");
        }

        var session = await StartDenemeAsync(user);
        var submit = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/denemeler/submit", new CentralExamDenemeSubmitRequestDto
        {
            DenemeAttemptId = session.DenemeAttemptId,
            Answers = session.Questions.Select((q, index) => new CentralExamDenemeAnswerDto
            {
                QuestionId = q.QuestionId,
                SelectedOptionKey = index < 2 ? "B" : "A"
            }).ToList()
        });
        submit.EnsureSuccessStatusCode();

        var profile = await GetProfileAsync(user);
        var outcome = profile.Outcomes.Single(o => o.ExamOutcomeId == ids.OutcomeId);

        Assert.Equal("weak", outcome.ReadinessStatus);
        Assert.Equal("review_deneme_mistakes", outcome.RecommendedAction);
        Assert.Contains("deneme_mistake_cluster", outcome.ReasonCodes);
        Assert.Contains(profile.NextActions, a => a.ActionType == "review_deneme_mistakes");
        AssertSafeProfile(profile);
    }

    [Fact]
    public async Task RepeatedSuccessMarksOutcomeStableWithoutGuaranteeClaim()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "exam-depth-stable");
        var ids = await GetKpssIdsAsync(factory);
        for (var i = 0; i < 3; i++)
        {
            await SeedQuestionAsync(factory, ids, $"Stable profile paragraph {i}");
        }

        var session = await StartPracticeAsync(user, 3);
        var submit = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/turkce-paragraf/submit", new PracticeSubmitRequestDto
        {
            PracticeSetId = session.PracticeSetId,
            Answers = session.Questions.Select(q => new PracticeAnswerDto
            {
                QuestionId = q.QuestionId,
                SelectedOptionKey = "A"
            }).ToList()
        });
        submit.EnsureSuccessStatusCode();

        var profile = await GetProfileAsync(user);
        var outcome = profile.Outcomes.Single(o => o.ExamOutcomeId == ids.OutcomeId);

        Assert.Equal("stable", outcome.ReadinessStatus);
        Assert.Equal("continue_exam_plan", outcome.RecommendedAction);
        Assert.Contains(outcome.OutcomeCode, profile.StableOutcomes);
        Assert.DoesNotContain("guarantee", JsonSerializer.Serialize(profile), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%100", JsonSerializer.Serialize(profile), StringComparison.OrdinalIgnoreCase);
        AssertSafeProfile(profile);
    }

    [Fact]
    public async Task DashboardAndTutorConsumeExamLearningProfile()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "exam-depth-dashboard");
        var ids = await GetKpssIdsAsync(factory);
        await SeedQuestionAsync(factory, ids, "Dashboard profile paragraph one");
        await SeedQuestionAsync(factory, ids, "Dashboard profile paragraph two");

        var session = await StartPracticeAsync(user, 2);
        var submit = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/turkce-paragraf/submit", new PracticeSubmitRequestDto
        {
            PracticeSetId = session.PracticeSetId,
            Answers = session.Questions.Select(q => new PracticeAnswerDto
            {
                QuestionId = q.QuestionId,
                SelectedOptionKey = "B"
            }).ToList()
        });
        submit.EnsureSuccessStatusCode();

        var dashboard = await user.Client.GetFromJsonAsync<DashboardTodayDto>("/api/dashboard/today");
        Assert.NotNull(dashboard?.ExamLearningProfile);
        Assert.Contains(dashboard!.ExamLearningProfile!.NextActions, a => a.ActionType == "repair_outcome");

        var tutorActions = await user.Client.GetFromJsonAsync<List<TutorNextLearningActionDto>>("/api/tutor/next-actions");
        Assert.NotNull(tutorActions);
        Assert.Contains(tutorActions!, a => a.ActionType == "start_micro_quiz");
    }

    private static async Task<ExamLearningProfileDto> GetProfileAsync(CoordinationTestUser user)
    {
        var response = await user.Client.GetAsync("/api/central-exams/kpss/learning-profile");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ExamLearningProfileDto>())!;
    }

    private static async Task<PracticeSessionDto> StartPracticeAsync(CoordinationTestUser user, int limit)
    {
        var response = await user.Client.PostAsJsonAsync(
            "/api/central-exams/kpss/turkce-paragraf/start",
            new PracticeStartRequestDto { Limit = limit });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PracticeSessionDto>())!;
    }

    private static async Task<CentralExamDenemeSessionDto> StartDenemeAsync(CoordinationTestUser user)
    {
        var response = await user.Client.PostAsJsonAsync(
            "/api/central-exams/kpss/denemeler/KPSS_MINI_TURKCE_PARAGRAF/start",
            new CentralExamDenemeStartRequestDto());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CentralExamDenemeSessionDto>())!;
    }

    private static async Task<KpssPath> GetKpssIdsAsync(ApiSmokeFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IExamFrameworkService>();
        var tree = await service.CreateSystemSkeletonAsync();
        var variant = tree.Variants.Single(v => v.Code == "KPSS_LISANS");
        var generalAbility = variant.Sections.Single(s => s.Code == "GENEL_YETENEK");
        var turkce = generalAbility.Subjects.Single(s => s.Code == "TURKCE");
        var paragrafTopic = turkce.Topics.Single(t => t.Code == "PARAGRAF");
        return new KpssPath(tree.Id, variant.Id, generalAbility.Id, turkce.Id, paragrafTopic.Id, paragrafTopic.Outcomes.Single().Id);
    }

    private static async Task<Guid> SeedQuestionAsync(ApiSmokeFactory factory, KpssPath ids, string stem)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var question = new QuestionItem
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
            Explanation = "The correct option is A.",
            Options =
            [
                new QuestionOption { OptionKey = "A", Text = "Correct option", IsCorrect = true, SortOrder = 0 },
                new QuestionOption { OptionKey = "B", Text = "Wrong option", IsCorrect = false, SortOrder = 1 }
            ],
            OutcomeLinks =
            [
                new QuestionOutcomeLink { ExamOutcomeId = ids.OutcomeId, IsPrimary = true, LinkStrength = 1.0m }
            ]
        };

        db.QuestionItems.Add(question);
        await db.SaveChangesAsync();
        return question.Id;
    }

    private static void AssertSafeProfile(ExamLearningProfileDto profile)
    {
        var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions(JsonSerializerDefaults.Web));
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
            "token",
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
    }

    private sealed record KpssPath(Guid DefinitionId, Guid VariantId, Guid SectionId, Guid SubjectId, Guid TopicId, Guid OutcomeId);
}
