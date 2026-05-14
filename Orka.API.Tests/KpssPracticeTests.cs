using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class KpssPracticeTests
{
    [Fact]
    public async Task StartSelectsOnlyPublishedKpssTurkceParagrafQuestionsAndHidesAnswers()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "kpss-practice-start");
        var ids = await GetKpssIdsAsync(factory);
        await SeedQuestionAsync(factory, ids.Paragraf, ownerUserId: null, qualityStatus: "published", stem: "Published paragraph 1");
        await SeedQuestionAsync(factory, ids.Paragraf, user.UserId, "published", "Published paragraph 2");
        await SeedQuestionAsync(factory, ids.Paragraf, user.UserId, "draft", "Draft paragraph");
        await SeedQuestionAsync(factory, ids.Paragraf, user.UserId, "needs_review", "Review paragraph");
        await SeedQuestionAsync(factory, ids.Math, ownerUserId: null, qualityStatus: "published", stem: "Published math");

        var response = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/turkce-paragraf/start", new PracticeStartRequestDto { Limit = 10 });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var session = JsonSerializer.Deserialize<PracticeSessionDto>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(session);
        Assert.Equal("ready", session!.Status);
        Assert.Equal(2, session.TotalQuestions);
        Assert.All(session.Questions, q =>
        {
            Assert.Equal("KPSS", q.ExamContext.ExamCode);
            Assert.Equal("TURKCE", q.ExamContext.SubjectCode);
            Assert.Equal("PARAGRAF", q.ExamContext.TopicCode);
        });
        Assert.DoesNotContain("Draft paragraph", body);
        Assert.DoesNotContain("Review paragraph", body);
        Assert.DoesNotContain("Published math", body);
        Assert.DoesNotContain("isCorrect", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Paragrafın ana fikri", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartDoesNotLeakOtherUsersPrivateQuestions()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "kpss-practice-user");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "kpss-practice-other");
        var ids = await GetKpssIdsAsync(factory);
        await SeedQuestionAsync(factory, ids.Paragraf, other.UserId, "published", "Other private paragraph");

        var session = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/turkce-paragraf/start", new PracticeStartRequestDto { Limit = 10 });
        session.EnsureSuccessStatusCode();
        var body = await session.Content.ReadAsStringAsync();

        Assert.DoesNotContain("Other private paragraph", body);
        var dto = JsonSerializer.Deserialize<PracticeSessionDto>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.Equal("empty", dto!.Status);
    }

    [Fact]
    public async Task SubmitReturnsCorrectnessExplanationAndSummaryCounts()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "kpss-practice-submit");
        var ids = await GetKpssIdsAsync(factory);
        var correct = await SeedQuestionAsync(factory, ids.Paragraf, ownerUserId: null, qualityStatus: "published", stem: "Correct paragraph");
        var wrong = await SeedQuestionAsync(factory, ids.Paragraf, ownerUserId: null, qualityStatus: "published", stem: "Wrong paragraph");
        var blank = await SeedQuestionAsync(factory, ids.Paragraf, ownerUserId: null, qualityStatus: "published", stem: "Blank paragraph");
        var start = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/turkce-paragraf/start", new PracticeStartRequestDto { Limit = 10 });
        start.EnsureSuccessStatusCode();
        var session = await start.Content.ReadFromJsonAsync<PracticeSessionDto>();

        var response = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/turkce-paragraf/submit", new PracticeSubmitRequestDto
        {
            PracticeSetId = session!.PracticeSetId,
            Answers =
            [
                new PracticeAnswerDto { QuestionId = correct, SelectedOptionKey = "A" },
                new PracticeAnswerDto { QuestionId = wrong, SelectedOptionKey = "B" },
                new PracticeAnswerDto { QuestionId = blank, SelectedOptionKey = null }
            ]
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PracticeResultDto>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(result);
        Assert.Equal(3, result!.TotalQuestions);
        Assert.Equal(2, result.AnsweredCount);
        Assert.Equal(1, result.CorrectCount);
        Assert.Equal(1, result.WrongCount);
        Assert.Equal(1, result.BlankCount);
        Assert.All(result.Results, item =>
        {
            Assert.Equal("A", item.CorrectOptionKey);
            Assert.Contains("Paragrafın ana fikri", item.Explanation);
            Assert.Equal("KPSS", item.ExamContext.ExamCode);
            Assert.Equal("PARAGRAF", item.ExamContext.TopicCode);
        });
        Assert.Single(result.TopicBreakdown);
    }

    [Fact]
    public async Task SubmitRejectsInvisibleDeletedPrivateOrOutOfScopeQuestions()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "kpss-practice-guard");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "kpss-practice-guard-other");
        var ids = await GetKpssIdsAsync(factory);
        var included = await SeedQuestionAsync(factory, ids.Paragraf, null, "published", "Included paragraph");
        var privateQuestion = await SeedQuestionAsync(factory, ids.Paragraf, other.UserId, "published", "Private other");
        var deletedQuestion = await SeedQuestionAsync(factory, ids.Paragraf, null, "published", "Deleted question", isDeleted: true);
        var outOfScope = await SeedQuestionAsync(factory, ids.Math, null, "published", "Math scope");
        var start = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/turkce-paragraf/start", new PracticeStartRequestDto { Limit = 10 });
        start.EnsureSuccessStatusCode();
        var session = await start.Content.ReadFromJsonAsync<PracticeSessionDto>();
        Assert.Contains(session!.Questions, q => q.QuestionId == included);

        Assert.Equal(HttpStatusCode.BadRequest, (await SubmitOneAsync(user, session.PracticeSetId, privateQuestion)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SubmitOneAsync(user, session.PracticeSetId, deletedQuestion)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SubmitOneAsync(user, session.PracticeSetId, outOfScope)).StatusCode);
    }

    private static Task<HttpResponseMessage> SubmitOneAsync(CoordinationTestUser user, Guid practiceSetId, Guid questionId) =>
        user.Client.PostAsJsonAsync("/api/central-exams/kpss/turkce-paragraf/submit", new PracticeSubmitRequestDto
        {
            PracticeSetId = practiceSetId,
            Answers = [new PracticeAnswerDto { QuestionId = questionId, SelectedOptionKey = "A" }]
        });

    private static async Task<KpssIds> GetKpssIdsAsync(ApiSmokeFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IExamFrameworkService>();
        var tree = await service.CreateSystemSkeletonAsync();
        var variant = tree.Variants.Single(v => v.Code == "KPSS_LISANS");
        var generalAbility = variant.Sections.Single(s => s.Code == "GENEL_YETENEK");
        var turkce = generalAbility.Subjects.Single(s => s.Code == "TURKCE");
        var paragrafTopic = turkce.Topics.Single(t => t.Code == "PARAGRAF");
        var math = generalAbility.Subjects.Single(s => s.Code == "MATEMATIK");
        var mathTopic = math.Topics.Single();

        return new KpssIds(
            new KpssPath(tree.Id, variant.Id, generalAbility.Id, turkce.Id, paragrafTopic.Id, paragrafTopic.Outcomes.Single().Id),
            new KpssPath(tree.Id, variant.Id, generalAbility.Id, math.Id, mathTopic.Id, mathTopic.Outcomes.Single().Id));
    }

    private static async Task<Guid> SeedQuestionAsync(
        ApiSmokeFactory factory,
        KpssPath ids,
        Guid? ownerUserId,
        string qualityStatus,
        string stem,
        bool isDeleted = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var question = new QuestionItem
        {
            OwnerUserId = ownerUserId,
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
            QualityStatus = qualityStatus,
            LicenseStatus = "open",
            SourceOrigin = "test_fixture",
            Explanation = "Paragrafın ana fikri A seçeneğinde verilmiştir.",
            IsDeleted = isDeleted,
            Options =
            [
                new QuestionOption { OptionKey = "A", Text = "Doğru seçenek", IsCorrect = true, SortOrder = 0 },
                new QuestionOption { OptionKey = "B", Text = "Yanlış seçenek", IsCorrect = false, SortOrder = 1 }
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

    private sealed record KpssIds(KpssPath Paragraf, KpssPath Math);
    private sealed record KpssPath(Guid DefinitionId, Guid VariantId, Guid SectionId, Guid SubjectId, Guid TopicId, Guid OutcomeId);
}
