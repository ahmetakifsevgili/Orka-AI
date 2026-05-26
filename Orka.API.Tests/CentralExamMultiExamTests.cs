using System.Net.Http.Json;
using AnyAscii;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class CentralExamMultiExamTests
{
    [Fact]
    public async Task OverviewReturnsMultiExamShellWithSafeAvailability()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "central-multi-overview");

        var exams = await user.Client.GetFromJsonAsync<List<CentralExamDto>>("/api/central-exams");

        Assert.NotNull(exams);
        Assert.Contains(exams!, e => e.ExamCode == "KPSS" && e.AvailabilityStatus == "available");
        Assert.Contains(exams!, e => e.ExamCode == "YKS" && e.AvailabilityStatus == "scaffolded");
        Assert.Contains(exams!, e => e.ExamCode == "LGS" && e.AvailabilityStatus == "scaffolded");
        Assert.Contains(exams!, e => e.ExamCode == "YDS" && e.AvailabilityStatus == "scaffolded");

        var kpss = exams!.Single(e => e.ExamCode == "KPSS");
        Assert.True(kpss.Capabilities.HasPractice);
        Assert.True(kpss.Capabilities.HasMiniDeneme);
        Assert.True(kpss.Capabilities.HasCountdown);

        foreach (var exam in exams.Where(e => e.ExamCode is "YKS" or "LGS" or "YDS"))
        {
            Assert.False(exam.CanClaimOfficial);
            Assert.Equal("unverified", exam.VerificationStatus);
            Assert.False(exam.Capabilities.HasPractice);
            Assert.False(exam.Capabilities.HasMiniDeneme);
            Assert.False(exam.Capabilities.HasCountdown);
            Assert.False(exam.Capabilities.HasStudyPlan);
            Assert.NotEmpty(exam.SupportedVariants);
        }
    }

    [Fact]
    public async Task ScaffoldedExamHomesExposeTreeWithoutOfficialClaims()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "central-multi-home");

        foreach (var code in new[] { "yks", "lgs", "yds" })
        {
            var home = await user.Client.GetFromJsonAsync<CentralExamStudyHomeDto>($"/api/central-exams/{code}");

            Assert.NotNull(home);
            Assert.Equal(code.ToUpperInvariant(), home!.ExamCode);
            Assert.False(home.CanClaimOfficial);
            Assert.Equal("unverified", home.VerificationStatus);
            Assert.False(home.Capabilities.HasPractice);
            Assert.False(home.Capabilities.HasMiniDeneme);
            Assert.False(home.Capabilities.HasCountdown);
            Assert.False(home.Capabilities.HasStudyPlan);
            Assert.Null(home.RecommendedEntryPoint);
            Assert.NotEmpty(home.SupportedVariants);
            Assert.NotEmpty(home.Sections);
            Assert.Contains("resmi mufredat iddiasi degildir", home.EmptyState.Transliterate(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ScaffoldedExamCountsDoNotExposePrivateOrUnreadyContentAsPractice()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "central-multi-counts");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "central-multi-other");
        var ids = await GetFirstSkeletonPathAsync(factory, "YKS");
        await SeedQuestionAsync(factory, ids, user.UserId, "draft", "Caller draft YKS item");
        await SeedQuestionAsync(factory, ids, user.UserId, "needs_review", "Caller review YKS item");
        await SeedQuestionAsync(factory, ids, other.UserId, "published", "Other private YKS item");
        await SeedQuestionAsync(factory, ids, user.UserId, "rejected", "Caller rejected YKS item");

        var home = await user.Client.GetFromJsonAsync<CentralExamStudyHomeDto>("/api/central-exams/yks");

        Assert.NotNull(home);
        Assert.Equal(0, home!.PracticeReadyCounts.PracticeReadyCount);
        Assert.Equal(0, home.PracticeReadyCounts.SystemPublishedCount);
        Assert.Equal(0, home.PracticeReadyCounts.UserPublishedCount);
        Assert.Equal(1, home.PracticeReadyCounts.CallerDraftCount);
        Assert.Equal(1, home.PracticeReadyCounts.CallerNeedsReviewCount);
        Assert.False(home.Capabilities.HasQuestionBank);
        Assert.False(home.Capabilities.HasPractice);
    }

    [Fact]
    public async Task SkeletonsAreSystemGlobalAndRemainReadableAcrossUsers()
    {
        using var factory = new ApiSmokeFactory();
        var userA = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "central-multi-a");
        var userB = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "central-multi-b");
        await userA.Client.GetAsync("/api/central-exams");

        foreach (var code in new[] { "YKS", "LGS", "YDS" })
        {
            var treeA = await userA.Client.GetFromJsonAsync<ExamDefinitionDto>($"/api/exams/{code}");
            var treeB = await userB.Client.GetFromJsonAsync<ExamDefinitionDto>($"/api/exams/{code}");

            Assert.NotNull(treeA);
            Assert.NotNull(treeB);
            Assert.Equal("system", treeA!.Visibility);
            Assert.Equal("system", treeB!.Visibility);
            Assert.Equal("unverified", treeA.VerificationStatus);
            Assert.False(treeA.CanClaimOfficial);
            Assert.Equal(treeA.Id, treeB.Id);
        }
    }

    private static async Task<ExamPathIds> GetFirstSkeletonPathAsync(ApiSmokeFactory factory, string examCode)
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IExamFrameworkService>();
        var tree = await service.CreateSystemSkeletonAsync(examCode);
        var variant = tree.Variants.OrderBy(v => v.SortOrder).First();
        var section = variant.Sections.OrderBy(s => s.SortOrder).First();
        var subject = section.Subjects.OrderBy(s => s.SortOrder).First();
        var topic = subject.Topics.OrderBy(t => t.SortOrder).First();
        var outcome = topic.Outcomes.OrderBy(o => o.SortOrder).First();
        return new ExamPathIds(tree.Id, variant.Id, section.Id, subject.Id, topic.Id, outcome.Id);
    }

    private static async Task SeedQuestionAsync(
        ApiSmokeFactory factory,
        ExamPathIds ids,
        Guid? ownerUserId,
        string qualityStatus,
        string stem)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        db.QuestionItems.Add(new QuestionItem
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
            Explanation = "Safe fixture explanation.",
            Options =
            [
                new QuestionOption { OptionKey = "A", Text = "Correct", IsCorrect = true, SortOrder = 0 },
                new QuestionOption { OptionKey = "B", Text = "Distractor", IsCorrect = false, SortOrder = 1 }
            ],
            OutcomeLinks =
            [
                new QuestionOutcomeLink { ExamOutcomeId = ids.OutcomeId, IsPrimary = true, LinkStrength = 1.0m }
            ]
        });
        await db.SaveChangesAsync();
    }

    private sealed record ExamPathIds(Guid DefinitionId, Guid VariantId, Guid SectionId, Guid SubjectId, Guid TopicId, Guid OutcomeId);
}
