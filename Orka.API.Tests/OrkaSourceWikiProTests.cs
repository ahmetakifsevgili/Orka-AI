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

public sealed class OrkaSourceWikiProTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task SourceWikiPro_NewLearnerDegradesSafelyWithoutSourceGroundedClaim()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-wiki-pro-new");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Source Wiki Pro New");

        var pro = await GetProAsync(user, topicId);

        Assert.Equal(topicId, pro.TopicId);
        Assert.Equal("thin_evidence", pro.ReadinessStatus);
        Assert.False(pro.EvidenceMap.CanClaimSourceGrounded);
        Assert.False(pro.EvidenceMap.ProviderOutputCountsAsEvidence);
        Assert.False(pro.EvidenceMap.WikiMemoryCountsAsCitationEvidence);
        Assert.Contains(pro.RecommendedActions, a => a.ActionType == "source_review");
        AssertSafePayload(JsonSerializer.Serialize(pro, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task SourceWikiPro_ReadySourceLinksConceptsAndNotebookHandoffWithoutRawChunkLeak()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-wiki-pro-ready");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Mitosis Pro Topic");
        var rawSource = "mitosis source evidence rawSourceChunk C:\\secret\\mitosis.txt token_marker_secret_value";
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(factory, user.UserId, topicId, "Mitosis Evidence", rawSource);
        var pageId = await CoordinationTestHelpers.SeedWikiPageAsync(factory, user.UserId, topicId, "Mitosis", "manual note rawPrompt should stay private");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var page = await db.WikiPages.SingleAsync(p => p.Id == pageId);
            page.ConceptKey = "mitosis";
            page.SourceReadiness = "source_grounded";
            page.EvidenceStatus = "source_grounded";
            var block = await db.WikiBlocks.SingleAsync(b => b.WikiPageId == pageId);
            block.BlockType = WikiBlockType.SourceNote;
            block.ConceptKey = "mitosis";
            block.SourceBasis = "source_grounded";
            await db.SaveChangesAsync();

            var lifecycle = scope.ServiceProvider.GetRequiredService<ISourceEvidenceLifecycleService>();
            await lifecycle.BuildSourceEvidenceBundleAsync(user.UserId, topicId, question: "mitosis");
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

            var linker = scope.ServiceProvider.GetRequiredService<ISourceConceptLinkingService>();
            await linker.SyncSourceConceptLinksAsync(user.UserId, sourceId);
        }

        var packResponse = await user.Client.PostAsJsonAsync($"/api/notebook-studio/sources/{sourceId}/pack", new
        {
            packType = "source_digest",
            includeArtifacts = false
        });
        packResponse.EnsureSuccessStatusCode();

        var pro = await GetProAsync(user, topicId, sourceId);

        Assert.True(pro.EvidenceMap.ReadySourceCount >= 1);
        Assert.True(pro.EvidenceMap.CanClaimSourceGrounded);
        Assert.Contains(pro.LinkedConcepts, l => l.ConceptKey == "mitosis" || l.WikiPageId == pageId);
        Assert.Contains(pro.NotebookHandoffs, a => a.ActionType == "open_notebook_pack");
        Assert.Equal("ready", pro.NotebookPackReadiness);
        AssertSafePayload(JsonSerializer.Serialize(pro, JsonOptions), user.UserId, rawSource);
    }

    [Fact]
    public async Task SourceWikiPro_StaleSourceAndCitationWarningBlockOverclaim()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-wiki-pro-stale");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Source Wiki Pro Stale");
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(factory, user.UserId, topicId, "Stale Citation Source", "stale citation source rawSourceChunk secret");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            db.SourceCitationChecks.Add(new SourceCitationCheck
            {
                Id = Guid.NewGuid(),
                UserId = user.UserId,
                TopicId = topicId,
                SourceId = sourceId,
                CitationId = "citation-1",
                CheckStatus = "unsupported",
                Confidence = 0.20m,
                Reason = "unsupported",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var lifecycle = scope.ServiceProvider.GetRequiredService<ISourceEvidenceLifecycleService>();
            await lifecycle.BuildSourceEvidenceBundleAsync(user.UserId, topicId, question: "citation");
            await lifecycle.MarkSourceStaleAsync(user.UserId, sourceId, "stale test");
        }

        var pro = await GetProAsync(user, topicId, sourceId);

        Assert.False(pro.EvidenceMap.CanClaimSourceGrounded);
        Assert.Contains(pro.StaleSources, s => s.SourceId == sourceId);
        Assert.Contains(pro.CitationWarnings, w => w.WarningCode is "citation_unsupported" or "citation_review_needed" or "citation_stale");
        Assert.Contains(pro.RecommendedActions, a => a.ActionType is "source_review" or "citation_review");
        Assert.Contains(pro.ConflictWarnings, w => w.WarningCode == "source_grounding_blocked");
        AssertSafePayload(JsonSerializer.Serialize(pro, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task SourceWikiPro_WikiRepairDuplicateAndManualNotesProduceSafeActions()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-wiki-pro-wiki");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Wiki Pro Topic");
        var pageId = await CoordinationTestHelpers.SeedWikiPageAsync(factory, user.UserId, topicId, "Repair Wiki", "student manual note");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var page = await db.WikiPages.SingleAsync(p => p.Id == pageId);
            page.SourceReadiness = "ready";
            page.EvidenceStatus = "ready";
            var block = await db.WikiBlocks.SingleAsync(b => b.WikiPageId == pageId);
            block.BlockType = WikiBlockType.ManualNote;
            block.Content = "student manual note";
            block.ConceptKey = "repair-concept";
            db.WikiBlocks.AddRange(
                new WikiBlock
                {
                    Id = Guid.NewGuid(),
                    WikiPageId = pageId,
                    BlockType = WikiBlockType.RepairNote,
                    Title = "Repair",
                    Content = "repair note",
                    ConceptKey = "repair-concept",
                    OrderIndex = 2,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new WikiBlock
                {
                    Id = Guid.NewGuid(),
                    WikiPageId = pageId,
                    BlockType = WikiBlockType.RepairNote,
                    Title = "Repair",
                    Content = "repair note",
                    ConceptKey = "repair-concept",
                    OrderIndex = 3,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }

        var pro = await GetProAsync(user, topicId, wikiPageId: pageId);

        Assert.Contains(pro.WikiRepairPages, p => p.WikiPageId == pageId);
        Assert.Contains(pro.ManualNotePages, p => p.WikiPageId == pageId && p.ManualNotePreserved);
        Assert.Contains(pro.DuplicateTracePages, p => p.WikiPageId == pageId);
        Assert.Contains(pro.RecommendedActions, a => a.ActionType is "repair_wiki_page" or "update_wiki_note");
        Assert.Contains(pro.TutorHandoffs, a => a.ActionType == "ask_tutor");
        AssertSafePayload(JsonSerializer.Serialize(pro, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task SourceWikiPro_DashboardCarriesContractAndCrossUserAccessIsBlocked()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-wiki-pro-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-wiki-pro-other");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, owner.UserId, "Dashboard Source Wiki Pro");
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(factory, owner.UserId, topicId, "Dashboard Pro Source", "dashboard pro source");
        var pageId = await CoordinationTestHelpers.SeedWikiPageAsync(factory, owner.UserId, topicId, "Dashboard Pro Wiki", "dashboard wiki");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var topic = await db.Topics.SingleAsync(t => t.Id == topicId);
            topic.LastAccessedAt = DateTime.UtcNow.AddMinutes(5);
            await db.SaveChangesAsync();
        }

        var dashboard = await owner.Client.GetFromJsonAsync<DashboardTodayDto>("/api/dashboard/today")
            ?? throw new InvalidOperationException("Dashboard payload missing.");
        Assert.NotNull(dashboard.SourceWikiPro);
        Assert.Equal(dashboard.SourceWikiIntelligenceProfile?.SourceCount, dashboard.SourceWikiPro!.EvidenceMap.UploadedSourceCount);

        var deniedTopic = await other.Client.GetAsync($"/api/sources/wiki-pro?topicId={topicId}");
        var deniedSource = await other.Client.GetAsync($"/api/sources/wiki-pro?sourceId={sourceId}");
        var deniedPage = await other.Client.GetAsync($"/api/sources/wiki-pro?wikiPageId={pageId}");

        Assert.Equal(HttpStatusCode.NotFound, deniedTopic.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deniedSource.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deniedPage.StatusCode);
        AssertSafePayload(JsonSerializer.Serialize(dashboard.SourceWikiPro, JsonOptions), owner.UserId);
    }

    private static async Task<OrkaSourceWikiProDto> GetProAsync(
        CoordinationTestUser user,
        Guid? topicId = null,
        Guid? sourceId = null,
        Guid? wikiPageId = null)
    {
        var query = new List<string>();
        if (topicId.HasValue) query.Add($"topicId={topicId}");
        if (sourceId.HasValue) query.Add($"sourceId={sourceId}");
        if (wikiPageId.HasValue) query.Add($"wikiPageId={wikiPageId}");
        var url = "/api/sources/wiki-pro" + (query.Count == 0 ? string.Empty : "?" + string.Join("&", query));
        var response = await user.Client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrkaSourceWikiProDto>())!;
    }

    private static void AssertSafePayload(string json, Guid userId, string? rawSourceText = null)
    {
        if (!string.IsNullOrWhiteSpace(rawSourceText))
        {
            Assert.DoesNotContain(rawSourceText, json, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var marker in new[]
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
            "token_marker_secret_value",
            "answerKey",
            "correctAnswer",
            "stackTrace",
            "ownerId",
            "userId"
        })
        {
            Assert.DoesNotContain(marker, json, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(userId.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("success guarantee", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("official curriculum complete", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%100", json, StringComparison.OrdinalIgnoreCase);
    }
}
