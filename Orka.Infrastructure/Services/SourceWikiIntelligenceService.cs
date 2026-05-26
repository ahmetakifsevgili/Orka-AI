using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class SourceWikiIntelligenceService : ISourceWikiIntelligenceService
{
    private const int MaxSources = 24;
    private const int MaxPages = 24;
    private const int MaxBlocks = 240;

    private static readonly string[] BlockedMarkers =
    [
        "rawPrompt", "hiddenPrompt", "systemPrompt", "developerPrompt", "rawProviderPayload",
        "rawSourceChunk", "rawToolPayload", "debugTrace", "localPath", "apiKey", "secret",
        "token", "answerKey", "correctAnswer", "stackTrace", "ownerId", "userId"
    ];

    private readonly OrkaDbContext _db;
    private readonly ISourceEvidenceLifecycleService _sourceLifecycle;
    private readonly ISourceConceptLinkingService _sourceConceptLinks;
    private readonly ISourceQuestionThreadService _sourceQuestionThreads;
    private readonly ISourceCompareService _sourceCompare;
    private readonly ITopicScopeResolver _topicScopeResolver;

    public SourceWikiIntelligenceService(
        OrkaDbContext db,
        ISourceEvidenceLifecycleService sourceLifecycle,
        ISourceConceptLinkingService sourceConceptLinks,
        ISourceQuestionThreadService sourceQuestionThreads,
        ISourceCompareService sourceCompare,
        ITopicScopeResolver topicScopeResolver)
    {
        _db = db;
        _sourceLifecycle = sourceLifecycle;
        _sourceConceptLinks = sourceConceptLinks;
        _sourceQuestionThreads = sourceQuestionThreads;
        _sourceCompare = sourceCompare;
        _topicScopeResolver = topicScopeResolver;
    }

    public async Task<SourceWikiIntelligenceProfileDto?> BuildProfileAsync(
        Guid userId,
        Guid? topicId = null,
        Guid? sourceId = null,
        Guid? wikiPageId = null,
        CancellationToken ct = default)
    {
        var scope = await ResolveScopeAsync(userId, topicId, sourceId, wikiPageId, ct);
        if (scope == null) return null;

        var sources = await LoadSourcesAsync(userId, scope.TopicIds, sourceId, ct);
        var pages = await LoadPagesAsync(userId, scope.TopicIds, wikiPageId, ct);
        var blocks = await LoadBlocksAsync(pages.Select(p => p.Id).ToArray(), ct);
        var blockMap = blocks.GroupBy(b => b.WikiPageId).ToDictionary(g => g.Key, g => (IReadOnlyList<WikiBlock>)g.ToArray());

        var latestBundle = scope.TopicId.HasValue
            ? await _sourceLifecycle.GetLatestSourceEvidenceBundleAsync(userId, scope.TopicId.Value, null, ct)
            : null;
        var lifecycle = scope.TopicId.HasValue
            ? await _sourceLifecycle.GetSourceLifecycleSummaryAsync(userId, scope.TopicId.Value, ct)
            : null;
        var studySummary = await _sourceQuestionThreads.GetStudySummaryAsync(userId, scope.TopicId, sourceId, wikiPageId, ct);
        var citationReview = await LoadCitationReviewAsync(userId, scope.TopicId, sourceId, ct);
        var linkedConcepts = await LoadLinkedConceptsAsync(userId, sources, wikiPageId, ct);
        var linkedCountBySource = linkedConcepts
            .Where(l => l.SourceId.HasValue)
            .GroupBy(l => l.SourceId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var evidenceStatus = ResolveEvidenceStatus(latestBundle, lifecycle, sources);
        var sourceReadiness = ResolveSourceReadiness(evidenceStatus, sources);
        var citationReadiness = citationReview?.ReviewStatus ?? "not_checked";
        var wikiPages = pages.Select(page =>
            BuildWikiPageReadiness(page, blockMap.TryGetValue(page.Id, out var pageBlocks) ? pageBlocks : [])).ToArray();
        var sourceEvidence = sources.Select(source =>
            BuildSourceReadiness(source, latestBundle, citationReview, linkedCountBySource)).ToArray();

        var citationWarningCount = citationReview?.Coverage.NeedsReviewCount ?? studySummary.CitationWarningCount;
        var repairPendingCount = wikiPages.Count(p => p.CurationStatus == "repair_pending");
        var sourceLimitedPageCount = wikiPages.Count(p => p.SourceLimitedSignalCount > 0 || IsLimited(p.SourceReadiness) || IsLimited(p.EvidenceStatus));
        var warnings = BuildWarnings(sources.Count, pages.Count, sourceReadiness, evidenceStatus, citationWarningCount, repairPendingCount, sourceLimitedPageCount, linkedConcepts.Count, studySummary)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
        var reasonCodes = BuildReasonCodes(sourceReadiness, evidenceStatus, citationReadiness, warnings, studySummary)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToArray();
        var nextActions = BuildNextActions(sources, wikiPages, citationWarningCount, linkedConcepts.Count, studySummary, warnings)
            .GroupBy(a => $"{a.ActionType}:{a.SourceId}:{a.WikiPageId}:{a.ConceptKey}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(a => PriorityScore(a.Priority))
            .Take(8)
            .ToArray();

        return new SourceWikiIntelligenceProfileDto
        {
            TopicId = scope.TopicId,
            SourceId = sourceId,
            WikiPageId = wikiPageId,
            ProfileStatus = ResolveProfileStatus(sources.Count, pages.Count, warnings, evidenceStatus, citationWarningCount),
            SourceReadiness = sourceReadiness,
            EvidenceStatus = evidenceStatus,
            CitationReadiness = citationReadiness,
            WikiHealthStatus = ResolveWikiHealth(wikiPages),
            CanClaimSourceGrounded = evidenceStatus == "source_grounded" && sources.Any(s => s.Status == "ready") && citationWarningCount == 0 && !warnings.Any(IsSourceGroundingBlocker),
            SourceCount = sources.Count,
            ReadySourceCount = sources.Count(s => s.Status == "ready"),
            WikiPageCount = pages.Count,
            LinkedConceptCount = linkedConcepts.Count,
            CitationWarningCount = citationWarningCount,
            SourceQuestionThreadCount = studySummary.ThreadCount,
            SourceQuestionTurnCount = studySummary.TurnCount,
            RepairPendingPageCount = repairPendingCount,
            SourceLimitedPageCount = sourceLimitedPageCount,
            EvidenceReadiness = sourceEvidence,
            WikiPages = wikiPages,
            LinkedConcepts = linkedConcepts.Take(16).ToArray(),
            NextActions = nextActions,
            Warnings = warnings,
            ReasonCodes = reasonCodes,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<ProfileScope?> ResolveScopeAsync(Guid userId, Guid? topicId, Guid? sourceId, Guid? wikiPageId, CancellationToken ct)
    {
        LearningSource? source = null;
        if (sourceId.HasValue)
        {
            source = await _db.LearningSources.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sourceId.Value && s.UserId == userId && !s.IsDeleted, ct);
            if (source == null) return null;
            topicId ??= source.TopicId;
        }

        WikiPage? page = null;
        if (wikiPageId.HasValue)
        {
            page = await _db.WikiPages.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == wikiPageId.Value && p.UserId == userId && !p.IsDeleted, ct);
            if (page == null) return null;
            topicId ??= page.TopicId;
        }

        IReadOnlyList<Guid> topicIds = Array.Empty<Guid>();
        if (topicId.HasValue)
        {
            var ownsTopic = await _db.Topics.AsNoTracking()
                .AnyAsync(t => t.Id == topicId.Value && t.UserId == userId && !t.IsArchived, ct);
            if (!ownsTopic) return null;

            var topicScope = await _topicScopeResolver.ResolveAsync(userId, topicId.Value, ct);
            topicIds = topicScope.IsValid && topicScope.TreeTopicIds.Count > 0
                ? topicScope.TreeTopicIds
                : [topicId.Value];

            if (source?.TopicId.HasValue == true && !topicIds.Contains(source.TopicId.Value)) return null;
            if (page != null && !topicIds.Contains(page.TopicId)) return null;
        }

        return new ProfileScope(topicId, topicIds);
    }

    private async Task<List<LearningSource>> LoadSourcesAsync(Guid userId, IReadOnlyList<Guid> topicIds, Guid? sourceId, CancellationToken ct)
    {
        var query = _db.LearningSources.AsNoTracking()
            .Where(s => s.UserId == userId && !s.IsDeleted);

        if (sourceId.HasValue)
        {
            query = query.Where(s => s.Id == sourceId.Value);
        }
        else if (topicIds.Count > 0)
        {
            query = query.Where(s => s.TopicId.HasValue && topicIds.Contains(s.TopicId.Value));
        }

        return await query
            .OrderByDescending(s => s.Status == "ready")
            .ThenByDescending(s => s.UpdatedAt)
            .Take(MaxSources)
            .ToListAsync(ct);
    }

    private async Task<List<WikiPage>> LoadPagesAsync(Guid userId, IReadOnlyList<Guid> topicIds, Guid? wikiPageId, CancellationToken ct)
    {
        var query = _db.WikiPages.AsNoTracking()
            .Where(p => p.UserId == userId && !p.IsDeleted);

        if (wikiPageId.HasValue)
        {
            query = query.Where(p => p.Id == wikiPageId.Value);
        }
        else if (topicIds.Count > 0)
        {
            query = query.Where(p => topicIds.Contains(p.TopicId));
        }

        return await query
            .OrderByDescending(p => p.UpdatedAt)
            .ThenBy(p => p.OrderIndex)
            .Take(MaxPages)
            .ToListAsync(ct);
    }

    private async Task<List<WikiBlock>> LoadBlocksAsync(IReadOnlyList<Guid> pageIds, CancellationToken ct)
    {
        if (pageIds.Count == 0) return [];

        return await _db.WikiBlocks.AsNoTracking()
            .Where(b => pageIds.Contains(b.WikiPageId) && !b.IsDeleted)
            .OrderBy(b => b.WikiPageId)
            .ThenBy(b => b.OrderIndex)
            .Take(MaxBlocks)
            .ToListAsync(ct);
    }

    private async Task<CitationReviewResultDto?> LoadCitationReviewAsync(Guid userId, Guid? topicId, Guid? sourceId, CancellationToken ct)
    {
        if (sourceId.HasValue)
        {
            return await _sourceCompare.GetSourceCitationReviewAsync(userId, sourceId.Value, ct);
        }

        if (topicId.HasValue)
        {
            return await _sourceCompare.GetTopicCitationReviewAsync(userId, topicId.Value, ct);
        }

        return null;
    }

    private async Task<IReadOnlyList<SourceConceptLinkDto>> LoadLinkedConceptsAsync(
        Guid userId,
        IReadOnlyList<LearningSource> sources,
        Guid? wikiPageId,
        CancellationToken ct)
    {
        var links = new List<SourceConceptLinkDto>();
        if (wikiPageId.HasValue)
        {
            var summary = await _sourceConceptLinks.GetWikiPageSourceLinksAsync(userId, wikiPageId.Value, ct);
            if (summary != null) links.AddRange(summary.Links);
        }

        foreach (var source in sources.Take(4))
        {
            var summary = await _sourceConceptLinks.GetSourceConceptLinksAsync(userId, source.Id, ct);
            if (summary != null) links.AddRange(summary.Links);
        }

        return links
            .Where(l => !string.IsNullOrWhiteSpace(l.ConceptKey) || l.WikiPageId.HasValue)
            .GroupBy(l => $"{l.SourceId}:{l.WikiPageId}:{l.ConceptKey}", StringComparer.OrdinalIgnoreCase)
            .Select(g => SanitizeLink(g.First()))
            .OrderBy(l => l.IsSuggestion)
            .ThenByDescending(l => l.ConfidenceScore ?? 0m)
            .Take(32)
            .ToArray();
    }

    private static SourceWikiEvidenceReadinessDto BuildSourceReadiness(
        LearningSource source,
        SourceEvidenceBundleDto? bundle,
        CitationReviewResultDto? citationReview,
        IReadOnlyDictionary<Guid, int> linkedCountBySource)
    {
        var evidenceStatus = ResolveSourceEvidenceStatus(source, bundle);
        var sourceReadiness = ResolveSingleSourceReadiness(source, evidenceStatus);
        var sourceCitationItems = citationReview?.Items.Where(i => i.SourceId == source.Id).ToArray() ?? [];
        var citationReadiness = ResolveCitationReadiness(sourceCitationItems);
        var warnings = new List<string>();
        if (source.Status != "ready") warnings.Add($"source_{SafeKey(source.Status)}");
        if (IsLimited(evidenceStatus)) warnings.Add("source_evidence_limited");
        if (citationReadiness is "needs_review" or "missing" or "unsupported" or "stale") warnings.Add("citation_review_needed");

        return new SourceWikiEvidenceReadinessDto
        {
            SourceId = source.Id,
            TopicId = source.TopicId,
            Title = SafeDisplay(source.Title, 160),
            Status = SafeDisplay(source.Status, 60),
            SourceReadiness = sourceReadiness,
            EvidenceStatus = evidenceStatus,
            CitationReadiness = citationReadiness,
            PageCount = source.PageCount,
            ChunkCount = source.ChunkCount,
            LinkedConceptCount = linkedCountBySource.TryGetValue(source.Id, out var count) ? count : 0,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToArray()
        };
    }

    private static WikiLearningPageReadinessDto BuildWikiPageReadiness(WikiPage page, IReadOnlyList<WikiBlock> blocks)
    {
        var curation = WikiAutoCurationService.BuildSummary(page, blocks);
        var visibleBlocks = blocks.Where(b => !b.IsDeleted && b.Visibility != "hidden_system").ToArray();
        var repairCount = visibleBlocks.Count(b => b.BlockType is WikiBlockType.RepairNote or WikiBlockType.MisconceptionNote);
        var sourceLimitedCount = visibleBlocks.Count(b => IsLimited(b.SourceBasis) || HasSourceWarning(b.SafetyWarningsJson));
        var manualNote = visibleBlocks.Any(b => b.BlockType is WikiBlockType.UserNote or WikiBlockType.ManualNote);

        return new WikiLearningPageReadinessDto
        {
            WikiPageId = page.Id,
            TopicId = page.TopicId,
            Title = SafeDisplay(page.Title, 160),
            PageType = SafeDisplay(page.PageType, 80),
            ConceptKey = SafeDisplay(page.ConceptKey, 120),
            SourceReadiness = SafeDisplay(page.SourceReadiness, 80),
            EvidenceStatus = SafeDisplay(page.EvidenceStatus, 80),
            CurationStatus = curation.CurationStatus,
            BlockCount = visibleBlocks.Length,
            RepairSignalCount = repairCount,
            SourceLimitedSignalCount = sourceLimitedCount,
            ManualNotePreserved = manualNote,
            NextAction = curation.NextAction,
            Warnings = curation.Warnings.Select(w => SafeDisplay(w, 140)).Where(NotBlank).Take(8).ToArray()
        };
    }

    private static IReadOnlyList<string> BuildWarnings(
        int sourceCount,
        int pageCount,
        string sourceReadiness,
        string evidenceStatus,
        int citationWarningCount,
        int repairPendingCount,
        int sourceLimitedPageCount,
        int linkedConceptCount,
        SourceStudySummaryDto studySummary)
    {
        var warnings = new List<string>();
        if (sourceCount == 0) warnings.Add("source_missing");
        if (pageCount == 0) warnings.Add("wiki_context_missing");
        if (IsLimited(sourceReadiness) || IsLimited(evidenceStatus)) warnings.Add("source_evidence_limited");
        if (citationWarningCount > 0) warnings.Add("citation_review_needed");
        if (repairPendingCount > 0) warnings.Add("wiki_repair_pending");
        if (sourceLimitedPageCount > 0) warnings.Add("wiki_source_limited");
        if (linkedConceptCount == 0 && sourceCount > 0) warnings.Add("source_concept_links_limited");
        if (studySummary.CitationWarningCount > 0 || studySummary.NeedsReviewCount > 0) warnings.Add("source_question_review_needed");
        if (IsLimited(sourceReadiness) || IsLimited(evidenceStatus) || citationWarningCount > 0) warnings.Add("source_grounded_claim_blocked");
        return warnings;
    }

    private static IReadOnlyList<string> BuildReasonCodes(
        string sourceReadiness,
        string evidenceStatus,
        string citationReadiness,
        IReadOnlyList<string> warnings,
        SourceStudySummaryDto studySummary)
    {
        var codes = new List<string> { $"source_readiness_{SafeKey(sourceReadiness)}", $"evidence_{SafeKey(evidenceStatus)}" };
        if (!string.IsNullOrWhiteSpace(citationReadiness)) codes.Add($"citation_{SafeKey(citationReadiness)}");
        codes.AddRange(warnings);
        if (studySummary.ThreadCount > 0) codes.Add("source_qa_memory_available");
        if (studySummary.RecommendedNextAction != "add_source") codes.Add(studySummary.RecommendedNextAction);
        return codes;
    }

    private static IEnumerable<SourceWikiNextActionDto> BuildNextActions(
        IReadOnlyList<LearningSource> sources,
        IReadOnlyList<WikiLearningPageReadinessDto> wikiPages,
        int citationWarningCount,
        int linkedConceptCount,
        SourceStudySummaryDto studySummary,
        IReadOnlyList<string> warnings)
    {
        if (sources.Count == 0)
        {
            yield return Action("add_source", "Kaynak ekle", "high", "source", null, null, null, ["source_missing"]);
        }

        if (warnings.Contains("source_evidence_limited", StringComparer.OrdinalIgnoreCase))
        {
            var source = sources.FirstOrDefault();
            yield return Action("review_source", "Kaynak kanitini kontrol et", "high", "source", source?.Id, null, null, ["source_evidence_limited"]);
        }

        if (citationWarningCount > 0)
        {
            yield return Action("citation_review", "Citation durumunu gozden gecir", "high", "source", sources.FirstOrDefault()?.Id, null, null, ["citation_review_needed"]);
        }

        var repairPage = wikiPages.FirstOrDefault(p => p.CurationStatus == "repair_pending" || p.RepairSignalCount > 0);
        if (repairPage != null)
        {
            yield return Action("repair_concept", "Wiki repair sinyalini Tutor ile toparla", "high", "wiki_page", null, repairPage.WikiPageId, repairPage.ConceptKey, ["wiki_repair_pending"]);
        }

        var sourceLimitedPage = wikiPages.FirstOrDefault(p => p.SourceLimitedSignalCount > 0 || IsLimited(p.SourceReadiness) || IsLimited(p.EvidenceStatus));
        if (sourceLimitedPage != null)
        {
            yield return Action("review_source", "Wiki sayfasinin kaynak uyarisini kontrol et", "normal", "wiki_page", null, sourceLimitedPage.WikiPageId, sourceLimitedPage.ConceptKey, ["wiki_source_limited"]);
        }

        if (studySummary.NeedsReviewCount > 0 || studySummary.CitationWarningCount > 0)
        {
            yield return Action("review_source_questions", "Kaynak soru-cevap hafizasini temizle", "normal", "source_question_thread", sources.FirstOrDefault()?.Id, null, null, ["source_question_review_needed"]);
        }

        if (sources.Count >= 2)
        {
            yield return Action("compare_sources", "Kaynaklari karsilastir", "normal", "source_collection", null, null, null, ["multi_source_ready"]);
        }

        if (sources.Count > 0 && linkedConceptCount == 0)
        {
            yield return Action("sync_source_concepts", "Kaynak-kavram baglarini olustur", "normal", "source", sources.First().Id, null, null, ["source_concept_links_limited"]);
        }

        if (wikiPages.Count > 0)
        {
            var page = wikiPages.First();
            yield return Action("open_notebook_pack", "Notebook pack ile guvenli ozet ac", "low", "wiki_page", null, page.WikiPageId, page.ConceptKey, ["wiki_context_available"]);
        }

        yield return Action("continue_learning", "Planli ogrenmeye devam et", "low", "topic", null, null, null, ["continue_learning"]);
    }

    private static SourceWikiNextActionDto Action(
        string actionType,
        string label,
        string priority,
        string targetType,
        Guid? sourceId,
        Guid? wikiPageId,
        string? conceptKey,
        IReadOnlyList<string> reasonCodes) => new()
        {
            ActionType = actionType,
            Label = label,
            Priority = priority,
            TargetType = targetType,
            SourceId = sourceId,
            WikiPageId = wikiPageId,
            ConceptKey = SafeDisplay(conceptKey, 120),
            ReasonCodes = reasonCodes.Select(r => SafeDisplay(r, 120)).Where(NotBlank).ToArray()
        };

    private static string ResolveEvidenceStatus(SourceEvidenceBundleDto? bundle, SourceLifecycleSummaryDto? lifecycle, IReadOnlyList<LearningSource> sources)
    {
        if (!string.IsNullOrWhiteSpace(bundle?.EvidenceStatus)) return bundle.EvidenceStatus;
        if (!string.IsNullOrWhiteSpace(lifecycle?.EvidenceStatus)) return lifecycle.EvidenceStatus;
        if (sources.Count == 0) return "evidence_insufficient";
        if (sources.Any(s => s.Status == "ready")) return "ready";
        if (sources.Any(s => s.Status is "stale" or "deleted")) return "stale";
        return "evidence_insufficient";
    }

    private static string ResolveSourceEvidenceStatus(LearningSource source, SourceEvidenceBundleDto? bundle)
    {
        if (source.IsDeleted) return "deleted";
        if (source.Status is "stale" or "deleted") return "stale";
        if (source.Status is "failed" or "error") return "degraded";
        if (bundle?.EvidenceStatus is "source_grounded" or "mixed") return bundle.EvidenceStatus;
        return source.Status == "ready" ? "ready" : "evidence_insufficient";
    }

    private static string ResolveSourceReadiness(string evidenceStatus, IReadOnlyList<LearningSource> sources)
    {
        if (evidenceStatus is "source_grounded" or "mixed") return evidenceStatus;
        if (sources.Count == 0) return "no_sources";
        if (sources.Any(s => s.Status == "ready")) return evidenceStatus == "ready" ? "ready" : evidenceStatus;
        if (sources.Any(s => s.Status is "stale" or "deleted")) return "stale";
        if (sources.Any(s => s.Status is "failed" or "error")) return "degraded";
        return "evidence_insufficient";
    }

    private static string ResolveSingleSourceReadiness(LearningSource source, string evidenceStatus)
    {
        if (source.IsDeleted) return "deleted";
        if (source.Status is "stale" or "deleted") return "stale";
        if (source.Status is "failed" or "error") return "degraded";
        if (evidenceStatus is "source_grounded" or "mixed") return evidenceStatus;
        return source.Status == "ready" ? "ready" : "evidence_insufficient";
    }

    private static string ResolveCitationReadiness(IReadOnlyList<CitationReviewItemDto> items)
    {
        if (items.Count == 0) return "not_checked";
        if (items.Any(i => i.CitationStatus is "missing" or "missing_citation")) return "missing";
        if (items.Any(i => i.CitationStatus is "unsupported" or "needs_review")) return "unsupported";
        if (items.Any(i => i.CitationStatus == "stale")) return "stale";
        return "supported";
    }

    private static string ResolveProfileStatus(int sourceCount, int pageCount, IReadOnlyList<string> warnings, string evidenceStatus, int citationWarningCount)
    {
        if (sourceCount == 0 && pageCount == 0) return "empty";
        if (citationWarningCount > 0 || warnings.Any(w => w.Contains("repair", StringComparison.OrdinalIgnoreCase))) return "needs_review";
        if (IsLimited(evidenceStatus) || warnings.Any(w => w.Contains("limited", StringComparison.OrdinalIgnoreCase))) return "limited";
        return "ready";
    }

    private static string ResolveWikiHealth(IReadOnlyList<WikiLearningPageReadinessDto> pages)
    {
        if (pages.Count == 0) return "empty";
        if (pages.Any(p => p.CurationStatus == "source_limited")) return "source_limited";
        if (pages.Any(p => p.CurationStatus == "repair_pending")) return "repair_pending";
        if (pages.Any(p => p.CurationStatus == "duplicate_trace")) return "duplicate_trace";
        if (pages.Any(p => p.CurationStatus == "stale_trace")) return "stale_trace";
        if (pages.Any(p => p.CurationStatus == "degraded")) return "degraded";
        return "clean";
    }

    private static bool IsSourceGroundingBlocker(string value) =>
        value.Contains("limited", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("blocked", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("citation", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("stale", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("insufficient", StringComparison.OrdinalIgnoreCase);

    private static bool IsLimited(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;
        return status.Contains("insufficient", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("stale", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("degraded", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("deleted", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("limited", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("missing", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSourceWarning(string? warningsJson)
    {
        if (string.IsNullOrWhiteSpace(warningsJson)) return false;
        return warningsJson.Contains("source", StringComparison.OrdinalIgnoreCase) ||
               warningsJson.Contains("stale", StringComparison.OrdinalIgnoreCase) ||
               warningsJson.Contains("degraded", StringComparison.OrdinalIgnoreCase) ||
               warningsJson.Contains("repair", StringComparison.OrdinalIgnoreCase);
    }

    private static SourceConceptLinkDto SanitizeLink(SourceConceptLinkDto link) => new()
    {
        SourceId = link.SourceId,
        SourceTitle = SafeDisplay(link.SourceTitle, 120),
        SourcePageId = link.SourcePageId,
        ConceptKey = SafeDisplay(link.ConceptKey, 120),
        ConceptTitle = SafeDisplay(link.ConceptTitle, 120),
        WikiPageId = link.WikiPageId,
        LinkType = SafeDisplay(link.LinkType, 80),
        Confidence = SafeDisplay(link.Confidence, 40),
        ConfidenceScore = link.ConfidenceScore,
        Basis = SafeDisplay(link.Basis, 120),
        EvidenceStatus = SafeDisplay(link.EvidenceStatus, 80),
        SourceReadiness = SafeDisplay(link.SourceReadiness, 80),
        IsSuggestion = link.IsSuggestion,
        Warnings = link.Warnings.Select(w => SafeDisplay(w, 140)).Where(NotBlank).Take(6).ToArray(),
        CreatedAt = link.CreatedAt,
        UpdatedAt = link.UpdatedAt
    };

    private static int PriorityScore(string priority) => priority switch
    {
        "urgent" => 4,
        "high" => 3,
        "normal" or "medium" => 2,
        _ => 1
    };

    private static string SafeKey(string? value)
    {
        var text = SafeDisplay(value, 80).ToLowerInvariant();
        text = Regex.Replace(text, @"[^a-z0-9_]+", "_");
        return string.IsNullOrWhiteSpace(text) ? "unknown" : text.Trim('_');
    }

    private static string SafeDisplay(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var text = Regex.Replace(value.Trim(), @"\s+", " ");
        text = Regex.Replace(text, @"[A-Za-z]:\\[^\s,;]+", "[local-path]");
        text = Regex.Replace(text, @"\b[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}\b", "[id]");
        foreach (var marker in BlockedMarkers)
        {
            text = text.Replace(marker, "[redacted]", StringComparison.OrdinalIgnoreCase);
        }

        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);

    private sealed record ProfileScope(Guid? TopicId, IReadOnlyList<Guid> TopicIds);
}
