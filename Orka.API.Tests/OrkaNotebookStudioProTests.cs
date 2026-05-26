using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class OrkaNotebookStudioProTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string RawSourcePhrase = "private rawSourceChunk C:\\secret\\artifact.txt token_marker_secret_value";

    [Fact]
    public async Task NotebookStudioPro_NewLearnerDegradesSafelyToCheckpointPack()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "notebook-pro-new");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Notebook Pro New");

        var pro = await GetProAsync(user, topicId);
        var json = JsonSerializer.Serialize(pro, JsonOptions);

        Assert.Equal(topicId, pro.TopicId);
        Assert.Contains(pro.ReadinessStatus, new[] { "thin_evidence", "limited" });
        Assert.Contains(pro.RecommendedPacks, p => p.PackType is "checkpoint_quiz_pack" or "artifact_collection");
        Assert.Contains(pro.Warnings, w => w.WarningCode == "export_preview_only");
        AssertSafePayload(json, user.UserId);
    }

    [Fact]
    public async Task NotebookStudioPro_RepeatedWrongCreatesRepairPack()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "notebook-pro-repair");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Notebook Pro Repair");
        await SeedWrongAttemptsAsync(factory, user.UserId, topicId);

        var pro = await GetProAsync(user, topicId);

        Assert.Contains(pro.RecommendedPacks, p => p.PackType == "repair_pack");
        Assert.Contains(pro.ArtifactQueue, a => a.ArtifactType is "misconception_repair_pack" or "study_guide");
        Assert.Contains(pro.TutorHandoffs, a => a.ActionType == "ask_tutor");
        AssertSafePayload(JsonSerializer.Serialize(pro, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task NotebookStudioPro_DueReviewCreatesReviewOrFlashcardPack()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "notebook-pro-review");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Notebook Pro Review");
        await SeedDueReviewAsync(factory, user.UserId, topicId);

        var pro = await GetProAsync(user, topicId);

        Assert.Contains(pro.RecommendedPacks, p => p.PackType is "review_pack" or "flashcard_pack");
        Assert.Contains(pro.ReviewHandoffs, a => a.ActionType is "create_review_pack" or "create_flashcards");
        AssertSafePayload(JsonSerializer.Serialize(pro, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task NotebookStudioPro_SourceInsufficientBlocksSourceBackedPackOverclaim()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "notebook-pro-source");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Notebook Pro Source");
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(factory, user.UserId, topicId, "Notebook Source", RawSourcePhrase);
        await SeedSourceLimitedAsync(factory, user.UserId, topicId, sourceId);

        var pro = await GetProAsync(user, topicId, sourceId: sourceId);
        var json = JsonSerializer.Serialize(pro, JsonOptions);

        Assert.Contains(pro.Warnings, w => w.WarningCode == "source_grounding_blocked");
        Assert.Contains(pro.RecommendedPacks, p => p.PackType == "source_study_pack");
        Assert.Contains(pro.SourceWikiHandoffs, a => a.ActionType is "create_source_study_pack" or "citation_review");
        Assert.DoesNotContain("source-grounded", json, StringComparison.OrdinalIgnoreCase);
        AssertSafePayload(json, user.UserId, RawSourcePhrase);
    }

    [Fact]
    public async Task NotebookStudioPro_WikiRepairPendingCreatesCleanupPack()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "notebook-pro-wiki");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Notebook Pro Wiki");
        var pageId = await CoordinationTestHelpers.SeedWikiPageAsync(factory, user.UserId, topicId, "Repair Wiki", "manual note rawPrompt private");
        await MarkWikiRepairAsync(factory, user.UserId, pageId);

        var pro = await GetProAsync(user, topicId, wikiPageId: pageId);

        Assert.Contains(pro.RecommendedPacks, p => p.PackType == "wiki_cleanup_pack");
        Assert.Contains(pro.WikiEvidenceLinks, l => l.WikiPageId == pageId);
        Assert.Contains(pro.Warnings, w => w.WarningCode == "wiki_source_backing_conflict");
        AssertSafePayload(JsonSerializer.Serialize(pro, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task NotebookStudioPro_StudyRoomTraceAndExportPreviewStayBounded()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "notebook-pro-trace");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Notebook Pro Trace");
        await SeedStudyRoomTraceAndPackAsync(factory, user.UserId, topicId);

        var pro = await GetProAsync(user, topicId);
        var json = JsonSerializer.Serialize(pro, JsonOptions);

        Assert.Contains(pro.RecommendedPacks, p => p.PackType is "study_room_summary_pack" or "artifact_collection");
        Assert.Contains(pro.StudyRoomTraceLinks, l => l.LinkType == "study_room_trace");
        Assert.Contains(pro.ExportPreviews, p => p.ExportLimitations.Contains("real_pptx_not_enabled"));
        Assert.Contains(pro.ExportPreviews, p => p.ExportLimitations.Contains("video_generation_not_enabled"));
        Assert.DoesNotContain("rawTranscript", json, StringComparison.OrdinalIgnoreCase);
        AssertSafePayload(json, user.UserId);
    }

    [Fact]
    public async Task NotebookStudioPro_DashboardConsumesProContract()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "notebook-pro-dashboard");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Notebook Pro Dashboard");
        await SeedWrongAttemptsAsync(factory, user.UserId, topicId);

        var dashboard = await user.Client.GetFromJsonAsync<DashboardTodayDto>("/api/dashboard/today");

        Assert.NotNull(dashboard?.NotebookStudioPro);
        Assert.Contains(dashboard!.NotebookStudioPro!.RecommendedPacks, p => p.PackType == "repair_pack");
        AssertSafePayload(JsonSerializer.Serialize(dashboard.NotebookStudioPro, JsonOptions), user.UserId);
    }

    [Fact]
    public async Task NotebookStudioPro_BlocksCrossUserTopicSourceWikiAccess()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "notebook-pro-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "notebook-pro-other");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, owner.UserId, "Private Pro Topic");
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(factory, owner.UserId, topicId, "Private Pro Source", "private source");
        var pageId = await CoordinationTestHelpers.SeedWikiPageAsync(factory, owner.UserId, topicId, "Private Pro Wiki", "private note");

        var byTopic = await other.Client.GetAsync($"/api/notebook-studio/pro?topicId={topicId}");
        var bySource = await other.Client.GetAsync($"/api/notebook-studio/pro?sourceId={sourceId}");
        var byWiki = await other.Client.GetAsync($"/api/notebook-studio/pro?wikiPageId={pageId}");

        Assert.Equal(HttpStatusCode.NotFound, byTopic.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, bySource.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, byWiki.StatusCode);
    }

    private static async Task<OrkaNotebookStudioProDto> GetProAsync(
        CoordinationTestUser user,
        Guid topicId,
        Guid? sourceId = null,
        Guid? wikiPageId = null)
    {
        var qs = $"topicId={topicId}";
        if (sourceId.HasValue) qs += $"&sourceId={sourceId.Value}";
        if (wikiPageId.HasValue) qs += $"&wikiPageId={wikiPageId.Value}";
        var response = await user.Client.GetAsync($"/api/notebook-studio/pro?{qs}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrkaNotebookStudioProDto>())!;
    }

    private static async Task SeedWrongAttemptsAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;

        db.KnowledgeTracingStates.Add(new KnowledgeTracingState
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ConceptKey = "repair-pack-concept",
            Label = "Repair Pack Concept",
            EvidenceCount = 3,
            CorrectCount = 0,
            IncorrectCount = 3,
            MasteryProbability = 0.24m,
            Confidence = 0.72m,
            RemediationNeed = "high",
            PracticeReadiness = "guided",
            LastEvidenceAt = now,
            CreatedAt = now.AddDays(-1),
            UpdatedAt = now
        });

        for (var i = 0; i < 3; i++)
        {
            db.QuizAttempts.Add(new QuizAttempt
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                Question = "Safe repair question",
                UserAnswer = "wrong",
                IsCorrect = false,
                Explanation = "Safe explanation",
                SkillTag = "repair-pack-concept",
                CreatedAt = now.AddMinutes(-i - 1)
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedDueReviewAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        db.ReviewItems.Add(new ReviewItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ReviewKey = "notebook-review",
            SkillTag = "Notebook Review",
            ConceptTag = "notebook-review",
            LearningObjective = "Notebook Review",
            DueAt = DateTime.UtcNow.AddDays(-2),
            Status = "active",
            CreatedAt = DateTime.UtcNow.AddDays(-10),
            UpdatedAt = DateTime.UtcNow.AddDays(-2)
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedSourceLimitedAsync(ApiSmokeFactory factory, Guid userId, Guid topicId, Guid sourceId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        db.SourceEvidenceBundles.Add(new SourceEvidenceBundle
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            BundleHash = Guid.NewGuid().ToString("N"),
            EvidenceStatus = "evidence_insufficient",
            SourceCount = 1,
            ReadySourceCount = 0,
            ChunkCount = 1,
            CitationCoverage = 0m,
            UnsupportedCitationCount = 1,
            StaleEvidenceCount = 1,
            EvidenceJson = "{}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.SourceCitationChecks.Add(new SourceCitationCheck
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SourceId = sourceId,
            CitationId = "citation-limited",
            CheckStatus = "unsupported",
            Confidence = 0.1m,
            Reason = "unsupported",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task MarkWikiRepairAsync(ApiSmokeFactory factory, Guid userId, Guid pageId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var page = await db.WikiPages.SingleAsync(p => p.Id == pageId && p.UserId == userId);
        page.SourceReadiness = "source_limited";
        page.EvidenceStatus = "evidence_insufficient";
        page.ConceptKey = "wiki-repair";

        var block = await db.WikiBlocks.SingleAsync(b => b.WikiPageId == pageId);
        block.BlockType = WikiBlockType.RepairNote;
        block.ConceptKey = "wiki-repair";
        block.SourceBasis = "evidence_insufficient";
        block.SafetyWarningsJson = "[\"source_limited\",\"repair_pending\"]";
        await db.SaveChangesAsync();
    }

    private static async Task SeedStudyRoomTraceAndPackAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var artifactId = Guid.NewGuid();
        var packId = Guid.NewGuid();

        db.ClassroomSessions.Add(new ClassroomSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            Transcript = "bounded safe study room summary",
            LastSegment = "checkpoint:submitted",
            Status = "completed",
            CreatedAt = DateTime.UtcNow.AddMinutes(-20),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        db.LearningArtifacts.Add(new LearningArtifact
        {
            Id = artifactId,
            UserId = userId,
            TopicId = topicId,
            ArtifactType = "slide_deck_outline",
            ArtifactStatus = "ready",
            Origin = "notebook_studio",
            RenderFormat = "markdown",
            Title = "Safe slide outline",
            SafeContent = "Safe outline body is not exposed by Pro DTO.",
            SourceBasis = "derived_metadata",
            SafetyWarningsJson = "[\"export_preview_only\"]",
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        db.LearningNotebookPacks.Add(new LearningNotebookPack
        {
            Id = packId,
            UserId = userId,
            TopicId = topicId,
            PackType = "study_room_summary_pack",
            PackStatus = "ready",
            Title = "Study Room summary pack",
            Summary = "Safe summary pack metadata",
            SourceReadiness = "derived_metadata",
            EvidenceStatus = "derived_metadata",
            ArtifactIdsJson = JsonSerializer.Serialize(new[] { artifactId }),
            NextActionsJson = "[\"open_existing_pack\"]",
            WarningsJson = "[\"export_preview_only\"]",
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        await db.SaveChangesAsync();
    }

    private static void AssertSafePayload(string json, Guid userId, string? rawPhrase = null)
    {
        var unsafeMarkers = new[]
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
            "userId",
            "rawTranscript"
        };

        foreach (var marker in unsafeMarkers)
        {
            Assert.DoesNotContain(marker, json, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(userId.ToString(), json, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(rawPhrase))
        {
            Assert.DoesNotContain(rawPhrase, json, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("C:\\", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("success guarantee", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("official curriculum complete", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("%100", json, StringComparison.OrdinalIgnoreCase);
    }
}
