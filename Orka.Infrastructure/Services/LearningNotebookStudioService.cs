using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class LearningNotebookStudioService : ILearningNotebookStudioService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan NotebookTtsTimeout = TimeSpan.FromSeconds(20);
    private static readonly HashSet<string> PackTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "topic_overview", "milestone_review", "wiki_page_review", "source_digest", "source_notebook", "source_review", "misconception_repair", "exam_review", "project_review"
    };

    private readonly OrkaDbContext _db;
    private readonly ISourceEvidenceLifecycleService _sourceLifecycle;
    private readonly IActiveLessonSnapshotService _snapshots;
    private readonly ILearningArtifactService _artifacts;
    private readonly IAgenticTrustPolicyService _trust;
    private readonly IFlashcardService _flashcards;
    private readonly IAssessmentBlueprintService _assessmentBlueprints;
    private readonly IEdgeTtsService _tts;
    private readonly IWikiLearningTraceWriter? _wikiTraceWriter;
    private readonly ISourceConceptLinkingService? _sourceConceptLinks;

    public LearningNotebookStudioService(
        OrkaDbContext db,
        ISourceEvidenceLifecycleService sourceLifecycle,
        IActiveLessonSnapshotService snapshots,
        ILearningArtifactService artifacts,
        IAgenticTrustPolicyService trust,
        IFlashcardService flashcards,
        IAssessmentBlueprintService assessmentBlueprints,
        IEdgeTtsService tts,
        IWikiLearningTraceWriter? wikiTraceWriter = null,
        ISourceConceptLinkingService? sourceConceptLinks = null)
    {
        _db = db;
        _sourceLifecycle = sourceLifecycle;
        _snapshots = snapshots;
        _artifacts = artifacts;
        _trust = trust;
        _flashcards = flashcards;
        _assessmentBlueprints = assessmentBlueprints;
        _tts = tts;
        _wikiTraceWriter = wikiTraceWriter;
        _sourceConceptLinks = sourceConceptLinks;
    }

    public async Task<LearningNotebookPackListDto> ListPacksAsync(
        Guid userId,
        Guid topicId,
        Guid? sessionId = null,
        Guid? wikiPageId = null,
        string? surface = null,
        Guid? sourceId = null,
        CancellationToken ct = default)
    {
        if (!await OwnsTopicAsync(userId, topicId, ct)) return new LearningNotebookPackListDto();

        var query = _db.LearningNotebookPacks.AsNoTracking()
            .Where(p => p.UserId == userId && p.TopicId == topicId && !p.IsDeleted);
        if (sessionId.HasValue) query = query.Where(p => p.SessionId == sessionId.Value);
        if (wikiPageId.HasValue) query = query.Where(p => p.WikiPageId == wikiPageId.Value);
        if (IsSourceSurface(surface))
        {
            query = query.Where(p =>
                p.PackType == "source_digest" ||
                p.PackType == "source_notebook" ||
                p.PackType == "source_review" ||
                p.SafeMetadataJson.Contains("sourceSurface"));
        }
        if (sourceId.HasValue)
        {
            var sourceIdText = sourceId.Value.ToString();
            query = query.Where(p => p.SafeMetadataJson.Contains(sourceIdText));
        }

        var packs = await query.OrderByDescending(p => p.UpdatedAt).Take(50).ToListAsync(ct);
        var dtos = new List<LearningNotebookPackDto>();
        foreach (var pack in packs)
        {
            dtos.Add(await ToDtoAsync(pack, includeArtifacts: false, ct));
        }

        return new LearningNotebookPackListDto { Count = dtos.Count, Items = dtos };
    }

    public async Task<LearningNotebookPackDto?> GetPackAsync(Guid userId, Guid packId, CancellationToken ct = default)
    {
        var pack = await _db.LearningNotebookPacks
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == packId && p.UserId == userId && !p.IsDeleted, ct);
        return pack == null ? null : await ToDtoAsync(pack, includeArtifacts: true, ct);
    }

    public async Task<LearningNotebookPackDto> BuildMilestonePackAsync(
        Guid userId,
        Guid topicId,
        LearningNotebookPackRequestDto request,
        CancellationToken ct = default)
    {
        var pageContext = await LoadWikiPageContextAsync(userId, request.WikiPageId, ct);
        if (request.WikiPageId.HasValue && pageContext == null)
            throw new InvalidOperationException("Wiki page not found for notebook studio.");
        if (pageContext != null)
        {
            topicId = pageContext.TopicId;
        }

        var topic = await _db.Topics.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == topicId && t.UserId == userId && !t.IsArchived, ct)
            ?? throw new InvalidOperationException("Topic not found for notebook studio.");

        if (request.SessionId.HasValue && !await OwnsSessionAsync(userId, request.SessionId.Value, ct))
            throw new InvalidOperationException("Session not found for notebook studio.");

        var packType = NormalizePackType(pageContext != null && string.Equals(request.PackType, "milestone_review", StringComparison.OrdinalIgnoreCase)
            ? "wiki_page_review"
            : request.PackType);
        var activeSnapshot = await _snapshots.GetActiveLessonSnapshotAsync(userId, topicId, request.SessionId, ct);
        var studentSnapshot = await _snapshots.GetStudentContextSnapshotAsync(userId, topicId, request.SessionId, ct);
        var sourceBundle = await _sourceLifecycle.GetLatestSourceEvidenceBundleAsync(userId, topicId, request.SessionId, ct)
                           ?? await _sourceLifecycle.BuildSourceEvidenceBundleAsync(userId, topicId, request.SessionId, request.UserGoal ?? topic.Title, ct);
        var notebook = await _sourceLifecycle.GetLatestWikiKnowledgeNotebookAsync(userId, topicId, ct)
                       ?? await _sourceLifecycle.BuildWikiKnowledgeNotebookAsync(userId, topicId, ct);
        var notebookSnapshotId = await _db.WikiKnowledgeNotebookSnapshots.AsNoTracking()
            .Where(n => n.UserId == userId && n.TopicId == topicId && !n.IsDeleted)
            .OrderByDescending(n => n.UpdatedAt)
            .Select(n => (Guid?)n.Id)
            .FirstOrDefaultAsync(ct);
        var planQualitySnapshotId = await _db.LearningPlanQualitySnapshots.AsNoTracking()
            .Where(p => p.UserId == userId && p.TopicId == topicId && !p.IsDeleted)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);
        var assessmentQualitySnapshotId = await _db.AssessmentQualitySnapshots.AsNoTracking()
            .Where(a => a.UserId == userId && a.TopicId == topicId && !a.IsDeleted)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);

        var conceptState = await BuildConceptStateAsync(userId, topicId, activeSnapshot, studentSnapshot, pageContext, ct);
        var sourceLinkSummary = await LoadSourceConceptSummaryAsync(userId, null, pageContext?.Id, ct);
        var warnings = sourceBundle.Warnings
            .Concat(notebook.SourceWarnings)
            .Concat(BuildPackWarnings(sourceBundle, conceptState, pageContext))
            .Concat(sourceLinkSummary?.Warnings ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();
        var now = DateTime.UtcNow;
        var pack = new LearningNotebookPack
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = request.SessionId,
            WikiPageId = pageContext?.Id,
            WikiPageTitle = pageContext?.Title,
            WikiPageKey = pageContext?.PageKey,
            ActiveLessonSnapshotId = activeSnapshot?.Id,
            StudentContextSnapshotId = studentSnapshot?.Id,
            SourceEvidenceBundleId = sourceBundle.Id,
            WikiNotebookSnapshotId = notebookSnapshotId,
            PlanQualitySnapshotId = planQualitySnapshotId,
            AssessmentQualitySnapshotId = assessmentQualitySnapshotId,
            PackType = packType,
            PackStatus = StatusFor(sourceBundle.EvidenceStatus),
            Title = BuildPackTitle(pageContext?.Title ?? topic.Title, packType),
            Summary = BuildSummary(pageContext?.Title ?? topic.Title, conceptState, sourceBundle, notebook, pageContext),
            SourceReadiness = sourceBundle.EvidenceStatus,
            EvidenceStatus = sourceBundle.EvidenceStatus,
            CompletedConceptKeysJson = Serialize(conceptState.Completed),
            WeakConceptKeysJson = Serialize(conceptState.Weak),
            MisconceptionKeysJson = Serialize(conceptState.Misconceptions),
            NextActionsJson = Serialize(BuildNextActions(conceptState, sourceBundle)),
            WarningsJson = Serialize(warnings),
            SafeMetadataJson = Serialize(new
            {
                contract = "orka_notebook_studio_v1",
                wikiPageId = pageContext?.Id,
                wikiPageTitle = pageContext?.Title,
                wikiPageKey = pageContext?.PageKey,
                wikiPageType = pageContext?.PageType,
                wikiPageConceptKey = pageContext?.ConceptKey,
                wikiPageBlockCount = pageContext?.BlockSnippets.Count ?? 0,
                wikiPageQuestionCount = pageContext?.QuestionSnippets.Count ?? 0,
                wikiCurationStatus = pageContext?.Curation.CurationStatus,
                wikiCurationSummary = pageContext?.Curation.StudentVisibleSummary,
                wikiCurationWarnings = pageContext?.Curation.Warnings.Take(6).ToArray(),
                linkedSourceIds = sourceLinkSummary?.Links
                    .Where(l => l.SourceId.HasValue)
                    .Select(l => l.SourceId!.Value)
                    .Distinct()
                    .Take(12)
                    .ToArray(),
                supportingSourceCount = sourceLinkSummary?.ConfirmedLinkCount ?? 0,
                notebook.ConceptCoverage,
                notebook.SourceCoverage,
                generatedFrom = pageContext == null ? "snapshot_plan_quiz_source_wiki" : "wiki_page_snapshot_plan_quiz_source"
            }),
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.LearningNotebookPacks.Add(pack);
        await _db.SaveChangesAsync(ct);

        if (request.IncludeArtifacts)
        {
            var artifactIds = new List<Guid>();
            foreach (var artifactType in new[] { "milestone_review", "study_guide", "briefing_doc", "source_digest", "misconception_repair_pack", "worked_example_set", "retrieval_card_set", "slide_deck_outline" })
            {
                var artifact = await BuildArtifactInternalAsync(userId, pack, artifactType, conceptState, sourceBundle, notebook, ct);
                if (artifact != null) artifactIds.Add(artifact.Id);
            }

            pack.ArtifactIdsJson = Serialize(artifactIds);
            pack.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return await ToDtoAsync(pack, includeArtifacts: true, ct);
    }

    public async Task<LearningNotebookPackDto?> BuildWikiPagePackAsync(
        Guid userId,
        Guid wikiPageId,
        LearningNotebookPackRequestDto request,
        CancellationToken ct = default)
    {
        var page = await _db.WikiPages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == wikiPageId && p.UserId == userId && !p.IsDeleted, ct);
        if (page == null) return null;

        var pageRequest = new LearningNotebookPackRequestDto
        {
            SessionId = request.SessionId,
            WikiPageId = wikiPageId,
            PackType = string.IsNullOrWhiteSpace(request.PackType) || string.Equals(request.PackType, "milestone_review", StringComparison.OrdinalIgnoreCase)
                ? "wiki_page_review"
                : request.PackType,
            FocusConceptKey = request.FocusConceptKey ?? page.ConceptKey,
            UserGoal = request.UserGoal ?? $"Wiki sayfasi calisma paketi: {page.Title}",
            IncludeArtifacts = request.IncludeArtifacts
        };

        return await BuildMilestonePackAsync(userId, page.TopicId, pageRequest, ct);
    }

    public async Task<LearningNotebookPackDto?> BuildSourcePackAsync(
        Guid userId,
        Guid sourceId,
        LearningNotebookPackRequestDto request,
        CancellationToken ct = default)
    {
        var source = await _db.LearningSources
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sourceId && s.UserId == userId && !s.IsDeleted, ct);
        if (source == null || !source.TopicId.HasValue) return null;

        var sourceBundle = await _sourceLifecycle.GetLatestSourceEvidenceBundleAsync(userId, source.TopicId.Value, source.SessionId, ct)
                           ?? await _sourceLifecycle.BuildSourceEvidenceBundleAsync(userId, source.TopicId.Value, source.SessionId, request.UserGoal ?? source.Title, ct);
        var page = await EnsureOrkaLmSourcePageAsync(userId, source, sourceBundle, ct);

        var sourceRequest = new LearningNotebookPackRequestDto
        {
            SessionId = request.SessionId ?? source.SessionId,
            WikiPageId = page.Id,
            SourceId = source.Id,
            SourceSurface = "source",
            PackType = string.IsNullOrWhiteSpace(request.PackType) || string.Equals(request.PackType, "milestone_review", StringComparison.OrdinalIgnoreCase)
                ? "source_digest"
                : request.PackType,
            FocusConceptKey = request.FocusConceptKey,
            UserGoal = request.UserGoal ?? $"OrkaLM source notebook pack: {source.Title}",
            IncludeArtifacts = request.IncludeArtifacts
        };

        return await BuildSourcePackCoreAsync(userId, source.TopicId.Value, source, sourceRequest, sourceBundle, ct);
    }

    public async Task<LearningNotebookPackDto?> BuildTopicSourcePackAsync(
        Guid userId,
        Guid topicId,
        LearningNotebookPackRequestDto request,
        CancellationToken ct = default)
    {
        if (!await OwnsTopicAsync(userId, topicId, ct)) return null;

        if (request.SourceId.HasValue)
        {
            return await BuildSourcePackAsync(userId, request.SourceId.Value, request, ct);
        }

        var source = await _db.LearningSources
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId == topicId && !s.IsDeleted)
            .OrderByDescending(s => s.Status == "ready")
            .ThenByDescending(s => s.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        if (source != null)
        {
            return await BuildSourcePackAsync(userId, source.Id, request, ct);
        }

        var topicRequest = new LearningNotebookPackRequestDto
        {
            SessionId = request.SessionId,
            SourceSurface = "source_collection",
            PackType = string.IsNullOrWhiteSpace(request.PackType) || string.Equals(request.PackType, "milestone_review", StringComparison.OrdinalIgnoreCase)
                ? "source_notebook"
                : request.PackType,
            FocusConceptKey = request.FocusConceptKey,
            UserGoal = request.UserGoal ?? "OrkaLM source collection pack",
            IncludeArtifacts = request.IncludeArtifacts
        };

        return await BuildSourcePackCoreAsync(userId, topicId, null, topicRequest, null, ct);
    }

    private async Task<LearningNotebookPackDto?> BuildSourcePackCoreAsync(
        Guid userId,
        Guid topicId,
        LearningSource? source,
        LearningNotebookPackRequestDto request,
        SourceEvidenceBundleDto? sourceBundle,
        CancellationToken ct)
    {
        var topic = await _db.Topics.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == topicId && t.UserId == userId && !t.IsArchived, ct);
        if (topic == null) return null;

        if (request.SessionId.HasValue && !await OwnsSessionAsync(userId, request.SessionId.Value, ct))
            return null;

        sourceBundle ??= await _sourceLifecycle.GetLatestSourceEvidenceBundleAsync(userId, topicId, request.SessionId, ct)
                         ?? await _sourceLifecycle.BuildSourceEvidenceBundleAsync(userId, topicId, request.SessionId, request.UserGoal ?? topic.Title, ct);
        var notebook = await _sourceLifecycle.GetLatestWikiKnowledgeNotebookAsync(userId, topicId, ct)
                       ?? await _sourceLifecycle.BuildWikiKnowledgeNotebookAsync(userId, topicId, ct);
        var notebookSnapshotId = await _db.WikiKnowledgeNotebookSnapshots.AsNoTracking()
            .Where(n => n.UserId == userId && n.TopicId == topicId && !n.IsDeleted)
            .OrderByDescending(n => n.UpdatedAt)
            .Select(n => (Guid?)n.Id)
            .FirstOrDefaultAsync(ct);
        var planQualitySnapshotId = await _db.LearningPlanQualitySnapshots.AsNoTracking()
            .Where(p => p.UserId == userId && p.TopicId == topicId && !p.IsDeleted)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);
        var assessmentQualitySnapshotId = await _db.AssessmentQualitySnapshots.AsNoTracking()
            .Where(a => a.UserId == userId && a.TopicId == topicId && !a.IsDeleted)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);

        var pageContext = await LoadWikiPageContextAsync(userId, request.WikiPageId, ct);
        var sourceLinkSummary = await LoadSourceConceptSummaryAsync(userId, source, pageContext?.Id, ct);
        var sourceQuestionSummary = await LoadSourceQuestionSummaryAsync(userId, topicId, source?.Id, pageContext?.Id, ct);
        var activeSnapshot = await _snapshots.GetActiveLessonSnapshotAsync(userId, topicId, request.SessionId, ct);
        var studentSnapshot = await _snapshots.GetStudentContextSnapshotAsync(userId, topicId, request.SessionId, ct);
        var conceptState = await BuildConceptStateAsync(userId, topicId, activeSnapshot, studentSnapshot, pageContext, ct);
        var packType = NormalizePackType(request.PackType);
        var sourceTitle = source == null ? topic.Title : source.Title;
        var warnings = sourceBundle.Warnings
            .Concat(notebook.SourceWarnings)
            .Concat(BuildPackWarnings(sourceBundle, conceptState, pageContext))
            .Concat(BuildSourcePackWarnings(source, sourceBundle))
            .Concat(sourceLinkSummary?.Warnings ?? Array.Empty<string>())
            .Concat(sourceQuestionSummary.Warnings)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();
        var now = DateTime.UtcNow;
        var pack = new LearningNotebookPack
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = request.SessionId,
            WikiPageId = pageContext?.Id,
            WikiPageTitle = pageContext?.Title,
            WikiPageKey = pageContext?.PageKey,
            ActiveLessonSnapshotId = activeSnapshot?.Id,
            StudentContextSnapshotId = studentSnapshot?.Id,
            SourceEvidenceBundleId = sourceBundle.Id,
            WikiNotebookSnapshotId = notebookSnapshotId,
            PlanQualitySnapshotId = planQualitySnapshotId,
            AssessmentQualitySnapshotId = assessmentQualitySnapshotId,
            PackType = packType,
            PackStatus = StatusFor(sourceBundle.EvidenceStatus),
            Title = source == null
                ? $"{topic.Title} - source notebook"
                : $"{source.Title} - source notebook",
            Summary = BuildSourcePackSummary(sourceTitle, conceptState, sourceBundle, pageContext, source) + BuildSourceQuestionPackSummary(sourceQuestionSummary),
            SourceReadiness = sourceBundle.EvidenceStatus,
            EvidenceStatus = sourceBundle.EvidenceStatus,
            CompletedConceptKeysJson = Serialize(conceptState.Completed),
            WeakConceptKeysJson = Serialize(conceptState.Weak),
            MisconceptionKeysJson = Serialize(conceptState.Misconceptions),
            NextActionsJson = Serialize(BuildSourceNextActions(conceptState, sourceBundle, source)),
            WarningsJson = Serialize(warnings),
            SafeMetadataJson = Serialize(new
            {
                contract = "orka_notebook_studio_v1",
                sourceSurface = source == null ? "source_collection" : "source",
                sourceId = source?.Id,
                sourceTitle = source?.Title,
                sourceStatus = source?.Status,
                sourceChunkCount = source?.ChunkCount,
                wikiPageId = pageContext?.Id,
                wikiPageTitle = pageContext?.Title,
                wikiPageKey = pageContext?.PageKey,
                wikiPageType = pageContext?.PageType,
                wikiPageConceptKey = pageContext?.ConceptKey,
                wikiPageBlockCount = pageContext?.BlockSnippets.Count ?? 0,
                wikiCurationStatus = pageContext?.Curation.CurationStatus,
                wikiCurationSummary = pageContext?.Curation.StudentVisibleSummary,
                wikiCurationWarnings = pageContext?.Curation.Warnings.Take(6).ToArray(),
                linkedConceptKeys = sourceLinkSummary?.Links
                    .Where(l => !string.IsNullOrWhiteSpace(l.ConceptKey))
                    .Select(l => l.ConceptKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(12)
                    .ToArray(),
                linkedSourceIds = sourceLinkSummary?.Links
                    .Where(l => l.SourceId.HasValue)
                    .Select(l => l.SourceId!.Value)
                    .Distinct()
                    .Take(12)
                    .ToArray(),
                sourceConceptLinkStatus = sourceLinkSummary == null ? "not_available" : "ready",
                sourceQuestionMemory = sourceQuestionSummary,
                notebook.ConceptCoverage,
                notebook.SourceCoverage,
                generatedFrom = source == null ? "orkalm_source_collection" : "orkalm_source_notebook"
            }),
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.LearningNotebookPacks.Add(pack);
        await _db.SaveChangesAsync(ct);

        if (request.IncludeArtifacts)
        {
            var artifactIds = new List<Guid>();
            foreach (var artifactType in new[] { "source_digest", "study_guide", "briefing_doc", "audio_script", "mind_map", "flashcard_set", "review_quiz", "slide_deck_outline", "slide_export_manifest" })
            {
                var artifact = await BuildArtifactInternalAsync(userId, pack, artifactType, conceptState, sourceBundle, notebook, ct);
                if (artifact != null) artifactIds.Add(artifact.Id);
            }

            pack.ArtifactIdsJson = Serialize(artifactIds);
            pack.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        return await ToDtoAsync(pack, includeArtifacts: true, ct);
    }

    private async Task<SourceConceptLinkSummaryDto?> LoadSourceConceptSummaryAsync(
        Guid userId,
        LearningSource? source,
        Guid? wikiPageId,
        CancellationToken ct)
    {
        if (_sourceConceptLinks == null) return null;
        try
        {
            if (source != null)
            {
                return await _sourceConceptLinks.SyncSourceConceptLinksAsync(userId, source.Id, ct)
                       ?? await _sourceConceptLinks.GetSourceConceptLinksAsync(userId, source.Id, ct);
            }

            if (wikiPageId.HasValue)
            {
                return await _sourceConceptLinks.GetWikiPageSourceLinksAsync(userId, wikiPageId.Value, ct);
            }
        }
        catch
        {
            // Link enrichment must not block Notebook pack creation.
        }

        return null;
    }

    private async Task<SourceQuestionPackSummary> LoadSourceQuestionSummaryAsync(
        Guid userId,
        Guid topicId,
        Guid? sourceId,
        Guid? wikiPageId,
        CancellationToken ct)
    {
        var artifacts = await _db.LearningArtifacts.AsNoTracking()
            .Where(a => a.UserId == userId &&
                        a.TopicId == topicId &&
                        !a.IsDeleted &&
                        a.ArtifactType == "source_question_thread")
            .OrderByDescending(a => a.UpdatedAt)
            .Take(40)
            .ToListAsync(ct);

        var summaries = new List<SourceQuestionPackSummary>();
        foreach (var artifact in artifacts)
        {
            var summary = TryParseSourceQuestionSummary(artifact.ContentJson);
            if (summary == null) continue;
            if (sourceId.HasValue && !summary.SourceIds.Contains(sourceId.Value)) continue;
            if (!sourceId.HasValue && wikiPageId.HasValue && summary.WikiPageId != wikiPageId.Value) continue;
            summaries.Add(summary);
        }

        if (summaries.Count == 0) return new SourceQuestionPackSummary();
        var threadCount = summaries.Count;
        var turnCount = summaries.Sum(s => s.TurnCount);
        var needsReview = summaries.Sum(s => s.NeedsReviewCount);
        var degraded = summaries.Sum(s => s.DegradedCount);
        var warnings = summaries.SelectMany(s => s.Warnings)
            .Concat(needsReview > 0 ? ["source_question_review_needed"] : Array.Empty<string>())
            .Concat(degraded > 0 ? ["source_question_thread_degraded"] : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        return new SourceQuestionPackSummary
        {
            ThreadCount = threadCount,
            TurnCount = turnCount,
            NeedsReviewCount = needsReview,
            DegradedCount = degraded,
            RecentQuestions = summaries.SelectMany(s => s.RecentQuestions).Take(6).ToArray(),
            Warnings = warnings
        };
    }

    private static SourceQuestionPackSummary? TryParseSourceQuestionSummary(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(contentJson);
            var root = doc.RootElement;
            var sourceIds = root.TryGetProperty("sourceIds", out var sourceIdsElement) && sourceIdsElement.ValueKind == JsonValueKind.Array
                ? sourceIdsElement.EnumerateArray()
                    .Select(e => Guid.TryParse(e.GetString(), out var id) ? id : Guid.Empty)
                    .Where(id => id != Guid.Empty)
                    .ToArray()
                : Array.Empty<Guid>();
            var wikiPageId = root.TryGetProperty("wikiPageId", out var wikiPageElement) &&
                             Guid.TryParse(wikiPageElement.GetString(), out var parsedPageId)
                ? parsedPageId
                : (Guid?)null;
            var turns = root.TryGetProperty("turns", out var turnsElement) && turnsElement.ValueKind == JsonValueKind.Array
                ? turnsElement.EnumerateArray().ToArray()
                : Array.Empty<JsonElement>();
            var needsReview = turns.Count(t =>
                t.TryGetProperty("reviewStatus", out var r) &&
                r.GetString() is "needs_review" or "missing_citation" or "unsupported" or "not_checked");
            var degraded = turns.Count(t =>
                (t.TryGetProperty("sourceBasis", out var b) && b.GetString() == "degraded") ||
                (t.TryGetProperty("evidenceStatus", out var e) && e.GetString() is "degraded" or "stale"));
            var questions = turns
                .Select(t => t.TryGetProperty("question", out var q) ? q.GetString() : null)
                .Where(NotBlank)
                .Select(q => Trim(q!, 180))
                .Take(6)
                .ToArray();
            var warnings = root.TryGetProperty("warnings", out var warningsElement) && warningsElement.ValueKind == JsonValueKind.Array
                ? warningsElement.EnumerateArray().Select(e => e.GetString()).Where(NotBlank).Select(w => Trim(w!, 100)).Take(8).ToArray()
                : Array.Empty<string>();

            return new SourceQuestionPackSummary
            {
                SourceIds = sourceIds,
                WikiPageId = wikiPageId,
                ThreadCount = 1,
                TurnCount = turns.Length,
                NeedsReviewCount = needsReview,
                DegradedCount = degraded,
                RecentQuestions = questions,
                Warnings = warnings
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task<WikiPage> EnsureOrkaLmSourcePageAsync(
        Guid userId,
        LearningSource source,
        SourceEvidenceBundleDto sourceBundle,
        CancellationToken ct)
    {
        if (!source.TopicId.HasValue)
            throw new InvalidOperationException("Source topic is required for OrkaLM source pages.");

        var pageKey = $"orkalm-source:{source.Id:N}";
        var page = await _db.WikiPages
            .FirstOrDefaultAsync(p => p.UserId == userId && p.TopicId == source.TopicId.Value && p.PageKey == pageKey && !p.IsDeleted, ct);
        var now = DateTime.UtcNow;
        if (page == null)
        {
            page = new WikiPage
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = source.TopicId.Value,
                SessionId = source.SessionId,
                PageKey = pageKey,
                PageType = "orkalm_source",
                Title = Clip(source.Title, 160),
                SourceReadiness = sourceBundle.EvidenceStatus,
                EvidenceStatus = sourceBundle.EvidenceStatus,
                SafeSummary = $"OrkaLM source notebook page for {Clip(source.Title, 120)}.",
                MetadataJson = Serialize(new
                {
                    sourceId = source.Id,
                    sourceTitle = source.Title,
                    sourceStatus = source.Status,
                    sourceChunkCount = source.ChunkCount,
                    sourceSurface = "orkalm_source"
                }),
                Status = StatusFor(sourceBundle.EvidenceStatus),
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.WikiPages.Add(page);
        }
        else
        {
            page.Title = Clip(source.Title, 160);
            page.SourceReadiness = sourceBundle.EvidenceStatus;
            page.EvidenceStatus = sourceBundle.EvidenceStatus;
            page.SafeSummary = $"OrkaLM source notebook page for {Clip(source.Title, 120)}.";
            page.MetadataJson = Serialize(new
            {
                sourceId = source.Id,
                sourceTitle = source.Title,
                sourceStatus = source.Status,
                sourceChunkCount = source.ChunkCount,
                sourceSurface = "orkalm_source"
            });
            page.Status = StatusFor(sourceBundle.EvidenceStatus);
            page.UpdatedAt = now;
        }

        await _db.SaveChangesAsync(ct);
        await EnsureSourceNoteTraceAsync(userId, source, page, sourceBundle, ct);
        return page;
    }

    private async Task EnsureSourceNoteTraceAsync(
        Guid userId,
        LearningSource source,
        WikiPage page,
        SourceEvidenceBundleDto sourceBundle,
        CancellationToken ct)
    {
        if (_wikiTraceWriter == null) return;
        try
        {
            await _wikiTraceWriter.RecordSourceNoteAsync(new WikiLearningTraceRequestDto
            {
                UserId = userId,
                TopicId = page.TopicId,
                SessionId = source.SessionId,
                ActiveWikiPageId = page.Id,
                SourceId = source.Id,
                SourceEvidenceBundleId = sourceBundle.EvidenceStatus is "source_grounded" or "mixed" ? sourceBundle.Id : null,
                TraceType = "source_note",
                Title = "OrkaLM source ready",
                SafeContent = $"Source notebook page prepared for {Clip(source.Title, 120)}. Indexed chunks: {source.ChunkCount}. Evidence status: {sourceBundle.EvidenceStatus}.",
                SourceBasis = sourceBundle.EvidenceStatus is "source_grounded" or "mixed" ? "source_grounded" : "evidence_insufficient",
                CreatedBy = "orkalm_source_notebook",
                Visibility = sourceBundle.EvidenceStatus is "source_grounded" or "mixed" ? "normal" : "highlighted",
                MetadataJson = Serialize(new { sourceId = source.Id, sourceSurface = "orkalm_source" })
            }, ct);
        }
        catch
        {
            // Source page creation must remain usable even if trace writing is unavailable.
        }
    }

    public async Task<LearningNotebookPackDto?> RefreshPackAsync(Guid userId, Guid packId, CancellationToken ct = default)
    {
        var pack = await _db.LearningNotebookPacks.FirstOrDefaultAsync(p => p.Id == packId && p.UserId == userId && !p.IsDeleted, ct);
        if (pack == null) return null;

        var sourceBundle = await _sourceLifecycle.GetLatestSourceEvidenceBundleAsync(userId, pack.TopicId, pack.SessionId, ct)
                           ?? await _sourceLifecycle.BuildSourceEvidenceBundleAsync(userId, pack.TopicId, pack.SessionId, pack.Title, ct);
        pack.SourceEvidenceBundleId = sourceBundle.Id;
        pack.SourceReadiness = sourceBundle.EvidenceStatus;
        pack.EvidenceStatus = sourceBundle.EvidenceStatus;
        pack.PackStatus = StatusFor(sourceBundle.EvidenceStatus);
        pack.WarningsJson = Serialize(ParseStrings(pack.WarningsJson).Concat(sourceBundle.Warnings).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToArray());
        pack.UpdatedAt = DateTime.UtcNow;

        foreach (var artifactId in ParseGuids(pack.ArtifactIdsJson))
        {
            await _artifacts.RefreshArtifactStatusAsync(userId, artifactId, "notebook pack refreshed", ct);
        }

        await _db.SaveChangesAsync(ct);
        return await ToDtoAsync(pack, includeArtifacts: true, ct);
    }

    public async Task<LearningArtifactDto?> BuildArtifactAsync(
        Guid userId,
        Guid packId,
        LearningNotebookArtifactRequestDto request,
        CancellationToken ct = default)
    {
        var pack = await _db.LearningNotebookPacks.FirstOrDefaultAsync(p => p.Id == packId && p.UserId == userId && !p.IsDeleted, ct);
        if (pack == null) return null;

        SourceEvidenceBundleDto? sourceBundle = null;
        if (pack.SourceEvidenceBundleId.HasValue)
        {
            var sourceBundleEntity = await _db.SourceEvidenceBundles.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == pack.SourceEvidenceBundleId && b.UserId == userId, ct);
            sourceBundle = sourceBundleEntity == null ? null : SourceEvidenceLifecycleProjection.ToDto(sourceBundleEntity);
        }
        sourceBundle ??= await _sourceLifecycle.GetLatestSourceEvidenceBundleAsync(userId, pack.TopicId, pack.SessionId, ct)
                         ?? await _sourceLifecycle.BuildSourceEvidenceBundleAsync(userId, pack.TopicId, pack.SessionId, pack.Title, ct);
        var notebook = await _sourceLifecycle.GetLatestWikiKnowledgeNotebookAsync(userId, pack.TopicId, ct)
                       ?? await _sourceLifecycle.BuildWikiKnowledgeNotebookAsync(userId, pack.TopicId, ct);
        var conceptState = new ConceptState(ParseStrings(pack.CompletedConceptKeysJson), ParseStrings(pack.WeakConceptKeysJson), ParseStrings(pack.MisconceptionKeysJson));
        var artifact = await BuildArtifactInternalAsync(userId, pack, request.ArtifactType, conceptState, sourceBundle, notebook, ct);
        if (artifact == null) return null;

        var ids = ParseGuids(pack.ArtifactIdsJson).Append(artifact.Id).Distinct().ToArray();
        pack.ArtifactIdsJson = Serialize(ids);
        pack.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await AppendArtifactTraceAsync(userId, pack, artifact, ct);
        return artifact;
    }

    private async Task AppendArtifactTraceAsync(
        Guid userId,
        LearningNotebookPack pack,
        LearningArtifactDto artifact,
        CancellationToken ct)
    {
        if (_wikiTraceWriter == null || !pack.WikiPageId.HasValue) return;

        try
        {
            var content = $"Artifact: {artifact.Title}\nType: {artifact.ArtifactType}\nStatus: {artifact.ArtifactStatus}\nSource basis: {artifact.SourceBasis}";
            await _wikiTraceWriter.RecordArtifactLinkAsync(new WikiLearningTraceRequestDto
            {
                UserId = userId,
                TopicId = pack.TopicId,
                SessionId = pack.SessionId,
                ActiveWikiPageId = pack.WikiPageId,
                ConceptKey = artifact.ConceptKey,
                LearningArtifactId = artifact.Id,
                SourceEvidenceBundleId = artifact.SourceBasis == "source_grounded" ? artifact.SourceEvidenceBundleId : null,
                TraceType = "artifact_link",
                Title = artifact.Title,
                SafeContent = content,
                SourceBasis = artifact.SourceBasis,
                CreatedBy = "notebook_studio",
                Visibility = "normal"
            }, ct);

            if (artifact.ArtifactType == "source_digest")
            {
                await _wikiTraceWriter.RecordSourceNoteAsync(new WikiLearningTraceRequestDto
                {
                    UserId = userId,
                    TopicId = pack.TopicId,
                    SessionId = pack.SessionId,
                    ActiveWikiPageId = pack.WikiPageId,
                    ConceptKey = artifact.ConceptKey,
                    LearningArtifactId = artifact.Id,
                    SourceEvidenceBundleId = artifact.SourceBasis == "source_grounded" ? artifact.SourceEvidenceBundleId : null,
                    TraceType = "source_note",
                    Title = "Source digest ready",
                    SafeContent = $"Source digest artifact is ready for this Wiki page: {artifact.Title}",
                    SourceBasis = artifact.SourceBasis == "source_grounded" ? "source_grounded" : "evidence_insufficient",
                    CreatedBy = "notebook_studio",
                    Visibility = artifact.SourceBasis == "source_grounded" ? "normal" : "highlighted"
                }, ct);
            }
        }
        catch
        {
            // Artifact creation must remain usable even if Wiki trace capture is unavailable.
        }
    }

    private async Task<LearningArtifactDto?> BuildArtifactInternalAsync(
        Guid userId,
        LearningNotebookPack pack,
        string artifactType,
        ConceptState conceptState,
        SourceEvidenceBundleDto sourceBundle,
        WikiKnowledgeNotebookDto notebook,
        CancellationToken ct)
    {
        var normalizedType = NormalizeArtifactType(artifactType);
        var wikiPage = ParseWikiPageMetadata(pack.SafeMetadataJson);
        var pageContext = await LoadWikiPageContextAsync(userId, pack.WikiPageId ?? wikiPage.WikiPageId, ct);
        var payload = await BuildArtifactPayloadAsync(userId, pack, normalizedType, conceptState, sourceBundle, notebook, pageContext, ct);
        var content = payload.SafeContent;
        var sourceBasis = SourceBasisFor(normalizedType, sourceBundle);
        var trust = await _trust.CheckPublicPayloadAsync(userId, new AgenticTrustCheckRequestDto
        {
            TopicId = pack.TopicId,
            SessionId = pack.SessionId,
            Surface = "notebook_artifact",
            Content = content
        }, ct);
        if (!trust.Allowed)
        {
            return null;
        }

        var request = new LearningArtifactRequestDto
        {
            TopicId = pack.TopicId,
            SessionId = pack.SessionId,
            ActiveLessonSnapshotId = pack.ActiveLessonSnapshotId,
            StudentContextSnapshotId = pack.StudentContextSnapshotId,
            PlanQualitySnapshotId = pack.PlanQualitySnapshotId,
            AssessmentQualitySnapshotId = pack.AssessmentQualitySnapshotId,
            SourceEvidenceBundleId = sourceBasis == "source_grounded" ? pack.SourceEvidenceBundleId : null,
            WikiNotebookSectionKey = (pack.WikiPageId ?? wikiPage.WikiPageId) is { } wikiPageId
                ? $"wiki_page:{wikiPageId:N}"
                : null,
            ConceptKey = conceptState.Weak.FirstOrDefault() ?? conceptState.Completed.FirstOrDefault(),
            ArtifactType = normalizedType,
            ArtifactStatus = pack.PackStatus == "ready" ? "ready" : "degraded",
            Origin = "notebook",
            RenderFormat = payload.RenderFormat,
            Title = TitleFor(normalizedType, pack.Title),
            SafeContent = content,
            ContentJson = payload.ContentJson,
            SourceBasis = sourceBasis,
            CitationIds = sourceBundle.EvidenceItems.Select(i => i.Label).Where(l => !string.IsNullOrWhiteSpace(l)).Take(8).ToArray(),
            Accessibility = new LearningArtifactAccessibilityDto
            {
                Status = "usable",
                Caption = TitleFor(normalizedType, pack.Title),
                Summary = $"{pack.Title} icin {normalizedType.Replace('_', ' ')}",
                TextFallback = content.Length > 600 ? content[..600] : content
            }
        };
        return await _artifacts.CreateArtifactAsync(userId, request, ct);
    }

    private async Task<ArtifactPayload> BuildArtifactPayloadAsync(
        Guid userId,
        LearningNotebookPack pack,
        string normalizedType,
        ConceptState conceptState,
        SourceEvidenceBundleDto sourceBundle,
        WikiKnowledgeNotebookDto notebook,
        WikiPageContext? pageContext,
        CancellationToken ct)
    {
        if (normalizedType == "audio_overview")
        {
            return await BuildAudioOverviewPayloadAsync(userId, pack, conceptState, sourceBundle, ct);
        }
        if (normalizedType == "audio_script")
        {
            return BuildAudioScriptPayload(pack, conceptState, sourceBundle);
        }
        if (normalizedType == "audio_transcript")
        {
            return BuildAudioTranscriptPayload(pack, conceptState, sourceBundle);
        }
        if (normalizedType == "caption_track")
        {
            return BuildCaptionTrackPayload(pack, conceptState, sourceBundle);
        }
        if (normalizedType == "mind_map")
        {
            return await BuildMindMapPayloadAsync(userId, pack, conceptState, sourceBundle, pageContext, ct);
        }
        if (normalizedType == "flashcard_set")
        {
            return await BuildFlashcardSetPayloadAsync(userId, pack, conceptState, pageContext, ct);
        }
        if (normalizedType == "review_quiz")
        {
            return await BuildReviewQuizPayloadAsync(userId, pack, conceptState, sourceBundle, pageContext, ct);
        }
        if (normalizedType == "slide_deck_outline")
        {
            return BuildSlideDeckOutlinePayload(pack, conceptState, sourceBundle, pageContext);
        }
        if (normalizedType == "video_ready_package")
        {
            return BuildVideoReadyPackagePayload(pack, conceptState, sourceBundle, pageContext);
        }
        if (normalizedType == "slide_export_manifest")
        {
            return BuildSlideExportManifestPayload(pack, conceptState, sourceBundle, pageContext);
        }
        if (normalizedType == "narration_script")
        {
            return BuildNarrationScriptPayload(pack, conceptState, sourceBundle, pageContext);
        }
        if (normalizedType == "visual_instruction_set")
        {
            return BuildVisualInstructionSetPayload(pack, conceptState, sourceBundle, pageContext);
        }
        if (normalizedType == "media_accessibility_note")
        {
            return BuildMediaAccessibilityNotePayload(pack, conceptState, sourceBundle, pageContext);
        }

        return new ArtifactPayload(BuildArtifactContent(pack, normalizedType, conceptState, sourceBundle, notebook, pageContext), null, "markdown");
    }

    private async Task<ConceptState> BuildConceptStateAsync(
        Guid userId,
        Guid topicId,
        ActiveLessonSnapshotDto? activeSnapshot,
        StudentContextSnapshotDto? studentSnapshot,
        WikiPageContext? pageContext,
        CancellationToken ct)
    {
        var completed = studentSnapshot?.StrongConcepts.Select(c => c.ConceptKey).Where(NotBlank).Take(8).ToList() ?? [];
        var weak = studentSnapshot?.WeakConcepts.Select(c => c.ConceptKey).Where(NotBlank).Take(8).ToList() ?? [];
        var misconceptions = studentSnapshot?.RecentMisconceptions.Select(c => c.ConceptKey).Where(NotBlank).Take(8).ToList() ?? [];

        if (NotBlank(pageContext?.ConceptKey))
        {
            completed.Add(pageContext!.ConceptKey!);
        }
        completed.AddRange(pageContext?.BlockConceptKeys ?? Array.Empty<string>());
        misconceptions.AddRange(pageContext?.MisconceptionKeys ?? Array.Empty<string>());
        if (pageContext?.RepairNoteCount > 0 || pageContext?.QuestionSnippets.Count > 0)
        {
            weak.AddRange((pageContext.BlockConceptKeys.Count > 0 ? pageContext.BlockConceptKeys : [pageContext.ConceptKey ?? "wiki-page-review"])
                .Where(NotBlank));
        }

        var masteries = await _db.ConceptMasteries.AsNoTracking()
            .Where(m => m.UserId == userId && m.TopicId == topicId)
            .OrderByDescending(m => m.UpdatedAt)
            .Take(20)
            .ToListAsync(ct);
        completed.AddRange(masteries.Where(m => m.MasteryScore >= 0.65m).Select(m => m.ConceptKey));
        weak.AddRange(masteries.Where(m => m.MasteryScore < 0.55m || m.RemediationNeed != "none").Select(m => m.ConceptKey));

        var graphConcepts = await LoadLatestConceptKeysAsync(userId, topicId, ct);
        if (completed.Count == 0 && graphConcepts.Count > 0)
        {
            completed.AddRange(graphConcepts.Take(3));
        }
        if (weak.Count == 0 && NotBlank(activeSnapshot?.ActiveConceptKey))
        {
            weak.Add(activeSnapshot!.ActiveConceptKey!);
        }
        if (weak.Count == 0 && graphConcepts.Count > 3)
        {
            weak.AddRange(graphConcepts.Skip(3).Take(2));
        }

        return new ConceptState(
            completed.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray(),
            weak.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray(),
            misconceptions.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray());
    }

    private async Task<WikiPageContext?> LoadWikiPageContextAsync(Guid userId, Guid? wikiPageId, CancellationToken ct)
    {
        if (!wikiPageId.HasValue) return null;

        var page = await _db.WikiPages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == wikiPageId.Value && p.UserId == userId && !p.IsDeleted, ct);
        if (page == null) return null;

        var blocks = await _db.WikiBlocks.AsNoTracking()
            .Where(b => b.WikiPageId == page.Id && !b.IsDeleted)
            .OrderBy(b => b.OrderIndex)
            .ThenBy(b => b.CreatedAt)
            .Take(80)
            .ToListAsync(ct);
        var curation = WikiAutoCurationService.BuildSummary(page, blocks);

        var snippets = blocks
            .Where(b => !string.IsNullOrWhiteSpace(b.Content))
            .Select(b => $"{b.BlockType}: {Clip(b.Content, 260)}")
            .Take(12)
            .ToArray();
        var questions = blocks
            .Where(b => b.BlockType == WikiBlockType.StudentQuestion)
            .Select(b => Clip(b.Content, 240))
            .Where(NotBlank)
            .Take(6)
            .ToArray();
        var concepts = blocks
            .Select(b => b.ConceptKey)
            .Append(page.ConceptKey)
            .Where(NotBlank)
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
        var misconceptions = blocks
            .Select(b => b.MisconceptionKey)
            .Where(NotBlank)
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

        return new WikiPageContext(
            page.Id,
            page.TopicId,
            page.Title,
            page.PageKey,
            page.PageType,
            page.ConceptKey,
            page.SourceReadiness,
            page.EvidenceStatus,
            page.SafeSummary,
            snippets,
            questions,
            concepts,
            misconceptions,
            blocks.Count(b => b.BlockType == WikiBlockType.RepairNote || b.BlockType == WikiBlockType.MisconceptionNote),
            curation);
    }

    private async Task<IReadOnlyList<string>> LoadLatestConceptKeysAsync(Guid userId, Guid topicId, CancellationToken ct)
    {
        var graphId = await _db.ConceptGraphSnapshots.AsNoTracking()
            .Where(g => g.UserId == userId && g.TopicId == topicId)
            .OrderByDescending(g => g.CreatedAt)
            .Select(g => (Guid?)g.Id)
            .FirstOrDefaultAsync(ct);
        if (!graphId.HasValue) return Array.Empty<string>();

        return await _db.LearningConcepts.AsNoTracking()
            .Where(c => c.ConceptGraphSnapshotId == graphId.Value)
            .OrderBy(c => c.Order)
            .Select(c => c.StableKey)
            .Take(16)
            .ToListAsync(ct);
    }

    private async Task<ArtifactPayload> BuildMindMapPayloadAsync(
        Guid userId,
        LearningNotebookPack pack,
        ConceptState conceptState,
        SourceEvidenceBundleDto sourceBundle,
        WikiPageContext? pageContext,
        CancellationToken ct)
    {
        var graphId = await _db.ConceptGraphSnapshots.AsNoTracking()
            .Where(g => g.UserId == userId && g.TopicId == pack.TopicId)
            .OrderByDescending(g => g.CreatedAt)
            .Select(g => (Guid?)g.Id)
            .FirstOrDefaultAsync(ct);
        if (!graphId.HasValue)
        {
            var fallback = "graph TD\nA[Notebook pack] --> B[Concept graph henuz hazir degil]\nA --> C[Once plan veya diagnostic olustur]";
            var fallbackJson = Serialize(new
            {
                format = "concept_graph_mind_map_v1",
                status = "fallback",
                source = "concept_graph_missing",
                sourceReadiness = sourceBundle.EvidenceStatus,
                nodes = Array.Empty<object>(),
                edges = Array.Empty<object>(),
                warnings = new[] { "Concept graph henuz hazir degil; mind map dusuk guvenli fallback olarak uretildi." }
            });
            return new ArtifactPayload(fallback, fallbackJson, "mermaid");
        }

        var focusConceptKeys = PageFocusConceptKeys(pageContext);
        var concepts = await _db.LearningConcepts.AsNoTracking()
            .Where(c => c.ConceptGraphSnapshotId == graphId.Value)
            .OrderBy(c => c.Order)
            .ToListAsync(ct);
        if (focusConceptKeys.Count > 0)
        {
            concepts = concepts
                .OrderByDescending(c => focusConceptKeys.Contains(c.StableKey))
                .ThenBy(c => c.Order)
                .Take(16)
                .ToList();
        }
        else
        {
            concepts = concepts.Take(16).ToList();
        }
        if (concepts.Count == 0)
        {
            var empty = "graph TD\nA[Notebook pack] --> B[Concept bulunamadi]";
            var emptyJson = Serialize(new
            {
                format = "concept_graph_mind_map_v1",
                status = "fallback",
                source = "concept_graph_empty",
                sourceReadiness = sourceBundle.EvidenceStatus,
                nodes = Array.Empty<object>(),
                edges = Array.Empty<object>(),
                warnings = new[] { "Concept graph snapshot var ama concept dugumu yok." }
            });
            return new ArtifactPayload(empty, emptyJson, "mermaid");
        }

        var conceptKeys = concepts.Select(c => c.StableKey)
            .Where(NotBlank)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var masteries = await _db.ConceptMasteries.AsNoTracking()
            .Where(m => m.UserId == userId && m.TopicId == pack.TopicId && conceptKeys.Contains(m.ConceptKey))
            .ToListAsync(ct);
        var masteryByKey = masteries
            .GroupBy(m => m.ConceptKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.UpdatedAt).First(), StringComparer.OrdinalIgnoreCase);
        var artifactLinks = await _db.LearningArtifacts.AsNoTracking()
            .Where(a => a.UserId == userId &&
                        a.TopicId == pack.TopicId &&
                        !a.IsDeleted &&
                        a.ConceptKey != null &&
                        conceptKeys.Contains(a.ConceptKey))
            .OrderByDescending(a => a.UpdatedAt)
            .Take(80)
            .Select(a => new { a.Id, a.ConceptKey, a.ArtifactType })
            .ToListAsync(ct);
        var artifactIdsByConcept = artifactLinks
            .GroupBy(a => a.ConceptKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Take(6).Select(a => a.Id).ToArray(), StringComparer.OrdinalIgnoreCase);

        var nodeIds = concepts
            .Select((concept, index) => new { concept.StableKey, NodeId = $"N{index}" })
            .ToDictionary(x => x.StableKey, x => x.NodeId, StringComparer.OrdinalIgnoreCase);
        var relations = await _db.ConceptRelations.AsNoTracking()
            .Where(r => r.ConceptGraphSnapshotId == graphId.Value &&
                        conceptKeys.Contains(r.SourceConceptKey) &&
                        conceptKeys.Contains(r.TargetConceptKey))
            .OrderByDescending(r => r.Weight)
            .Take(32)
            .ToListAsync(ct);

        var lines = new List<string> { "graph TD" };
        if (pageContext != null)
        {
            lines.Add($"P0[{EscapeMermaid(pageContext.Title)} - Wiki sayfasi]");
        }
        var nodes = new List<object>();
        foreach (var (concept, index) in concepts.Select((concept, index) => (concept, index)))
        {
            var nodeId = $"N{index}";
            var status = NodeStateFor(concept.StableKey, conceptState);
            var mastery = masteryByKey.TryGetValue(concept.StableKey, out var m) ? m : null;
            var masteryStatus = MasteryStatusFor(mastery);
            var hasMisconception = conceptState.Misconceptions.Contains(concept.StableKey, StringComparer.OrdinalIgnoreCase) ||
                                   HasMisconceptionEvidence(mastery);
            var isWikiPageFocus = focusConceptKeys.Contains(concept.StableKey);
            lines.Add($"{nodeId}[{EscapeMermaid(concept.Label)} - {status}]");
            if (pageContext != null && isWikiPageFocus)
            {
                lines.Add($"P0 --> {nodeId}");
            }
            nodes.Add(new
            {
                nodeId,
                conceptKey = concept.StableKey,
                conceptLabel = concept.Label,
                wikiPageId = isWikiPageFocus ? pageContext?.Id : null,
                wikiPageTitle = isWikiPageFocus ? pageContext?.Title : null,
                isWikiPageFocus,
                order = concept.Order,
                difficultyBand = concept.DifficultyBand,
                learningState = status,
                masteryStatus,
                masteryScore = mastery?.MasteryScore,
                remediationNeed = mastery?.RemediationNeed ?? "unknown",
                sourceReadiness = sourceBundle.EvidenceStatus,
                misconceptionIndicator = hasMisconception,
                relatedArtifactIds = artifactIdsByConcept.TryGetValue(concept.StableKey, out var ids) ? ids : Array.Empty<Guid>(),
                quizHook = QuizHookFor(status, hasMisconception),
                tutorAction = status == "zayif" ? "misconception_repair" : "socratic_check"
            });
        }

        var edges = new List<object>();
        if (relations.Count > 0)
        {
            foreach (var relation in relations)
            {
                lines.Add($"{nodeIds[relation.SourceConceptKey]} --> {nodeIds[relation.TargetConceptKey]}");
                edges.Add(new
                {
                    sourceConceptKey = relation.SourceConceptKey,
                    targetConceptKey = relation.TargetConceptKey,
                    relationType = relation.RelationType,
                    relation.Weight
                });
            }
        }
        else
        {
            for (var i = 1; i < concepts.Count; i++)
            {
                lines.Add($"N{i - 1} --> N{i}");
                edges.Add(new
                {
                    sourceConceptKey = concepts[i - 1].StableKey,
                    targetConceptKey = concepts[i].StableKey,
                    relationType = "sequence_fallback",
                    weight = 0.4
                });
            }
        }

        var contentJson = Serialize(new
        {
            format = "concept_graph_mind_map_v1",
            status = "ready",
            source = "ConceptGraphSnapshot",
            conceptGraphSnapshotId = graphId.Value,
            wikiPageId = pageContext?.Id,
            wikiPageTitle = pageContext?.Title,
            pageScoped = pageContext != null,
            focusConceptKeys,
            sourceReadiness = sourceBundle.EvidenceStatus,
            evidenceStatus = sourceBundle.EvidenceStatus,
            nodes,
            edges,
            warnings = relations.Count == 0
                ? new[] { "ConceptRelation bulunamadi; mind map sira tabanli fallback baglantilar kullandi." }
                : Array.Empty<string>()
        });
        return new ArtifactPayload(string.Join('\n', lines), contentJson, "mermaid");
    }

    private async Task<ArtifactPayload> BuildAudioOverviewPayloadAsync(
        Guid userId,
        LearningNotebookPack pack,
        ConceptState conceptState,
        SourceEvidenceBundleDto sourceBundle,
        CancellationToken ct)
    {
        var scriptPayload = BuildAudioScriptPayload(pack, conceptState, sourceBundle);
        var script = ExtractScript(scriptPayload.SafeContent);
        var trust = await _trust.CheckPublicPayloadAsync(userId, new AgenticTrustCheckRequestDto
        {
            TopicId = pack.TopicId,
            SessionId = pack.SessionId,
            Surface = "notebook_audio_script",
            Content = script
        }, ct);
        if (!trust.Allowed)
        {
            return scriptPayload;
        }

        var job = new AudioOverviewJob
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = pack.TopicId,
            SessionId = pack.SessionId,
            Status = "generating",
            Script = script,
            SpeakersJson = JsonSerializer.Serialize(AudioDialogueFormatter.ParseSpeakers(script), JsonOptions),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.AudioOverviewJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        try
        {
            using var ttsTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            ttsTimeout.CancelAfter(NotebookTtsTimeout);
            var audioBytes = await _tts.SynthesizeDialogueAsync(script, ttsTimeout.Token);
            if (audioBytes.Length == 0)
            {
                throw new InvalidOperationException("Edge-TTS returned empty audio.");
            }

            job.AudioBytes = audioBytes;
            job.AudioByteLength = audioBytes.LongLength;
            job.AudioExpiresAt = DateTime.UtcNow.AddDays(7);
            job.AudioPurgedAt = null;
            job.ContentType = "audio/mpeg";
            job.Status = "ready";
            job.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            var content = BuildAudioOverviewContent(job.Id, script, job.Status, sourceBundle.EvidenceStatus);
            var contentJson = Serialize(new
            {
                job.Status,
                audioOverviewJobId = job.Id,
                contentType = job.ContentType,
                fileName = BuildAudioFileName(job.Id, job.ContentType),
                downloadUrl = $"/api/audio/overview/{job.Id}/stream",
                fallbackReason = (string?)null,
                speakers = AudioDialogueFormatter.ParseSpeakers(script),
                transcriptArtifact = true
            });
            return new ArtifactPayload(content, contentJson, "markdown");
        }
        catch (Exception ex)
        {
            job.Status = "script-only";
            job.ErrorMessage = "Edge-TTS uretilemedi; Notebook Studio transcript artifact ve frontend TTS fallback kullanmali.";
            job.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);

            var content = BuildAudioOverviewContent(job.Id, script, job.Status, sourceBundle.EvidenceStatus);
            var contentJson = Serialize(new
            {
                audioOverviewJobId = job.Id,
                job.Status,
                contentType = (string?)null,
                fileName = (string?)null,
                downloadUrl = (string?)null,
                fallbackReason = "tts_unavailable_script_only",
                safeErrorCode = ex.GetType().Name,
                speakers = AudioDialogueFormatter.ParseSpeakers(script),
                transcriptArtifact = true
            });
            return new ArtifactPayload(content, contentJson, "markdown");
        }
    }

    private static ArtifactPayload BuildAudioScriptPayload(
        LearningNotebookPack pack,
        ConceptState conceptState,
        SourceEvidenceBundleDto? sourceBundle)
    {
        var completed = JoinOrFallback(conceptState.Completed, "tamamlanan concept sinyali henuz zayif");
        var weak = JoinOrFallback(conceptState.Weak, "zayif concept sinyali henuz yok");
        var evidence = sourceBundle?.EvidenceStatus ?? pack.EvidenceStatus;
        var script = AudioDialogueFormatter.NormalizeScript($"""
            [HOCA]: {pack.Title} icin kisa bir Notebook Studio sesli tekrar metni hazirliyoruz.
            [ASISTAN]: Once tamamlanan kavramlari toparlayalim: {completed}.
            [HOCA]: Sonra dikkat isteyen alanlara gelelim: {weak}.
            [ASISTAN]: Kaynak durumu {evidence}. Kaynak zemini sinirliyse bunu kesin kaynakli anlatim gibi sunmayacagiz.
            [HOCA]: Kapanista bir mini kontrol sorusu onerelim ve pasif dinleme yerine aktif hatirlama yapalim.
            """);
        var content = $"# Audio script\n\n{script}\n\nNot: Bu transcript LearningArtifact olarak saklanir; ham kaynak, prompt veya provider payload icermez.";
        var contentJson = Serialize(new
        {
            status = "script_ready",
            speakers = AudioDialogueFormatter.ParseSpeakers(script),
            evidenceStatus = evidence,
            transcriptArtifact = true,
            audioJobCreated = false
        });
        return new ArtifactPayload(content, contentJson, "markdown");
    }

    private static ArtifactPayload BuildAudioTranscriptPayload(
        LearningNotebookPack pack,
        ConceptState conceptState,
        SourceEvidenceBundleDto? sourceBundle)
    {
        var scriptPayload = BuildAudioScriptPayload(pack, conceptState, sourceBundle);
        var script = ExtractScript(scriptPayload.SafeContent);
        var evidence = sourceBundle?.EvidenceStatus ?? pack.EvidenceStatus;
        var content = "# Audio transcript\n\n" +
                      $"{script}\n\n" +
                      $"Kaynak durumu: {evidence}\n\n" +
                      "Transcript notu: Bu metin pack-aware audio icin guvenli text fallback'tir; ham kaynak, prompt veya provider payload icermez.";
        var contentJson = Serialize(new
        {
            format = "audio_transcript_v1",
            status = "transcript_ready",
            sourceReadiness = evidence,
            sourceBasis = MediaSourceBasisFor(evidence),
            speakers = AudioDialogueFormatter.ParseSpeakers(script),
            transcriptAvailable = true,
            captionTrackRecommended = true,
            accessibility = new
            {
                textFallback = true,
                speakerLabels = true,
                canUseBrowserTtsFallback = true
            }
        });
        return new ArtifactPayload(content, contentJson, "markdown");
    }

    private static ArtifactPayload BuildCaptionTrackPayload(
        LearningNotebookPack pack,
        ConceptState conceptState,
        SourceEvidenceBundleDto? sourceBundle)
    {
        var script = ExtractScript(BuildAudioScriptPayload(pack, conceptState, sourceBundle).SafeContent);
        var segments = AudioDialogueFormatter.ParseSegments(script).Take(12).ToArray();
        var cues = segments.Select((segment, index) => new
        {
            cueId = index + 1,
            speaker = segment.Speaker,
            text = Clip(segment.Text, 260),
            timingHint = $"segment_{index + 1}"
        }).ToArray();
        var evidence = sourceBundle?.EvidenceStatus ?? pack.EvidenceStatus;
        var content = "WEBVTT\n\n" + string.Join("\n\n", cues.Select(c =>
            $"{c.cueId}\nNOTE {c.speaker} - {c.timingHint}\n{c.text}")) +
            "\n\nNOTE Bu caption track deterministik text fallback'tir; henuz medya zaman kodu degildir.";
        var contentJson = Serialize(new
        {
            format = "caption_track_v1",
            status = "caption_outline_ready",
            sourceReadiness = evidence,
            timingMode = "segment_hint_only",
            exportReadiness = "needs_review",
            cues,
            warnings = new[] { "Gercek audio/video zaman kodu yok; export oncesi medya timeline ile eslestirilmeli." }
        });
        return new ArtifactPayload(content, contentJson, "plain_text");
    }

    private static string BuildAudioOverviewContent(Guid jobId, string script, string status, string evidenceStatus) =>
        $"# Audio overview\n\nAudio overview job: {jobId}\n\n{script}\n\nDurum: {status}\n\nKaynak durumu: {evidenceStatus}\n\nNot: Bu artifact transcript ve job metadata tasir; ham kaynak, prompt veya provider payload icermez.";

    private static string ExtractScript(string safeContent)
    {
        var marker = "# Audio script";
        var text = safeContent.Replace(marker, string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        var noteIndex = text.IndexOf("\n\nNot:", StringComparison.OrdinalIgnoreCase);
        if (noteIndex >= 0) text = text[..noteIndex].Trim();
        return AudioDialogueFormatter.NormalizeScript(text);
    }

    private static string BuildAudioFileName(Guid id, string? contentType)
    {
        var ext = (contentType ?? "audio/mpeg").Contains("wav", StringComparison.OrdinalIgnoreCase) ? "wav" : "mp3";
        return $"orka-audio-overview-{id}.{ext}";
    }

    private static ArtifactPayload BuildSlideDeckOutlinePayload(
        LearningNotebookPack pack,
        ConceptState conceptState,
        SourceEvidenceBundleDto sourceBundle,
        WikiPageContext? pageContext)
    {
        var completed = conceptState.Completed.Take(4).DefaultIfEmpty("tamamlanan concept sinyali zayif").ToArray();
        var weak = conceptState.Weak.Take(4).DefaultIfEmpty("zayif concept sinyali yok").ToArray();
        var citationLabel = sourceBundle.EvidenceStatus is "source_grounded" or "mixed"
            ? sourceBundle.EvidenceItems.Select(i => i.Label).FirstOrDefault(NotBlank)
            : null;
        var misconceptionWarning = conceptState.Misconceptions.Count > 0
            ? $"Dikkat: {string.Join(", ", conceptState.Misconceptions.Take(3))} sinyali onarimla ele alinmali."
            : "Belirgin misconception sinyali yok; yine de checkpoint sorusu kullan.";
        var pageTitle = pageContext?.Title ?? pack.Title;
        var pageSummary = string.IsNullOrWhiteSpace(pageContext?.SafeSummary)
            ? "Sayfa ozeti henuz zayif; Tutor anlatimi gozlenen concept ve pack sinyaliyle sinirli tutmali."
            : pageContext!.SafeSummary!;
        var pageNotes = pageContext?.BlockSnippets.Count > 0
            ? pageContext.BlockSnippets.Take(4).ToArray()
            : ["Sayfa notu henuz zayif; once Wiki notu veya Tutor aciklamasi eklenmeli."];
        var pageQuestions = pageContext?.QuestionSnippets.Count > 0
            ? pageContext.QuestionSnippets.Take(3).ToArray()
            : ["Ogrenci sorusu henuz yakalanmamis; checkpoint ile aktif hatirlama yap."];
        var sourceBasis = sourceBundle.EvidenceStatus is "source_grounded" or "mixed" ? "source_grounded" : "model_assisted";
        var sourceWarning = sourceBasis == "source_grounded"
            ? "Kaynak etiketi kullanilabilir; yine de ham kaynak chunk'i slayta koyma."
            : "Kaynak zemini sinirli; slayt source-backed gibi sunulmamali.";

        var slides = new[]
        {
            new
            {
                order = 1,
                title = pageContext == null ? "Milestone resmi" : "Wiki sayfasi resmi",
                bullets = new[] { $"Paket: {pack.Title}", $"Sayfa: {pageTitle}", $"Kaynak durumu: {sourceBundle.EvidenceStatus}", "Amac: pasif tekrar degil, aktif hatirlama." },
                speakerNotes = $"Ogrenciye bu paketin hangi sayfa/asamayi kapattigini ve kaynak guvenini net soyle. {sourceWarning}",
                sourceLabel = citationLabel,
                visualSuggestion = pageContext == null ? "Concept graph uzerinden kisa yol haritasi" : "Wiki sayfasi root node + concept graph focus node",
                checkpointQuestion = pageContext == null ? "Bu milestone'u bir cumlede nasil ozetlersin?" : $"{pageTitle} sayfasini bir cumlede nasil ozetlersin?",
                misconceptionWarning = (string?)null,
                nextAction = "study_guide",
                accessibilitySummary = "Baslik, kaynak durumu ve amac tek ekranda okunur."
            },
            new
            {
                order = 2,
                title = "Sayfa notlari ve kavramlar",
                bullets = pageNotes.Concat(completed.Select(c => $"Tamamlanan: {c}")).Take(6).ToArray(),
                speakerNotes = "Her kavram icin once Wiki notundan kisa dayanak ver, sonra ogrenciden kendi cumlesiyle tekrar isteme hazirligi yap.",
                sourceLabel = citationLabel,
                visualSuggestion = "Tablo: kavram / ornek / kontrol sorusu",
                checkpointQuestion = "Bu kavramlardan birini yeni bir ornekte kullanabilir misin?",
                misconceptionWarning = (string?)null,
                nextAction = "retrieval_prompt",
                accessibilitySummary = "Liste maddeleri kisa tutulur; tablo onerisi text fallback ile verilir."
            },
            new
            {
                order = 3,
                title = "Takilma ve onarim",
                bullets = pageQuestions.Concat(weak.Select(c => $"Onarim hedefi: {c}")).Take(6).ToArray(),
                speakerNotes = "Ogrenci sorusunu yargilamadan tekrar cercevele; zayif alanda once ipucu ver, sonra mikro kontrol sorusu sor.",
                sourceLabel = citationLabel,
                visualSuggestion = "Prerequisite bridge veya worked example",
                checkpointQuestion = "Bu kavramda en cok hangi adim karisiyor?",
                misconceptionWarning = (string?)misconceptionWarning,
                nextAction = "misconception_repair_pack",
                accessibilitySummary = "Misconception uyarisi net ama kesin tani gibi degil."
            },
            new
            {
                order = 4,
                title = "Kapanis ve sonraki adim",
                bullets = new[] { "Kisa review quiz baslat.", "Yanlis cevap varsa repair pack'e don.", "Hazirsa siradaki plan adimina gec." },
                speakerNotes = "Basari garantisi veya resmi kapsam iddiasi kurma; sadece gozlenen learning state'e dayan.",
                sourceLabel = citationLabel,
                visualSuggestion = "Next action card",
                checkpointQuestion = "Siradaki adima gecmeden once hangi tek soruyu cozmelisin?",
                misconceptionWarning = (string?)null,
                nextAction = "review_quiz",
                accessibilitySummary = "Sonraki aksiyonlar kisa ve klavye ile secilebilir olmali."
            }
        };

        var content = "# Slide deck outline\n\n" +
                      (pageContext == null ? string.Empty : $"Wiki sayfasi: {pageTitle}\n\n") +
                      $"Sayfa ozeti: {pageSummary}\n\n" +
                      $"Kaynak durumu: {sourceBundle.EvidenceStatus}\n\n" +
                      string.Join("\n\n", slides.Select(s =>
                          $"## {s.order}. {s.title}\n\n" +
                          string.Join('\n', s.bullets.Select(b => $"- {b}")) +
                          $"\n\nSpeaker note: {s.speakerNotes}\n\nCheckpoint: {s.checkpointQuestion}\n\nAccessibility: {s.accessibilitySummary}")) +
                      "\n\nNot: Bu taslak ogretici tekrar icindir; resmi mufredat/sinav kapsami veya basari garantisi iddia etmez.";
        var contentJson = Serialize(new
        {
            format = "slide_deck_outline_v1",
            wikiPageId = pageContext?.Id,
            wikiPageTitle = pageContext?.Title,
            pageScoped = pageContext != null,
            sourceReadiness = sourceBundle.EvidenceStatus,
            sourceBasis,
            sourceWarning,
            misconceptionKeys = conceptState.Misconceptions.Take(8).ToArray(),
            completedConceptKeys = conceptState.Completed.Take(8).ToArray(),
            weakConceptKeys = conceptState.Weak.Take(8).ToArray(),
            exportReadiness = "outline_only",
            videoReadyPackage = new
            {
                slideSequence = true,
                narrationScript = false,
                visualInstructions = true,
                timingHints = false,
                transcript = false,
                accessibilityCaption = true,
                sourceBasis
            },
            accessibility = new
            {
                textFallback = true,
                speakerNotes = true,
                checkpointQuestions = true,
                perSlideSummary = true
            },
            slides
        });
        return new ArtifactPayload(content, contentJson, "markdown");
    }

    private static ArtifactPayload BuildVideoReadyPackagePayload(
        LearningNotebookPack pack,
        ConceptState conceptState,
        SourceEvidenceBundleDto sourceBundle,
        WikiPageContext? pageContext)
    {
        var sourceBasis = MediaSourceBasisFor(sourceBundle.EvidenceStatus);
        var pageTitle = pageContext?.Title ?? pack.Title;
        var completed = conceptState.Completed.Take(5).ToArray();
        var weak = conceptState.Weak.Take(5).ToArray();
        var warnings = BuildMediaWarnings(sourceBundle, "Video uretilmedi; bu paket yalnizca video-ready ders taslagidir.");
        var scenes = new[]
        {
            new
            {
                id = "scene-1",
                title = "Konu cercevesi",
                narrationHint = $"{pageTitle} icin pack hedefini ve kaynak durumunu ac.",
                visualInstruction = pageContext == null ? "Milestone baslik karti + concept graph ipucu" : "Wiki sayfasi basligi + odak concept node",
                timingHintSeconds = 35,
                captionHint = "Bu bolumde neyi tekrar ediyoruz?",
                sourceLabel = SafeCitationLabel(sourceBundle)
            },
            new
            {
                id = "scene-2",
                title = "Kavramlar",
                narrationHint = $"Tamamlanan kavramlari bagla: {JoinOrFallback(completed, "sinyal zayif")}.",
                visualInstruction = "Kavram / ornek / kontrol sorusu tablosu",
                timingHintSeconds = 55,
                captionHint = "Ana kavramlar ve kisa ornekler",
                sourceLabel = SafeCitationLabel(sourceBundle)
            },
            new
            {
                id = "scene-3",
                title = "Onarim",
                narrationHint = $"Dikkat isteyen alanlari isaretle: {JoinOrFallback(weak, "zayif sinyal yok")}.",
                visualInstruction = "Yanlis anlama -> ipucu -> mikro kontrol akisi",
                timingHintSeconds = 45,
                captionHint = "Takilma noktasi ve aktif kontrol",
                sourceLabel = SafeCitationLabel(sourceBundle)
            },
            new
            {
                id = "scene-4",
                title = "Aktif tekrar",
                narrationHint = "Flashcard veya review quiz ile pasif izlemeyi aktif hatirlamaya cevir.",
                visualInstruction = "Next action card",
                timingHintSeconds = 30,
                captionHint = "Siradaki en kucuk kontrol adimi",
                sourceLabel = (string?)null
            }
        };

        var content = "# Video-ready package\n\n" +
                      "Bu artifact video dosyasi uretmez; ileride guvenli video pipeline'i icin ders taslagi verir.\n\n" +
                      $"Baslik: {pageTitle}\n\nKaynak zemini: {sourceBundle.EvidenceStatus}\n\n" +
                      string.Join("\n\n", scenes.Select(s =>
                          $"## {s.title}\n\n- Narration: {s.narrationHint}\n- Visual: {s.visualInstruction}\n- Caption: {s.captionHint}\n- Timing hint: {s.timingHintSeconds}s")) +
                      "\n\nExport readiness: outline_ready. Gercek video uretimi bu fazda yok.";
        var contentJson = Serialize(new
        {
            format = "video_ready_package_v1",
            status = "outline_ready",
            title = pageTitle,
            sourceBasis,
            sourceReadiness = sourceBundle.EvidenceStatus,
            wikiPageId = pageContext?.Id,
            wikiPageTitle = pageContext?.Title,
            slideSequenceIds = scenes.Select(s => s.id).ToArray(),
            narrationScriptOutline = scenes.Select(s => new { s.id, s.narrationHint }).ToArray(),
            visualInstructionSet = scenes.Select(s => new { s.id, s.visualInstruction }).ToArray(),
            timingHints = scenes.Select(s => new { s.id, s.timingHintSeconds }).ToArray(),
            captionOutline = scenes.Select(s => new { s.id, s.captionHint }).ToArray(),
            accessibilityNotes = new[] { "Transcript ve caption outline hazir.", "Visual instruction text fallback ile verildi." },
            sourceLabels = sourceBundle.EvidenceItems.Select(i => i.Label).Where(NotBlank).Take(6).ToArray(),
            warnings,
            exportReadiness = "outline_ready",
            generatedVideo = false,
            videoProvider = (string?)null
        });
        return new ArtifactPayload(content, contentJson, "markdown");
    }

    private static ArtifactPayload BuildSlideExportManifestPayload(
        LearningNotebookPack pack,
        ConceptState conceptState,
        SourceEvidenceBundleDto sourceBundle,
        WikiPageContext? pageContext)
    {
        var pageTitle = pageContext?.Title ?? pack.Title;
        var sourceBasis = MediaSourceBasisFor(sourceBundle.EvidenceStatus);
        var slides = new[]
        {
            new { id = "slide-1", title = pageContext == null ? "Milestone resmi" : "Wiki sayfasi resmi", layoutHint = "title_status", hasSpeakerNotes = true, hasCheckpoint = true },
            new { id = "slide-2", title = "Sayfa notlari ve kavramlar", layoutHint = "bullets_table_hint", hasSpeakerNotes = true, hasCheckpoint = true },
            new { id = "slide-3", title = "Takilma ve onarim", layoutHint = "repair_flow", hasSpeakerNotes = true, hasCheckpoint = true },
            new { id = "slide-4", title = "Kapanis ve sonraki adim", layoutHint = "next_action", hasSpeakerNotes = true, hasCheckpoint = true }
        };
        var warnings = BuildMediaWarnings(sourceBundle, "PPTX export etkin degil; bu manifest yalnizca export seam tasir.");
        var content = "# Slide export manifest\n\n" +
                      $"Deck: {pageTitle}\n\n" +
                      $"Slide sayisi: {slides.Length}\n\n" +
                      $"Export readiness: pptx_not_enabled\n\n" +
                      string.Join("\n", slides.Select(s => $"- {s.id}: {s.title} ({s.layoutHint})")) +
                      "\n\nNot: Bu manifest PPTX dosyasi degildir; future deterministic export icin guvenli yapi tasir.";
        var contentJson = Serialize(new
        {
            format = "slide_export_manifest_v1",
            deckTitle = pageTitle,
            slideCount = slides.Length,
            sourceBasis,
            sourceReadiness = sourceBundle.EvidenceStatus,
            wikiPageId = pageContext?.Id,
            wikiPageTitle = pageContext?.Title,
            slides,
            citationLabels = sourceBundle.EvidenceItems.Select(i => i.Label).Where(NotBlank).Take(6).ToArray(),
            diagramTableFormulaHints = new[] { "concept_graph", "concept_example_table", "checkpoint_question" },
            accessibilitySummary = "Tum slaytlar speaker note, checkpoint ve text fallback tasimali.",
            exportReadiness = "pptx_not_enabled",
            pptxGenerated = false,
            warnings
        });
        return new ArtifactPayload(content, contentJson, "markdown");
    }

    private static ArtifactPayload BuildNarrationScriptPayload(
        LearningNotebookPack pack,
        ConceptState conceptState,
        SourceEvidenceBundleDto sourceBundle,
        WikiPageContext? pageContext)
    {
        var pageTitle = pageContext?.Title ?? pack.Title;
        var content = "# Narration script\n\n" +
                      $"[HOCA]: {pageTitle} icin kisa bir anlatim akisi kuruyoruz.\n" +
                      $"[ASISTAN]: Kaynak durumu {sourceBundle.EvidenceStatus}; kaynak sinirliyse bunu acik soyle.\n" +
                      $"[HOCA]: Tamamlanan kavramlardan basla: {JoinOrFallback(conceptState.Completed, "sinyal zayif")}.\n" +
                      $"[ASISTAN]: Sonra onarim alanini isaretle: {JoinOrFallback(conceptState.Weak, "zayif sinyal yok")}.\n" +
                      "[HOCA]: Kapanista review quiz veya flashcard ile aktif hatirlama iste.";
        var contentJson = Serialize(new
        {
            format = "narration_script_v1",
            status = "outline_ready",
            sourceBasis = MediaSourceBasisFor(sourceBundle.EvidenceStatus),
            sourceReadiness = sourceBundle.EvidenceStatus,
            speakerLabels = new[] { "HOCA", "ASISTAN" },
            exportReadiness = "needs_review"
        });
        return new ArtifactPayload(content, contentJson, "markdown");
    }

    private static ArtifactPayload BuildVisualInstructionSetPayload(
        LearningNotebookPack pack,
        ConceptState conceptState,
        SourceEvidenceBundleDto sourceBundle,
        WikiPageContext? pageContext)
    {
        var pageLine = pageContext == null ? "Milestone/topic pack" : $"Wiki sayfasi: {pageContext.Title}";
        var content = "# Visual instruction set\n\n" +
                      $"{pageLine}\n\n" +
                      "- Concept graph odak dugumlerini goster.\n" +
                      "- Zayif kavramlari amber uyarisi gibi, kesin tani gibi degil isaretle.\n" +
                      "- Kaynak durumu sinirliyse kaynak badge'i amber kullan.\n" +
                      "- Misconception varsa repair flow: hata kalibi -> ipucu -> mikro kontrol.\n\n" +
                      "Not: Bu gorsel talimat seti image/video uretmez; designer/export pipeline icin guvenli brief'tir.";
        var contentJson = Serialize(new
        {
            format = "visual_instruction_set_v1",
            status = "outline_ready",
            sourceReadiness = sourceBundle.EvidenceStatus,
            weakConceptKeys = conceptState.Weak.Take(8).ToArray(),
            misconceptionKeys = conceptState.Misconceptions.Take(8).ToArray(),
            exportReadiness = "export_supported_later"
        });
        return new ArtifactPayload(content, contentJson, "markdown");
    }

    private static ArtifactPayload BuildMediaAccessibilityNotePayload(
        LearningNotebookPack pack,
        ConceptState conceptState,
        SourceEvidenceBundleDto sourceBundle,
        WikiPageContext? pageContext)
    {
        var content = "# Media accessibility note\n\n" +
                      "- Audio icin transcript/caption text fallback sagla.\n" +
                      "- Video-ready package icin her scene caption hint tasimali.\n" +
                      "- Slide outline icin speaker notes ve checkpoint sorusu gorunur olmali.\n" +
                      "- Source readiness badge'i metinle de okunmali.\n" +
                      "- Misconception sinyali kesin tani gibi yazilmamali.\n\n" +
                      $"Baglam: {(pageContext == null ? pack.Title : pageContext.Title)}\n\n" +
                      $"Kaynak durumu: {sourceBundle.EvidenceStatus}\n\n" +
                      $"Dikkat isteyen kavramlar: {JoinOrFallback(conceptState.Weak, "zayif sinyal yok")}.";
        var contentJson = Serialize(new
        {
            format = "media_accessibility_note_v1",
            status = "usable",
            textFallbackRequired = true,
            captionsRequiredForVideo = true,
            transcriptRequiredForAudio = true,
            keyboardFriendlyActions = true,
            sourceReadiness = sourceBundle.EvidenceStatus
        });
        return new ArtifactPayload(content, contentJson, "markdown");
    }

    private async Task<ArtifactPayload> BuildFlashcardSetPayloadAsync(
        Guid userId,
        LearningNotebookPack pack,
        ConceptState conceptState,
        WikiPageContext? pageContext,
        CancellationToken ct)
    {
        var targets = conceptState.Weak.Concat(conceptState.Completed)
            .Where(NotBlank)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        if (targets.Length == 0)
        {
            targets = ["notebook-review"];
        }

        var labels = await LoadConceptLabelsAsync(userId, pack.TopicId, targets, ct);
        var createdFrom = "notebook_studio";
        var cards = new List<FlashcardDto>();
        foreach (var conceptKey in targets)
        {
            var existing = await _db.Flashcards.AsNoTracking()
                .Where(f => f.UserId == userId &&
                            f.TopicId == pack.TopicId &&
                            f.Status != "deleted" &&
                            f.CreatedFrom == createdFrom &&
                            f.ConceptTag == conceptKey &&
                            f.WikiPageId == (pageContext == null ? null : pageContext.Id))
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => (Guid?)f.Id)
                .FirstOrDefaultAsync(ct);
            if (existing.HasValue)
            {
                continue;
            }

            var label = labels.TryGetValue(conceptKey, out var conceptLabel) ? conceptLabel : conceptKey;
            cards.Add(await _flashcards.CreateAsync(userId, new CreateFlashcardRequest(
                pack.TopicId,
                null,
                pageContext?.Id,
                pageContext == null ? $"{label}: ana fikri nasil aciklarsin?" : $"{pageContext.Title} / {label}: ana fikri nasil aciklarsin?",
                pageContext == null
                    ? $"{label} icin once tanimi soyle, sonra bir ornek ver, en sonda kendine kisa kontrol sorusu sor."
                    : $"{label} icin once {pageContext.Title} sayfasindaki notlardan hatirla, sonra bir ornek ver, en sonda kendine kisa kontrol sorusu sor.",
                pageContext == null ? "Tanim + ornek + yaygin hata uclusunu kullan." : "Wiki sayfasi notu + ornek + yaygin hata uclusunu kullan.",
                "notebook_studio",
                conceptKey,
                pageContext == null ? $"{pack.Title} milestone tekrar karti" : $"{pageContext.Title} sayfa tekrar karti",
                conceptState.Weak.Contains(conceptKey, StringComparer.OrdinalIgnoreCase) ? "repair" : "core",
                createdFrom), ct));
        }

        var due = await _db.ReviewItems.AsNoTracking()
            .Where(r => r.UserId == userId && r.TopicId == pack.TopicId && r.SourceType == "flashcard" && targets.Contains(r.ConceptTag ?? string.Empty))
            .OrderBy(r => r.DueAt)
            .Take(12)
            .Select(r => new { r.Id, r.ConceptTag, r.DueAt, r.FlashcardId })
            .ToListAsync(ct);

        var content = "# Flashcard set\n\n" +
                      (pageContext == null ? string.Empty : $"Wiki sayfasi: {pageContext.Title}\n\n") +
                      $"Hedef kavramlar: {JoinOrFallback(targets, "kavram sinyali yok")}\n\n" +
                      $"Yeni kart sayisi: {cards.Count}\n\n" +
                      $"SRS'e bagli tekrar kaydi: {due.Count}\n\n" +
                      "Kartlar aktif hatirlama icin olusturuldu; pasif okuma yerine once cevaplamayi dene.";
        var contentJson = Serialize(new
        {
            flashcardIds = cards.Select(c => c.Id).ToArray(),
            reviewItemIds = due.Select(d => d.Id).ToArray(),
            conceptKeys = targets,
            wikiPageId = pageContext?.Id,
            wikiPageTitle = pageContext?.Title,
            source = "FlashcardService + ReviewSrsService"
        });
        return new ArtifactPayload(content, contentJson, "markdown");
    }

    private async Task<ArtifactPayload> BuildReviewQuizPayloadAsync(
        Guid userId,
        LearningNotebookPack pack,
        ConceptState conceptState,
        SourceEvidenceBundleDto sourceBundle,
        WikiPageContext? pageContext,
        CancellationToken ct)
    {
        var targetConcept = pageContext?.ConceptKey
                            ?? conceptState.Weak.FirstOrDefault(NotBlank)
                            ?? conceptState.Completed.FirstOrDefault(NotBlank)
                            ?? "notebook-review";
        var blueprint = await _assessmentBlueprints.BuildBlueprintForPlanStepAsync(userId, new AssessmentBlueprintRequestDto
        {
            TopicId = pack.TopicId,
            SessionId = pack.SessionId,
            PlanQualitySnapshotId = pack.PlanQualitySnapshotId,
            PlanStepId = pack.Id.ToString("N"),
            AssessmentMode = "review_check",
            ConceptKey = targetConcept,
            ItemCountTarget = Math.Clamp(conceptState.Weak.Count + conceptState.Completed.Count, 3, 6)
        }, ct);
        if (blueprint.AssessmentMode == "diagnostic_check")
        {
            blueprint.AssessmentMode = "review_check";
            blueprint.UserSafeModeLabel = "Tekrar kontrolu";
            blueprint.PlanStepId = pack.Id.ToString("N");
            blueprint.TargetConcepts =
            [
                new AssessmentBlueprintConceptDto
                {
                    ConceptKey = targetConcept,
                    Label = targetConcept,
                    Role = conceptState.Weak.Contains(targetConcept, StringComparer.OrdinalIgnoreCase) ? "remediation_target" : "review_target",
                    DifficultyBand = "core",
                    ConfidenceStatus = "observed_only"
                }
            ];
            blueprint.CognitiveSkillMix = ["conceptual", "retrieval"];
            blueprint.LeakageSafetyRequirements =
            [
                "correct_option_hidden_pre_submit",
                "explanation_after_submit",
                "no_answer_key_in_public_dto"
            ];
        }
        blueprint.EvidenceMode = EvidenceModeFor(sourceBundle.EvidenceStatus);

        var concepts = blueprint.TargetConcepts.Select(c => $"{c.ConceptKey} ({c.Role})").DefaultIfEmpty(targetConcept);
        var warnings = blueprint.Warnings.Count == 0 ? "uyari yok" : string.Join("; ", blueprint.Warnings.Take(5));
        var content = "# Review quiz blueprint\n\n" +
                      (pageContext == null ? string.Empty : $"Wiki sayfasi: {pageContext.Title}\n\n") +
                      $"Mod: {blueprint.UserSafeModeLabel} ({blueprint.AssessmentMode})\n\n" +
                      $"Hedef kavramlar: {string.Join(", ", concepts)}\n\n" +
                      $"Soru hedefi: {blueprint.ItemCountTarget}\n\n" +
                      $"Kanıt modu: {blueprint.EvidenceMode}\n\n" +
                      $"Uyarılar: {warnings}\n\n" +
                      "Guvenlik: Bu artifact cevap anahtari veya dogru secenek icermez; aciklama submit sonrasina aittir.";
        var contentJson = Serialize(new
        {
            format = "notebook_review_quiz_v1",
            wikiPageId = pageContext?.Id,
            wikiPageTitle = pageContext?.Title,
            targetConcept,
            misconceptionKeys = conceptState.Misconceptions,
            blueprint
        });
        return new ArtifactPayload(content, contentJson, "markdown");
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadConceptLabelsAsync(
        Guid userId,
        Guid topicId,
        IReadOnlyCollection<string> conceptKeys,
        CancellationToken ct)
    {
        if (conceptKeys.Count == 0) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var graphId = await _db.ConceptGraphSnapshots.AsNoTracking()
            .Where(g => g.UserId == userId && g.TopicId == topicId)
            .OrderByDescending(g => g.CreatedAt)
            .Select(g => (Guid?)g.Id)
            .FirstOrDefaultAsync(ct);
        if (!graphId.HasValue) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var concepts = await _db.LearningConcepts.AsNoTracking()
            .Where(c => c.ConceptGraphSnapshotId == graphId.Value && conceptKeys.Contains(c.StableKey))
            .Select(c => new { c.StableKey, c.Label })
            .ToListAsync(ct);
        return concepts.ToDictionary(c => c.StableKey, c => c.Label, StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildArtifactContent(
        LearningNotebookPack pack,
        string artifactType,
        ConceptState concepts,
        SourceEvidenceBundleDto sourceBundle,
        WikiKnowledgeNotebookDto notebook,
        WikiPageContext? pageContext)
    {
        var completed = JoinOrFallback(concepts.Completed, "henuz tamamlanmis concept sinyali yok");
        var weak = JoinOrFallback(concepts.Weak, "zayif concept sinyali yok");
        var misconceptions = JoinOrFallback(concepts.Misconceptions, "belirgin misconception sinyali yok");
        var sources = sourceBundle.EvidenceItems.Take(5).Select(i => $"- {i.Label}: {i.SnippetSummary}").ToArray();
        var sourceBlock = sources.Length == 0 ? "- Aktif kaynak ozeti yok." : string.Join('\n', sources);
        var pageTitle = pageContext?.Title ?? pack.Title;
        var pageSummary = string.IsNullOrWhiteSpace(pageContext?.SafeSummary) ? "Sayfa ozeti henuz zayif." : pageContext!.SafeSummary!;
        var pageNotes = pageContext?.BlockSnippets.Count > 0
            ? string.Join('\n', pageContext.BlockSnippets.Take(6).Select(s => $"- {s}"))
            : "- Bu Wiki sayfasinda henuz yakalanmis ders notu yok.";
        var pageQuestions = pageContext?.QuestionSnippets.Count > 0
            ? string.Join('\n', pageContext.QuestionSnippets.Take(4).Select(q => $"- {q}"))
            : "- Ogrenci sorusu henuz yakalanmamis.";
        var pageLine = pageContext == null
            ? "Bu artifact milestone/topic paketinden uretildi."
            : $"Bu artifact `{pageTitle}` Wiki sayfasina bagli uretildi.";

        return artifactType switch
        {
            "source_digest" => $"# Kaynak ozeti\n\n{pageLine}\n\nKanit durumu: {sourceBundle.EvidenceStatus}\n\n{sourceBlock}\n\nWiki sayfa ozeti: {pageSummary}\n\nNot: Silinmis veya stale kaynaklar guclu kanit sayilmaz; kaynak yoksa bu ozet kaynakli iddia kurmaz.",
            "misconception_repair_pack" => $"# Yanlis anlama onarim paketi\n\n{pageLine}\n\nZayif alanlar: {weak}\n\nMisconception sinyalleri: {misconceptions}\n\nOgrenci soru sinyalleri:\n{pageQuestions}\n\nPekistirme notlari:\n{pageNotes}\n\nIlk hamle: kisa aciklama, sonra bir mikro kontrol sorusu.",
            "worked_example_set" => $"# Worked example set\n\n{pageLine}\n\nHedef kavramlar: {weak}\n\n1. Once kavrami tek cumleyle tanimla.\n2. Sonra sayfadaki notlardan bir ornek sec.\n3. Ornegi adim adim cozumle.\n4. Sonunda ogrenciye benzer ama kucuk bir varyasyon sor.\n\nSayfa notlari:\n{pageNotes}",
            "retrieval_card_set" => $"# Retrieval card set\n\n{pageLine}\n\nAktif hatirlama hedefleri: {completed}\n\nDikkat isteyen hedefler: {weak}\n\nKart onerileri:\n- Tanimi kapatip kendi cumlenle soyle.\n- Sayfadaki bir soruyu cevaplamadan once tahmin yaz.\n- Yanlis anlama sinyali varsa once hata kalibini adlandir.\n\nOgrenci sorulari:\n{pageQuestions}",
            "slide_deck_outline" => $"# Slayt taslagi\n\n{pageLine}\n\n1. Bu milestone neyi kapsar?\n2. Tamamlanan kavramlar: {completed}\n3. Dikkat isteyen kavramlar: {weak}\n4. Kaynak durumu: {sourceBundle.EvidenceStatus}\n5. Mini kontrol: bir ornek uzerinden acikla.\n\nSpeaker note: Pasif tekrar yerine ornek + kontrol sorusu kullan.",
            "briefing_doc" => $"# Kisa briefing\n\n{pageLine}\n\nPaket: {pack.Title}\n\nSayfa ozeti: {pageSummary}\n\nTamamlananlar: {completed}\n\nZayif alanlar: {weak}\n\nWiki kapsam: {notebook.ConceptCoverage} / {notebook.SourceCoverage}",
            "review_quiz" => $"# Review quiz tohumu\n\nOlcum modu: review_check\n\nHedef kavramlar: {weak}\n\nGuvenlik: Cevap anahtari submit oncesi gosterilmez.",
            "flashcard_set" => $"# Flashcard set tohumu\n\nKartlar zayif veya yeni kavramlardan uretilecek.\n\nKavramlar: {weak}",
            _ => $"# Calisma rehberi\n\n{pageLine}\n\nBu paket {pack.Title} icin hazirlandi.\n\nSayfa ozeti: {pageSummary}\n\nDers notlari:\n{pageNotes}\n\nOgrenci soru sinyalleri:\n{pageQuestions}\n\nTamamlanan kavramlar: {completed}\n\nTekrar gerektiren kavramlar: {weak}\n\nKaynak durumu: {sourceBundle.EvidenceStatus}\n\nOnerilen calisma: once ozeti oku, sonra flashcard/review quiz ile aktif tekrar yap."
        };
    }

    private static string BuildArtifactContent(
        LearningNotebookPack pack,
        string artifactType,
        ConceptState concepts,
        SourceEvidenceBundleDto sourceBundle,
        WikiKnowledgeNotebookDto notebook)
    {
        var completed = JoinOrFallback(concepts.Completed, "henuz tamamlanmis concept sinyali yok");
        var weak = JoinOrFallback(concepts.Weak, "zayif concept sinyali yok");
        var misconceptions = JoinOrFallback(concepts.Misconceptions, "belirgin misconception sinyali yok");
        var sources = sourceBundle.EvidenceItems.Take(5).Select(i => $"- {i.Label}: {i.SnippetSummary}").ToArray();
        var sourceBlock = sources.Length == 0 ? "- Aktif kaynak ozeti yok." : string.Join('\n', sources);

        return artifactType switch
        {
            "source_digest" => $"# Kaynak ozeti\n\nKanıt durumu: {sourceBundle.EvidenceStatus}\n\n{sourceBlock}\n\nNot: Silinmis veya stale kaynaklar guclu kanit sayilmaz.",
            "misconception_repair_pack" => $"# Yanlis anlama onarim paketi\n\nZayif alanlar: {weak}\n\nMisconception sinyalleri: {misconceptions}\n\nIlk hamle: kisa aciklama, sonra bir mikro kontrol sorusu.",
            "slide_deck_outline" => $"# Slayt taslagi\n\n1. Bu milestone neyi kapsar?\n2. Tamamlanan kavramlar: {completed}\n3. Dikkat isteyen kavramlar: {weak}\n4. Kaynak durumu: {sourceBundle.EvidenceStatus}\n5. Mini kontrol: bir ornek uzerinden acikla.\n\nSpeaker note: Pasif tekrar yerine ornek + kontrol sorusu kullan.",
            "briefing_doc" => $"# Kisa briefing\n\nPaket: {pack.Title}\n\nTamamlananlar: {completed}\n\nZayif alanlar: {weak}\n\nWiki kapsam: {notebook.ConceptCoverage} / {notebook.SourceCoverage}",
            "review_quiz" => $"# Review quiz tohumu\n\nOlcum modu: review_check\n\nHedef kavramlar: {weak}\n\nGuvenlik: Cevap anahtari submit oncesi gosterilmez.",
            "flashcard_set" => $"# Flashcard set tohumu\n\nKartlar zayif veya yeni kavramlardan uretilecek.\n\nKavramlar: {weak}",
            _ => $"# Calisma rehberi\n\nBu paket {pack.Title} icin hazirlandi.\n\nTamamlanan kavramlar: {completed}\n\nTekrar gerektiren kavramlar: {weak}\n\nKaynak durumu: {sourceBundle.EvidenceStatus}\n\nOnerilen calisma: once ozeti oku, sonra flashcard/review quiz ile aktif tekrar yap."
        };
    }

    private static string BuildSummary(string topicTitle, ConceptState concepts, SourceEvidenceBundleDto sourceBundle, WikiKnowledgeNotebookDto notebook, WikiPageContext? pageContext) =>
        pageContext == null
            ? BuildSummary(topicTitle, concepts, sourceBundle, notebook)
            : $"{pageContext.Title} Wiki sayfasi icin OrkaLM paketi hazirlandi. Blok sayisi: {pageContext.BlockSnippets.Count}. Ogrenci soru sinyali: {pageContext.QuestionSnippets.Count}. Kaynak durumu: {sourceBundle.EvidenceStatus}.";

    private static string BuildSummary(string topicTitle, ConceptState concepts, SourceEvidenceBundleDto sourceBundle, WikiKnowledgeNotebookDto notebook) =>
        $"{topicTitle} icin milestone paketi hazirlandi. Tamamlanan concept sayisi: {concepts.Completed.Count}. Zayif concept sayisi: {concepts.Weak.Count}. Kaynak durumu: {sourceBundle.EvidenceStatus}. Wiki kapsam: {notebook.ConceptCoverage}.";

    private static string BuildSourcePackSummary(
        string sourceTitle,
        ConceptState concepts,
        SourceEvidenceBundleDto sourceBundle,
        WikiPageContext? pageContext,
        LearningSource? source)
    {
        var context = pageContext == null
            ? "Source collection context"
            : $"{pageContext.Title} source page";
        var chunks = source == null ? "collection" : $"{source.ChunkCount} indexed chunks";
        return $"{sourceTitle} icin OrkaLM source notebook paketi hazirlandi. Context: {context}. Source scope: {chunks}. Kaynak durumu: {sourceBundle.EvidenceStatus}. Zayif concept sayisi: {concepts.Weak.Count}.";
    }

    private static string BuildSourceQuestionPackSummary(SourceQuestionPackSummary summary)
    {
        if (summary.ThreadCount == 0) return string.Empty;
        var review = summary.NeedsReviewCount > 0
            ? $"{summary.NeedsReviewCount} source Q&A turn review istiyor."
            : "Source Q&A memory review blocker gostermiyor.";
        return $" Source Q&A memory: {summary.ThreadCount} thread / {summary.TurnCount} turn. {review}";
    }

    private static IReadOnlyList<string> BuildPackWarnings(SourceEvidenceBundleDto bundle, ConceptState concepts, WikiPageContext? pageContext)
    {
        var warnings = BuildPackWarnings(bundle, concepts).ToList();
        if (pageContext != null && pageContext.BlockSnippets.Count == 0)
            warnings.Add("Wiki sayfasinda henuz blok yok; OrkaLM sayfa paketi dusuk baglamla olusturuldu.");
        if (pageContext?.Curation.Warnings.Count > 0)
            warnings.AddRange(pageContext.Curation.Warnings.Select(w => $"wiki_curation:{w}"));
        return warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> BuildPackWarnings(SourceEvidenceBundleDto bundle, ConceptState concepts)
    {
        var warnings = new List<string>();
        if (bundle.EvidenceStatus is "evidence_insufficient" or "degraded" or "stale")
            warnings.Add("Notebook pack kaynakli kesinlik iddia etmez; kaynak zemini sinirli.");
        if (concepts.Completed.Count == 0)
            warnings.Add("Tamamlanan concept sinyali zayif; pack diagnostic-first okunmali.");
        return warnings;
    }

    private static IReadOnlyList<string> BuildSourcePackWarnings(LearningSource? source, SourceEvidenceBundleDto bundle)
    {
        var warnings = new List<string>();
        if (source == null)
            warnings.Add("Source collection pack, belirli tek PDF/kaynak secilmeden olusturuldu.");
        else
        {
            if (!string.Equals(source.Status, "ready", StringComparison.OrdinalIgnoreCase))
                warnings.Add("Secili kaynak ready degil; source-backed iddia sinirli.");
            if (source.ChunkCount <= 0)
                warnings.Add("Secili kaynakta indekslenmis chunk yok; OrkaLM ciktilari evidence_insufficient okunmali.");
        }
        if (bundle.EvidenceStatus is "evidence_insufficient" or "degraded" or "stale")
            warnings.Add("Source notebook kaynak kanitini abartmaz; citation/evidence zayifsa bunu acik gosterir.");
        return warnings;
    }

    private static IReadOnlyList<NotebookStudioNextActionDto> BuildSourceNextActions(ConceptState concepts, SourceEvidenceBundleDto bundle, LearningSource? source)
    {
        var actions = new List<NotebookStudioNextActionDto>();
        if (source == null)
        {
            actions.Add(new NotebookStudioNextActionDto { ActionType = "select_source", UserSafeLabel = "Once bir kaynak sec veya yukle", Priority = "high" });
        }
        if (bundle.EvidenceStatus is "source_grounded" or "mixed")
        {
            actions.Add(new NotebookStudioNextActionDto { ActionType = "source_digest", UserSafeLabel = "Kaynak ozetini incele", Priority = "high" });
        }
        else
        {
            actions.Add(new NotebookStudioNextActionDto { ActionType = "refresh_evidence", UserSafeLabel = "Kaynak kanitini yenile veya kaynak ekle", Priority = "high" });
        }
        actions.Add(new NotebookStudioNextActionDto { ActionType = "create_study_guide", UserSafeLabel = "Kaynak icin calisma rehberi olustur", Priority = "normal" });
        actions.Add(new NotebookStudioNextActionDto { ActionType = "create_flashcards", UserSafeLabel = "Kaynak kavramlarindan flashcard olustur", Priority = "normal" });
        actions.Add(new NotebookStudioNextActionDto { ActionType = "start_review_quiz", UserSafeLabel = "Kaynak odakli tekrar quizi baslat", Priority = concepts.Weak.Count > 0 ? "high" : "normal" });
        actions.Add(new NotebookStudioNextActionDto { ActionType = "slide_export_preview", UserSafeLabel = "Kaynak slayt/export onizlemesini hazirla", Priority = "normal" });
        return actions;
    }

    private static IReadOnlyList<NotebookStudioNextActionDto> BuildNextActions(ConceptState concepts, SourceEvidenceBundleDto bundle)
    {
        var actions = new List<NotebookStudioNextActionDto>();
        if (concepts.Weak.Count > 0)
            actions.Add(new NotebookStudioNextActionDto { ActionType = "start_review_quiz", UserSafeLabel = "Zayif kavramlar icin kisa tekrar quizi baslat", Priority = "high" });
        if (concepts.Misconceptions.Count > 0)
            actions.Add(new NotebookStudioNextActionDto { ActionType = "tutor_repair", UserSafeLabel = "Tutor ile yanlis anlama onarimi yap", Priority = "high" });
        actions.Add(new NotebookStudioNextActionDto { ActionType = "create_flashcards", UserSafeLabel = "Bu milestone icin flashcard seti olustur", Priority = "normal" });
        if (bundle.EvidenceStatus is "source_grounded" or "mixed")
            actions.Add(new NotebookStudioNextActionDto { ActionType = "review_sources", UserSafeLabel = "Kaynak ozetini gozden gecir", Priority = "normal" });
        else
            actions.Add(new NotebookStudioNextActionDto { ActionType = "add_sources", UserSafeLabel = "Kaynak ekleyerek paketi guclendir", Priority = "normal" });
        return actions;
    }

    private async Task<LearningNotebookPackDto> ToDtoAsync(LearningNotebookPack pack, bool includeArtifacts, CancellationToken ct)
    {
        var artifactIds = ParseGuids(pack.ArtifactIdsJson);
        var artifacts = new List<LearningArtifactDto>();
        if (includeArtifacts)
        {
            foreach (var id in artifactIds.Take(20))
            {
                var artifact = await _artifacts.GetArtifactAsync(pack.UserId, id, ct);
                if (artifact != null) artifacts.Add(artifact);
            }
        }
        var wikiPage = ParseWikiPageMetadata(pack.SafeMetadataJson);

        return new LearningNotebookPackDto
        {
            Id = pack.Id,
            TopicId = pack.TopicId,
            SessionId = pack.SessionId,
            WikiPageId = pack.WikiPageId ?? wikiPage.WikiPageId,
            WikiPageTitle = pack.WikiPageTitle ?? wikiPage.WikiPageTitle,
            WikiPageKey = pack.WikiPageKey ?? wikiPage.WikiPageKey,
            SourceSurface = wikiPage.SourceSurface,
            SourceId = wikiPage.SourceId,
            SourceTitle = wikiPage.SourceTitle,
            ActiveLessonSnapshotId = pack.ActiveLessonSnapshotId,
            StudentContextSnapshotId = pack.StudentContextSnapshotId,
            SourceEvidenceBundleId = pack.SourceEvidenceBundleId,
            WikiNotebookSnapshotId = pack.WikiNotebookSnapshotId,
            PlanQualitySnapshotId = pack.PlanQualitySnapshotId,
            AssessmentQualitySnapshotId = pack.AssessmentQualitySnapshotId,
            PackType = pack.PackType,
            PackStatus = pack.PackStatus,
            Title = pack.Title,
            Summary = pack.Summary,
            SourceReadiness = pack.SourceReadiness,
            EvidenceStatus = pack.EvidenceStatus,
            CompletedConceptKeys = ParseStrings(pack.CompletedConceptKeysJson),
            WeakConceptKeys = ParseStrings(pack.WeakConceptKeysJson),
            MisconceptionKeys = ParseStrings(pack.MisconceptionKeysJson),
            ArtifactIds = artifactIds,
            Artifacts = artifacts,
            NextActions = Parse(pack.NextActionsJson, Array.Empty<NotebookStudioNextActionDto>()),
            Warnings = ParseStrings(pack.WarningsJson),
            CreatedAt = pack.CreatedAt,
            UpdatedAt = pack.UpdatedAt
        };
    }

    private async Task<bool> OwnsTopicAsync(Guid userId, Guid topicId, CancellationToken ct) =>
        await _db.Topics.AsNoTracking().AnyAsync(t => t.Id == topicId && t.UserId == userId && !t.IsArchived, ct);

    private async Task<bool> OwnsSessionAsync(Guid userId, Guid sessionId, CancellationToken ct) =>
        await _db.Sessions.AsNoTracking().AnyAsync(s => s.Id == sessionId && s.UserId == userId, ct);

    private static string NormalizePackType(string? value)
    {
        var key = Normalize(value);
        return PackTypes.Contains(key) ? key : "milestone_review";
    }

    private static bool IsSourceSurface(string? value)
    {
        var key = Normalize(value);
        return key is "source" or "source_notebook" or "source_collection" or "orkalm" or "orkalm_source";
    }

    private static string NormalizeArtifactType(string? value)
    {
        var key = Normalize(value);
        return string.IsNullOrWhiteSpace(key) ? "study_guide" : key;
    }

    private static string SourceBasisFor(string artifactType, SourceEvidenceBundleDto bundle)
    {
        if (artifactType == "source_digest" && bundle.EvidenceStatus is "source_grounded" or "mixed") return "source_grounded";
        if (artifactType == "source_digest") return "evidence_insufficient";
        if (bundle.EvidenceStatus == "wiki_backed") return "wiki_backed";
        if (bundle.EvidenceStatus is "source_grounded" or "mixed") return "source_grounded";
        return "model_assisted";
    }

    private static string MediaSourceBasisFor(string evidenceStatus) =>
        evidenceStatus switch
        {
            "source_grounded" or "mixed" => "source_grounded",
            "wiki_backed" => "wiki_backed",
            _ => "model_assisted"
        };

    private static IReadOnlyList<string> BuildMediaWarnings(SourceEvidenceBundleDto sourceBundle, string baseWarning)
    {
        var warnings = new List<string> { baseWarning };
        if (sourceBundle.EvidenceStatus is "evidence_insufficient" or "degraded" or "stale")
            warnings.Add("Kaynak zemini sinirli; media/export ciktilari source-backed gibi sunulmamalidir.");
        return warnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
    }

    private static string? SafeCitationLabel(SourceEvidenceBundleDto sourceBundle) =>
        sourceBundle.EvidenceItems.Select(i => i.Label).FirstOrDefault(NotBlank);

    private static string EvidenceModeFor(string evidenceStatus) => evidenceStatus switch
    {
        "source_grounded" => "source_grounded",
        "wiki_backed" => "wiki_backed",
        "mixed" => "mixed",
        _ => "evidence_insufficient"
    };

    private static string StatusFor(string evidenceStatus) =>
        evidenceStatus switch
        {
            "source_grounded" or "mixed" or "wiki_backed" => "ready",
            "stale" => "stale",
            "degraded" => "degraded",
            _ => "degraded"
        };

    private static string BuildPackTitle(string topicTitle, string packType) =>
        $"{topicTitle} - {packType.Replace('_', ' ')}";

    private static string TitleFor(string artifactType, string packTitle) =>
        $"{packTitle}: {artifactType.Replace('_', ' ')}";

    private static string JoinOrFallback(IReadOnlyList<string> values, string fallback) =>
        values.Count == 0 ? fallback : string.Join(", ", values.Take(8));

    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);
    private static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    private static string Trim(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].Trim();
    }
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static T Parse<T>(string? json, T fallback)
    {
        if (string.IsNullOrWhiteSpace(json)) return fallback;
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback; }
        catch { return fallback; }
    }

    private static IReadOnlyList<string> ParseStrings(string? json) => Parse(json, Array.Empty<string>());
    private static IReadOnlyList<Guid> ParseGuids(string? json) => Parse(json, Array.Empty<Guid>());

    private static string EscapeMermaid(string value) =>
        value.Replace("[", "(").Replace("]", ")").Replace("\"", "'");

    private static string NodeStateFor(string conceptKey, ConceptState conceptState)
    {
        if (conceptState.Weak.Contains(conceptKey, StringComparer.OrdinalIgnoreCase)) return "zayif";
        if (conceptState.Completed.Contains(conceptKey, StringComparer.OrdinalIgnoreCase)) return "tamamlandi";
        return "siradaki";
    }

    private static string MasteryStatusFor(ConceptMastery? mastery)
    {
        if (mastery == null) return "unknown";
        if (mastery.RemediationNeed != "none" || mastery.MasteryScore < 0.55m) return "needs_repair";
        if (mastery.MasteryScore >= 0.75m && mastery.Confidence >= 0.6m) return "strong";
        if (mastery.MasteryScore >= 0.55m) return "developing";
        return "observed_only";
    }

    private static bool HasMisconceptionEvidence(ConceptMastery? mastery)
    {
        if (mastery == null || string.IsNullOrWhiteSpace(mastery.MisconceptionEvidenceJson)) return false;
        return mastery.MisconceptionEvidenceJson.Trim() is not "[]" and not "{}";
    }

    private static string QuizHookFor(string nodeState, bool hasMisconception)
    {
        if (hasMisconception) return "misconception_probe";
        return nodeState switch
        {
            "zayif" => "review_check",
            "tamamlandi" => "retrieval_practice",
            _ => "micro_quiz"
        };
    }

    private static IReadOnlyList<string> PageFocusConceptKeys(WikiPageContext? pageContext)
    {
        if (pageContext == null) return Array.Empty<string>();
        return pageContext.BlockConceptKeys
            .Append(pageContext.ConceptKey)
            .Where(NotBlank)
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }

    private static string Clip(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var cleaned = value.Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    private static PackWikiPageMetadata ParseWikiPageMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return new PackWikiPageMetadata(null, null, null, null, null, null);
        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            var root = document.RootElement;
            var wikiPageId = root.TryGetProperty("wikiPageId", out var idElement) &&
                             idElement.ValueKind == JsonValueKind.String &&
                             Guid.TryParse(idElement.GetString(), out var id)
                ? id
                : (Guid?)null;
            var title = root.TryGetProperty("wikiPageTitle", out var titleElement) && titleElement.ValueKind == JsonValueKind.String
                ? titleElement.GetString()
                : null;
            var key = root.TryGetProperty("wikiPageKey", out var keyElement) && keyElement.ValueKind == JsonValueKind.String
                ? keyElement.GetString()
                : null;
            var sourceSurface = root.TryGetProperty("sourceSurface", out var surfaceElement) && surfaceElement.ValueKind == JsonValueKind.String
                ? surfaceElement.GetString()
                : null;
            var sourceId = root.TryGetProperty("sourceId", out var sourceIdElement) &&
                           sourceIdElement.ValueKind == JsonValueKind.String &&
                           Guid.TryParse(sourceIdElement.GetString(), out var parsedSourceId)
                ? parsedSourceId
                : (Guid?)null;
            var sourceTitle = root.TryGetProperty("sourceTitle", out var sourceTitleElement) && sourceTitleElement.ValueKind == JsonValueKind.String
                ? sourceTitleElement.GetString()
                : null;
            return new PackWikiPageMetadata(wikiPageId, title, key, sourceSurface, sourceId, sourceTitle);
        }
        catch
        {
            return new PackWikiPageMetadata(null, null, null, null, null, null);
        }
    }

    private sealed record WikiPageContext(
        Guid Id,
        Guid TopicId,
        string Title,
        string PageKey,
        string PageType,
        string? ConceptKey,
        string SourceReadiness,
        string EvidenceStatus,
        string? SafeSummary,
        IReadOnlyList<string> BlockSnippets,
        IReadOnlyList<string> QuestionSnippets,
        IReadOnlyList<string> BlockConceptKeys,
        IReadOnlyList<string> MisconceptionKeys,
        int RepairNoteCount,
        WikiCurationSummaryDto Curation);

    private sealed record PackWikiPageMetadata(
        Guid? WikiPageId,
        string? WikiPageTitle,
        string? WikiPageKey,
        string? SourceSurface,
        Guid? SourceId,
        string? SourceTitle);

    private sealed record ConceptState(
        IReadOnlyList<string> Completed,
        IReadOnlyList<string> Weak,
        IReadOnlyList<string> Misconceptions);

    private sealed record SourceQuestionPackSummary
    {
        public IReadOnlyList<Guid> SourceIds { get; init; } = Array.Empty<Guid>();
        public Guid? WikiPageId { get; init; }
        public int ThreadCount { get; init; }
        public int TurnCount { get; init; }
        public int NeedsReviewCount { get; init; }
        public int DegradedCount { get; init; }
        public IReadOnlyList<string> RecentQuestions { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    }

    private sealed record ArtifactPayload(string SafeContent, string? ContentJson, string RenderFormat);

    private static class SourceEvidenceLifecycleProjection
    {
        public static SourceEvidenceBundleDto ToDto(SourceEvidenceBundle entity)
        {
            var payload = Parse(entity.EvidenceJson, new SourceEvidenceBundlePayload(Array.Empty<SourceEvidenceItemDto>(), Array.Empty<string>()));
            return new SourceEvidenceBundleDto
            {
                Id = entity.Id,
                TopicId = entity.TopicId,
                SessionId = entity.SessionId,
                BundleHash = entity.BundleHash,
                EvidenceStatus = entity.EvidenceStatus,
                SourceCount = entity.SourceCount,
                ReadySourceCount = entity.ReadySourceCount,
                ChunkCount = entity.ChunkCount,
                CitationCoverage = entity.CitationCoverage,
                UnsupportedCitationCount = entity.UnsupportedCitationCount,
                StaleEvidenceCount = entity.StaleEvidenceCount,
                DeletedEvidenceCount = entity.DeletedEvidenceCount,
                EvidenceItems = payload.Items,
                Warnings = payload.Warnings,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt,
                ExpiresAt = entity.ExpiresAt
            };
        }

        private static T Parse<T>(string? json, T fallback)
        {
            if (string.IsNullOrWhiteSpace(json)) return fallback;
            try { return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback; }
            catch { return fallback; }
        }

        private sealed record SourceEvidenceBundlePayload(
            IReadOnlyList<SourceEvidenceItemDto> Items,
            IReadOnlyList<string> Warnings);
    }
}
