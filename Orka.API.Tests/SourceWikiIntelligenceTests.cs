using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class SourceWikiIntelligenceTests
{
    [Fact]
    public async Task ProfileConnectsReadySourceWikiRepairAndConceptLinksWithoutRawLeak()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-wiki-ready");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Source Wiki Topic");
        var rawChunk = "mitosis source evidence rawSourceChunk with C:\\secret\\source.txt and private learner detail";
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(factory, user.UserId, topicId, "Mitosis Evidence", rawChunk);
        var pageId = await CoordinationTestHelpers.SeedWikiPageAsync(factory, user.UserId, topicId, "Mitosis Concept", "manual note rawPrompt should not leak");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var page = await db.WikiPages.SingleAsync(p => p.Id == pageId);
            page.ConceptKey = "mitosis";
            page.SourceReadiness = "source_grounded";
            page.EvidenceStatus = "source_grounded";
            var block = await db.WikiBlocks.SingleAsync(b => b.WikiPageId == pageId);
            block.BlockType = WikiBlockType.RepairNote;
            block.ConceptKey = "mitosis";
            block.SourceBasis = "source_grounded";
            await db.SaveChangesAsync();

            var lifecycle = scope.ServiceProvider.GetRequiredService<ISourceEvidenceLifecycleService>();
            await lifecycle.BuildSourceEvidenceBundleAsync(user.UserId, topicId, question: "mitosis evidence");
            var bundle = await db.SourceEvidenceBundles
                .Where(b => b.UserId == user.UserId && b.TopicId == topicId && !b.IsDeleted)
                .OrderByDescending(b => b.UpdatedAt)
                .FirstAsync();
            bundle.EvidenceStatus = "source_grounded";
            bundle.SourceCount = 1;
            bundle.ReadySourceCount = 1;
            bundle.CitationCoverage = 1m;
            bundle.UpdatedAt = DateTime.UtcNow.AddSeconds(1);
            await db.SaveChangesAsync();
        }

        var response = await user.Client.GetAsync($"/api/sources/wiki-intelligence?topicId={topicId}");
        response.EnsureSuccessStatusCode();
        var profile = (await response.Content.ReadFromJsonAsync<SourceWikiIntelligenceProfileDto>())!;

        Assert.Equal(topicId, profile.TopicId);
        Assert.True(profile.SourceCount >= 1);
        Assert.True(profile.WikiPageCount >= 1);
        Assert.True(profile.CanClaimSourceGrounded);
        Assert.Contains(profile.EvidenceReadiness, e => e.SourceId == sourceId && e.SourceReadiness is "source_grounded" or "ready");
        Assert.Contains(profile.WikiPages, p => p.WikiPageId == pageId && p.CurationStatus == "repair_pending");
        Assert.Contains(profile.LinkedConcepts, l => l.ConceptKey == "mitosis" || l.WikiPageId == pageId);
        Assert.Contains(profile.NextActions, a => a.ActionType == "repair_concept");
        AssertSafePayload(JsonSerializer.Serialize(profile), rawChunk, user.UserId);
    }

    [Fact]
    public async Task StaleOrInsufficientEvidenceCreatesWarningsAndBlocksSourceGroundedOverclaim()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-wiki-stale-a");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-wiki-stale-b");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Stale Source Topic");
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(
            factory,
            user.UserId,
            topicId,
            "Stale Evidence",
            "stale source paragraph rawSourceChunk secret token answerKey");
        var pageId = await CoordinationTestHelpers.SeedWikiPageAsync(factory, user.UserId, topicId, "Stale Wiki", "source limited body should stay private");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var page = await db.WikiPages.SingleAsync(p => p.Id == pageId);
            page.SourceReadiness = "evidence_insufficient";
            page.EvidenceStatus = "evidence_insufficient";
            var block = await db.WikiBlocks.SingleAsync(b => b.WikiPageId == pageId);
            block.SourceBasis = "evidence_insufficient";
            block.SafetyWarningsJson = "[\"source_limited\"]";
            await db.SaveChangesAsync();

            var lifecycle = scope.ServiceProvider.GetRequiredService<ISourceEvidenceLifecycleService>();
            await lifecycle.BuildSourceEvidenceBundleAsync(user.UserId, topicId, question: "stale source");
            await lifecycle.MarkSourceStaleAsync(user.UserId, sourceId, "test stale");
        }

        var response = await user.Client.GetAsync($"/api/sources/wiki-intelligence?sourceId={sourceId}");
        response.EnsureSuccessStatusCode();
        var profile = (await response.Content.ReadFromJsonAsync<SourceWikiIntelligenceProfileDto>())!;

        Assert.False(profile.CanClaimSourceGrounded);
        Assert.Contains("source_evidence_limited", profile.Warnings);
        Assert.Contains("source_grounded_claim_blocked", profile.Warnings);
        Assert.Contains(profile.NextActions, a => a.ActionType == "review_source");
        Assert.Contains(profile.WikiPages, p => p.WikiPageId == pageId && p.SourceLimitedSignalCount > 0);
        AssertSafePayload(JsonSerializer.Serialize(profile), "stale source paragraph rawSourceChunk secret token answerKey", user.UserId);

        var crossUserSource = await other.Client.GetAsync($"/api/sources/wiki-intelligence?sourceId={sourceId}");
        Assert.Equal(HttpStatusCode.NotFound, crossUserSource.StatusCode);

        var crossUserPage = await other.Client.GetAsync($"/api/sources/wiki-intelligence?wikiPageId={pageId}");
        Assert.Equal(HttpStatusCode.NotFound, crossUserPage.StatusCode);
    }

    [Fact]
    public async Task DashboardAndTutorConsumeSourceWikiIntelligenceSafely()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-wiki-dashboard");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Dashboard Source Wiki");
        await CoordinationTestHelpers.SeedSourceAsync(factory, user.UserId, topicId, "Dashboard Source", "dashboard source evidence concept");
        var pageId = await CoordinationTestHelpers.SeedWikiPageAsync(factory, user.UserId, topicId, "Dashboard Wiki", "dashboard wiki note");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var topic = await db.Topics.SingleAsync(t => t.Id == topicId);
            topic.LastAccessedAt = DateTime.UtcNow.AddMinutes(1);
            var page = await db.WikiPages.SingleAsync(p => p.Id == pageId);
            page.SourceReadiness = "ready";
            page.EvidenceStatus = "ready";
            await db.SaveChangesAsync();
        }

        var dashboard = await user.Client.GetFromJsonAsync<DashboardTodayDto>("/api/dashboard/today");
        Assert.NotNull(dashboard?.SourceWikiIntelligenceProfile);
        Assert.True(dashboard!.SourceWikiIntelligenceProfile!.SourceCount >= 1);
        Assert.Contains(dashboard.SourceWikiIntelligenceProfile.NextActions, a => a.ActionType == "open_notebook_pack");

        var tutorActions = await user.Client.GetFromJsonAsync<List<TutorNextLearningActionDto>>($"/api/tutor/next-actions?topicId={topicId}");
        Assert.NotNull(tutorActions);
        Assert.Contains(tutorActions!, a => a.ActionType is "open_notebook_pack" or "open_source_evidence" or "review_wiki_section");

        var json = JsonSerializer.Serialize(new { dashboard, tutorActions });
        AssertSafePayload(json, "dashboard source evidence concept", user.UserId);
    }

    private static void AssertSafePayload(string json, string rawSourceText, Guid userId)
    {
        Assert.DoesNotContain(rawSourceText, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawPrompt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawSourceChunk", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawProviderPayload", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawToolPayload", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correctAnswer", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stackTrace", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ownerId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(userId.ToString(), json, StringComparison.OrdinalIgnoreCase);
    }
}
