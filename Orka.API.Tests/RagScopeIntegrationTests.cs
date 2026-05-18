using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class RagScopeIntegrationTests
{
    [Fact]
    public async Task TopicRetrieval_UsesTreeScopeAndKeepsCurrentTopicRankingBoost()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "rag-a");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "rag-b");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Rag");
        var foreignChild = await CoordinationTestHelpers.SeedTopicAsync(factory, other.UserId, "Foreign Rag Child", tree.RootId);

        var rootSourceId = await CoordinationTestHelpers.SeedSourceAsync(
            factory,
            user.UserId,
            tree.RootId,
            "Root Source",
            "shared priority concept from coordinated source scope");
        var lessonSourceId = await CoordinationTestHelpers.SeedSourceAsync(
            factory,
            user.UserId,
            tree.LessonId,
            "Lesson Source",
            "shared priority concept from coordinated source scope");
        var foreignSourceId = await CoordinationTestHelpers.SeedSourceAsync(
            factory,
            other.UserId,
            foreignChild,
            "Foreign Source",
            "shared priority concept from coordinated source scope");

        using var scope = factory.Services.CreateScope();
        var sources = scope.ServiceProvider.GetRequiredService<ILearningSourceService>();

        var lessonEvidence = await sources.RetrieveTopicEvidenceAsync(
            user.UserId,
            tree.LessonId,
            "shared priority concept",
            take: 5);
        Assert.Contains(lessonEvidence, e => e.SourceId == rootSourceId);
        Assert.Contains(lessonEvidence, e => e.SourceId == lessonSourceId);
        Assert.DoesNotContain(lessonEvidence, e => e.SourceId == foreignSourceId);
        Assert.Equal(lessonSourceId, lessonEvidence[0].SourceId);
        Assert.Contains(lessonEvidence, e =>
            e.SourceId == rootSourceId &&
            e.SourceTopicId == tree.RootId &&
            e.ScopeRelation == "ancestor" &&
            e.RetrievalScope == "wiki_topic_tree" &&
            e.CitationId == $"[doc:{rootSourceId}:p1]");
        Assert.Contains(lessonEvidence, e =>
            e.SourceId == lessonSourceId &&
            e.SourceTopicId == tree.LessonId &&
            e.ScopeRelation == "current" &&
            !string.IsNullOrWhiteSpace(e.SourceTopicTitle));

        var rootEvidence = await sources.RetrieveTopicEvidenceAsync(
            user.UserId,
            tree.RootId,
            "shared priority concept",
            take: 5);
        Assert.Contains(rootEvidence, e => e.SourceId == lessonSourceId);
        Assert.DoesNotContain(rootEvidence, e => e.SourceId == foreignSourceId);
        Assert.Contains(rootEvidence, e =>
            e.SourceId == lessonSourceId &&
            e.SourceTopicId == tree.LessonId &&
            e.ScopeRelation == "descendant" &&
            e.RetrievalScope == "wiki_topic_tree" &&
            e.CitationId == $"[doc:{lessonSourceId}:p1]");

        var moduleEvidence = await sources.RetrieveTopicEvidenceAsync(
            user.UserId,
            tree.ModuleId,
            "shared priority concept",
            take: 5);
        Assert.Contains(moduleEvidence, e =>
            e.SourceId == rootSourceId &&
            e.ScopeRelation == "ancestor");
        Assert.Contains(moduleEvidence, e =>
            e.SourceId == lessonSourceId &&
            e.ScopeRelation == "descendant");
        Assert.DoesNotContain(moduleEvidence, e => e.SourceId == foreignSourceId);

        var wikiEvidence = scope.ServiceProvider.GetRequiredService<IWikiEvidenceService>();
        var lessonBundle = await wikiEvidence.BuildAsync(new WikiLearningRequestDto
        {
            UserId = user.UserId,
            TopicId = tree.LessonId,
            Question = "shared priority concept"
        });
        Assert.Contains(lessonBundle.Citations, c =>
            c.SourceId == rootSourceId &&
            c.SourceTopicId == tree.RootId &&
            c.ScopeRelation == "ancestor" &&
            c.RetrievalScope == "wiki_topic_tree" &&
            c.CitationId == $"[doc:{rootSourceId}:p1]");
    }

    [Fact]
    public async Task ExactSourceAndPageBehavior_RemainsSourceBoundAndUserSafe()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "rag-exact-a");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "rag-exact-b");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "RagExact");
        var rootSourceId = await CoordinationTestHelpers.SeedSourceAsync(
            factory,
            user.UserId,
            tree.RootId,
            "Exact Root Source",
            "exact root source text");
        await CoordinationTestHelpers.SeedSourceAsync(
            factory,
            user.UserId,
            tree.LessonId,
            "Exact Lesson Source",
            "exact lesson source text");

        using var scope = factory.Services.CreateScope();
        var sources = scope.ServiceProvider.GetRequiredService<ILearningSourceService>();

        var exactEvidence = await sources.RetrieveTopicEvidenceAsync(
            user.UserId,
            tree.LessonId,
            "exact source text",
            take: 5,
            sourceId: rootSourceId);
        Assert.NotEmpty(exactEvidence);
        Assert.All(exactEvidence, e => Assert.Equal(rootSourceId, e.SourceId));
        Assert.All(exactEvidence, e => Assert.Equal("direct-source", e.ScopeRelation));
        Assert.All(exactEvidence, e => Assert.Equal("source_direct", e.RetrievalScope));

        var page = await sources.GetPageAsync(user.UserId, rootSourceId, 1);
        Assert.NotNull(page);
        Assert.Single(page!.Chunks);
        Assert.Equal(string.Empty, page.Chunks[0].Text);
        Assert.Null(page.Chunks[0].HighlightHint);

        var crossUserPage = await sources.GetPageAsync(other.UserId, rootSourceId, 1);
        Assert.Null(crossUserPage);
    }

    [Fact]
    public async Task WikiEvidence_DoesNotRequireExactTopicSourceBeforeScopedRetrieval()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "rag-wiki-a");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "RagWiki");
        var lessonSourceId = await CoordinationTestHelpers.SeedSourceAsync(
            factory,
            user.UserId,
            tree.LessonId,
            "Only Lesson Source",
            "scoped descendant evidence for root workspace");

        using var scope = factory.Services.CreateScope();
        var wikiEvidence = scope.ServiceProvider.GetRequiredService<IWikiEvidenceService>();

        var rootBundle = await wikiEvidence.BuildAsync(new WikiLearningRequestDto
        {
            UserId = user.UserId,
            TopicId = tree.RootId,
            Question = "scoped descendant evidence"
        });

        Assert.Equal(0, rootBundle.ReadySourceCount);
        Assert.Contains(rootBundle.SourceChunks, c =>
            c.SourceId == lessonSourceId &&
            c.SourceTopicId == tree.LessonId &&
            c.ScopeRelation == "descendant" &&
            c.RetrievalScope == "wiki_topic_tree");
        Assert.Contains(rootBundle.Citations, c =>
            c.SourceId == lessonSourceId &&
            c.ScopeRelation == "descendant" &&
            c.RetrievalScope == "wiki_topic_tree" &&
            c.CitationId == $"[doc:{lessonSourceId}:p1]");
        Assert.Equal("healthy", rootBundle.RetrievalHealth);
    }

    [Fact]
    public async Task TopicRetrieval_IsDeterministicAndDoesNotLetDescendantsEraseCurrentTopic()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "rag-quota");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "RagQuota");
        var currentSourceId = await CoordinationTestHelpers.SeedSourceAsync(
            factory,
            user.UserId,
            tree.RootId,
            "Current Source",
            "quota needle");

        for (var i = 0; i < 8; i++)
        {
            await CoordinationTestHelpers.SeedSourceAsync(
                factory,
                user.UserId,
                tree.LessonId,
                $"Descendant Source {i}",
                "quota needle quota needle quota needle");
        }

        using var scope = factory.Services.CreateScope();
        var sources = scope.ServiceProvider.GetRequiredService<ILearningSourceService>();

        var first = await sources.RetrieveTopicEvidenceAsync(user.UserId, tree.RootId, "quota needle", take: 3);
        var second = await sources.RetrieveTopicEvidenceAsync(user.UserId, tree.RootId, "quota needle", take: 3);

        Assert.Contains(first, e => e.SourceId == currentSourceId && e.ScopeRelation == "current");
        Assert.Equal(
            first.Select(e => e.CitationId).ToArray(),
            second.Select(e => e.CitationId).ToArray());
        Assert.All(first, e => Assert.StartsWith($"[doc:{e.SourceId}:p", e.CitationId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TopicRetrieval_RanksCurrentBeforeAncestorWhenScoresTie()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "rag-rank");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "RagRank");
        var ancestorSourceId = await CoordinationTestHelpers.SeedSourceAsync(
            factory,
            user.UserId,
            tree.RootId,
            "Ancestor Source",
            "same tie evidence");
        var currentSourceId = await CoordinationTestHelpers.SeedSourceAsync(
            factory,
            user.UserId,
            tree.LessonId,
            "Current Source",
            "same tie evidence");

        using var scope = factory.Services.CreateScope();
        var sources = scope.ServiceProvider.GetRequiredService<ILearningSourceService>();

        var evidence = await sources.RetrieveTopicEvidenceAsync(user.UserId, tree.LessonId, "same tie evidence", take: 2);

        Assert.Equal(currentSourceId, evidence[0].SourceId);
        Assert.Equal("current", evidence[0].ScopeRelation);
        Assert.Contains(evidence, e => e.SourceId == ancestorSourceId && e.ScopeRelation == "ancestor");
    }

    [Fact]
    public async Task SourceQualityEvidenceQuality_ProducesDeterministicTrustLabels()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "rag-quality");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "RagQuality");

        using var scope = factory.Services.CreateScope();
        var sources = scope.ServiceProvider.GetRequiredService<ILearningSourceService>();

        var missing = await sources.GetTopicQualityAsync(user.UserId, tree.RootId);
        Assert.Equal("missing", missing.EvidenceQuality?.Status);

        await CoordinationTestHelpers.SeedSourceAsync(factory, user.UserId, tree.RootId, "Strong Source", "strong evidence");
        await SeedQualityTraceAsync(factory, user.UserId, tree.RootId, "healthy", "supported");
        var strong = await sources.GetTopicQualityAsync(user.UserId, tree.RootId);
        Assert.Equal("strong", strong.EvidenceQuality?.Status);
        Assert.Contains("healthy_retrieval_and_citations", strong.EvidenceQuality!.Reasons);

        var partialTopic = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Partial Quality", tree.RootId);
        await CoordinationTestHelpers.SeedSourceAsync(factory, user.UserId, partialTopic, "Partial Source", "partial evidence");
        await SeedQualityTraceAsync(factory, user.UserId, partialTopic, "healthy", null);
        var partial = await sources.GetTopicQualityAsync(user.UserId, partialTopic);
        Assert.Equal("partial", partial.EvidenceQuality?.Status);

        var weakTopic = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Weak Quality", tree.RootId);
        await CoordinationTestHelpers.SeedSourceAsync(factory, user.UserId, weakTopic, "Weak Source", "weak evidence");
        await SeedQualityTraceAsync(factory, user.UserId, weakTopic, "healthy", "citation_unsupported");
        var weak = await sources.GetTopicQualityAsync(user.UserId, weakTopic);
        Assert.Equal("weak", weak.EvidenceQuality?.Status);
        Assert.Contains("citation_unsupported", weak.EvidenceQuality!.Reasons);
    }

    [Fact]
    public async Task WikiEvidence_CarriesEvidenceQualityForScopedSources()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "rag-wiki-quality");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "RagWikiQuality");
        await CoordinationTestHelpers.SeedSourceAsync(
            factory,
            user.UserId,
            tree.LessonId,
            "Scoped Source",
            "wiki quality scoped evidence");

        using var scope = factory.Services.CreateScope();
        var wikiEvidence = scope.ServiceProvider.GetRequiredService<IWikiEvidenceService>();

        var rootBundle = await wikiEvidence.BuildAsync(new WikiLearningRequestDto
        {
            UserId = user.UserId,
            TopicId = tree.RootId,
            Question = "wiki quality scoped evidence"
        });

        Assert.NotNull(rootBundle.EvidenceQuality);
        Assert.NotEqual("missing", rootBundle.EvidenceQuality!.Status);
        Assert.True(rootBundle.EvidenceQuality.RetrievedEvidenceCount > 0);
    }

    private static async Task SeedQualityTraceAsync(
        ApiSmokeFactory factory,
        Guid userId,
        Guid topicId,
        string retrievalStatus,
        string? citationStatus)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var source = await db.LearningSources.FirstAsync(s => s.UserId == userId && s.TopicId == topicId);
        var chunk = await db.SourceChunks.FirstAsync(c => c.LearningSourceId == source.Id);
        var now = DateTime.UtcNow;
        var runId = Guid.NewGuid();
        db.SourceRetrievalRuns.Add(new SourceRetrievalRun
        {
            Id = runId,
            UserId = userId,
            TopicId = topicId,
            SourceId = source.Id,
            Query = "quality trace",
            RetrievalScope = "wiki_topic_tree",
            RequestedTopK = 3,
            RetrievedCount = 1,
            IsEmpty = false,
            MaxScore = retrievalStatus == "low_confidence" ? 0.2m : 0.9m,
            AverageScore = retrievalStatus == "low_confidence" ? 0.2m : 0.9m,
            QualityStatus = retrievalStatus,
            CreatedAt = now,
            CompletedAt = now
        });
        if (!string.IsNullOrWhiteSpace(citationStatus))
        {
            db.SourceCitationChecks.Add(new SourceCitationCheck
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                SourceRetrievalRunId = runId,
                SourceId = source.Id,
                SourceChunkId = chunk.Id,
                CitationId = $"[doc:{source.Id}:p1]",
                SourceType = "document",
                PageNumber = 1,
                ChunkIndex = 0,
                CheckStatus = citationStatus,
                Confidence = citationStatus == "supported" ? 0.9m : 0m,
                Reason = citationStatus,
                CreatedAt = now
            });
        }
        await db.SaveChangesAsync();
    }
}
