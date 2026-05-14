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

public sealed class CentralExamTests
{
    [Fact]
    public async Task OverviewReturnsKpssAsSupportedCentralExam()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "central-overview");

        var exams = await user.Client.GetFromJsonAsync<List<CentralExamDto>>("/api/central-exams");

        var kpss = Assert.Single(exams!, exam => exam.ExamCode == "KPSS");
        Assert.Equal("available", kpss.AvailabilityStatus);
        Assert.False(kpss.CanClaimOfficial);
        Assert.Equal("unverified", kpss.VerificationStatus);
        Assert.Contains(kpss.SupportedVariants, v => v.VariantCode == "KPSS_LISANS");
        Assert.Contains(kpss.SupportedVariants, v => v.VariantCode == "KPSS_ONLISANS");
    }

    [Fact]
    public async Task KpssStudyHomeReturnsSafeTreeAndPracticeCounts()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "central-home");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "central-home-other");
        var ids = await GetKpssIdsAsync(factory);
        await SeedQuestionAsync(factory, ids, ownerUserId: null, qualityStatus: "published", stem: "Published system paragraph");
        await SeedQuestionAsync(factory, ids, user.UserId, "published", "Published user paragraph");
        await SeedQuestionAsync(factory, ids, user.UserId, "draft", "Draft user paragraph");
        await SeedQuestionAsync(factory, ids, user.UserId, "needs_review", "Review user paragraph");
        await SeedQuestionAsync(factory, ids, other.UserId, "published", "Other private paragraph");

        var response = await user.Client.GetAsync("/api/central-exams/kpss");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var home = JsonSerializer.Deserialize<CentralExamStudyHomeDto>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(home);
        Assert.Equal("KPSS", home!.ExamCode);
        Assert.False(home.CanClaimOfficial);
        Assert.Equal("unverified", home.VerificationStatus);
        Assert.Contains("Resmi", home.UserSafeVerificationLabel);
        Assert.Contains(home.Sections, s => s.Code == "GENEL_YETENEK");
        Assert.Equal(2, home.PracticeReadyCounts.PracticeReadyCount);
        Assert.Equal(1, home.PracticeReadyCounts.SystemPublishedCount);
        Assert.Equal(1, home.PracticeReadyCounts.UserPublishedCount);
        Assert.Equal(1, home.PracticeReadyCounts.CallerDraftCount);
        Assert.Equal(1, home.PracticeReadyCounts.CallerNeedsReviewCount);
        Assert.Equal(2, home.RecommendedEntryPoint!.PracticeReadyCount);
        Assert.DoesNotContain("Other private paragraph", body);
        Assert.DoesNotContain("OwnerUserId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawImport", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("debug", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("official curriculum complete", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CountdownReturnsSafeUnconfiguredStateWithoutOfficialDateClaim()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "central-countdown");

        var countdown = await user.Client.GetFromJsonAsync<CentralExamCountdownDto>("/api/central-exams/kpss/countdown");

        Assert.NotNull(countdown);
        Assert.Null(countdown!.ExamDate);
        Assert.Null(countdown.DaysRemaining);
        Assert.Equal("not_configured", countdown.VerificationStatus);
        Assert.Contains("doğrulanmış", countdown.UserSafeLabel);
        Assert.DoesNotContain("ÖSYM resmi", countdown.UserSafeLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task KpssTurkceParagrafEntryReturnsSafeEmptyState()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "central-empty");
        await GetKpssIdsAsync(factory);

        var entry = await user.Client.GetFromJsonAsync<CentralExamPracticeEntryDto>("/api/central-exams/kpss/turkce-paragraf");

        Assert.NotNull(entry);
        Assert.False(entry!.HasPracticeReadyQuestions);
        Assert.Equal(0, entry.PracticeReadyCount);
        Assert.Contains("henüz yayına hazır", entry.EmptyState);
        Assert.Equal("KPSS", entry.ExamContext.ExamCode);
        Assert.Equal("TURKCE", entry.ExamContext.SubjectCode);
        Assert.Equal("PARAGRAF", entry.ExamContext.TopicCode);
    }

    private static async Task<KpssIds> GetKpssIdsAsync(ApiSmokeFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IExamFrameworkService>();
        var tree = await service.CreateSystemSkeletonAsync();
        var variant = tree.Variants.Single(v => v.Code == "KPSS_LISANS");
        var section = variant.Sections.Single(s => s.Code == "GENEL_YETENEK");
        var subject = section.Subjects.Single(s => s.Code == "TURKCE");
        var topic = subject.Topics.Single(t => t.Code == "PARAGRAF");
        var outcome = topic.Outcomes.Single();
        return new KpssIds(tree.Id, variant.Id, section.Id, subject.Id, topic.Id, outcome.Id);
    }

    private static async Task<Guid> SeedQuestionAsync(
        ApiSmokeFactory factory,
        KpssIds ids,
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
            Difficulty = "easy",
            CognitiveSkill = "reading_comprehension",
            QualityStatus = qualityStatus,
            LicenseStatus = "open",
            SourceOrigin = "test_fixture",
            Explanation = "Paragrafın ana fikri bu seçenekte verilir.",
            Options =
            [
                new QuestionOption { OptionKey = "A", Text = "Doğru seçenek", IsCorrect = true, SortOrder = 0 },
                new QuestionOption { OptionKey = "B", Text = "Çeldirici", IsCorrect = false, SortOrder = 1 }
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

    private sealed record KpssIds(Guid DefinitionId, Guid VariantId, Guid SectionId, Guid SubjectId, Guid TopicId, Guid OutcomeId);
}
