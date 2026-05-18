using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class LearningNotebookStudioTests
{
    [Fact]
    public async Task BuildMilestonePack_CreatesUserScopedPackWithSafeArtifacts()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "notebook-pack");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Algorithms");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(factory, user.UserId, topicId, DateTime.UtcNow);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var graph = new ConceptGraphSnapshot
            {
                Id = Guid.NewGuid(),
                UserId = user.UserId,
                TopicId = topicId,
                PlanRequestId = Guid.NewGuid(),
                IntentHash = "algorithms-pack",
                TopicTitle = "Algorithms",
                ApprovedResearchIntent = "Learn algorithms",
                Domain = "algorithms",
                SourceConfidence = "medium",
                SourceBundleHash = "bundle",
                GraphJson = "{}",
                CreatedAt = DateTime.UtcNow
            };
            db.ConceptGraphSnapshots.Add(graph);
            db.LearningConcepts.AddRange(
                new LearningConcept { Id = Guid.NewGuid(), ConceptGraphSnapshotId = graph.Id, StableKey = "complexity", Label = "Complexity", Order = 0 },
                new LearningConcept { Id = Guid.NewGuid(), ConceptGraphSnapshotId = graph.Id, StableKey = "searching", Label = "Searching", Order = 1 },
                new LearningConcept { Id = Guid.NewGuid(), ConceptGraphSnapshotId = graph.Id, StableKey = "sorting", Label = "Sorting", Order = 2 });
            db.ConceptMasteries.Add(new ConceptMastery
            {
                Id = Guid.NewGuid(),
                UserId = user.UserId,
                TopicId = topicId,
                ConceptKey = "complexity",
                Label = "Complexity",
                MasteryScore = 0.75m,
                Confidence = 0.7m,
                RemediationNeed = "none"
            });
            db.ConceptMasteries.Add(new ConceptMastery
            {
                Id = Guid.NewGuid(),
                UserId = user.UserId,
                TopicId = topicId,
                ConceptKey = "sorting",
                Label = "Sorting",
                MasteryScore = 0.35m,
                Confidence = 0.6m,
                RemediationNeed = "medium"
            });
            await db.SaveChangesAsync();
        }

        var response = await user.Client.PostAsJsonAsync($"/api/notebook-studio/topic/{topicId}/milestone-pack", new
        {
            sessionId,
            packType = "milestone_review",
            includeArtifacts = true
        });

        response.EnsureSuccessStatusCode();
        var pack = await response.Content.ReadFromJsonAsync<LearningNotebookPackDto>();
        Assert.NotNull(pack);
        Assert.Equal(topicId, pack!.TopicId);
        Assert.Contains("complexity", pack.CompletedConceptKeys);
        Assert.Contains("sorting", pack.WeakConceptKeys);
        Assert.True(pack.ArtifactIds.Count >= 4);
        Assert.All(pack.Artifacts, artifact =>
        {
            Assert.NotEqual(Guid.Empty, artifact.Id);
            Assert.DoesNotContain("rawProviderPayload", artifact.SafeContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hiddenPrompt", artifact.SafeContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("C:\\", artifact.SafeContent, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task NotebookStudio_IsUserScoped()
    {
        using var factory = new ApiSmokeFactory();
        var userA = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "notebook-owner");
        var userB = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "notebook-other");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, userA.UserId, "Private Notebook");

        var created = await userA.Client.PostAsJsonAsync($"/api/notebook-studio/topic/{topicId}/milestone-pack", new { includeArtifacts = false });
        created.EnsureSuccessStatusCode();
        var pack = await created.Content.ReadFromJsonAsync<LearningNotebookPackDto>();
        Assert.NotNull(pack);

        var denied = await userB.Client.GetAsync($"/api/notebook-studio/packs/{pack!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        var list = await userB.Client.GetFromJsonAsync<LearningNotebookPackListDto>($"/api/notebook-studio/topic/{topicId}/packs");
        Assert.NotNull(list);
        Assert.Equal(0, list!.Count);
    }

    [Fact]
    public async Task ListPacks_CanFilterByWikiPageAndSkipsSoftDeletedPacks()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "notebook-list-filter");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Notebook Filter Topic");
        var pageId = await SeedNotebookWikiPageAsync(factory, user.UserId, topicId);

        var topicPackResponse = await user.Client.PostAsJsonAsync($"/api/notebook-studio/topic/{topicId}/milestone-pack", new
        {
            includeArtifacts = false
        });
        topicPackResponse.EnsureSuccessStatusCode();

        var pagePackResponse = await user.Client.PostAsJsonAsync($"/api/notebook-studio/wiki-page/{pageId}/pack", new
        {
            includeArtifacts = false
        });
        pagePackResponse.EnsureSuccessStatusCode();
        var pagePack = await pagePackResponse.Content.ReadFromJsonAsync<LearningNotebookPackDto>();
        Assert.NotNull(pagePack);

        var all = await user.Client.GetFromJsonAsync<LearningNotebookPackListDto>($"/api/notebook-studio/topic/{topicId}/packs");
        Assert.NotNull(all);
        Assert.True(all!.Count >= 2);

        var filtered = await user.Client.GetFromJsonAsync<LearningNotebookPackListDto>($"/api/notebook-studio/topic/{topicId}/packs?wikiPageId={pageId}");
        Assert.NotNull(filtered);
        var filteredPack = Assert.Single(filtered!.Items);
        Assert.Equal(pageId, filteredPack.WikiPageId);
        Assert.Equal("Big-O Wiki", filteredPack.WikiPageTitle);
        Assert.Equal(pagePack!.Id, filteredPack.Id);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var stored = await db.LearningNotebookPacks.SingleAsync(p => p.Id == pagePack.Id);
            stored.IsDeleted = true;
            await db.SaveChangesAsync();
        }

        var afterDelete = await user.Client.GetFromJsonAsync<LearningNotebookPackListDto>($"/api/notebook-studio/topic/{topicId}/packs?wikiPageId={pageId}");
        Assert.NotNull(afterDelete);
        Assert.Equal(0, afterDelete!.Count);
    }

    [Fact]
    public async Task SourceNotebookEndpoints_AreUserScopedAndDoNotExposeRawChunks()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-notebook-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-notebook-other");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, owner.UserId, "Source Notebook Topic");
        var longTail = new string('q', 420);
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(
            factory,
            owner.UserId,
            topicId,
            "Source Notebook PDF",
            $"source notebook citation evidence {longTail}");

        var topicNotebook = await owner.Client.GetFromJsonAsync<SourceNotebookDto>($"/api/sources/topic/{topicId}/notebook");
        Assert.NotNull(topicNotebook);
        Assert.Equal(topicId, topicNotebook!.TopicId);
        Assert.Equal(1, topicNotebook.SourceCount);
        Assert.Equal(1, topicNotebook.ReadySourceCount);
        var topicNotebookJson = System.Text.Json.JsonSerializer.Serialize(topicNotebook);
        Assert.DoesNotContain(longTail, topicNotebookJson);
        Assert.DoesNotContain("rawProviderPayload", topicNotebookJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", topicNotebookJson, StringComparison.OrdinalIgnoreCase);

        var sourceNotebook = await owner.Client.GetFromJsonAsync<SourceNotebookDto>($"/api/sources/{sourceId}/notebook");
        Assert.NotNull(sourceNotebook);
        Assert.Equal(sourceId, sourceNotebook!.SourceId);
        Assert.Single(sourceNotebook.Sources);

        var deniedTopic = await other.Client.GetAsync($"/api/sources/topic/{topicId}/notebook");
        Assert.Equal(HttpStatusCode.NotFound, deniedTopic.StatusCode);
        var deniedSource = await other.Client.GetAsync($"/api/sources/{sourceId}/notebook");
        Assert.Equal(HttpStatusCode.NotFound, deniedSource.StatusCode);
    }

    [Fact]
    public async Task BuildSourcePack_CreatesSourcePageAndFiltersSourcePacks()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-pack-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-pack-other");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, owner.UserId, "Source Pack Topic");
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(
            factory,
            owner.UserId,
            topicId,
            "Algorithms Notes",
            "binary search source evidence with safe citation notes");

        var response = await owner.Client.PostAsJsonAsync($"/api/notebook-studio/sources/{sourceId}/pack", new
        {
            packType = "source_digest",
            includeArtifacts = true
        });
        response.EnsureSuccessStatusCode();
        var pack = await response.Content.ReadFromJsonAsync<LearningNotebookPackDto>();
        Assert.NotNull(pack);
        Assert.Equal(sourceId, pack!.SourceId);
        Assert.Equal("source", pack.SourceSurface);
        Assert.Equal("source_digest", pack.PackType);
        Assert.NotNull(pack.WikiPageId);
        Assert.Contains(pack.Artifacts, a => a.ArtifactType == "source_digest");
        Assert.All(pack.Artifacts, artifact =>
        {
            Assert.DoesNotContain("rawProviderPayload", artifact.SafeContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("rawSourceChunk", artifact.SafeContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("answerKey", artifact.SafeContent, StringComparison.OrdinalIgnoreCase);
        });

        var filtered = await owner.Client.GetFromJsonAsync<LearningNotebookPackListDto>($"/api/notebook-studio/topic/{topicId}/packs?surface=source&sourceId={sourceId}");
        Assert.NotNull(filtered);
        var filteredPack = Assert.Single(filtered!.Items);
        Assert.Equal(pack.Id, filteredPack.Id);
        Assert.Equal(sourceId, filteredPack.SourceId);

        var denied = await other.Client.PostAsJsonAsync($"/api/notebook-studio/sources/{sourceId}/pack", new { includeArtifacts = false });
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        Assert.Equal(1, await db.WikiPages.CountAsync(p => p.UserId == owner.UserId && p.TopicId == topicId && p.PageType == "orkalm_source"));
    }

    [Fact]
    public async Task SourceConceptLinks_AreUserScopedIdempotentAndPublicSafe()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-concept-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-concept-other");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, owner.UserId, "Source Concept Topic");
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(
            factory,
            owner.UserId,
            topicId,
            "Binary Search Notes",
            "binary search concept evidence with rawSourceChunk marker that must never appear publicly");
        Guid conceptPageId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var now = DateTime.UtcNow;
            conceptPageId = Guid.NewGuid();
            db.WikiPages.Add(new WikiPage
            {
                Id = conceptPageId,
                UserId = owner.UserId,
                TopicId = topicId,
                PageKey = "concept:binary-search",
                PageType = "concept",
                ConceptKey = "binary-search",
                Title = "Binary Search",
                Status = "ready",
                SourceReadiness = "source_grounded",
                EvidenceStatus = "source_grounded",
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
        }

        var sync = await owner.Client.PostAsync($"/api/sources/{sourceId}/concept-links/sync", null);
        sync.EnsureSuccessStatusCode();
        var links = await sync.Content.ReadFromJsonAsync<SourceConceptLinkSummaryDto>();
        Assert.NotNull(links);
        Assert.Equal(sourceId, links!.SourceId);
        Assert.True(links.ConfirmedLinkCount >= 1);
        Assert.Contains(links.Links, l =>
            !l.IsSuggestion &&
            l.WikiPageId == conceptPageId &&
            l.ConceptKey == "binary-search" &&
            l.Confidence == "high");
        var safeJson = System.Text.Json.JsonSerializer.Serialize(links);
        Assert.DoesNotContain("rawSourceChunk", safeJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawProviderPayload", safeJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", safeJson, StringComparison.OrdinalIgnoreCase);

        var second = await owner.Client.PostAsync($"/api/sources/{sourceId}/concept-links/sync", null);
        second.EnsureSuccessStatusCode();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            Assert.Equal(1, await db.WikiLinks.CountAsync(l =>
                l.UserId == owner.UserId &&
                l.TargetPageId == conceptPageId &&
                l.LinkType == "source_supports" &&
                !l.IsDeleted));
        }

        var graph = await owner.Client.GetFromJsonAsync<SourceConceptGraphDto>($"/api/sources/topic/{topicId}/concept-graph");
        Assert.NotNull(graph);
        Assert.Contains(graph!.Nodes, n => n.NodeType == "source_page");
        Assert.Contains(graph.Nodes, n => n.NodeType == "concept_page" && n.ConceptKey == "binary-search");
        Assert.Contains(graph.Edges, e => e.LinkType == "source_supports" && e.Confidence == "high");

        var supportingSources = await owner.Client.GetFromJsonAsync<SourceConceptLinkSummaryDto>($"/api/wiki/pages/{conceptPageId}/source-links");
        Assert.NotNull(supportingSources);
        Assert.Contains(supportingSources!.Links, l => l.SourceId == sourceId && !l.IsSuggestion);

        var denied = await other.Client.GetAsync($"/api/sources/{sourceId}/concept-links");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        var deniedSync = await other.Client.PostAsync($"/api/sources/{sourceId}/concept-links/sync", null);
        Assert.Equal(HttpStatusCode.NotFound, deniedSync.StatusCode);
    }

    [Fact]
    public async Task BuildPackArtifact_CanCreateConceptGraphMindMapAndAudioOverviewSafely()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "notebook-artifact");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Data Structures");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var graph = new ConceptGraphSnapshot
            {
                Id = Guid.NewGuid(),
                UserId = user.UserId,
                TopicId = topicId,
                PlanRequestId = Guid.NewGuid(),
                IntentHash = "data-structures-map",
                TopicTitle = "Data Structures",
                ApprovedResearchIntent = "Review data structures",
                Domain = "computer_science",
                SourceConfidence = "medium",
                SourceBundleHash = "bundle",
                GraphJson = "{}",
                CreatedAt = DateTime.UtcNow
            };
            db.ConceptGraphSnapshots.Add(graph);
            db.LearningConcepts.AddRange(
                new LearningConcept { Id = Guid.NewGuid(), ConceptGraphSnapshotId = graph.Id, StableKey = "array", Label = "Array", Order = 0 },
                new LearningConcept { Id = Guid.NewGuid(), ConceptGraphSnapshotId = graph.Id, StableKey = "linked-list", Label = "Linked List", Order = 1 });
            db.ConceptRelations.Add(new ConceptRelation
            {
                Id = Guid.NewGuid(),
                ConceptGraphSnapshotId = graph.Id,
                SourceConceptKey = "array",
                TargetConceptKey = "linked-list",
                RelationType = "contrast",
                Weight = 0.8
            });
            db.ConceptMasteries.Add(new ConceptMastery
            {
                Id = Guid.NewGuid(),
                UserId = user.UserId,
                TopicId = topicId,
                ConceptKey = "linked-list",
                Label = "Linked List",
                MasteryScore = 0.3m,
                Confidence = 0.55m,
                RemediationNeed = "high",
                MisconceptionEvidenceJson = "[\"pointer_confusion\"]"
            });
            await db.SaveChangesAsync();
        }

        var created = await user.Client.PostAsJsonAsync($"/api/notebook-studio/topic/{topicId}/milestone-pack", new { includeArtifacts = false });
        created.EnsureSuccessStatusCode();
        var pack = await created.Content.ReadFromJsonAsync<LearningNotebookPackDto>();

        var mindMapResponse = await user.Client.PostAsJsonAsync($"/api/notebook-studio/packs/{pack!.Id}/artifact", new
        {
            artifactType = "mind_map"
        });
        mindMapResponse.EnsureSuccessStatusCode();
        var mindMap = await mindMapResponse.Content.ReadFromJsonAsync<LearningArtifactDto>();
        Assert.Equal("mind_map", mindMap!.ArtifactType);
        Assert.Equal("mermaid", mindMap.RenderFormat);
        Assert.DoesNotContain("rawSourceChunk", mindMap.SafeContent, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(mindMap.ContentJson);
        Assert.Contains("concept_graph_mind_map_v1", mindMap.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("linked-list", mindMap.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("misconceptionIndicator", mindMap.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("misconception_probe", mindMap.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sourceReadiness", mindMap.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("contrast", mindMap.ContentJson!, StringComparison.OrdinalIgnoreCase);

        var audioResponse = await user.Client.PostAsJsonAsync($"/api/notebook-studio/packs/{pack.Id}/artifact", new
        {
            artifactType = "audio_overview"
        });
        audioResponse.EnsureSuccessStatusCode();
        var audio = await audioResponse.Content.ReadFromJsonAsync<LearningArtifactDto>();
        Assert.Equal("audio_overview", audio!.ArtifactType);
        Assert.DoesNotContain("systemPrompt", audio.SafeContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("audioOverviewJobId", audio.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("transcriptArtifact", audio.ContentJson!, StringComparison.OrdinalIgnoreCase);

        var scriptResponse = await user.Client.PostAsJsonAsync($"/api/notebook-studio/packs/{pack.Id}/artifact", new
        {
            artifactType = "audio_script"
        });
        scriptResponse.EnsureSuccessStatusCode();
        var script = await scriptResponse.Content.ReadFromJsonAsync<LearningArtifactDto>();
        Assert.Equal("audio_script", script!.ArtifactType);
        Assert.Contains("[HOCA]:", script.SafeContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawProviderPayload", script.SafeContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildWikiPagePack_UsesPageBlocksQuestionsAndSafeArtifactSection()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "notebook-page-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "notebook-page-other");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, owner.UserId, "Algorithms Wiki Page");
        var pageId = await SeedNotebookWikiPageAsync(factory, owner.UserId, topicId);

        var response = await owner.Client.PostAsJsonAsync($"/api/notebook-studio/wiki-page/{pageId}/pack", new
        {
            includeArtifacts = true
        });

        response.EnsureSuccessStatusCode();
        var pack = await response.Content.ReadFromJsonAsync<LearningNotebookPackDto>();
        Assert.NotNull(pack);
        Assert.Equal(pageId, pack!.WikiPageId);
        Assert.Equal("Big-O Wiki", pack.WikiPageTitle);
        Assert.Equal("wiki_page_review", pack.PackType);
        Assert.Contains("big-o", pack.CompletedConceptKeys);
        Assert.Contains("big-o", pack.WeakConceptKeys);
        Assert.Contains("growth-rate-confusion", pack.MisconceptionKeys);
        Assert.Contains(pack.NextActions, action => action.ActionType == "tutor_repair");
        Assert.Contains("Wiki sayfasi", pack.Summary, StringComparison.OrdinalIgnoreCase);
        var artifactTypes = pack.Artifacts.Select(a => a.ArtifactType).ToArray();
        Assert.Contains("study_guide", artifactTypes);
        Assert.Contains("briefing_doc", artifactTypes);
        Assert.Contains("source_digest", artifactTypes);
        Assert.Contains("misconception_repair_pack", artifactTypes);
        Assert.Contains("worked_example_set", artifactTypes);
        Assert.Contains("retrieval_card_set", artifactTypes);
        Assert.All(pack.Artifacts, artifact =>
        {
            Assert.Equal($"wiki_page:{pageId:N}", artifact.WikiNotebookSectionKey);
            Assert.DoesNotContain("rawProviderPayload", artifact.SafeContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hiddenPrompt", artifact.SafeContent, StringComparison.OrdinalIgnoreCase);
        });
        var studyGuide = Assert.Single(pack.Artifacts.Where(a => a.ArtifactType == "study_guide"));
        Assert.Contains("Big-O Wiki", studyGuide.SafeContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ogrenci soru sinyalleri", studyGuide.SafeContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Big-O neden sadece en buyuk terime", studyGuide.SafeContent, StringComparison.OrdinalIgnoreCase);
        var repairPack = Assert.Single(pack.Artifacts.Where(a => a.ArtifactType == "misconception_repair_pack"));
        Assert.Contains("growth-rate-confusion", repairPack.SafeContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Pekistirme notlari", repairPack.SafeContent, StringComparison.OrdinalIgnoreCase);
        var sourceDigest = Assert.Single(pack.Artifacts.Where(a => a.ArtifactType == "source_digest"));
        Assert.Equal("evidence_insufficient", sourceDigest.SourceBasis);
        Assert.Contains("kaynak yoksa", sourceDigest.SafeContent, StringComparison.OrdinalIgnoreCase);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var storedPack = await db.LearningNotebookPacks.AsNoTracking().SingleAsync(p => p.Id == pack.Id);
            Assert.Equal(pageId, storedPack.WikiPageId);
            Assert.Equal("Big-O Wiki", storedPack.WikiPageTitle);
            Assert.Equal("big-o", storedPack.WikiPageKey);
        }

        var audioResponse = await owner.Client.PostAsJsonAsync("/api/audio/overview", new
        {
            topicId
        });
        audioResponse.EnsureSuccessStatusCode();
        var audio = await audioResponse.Content.ReadFromJsonAsync<AudioOverviewJobDto>();
        Assert.NotNull(audio);
        Assert.Contains("Big-O Wiki", audio!.Script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OrkaLM pack", audio.Script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("growth-rate-confusion", audio.Script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawSourceChunk", audio.Script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("systemPrompt", audio.Script, StringComparison.OrdinalIgnoreCase);

        var mindMapResponse = await owner.Client.PostAsJsonAsync($"/api/notebook-studio/packs/{pack.Id}/artifact", new
        {
            artifactType = "mind_map"
        });
        mindMapResponse.EnsureSuccessStatusCode();
        var mindMap = await mindMapResponse.Content.ReadFromJsonAsync<LearningArtifactDto>();
        Assert.Equal("mind_map", mindMap!.ArtifactType);
        Assert.Contains("Big-O Wiki", mindMap.SafeContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wikiPageId", mindMap.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(pageId.ToString(), mindMap.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("isWikiPageFocus", mindMap.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("review_check", mindMap.ContentJson!, StringComparison.OrdinalIgnoreCase);

        var flashcardResponse = await owner.Client.PostAsJsonAsync($"/api/notebook-studio/packs/{pack.Id}/artifact", new
        {
            artifactType = "flashcard_set"
        });
        flashcardResponse.EnsureSuccessStatusCode();
        var flashcards = await flashcardResponse.Content.ReadFromJsonAsync<LearningArtifactDto>();
        Assert.Equal("flashcard_set", flashcards!.ArtifactType);
        Assert.Contains(pageId.ToString(), flashcards.ContentJson!, StringComparison.OrdinalIgnoreCase);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            Assert.True(await db.Flashcards.AnyAsync(f => f.UserId == owner.UserId && f.TopicId == topicId && f.WikiPageId == pageId));
        }

        var reviewResponse = await owner.Client.PostAsJsonAsync($"/api/notebook-studio/packs/{pack.Id}/artifact", new
        {
            artifactType = "review_quiz"
        });
        reviewResponse.EnsureSuccessStatusCode();
        var review = await reviewResponse.Content.ReadFromJsonAsync<LearningArtifactDto>();
        Assert.Equal("review_quiz", review!.ArtifactType);
        Assert.Contains("notebook_review_quiz_v1", review.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(pageId.ToString(), review.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("big-o", review.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correctAnswer", review.SafeContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", review.ContentJson!, StringComparison.OrdinalIgnoreCase);

        var slideResponse = await owner.Client.PostAsJsonAsync($"/api/notebook-studio/packs/{pack.Id}/artifact", new
        {
            artifactType = "slide_deck_outline"
        });
        slideResponse.EnsureSuccessStatusCode();
        var slide = await slideResponse.Content.ReadFromJsonAsync<LearningArtifactDto>();
        Assert.Equal("slide_deck_outline", slide!.ArtifactType);
        Assert.Contains("Big-O Wiki", slide.SafeContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sayfa ozeti", slide.SafeContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Takilma ve onarim", slide.SafeContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Accessibility", slide.SafeContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wikiPageId", slide.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(pageId.ToString(), slide.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("videoReadyPackage", slide.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exportReadiness", slide.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sourceWarning", slide.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawSourceChunk", slide.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("success guarantee", slide.SafeContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("resmi osym", slide.SafeContent, StringComparison.OrdinalIgnoreCase);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var artifactBlock = await db.WikiBlocks.AsNoTracking()
                .FirstOrDefaultAsync(b => b.WikiPageId == pageId && b.LearningArtifactId == slide.Id && !b.IsDeleted);
            Assert.NotNull(artifactBlock);
            Assert.Equal(WikiBlockType.ArtifactLink, artifactBlock!.BlockType);
            Assert.DoesNotContain("rawProviderPayload", artifactBlock.Content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hiddenPrompt", artifactBlock.Content, StringComparison.OrdinalIgnoreCase);
        }

        var denied = await other.Client.PostAsJsonAsync($"/api/notebook-studio/wiki-page/{pageId}/pack", new
        {
            includeArtifacts = false
        });
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
    }

    [Fact]
    public async Task BuildAdvancedMediaExportArtifacts_CreatesSafeFutureReadyManifests()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "notebook-media-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "notebook-media-other");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, owner.UserId, "Media Export Topic");
        var pageId = await SeedNotebookWikiPageAsync(factory, owner.UserId, topicId);

        var created = await owner.Client.PostAsJsonAsync($"/api/notebook-studio/wiki-page/{pageId}/pack", new
        {
            includeArtifacts = false
        });
        created.EnsureSuccessStatusCode();
        var pack = await created.Content.ReadFromJsonAsync<LearningNotebookPackDto>();
        Assert.NotNull(pack);

        var denied = await other.Client.PostAsJsonAsync($"/api/notebook-studio/packs/{pack!.Id}/artifact", new
        {
            artifactType = "video_ready_package"
        });
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        var transcript = await BuildArtifactAsync(owner, pack.Id, "audio_transcript");
        Assert.Equal("audio_transcript", transcript.ArtifactType);
        Assert.Contains("audio_transcript_v1", transcript.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("transcript_ready", transcript.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawSourceChunk", transcript.SafeContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hiddenPrompt", transcript.SafeContent, StringComparison.OrdinalIgnoreCase);

        var captions = await BuildArtifactAsync(owner, pack.Id, "caption_track");
        Assert.Equal("caption_track", captions.ArtifactType);
        Assert.Equal("plain_text", captions.RenderFormat);
        Assert.Contains("WEBVTT", captions.SafeContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("caption_track_v1", captions.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("needs_review", captions.ContentJson!, StringComparison.OrdinalIgnoreCase);

        var videoReady = await BuildArtifactAsync(owner, pack.Id, "video_ready_package");
        Assert.Equal("video_ready_package", videoReady.ArtifactType);
        Assert.Contains("video_ready_package_v1", videoReady.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("generatedVideo", videoReady.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("false", videoReady.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("captionOutline", videoReady.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("visualInstructionSet", videoReady.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("outline_ready", videoReady.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("video generated", videoReady.SafeContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawProviderPayload", videoReady.ContentJson!, StringComparison.OrdinalIgnoreCase);

        var slideManifest = await BuildArtifactAsync(owner, pack.Id, "slide_export_manifest");
        Assert.Equal("slide_export_manifest", slideManifest.ArtifactType);
        Assert.Contains("slide_export_manifest_v1", slideManifest.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pptx_not_enabled", slideManifest.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pptxGenerated", slideManifest.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("false", slideManifest.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("accessibilitySummary", slideManifest.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawSourceChunk", slideManifest.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("success guarantee", slideManifest.SafeContent, StringComparison.OrdinalIgnoreCase);

        var narration = await BuildArtifactAsync(owner, pack.Id, "narration_script");
        Assert.Equal("narration_script", narration.ArtifactType);
        Assert.Contains("narration_script_v1", narration.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[HOCA]:", narration.SafeContent, StringComparison.OrdinalIgnoreCase);

        var visuals = await BuildArtifactAsync(owner, pack.Id, "visual_instruction_set");
        Assert.Equal("visual_instruction_set", visuals.ArtifactType);
        Assert.Contains("visual_instruction_set_v1", visuals.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("image/video uretmez", visuals.SafeContent, StringComparison.OrdinalIgnoreCase);

        var accessibility = await BuildArtifactAsync(owner, pack.Id, "media_accessibility_note");
        Assert.Equal("media_accessibility_note", accessibility.ArtifactType);
        Assert.Contains("media_accessibility_note_v1", accessibility.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("transcript/caption", accessibility.SafeContent, StringComparison.OrdinalIgnoreCase);

        foreach (var artifact in new[] { transcript, captions, videoReady, slideManifest, narration, visuals, accessibility })
        {
            Assert.DoesNotContain("rawPrompt", artifact.SafeContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("systemPrompt", artifact.SafeContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("answerKey", artifact.SafeContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("correctAnswer", artifact.SafeContent, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("C:\\", artifact.SafeContent, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ExportSlideDeck_BuildsDeterministicPreviewMarkdownHtmlAndHonestPptxStatus()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "notebook-export-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "notebook-export-other");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, owner.UserId, "Export Topic");
        var pageId = await SeedNotebookWikiPageAsync(factory, owner.UserId, topicId);

        var created = await owner.Client.PostAsJsonAsync($"/api/notebook-studio/wiki-page/{pageId}/pack", new
        {
            includeArtifacts = true
        });
        created.EnsureSuccessStatusCode();
        var pack = await created.Content.ReadFromJsonAsync<LearningNotebookPackDto>();
        Assert.NotNull(pack);
        Assert.Contains(pack!.Artifacts, artifact => artifact.ArtifactType == "slide_deck_outline");

        var deniedPreview = await other.Client.GetAsync($"/api/notebook-studio/packs/{pack.Id}/export/preview");
        Assert.Equal(HttpStatusCode.NotFound, deniedPreview.StatusCode);

        var previewResponse = await owner.Client.GetAsync($"/api/notebook-studio/packs/{pack.Id}/export/preview");
        previewResponse.EnsureSuccessStatusCode();
        var preview = await previewResponse.Content.ReadFromJsonAsync<NotebookSlideExportPreviewDto>();
        Assert.NotNull(preview);
        Assert.True(preview!.SlideCount >= 4);
        Assert.Equal("preview_ready", preview.ExportReadiness);
        Assert.Contains(preview.SourceBasis, new[] { "wiki_backed", "evidence_insufficient", "model_assisted" });
        Assert.Contains(preview.SourceReadiness, new[] { "wiki_backed", "evidence_insufficient", "model_assisted" });
        Assert.Contains(preview.Slides, slide => slide.HasSpeakerNotes && !string.IsNullOrWhiteSpace(slide.CheckpointQuestion));
        Assert.Contains(preview.Slides, slide => !string.IsNullOrWhiteSpace(slide.SourceLabel) || !string.IsNullOrWhiteSpace(slide.AccessibilitySummary));
        Assert.Contains(preview.Warnings, warning =>
            warning.Contains("Evidence", StringComparison.OrdinalIgnoreCase) ||
            warning.Contains("kaynak", StringComparison.OrdinalIgnoreCase) ||
            warning.Contains("kanıt", StringComparison.OrdinalIgnoreCase) ||
            warning.Contains("kanit", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("rawSourceChunk", preview.AccessibilitySummary, StringComparison.OrdinalIgnoreCase);

        var markdownResponse = await owner.Client.PostAsJsonAsync($"/api/notebook-studio/packs/{pack.Id}/export", new
        {
            format = "markdown"
        });
        markdownResponse.EnsureSuccessStatusCode();
        var markdown = await markdownResponse.Content.ReadFromJsonAsync<NotebookExportResultDto>();
        Assert.NotNull(markdown);
        Assert.Equal("markdown", markdown!.Format);
        Assert.Equal("ready", markdown.Status);
        Assert.Equal("text/markdown", markdown.ContentType);
        Assert.Contains("Slide count", markdown.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Source readiness", markdown.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Accessibility", markdown.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Speaker notes", markdown.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Checkpoint", markdown.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawPrompt", markdown.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("success guarantee", markdown.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", markdown.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", markdown.Content, StringComparison.OrdinalIgnoreCase);
        Assert.False(markdown.BinaryExportAvailable);
        Assert.False(markdown.PptxLocalProofAvailable);

        var htmlResponse = await owner.Client.PostAsJsonAsync($"/api/notebook-studio/packs/{pack.Id}/export", new
        {
            format = "html"
        });
        htmlResponse.EnsureSuccessStatusCode();
        var html = await htmlResponse.Content.ReadFromJsonAsync<NotebookExportResultDto>();
        Assert.NotNull(html);
        Assert.Equal("html", html!.Format);
        Assert.Equal("text/html", html.ContentType);
        Assert.Contains("<!doctype html>", html.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Source readiness", html.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Accessibility", html.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Checkpoint", html.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", html.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawProviderPayload", html.Content, StringComparison.OrdinalIgnoreCase);

        var manifestResponse = await owner.Client.PostAsJsonAsync($"/api/notebook-studio/packs/{pack.Id}/export", new
        {
            format = "manifest_only"
        });
        manifestResponse.EnsureSuccessStatusCode();
        var manifest = await manifestResponse.Content.ReadFromJsonAsync<NotebookExportResultDto>();
        Assert.NotNull(manifest);
        Assert.Equal("manifest_only", manifest!.Format);
        Assert.Contains(manifest.ExportReadiness, new[] { "manifest_ready", "outline_ready" });
        Assert.Contains("Slide count", manifest.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Source readiness", manifest.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PPTX status: pptx_not_enabled", manifest.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Accessibility", manifest.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawSourceChunk", manifest.Content, StringComparison.OrdinalIgnoreCase);

        var pptxResponse = await owner.Client.PostAsJsonAsync($"/api/notebook-studio/packs/{pack.Id}/export", new
        {
            format = "pptx_local_proof"
        });
        pptxResponse.EnsureSuccessStatusCode();
        var pptx = await pptxResponse.Content.ReadFromJsonAsync<NotebookExportResultDto>();
        Assert.NotNull(pptx);
        Assert.Equal("pptx_local_proof", pptx!.Format);
        Assert.Equal("unsupported", pptx.Status);
        Assert.Equal("pptx_not_enabled", pptx.ExportReadiness);
        Assert.False(pptx.BinaryExportAvailable);
        Assert.False(pptx.PptxLocalProofAvailable);
        Assert.Contains("not enabled", pptx.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("download", pptx.Content, StringComparison.OrdinalIgnoreCase);

        var deniedExport = await other.Client.PostAsJsonAsync($"/api/notebook-studio/packs/{pack.Id}/export", new
        {
            format = "markdown"
        });
        Assert.Equal(HttpStatusCode.NotFound, deniedExport.StatusCode);
    }

    [Fact]
    public async Task BuildPackArtifact_CreatesFlashcardSrsAndReviewBlueprintWithoutAnswerKey()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "notebook-review");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Algorithms Review");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var graph = new ConceptGraphSnapshot
            {
                Id = Guid.NewGuid(),
                UserId = user.UserId,
                TopicId = topicId,
                PlanRequestId = Guid.NewGuid(),
                IntentHash = "review-pack",
                TopicTitle = "Algorithms Review",
                ApprovedResearchIntent = "Review algorithms",
                Domain = "algorithms",
                SourceConfidence = "medium",
                SourceBundleHash = "bundle",
                GraphJson = "{}",
                CreatedAt = DateTime.UtcNow
            };
            db.ConceptGraphSnapshots.Add(graph);
            db.LearningConcepts.AddRange(
                new LearningConcept { Id = Guid.NewGuid(), ConceptGraphSnapshotId = graph.Id, StableKey = "big-o", Label = "Big-O", Order = 0 },
                new LearningConcept { Id = Guid.NewGuid(), ConceptGraphSnapshotId = graph.Id, StableKey = "binary-search", Label = "Binary Search", Order = 1 });
            db.ConceptMasteries.Add(new ConceptMastery
            {
                Id = Guid.NewGuid(),
                UserId = user.UserId,
                TopicId = topicId,
                ConceptKey = "binary-search",
                Label = "Binary Search",
                MasteryScore = 0.25m,
                Confidence = 0.55m,
                RemediationNeed = "high"
            });
            await db.SaveChangesAsync();
        }

        var created = await user.Client.PostAsJsonAsync($"/api/notebook-studio/topic/{topicId}/milestone-pack", new { includeArtifacts = false });
        created.EnsureSuccessStatusCode();
        var pack = await created.Content.ReadFromJsonAsync<LearningNotebookPackDto>();
        Assert.NotNull(pack);

        var flashcardResponse = await user.Client.PostAsJsonAsync($"/api/notebook-studio/packs/{pack!.Id}/artifact", new { artifactType = "flashcard_set" });
        flashcardResponse.EnsureSuccessStatusCode();
        var flashcards = await flashcardResponse.Content.ReadFromJsonAsync<LearningArtifactDto>();
        Assert.Equal("flashcard_set", flashcards!.ArtifactType);
        Assert.Contains("SRS", flashcards.SafeContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawSourceChunk", flashcards.SafeContent, StringComparison.OrdinalIgnoreCase);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            Assert.True(await db.Flashcards.AnyAsync(f => f.UserId == user.UserId && f.TopicId == topicId && f.CreatedFrom == "notebook_studio"));
            Assert.True(await db.ReviewItems.AnyAsync(r => r.UserId == user.UserId && r.TopicId == topicId && r.SourceType == "flashcard"));
        }

        var reviewResponse = await user.Client.PostAsJsonAsync($"/api/notebook-studio/packs/{pack.Id}/artifact", new { artifactType = "review_quiz" });
        reviewResponse.EnsureSuccessStatusCode();
        var review = await reviewResponse.Content.ReadFromJsonAsync<LearningArtifactDto>();
        Assert.Equal("review_quiz", review!.ArtifactType);
        Assert.Contains("review_check", review.SafeContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correctAnswer", review.SafeContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", review.SafeContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("review_check", review.ContentJson!, StringComparison.OrdinalIgnoreCase);

        var slideResponse = await user.Client.PostAsJsonAsync($"/api/notebook-studio/packs/{pack.Id}/artifact", new { artifactType = "slide_deck_outline" });
        slideResponse.EnsureSuccessStatusCode();
        var slide = await slideResponse.Content.ReadFromJsonAsync<LearningArtifactDto>();
        Assert.Equal("slide_deck_outline", slide!.ArtifactType);
        Assert.Contains("slide_deck_outline_v1", slide.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("speakerNotes", slide.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("checkpointQuestion", slide.ContentJson!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("success guarantee", slide.SafeContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawSourceChunk", slide.ContentJson!, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<LearningArtifactDto> BuildArtifactAsync(CoordinationTestUser user, Guid packId, string artifactType)
    {
        var response = await user.Client.PostAsJsonAsync($"/api/notebook-studio/packs/{packId}/artifact", new { artifactType });
        response.EnsureSuccessStatusCode();
        var artifact = await response.Content.ReadFromJsonAsync<LearningArtifactDto>();
        Assert.NotNull(artifact);
        return artifact!;
    }

    private static async Task<Guid> SeedNotebookWikiPageAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;
        var pageId = Guid.NewGuid();
        db.WikiPages.Add(new WikiPage
        {
            Id = pageId,
            UserId = userId,
            TopicId = topicId,
            Title = "Big-O Wiki",
            PageKey = "big-o",
            PageType = "concept",
            ConceptKey = "big-o",
            SourceReadiness = "evidence_insufficient",
            EvidenceStatus = "evidence_insufficient",
            SafeSummary = "Big-O safe summary",
            Status = "ready",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.WikiBlocks.AddRange(
            new WikiBlock
            {
                Id = Guid.NewGuid(),
                WikiPageId = pageId,
                BlockType = WikiBlockType.StudentQuestion,
                Title = "Soru",
                Content = "Ogrenci sorusu: Big-O neden sadece en buyuk terime bakar?",
                Source = "student",
                SourceBasis = "model_assisted",
                ConceptKey = "big-o",
                OrderIndex = 1,
                CreatedAt = now,
                UpdatedAt = now
            },
            new WikiBlock
            {
                Id = Guid.NewGuid(),
                WikiPageId = pageId,
                BlockType = WikiBlockType.RepairNote,
                Title = "Pekistirme",
                Content = "n ve n^2 buyume farkini tabloyla karsilastir.",
                Source = "tutor",
                SourceBasis = "model_assisted",
                ConceptKey = "big-o",
                MisconceptionKey = "growth-rate-confusion",
                OrderIndex = 2,
                CreatedAt = now,
                UpdatedAt = now
            });
        var graphId = Guid.NewGuid();
        db.ConceptGraphSnapshots.Add(new ConceptGraphSnapshot
        {
            Id = graphId,
            UserId = userId,
            TopicId = topicId,
            PlanRequestId = Guid.NewGuid(),
            IntentHash = "wiki-page-big-o",
            TopicTitle = "Algorithms Wiki Page",
            ApprovedResearchIntent = "Review Big-O wiki page",
            Domain = "algorithms",
            SourceConfidence = "medium",
            SourceBundleHash = "wiki-page-bundle",
            GraphJson = "{}",
            CreatedAt = now
        });
        db.LearningConcepts.AddRange(
            new LearningConcept { Id = Guid.NewGuid(), ConceptGraphSnapshotId = graphId, StableKey = "big-o", Label = "Big-O", Order = 0 },
            new LearningConcept { Id = Guid.NewGuid(), ConceptGraphSnapshotId = graphId, StableKey = "growth-rate", Label = "Growth Rate", Order = 1 },
            new LearningConcept { Id = Guid.NewGuid(), ConceptGraphSnapshotId = graphId, StableKey = "complexity", Label = "Complexity", Order = 2 });
        db.ConceptRelations.Add(new ConceptRelation
        {
            Id = Guid.NewGuid(),
            ConceptGraphSnapshotId = graphId,
            SourceConceptKey = "big-o",
            TargetConceptKey = "growth-rate",
            RelationType = "explains",
            Weight = 0.9
        });
        await db.SaveChangesAsync();
        return pageId;
    }
}
