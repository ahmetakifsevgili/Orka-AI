using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class SourceConceptLinkingService : ISourceConceptLinkingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] BlockedMarkers =
    [
        "rawPrompt", "hiddenPrompt", "systemPrompt", "rawProviderPayload", "rawSourceChunk",
        "rawToolPayload", "debugTrace", "localPath", "apiKey", "secret", "answerKey",
        "correctAnswer", "stackTrace"
    ];

    private readonly OrkaDbContext _db;
    private readonly ILearningRuntimeTelemetryService? _telemetry;

    public SourceConceptLinkingService(OrkaDbContext db, ILearningRuntimeTelemetryService? telemetry = null)
    {
        _db = db;
        _telemetry = telemetry;
    }

    public async Task<SourceConceptLinkSummaryDto?> GetSourceConceptLinksAsync(
        Guid userId,
        Guid sourceId,
        CancellationToken ct = default)
    {
        var source = await LoadSourceAsync(userId, sourceId, ct);
        if (source?.TopicId == null) return null;

        var sourcePage = await FindSourcePageAsync(userId, source, ct);
        var latestBundle = await LoadLatestBundleAsync(userId, source.TopicId.Value, ct);
        var confirmed = sourcePage == null
            ? []
            : await LoadLinksFromSourcePageAsync(userId, source, sourcePage, latestBundle, ct);
        var suggestions = await BuildCandidatesAsync(userId, source, latestBundle, sourcePage, includePersisted: false, ct);
        var all = confirmed
            .Concat(suggestions.Where(s => confirmed.All(c => c.WikiPageId != s.WikiPageId)))
            .OrderBy(l => l.IsSuggestion)
            .ThenByDescending(l => l.ConfidenceScore ?? 0m)
            .Take(16)
            .ToList();

        return BuildSummary(source.TopicId.Value, source, null, sourcePage?.Id, all, latestBundle);
    }

    public async Task<SourceConceptLinkSummaryDto?> SyncSourceConceptLinksAsync(
        Guid userId,
        Guid sourceId,
        CancellationToken ct = default)
    {
        var source = await LoadSourceAsync(userId, sourceId, ct);
        if (source?.TopicId == null) return null;

        await RecordTelemetryAsync(userId, source.TopicId, "source_concept_link_sync_requested", "started", null, ct);
        var latestBundle = await LoadLatestBundleAsync(userId, source.TopicId.Value, ct);
        var candidates = (await BuildCandidatesAsync(userId, source, latestBundle, null, includePersisted: false, ct))
            .Select(candidate =>
            {
                candidate.IsSuggestion = true;
                candidate.SourcePageId = null;
                candidate.LinkType = "source_mentions";
                return candidate;
            })
            .OrderByDescending(candidate => candidate.ConfidenceScore ?? 0m)
            .Take(20)
            .ToList();

        await RecordTelemetryAsync(userId, source.TopicId, "source_concept_link_suggested", "ok", candidates.Count, ct);
        return BuildSummary(source.TopicId.Value, source, null, null, candidates, latestBundle);
    }

    public async Task<SourceConceptGraphDto?> GetTopicSourceConceptGraphAsync(
        Guid userId,
        Guid topicId,
        CancellationToken ct = default)
    {
        var ownsTopic = await _db.Topics.AsNoTracking()
            .AnyAsync(t => t.Id == topicId && t.UserId == userId && !t.IsArchived, ct);
        if (!ownsTopic)
        {
            return new SourceConceptGraphDto
            {
                TopicId = topicId,
                GraphStatus = "not_found",
                Warnings = ["Konu bulunamadi veya kullanici kapsaminda degil."]
            };
        }

        var sources = await _db.LearningSources.AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId == topicId && !s.IsDeleted)
            .OrderByDescending(s => s.Status == "ready")
            .ThenByDescending(s => s.UpdatedAt)
            .Take(24)
            .ToListAsync(ct);
        var latestBundle = await LoadLatestBundleAsync(userId, topicId, ct);

        var nodeMap = new Dictionary<string, SourceConceptGraphNodeDto>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<SourceConceptGraphEdgeDto>();
        foreach (var source in sources)
        {
            var sourceNodeId = $"source:{source.Id:N}";
            nodeMap.TryAdd(sourceNodeId, new SourceConceptGraphNodeDto
            {
                Id = sourceNodeId,
                NodeType = "source",
                Label = SafeDisplay(source.Title, 120),
                SourceId = source.Id,
                WikiPageId = null,
                Status = SafeDisplay(source.Status, 40),
                SourceReadiness = ResolveSourceReadiness(source, latestBundle),
                EvidenceStatus = ResolveEvidenceStatus(source, latestBundle)
            });

            var candidates = await BuildCandidatesAsync(userId, source, latestBundle, null, includePersisted: false, ct);
            foreach (var candidate in candidates.Take(8))
            {
                var targetNodeId = candidate.WikiPageId.HasValue
                    ? $"concept:{candidate.WikiPageId.Value:N}"
                    : $"concept:{NormalizeKey(candidate.ConceptKey)}";
                nodeMap.TryAdd(targetNodeId, new SourceConceptGraphNodeDto
                {
                    Id = targetNodeId,
                    NodeType = "concept",
                    Label = SafeDisplay(candidate.ConceptTitle, 120),
                    WikiPageId = candidate.WikiPageId,
                    ConceptKey = candidate.ConceptKey,
                    Status = "suggested",
                    SourceReadiness = candidate.SourceReadiness,
                    EvidenceStatus = candidate.EvidenceStatus
                });
                edges.Add(new SourceConceptGraphEdgeDto
                {
                    SourceNodeId = sourceNodeId,
                    TargetNodeId = targetNodeId,
                    LinkType = "source_mentions",
                    Confidence = candidate.Confidence,
                    ConfidenceScore = candidate.ConfidenceScore,
                    Basis = candidate.Basis,
                    IsSuggestion = true,
                    Warnings = candidate.Warnings
                });
            }
        }

        await RecordTelemetryAsync(userId, topicId, "source_concept_graph_viewed", "ok", edges.Count, ct);
        return new SourceConceptGraphDto
        {
            TopicId = topicId,
            GraphStatus = edges.Count > 0 ? "ready" : "empty",
            Nodes = nodeMap.Values.OrderBy(n => n.NodeType).ThenBy(n => n.Label).ToList(),
            Edges = edges,
            Warnings = edges.Count == 0
                ? ["OrkaLM kaynak-kavram onerisi bulunamadi; Wiki graph ayridir ve otomatik yazilmaz."]
                : ["OrkaLM source graph oneridir; WikiLink veya WikiPage otomatik olusturmaz."]
        };
    }

    public async Task<SourceConceptLinkSummaryDto?> GetWikiPageSourceLinksAsync(
        Guid userId,
        Guid wikiPageId,
        CancellationToken ct = default)
    {
        var page = await _db.WikiPages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == wikiPageId && p.UserId == userId && !p.IsDeleted, ct);
        if (page == null) return null;

        var links = await _db.WikiLinks.AsNoTracking()
            .Include(l => l.SourcePage)
            .Where(l => l.UserId == userId && l.TargetPageId == page.Id && !l.IsDeleted)
            .Where(l => l.LinkType == "source_supports" || l.LinkType == "source_mentions" || l.LinkType == "source_reviews" || l.LinkType == "source_remediates")
            .OrderByDescending(l => l.Strength)
            .ThenByDescending(l => l.UpdatedAt)
            .Take(20)
            .ToListAsync(ct);

        var dtos = links.Select(l => new SourceConceptLinkDto
        {
            SourceId = TryReadGuid(l.MetadataJson, "sourceId"),
            SourceTitle = SafeDisplay(l.SourcePage?.Title ?? TryReadString(l.MetadataJson, "sourceTitle") ?? "Source", 120),
            SourcePageId = l.SourcePageId,
            ConceptKey = page.ConceptKey ?? TryReadString(l.MetadataJson, "conceptKey") ?? string.Empty,
            ConceptTitle = SafeDisplay(page.Title, 120),
            WikiPageId = page.Id,
            LinkType = l.LinkType,
            Confidence = TryReadString(l.MetadataJson, "confidence") ?? ConfidenceFromStrength(l.Strength),
            ConfidenceScore = l.Strength,
            Basis = TryReadString(l.MetadataJson, "basis") ?? "existing_wiki_link",
            EvidenceStatus = l.SourcePage?.EvidenceStatus ?? page.EvidenceStatus,
            SourceReadiness = l.SourcePage?.SourceReadiness ?? page.SourceReadiness,
            IsSuggestion = false,
            Warnings = LinkWarnings(l.SourcePage?.SourceReadiness ?? page.SourceReadiness, l.SourcePage?.EvidenceStatus ?? page.EvidenceStatus),
            CreatedAt = new DateTimeOffset(l.CreatedAt, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(l.UpdatedAt, TimeSpan.Zero)
        }).ToList();

        return new SourceConceptLinkSummaryDto
        {
            TopicId = page.TopicId,
            WikiPageId = page.Id,
            Title = $"{SafeDisplay(page.Title, 120)} supporting sources",
            SourceReadiness = page.SourceReadiness,
            EvidenceStatus = page.EvidenceStatus,
            ConfirmedLinkCount = dtos.Count,
            Links = dtos,
            Warnings = dtos.Count == 0 ? ["Bu concept sayfasi icin kaynak linki henuz yok."] : [],
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<LearningSource?> LoadSourceAsync(Guid userId, Guid sourceId, CancellationToken ct) =>
        await _db.LearningSources.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sourceId && s.UserId == userId && !s.IsDeleted, ct);

    private async Task<SourceEvidenceBundle?> LoadLatestBundleAsync(Guid userId, Guid topicId, CancellationToken ct) =>
        await _db.SourceEvidenceBundles.AsNoTracking()
            .Where(b => b.UserId == userId && b.TopicId == topicId && !b.IsDeleted)
            .OrderByDescending(b => b.UpdatedAt)
            .ThenByDescending(b => b.CreatedAt)
            .FirstOrDefaultAsync(ct);

    private async Task<WikiPage?> FindSourcePageAsync(Guid userId, LearningSource source, CancellationToken ct)
    {
        if (!source.TopicId.HasValue) return null;
        var pageKey = SourcePageKey(source.Id);
        return await _db.WikiPages
            .FirstOrDefaultAsync(p => p.UserId == userId &&
                                      p.TopicId == source.TopicId.Value &&
                                      !p.IsDeleted &&
                                      (p.PageKey == pageKey || (p.PageType == "orkalm_source" && p.MetadataJson.Contains(source.Id.ToString()))),
                ct);
    }

    private async Task<IReadOnlyList<SourceConceptLinkDto>> BuildCandidatesAsync(
        Guid userId,
        LearningSource source,
        SourceEvidenceBundle? bundle,
        WikiPage? sourcePage,
        bool includePersisted,
        CancellationToken ct)
    {
        if (!source.TopicId.HasValue) return [];
        var conceptPages = await _db.WikiPages.AsNoTracking()
            .Where(p => p.UserId == userId && p.TopicId == source.TopicId.Value && !p.IsDeleted)
            .Where(p => p.PageType != "orkalm_source" && p.PageType != "source_note")
            .Where(p => p.ConceptKey != null || p.PageType == "concept" || p.PageType == "topic_root")
            .OrderBy(p => p.OrderIndex)
            .ThenBy(p => p.Title)
            .Take(80)
            .ToListAsync(ct);
        if (conceptPages.Count == 0) return [];

        var text = await BuildInternalSourceMatchTextAsync(source.Id, ct);
        var sourceReadiness = ResolveSourceReadiness(source, bundle);
        var evidenceStatus = ResolveEvidenceStatus(source, bundle);
        var links = new List<SourceConceptLinkDto>();
        foreach (var page in conceptPages)
        {
            var score = ScoreCandidate(source, text, page, out var basis);
            if (score < 0.35m) continue;

            var confidence = score >= 0.82m ? "high" : score >= 0.58m ? "medium" : "low";
            var sourceReady = evidenceStatus is "source_grounded" or "mixed";
            var suggestion = confidence == "low" || !sourceReady;
            links.Add(new SourceConceptLinkDto
            {
                SourceId = source.Id,
                SourceTitle = SafeDisplay(source.Title, 120),
                SourcePageId = sourcePage?.Id,
                ConceptKey = page.ConceptKey ?? NormalizeKey(page.Title),
                ConceptTitle = SafeDisplay(page.Title, 120),
                WikiPageId = page.Id,
                LinkType = !suggestion && confidence == "high" ? "source_supports" : "source_mentions",
                Confidence = confidence,
                ConfidenceScore = score,
                Basis = basis,
                EvidenceStatus = evidenceStatus,
                SourceReadiness = sourceReadiness,
                IsSuggestion = suggestion,
                Warnings = LinkWarnings(sourceReadiness, evidenceStatus)
            });
        }

        return links
            .OrderBy(l => l.IsSuggestion)
            .ThenByDescending(l => l.ConfidenceScore ?? 0m)
            .Take(includePersisted ? 20 : 16)
            .ToList();
    }

    private async Task<string> BuildInternalSourceMatchTextAsync(Guid sourceId, CancellationToken ct)
    {
        var chunks = await _db.SourceChunks.AsNoTracking()
            .Where(c => c.LearningSourceId == sourceId)
            .OrderBy(c => c.ChunkIndex)
            .Take(24)
            .Select(c => new { c.Text, c.HighlightHint })
            .ToListAsync(ct);
        return NormalizeSearchText(string.Join(' ', chunks.SelectMany(c => new[] { c.HighlightHint, c.Text })));
    }

    private async Task<IReadOnlyList<SourceConceptLinkDto>> LoadLinksFromSourcePageAsync(
        Guid userId,
        LearningSource source,
        WikiPage sourcePage,
        SourceEvidenceBundle? bundle,
        CancellationToken ct)
    {
        var rows = await _db.WikiLinks.AsNoTracking()
            .Include(l => l.TargetPage)
            .Where(l => l.UserId == userId && l.SourcePageId == sourcePage.Id && !l.IsDeleted)
            .Where(l => l.LinkType == "source_supports" || l.LinkType == "source_mentions" || l.LinkType == "source_reviews" || l.LinkType == "source_remediates")
            .OrderByDescending(l => l.Strength)
            .ThenByDescending(l => l.UpdatedAt)
            .Take(20)
            .ToListAsync(ct);
        return rows.Select(l => new SourceConceptLinkDto
        {
            SourceId = source.Id,
            SourceTitle = SafeDisplay(source.Title, 120),
            SourcePageId = sourcePage.Id,
            ConceptKey = l.TargetPage?.ConceptKey ?? TryReadString(l.MetadataJson, "conceptKey") ?? string.Empty,
            ConceptTitle = SafeDisplay(l.TargetPage?.Title ?? l.TargetPageKey, 120),
            WikiPageId = l.TargetPageId,
            LinkType = l.LinkType,
            Confidence = TryReadString(l.MetadataJson, "confidence") ?? ConfidenceFromStrength(l.Strength),
            ConfidenceScore = l.Strength,
            Basis = TryReadString(l.MetadataJson, "basis") ?? "existing_wiki_link",
            EvidenceStatus = ResolveEvidenceStatus(source, bundle),
            SourceReadiness = ResolveSourceReadiness(source, bundle),
            IsSuggestion = false,
            Warnings = LinkWarnings(ResolveSourceReadiness(source, bundle), ResolveEvidenceStatus(source, bundle)),
            CreatedAt = new DateTimeOffset(l.CreatedAt, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(l.UpdatedAt, TimeSpan.Zero)
        }).ToList();
    }

    private static SourceConceptLinkSummaryDto BuildSummary(
        Guid topicId,
        LearningSource? source,
        WikiPage? wikiPage,
        Guid? sourcePageId,
        IReadOnlyList<SourceConceptLinkDto> links,
        SourceEvidenceBundle? bundle)
    {
        var sourceReadiness = source == null ? wikiPage?.SourceReadiness ?? "evidence_insufficient" : ResolveSourceReadiness(source, bundle);
        var evidenceStatus = source == null ? wikiPage?.EvidenceStatus ?? "evidence_insufficient" : ResolveEvidenceStatus(source, bundle);
        return new SourceConceptLinkSummaryDto
        {
            TopicId = topicId,
            SourceId = source?.Id,
            WikiPageId = wikiPage?.Id ?? sourcePageId,
            Title = source != null
                ? $"{SafeDisplay(source.Title, 120)} related concepts"
                : $"{SafeDisplay(wikiPage?.Title, 120)} supporting sources",
            SourceReadiness = sourceReadiness,
            EvidenceStatus = evidenceStatus,
            ConfirmedLinkCount = links.Count(l => !l.IsSuggestion),
            SuggestedLinkCount = links.Count(l => l.IsSuggestion),
            Links = links,
            Warnings = BuildSummaryWarnings(sourceReadiness, evidenceStatus, links.Count),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static decimal ScoreCandidate(LearningSource source, string internalSourceText, WikiPage page, out string basis)
    {
        var sourceHeader = NormalizeSearchText($"{source.Title} {source.FileName}");
        var conceptKey = NormalizeSearchText(page.ConceptKey);
        var title = NormalizeSearchText(page.Title);
        var combined = $"{sourceHeader} {internalSourceText}";

        if (!string.IsNullOrWhiteSpace(conceptKey) && ContainsToken(combined, conceptKey))
        {
            basis = "concept_key_match";
            return sourceHeader.Contains(conceptKey, StringComparison.Ordinal) ? 0.92m : 0.84m;
        }

        if (!string.IsNullOrWhiteSpace(title) && ContainsPhrase(combined, title))
        {
            basis = sourceHeader.Contains(title, StringComparison.Ordinal) ? "title_match" : "citation_label_match";
            return sourceHeader.Contains(title, StringComparison.Ordinal) ? 0.78m : 0.62m;
        }

        var tokens = title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length >= 4).Distinct().ToList();
        var matches = tokens.Count(t => ContainsToken(combined, t));
        if (tokens.Count > 0 && matches > 0)
        {
            basis = "title_match";
            return Math.Min(0.55m, 0.34m + (matches * 0.08m));
        }

        basis = "plan_context";
        return 0m;
    }

    private static string ResolveSourceReadiness(LearningSource source, SourceEvidenceBundle? bundle)
    {
        if (source.IsDeleted || string.Equals(source.Status, "deleted", StringComparison.OrdinalIgnoreCase)) return "deleted";
        if (string.Equals(source.Status, "stale", StringComparison.OrdinalIgnoreCase)) return "stale";
        if (!string.Equals(source.Status, "ready", StringComparison.OrdinalIgnoreCase) || source.ChunkCount <= 0) return "evidence_insufficient";
        return bundle?.EvidenceStatus is "source_grounded" or "mixed" ? bundle.EvidenceStatus : "source_grounded";
    }

    private static string ResolveEvidenceStatus(LearningSource source, SourceEvidenceBundle? bundle)
    {
        if (source.IsDeleted || string.Equals(source.Status, "deleted", StringComparison.OrdinalIgnoreCase)) return "deleted";
        if (string.Equals(source.Status, "stale", StringComparison.OrdinalIgnoreCase)) return "stale";
        if (!string.Equals(source.Status, "ready", StringComparison.OrdinalIgnoreCase) || source.ChunkCount <= 0) return "evidence_insufficient";
        return bundle?.EvidenceStatus ?? "source_grounded";
    }

    private static IReadOnlyList<string> LinkWarnings(string sourceReadiness, string evidenceStatus)
    {
        var warnings = new List<string>();
        if (sourceReadiness is "stale" or "deleted" || evidenceStatus is "stale" or "deleted")
        {
            warnings.Add("Kaynak durumu eski/silinmis; link guvenilir kaynak iddiasi tasimaz.");
        }
        else if (evidenceStatus is not "source_grounded" and not "mixed")
        {
            warnings.Add("Kaynak kaniti sinirli; link oneridir, kesin kaynakli iddia degildir.");
        }

        return warnings;
    }

    private static IReadOnlyList<string> BuildSummaryWarnings(string sourceReadiness, string evidenceStatus, int linkCount)
    {
        var warnings = LinkWarnings(sourceReadiness, evidenceStatus).ToList();
        if (linkCount == 0)
        {
            warnings.Add("Kaynak-kavram onerisi bulunamadi; OrkaLM grafi Wiki'ye otomatik yazmaz.");
        }

        return warnings;
    }

    private async Task RecordTelemetryAsync(Guid userId, Guid? topicId, string eventName, string status, int? count, CancellationToken ct)
    {
        if (_telemetry == null) return;
        try
        {
            await _telemetry.RecordEventAsync(userId, new LearningRuntimeEventRequestDto
            {
                TopicId = topicId,
                Category = "orkalm_source_graph",
                Operation = eventName,
                Status = status,
                SafeMessage = count.HasValue ? $"{eventName}: {count.Value}" : eventName,
                SafeMetadata = count.HasValue
                    ? new Dictionary<string, string> { ["count"] = count.Value.ToString() }
                    : new Dictionary<string, string>()
            }, ct);
        }
        catch
        {
            // Telemetry must not block source notebook usage.
        }
    }

    private static string SourcePageKey(Guid sourceId) => $"orkalm-source:{sourceId:N}";

    private static string StatusFor(string evidenceStatus) => evidenceStatus switch
    {
        "source_grounded" or "mixed" or "wiki_backed" => "ready",
        "stale" => "stale",
        "deleted" or "degraded" => "degraded",
        _ => "degraded"
    };

    private static string ConfidenceFromStrength(decimal value) => value >= 0.80m ? "high" : value >= 0.55m ? "medium" : "low";

    private static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = NormalizeSearchText(value);
        normalized = Regex.Replace(normalized, @"[^a-z0-9_\-:.]+", "-");
        return normalized.Trim('-');
    }

    private static string NormalizeSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var lower = value.Trim().ToLowerInvariant();
        lower = Regex.Replace(lower, @"[_\-:.]+", " ");
        lower = Regex.Replace(lower, @"\s+", " ");
        return lower.Trim();
    }

    private static bool ContainsPhrase(string text, string phrase) =>
        !string.IsNullOrWhiteSpace(text) &&
        !string.IsNullOrWhiteSpace(phrase) &&
        text.Contains(phrase, StringComparison.Ordinal);

    private static bool ContainsToken(string text, string token)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token)) return false;
        var pattern = $@"(^|\s){Regex.Escape(token)}($|\s)";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string SafeDisplay(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var cleaned = Regex.Replace(value.Trim(), @"\s+", " ");
        foreach (var marker in BlockedMarkers)
        {
            cleaned = Regex.Replace(cleaned, Regex.Escape(marker), "[redacted]", RegexOptions.IgnoreCase);
        }

        return cleaned.Length <= max ? cleaned : cleaned[..max];
    }

    private static string SerializeSafe(object value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return BlockedMarkers.Any(m => json.Contains(m, StringComparison.OrdinalIgnoreCase))
            ? "{}"
            : json;
    }

    private static Guid? TryReadGuid(string? json, string property)
    {
        var value = TryReadString(json, property);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private static string? TryReadString(string? json, string property)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String
                ? SafeDisplay(prop.GetString(), 160)
                : null;
        }
        catch
        {
            return null;
        }
    }
}
