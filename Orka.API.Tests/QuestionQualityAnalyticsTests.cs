using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class QuestionQualityAnalyticsTests
{
    [Fact]
    public async Task RecalculateQuestionAnalytics_UsesPracticeAndDenemeAnswers()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "quality-mixed");
        var ids = await GetKpssIdsAsync(factory);
        var questionId = await SeedQuestionAsync(factory, ids, null, "published", "Mixed analytics item");
        await SeedPracticeAnswerAsync(factory, user.UserId, ids, questionId, "A", isCorrect: true);
        await SeedPracticeAnswerAsync(factory, user.UserId, ids, questionId, "A", isCorrect: true);
        await SeedPracticeAnswerAsync(factory, user.UserId, ids, questionId, "B", isCorrect: false);
        await SeedDenemeAnswerAsync(factory, user.UserId, ids, questionId, "A", isCorrect: true);
        await SeedDenemeAnswerAsync(factory, user.UserId, ids, questionId, "A", isCorrect: true);
        await SeedDenemeAnswerAsync(factory, user.UserId, ids, questionId, null, isCorrect: false);

        var response = await user.Client.PostAsync($"/api/question-quality/questions/{questionId}/recalculate", null);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<RecalculateQuestionAnalyticsResultDto>();
        var analytics = result!.Analytics!;

        Assert.Equal(6, analytics.AttemptCount);
        Assert.Equal(5, analytics.AnsweredCount);
        Assert.Equal(4, analytics.CorrectCount);
        Assert.Equal(1, analytics.WrongCount);
        Assert.Equal(1, analytics.BlankCount);
        Assert.Equal(0.8m, analytics.CorrectnessRate);
        Assert.Equal("easy", analytics.DifficultyEstimate);
        Assert.Equal("low", analytics.SampleSizeStatus);
        Assert.Contains(analytics.Options, o => o.OptionKey == "A" && o.SelectionCount == 4 && o.IsCorrectOption);
        Assert.Contains(analytics.Options, o => o.OptionKey == "B" && o.SelectionCount == 1 && !o.IsCorrectOption);
        Assert.Contains(analytics.ReviewSignals, s => s.SignalType == "low_sample_size" && s.Severity == "info");
    }

    [Fact]
    public async Task EmptyQuestionAnalytics_ReturnsNoSampleAndNoStrongClaims()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "quality-empty");
        var ids = await GetKpssIdsAsync(factory);
        var questionId = await SeedQuestionAsync(factory, ids, null, "published", "Empty analytics item");

        var response = await user.Client.PostAsync($"/api/question-quality/questions/{questionId}/recalculate", null);
        response.EnsureSuccessStatusCode();
        var analytics = (await response.Content.ReadFromJsonAsync<RecalculateQuestionAnalyticsResultDto>())!.Analytics!;

        Assert.Equal(0, analytics.AttemptCount);
        Assert.Equal("none", analytics.SampleSizeStatus);
        Assert.Equal("insufficient_data", analytics.DifficultyEstimate);
        Assert.Equal("insufficient_data", analytics.QualitySignal);
        Assert.DoesNotContain(analytics.ReviewSignals, s => s.Severity == "warning");
    }

    [Fact]
    public async Task UsableSample_CreatesHumbleQualityAndDistractorSignals()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "quality-usable");
        var ids = await GetKpssIdsAsync(factory);
        var questionId = await SeedQuestionAsync(factory, ids, null, "published", "Usable analytics item");
        for (var i = 0; i < 19; i++)
        {
            await SeedPracticeAnswerAsync(factory, user.UserId, ids, questionId, "A", isCorrect: true);
        }

        await SeedPracticeAnswerAsync(factory, user.UserId, ids, questionId, "B", isCorrect: false);

        var analytics = (await (await user.Client.PostAsync($"/api/question-quality/questions/{questionId}/recalculate", null))
            .Content.ReadFromJsonAsync<RecalculateQuestionAnalyticsResultDto>())!.Analytics!;

        Assert.Equal("usable", analytics.SampleSizeStatus);
        Assert.Equal("very_easy", analytics.DifficultyEstimate);
        Assert.Equal("likely_too_easy", analytics.QualitySignal);
        Assert.Contains(analytics.ReviewSignals, s => s.SignalType == "too_easy" && s.Severity == "warning");
        Assert.Contains(analytics.Options, o => o.OptionKey == "C" && o.DistractorSignal == "unused");
        Assert.Contains(analytics.ReviewSignals, s => s.SignalType == "weak_distractor");
    }

    [Fact]
    public async Task OverAttractingDistractor_IsDetectedWithoutPsychometricClaims()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "quality-distractor");
        var ids = await GetKpssIdsAsync(factory);
        var questionId = await SeedQuestionAsync(factory, ids, null, "published", "Over attracting item");
        for (var i = 0; i < 5; i++)
        {
            await SeedPracticeAnswerAsync(factory, user.UserId, ids, questionId, "A", isCorrect: true);
        }

        for (var i = 0; i < 15; i++)
        {
            await SeedPracticeAnswerAsync(factory, user.UserId, ids, questionId, "B", isCorrect: false);
        }

        var raw = await user.Client.PostAsync($"/api/question-quality/questions/{questionId}/recalculate", null);
        raw.EnsureSuccessStatusCode();
        var analytics = (await raw.Content.ReadFromJsonAsync<RecalculateQuestionAnalyticsResultDto>())!.Analytics!;

        Assert.Equal("usable", analytics.SampleSizeStatus);
        Assert.Contains(analytics.Options, o => o.OptionKey == "B" && o.DistractorSignal == "over_attracting");
        Assert.Contains(analytics.ReviewSignals, s => s.SignalType == "over_attracting_distractor" && s.Severity == "warning");
        var body = await raw.Content.ReadAsStringAsync();
        Assert.DoesNotContain("psychometric", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("percentile", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OwnershipAndSystemVisibility_AreEnforced()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "quality-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "quality-other");
        var ids = await GetKpssIdsAsync(factory);
        var privateQuestion = await SeedQuestionAsync(factory, ids, owner.UserId, "published", "Private analytics item");
        var systemQuestion = await SeedQuestionAsync(factory, ids, null, "published", "System analytics item");

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsync($"/api/question-quality/questions/{privateQuestion}/recalculate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/question-quality/questions/{privateQuestion}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.Client.PostAsync($"/api/question-quality/questions/{systemQuestion}/recalculate", null)).StatusCode);

        var raw = await other.Client.GetStringAsync($"/api/question-quality/questions/{systemQuestion}");
        Assert.DoesNotContain("ownerUserId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("debug", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ContentOpsReadinessAndCoverage_UseAnalyticsSafely()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "quality-coverage");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "quality-coverage-other");
        var ids = await GetKpssIdsAsync(factory);
        var questionId = await SeedQuestionAsync(factory, ids, null, "published", "Coverage analytics item");
        await SeedQuestionAsync(factory, ids, user.UserId, "draft", "Caller draft item");
        await SeedQuestionAsync(factory, ids, user.UserId, "needs_review", "Caller review item");
        await SeedQuestionAsync(factory, ids, other.UserId, "published", "Other private published item");
        for (var i = 0; i < 20; i++)
        {
            await SeedPracticeAnswerAsync(factory, user.UserId, ids, questionId, null, isCorrect: false);
        }

        await user.Client.PostAsync($"/api/question-quality/questions/{questionId}/recalculate", null);
        var readiness = await user.Client.GetFromJsonAsync<QuestionPublishReadinessDto>($"/api/content-ops/questions/{questionId}/publish-readiness");
        Assert.Contains(readiness!.WarningIssues, i => i.Code == "analytics_high_blank_rate");

        var coverage = await user.Client.GetFromJsonAsync<CentralExamBlueprintCoverageDto>("/api/question-quality/central-exams/KPSS/coverage?variantCode=KPSS_LISANS");
        var paragraf = coverage!.Topics.Single(t => t.TopicCode == "PARAGRAF");
        Assert.Equal(1, paragraf.PublishedQuestionCount);
        Assert.Equal(1, paragraf.PracticeReadyCount);
        Assert.Equal(1, paragraf.CallerDraftCount);
        Assert.Equal(1, paragraf.CallerNeedsReviewCount);
        Assert.NotEqual("strong", paragraf.CoverageStatus);
        Assert.Contains("resmi sinav kapsami", coverage.UserSafeLabel);

        var yks = await user.Client.GetFromJsonAsync<CentralExamBlueprintCoverageDto>("/api/question-quality/central-exams/YKS/coverage");
        Assert.Equal(0, yks!.Topics.Sum(t => t.PracticeReadyCount));
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
            SourceOrigin = "orka_original_fixture",
            SourceTitle = "Orka original analytics fixture",
            Explanation = "Original fixture explanation.",
            Options =
            [
                new QuestionOption { OptionKey = "A", Text = "Correct option", IsCorrect = true, SortOrder = 0 },
                new QuestionOption { OptionKey = "B", Text = "Distractor B", IsCorrect = false, SortOrder = 1 },
                new QuestionOption { OptionKey = "C", Text = "Distractor C", IsCorrect = false, SortOrder = 2 }
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

    private static async Task SeedPracticeAnswerAsync(
        ApiSmokeFactory factory,
        Guid userId,
        KpssPath ids,
        Guid questionId,
        string? selectedOption,
        bool isCorrect)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var attempt = new CentralExamPracticeAttempt
        {
            UserId = userId,
            ExamDefinitionId = ids.DefinitionId,
            ExamVariantId = ids.VariantId,
            ExamSectionId = ids.SectionId,
            ExamSubjectId = ids.SubjectId,
            ExamTopicId = ids.TopicId,
            ExamCode = "KPSS",
            VariantCode = "KPSS_LISANS",
            SectionCode = "GENEL_YETENEK",
            SubjectCode = "TURKCE",
            TopicCode = "PARAGRAF",
            Status = "submitted",
            TotalQuestions = 1,
            AnsweredCount = selectedOption is null ? 0 : 1,
            CorrectCount = isCorrect ? 1 : 0,
            WrongCount = selectedOption is not null && !isCorrect ? 1 : 0,
            BlankCount = selectedOption is null ? 1 : 0,
            SubmittedAt = DateTime.UtcNow
        };
        attempt.Answers.Add(new CentralExamPracticeAnswer
        {
            QuestionItemId = questionId,
            ExamOutcomeId = ids.OutcomeId,
            ExamTopicId = ids.TopicId,
            TopicCode = "PARAGRAF",
            OutcomeCode = "PARAGRAF_MAIN",
            SelectedOptionKey = selectedOption,
            CorrectOptionKey = "A",
            OptionKeysJson = "[\"A\",\"B\",\"C\"]",
            IsCorrect = isCorrect,
            IsBlank = selectedOption is null,
            SubmittedAt = DateTime.UtcNow
        });
        db.CentralExamPracticeAttempts.Add(attempt);
        await db.SaveChangesAsync();
    }

    private static async Task SeedDenemeAnswerAsync(
        ApiSmokeFactory factory,
        Guid userId,
        KpssPath ids,
        Guid questionId,
        string? selectedOption,
        bool isCorrect)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var blueprint = new CentralExamDenemeBlueprint
        {
            ExamDefinitionId = ids.DefinitionId,
            Code = $"KPSS_MINI_TEST_{Guid.NewGuid():N}",
            Name = "Analytics fixture blueprint",
            Visibility = "system",
            VerificationStatus = "unverified",
            TotalQuestionCount = 1
        };
        var attempt = new CentralExamDenemeAttempt
        {
            UserId = userId,
            Blueprint = blueprint,
            ExamDefinitionId = ids.DefinitionId,
            ExamVariantId = ids.VariantId,
            ExamCode = "KPSS",
            VariantCode = "KPSS_LISANS",
            Status = "submitted",
            TotalQuestions = 1,
            AnsweredCount = selectedOption is null ? 0 : 1,
            CorrectCount = isCorrect ? 1 : 0,
            WrongCount = selectedOption is not null && !isCorrect ? 1 : 0,
            BlankCount = selectedOption is null ? 1 : 0,
            SubmittedAt = DateTime.UtcNow
        };
        attempt.Answers.Add(new CentralExamDenemeAnswer
        {
            QuestionItemId = questionId,
            ExamSectionId = ids.SectionId,
            ExamSubjectId = ids.SubjectId,
            ExamTopicId = ids.TopicId,
            ExamOutcomeId = ids.OutcomeId,
            SectionCode = "GENEL_YETENEK",
            SubjectCode = "TURKCE",
            TopicCode = "PARAGRAF",
            OutcomeCode = "PARAGRAF_MAIN",
            SelectedOptionKey = selectedOption,
            CorrectOptionKey = "A",
            OptionKeysJson = "[\"A\",\"B\",\"C\"]",
            IsCorrect = isCorrect,
            IsBlank = selectedOption is null,
            SubmittedAt = DateTime.UtcNow
        });
        db.CentralExamDenemeAttempts.Add(attempt);
        await db.SaveChangesAsync();
    }

    private sealed record KpssPath(Guid DefinitionId, Guid VariantId, Guid SectionId, Guid SubjectId, Guid TopicId, Guid OutcomeId);
}
