using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class CentralExamLearningLoopTests
{
    [Fact]
    public async Task WrongAndBlankAnswersCreateBoundedLearningSignals()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "central-loop-signals");
        var ids = await GetKpssIdsAsync(factory);
        var wrong = await SeedQuestionAsync(factory, ids, "Wrong loop paragraph");
        var blank = await SeedQuestionAsync(factory, ids, "Blank loop paragraph");
        var session = await StartAsync(user);

        var submit = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/turkce-paragraf/submit", new PracticeSubmitRequestDto
        {
            PracticeSetId = session.PracticeSetId,
            Answers =
            [
                new PracticeAnswerDto { QuestionId = wrong, SelectedOptionKey = "B" },
                new PracticeAnswerDto { QuestionId = blank, SelectedOptionKey = null }
            ]
        });
        submit.EnsureSuccessStatusCode();
        var result = await submit.Content.ReadFromJsonAsync<PracticeResultDto>();

        Assert.NotNull(result);
        Assert.Equal("tutor_remediation", result!.NextAction!.ActionType);
        Assert.Contains("PARAGRAF", result.StudyContext!.SuggestedWikiPath);
        Assert.Contains("tekrar", result.TutorRemediationContext, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("usable", result.LearningSignal!.Status);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var signals = await db.LearningSignals
            .Where(s => s.UserId == user.UserId && s.SignalType == LearningSignalTypes.CentralExamWeaknessDetected)
            .ToListAsync();

        Assert.Equal(2, signals.Count);
        Assert.All(signals, signal =>
        {
            Assert.Null(signal.TopicId);
            Assert.NotNull(signal.PayloadJson);
            using var doc = JsonDocument.Parse(signal.PayloadJson!);
            Assert.True(doc.RootElement.TryGetProperty("learningSignalConfidence", out _));
            Assert.True(doc.RootElement.TryGetProperty("remediationSeed", out _));
            Assert.True(doc.RootElement.TryGetProperty("examContext", out var examContext));
            Assert.Equal("KPSS", examContext.GetProperty("examCode").GetString());
            Assert.DoesNotContain("evaluator", signal.PayloadJson!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("debug", signal.PayloadJson!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("prompt", signal.PayloadJson!, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task CentralExamSignalIsVisibleToGlobalLearningMemory()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "central-loop-memory");
        var ids = await GetKpssIdsAsync(factory);
        var question = await SeedQuestionAsync(factory, ids, "Memory loop paragraph");
        var session = await StartAsync(user);

        var submit = await user.Client.PostAsJsonAsync("/api/central-exams/kpss/turkce-paragraf/submit", new PracticeSubmitRequestDto
        {
            PracticeSetId = session.PracticeSetId,
            Answers = [new PracticeAnswerDto { QuestionId = question, SelectedOptionKey = "B" }]
        });
        submit.EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var memory = scope.ServiceProvider.GetRequiredService<ILearningMemoryService>();
        var snapshot = await memory.BuildAsync(user.UserId, Array.Empty<Guid>());

        Assert.Contains(snapshot.RecentProgressSignals, s => s.Contains(LearningSignalTypes.CentralExamWeaknessDetected));
        Assert.Contains(snapshot.RecentMisconceptions, c => c.ConceptKey.Contains("kpss"));
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
