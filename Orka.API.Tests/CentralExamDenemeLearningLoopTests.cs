using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class CentralExamDenemeLearningLoopTests
{
    [Fact]
    public async Task WrongAndBlankAnswersCreateBoundedLearningSignals()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "deneme-loop");
        var ids = await GetKpssIdsAsync(factory);
        for (var i = 0; i < 5; i++)
        {
            await SeedQuestionAsync(factory, ids, null, "published", $"Loop paragraph {i}");
        }

        var session = await StartAsync(user);
        var submit = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/denemeler/submit", new CentralExamDenemeSubmitRequestDto
        {
            DenemeAttemptId = session.DenemeAttemptId,
            Answers =
            [
                new CentralExamDenemeAnswerDto { QuestionId = session.Questions[0].QuestionId, SelectedOptionKey = "B" },
                new CentralExamDenemeAnswerDto { QuestionId = session.Questions[1].QuestionId, SelectedOptionKey = null },
                new CentralExamDenemeAnswerDto { QuestionId = session.Questions[2].QuestionId, SelectedOptionKey = "A" },
                new CentralExamDenemeAnswerDto { QuestionId = session.Questions[3].QuestionId, SelectedOptionKey = "A" },
                new CentralExamDenemeAnswerDto { QuestionId = session.Questions[4].QuestionId, SelectedOptionKey = "A" }
            ]
        });
        submit.EnsureSuccessStatusCode();
        var result = await submit.Content.ReadFromJsonAsync<CentralExamDenemeResultDto>();

        Assert.NotNull(result!.NextAction);
        Assert.Contains("PARAGRAF", result.NextAction!.ExamContext.TopicCode);
        Assert.Contains("kisa", result.TutorRemediationContext);
        Assert.NotNull(result.StudyContext);
        Assert.Contains("KPSS", result.StudyContext!.PathLabel);
        Assert.Contains("PARAGRAF", result.StudyContext.PathLabel);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var answered = await db.LearningSignals
            .Where(s => s.UserId == user.UserId && s.SignalType == LearningSignalTypes.CentralExamDenemeAnswered)
            .ToListAsync();
        var weak = await db.LearningSignals
            .Where(s => s.UserId == user.UserId && s.SignalType == LearningSignalTypes.CentralExamDenemeWeaknessDetected)
            .ToListAsync();

        Assert.Single(answered);
        Assert.Equal(2, weak.Count);
        Assert.All(weak, signal =>
        {
            Assert.Contains("learningSignalConfidence", signal.PayloadJson);
            Assert.Contains("remediationSeed", signal.PayloadJson);
            Assert.Contains("examContext", signal.PayloadJson);
            Assert.Contains("central_exam_mini_deneme", signal.PayloadJson);
            Assert.DoesNotContain("rawEvaluator", signal.PayloadJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("debug", signal.PayloadJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("provider", signal.PayloadJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hiddenPrompt", signal.PayloadJson, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task DenemeSignalIsVisibleToGlobalLearningMemory()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "deneme-memory");
        var ids = await GetKpssIdsAsync(factory);
        for (var i = 0; i < 5; i++)
        {
            await SeedQuestionAsync(factory, ids, null, "published", $"Memory paragraph {i}");
        }

        var session = await StartAsync(user);
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

        using var scope = factory.Services.CreateScope();
        var memory = scope.ServiceProvider.GetRequiredService<ILearningMemoryService>();
        var snapshot = await memory.BuildAsync(user.UserId, []);

        Assert.Contains(snapshot.RecentProgressSignals, s => s.Contains(LearningSignalTypes.CentralExamDenemeWeaknessDetected));
    }

    private static async Task<CentralExamDenemeSessionDto> StartAsync(CoordinationTestUser user)
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
