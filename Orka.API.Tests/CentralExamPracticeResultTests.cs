using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class CentralExamPracticeResultTests
{
    [Fact]
    public async Task StartCreatesCallerOwnedAttemptAndPreservesExamContext()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "central-practice-start");
        var ids = await GetKpssIdsAsync(factory);
        await SeedQuestionAsync(factory, ids, null, "published", "Attempt paragraph");

        var response = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/turkce-paragraf/start", new PracticeStartRequestDto { Limit = 5 });
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<PracticeSessionDto>();

        Assert.NotNull(session);
        Assert.NotEqual(Guid.Empty, session!.PracticeSetId);
        Assert.Equal(session.PracticeSetId, session.PracticeAttemptId);
        Assert.Equal("KPSS", session.ExamContext.ExamCode);
        Assert.Equal("TURKCE", session.ExamContext.SubjectCode);
        Assert.Equal("PARAGRAF", session.ExamContext.TopicCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var attempt = await db.CentralExamPracticeAttempts.Include(a => a.Answers).SingleAsync(a => a.Id == session.PracticeSetId);
        Assert.Equal(user.UserId, attempt.UserId);
        Assert.Equal("started", attempt.Status);
        Assert.Equal(ids.TopicId, attempt.ExamTopicId);
        Assert.Single(attempt.Answers);
        Assert.Equal("A", attempt.Answers.Single().CorrectOptionKey);
    }

    [Fact]
    public async Task SubmitPersistsAnswersAndReturnsIdempotentResult()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "central-practice-submit");
        var ids = await GetKpssIdsAsync(factory);
        var correct = await SeedQuestionAsync(factory, ids, null, "published", "Correct paragraph");
        var wrong = await SeedQuestionAsync(factory, ids, null, "published", "Wrong paragraph");
        var blank = await SeedQuestionAsync(factory, ids, null, "published", "Blank paragraph");
        var session = await StartAsync(user);

        var submit = new PracticeSubmitRequestDto
        {
            PracticeSetId = session.PracticeSetId,
            Answers =
            [
                new PracticeAnswerDto { QuestionId = correct, SelectedOptionKey = "A" },
                new PracticeAnswerDto { QuestionId = wrong, SelectedOptionKey = "B" },
                new PracticeAnswerDto { QuestionId = blank, SelectedOptionKey = null }
            ]
        };

        var response = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/turkce-paragraf/submit", submit);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PracticeResultDto>();
        var duplicate = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/turkce-paragraf/submit", submit);
        duplicate.EnsureSuccessStatusCode();
        var duplicateResult = await duplicate.Content.ReadFromJsonAsync<PracticeResultDto>();

        Assert.NotNull(result);
        Assert.Equal(session.PracticeSetId, result!.PracticeAttemptId);
        Assert.Equal("submitted", result.Status);
        Assert.Equal(3, result.TotalQuestions);
        Assert.Equal(2, result.AnsweredCount);
        Assert.Equal(1, result.CorrectCount);
        Assert.Equal(1, result.WrongCount);
        Assert.Equal(1, result.BlankCount);
        Assert.Equal(result.CorrectCount, duplicateResult!.CorrectCount);
        Assert.NotNull(result.NextAction);
        Assert.NotNull(result.StudyContext);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        Assert.Equal(1, await db.CentralExamPracticeAttempts.CountAsync(a => a.Id == session.PracticeSetId && a.Status == "submitted"));
        Assert.Equal(3, await db.CentralExamPracticeAnswers.CountAsync(a => a.PracticeAttemptId == session.PracticeSetId));
        Assert.Equal(3, await db.CentralExamPracticeAnswers.CountAsync(a => a.PracticeAttemptId == session.PracticeSetId && a.SubmittedAt.HasValue));
    }

    [Fact]
    public async Task UserCannotReadOrSubmitAnotherUsersAttempt()
    {
        using var factory = new ApiSmokeFactory();
        var userA = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "central-practice-owner");
        var userB = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "central-practice-other");
        var ids = await GetKpssIdsAsync(factory);
        await SeedQuestionAsync(factory, ids, null, "published", "Shared paragraph");
        var session = await StartAsync(userA);

        Assert.Equal(HttpStatusCode.NotFound, (await userB.Client.GetAsync($"/api/central-exams/practice-attempts/{session.PracticeSetId}")).StatusCode);
        var submit = await userB.Client.PostAsJsonAsync("/api/central-exams/kpss/turkce-paragraf/submit", new PracticeSubmitRequestDto
        {
            PracticeSetId = session.PracticeSetId,
            Answers = [new PracticeAnswerDto { QuestionId = session.Questions.Single().QuestionId, SelectedOptionKey = "A" }]
        });
        Assert.Equal(HttpStatusCode.BadRequest, submit.StatusCode);
    }

    [Fact]
    public async Task DeletedAfterStartQuestionGradesFromSnapshot()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "central-practice-deleted-snapshot");
        var ids = await GetKpssIdsAsync(factory);
        var questionId = await SeedQuestionAsync(factory, ids, null, "published", "Snapshot paragraph");
        var session = await StartAsync(user);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var question = await db.QuestionItems.SingleAsync(q => q.Id == questionId);
            question.IsDeleted = true;
            await db.SaveChangesAsync();
        }

        var response = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/turkce-paragraf/submit", new PracticeSubmitRequestDto
        {
            PracticeSetId = session.PracticeSetId,
            Answers = [new PracticeAnswerDto { QuestionId = questionId, SelectedOptionKey = "A" }]
        });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PracticeResultDto>();

        Assert.Equal(1, result!.CorrectCount);
        Assert.Contains(result.Results, r => r.QuestionId == questionId && r.CorrectOptionKey == "A");
    }

    private static async Task<PracticeSessionDto> StartAsync(CoordinationTestUser user)
    {
        var response = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/turkce-paragraf/start", new PracticeStartRequestDto { Limit = 10 });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PracticeSessionDto>())!;
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

    private static async Task<Guid> SeedQuestionAsync(
        ApiSmokeFactory factory,
        KpssPath ids,
        Guid? ownerUserId,
        string qualityStatus,
        string stem)
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

    private sealed record KpssPath(Guid DefinitionId, Guid VariantId, Guid SectionId, Guid SubjectId, Guid TopicId, Guid OutcomeId);
}
