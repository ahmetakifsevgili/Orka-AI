using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class CentralExamDenemeTests
{
    [Fact]
    public async Task BlueprintIsReturnedSafelyAndReportsInsufficientContent()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "deneme-blueprint");

        var response = await user.Client.GetAsync("/api/central-exams/kpss/denemeler");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var blueprints = JsonSerializer.Deserialize<List<CentralExamDenemeBlueprintDto>>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var blueprint = Assert.Single(blueprints!);
        Assert.Equal("KPSS_MINI_TURKCE_PARAGRAF", blueprint.Code);
        Assert.False(blueprint.CanClaimOfficial);
        Assert.Equal("unverified", blueprint.VerificationStatus);
        Assert.False(blueprint.HasEnoughQuestions);
        Assert.Contains("yeterli", blueprint.EmptyState);
        Assert.DoesNotContain("official curriculum complete", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OSYM resmi simulasyon", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("puan tahmini", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartCreatesCallerOwnedAttemptAndSelectsBlueprintDistribution()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "deneme-start");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "deneme-start-other");
        var ids = await GetKpssIdsAsync(factory);
        for (var i = 0; i < 5; i++)
        {
            await SeedQuestionAsync(factory, ids.Paragraf, null, "published", $"Published paragraph {i}");
        }

        await SeedQuestionAsync(factory, ids.Paragraf, user.UserId, "draft", "Draft paragraph");
        await SeedQuestionAsync(factory, ids.Paragraf, user.UserId, "needs_review", "Review paragraph");
        await SeedQuestionAsync(factory, ids.Paragraf, user.UserId, "rejected", "Rejected paragraph");
        await SeedQuestionAsync(factory, ids.Paragraf, other.UserId, "published", "Other private paragraph");
        await SeedQuestionAsync(factory, ids.Paragraf, null, "published", "Deleted paragraph", isDeleted: true);
        await SeedQuestionAsync(factory, ids.Math, null, "published", "Math question");

        var response = await user.Client.PostAsJsonAsync(
            "/api/central-exams/kpss/denemeler/KPSS_MINI_TURKCE_PARAGRAF/start",
            new CentralExamDenemeStartRequestDto());
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var session = JsonSerializer.Deserialize<CentralExamDenemeSessionDto>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(session);
        Assert.Equal("ready", session!.Status);
        Assert.Equal(5, session.TotalQuestions);
        Assert.All(session.Questions, q =>
        {
            Assert.Equal("KPSS", q.ExamContext.ExamCode);
            Assert.Equal("GENEL_YETENEK", q.ExamContext.SectionCode);
            Assert.Equal("TURKCE", q.ExamContext.SubjectCode);
            Assert.Equal("PARAGRAF", q.ExamContext.TopicCode);
        });
        Assert.DoesNotContain("isCorrect", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Paragrafin ana fikri", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Draft paragraph", body);
        Assert.DoesNotContain("Review paragraph", body);
        Assert.DoesNotContain("Rejected paragraph", body);
        Assert.DoesNotContain("Other private paragraph", body);
        Assert.DoesNotContain("Deleted paragraph", body);
        Assert.DoesNotContain("Math question", body);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var attempt = await db.CentralExamDenemeAttempts.Include(a => a.Answers).SingleAsync(a => a.Id == session.DenemeAttemptId);
        Assert.Equal(user.UserId, attempt.UserId);
        Assert.Equal("started", attempt.Status);
        Assert.Equal(5, attempt.Answers.Count);
        Assert.All(attempt.Answers, a => Assert.Equal(ids.Paragraf.TopicId, a.ExamTopicId));
    }

    [Fact]
    public async Task StartRejectsInsufficientContentWithoutFillingUnrelatedTopics()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "deneme-insufficient");
        var ids = await GetKpssIdsAsync(factory);
        await SeedQuestionAsync(factory, ids.Paragraf, null, "published", "Only paragraph");
        for (var i = 0; i < 10; i++)
        {
            await SeedQuestionAsync(factory, ids.Math, null, "published", $"Math filler {i}");
        }

        var response = await user.Client.PostAsJsonAsync(
            "/api/central-exams/kpss/denemeler/KPSS_MINI_TURKCE_PARAGRAF/start",
            new CentralExamDenemeStartRequestDto());
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<CentralExamDenemeSessionDto>();

        Assert.Equal("insufficient_content", session!.Status);
        Assert.Equal(Guid.Empty, session.DenemeAttemptId);
        Assert.Equal(0, session.TotalQuestions);
        Assert.Contains("yeterli", session.EmptyState);
    }

    [Fact]
    public async Task SubmitPersistsSummaryBreakdownAndIsIdempotent()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "deneme-submit");
        var ids = await GetKpssIdsAsync(factory);
        for (var i = 0; i < 5; i++)
        {
            await SeedQuestionAsync(factory, ids.Paragraf, null, "published", $"Paragraph {i}");
        }

        var session = await StartAsync(user);
        var submit = new CentralExamDenemeSubmitRequestDto
        {
            DenemeAttemptId = session.DenemeAttemptId,
            Answers =
            [
                new CentralExamDenemeAnswerDto { QuestionId = session.Questions[0].QuestionId, SelectedOptionKey = "A" },
                new CentralExamDenemeAnswerDto { QuestionId = session.Questions[1].QuestionId, SelectedOptionKey = "A" },
                new CentralExamDenemeAnswerDto { QuestionId = session.Questions[2].QuestionId, SelectedOptionKey = "B" },
                new CentralExamDenemeAnswerDto { QuestionId = session.Questions[3].QuestionId, SelectedOptionKey = "B" },
                new CentralExamDenemeAnswerDto { QuestionId = session.Questions[4].QuestionId, SelectedOptionKey = null }
            ]
        };

        var response = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/denemeler/submit", submit);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CentralExamDenemeResultDto>();
        var duplicate = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/denemeler/submit", submit);
        duplicate.EnsureSuccessStatusCode();
        var duplicateResult = await duplicate.Content.ReadFromJsonAsync<CentralExamDenemeResultDto>();

        Assert.Equal("submitted", result!.Status);
        Assert.Equal(5, result.Summary.TotalQuestions);
        Assert.Equal(4, result.Summary.AnsweredCount);
        Assert.Equal(2, result.Summary.CorrectCount);
        Assert.Equal(2, result.Summary.WrongCount);
        Assert.Equal(1, result.Summary.BlankCount);
        Assert.Equal(result.Summary.CorrectCount, duplicateResult!.Summary.CorrectCount);
        Assert.Single(result.Breakdown);
        Assert.NotNull(result.NextAction);
        Assert.NotNull(result.StudyContext);
        Assert.All(result.Results, r =>
        {
            Assert.Equal("A", r.CorrectOptionKey);
            Assert.Contains("Paragrafin ana fikri", r.Explanation);
        });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        Assert.Equal(1, await db.CentralExamDenemeAttempts.CountAsync(a => a.Id == session.DenemeAttemptId && a.Status == "submitted"));
        Assert.Equal(5, await db.CentralExamDenemeAnswers.CountAsync(a => a.DenemeAttemptId == session.DenemeAttemptId));
    }

    [Fact]
    public async Task UserCannotReadOrSubmitAnotherUsersAttemptAndDeletedAfterStartGradesFromSnapshot()
    {
        using var factory = new ApiSmokeFactory();
        var userA = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "deneme-owner");
        var userB = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "deneme-other");
        var ids = await GetKpssIdsAsync(factory);
        for (var i = 0; i < 5; i++)
        {
            await SeedQuestionAsync(factory, ids.Paragraf, null, "published", $"Snapshot paragraph {i}");
        }

        var session = await StartAsync(userA);
        var firstQuestion = session.Questions[0].QuestionId;
        Assert.Equal(HttpStatusCode.NotFound, (await userB.Client.GetAsync($"/api/central-exams/deneme-attempts/{session.DenemeAttemptId}")).StatusCode);
        var otherSubmit = await userB.Client.PostAsJsonAsync("/api/central-exams/kpss/denemeler/submit", new CentralExamDenemeSubmitRequestDto
        {
            DenemeAttemptId = session.DenemeAttemptId,
            Answers = [new CentralExamDenemeAnswerDto { QuestionId = firstQuestion, SelectedOptionKey = "A" }]
        });
        Assert.Equal(HttpStatusCode.BadRequest, otherSubmit.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var question = await db.QuestionItems.SingleAsync(q => q.Id == firstQuestion);
            question.IsDeleted = true;
            await db.SaveChangesAsync();
        }

        var response = await userA.Client.PostAsJsonAsync("/api/central-exams/kpss/denemeler/submit", new CentralExamDenemeSubmitRequestDto
        {
            DenemeAttemptId = session.DenemeAttemptId,
            Answers = [new CentralExamDenemeAnswerDto { QuestionId = firstQuestion, SelectedOptionKey = "A" }]
        });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CentralExamDenemeResultDto>();

        Assert.Equal(1, result!.Summary.CorrectCount);
        Assert.Contains(result.Results, r => r.QuestionId == firstQuestion && r.CorrectOptionKey == "A");
    }

    private static async Task<CentralExamDenemeSessionDto> StartAsync(CoordinationTestUser user)
    {
        var response = await user.Client.PostAsJsonAsync(
            "/api/central-exams/kpss/denemeler/KPSS_MINI_TURKCE_PARAGRAF/start",
            new CentralExamDenemeStartRequestDto());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CentralExamDenemeSessionDto>())!;
    }

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
            Explanation = "Paragrafin ana fikri A seceneginde verilmistir.",
            IsDeleted = isDeleted,
            Options =
            [
                new QuestionOption { OptionKey = "A", Text = "Dogru secenek", IsCorrect = true, SortOrder = 0 },
                new QuestionOption { OptionKey = "B", Text = "Yanlis secenek", IsCorrect = false, SortOrder = 1 }
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
