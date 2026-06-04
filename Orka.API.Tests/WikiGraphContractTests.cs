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

public sealed class WikiGraphContractTests
{
    [Fact]
    public async Task SyncWikiGraph_FromConceptGraphCreatesConceptPagesAndLearningLinks()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-sync-graph");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Algorithms Sync");
        var graphId = await SeedConceptGraphAsync(factory, user.UserId, tree.RootId);

        var response = await user.Client.PostAsJsonAsync($"/api/wiki/{tree.RootId}/sync-graph", new WikiGraphSyncRequestDto
        {
            ConceptGraphSnapshotId = graphId
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = json.RootElement;
        Assert.Equal(graphId, root.GetProperty("conceptGraphSnapshotId").GetGuid());
        Assert.Equal("source_grounded", root.GetProperty("sourceReadiness").GetString());
        Assert.True(root.GetProperty("createdPageCount").GetInt32() >= 3);

        var graph = root.GetProperty("graph");
        var pages = graph.GetProperty("pages").EnumerateArray().ToArray();
        Assert.Contains(pages, p =>
            p.GetProperty("pageType").GetString() == "topic_root" &&
            p.GetProperty("pageKey").GetString()!.StartsWith("topic:", StringComparison.Ordinal));
        Assert.Contains(pages, p =>
            p.GetProperty("pageType").GetString() == "concept" &&
            p.GetProperty("pageKey").GetString() == "concept:big-o" &&
            p.GetProperty("conceptKey").GetString() == "big-o" &&
            p.GetProperty("sourceReadiness").GetString() == "source_grounded");

        var links = graph.GetProperty("links").EnumerateArray().ToArray();
        Assert.Contains(links, l => l.GetProperty("linkType").GetString() == "parent_child");
        Assert.Contains(links, l => l.GetProperty("linkType").GetString() == "prerequisite");
        Assert.Contains(links, l => l.GetProperty("linkType").GetString() == "plan_sequence");

        var second = await user.Client.PostAsJsonAsync($"/api/wiki/{tree.RootId}/sync-graph", new WikiGraphSyncRequestDto
        {
            ConceptGraphSnapshotId = graphId
        });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var secondJson = await JsonDocument.ParseAsync(await second.Content.ReadAsStreamAsync());
        Assert.Equal(0, secondJson.RootElement.GetProperty("createdPageCount").GetInt32());
    }

    [Fact]
    public async Task SyncWikiGraph_FallsBackToTopicTreeWhenConceptGraphMissing()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-sync-tree");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Tree Wiki");

        var response = await user.Client.PostAsJsonAsync($"/api/wiki/{tree.RootId}/sync-graph", new WikiGraphSyncRequestDto());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = json.RootElement;
        Assert.Equal("evidence_insufficient", root.GetProperty("sourceReadiness").GetString());
        Assert.True(root.GetProperty("createdPageCount").GetInt32() >= 3);
        Assert.Contains(root.GetProperty("warnings").EnumerateArray(), w =>
            w.GetString()!.Contains("fallback", StringComparison.OrdinalIgnoreCase));

        var pages = root.GetProperty("graph").GetProperty("pages").EnumerateArray().ToArray();
        Assert.Contains(pages, p => p.GetProperty("title").GetString()!.Contains("Tree Wiki", StringComparison.Ordinal));
        Assert.Contains(pages, p => p.GetProperty("pageType").GetString() == "lesson");

        var links = root.GetProperty("graph").GetProperty("links").EnumerateArray().ToArray();
        Assert.Contains(links, l => l.GetProperty("linkType").GetString() == "parent_child");
    }

    [Fact]
    public async Task WikiGraph_ReturnsConceptPagesLinksAndSafeMetadata()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-graph-safe");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Algorithms Graph");

        var (rootPageId, childPageId) = await SeedGraphPagesAsync(factory, user.UserId, tree.RootId, tree.LessonId);

        var graphResponse = await user.Client.GetAsync($"/api/wiki/{tree.RootId}/graph");
        Assert.Equal(HttpStatusCode.OK, graphResponse.StatusCode);

        using var graphJson = await JsonDocument.ParseAsync(await graphResponse.Content.ReadAsStreamAsync());
        var root = graphJson.RootElement;
        Assert.Equal("ready", root.GetProperty("graphStatus").GetString());

        var pages = root.GetProperty("pages").EnumerateArray().ToArray();
        Assert.Contains(pages, p =>
            p.GetProperty("id").GetGuid() == rootPageId &&
            p.GetProperty("pageKey").GetString() == "algorithms" &&
            p.GetProperty("pageType").GetString() == "topic_root" &&
            p.GetProperty("conceptKey").GetString() == "algorithms" &&
            p.GetProperty("sourceReadiness").GetString() == "wiki_backed");
        Assert.Contains(pages, p =>
            p.GetProperty("id").GetGuid() == childPageId &&
            p.GetProperty("parentWikiPageId").GetGuid() == rootPageId &&
            p.GetProperty("parentConceptKey").GetString() == "algorithms");

        var links = root.GetProperty("links").EnumerateArray().ToArray();
        Assert.Contains(links, l =>
            l.GetProperty("sourcePageId").GetGuid() == rootPageId &&
            l.GetProperty("targetPageId").GetGuid() == childPageId &&
            l.GetProperty("linkType").GetString() == "parent_child");

        var raw = graphJson.RootElement.GetRawText();
        Assert.DoesNotContain("Hidden raw page content", raw);
        Assert.DoesNotContain("Raw tutor block content", raw);
    }

    [Fact]
    public async Task LocalWikiGraph_IsUserScopedAndRejectsForeignPages()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-graph-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-graph-other");
        var ownerTree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, owner.UserId, "Owner Wiki Graph");
        var otherTopicId = await CoordinationTestHelpers.SeedTopicAsync(factory, other.UserId, "Other Wiki Topic");

        var (ownerRootId, _) = await SeedGraphPagesAsync(factory, owner.UserId, ownerTree.RootId, ownerTree.LessonId);
        await CoordinationTestHelpers.SeedWikiPageAsync(factory, other.UserId, otherTopicId, "Other Page", "other content");

        var otherLocal = await other.Client.GetAsync($"/api/wiki/page/{ownerRootId}/graph");
        Assert.Equal(HttpStatusCode.NotFound, otherLocal.StatusCode);

        var ownerGraphAsOther = await other.Client.GetAsync($"/api/wiki/{ownerTree.RootId}/graph");
        Assert.Equal(HttpStatusCode.OK, ownerGraphAsOther.StatusCode);
        using var json = await JsonDocument.ParseAsync(await ownerGraphAsOther.Content.ReadAsStreamAsync());
        Assert.Equal("not_found", json.RootElement.GetProperty("graphStatus").GetString());
        Assert.Empty(json.RootElement.GetProperty("pages").EnumerateArray());
    }

    [Fact]
    public async Task CreateWikiLink_NormalizesValidLinksAndRejectsOtherUserTarget()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-link-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-link-other");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, owner.UserId, "Link Graph");
        var otherTopicId = await CoordinationTestHelpers.SeedTopicAsync(factory, other.UserId, "Foreign Link Topic");

        var (sourceId, targetId) = await SeedGraphPagesAsync(factory, owner.UserId, tree.RootId, tree.LessonId);
        var foreignPageId = await CoordinationTestHelpers.SeedWikiPageAsync(factory, other.UserId, otherTopicId, "Foreign", "foreign");

        var foreign = await owner.Client.PostAsJsonAsync("/api/wiki/links", new CreateWikiLinkRequestDto
        {
            SourcePageId = sourceId,
            TargetPageId = foreignPageId,
            LinkType = "parent_child",
            CreatedBy = "system",
            Strength = 0.8m
        });
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);

        var valid = await owner.Client.PostAsJsonAsync("/api/wiki/links", new CreateWikiLinkRequestDto
        {
            SourcePageId = sourceId,
            TargetPageId = targetId,
            LinkType = "weird_custom_type",
            CreatedBy = "student",
            Strength = 2m
        });
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);

        using var validJson = await JsonDocument.ParseAsync(await valid.Content.ReadAsStreamAsync());
        Assert.Equal("related", validJson.RootElement.GetProperty("linkType").GetString());
        Assert.Equal(1m, validJson.RootElement.GetProperty("strength").GetDecimal());

        var localGraph = await owner.Client.GetAsync($"/api/wiki/page/{sourceId}/graph");
        Assert.Equal(HttpStatusCode.OK, localGraph.StatusCode);
        using var localJson = await JsonDocument.ParseAsync(await localGraph.Content.ReadAsStreamAsync());
        Assert.Contains(localJson.RootElement.GetProperty("links").EnumerateArray(), l =>
            l.GetProperty("sourcePageId").GetGuid() == sourceId &&
            l.GetProperty("targetPageId").GetGuid() == targetId &&
            l.GetProperty("linkType").GetString() == "related");
    }

    [Fact]
    public async Task WikiPage_ReturnsBlockLearningMetadataOnlyAfterOwnershipCheck()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-block-meta");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Block Meta");

        var (_, childPageId) = await SeedGraphPagesAsync(factory, user.UserId, tree.RootId, tree.LessonId);

        var response = await user.Client.GetAsync($"/api/wiki/page/{childPageId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var blocks = body.RootElement.GetProperty("blocks").EnumerateArray().ToArray();
        Assert.Contains(blocks, b =>
            b.GetProperty("type").GetInt32() == (int)WikiBlockType.RepairNote &&
            b.GetProperty("sourceBasis").GetString() == "wiki_backed" &&
            b.GetProperty("conceptKey").GetString() == "big-o" &&
            b.GetProperty("misconceptionKey").GetString() == "big-o-linear-confusion" &&
            b.GetProperty("visibility").GetString() == "normal");
    }

    [Fact]
    public async Task WikiPage_ReportsTypedLearningSystemBindingSeparatelyFromContentReadiness()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-system-binding");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "System Binding");
        var (_, childPageId) = await SeedGraphPagesAsync(factory, user.UserId, tree.RootId, tree.LessonId);
        var planStepId = Guid.NewGuid();
        var quizAttemptId = Guid.NewGuid();
        var tutorTurnStateId = Guid.NewGuid();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var page = await db.WikiPages.FirstAsync(p => p.Id == childPageId);
            page.PlanStepId = planStepId;
            var repair = await db.WikiBlocks.FirstAsync(b => b.WikiPageId == childPageId && b.BlockType == WikiBlockType.RepairNote);
            repair.QuizAttemptId = quizAttemptId;
            db.WikiBlocks.Add(new WikiBlock
            {
                Id = Guid.NewGuid(),
                WikiPageId = childPageId,
                BlockType = WikiBlockType.TutorExplanation,
                Title = "Tutor trace",
                Content = "Tutor explanation bound to the active concept.",
                SourceBasis = "tutor_generated",
                ConceptKey = "big-o",
                TutorTurnStateId = tutorTurnStateId,
                OrderIndex = 2,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await user.Client.GetAsync($"/api/wiki/page/{childPageId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var pageJson = body.RootElement.GetProperty("page");
        Assert.Equal("ready", pageJson.GetProperty("contentReadiness").GetString());
        Assert.Equal(planStepId, pageJson.GetProperty("planStepId").GetGuid());
        var binding = pageJson.GetProperty("learningSystemBinding");
        Assert.Equal("bound", binding.GetProperty("readiness").GetString());
        Assert.True(binding.GetProperty("hasConceptBinding").GetBoolean());
        Assert.True(binding.GetProperty("hasPlanBinding").GetBoolean());
        Assert.True(binding.GetProperty("hasDiagnosticBinding").GetBoolean());
        Assert.True(binding.GetProperty("hasTutorBinding").GetBoolean());
        Assert.True(binding.GetProperty("hasAssessmentOrQuestionBankBinding").GetBoolean());
        Assert.Contains(binding.GetProperty("reasonCodes").EnumerateArray(), code => code.GetString() == "plan_step_id_present");
        Assert.Contains(binding.GetProperty("reasonCodes").EnumerateArray(), code => code.GetString() == "quiz_attempt_or_review_signal_present");

        var list = await user.Client.GetAsync($"/api/wiki/{tree.LessonId}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listJson = await JsonDocument.ParseAsync(await list.Content.ReadAsStreamAsync());
        var listedPage = Assert.Single(listJson.RootElement.EnumerateArray());
        Assert.Equal("bound", listedPage.GetProperty("learningSystemBinding").GetProperty("readiness").GetString());
    }

    [Fact]
    public async Task WikiPageQuestions_ReturnsPracticeReadyConceptQuestionsWithoutAnswerKeys()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-page-questions");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Wiki Questions");
        var (_, pageId) = await SeedGraphPagesAsync(factory, user.UserId, tree.RootId, tree.LessonId);
        var seeded = await SeedWikiPracticeQuestionAsync(factory, user.UserId, tree.RootId, "big-o");

        var response = await user.Client.GetAsync($"/api/wiki/page/{pageId}/questions?count=3");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("correctAnswer", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isCorrect\":true", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("diagnosticSignalIfChosen", raw, StringComparison.OrdinalIgnoreCase);

        var body = JsonSerializer.Deserialize<WikiPageQuestionSetDto>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(body);
        Assert.Equal(pageId, body!.PageId);
        Assert.Equal(tree.LessonId, body.TopicId);
        Assert.Equal("big-o", body.ConceptKey);
        Assert.Equal("ready", body.Status);

        var question = Assert.Single(body.Questions);
        Assert.Equal(seeded.QuestionItemId, question.QuestionItemId);
        Assert.Equal(seeded.AssessmentItemId, question.AssessmentItemId);
        Assert.Equal(tree.RootId, question.TopicId);
        Assert.Equal("big-o", question.ConceptKey);
        Assert.All(question.Options, option =>
        {
            Assert.False(option.IsCorrect);
            Assert.Null(option.Rationale);
            Assert.Null(option.DiagnosticSignalJson);
        });
    }

    [Fact]
    public async Task WikiPagePracticeStart_UsesPageScopeAndRecordsLearningAttempt()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-page-practice");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Wiki Practice");
        var (_, pageId) = await SeedGraphPagesAsync(factory, user.UserId, tree.RootId, tree.LessonId);
        var seeded = await SeedWikiPracticeQuestionAsync(factory, user.UserId, tree.RootId, "big-o");

        var start = await user.Client.PostAsJsonAsync($"/api/wiki/page/{pageId}/practice/start", new WikiPagePracticeStartRequestDto
        {
            Count = 3,
            Mode = "wiki_micro_drill"
        });
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        var session = await start.Content.ReadFromJsonAsync<QuestionPracticeSessionDto>();
        Assert.NotNull(session);
        Assert.Equal("ready", session!.Status);
        Assert.Equal("wiki_micro_drill", session.Mode);
        var question = Assert.Single(session.Questions);
        Assert.Equal(seeded.QuestionItemId, question.QuestionItemId);

        var submit = await user.Client.PostAsJsonAsync("/api/question-practice/submit", new QuestionPracticeSubmitRequestDto
        {
            PracticeSetId = session.PracticeSetId,
            TopicId = tree.LessonId,
            Mode = session.Mode,
            Answers =
            [
                new QuestionPracticeAnswerDto
                {
                    QuestionItemId = seeded.QuestionItemId,
                    SelectedOptionKey = "B",
                    ConfidenceSelfRating = 0.35m
                }
            ]
        });
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var attempt = await db.QuizAttempts.SingleAsync(a => a.UserId == user.UserId && a.AssessmentItemId == seeded.AssessmentItemId);
        Assert.False(attempt.IsCorrect);
        Assert.Equal(tree.RootId, attempt.TopicId);
        Assert.Equal("big-o", attempt.SkillTag);
    }

    [Fact]
    public async Task AddWikiBlock_CreatesPageAwareLearningBlockAndRedactsUnsafeMarkers()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-add-block");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Block Add");
        var (_, pageId) = await SeedGraphPagesAsync(factory, user.UserId, tree.RootId, tree.LessonId);

        var response = await user.Client.PostAsJsonAsync($"/api/wiki/page/{pageId}/blocks", new CreateWikiBlockRequestDto
        {
            BlockType = "student_question",
            Title = "Neden O(n) hiddenPrompt?",
            Content = "Big-O neden boyle? rawProviderPayload burada gorunmemeli.",
            SourceBasis = "source_grounded",
            ConceptKey = "big-o",
            MisconceptionKey = "big-o-linear-confusion",
            Visibility = "highlighted"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = json.RootElement;
        Assert.Equal("student_question", root.GetProperty("blockType").GetString());
        Assert.Equal("big-o", root.GetProperty("conceptKey").GetString());
        Assert.Equal("highlighted", root.GetProperty("visibility").GetString());
        Assert.Equal("evidence_insufficient", root.GetProperty("sourceBasis").GetString());
        var raw = root.GetRawText();
        Assert.DoesNotContain("hiddenPrompt", raw);
        Assert.DoesNotContain("rawProviderPayload", raw);
        Assert.Contains("source_grounded_bundle_missing_or_not_ready", raw);

        var page = await user.Client.GetAsync($"/api/wiki/page/{pageId}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        using var pageJson = await JsonDocument.ParseAsync(await page.Content.ReadAsStreamAsync());
        Assert.Contains(pageJson.RootElement.GetProperty("blocks").EnumerateArray(), b =>
            b.GetProperty("type").GetInt32() == (int)WikiBlockType.StudentQuestion &&
            b.GetProperty("sourceBasis").GetString() == "evidence_insufficient" &&
            b.GetProperty("conceptKey").GetString() == "big-o");
    }

    [Fact]
    public async Task AddWikiBlock_IsUserScoped()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-add-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-add-other");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, owner.UserId, "Block Scope");
        var (_, pageId) = await SeedGraphPagesAsync(factory, owner.UserId, tree.RootId, tree.LessonId);

        var response = await other.Client.PostAsJsonAsync($"/api/wiki/page/{pageId}/blocks", new CreateWikiBlockRequestDto
        {
            BlockType = "manual_note",
            Content = "foreign write"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WikiLearningTraceWriter_WritesTutorAndStudentBlocksToActivePageAndDedupes()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-trace-tutor");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Trace Tutor");
        var (_, pageId) = await SeedGraphPagesAsync(factory, user.UserId, tree.RootId, tree.LessonId);
        var turnStateId = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IWikiLearningTraceWriter>();

        var question = await writer.RecordStudentQuestionAsync(new WikiLearningTraceRequestDto
        {
            UserId = user.UserId,
            TopicId = tree.RootId,
            ActiveWikiPageId = pageId,
            ConceptKey = "big-o",
            TutorTurnStateId = turnStateId,
            SafeContent = "O(n) neden tek adim gibi anlatilmiyor?",
            SourceBasis = "student_manual",
            CreatedBy = "student"
        });
        var tutor = await writer.RecordTutorExplanationAsync(new WikiLearningTraceRequestDto
        {
            UserId = user.UserId,
            TopicId = tree.RootId,
            ActiveWikiPageId = pageId,
            ConceptKey = "big-o",
            TutorTurnStateId = turnStateId,
            SafeContent = "Big-O buyume oranini anlatan guvenli tutor notudur.",
            SourceBasis = "tutor_generated",
            CreatedBy = "tutor"
        });
        var duplicate = await writer.RecordTutorExplanationAsync(new WikiLearningTraceRequestDto
        {
            UserId = user.UserId,
            TopicId = tree.RootId,
            ActiveWikiPageId = pageId,
            ConceptKey = "big-o",
            TutorTurnStateId = turnStateId,
            SafeContent = "Big-O buyume oranini anlatan guvenli tutor notudur.",
            SourceBasis = "tutor_generated",
            CreatedBy = "tutor"
        });

        Assert.NotNull(question);
        Assert.NotNull(tutor);
        Assert.NotNull(duplicate);
        Assert.Equal(tutor!.Id, duplicate!.Id);
        Assert.Equal("student_manual", question!.SourceBasis);
        Assert.Equal("tutor_generated", tutor.SourceBasis);

        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var traceBlocks = await db.WikiBlocks
            .Where(b => b.WikiPageId == pageId && b.TutorTurnStateId == turnStateId && !b.IsDeleted)
            .ToListAsync();
        Assert.Equal(2, traceBlocks.Count);
        Assert.Contains(traceBlocks, b => b.BlockType == WikiBlockType.StudentQuestion);
        Assert.Contains(traceBlocks, b => b.BlockType == WikiBlockType.TutorExplanation);
    }

    [Fact]
    public async Task WikiLearningTraceWriter_DedupesNormalizedTraceTextWithoutRawLeak()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-trace-normalized-dedupe");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Trace Dedupe");
        var (_, pageId) = await SeedGraphPagesAsync(factory, user.UserId, tree.RootId, tree.LessonId);

        using var scope = factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IWikiLearningTraceWriter>();
        var first = await writer.RecordTutorExplanationAsync(new WikiLearningTraceRequestDto
        {
            UserId = user.UserId,
            TopicId = tree.RootId,
            ActiveWikiPageId = pageId,
            ConceptKey = "big-o",
            SafeContent = "Big-O   buyume oranini anlatan guvenli tutor notudur.",
            SourceBasis = "tutor_generated",
            CreatedBy = "tutor"
        });
        var second = await writer.RecordTutorExplanationAsync(new WikiLearningTraceRequestDto
        {
            UserId = user.UserId,
            TopicId = tree.RootId,
            ActiveWikiPageId = pageId,
            ConceptKey = "big-o",
            SafeContent = "Big-O buyume oranini anlatan guvenli tutor notudur.",
            SourceBasis = "tutor_generated",
            CreatedBy = "tutor"
        });

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.Id, second!.Id);

        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        Assert.Equal(1, await db.WikiBlocks.CountAsync(b =>
            b.WikiPageId == pageId &&
            b.BlockType == WikiBlockType.TutorExplanation &&
            b.ConceptKey == "big-o" &&
            !b.IsDeleted));
    }

    [Fact]
    public async Task WikiPageCuration_ReturnsDuplicateRepairAndSourceSafeSummary()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-curation-summary");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Trace Curation");
        var (_, pageId) = await SeedGraphPagesAsync(factory, user.UserId, tree.RootId, tree.LessonId);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var now = DateTime.UtcNow;
            db.WikiBlocks.AddRange(
                new WikiBlock
                {
                    Id = Guid.NewGuid(),
                    WikiPageId = pageId,
                    BlockType = WikiBlockType.TutorExplanation,
                    Title = "Tutor note",
                    Content = "Duplicate curation note",
                    SourceBasis = "tutor_generated",
                    ConceptKey = "big-o",
                    OrderIndex = 20,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new WikiBlock
                {
                    Id = Guid.NewGuid(),
                    WikiPageId = pageId,
                    BlockType = WikiBlockType.TutorExplanation,
                    Title = "Tutor note",
                    Content = "Duplicate   curation note",
                    SourceBasis = "tutor_generated",
                    ConceptKey = "big-o",
                    OrderIndex = 21,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            await db.SaveChangesAsync();
        }

        var response = await user.Client.GetAsync($"/api/wiki/page/{pageId}/curation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;
        Assert.Equal("duplicate_trace", root.GetProperty("curationStatus").GetString());
        Assert.True(root.GetProperty("suppressedSignalCount").GetInt32() >= 1);
        Assert.Contains(root.GetProperty("warnings").EnumerateArray(), w => w.GetString() == "duplicate_trace_suppressed");
        Assert.DoesNotContain(user.UserId.ToString(), root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawPrompt", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawProviderPayload", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WikiCopilot_CleanPageReturnsPageAwareStudySuggestionsWithoutRawLeak()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-copilot-clean");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Copilot Clean");
        var (pageId, _) = await SeedGraphPagesAsync(factory, user.UserId, tree.RootId, tree.LessonId);

        var response = await user.Client.GetAsync($"/api/wiki/page/{pageId}/copilot");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;
        Assert.Equal(pageId, root.GetProperty("pageId").GetGuid());
        Assert.Equal("clean", root.GetProperty("curationStatus").GetString());
        var actions = root.GetProperty("suggestedActions").EnumerateArray().ToArray();
        Assert.Contains(actions, a => a.GetProperty("actionType").GetString() == "summarize_page");
        Assert.Contains(actions, a => a.GetProperty("actionType").GetString() == "create_study_pack");
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("studentVisibleSummary").GetString()));
        Assert.DoesNotContain(user.UserId.ToString(), root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Raw tutor block content", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawPrompt", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawProviderPayload", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WikiCopilot_RepairPendingPageSuggestsRepairAndCheckpoint()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-copilot-repair");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Copilot Repair");
        var (_, pageId) = await SeedGraphPagesAsync(factory, user.UserId, tree.RootId, tree.LessonId);

        var response = await user.Client.GetAsync($"/api/wiki/page/{pageId}/copilot");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;
        Assert.Equal("repair_pending", root.GetProperty("repairState").GetString());
        Assert.Equal("start_repair", root.GetProperty("primaryAction").GetProperty("actionType").GetString());
        var actions = root.GetProperty("suggestedActions").EnumerateArray().ToArray();
        Assert.Contains(actions, a => a.GetProperty("actionType").GetString() == "generate_checkpoint");
        Assert.Contains(root.GetProperty("warnings").EnumerateArray(), w => w.GetString() == "repair_pending");
    }

    [Fact]
    public async Task WikiCopilot_SourceLimitedPageBlocksSourceGroundedActions()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-copilot-source-limited");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Copilot Source Limited");
        var pageId = await CoordinationTestHelpers.SeedWikiPageAsync(factory, user.UserId, tree.RootId, "Thin source page", "Tutor generated note.");

        var response = await user.Client.GetAsync($"/api/wiki/page/{pageId}/copilot");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;
        Assert.Equal("source_limited", root.GetProperty("curationStatus").GetString());
        var actions = root.GetProperty("suggestedActions").EnumerateArray().ToArray();
        var sourceAction = Assert.Single(actions.Where(a => a.GetProperty("actionType").GetString() == "ask_source"));
        Assert.Equal("blocked", sourceAction.GetProperty("availability").GetString());
        Assert.Contains(sourceAction.GetProperty("safetyWarnings").EnumerateArray(), w => w.GetString() == "action_degraded_for_safety");
        Assert.Contains(root.GetProperty("warnings").EnumerateArray(), w => w.GetString() == "source_grounded_actions_degraded");
    }

    [Fact]
    public async Task WikiCopilot_EmptyPageSuggestsTutorHelpNotStudyPack()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-copilot-empty");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Copilot Empty");
        Guid pageId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            pageId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            db.WikiPages.Add(new WikiPage
            {
                Id = pageId,
                UserId = user.UserId,
                TopicId = tree.RootId,
                Title = "Empty Copilot Page",
                PageKey = "empty-copilot-page",
                PageType = "concept",
                ConceptKey = "empty-copilot-page",
                SourceReadiness = "evidence_insufficient",
                EvidenceStatus = "evidence_insufficient",
                Status = "draft",
                CreatedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync();
        }

        var response = await user.Client.GetAsync($"/api/wiki/page/{pageId}/copilot");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;
        Assert.Equal("degraded", root.GetProperty("curationStatus").GetString());
        var actions = root.GetProperty("suggestedActions").EnumerateArray().ToArray();
        Assert.Contains(actions, a => a.GetProperty("actionType").GetString() == "ask_tutor_about_page");
        Assert.DoesNotContain(actions, a => a.GetProperty("actionType").GetString() == "create_study_pack");
    }

    [Fact]
    public async Task WikiCopilot_IsUserScoped()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-copilot-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-copilot-other");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, owner.UserId, "Copilot Scope");
        var (pageId, _) = await SeedGraphPagesAsync(factory, owner.UserId, tree.RootId, tree.LessonId);

        var response = await other.Client.GetAsync($"/api/wiki/page/{pageId}/copilot");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task WikiLearningTraceWriter_ResolvesConceptPageAndRedactsUnsafeMarkers()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-trace-redact");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Trace Redact");
        var (_, pageId) = await SeedGraphPagesAsync(factory, user.UserId, tree.RootId, tree.LessonId);

        using var scope = factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IWikiLearningTraceWriter>();
        var block = await writer.RecordTutorExplanationAsync(new WikiLearningTraceRequestDto
        {
            UserId = user.UserId,
            TopicId = tree.RootId,
            ConceptKey = "big-o",
            SafeContent = "hiddenPrompt ve rawProviderPayload C:\\secret\\trace.txt gorunmemeli.",
            SourceBasis = "tutor_generated",
            CreatedBy = "tutor"
        });

        Assert.NotNull(block);
        Assert.Equal(pageId, block!.WikiPageId);
        Assert.DoesNotContain("hiddenPrompt", block.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawProviderPayload", block.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", block.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[redacted]", block.Content);
    }

    [Fact]
    public async Task WikiLearningTraceWriter_WritesQuizAndArtifactBlocksWithoutDuplicate()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-trace-quiz-artifact");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Trace Quiz Artifact");
        var (_, pageId) = await SeedGraphPagesAsync(factory, user.UserId, tree.RootId, tree.LessonId);
        var quizAttemptId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IWikiLearningTraceWriter>();
        var quiz = await writer.RecordQuizResultAsync(new WikiLearningTraceRequestDto
        {
            UserId = user.UserId,
            TopicId = tree.RootId,
            ActiveWikiPageId = pageId,
            ConceptKey = "big-o",
            QuizAttemptId = quizAttemptId,
            SafeContent = "Sonuc: wrong. Remediation ihtiyaci: medium.",
            SourceBasis = "assessment_verified",
            CreatedBy = "quiz_attempt"
        });
        var duplicateQuiz = await writer.RecordQuizResultAsync(new WikiLearningTraceRequestDto
        {
            UserId = user.UserId,
            TopicId = tree.RootId,
            ActiveWikiPageId = pageId,
            ConceptKey = "big-o",
            QuizAttemptId = quizAttemptId,
            SafeContent = "Sonuc: wrong. Remediation ihtiyaci: medium.",
            SourceBasis = "assessment_verified",
            CreatedBy = "quiz_attempt"
        });
        var artifact = await writer.RecordArtifactLinkAsync(new WikiLearningTraceRequestDto
        {
            UserId = user.UserId,
            TopicId = tree.RootId,
            ActiveWikiPageId = pageId,
            ConceptKey = "big-o",
            LearningArtifactId = artifactId,
            SafeContent = "Artifact: Big-O study guide",
            SourceBasis = "wiki_backed",
            CreatedBy = "notebook_studio"
        });

        Assert.NotNull(quiz);
        Assert.NotNull(duplicateQuiz);
        Assert.NotNull(artifact);
        Assert.Equal(quiz!.Id, duplicateQuiz!.Id);
        Assert.Equal("quiz_result", quiz.BlockType);
        Assert.Equal("artifact_link", artifact!.BlockType);

        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        Assert.Equal(1, await db.WikiBlocks.CountAsync(b => b.WikiPageId == pageId && b.QuizAttemptId == quizAttemptId && !b.IsDeleted));
        Assert.Equal(1, await db.WikiBlocks.CountAsync(b => b.WikiPageId == pageId && b.LearningArtifactId == artifactId && !b.IsDeleted));
    }

    [Fact]
    public async Task WikiLearningTraceWriter_DegradesSourceGroundedTraceWithoutReadyBundle()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-trace-source");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Trace Source");
        var (_, pageId) = await SeedGraphPagesAsync(factory, user.UserId, tree.RootId, tree.LessonId);

        using var scope = factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IWikiLearningTraceWriter>();
        var block = await writer.RecordSourceNoteAsync(new WikiLearningTraceRequestDto
        {
            UserId = user.UserId,
            TopicId = tree.RootId,
            ActiveWikiPageId = pageId,
            ConceptKey = "big-o",
            SafeContent = "Kaynak notu hazir ama bundle dogrulanmadi.",
            SourceBasis = "source_grounded",
            CreatedBy = "source_evidence"
        });

        Assert.NotNull(block);
        Assert.Equal("source_note", block!.BlockType);
        Assert.Equal("evidence_insufficient", block.SourceBasis);
        Assert.Contains("source_grounded_bundle_missing_or_not_ready", block.SafetyWarnings);
    }

    private static async Task<(Guid RootPageId, Guid ChildPageId)> SeedGraphPagesAsync(
        ApiSmokeFactory factory,
        Guid userId,
        Guid rootTopicId,
        Guid lessonTopicId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;
        var rootPageId = Guid.NewGuid();
        var childPageId = Guid.NewGuid();

        db.WikiPages.AddRange(
            new WikiPage
            {
                Id = rootPageId,
                UserId = userId,
                TopicId = rootTopicId,
                Title = "Algorithms",
                Content = "Hidden raw page content",
                PageKey = "algorithms",
                PageType = "topic_root",
                ConceptKey = "algorithms",
                SourceReadiness = "wiki_backed",
                EvidenceStatus = "wiki_backed",
                SafeSummary = "Algorithms safe summary",
                Status = "ready",
                OrderIndex = 1,
                CreatedAt = now,
                UpdatedAt = now
            },
            new WikiPage
            {
                Id = childPageId,
                UserId = userId,
                TopicId = lessonTopicId,
                ParentWikiPageId = rootPageId,
                Title = "Big-O Basics",
                PageKey = "big-o",
                PageType = "concept",
                ConceptKey = "big-o",
                ParentConceptKey = "algorithms",
                SourceReadiness = "source_grounded",
                EvidenceStatus = "source_grounded",
                SafeSummary = "Big-O safe summary",
                Status = "ready",
                OrderIndex = 2,
                CreatedAt = now,
                UpdatedAt = now
            });

        db.WikiBlocks.AddRange(
            new WikiBlock
            {
                Id = Guid.NewGuid(),
                WikiPageId = rootPageId,
                BlockType = WikiBlockType.TutorExplanation,
                Title = "Tutor note",
                Content = "Raw tutor block content",
                SourceBasis = "model_assisted",
                ConceptKey = "algorithms",
                OrderIndex = 1,
                CreatedAt = now,
                UpdatedAt = now
            },
            new WikiBlock
            {
                Id = Guid.NewGuid(),
                WikiPageId = childPageId,
                BlockType = WikiBlockType.RepairNote,
                Title = "Repair",
                Content = "Do not confuse constant and linear work.",
                SourceBasis = "wiki_backed",
                ConceptKey = "big-o",
                MisconceptionKey = "big-o-linear-confusion",
                Visibility = "normal",
                OrderIndex = 1,
                CreatedAt = now,
                UpdatedAt = now
            });

        db.WikiLinks.Add(new WikiLink
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = rootTopicId,
            SourcePageId = rootPageId,
            TargetPageId = childPageId,
            TargetPageKey = "big-o",
            LinkType = "parent_child",
            Strength = 1m,
            CreatedBy = "system",
            SafeLabel = "Algorithms -> Big-O",
            CreatedAt = now,
            UpdatedAt = now
        });

        await db.SaveChangesAsync();
        return (rootPageId, childPageId);
    }

    private static async Task<WikiPracticeQuestionIds> SeedWikiPracticeQuestionAsync(
        ApiSmokeFactory factory,
        Guid userId,
        Guid topicId,
        string conceptKey)
    {
        using var scope = factory.Services.CreateScope();
        var examService = scope.ServiceProvider.GetRequiredService<IExamFrameworkService>();
        var exam = await examService.CreateSystemSkeletonAsync();

        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;
        var snapshotId = Guid.NewGuid();
        var conceptId = Guid.NewGuid();
        var assessmentItemId = Guid.NewGuid();

        db.ConceptGraphSnapshots.Add(new ConceptGraphSnapshot
        {
            Id = snapshotId,
            UserId = userId,
            TopicId = topicId,
            ApprovedResearchIntent = "wiki practice concept binding",
            TopicTitle = "Wiki practice concept binding",
            Domain = "computer_science",
            SourceConfidence = "model_assisted",
            GraphJson = "{}",
            CreatedAt = now
        });
        db.LearningConcepts.Add(new LearningConcept
        {
            Id = conceptId,
            ConceptGraphSnapshotId = snapshotId,
            StableKey = conceptKey,
            Label = "Big-O growth",
            Description = "Explain growth rate with input size.",
            DifficultyBand = "core",
            Order = 1,
            CreatedAt = now
        });

        var generatedQuestionJson = $$"""
        {
          "assessmentItemId": "{{assessmentItemId}}",
          "question": "Which statement best diagnoses a Big-O misconception?",
          "options": [
            { "id": "A", "text": "Big-O describes growth rate as input size changes.", "isCorrect": true, "rationale": "This checks growth-rate reasoning.", "diagnosticSignalIfChosen": "growth_rate_reasoning_present" },
            { "id": "B", "text": "Big-O is the exact runtime for every input.", "isCorrect": false, "rationale": "This exposes exact-runtime confusion.", "diagnosticSignalIfChosen": "big_o_exact_runtime_confusion" },
            { "id": "C", "text": "Big-O only applies to loops with one counter.", "isCorrect": false, "rationale": "This exposes syntax-only reasoning.", "diagnosticSignalIfChosen": "syntax_only_big_o_reasoning" }
          ],
          "correctAnswer": "A",
          "explanation": "Big-O is about growth behavior, not exact runtime."
        }
        """;

        db.AssessmentItems.Add(new AssessmentItem
        {
            Id = assessmentItemId,
            UserId = userId,
            TopicId = topicId,
            ConceptGraphSnapshotId = snapshotId,
            LearningConceptId = conceptId,
            AssessmentItemKey = $"{conceptKey}:wiki-practice-1",
            ConceptKey = conceptKey,
            ConceptLabel = "Big-O growth",
            QuestionType = "diagnostic_multiple_choice",
            CognitiveSkill = "conceptual",
            Difficulty = "core",
            MisconceptionTarget = "big_o_exact_runtime_confusion",
            EvidenceExpected = "Learner distinguishes growth rate from exact runtime.",
            GeneratedQuestionJson = generatedQuestionJson,
            ScoringRuleJson = """{"basis":"assessment_item","signals":["growth_rate_reasoning_present","big_o_exact_runtime_confusion"]}""",
            Order = 1,
            CreatedAt = now
        });

        var question = new QuestionItem
        {
            Id = Guid.NewGuid(),
            OwnerUserId = userId,
            ExamDefinitionId = exam.Id,
            LearningTopicId = topicId,
            ConceptGraphSnapshotId = snapshotId,
            LearningConceptId = conceptId,
            AssessmentItemId = assessmentItemId,
            QuestionBankSource = "diagnostic_assessment_item",
            ConceptKey = conceptKey,
            ConceptLabel = "Big-O growth",
            MisconceptionTarget = "big_o_exact_runtime_confusion",
            EvidenceExpected = "Learner distinguishes growth rate from exact runtime.",
            ScoringRuleJson = """{"basis":"assessment_item","signals":["growth_rate_reasoning_present","big_o_exact_runtime_confusion"]}""",
            CalibrationStatus = "review_ready",
            VisualReadinessStatus = "not_required",
            QuestionType = "multiple_choice",
            Stem = "Which statement best diagnoses a Big-O misconception?",
            Difficulty = "medium",
            CognitiveSkill = "conceptual",
            QualityStatus = "diagnostic_ready",
            LicenseStatus = "user_provided",
            SourceOrigin = "diagnostic_engine",
            SourceTitle = "Orka diagnostic assessment item",
            Explanation = "Big-O is about growth behavior, not exact runtime.",
            CreatedAt = now,
            UpdatedAt = now,
            Options =
            [
                new QuestionOption { OptionKey = "A", Text = "Big-O describes growth rate as input size changes.", IsCorrect = true, Rationale = "This checks growth-rate reasoning.", DiagnosticSignalJson = """{"signal":"growth_rate_reasoning_present"}""", SortOrder = 0 },
                new QuestionOption { OptionKey = "B", Text = "Big-O is the exact runtime for every input.", IsCorrect = false, Rationale = "This exposes exact-runtime confusion.", MisconceptionKey = "big_o_exact_runtime_confusion", DiagnosticSignalJson = """{"signal":"big_o_exact_runtime_confusion"}""", SortOrder = 1 },
                new QuestionOption { OptionKey = "C", Text = "Big-O only applies to loops with one counter.", IsCorrect = false, Rationale = "This exposes syntax-only reasoning.", MisconceptionKey = "syntax_only_big_o_reasoning", DiagnosticSignalJson = """{"signal":"syntax_only_big_o_reasoning"}""", SortOrder = 2 }
            ]
        };

        db.QuestionItems.Add(question);
        await db.SaveChangesAsync();
        return new WikiPracticeQuestionIds(assessmentItemId, question.Id);
    }

    private static async Task<Guid> SeedConceptGraphAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;
        var graphId = Guid.NewGuid();

        db.ConceptGraphSnapshots.Add(new ConceptGraphSnapshot
        {
            Id = graphId,
            UserId = userId,
            TopicId = topicId,
            IntentHash = "wiki-sync-graph",
            ApprovedResearchIntent = "Teach algorithms",
            TopicTitle = "Algorithms",
            Domain = "computer_science",
            SourceConfidence = "medium",
            SourceBundleHash = "sources",
            GraphJson = "{}",
            CreatedAt = now
        });
        db.LearningConcepts.AddRange(
            new LearningConcept
            {
                Id = Guid.NewGuid(),
                ConceptGraphSnapshotId = graphId,
                StableKey = "algorithm-basics",
                Label = "Algorithm Basics",
                Description = "What an algorithm is and why step order matters.",
                DifficultyBand = "core",
                Order = 0,
                CreatedAt = now
            },
            new LearningConcept
            {
                Id = Guid.NewGuid(),
                ConceptGraphSnapshotId = graphId,
                StableKey = "big-o",
                Label = "Big-O",
                Description = "How growth rate describes algorithm cost.",
                DifficultyBand = "core",
                Order = 1,
                CreatedAt = now
            });
        db.ConceptRelations.Add(new ConceptRelation
        {
            Id = Guid.NewGuid(),
            ConceptGraphSnapshotId = graphId,
            SourceConceptKey = "algorithm-basics",
            TargetConceptKey = "big-o",
            RelationType = "prerequisite",
            Weight = 0.9,
            CreatedAt = now
        });
        db.SourceEvidenceBundles.Add(new SourceEvidenceBundle
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            BundleHash = "wiki-sync-bundle",
            EvidenceStatus = "source_grounded",
            SourceCount = 1,
            ReadySourceCount = 1,
            ChunkCount = 2,
            CitationCoverage = 0.8m,
            EvidenceJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        });

        await db.SaveChangesAsync();
        return graphId;
    }

    private sealed record WikiPracticeQuestionIds(Guid AssessmentItemId, Guid QuestionItemId);
}
