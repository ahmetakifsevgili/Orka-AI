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

public sealed class SourceEvidenceLifecycleTests
{
    [Fact]
    public async Task ReadySourceBuildsSafeEvidenceBundleWithoutRawPayload()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-life-ready");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Cell Division");
        var longTail = new string('z', 420);
        await CoordinationTestHelpers.SeedSourceAsync(
            factory,
            user.UserId,
            topicId,
            "Cell notes",
            $"cell division mitosis evidence {longTail}");

        using var scope = factory.Services.CreateScope();
        var lifecycle = scope.ServiceProvider.GetRequiredService<ISourceEvidenceLifecycleService>();
        var bundle = await lifecycle.BuildSourceEvidenceBundleAsync(user.UserId, topicId, question: "cell division mitosis");

        Assert.Equal("source_grounded", bundle.EvidenceStatus);
        Assert.Equal(1, bundle.ReadySourceCount);
        Assert.NotEmpty(bundle.EvidenceItems);
        Assert.All(bundle.EvidenceItems, item =>
        {
            Assert.Equal("document", item.SourceType);
            Assert.NotEqual(longTail, item.SnippetSummary);
            Assert.DoesNotContain(longTail, item.SnippetSummary);
            Assert.Null(item.Url);
        });
    }

    [Fact]
    public async Task StaleSourceStopsSourceGroundingAndMarksSnapshotsStale()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-life-stale");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Evidence Topic");
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(
            factory,
            user.UserId,
            topicId,
            "Evidence Source",
            "source lifecycle evidence needle");

        using var scope = factory.Services.CreateScope();
        var lifecycle = scope.ServiceProvider.GetRequiredService<ISourceEvidenceLifecycleService>();
        var snapshots = scope.ServiceProvider.GetRequiredService<IActiveLessonSnapshotService>();

        await lifecycle.BuildSourceEvidenceBundleAsync(user.UserId, topicId, question: "lifecycle evidence needle");
        var active = await snapshots.BuildOrRefreshActiveLessonSnapshotAsync(user.UserId, new ActiveLessonSnapshotRequestDto { TopicId = topicId });
        Assert.True(active.EvidenceSummary.SourceEvidenceCount > 0);

        var marked = await lifecycle.MarkSourceStaleAsync(user.UserId, sourceId, "source changed");
        Assert.True(marked);

        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var staleSnapshotStatus = await db.ActiveLessonSnapshots
            .Where(s => s.Id == active.Id)
            .Select(s => s.Status)
            .SingleAsync();
        Assert.Equal("stale", staleSnapshotStatus);

        var refreshed = await lifecycle.BuildSourceEvidenceBundleAsync(user.UserId, topicId, question: "lifecycle evidence needle");
        Assert.Equal("stale", refreshed.EvidenceStatus);
        Assert.Equal(0, refreshed.ReadySourceCount);
        Assert.Empty(refreshed.EvidenceItems);
    }

    [Fact]
    public async Task CitationValidationIsUserScopedAndRejectsInactiveSource()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-life-cite-a");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-life-cite-b");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Citations");
        var otherTopicId = await CoordinationTestHelpers.SeedTopicAsync(factory, other.UserId, "Foreign Citations");
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(factory, user.UserId, topicId, "Citation Source", "citation evidence active");
        var otherSourceId = await CoordinationTestHelpers.SeedSourceAsync(factory, other.UserId, otherTopicId, "Other Source", "private citation evidence");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var chunk = await db.SourceChunks.FirstAsync(c => c.LearningSourceId == sourceId);
        var otherChunk = await db.SourceChunks.FirstAsync(c => c.LearningSourceId == otherSourceId);
        var lifecycle = scope.ServiceProvider.GetRequiredService<ISourceEvidenceLifecycleService>();

        var active = await lifecycle.ValidateCitationSetAsync(user.UserId, new ValidateSourceCitationSetRequestDto
        {
            TopicId = topicId,
            Citations = [new ValidateSourceCitationDto { SourceId = sourceId, ChunkId = chunk.Id, CitationId = $"[doc:{sourceId}:p1]" }]
        });
        Assert.Equal(1, active.SupportedCount);
        Assert.True(active.Results[0].Supported);

        await lifecycle.MarkSourceStaleAsync(user.UserId, sourceId, "old source");
        var stale = await lifecycle.ValidateCitationSetAsync(user.UserId, new ValidateSourceCitationSetRequestDto
        {
            TopicId = topicId,
            Citations = [new ValidateSourceCitationDto { SourceId = sourceId, ChunkId = chunk.Id, CitationId = $"[doc:{sourceId}:p1]" }]
        });
        Assert.Equal(0, stale.SupportedCount);
        Assert.Equal("stale", stale.Results[0].Status);

        var crossUser = await lifecycle.ValidateCitationSetAsync(user.UserId, new ValidateSourceCitationSetRequestDto
        {
            Citations = [new ValidateSourceCitationDto { SourceId = otherSourceId, ChunkId = otherChunk.Id, CitationId = $"[doc:{otherSourceId}:p1]" }]
        });
        Assert.Equal(0, crossUser.SupportedCount);
        Assert.Equal("citation_not_found", crossUser.Results[0].Status);
    }

    [Fact]
    public async Task KnowledgeNotebookGroupsSourceWikiAndKorteksSeedWithoutAutoGeneratingNotes()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-life-wiki");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Notebook Topic");
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(factory, user.UserId, topicId, "Notebook Source", "notebook source concept evidence");
        var pageId = await CoordinationTestHelpers.SeedWikiPageAsync(factory, user.UserId, topicId, "Notebook Wiki", "notebook source concept wiki block");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        db.KorteksResearchWorkflows.Add(new KorteksResearchWorkflow
        {
            Id = Guid.NewGuid(),
            UserId = user.UserId,
            TopicId = topicId,
            Topic = "Notebook Topic",
            Status = "ready",
            GroundingMode = "ExternalResearch",
            SourceCount = 2,
            SynthesisJson = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var lifecycle = scope.ServiceProvider.GetRequiredService<ISourceEvidenceLifecycleService>();
        await lifecycle.BuildSourceEvidenceBundleAsync(user.UserId, topicId, question: "notebook source concept");
        var notebook = await lifecycle.BuildWikiKnowledgeNotebookAsync(user.UserId, topicId);

        Assert.Contains(notebook.Sections, s => s.SectionKey == "source-evidence" && s.SourceIds.Contains(sourceId));
        Assert.Contains(notebook.Sections, s => s.SectionKey == "wiki-notes" && s.WikiBlockIds.Count > 0);
        Assert.Contains(notebook.Sections, s => s.SectionKey == "korteks-external-seed" && s.Status == "external_research_seed");
        Assert.Contains(notebook.SourceWarnings, w => w.Contains("external research seed", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, await db.WikiPages.CountAsync(p => p.Id == pageId));
    }

    [Fact]
    public async Task EndpointsAreAuthenticatedAndCallerScoped()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-life-api-a");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-life-api-b");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "API Topic");
        var otherTopicId = await CoordinationTestHelpers.SeedTopicAsync(factory, other.UserId, "Other API Topic");
        await CoordinationTestHelpers.SeedSourceAsync(factory, user.UserId, topicId, "API Source", "api source evidence");

        var ok = await user.Client.PostAsJsonAsync($"/api/sources/topic/{topicId}/evidence-bundle/refresh", new { question = "api source" });
        ok.EnsureSuccessStatusCode();
        var bundle = await ok.Content.ReadFromJsonAsync<SourceEvidenceBundleDto>();
        Assert.NotNull(bundle);
        Assert.DoesNotContain(bundle!.EvidenceItems, i => i.SnippetSummary.Contains("raw provider", StringComparison.OrdinalIgnoreCase));

        var forbidden = await other.Client.GetAsync($"/api/sources/topic/{topicId}/evidence-bundle");
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);

        var notebook = await other.Client.GetAsync($"/api/wiki/{otherTopicId}/knowledge-notebook");
        Assert.True(notebook.IsSuccessStatusCode);
    }

    [Fact]
    public async Task AskSourceReturnsSafeDtoAndWritesWikiTraceWithoutRawChunks()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-ask-safe-a");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-ask-safe-b");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Ask Source Topic");
        var sourceRawText = "private rawSourceChunk lesson evidence with C:\\secret\\source.txt";
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(factory, user.UserId, topicId, "Ask Source", sourceRawText);
        var pageId = await CoordinationTestHelpers.SeedWikiPageAsync(factory, user.UserId, topicId, "Ask Wiki", "safe wiki context");

        var crossUser = await other.Client.PostAsJsonAsync($"/api/sources/{sourceId}/ask", new SourceQuestionRequestDto
        {
            Question = "What does this source say?",
            WikiPageId = pageId,
            WriteWikiTrace = true
        });
        Assert.Equal(HttpStatusCode.NotFound, crossUser.StatusCode);

        var response = await user.Client.PostAsJsonAsync($"/api/sources/{sourceId}/ask", new SourceQuestionRequestDto
        {
            TopicId = topicId,
            WikiPageId = pageId,
            Question = "What does this source say?",
            Mode = "selected_source",
            IncludeLearnerContext = true,
            WriteWikiTrace = true
        });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<SourceQuestionResponseDto>();
        Assert.NotNull(body);
        Assert.NotEqual("source_grounded", body!.SourceBasis);
        Assert.NotEmpty(body.Citations);
        Assert.All(body.Citations, citation =>
        {
            Assert.DoesNotContain("rawSourceChunk", citation.Label, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", citation.Label, StringComparison.OrdinalIgnoreCase);
            Assert.True(citation.SourceChunkId.HasValue);
        });

        var json = JsonSerializer.Serialize(body);
        Assert.DoesNotContain(sourceRawText, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawSourceChunk", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(user.UserId.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.True(body.Safety.RawPayloadRemoved);

        var sourcePage = await user.Client.GetAsync($"/api/sources/{sourceId}/pages/1");
        sourcePage.EnsureSuccessStatusCode();
        var sourcePageJson = await sourcePage.Content.ReadAsStringAsync();
        Assert.DoesNotContain(sourceRawText, sourcePageJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawSourceChunk", sourcePageJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\secret", sourcePageJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(user.UserId.ToString(), sourcePageJson, StringComparison.OrdinalIgnoreCase);

        var quality = await user.Client.GetAsync($"/api/sources/topic/{topicId}/quality");
        quality.EnsureSuccessStatusCode();
        var qualityJson = await quality.Content.ReadAsStringAsync();
        Assert.DoesNotContain(user.UserId.ToString(), qualityJson, StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var blocks = await db.WikiBlocks
            .Where(b => b.WikiPageId == pageId && !b.IsDeleted)
            .ToListAsync();
        Assert.Contains(blocks, b => b.BlockType == WikiBlockType.StudentQuestion && b.SourceBasis == "student_manual");
        Assert.Contains(blocks, b =>
            b.BlockType == WikiBlockType.TutorExplanation &&
            b.SourceBasis != "source_grounded" &&
            !b.Content.Contains("rawSourceChunk", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AskTopicSourcesUsesOwnedCollectionAndDegradesWhenEvidenceIsMissing()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-ask-topic-a");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-ask-topic-b");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Ask Collection Topic");
        var otherTopicId = await CoordinationTestHelpers.SeedTopicAsync(factory, other.UserId, "Other Collection Topic");
        await CoordinationTestHelpers.SeedSourceAsync(factory, user.UserId, topicId, "Collection Source", "collection source evidence");

        var ok = await user.Client.PostAsJsonAsync($"/api/sources/topic/{topicId}/ask", new SourceQuestionRequestDto
        {
            TopicId = topicId,
            Question = "Ask the collection",
            Mode = "source_collection"
        });
        ok.EnsureSuccessStatusCode();
        var body = await ok.Content.ReadFromJsonAsync<SourceQuestionResponseDto>();
        Assert.NotNull(body);
        Assert.NotEqual("source_grounded", body!.SourceBasis);
        Assert.True(body.Context.SourceId.HasValue);
        Assert.True(body.Safety.RawPayloadRemoved);

        var forbidden = await other.Client.PostAsJsonAsync($"/api/sources/topic/{topicId}/ask", new SourceQuestionRequestDto
        {
            Question = "Can I ask another user's topic?"
        });
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);

        var empty = await other.Client.PostAsJsonAsync($"/api/sources/topic/{otherTopicId}/ask", new SourceQuestionRequestDto
        {
            Question = "Any sources?"
        });
        empty.EnsureSuccessStatusCode();
        var emptyBody = await empty.Content.ReadFromJsonAsync<SourceQuestionResponseDto>();
        Assert.NotNull(emptyBody);
        Assert.Equal("evidence_insufficient", emptyBody!.SourceBasis);
        Assert.Contains("no_source_available", emptyBody.Warnings);
    }

    [Fact]
    public async Task MultiSourceCompareIsScopedDeterministicAndDoesNotExposeRawCitationInternals()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-compare-a");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-compare-b");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Compare Topic");
        var sourceA = await CoordinationTestHelpers.SeedSourceAsync(factory, user.UserId, topicId, "Mitosis Notes A", "mitosis spindle checkpoint evidence rawSourceChunk C:\\secret\\a.txt");
        var sourceB = await CoordinationTestHelpers.SeedSourceAsync(factory, user.UserId, topicId, "Mitosis Notes B", "mitosis phase coverage evidence hiddenPrompt C:\\secret\\b.txt");
        var conceptPageId = await CoordinationTestHelpers.SeedWikiPageAsync(factory, user.UserId, topicId, "Mitosis", "safe concept page");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var chunkA = await db.SourceChunks.FirstAsync(c => c.LearningSourceId == sourceA);
            var chunkB = await db.SourceChunks.FirstAsync(c => c.LearningSourceId == sourceB);
            db.SourceCitationChecks.AddRange(
                new SourceCitationCheck
                {
                    Id = Guid.NewGuid(),
                    UserId = user.UserId,
                    TopicId = topicId,
                    SourceId = sourceA,
                    SourceChunkId = chunkA.Id,
                    CitationId = $"[doc:{sourceA}:p1]",
                    SourceType = "document",
                    PageNumber = 1,
                    ChunkIndex = 0,
                    Answer = "rawSourceChunk provider payload C:\\secret\\answer.txt",
                    ClaimText = "hiddenPrompt rawProviderPayload",
                    CheckStatus = "supported",
                    Confidence = 0.93m,
                    Reason = "citation_matches_retrieved_chunk",
                    CreatedAt = DateTime.UtcNow
                },
                new SourceCitationCheck
                {
                    Id = Guid.NewGuid(),
                    UserId = user.UserId,
                    TopicId = topicId,
                    SourceId = sourceB,
                    SourceChunkId = chunkB.Id,
                    CitationId = string.Empty,
                    SourceType = "document",
                    PageNumber = 1,
                    ChunkIndex = 0,
                    Answer = "answerKey correctAnswer stackTrace",
                    ClaimText = "rawToolPayload",
                    CheckStatus = "citation_missing",
                    Confidence = 0m,
                    Reason = "answer_contains_no_document_citation",
                    CreatedAt = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }

        var crossUser = await other.Client.PostAsJsonAsync($"/api/sources/topic/{topicId}/compare", new MultiSourceCompareRequestDto
        {
            SourceIds = [sourceA, sourceB],
            IncludeCitationReview = true
        });
        Assert.Equal(HttpStatusCode.NotFound, crossUser.StatusCode);

        var response = await user.Client.PostAsJsonAsync($"/api/sources/topic/{topicId}/compare", new MultiSourceCompareRequestDto
        {
            SourceIds = [sourceA, sourceB],
            WikiPageId = conceptPageId,
            IncludeConceptLinks = true,
            IncludeCitationReview = true,
            WriteWikiTrace = true
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<MultiSourceCompareResultDto>();
        Assert.NotNull(body);
        Assert.Equal(2, body!.ComparedSourceCount);
        Assert.NotEmpty(body.SourceSummaries);
        Assert.NotEmpty(body.SharedConcepts);
        Assert.True(body.CitationCoverage.NeedsReviewCount > 0);
        Assert.Contains("semantic_agreement_not_claimed", body.Warnings);
        Assert.True(body.TraceBlockId.HasValue);

        var json = JsonSerializer.Serialize(body);
        Assert.DoesNotContain("rawSourceChunk", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hiddenPrompt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawProviderPayload", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawToolPayload", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correctAnswer", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(user.UserId.ToString(), json, StringComparison.OrdinalIgnoreCase);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var traceBlock = await verifyDb.WikiBlocks.FirstOrDefaultAsync(b => b.Id == body.TraceBlockId.Value);
        Assert.NotNull(traceBlock);
        Assert.DoesNotContain("rawSourceChunk", traceBlock!.Content, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual("source_grounded", traceBlock.SourceBasis);
    }

    [Fact]
    public async Task CitationReviewReturnsSafeStatusesWithoutRawAnswerOrClaimText()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-review-a");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-review-b");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Citation Review Topic");
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(factory, user.UserId, topicId, "Review Source", "citation review evidence");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var chunk = await db.SourceChunks.FirstAsync(c => c.LearningSourceId == sourceId);
            db.SourceCitationChecks.AddRange(
                new SourceCitationCheck
                {
                    Id = Guid.NewGuid(),
                    UserId = user.UserId,
                    TopicId = topicId,
                    SourceId = sourceId,
                    SourceChunkId = chunk.Id,
                    CitationId = $"[doc:{sourceId}:p1]",
                    SourceType = "document",
                    PageNumber = 1,
                    ChunkIndex = 0,
                    Answer = "rawPrompt localPath C:\\secret\\review.txt",
                    ClaimText = "systemPrompt debugTrace",
                    CheckStatus = "supported",
                    Confidence = 0.88m,
                    Reason = "citation_matches_retrieved_chunk",
                    CreatedAt = DateTime.UtcNow
                },
                new SourceCitationCheck
                {
                    Id = Guid.NewGuid(),
                    UserId = user.UserId,
                    TopicId = topicId,
                    SourceId = sourceId,
                    CitationId = "[doc:foreign:p9]",
                    SourceType = "document",
                    PageNumber = 9,
                    Answer = "secret apiKey",
                    ClaimText = "stackTrace",
                    CheckStatus = "citation_unsupported",
                    Confidence = 0m,
                    Reason = "citation_not_in_retrieved_evidence",
                    CreatedAt = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }

        var forbidden = await other.Client.GetAsync($"/api/sources/{sourceId}/citation-review");
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);

        var response = await user.Client.GetAsync($"/api/sources/{sourceId}/citation-review");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CitationReviewResultDto>();
        Assert.NotNull(body);
        Assert.Equal(2, body!.Coverage.TotalCitationChecks);
        Assert.Contains(body.Items, i => i.CitationStatus == "supported");
        Assert.Contains(body.Items, i => i.CitationStatus == "unsupported");

        var json = JsonSerializer.Serialize(body);
        Assert.DoesNotContain("rawPrompt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("systemPrompt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("debugTrace", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(user.UserId.ToString(), json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SourceQuestionThreadsPersistSafeMemoryReviewTraceAndPackSummary()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-thread-a");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "source-thread-b");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Source Thread Topic");
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(factory, user.UserId, topicId, "Thread Source", "thread source evidence about mitosis");
        var pageId = await CoordinationTestHelpers.SeedWikiPageAsync(factory, user.UserId, topicId, "Thread Wiki", "safe thread wiki context");

        var forbidden = await other.Client.PostAsJsonAsync("/api/sources/question-threads", new SourceQuestionThreadRequestDto
        {
            TopicId = topicId,
            SourceId = sourceId,
            InitialQuestion = "Can I read another user's source?"
        });
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);

        var create = await user.Client.PostAsJsonAsync("/api/sources/question-threads", new SourceQuestionThreadRequestDto
        {
            TopicId = topicId,
            SourceId = sourceId,
            SourceIds = [sourceId],
            WikiPageId = pageId,
            InitialQuestion = "Explain the source without rawPrompt or C:\\secret\\thread.txt",
            IncludeLearnerContext = true,
            WriteWikiTrace = true
        });
        create.EnsureSuccessStatusCode();
        var thread = await create.Content.ReadFromJsonAsync<SourceQuestionThreadDto>();
        Assert.NotNull(thread);
        Assert.Single(thread!.Turns);
        Assert.True(thread.Turns[0].TraceBlockId.HasValue);

        var followUp = await user.Client.PostAsJsonAsync($"/api/sources/question-threads/{thread.ThreadId}/ask", new SourceQuestionFollowUpRequestDto
        {
            Question = "Continue from the prior answer but do not reveal rawSourceChunk or answerKey.",
            IncludeLearnerContext = true,
            WriteWikiTrace = false
        });
        followUp.EnsureSuccessStatusCode();
        var updated = await followUp.Content.ReadFromJsonAsync<SourceQuestionThreadDto>();
        Assert.NotNull(updated);
        Assert.Equal(2, updated!.Turns.Count);

        var review = await user.Client.PatchAsJsonAsync($"/api/sources/question-threads/{thread.ThreadId}/review", new SourceQuestionReviewStateDto
        {
            ReviewStatus = "supported",
            Warnings = ["student_checked_summary"]
        });
        review.EnsureSuccessStatusCode();
        var reviewed = await review.Content.ReadFromJsonAsync<SourceQuestionThreadDto>();
        Assert.NotNull(reviewed);
        Assert.NotEqual("supported", reviewed!.CitationReviewStatus);
        Assert.Contains(reviewed.Warnings, w => w.Contains("supported_review_requires", StringComparison.OrdinalIgnoreCase));

        var crossRead = await other.Client.GetAsync($"/api/sources/question-threads/{thread.ThreadId}");
        Assert.Equal(HttpStatusCode.NotFound, crossRead.StatusCode);

        var list = await user.Client.GetFromJsonAsync<SourceQuestionThreadListDto>($"/api/sources/question-threads?topicId={topicId}&sourceId={sourceId}");
        Assert.NotNull(list);
        Assert.True(list!.Count >= 1);

        var forbiddenSummary = await other.Client.GetAsync($"/api/sources/study-summary?topicId={topicId}&sourceId={sourceId}");
        Assert.Equal(HttpStatusCode.NotFound, forbiddenSummary.StatusCode);

        var summary = await user.Client.GetFromJsonAsync<SourceStudySummaryDto>($"/api/sources/study-summary?topicId={topicId}&sourceId={sourceId}&wikiPageId={pageId}");
        Assert.NotNull(summary);
        Assert.True(summary!.ThreadCount >= 1);
        Assert.True(summary.TurnCount >= 2);
        Assert.True(summary.NeedsReviewCount >= 1);
        Assert.True(summary.CitationWarningCount >= 1);
        Assert.Contains("review_citations", summary.NextActions);
        Assert.DoesNotContain(user.UserId.ToString(), JsonSerializer.Serialize(summary), StringComparison.OrdinalIgnoreCase);

        var json = JsonSerializer.Serialize(reviewed);
        Assert.DoesNotContain("rawPrompt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawSourceChunk", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(user.UserId.ToString(), json, StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var artifact = await db.LearningArtifacts.FirstAsync(a => a.Id == thread.ThreadId);
        Assert.Equal("source_question_thread", artifact.ArtifactType);
        Assert.DoesNotContain("rawPrompt", artifact.ContentJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawSourceChunk", artifact.ContentJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\secret", artifact.ContentJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var blocks = await db.WikiBlocks.Where(b => b.WikiPageId == pageId && !b.IsDeleted).ToListAsync();
        Assert.Contains(blocks, b => b.BlockType == WikiBlockType.StudentQuestion);
        Assert.DoesNotContain(blocks, b => b.Content.Contains("rawPrompt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(blocks, b => b.Content.Contains("C:\\secret", StringComparison.OrdinalIgnoreCase));

        var notebook = scope.ServiceProvider.GetRequiredService<ILearningNotebookStudioService>();
        var pack = await notebook.BuildSourcePackAsync(user.UserId, sourceId, new LearningNotebookPackRequestDto
        {
            SourceId = sourceId,
            PackType = "source_notebook",
            IncludeArtifacts = false
        });
        Assert.NotNull(pack);
        Assert.Contains("Source Q&A memory", pack!.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(pack.Warnings, w => w == "source_question_review_needed");
    }
}
