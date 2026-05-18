using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class SourceCompareService : ISourceCompareService
{
    private const int MaxSources = 8;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] BlockedMarkers =
    [
        "rawPrompt", "hiddenPrompt", "systemPrompt", "rawProviderPayload", "rawSourceChunk",
        "rawToolPayload", "debugTrace", "localPath", "apiKey", "secret", "answerKey",
        "correctAnswer", "stackTrace"
    ];

    private readonly OrkaDbContext _db;
    private readonly ISourceConceptLinkingService _sourceConceptLinks;
    private readonly IWikiLearningTraceWriter? _traceWriter;
    private readonly ILearningRuntimeTelemetryService? _telemetry;

    public SourceCompareService(
        OrkaDbContext db,
        ISourceConceptLinkingService sourceConceptLinks,
        IWikiLearningTraceWriter? traceWriter = null,
        ILearningRuntimeTelemetryService? telemetry = null)
    {
        _db = db;
        _sourceConceptLinks = sourceConceptLinks;
        _traceWriter = traceWriter;
        _telemetry = telemetry;
    }

    public async Task<MultiSourceCompareResultDto?> CompareTopicAsync(
        Guid userId,
        Guid topicId,
        MultiSourceCompareRequestDto request,
        CancellationToken ct = default)
    {
        var ownsTopic = await _db.Topics.AsNoTracking()
            .AnyAsync(t => t.Id == topicId && t.UserId == userId && !t.IsArchived, ct);
        if (!ownsTopic) return null;

        request.TopicId = topicId;
        return await CompareAsync(userId, request, ct);
    }

    public async Task<MultiSourceCompareResultDto?> CompareAsync(
        Guid userId,
        MultiSourceCompareRequestDto request,
        CancellationToken ct = default)
    {
        await RecordTelemetryAsync(userId, request.TopicId, "multi_source_compare_requested", "started", null, ct);

        var explicitIds = (request.SourceIds ?? Array.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(MaxSources)
            .ToArray();

        var sources = await LoadSourcesAsync(userId, request.TopicId, explicitIds, ct);
        if (explicitIds.Length > 0 && sources.Count != explicitIds.Length)
        {
            return null;
        }

        if (sources.Count < 2)
        {
            await RecordTelemetryAsync(userId, request.TopicId, "multi_source_compare_degraded", "not_enough_sources", sources.Count, ct);
            return new MultiSourceCompareResultDto
            {
                TopicId = request.TopicId,
                ComparedSourceCount = sources.Count,
                CompareStatus = "degraded",
                EvidenceStatus = "evidence_insufficient",
                SourceReadiness = "evidence_insufficient",
                SourceSummaries = sources.Select(s => BuildSourceSummary(s, [], [])).ToArray(),
                Warnings = ["at_least_two_sources_required"],
                NextActions = ["select_more_sources", "add_source"],
                SafetyStatus = "safe"
            };
        }

        var sourceIds = sources.Select(s => s.Id).ToArray();
        var citationChecks = await _db.SourceCitationChecks.AsNoTracking()
            .Where(c => c.UserId == userId && c.SourceId.HasValue && sourceIds.Contains(c.SourceId.Value))
            .OrderByDescending(c => c.CreatedAt)
            .Take(240)
            .ToListAsync(ct);

        var linkMap = request.IncludeConceptLinks
            ? await LoadConceptLinksAsync(userId, sources, ct)
            : new Dictionary<Guid, IReadOnlyList<SourceConceptLinkDto>>();

        var summaries = sources
            .Select(s => BuildSourceSummary(
                s,
                citationChecks.Where(c => c.SourceId == s.Id).ToList(),
                linkMap.TryGetValue(s.Id, out var links) ? links : []))
            .ToArray();
        var coverage = BuildCoverage(citationChecks, summaries);
        var overlaps = BuildConceptOverlaps(linkMap, minSourceCount: 2);
        var sourceOnly = BuildConceptOverlaps(linkMap, minSourceCount: 1)
            .Where(o => o.SourceIds.Count == 1)
            .Take(12)
            .ToArray();
        var warnings = BuildCompareWarnings(summaries, coverage, overlaps.Count).ToList();
        var status = warnings.Any(w => w.Contains("insufficient", StringComparison.OrdinalIgnoreCase) ||
                                       w.Contains("stale", StringComparison.OrdinalIgnoreCase) ||
                                       w.Contains("citation", StringComparison.OrdinalIgnoreCase))
            ? "degraded"
            : "ready";

        var result = new MultiSourceCompareResultDto
        {
            TopicId = request.TopicId ?? sources.FirstOrDefault(s => s.TopicId.HasValue)?.TopicId,
            ComparedSourceCount = sources.Count,
            CompareStatus = status,
            EvidenceStatus = ResolveAggregateEvidence(summaries),
            SourceReadiness = ResolveAggregateReadiness(summaries),
            SourceSummaries = summaries,
            SharedConcepts = overlaps,
            SourceOnlyConcepts = sourceOnly,
            CitationCoverage = coverage,
            CitationReviewItems = request.IncludeCitationReview
                ? BuildReviewItems(citationChecks, sources).Take(24).ToArray()
                : [],
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray(),
            NextActions = BuildNextActions(coverage, overlaps.Count, sourceOnly.Length),
            SafetyStatus = "safe"
        };

        var trace = await WriteTraceIfRequestedAsync(userId, request, result, sources, ct);
        result.TraceBlockId = trace?.Id;
        await RecordTelemetryAsync(userId, result.TopicId, "multi_source_compare_completed", result.CompareStatus, result.ComparedSourceCount, ct);
        return result;
    }

    public async Task<CitationReviewResultDto?> GetSourceCitationReviewAsync(
        Guid userId,
        Guid sourceId,
        CancellationToken ct = default)
    {
        var source = await _db.LearningSources.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sourceId && s.UserId == userId && !s.IsDeleted, ct);
        if (source == null) return null;

        var checks = await _db.SourceCitationChecks.AsNoTracking()
            .Where(c => c.UserId == userId && c.SourceId == sourceId)
            .OrderByDescending(c => c.CreatedAt)
            .Take(80)
            .ToListAsync(ct);

        await RecordTelemetryAsync(userId, source.TopicId, "citation_review_viewed", "ok", checks.Count, ct);
        return BuildCitationReview(source.TopicId, source.Id, [source], checks);
    }

    public async Task<CitationReviewResultDto?> GetTopicCitationReviewAsync(
        Guid userId,
        Guid topicId,
        CancellationToken ct = default)
    {
        var ownsTopic = await _db.Topics.AsNoTracking()
            .AnyAsync(t => t.Id == topicId && t.UserId == userId && !t.IsArchived, ct);
        if (!ownsTopic) return null;

        var sources = await _db.LearningSources.AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId == topicId && !s.IsDeleted)
            .OrderByDescending(s => s.UpdatedAt)
            .Take(MaxSources)
            .ToListAsync(ct);
        var sourceIds = sources.Select(s => s.Id).ToArray();
        var checks = await _db.SourceCitationChecks.AsNoTracking()
            .Where(c => c.UserId == userId && c.SourceId.HasValue && sourceIds.Contains(c.SourceId.Value))
            .OrderByDescending(c => c.CreatedAt)
            .Take(160)
            .ToListAsync(ct);

        await RecordTelemetryAsync(userId, topicId, "citation_review_viewed", "ok", checks.Count, ct);
        return BuildCitationReview(topicId, null, sources, checks);
    }

    private async Task<List<LearningSource>> LoadSourcesAsync(
        Guid userId,
        Guid? topicId,
        IReadOnlyList<Guid> explicitIds,
        CancellationToken ct)
    {
        var query = _db.LearningSources.AsNoTracking()
            .Where(s => s.UserId == userId && !s.IsDeleted);
        if (topicId.HasValue)
        {
            query = query.Where(s => s.TopicId == topicId.Value);
        }

        if (explicitIds.Count > 0)
        {
            query = query.Where(s => explicitIds.Contains(s.Id));
        }

        return await query
            .OrderByDescending(s => s.Status == "ready")
            .ThenByDescending(s => s.ChunkCount)
            .ThenByDescending(s => s.UpdatedAt)
            .Take(MaxSources)
            .ToListAsync(ct);
    }

    private async Task<Dictionary<Guid, IReadOnlyList<SourceConceptLinkDto>>> LoadConceptLinksAsync(
        Guid userId,
        IReadOnlyList<LearningSource> sources,
        CancellationToken ct)
    {
        var map = new Dictionary<Guid, IReadOnlyList<SourceConceptLinkDto>>();
        foreach (var source in sources)
        {
            try
            {
                var summary = await _sourceConceptLinks.GetSourceConceptLinksAsync(userId, source.Id, ct);
                map[source.Id] = summary?.Links ?? [];
            }
            catch
            {
                map[source.Id] = [];
            }
        }

        return map;
    }

    private static MultiSourceCompareSourceDto BuildSourceSummary(
        LearningSource source,
        IReadOnlyList<SourceCitationCheck> checks,
        IReadOnlyList<SourceConceptLinkDto> links)
    {
        var supported = checks.Count(c => IsSupported(c.CheckStatus));
        var unsupported = checks.Count(c => IsUnsupported(c.CheckStatus));
        var missing = checks.Count(c => IsMissing(c.CheckStatus));
        var needsReview = checks.Count - supported;
        var coverage = checks.Count == 0 ? 0m : Math.Round(supported / (decimal)checks.Count, 4);
        var warnings = BuildSourceWarnings(source, checks, links);
        return new MultiSourceCompareSourceDto
        {
            SourceId = source.Id,
            SourceTitle = SafeDisplay(source.Title, 160),
            Status = SafeDisplay(source.Status, 60),
            SourceReadiness = ResolveSourceReadiness(source),
            EvidenceStatus = ResolveEvidenceStatus(source),
            PageCount = source.PageCount,
            ChunkCount = source.ChunkCount,
            CitationCoverage = coverage,
            CitationCheckCount = checks.Count,
            SupportedCitationCount = supported,
            UnsupportedCitationCount = unsupported,
            MissingCitationCount = missing,
            NeedsReviewCitationCount = needsReview,
            LinkedConceptCount = links.Count,
            Warnings = warnings
        };
    }

    private static MultiSourceCitationCoverageDto BuildCoverage(
        IReadOnlyList<SourceCitationCheck> checks,
        IReadOnlyList<MultiSourceCompareSourceDto> summaries)
    {
        var supported = checks.Count(c => IsSupported(c.CheckStatus));
        var unsupported = checks.Count(c => IsUnsupported(c.CheckStatus));
        var missing = checks.Count(c => IsMissing(c.CheckStatus));
        var stale = summaries.Count(s => s.SourceReadiness is "stale" or "deleted");
        var needsReview = checks.Count - supported + stale;
        var ratio = checks.Count == 0 ? 0m : Math.Round(supported / (decimal)checks.Count, 4);
        return new MultiSourceCitationCoverageDto
        {
            TotalCitationChecks = checks.Count,
            SupportedCount = supported,
            UnsupportedCount = unsupported,
            MissingCount = missing,
            StaleCount = stale,
            NeedsReviewCount = needsReview,
            CoverageRatio = ratio,
            CoverageStatus = checks.Count == 0 ? "not_checked" :
                missing > 0 ? "missing" :
                unsupported > 0 ? "needs_review" :
                stale > 0 ? "stale" :
                "supported"
        };
    }

    private static IReadOnlyList<MultiSourceConceptOverlapDto> BuildConceptOverlaps(
        IReadOnlyDictionary<Guid, IReadOnlyList<SourceConceptLinkDto>> linkMap,
        int minSourceCount)
    {
        return linkMap
            .SelectMany(kv => kv.Value.Select(l => new { SourceId = kv.Key, Link = l }))
            .Where(x => !string.IsNullOrWhiteSpace(x.Link.ConceptKey) || x.Link.WikiPageId.HasValue)
            .GroupBy(x => x.Link.WikiPageId?.ToString("N") ?? x.Link.ConceptKey, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var rows = g.ToList();
                var sourceIds = rows.Select(r => r.SourceId).Distinct().ToArray();
                var first = rows.OrderBy(r => r.Link.IsSuggestion).ThenByDescending(r => r.Link.ConfidenceScore ?? 0m).First().Link;
                return new MultiSourceConceptOverlapDto
                {
                    ConceptKey = first.ConceptKey,
                    ConceptTitle = SafeDisplay(first.ConceptTitle, 140),
                    WikiPageId = first.WikiPageId,
                    SourceIds = sourceIds,
                    SourceTitles = rows.Select(r => SafeDisplay(r.Link.SourceTitle, 120)).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToArray(),
                    LinkConfidence = ResolveGroupConfidence(rows.Select(r => r.Link.Confidence)),
                    IsSuggestion = rows.Any(r => r.Link.IsSuggestion),
                    Basis = rows.Select(r => r.Link.Basis).FirstOrDefault(b => !string.IsNullOrWhiteSpace(b)) ?? "coverage_overlap",
                    Warnings = rows.SelectMany(r => r.Link.Warnings).Select(w => SafeDisplay(w, 160)).Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToArray()
                };
            })
            .Where(o => o.SourceIds.Count >= minSourceCount)
            .OrderByDescending(o => o.SourceIds.Count)
            .ThenBy(o => o.IsSuggestion)
            .ThenBy(o => o.ConceptTitle)
            .Take(20)
            .ToArray();
    }

    private static CitationReviewResultDto BuildCitationReview(
        Guid? topicId,
        Guid? sourceId,
        IReadOnlyList<LearningSource> sources,
        IReadOnlyList<SourceCitationCheck> checks)
    {
        var summaries = sources.Select(s => BuildSourceSummary(s, checks.Where(c => c.SourceId == s.Id).ToList(), [])).ToArray();
        var coverage = BuildCoverage(checks, summaries);
        return new CitationReviewResultDto
        {
            TopicId = topicId,
            SourceId = sourceId,
            ReviewStatus = checks.Count == 0 ? "not_checked" : coverage.CoverageStatus,
            Coverage = coverage,
            Items = BuildReviewItems(checks, sources).Take(60).ToArray(),
            Warnings = BuildCitationReviewWarnings(coverage, sources).ToArray(),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static IEnumerable<CitationReviewItemDto> BuildReviewItems(
        IReadOnlyList<SourceCitationCheck> checks,
        IReadOnlyList<LearningSource> sources)
    {
        var sourceMap = sources.ToDictionary(s => s.Id);
        foreach (var check in checks.OrderByDescending(c => c.CreatedAt))
        {
            var source = check.SourceId.HasValue && sourceMap.TryGetValue(check.SourceId.Value, out var found)
                ? found
                : null;
            var status = ResolveCitationStatus(check, source);
            yield return new CitationReviewItemDto
            {
                Id = check.Id,
                CitationId = SafeDisplay(string.IsNullOrWhiteSpace(check.CitationId) ? "missing-citation" : check.CitationId, 120),
                SourceId = check.SourceId,
                SourceTitle = SafeDisplay(source?.Title ?? "Source", 140),
                SourceChunkId = check.SourceChunkId,
                PageNumber = check.PageNumber,
                ChunkIndex = check.ChunkIndex,
                SourceReadiness = source == null ? "evidence_insufficient" : ResolveSourceReadiness(source),
                EvidenceStatus = source == null ? "evidence_insufficient" : ResolveEvidenceStatus(source),
                CitationStatus = status,
                Confidence = check.Confidence,
                UserSafeWarning = BuildCitationWarning(status, check.Reason),
                CreatedAt = new DateTimeOffset(check.CreatedAt, TimeSpan.Zero)
            };
        }
    }

    private async Task<WikiBlockDto?> WriteTraceIfRequestedAsync(
        Guid userId,
        MultiSourceCompareRequestDto request,
        MultiSourceCompareResultDto result,
        IReadOnlyList<LearningSource> sources,
        CancellationToken ct)
    {
        if (!request.WriteWikiTrace || _traceWriter == null) return null;

        try
        {
            var title = "Multi-source compare";
            var content =
                $"Compared {result.ComparedSourceCount} sources. Readiness: {result.SourceReadiness}. Evidence: {result.EvidenceStatus}. " +
                $"Shared concept count: {result.SharedConcepts.Count}. Citation warnings: {result.CitationCoverage.NeedsReviewCount}. " +
                $"Sources: {string.Join(", ", sources.Select(s => SafeDisplay(s.Title, 80)).Take(6))}.";
            return await _traceWriter.RecordSourceNoteAsync(new WikiLearningTraceRequestDto
            {
                UserId = userId,
                TopicId = result.TopicId ?? request.TopicId,
                ActiveWikiPageId = request.WikiPageId,
                TraceType = "source_note",
                Title = title,
                SafeContent = content,
                SourceBasis = result.EvidenceStatus is "source_grounded" or "mixed" ? "wiki_backed" : "evidence_insufficient",
                CreatedBy = "orkalm_multi_source_compare",
                MetadataJson = SerializeSafe(new
                {
                    comparedSourceCount = result.ComparedSourceCount,
                    sharedConceptCount = result.SharedConcepts.Count,
                    citationWarningCount = result.CitationCoverage.NeedsReviewCount,
                    compareStatus = result.CompareStatus
                })
            }, ct);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> BuildSourceWarnings(
        LearningSource source,
        IReadOnlyList<SourceCitationCheck> checks,
        IReadOnlyList<SourceConceptLinkDto> links)
    {
        var warnings = new List<string>();
        if (!IsReadySource(source)) warnings.Add("source_not_ready");
        if (source.ChunkCount <= 0) warnings.Add("evidence_insufficient");
        if (checks.Count == 0) warnings.Add("citation_not_checked");
        if (checks.Any(c => IsUnsupported(c.CheckStatus))) warnings.Add("citation_unsupported");
        if (checks.Any(c => IsMissing(c.CheckStatus))) warnings.Add("citation_missing");
        if (links.Count == 0) warnings.Add("no_concept_links");
        return warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> BuildCompareWarnings(
        IReadOnlyList<MultiSourceCompareSourceDto> summaries,
        MultiSourceCitationCoverageDto coverage,
        int sharedConceptCount)
    {
        var warnings = new List<string>();
        if (summaries.Any(s => s.SourceReadiness is "stale" or "deleted" or "evidence_insufficient"))
            warnings.Add("source_readiness_degraded");
        if (coverage.TotalCitationChecks == 0)
            warnings.Add("citation_not_checked");
        if (coverage.NeedsReviewCount > 0)
            warnings.Add("citation_review_needed");
        if (sharedConceptCount == 0)
            warnings.Add("no_shared_concept_overlap");
        warnings.Add("semantic_agreement_not_claimed");
        return warnings;
    }

    private static IReadOnlyList<string> BuildCitationReviewWarnings(
        MultiSourceCitationCoverageDto coverage,
        IReadOnlyList<LearningSource> sources)
    {
        var warnings = new List<string>();
        if (coverage.TotalCitationChecks == 0) warnings.Add("citation_not_checked");
        if (coverage.UnsupportedCount > 0) warnings.Add("citation_unsupported");
        if (coverage.MissingCount > 0) warnings.Add("citation_missing");
        if (sources.Any(s => !IsReadySource(s))) warnings.Add("source_readiness_degraded");
        return warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> BuildNextActions(MultiSourceCitationCoverageDto coverage, int sharedConceptCount, int sourceOnlyCount)
    {
        var actions = new List<string> { "ask_selected_sources", "create_source_pack", "create_source_digest" };
        if (coverage.NeedsReviewCount > 0) actions.Add("review_citations");
        if (sharedConceptCount > 0) actions.Add("open_shared_concepts");
        if (sourceOnlyCount > 0) actions.Add("review_source_only_concepts");
        actions.Add("write_compare_note_to_wiki");
        return actions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ResolveAggregateEvidence(IReadOnlyList<MultiSourceCompareSourceDto> summaries)
    {
        if (summaries.Count == 0) return "evidence_insufficient";
        if (summaries.Any(s => s.EvidenceStatus is "deleted" or "stale" or "degraded")) return "degraded";
        if (summaries.All(s => s.EvidenceStatus == "source_grounded")) return "source_grounded";
        if (summaries.Any(s => s.EvidenceStatus is "source_grounded" or "mixed")) return "mixed";
        return "evidence_insufficient";
    }

    private static string ResolveAggregateReadiness(IReadOnlyList<MultiSourceCompareSourceDto> summaries)
    {
        if (summaries.Count == 0) return "evidence_insufficient";
        if (summaries.Any(s => s.SourceReadiness is "deleted" or "stale" or "degraded")) return "degraded";
        if (summaries.All(s => s.SourceReadiness == "source_ready")) return "source_ready";
        if (summaries.Any(s => s.SourceReadiness is "source_ready" or "mixed" or "source_grounded")) return "mixed";
        return "evidence_insufficient";
    }

    private static string ResolveSourceReadiness(LearningSource source)
    {
        if (source.IsDeleted || string.Equals(source.Status, "deleted", StringComparison.OrdinalIgnoreCase)) return "deleted";
        if (string.Equals(source.Status, "stale", StringComparison.OrdinalIgnoreCase)) return "stale";
        if (!IsReadySource(source) || source.ChunkCount <= 0) return "evidence_insufficient";
        return "source_ready";
    }

    private static string ResolveEvidenceStatus(LearningSource source)
    {
        if (source.IsDeleted || string.Equals(source.Status, "deleted", StringComparison.OrdinalIgnoreCase)) return "deleted";
        if (string.Equals(source.Status, "stale", StringComparison.OrdinalIgnoreCase)) return "stale";
        if (!IsReadySource(source) || source.ChunkCount <= 0) return "evidence_insufficient";
        return "source_grounded";
    }

    private static string ResolveCitationStatus(SourceCitationCheck check, LearningSource? source)
    {
        if (source == null) return "needs_review";
        if (source.Status.Equals("stale", StringComparison.OrdinalIgnoreCase)) return "stale";
        if (IsSupported(check.CheckStatus)) return "supported";
        if (IsUnsupported(check.CheckStatus)) return "unsupported";
        if (IsMissing(check.CheckStatus)) return "missing";
        return string.IsNullOrWhiteSpace(check.CheckStatus) ? "not_checked" : "needs_review";
    }

    private static string BuildCitationWarning(string status, string? reason) => status switch
    {
        "supported" => string.Empty,
        "unsupported" => "Citation retrieved evidence ile desteklenmedi; review gerekli.",
        "missing" => "Yanitta citation etiketi eksik; kaynakli iddia olarak sayilmaz.",
        "stale" => "Kaynak eski durumda; citation guvenilir kaynak iddiasi tasimaz.",
        _ => SafeDisplay(reason ?? "Citation review gerekli.", 160)
    };

    private static string ResolveGroupConfidence(IEnumerable<string> values)
    {
        var list = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.ToLowerInvariant()).ToList();
        if (list.Contains("high")) return "high";
        if (list.Contains("medium")) return "medium";
        return "low";
    }

    private static bool IsReadySource(LearningSource source) =>
        string.Equals(source.Status, "ready", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(source.Status, "processed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(source.Status, "indexed", StringComparison.OrdinalIgnoreCase);

    private static bool IsSupported(string? status) => string.Equals(status, "supported", StringComparison.OrdinalIgnoreCase);
    private static bool IsUnsupported(string? status) => string.Equals(status, "citation_unsupported", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "unsupported", StringComparison.OrdinalIgnoreCase);
    private static bool IsMissing(string? status) => string.Equals(status, "citation_missing", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "missing", StringComparison.OrdinalIgnoreCase);

    private async Task RecordTelemetryAsync(Guid userId, Guid? topicId, string operation, string status, int? count, CancellationToken ct)
    {
        if (_telemetry == null) return;
        try
        {
            await _telemetry.RecordEventAsync(userId, new LearningRuntimeEventRequestDto
            {
                TopicId = topicId,
                Category = "orkalm_source_compare",
                Operation = operation,
                Status = status,
                Severity = status is "failed" or "degraded" or "not_enough_sources" ? "warning" : "info",
                SafeMessage = "Source compare/review event recorded without raw source, answer, or citation claim text.",
                SafeMetadata = new Dictionary<string, string>
                {
                    ["count"] = count?.ToString() ?? "0",
                    ["rawContentStored"] = "false"
                }
            }, ct);
        }
        catch
        {
            // Telemetry must never block source compare/review.
        }
    }

    private static string SafeDisplay(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var safe = Regex.Replace(value.Trim(), @"\s+", " ");
        foreach (var marker in BlockedMarkers)
        {
            safe = Regex.Replace(safe, Regex.Escape(marker), "[redacted]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return safe.Length <= maxLength ? safe : safe[..maxLength];
    }

    private static string SerializeSafe(object value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return BlockedMarkers.Any(m => json.Contains(m, StringComparison.OrdinalIgnoreCase))
            ? "{}"
            : json;
    }
}
