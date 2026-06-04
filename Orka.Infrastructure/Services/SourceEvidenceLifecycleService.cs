using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class SourceEvidenceLifecycleService : ISourceEvidenceLifecycleService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private readonly OrkaDbContext _db;
    private readonly IWikiEvidenceService _wikiEvidence;
    private readonly IActiveLessonSnapshotService? _snapshots;
    private readonly ILogger<SourceEvidenceLifecycleService> _logger;

    public SourceEvidenceLifecycleService(
        OrkaDbContext db,
        IWikiEvidenceService wikiEvidence,
        ILogger<SourceEvidenceLifecycleService> logger,
        IActiveLessonSnapshotService? snapshots = null)
    {
        _db = db;
        _wikiEvidence = wikiEvidence;
        _logger = logger;
        _snapshots = snapshots;
    }

    public async Task<SourceEvidenceBundleDto> BuildSourceEvidenceBundleAsync(
        Guid userId,
        Guid topicId,
        Guid? sessionId = null,
        string? question = null,
        CancellationToken ct = default)
    {
        var topic = await EnsureTopicAsync(userId, topicId, ct);
        var lifecycle = await GetSourceLifecycleSummaryAsync(userId, topicId, ct);
        var evidence = await _wikiEvidence.BuildAsync(new WikiLearningRequestDto
        {
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            Question = string.IsNullOrWhiteSpace(question) ? topic.Title : question.Trim()
        }, ct);

        var items = evidence.SourceChunks
            .Where(c => c.ChunkId.HasValue)
            .Select(c => new SourceEvidenceItemDto
            {
                SourceId = c.SourceId,
                ChunkId = c.ChunkId,
                SourceType = "document",
                Title = Clean(c.SourceTitle, 220) ?? "Kaynak",
                Label = Clean($"{c.SourceTitle} / s.{c.PageNumber}", 260) ?? "Kaynak",
                PageNumber = c.PageNumber,
                SnippetSummary = Summarize(c.Text, 360),
                Confidence = c.FusedScore,
                ScopeRelation = Clean(c.ScopeRelation, 64) ?? "unknown",
                RetrievalScope = Clean(c.RetrievalScope, 64) ?? "unknown",
                Status = IsLowConfidence(c.QualityStatus) ? "degraded" : "active",
                UserSafeWarning = IsLowConfidence(c.QualityStatus) ? "Bu kaynak parcasinin eslesme guveni dusuk." : null
            })
            .Take(16)
            .ToList();

        var warnings = BuildWarnings(lifecycle, evidence, items).ToArray();
        var status = DetermineEvidenceStatus(items.Count, evidence.WikiBlocks.Count, lifecycle);
        var hash = HashBundle(topicId, sessionId, status, items, lifecycle);
        var now = DateTime.UtcNow;

        var entity = new SourceEvidenceBundle
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            BundleHash = hash,
            EvidenceStatus = status,
            SourceCount = lifecycle.SourceCount,
            ReadySourceCount = lifecycle.ReadySourceCount,
            ChunkCount = items.Count,
            CitationCoverage = evidence.CitationCoverage,
            UnsupportedCitationCount = evidence.UnsupportedCitationCount,
            StaleEvidenceCount = lifecycle.StaleSourceCount + lifecycle.FailedSourceCount,
            DeletedEvidenceCount = lifecycle.DeletedSourceCount,
            EvidenceJson = JsonSerializer.Serialize(new SourceEvidenceBundlePayload(items, warnings), JsonOptions),
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.AddHours(6)
        };

        _db.SourceEvidenceBundles.Add(entity);
        await _db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<SourceEvidenceBundleDto?> GetLatestSourceEvidenceBundleAsync(
        Guid userId,
        Guid topicId,
        Guid? sessionId = null,
        CancellationToken ct = default)
    {
        var entity = await _db.SourceEvidenceBundles
            .AsNoTracking()
            .Where(b =>
                b.UserId == userId &&
                !b.IsDeleted &&
                b.TopicId == topicId &&
                (!sessionId.HasValue || b.SessionId == sessionId))
            .OrderByDescending(b => b.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        return entity == null ? null : ToDto(entity);
    }

    public async Task<SourceLifecycleSummaryDto> GetSourceLifecycleSummaryAsync(
        Guid userId,
        Guid topicId,
        CancellationToken ct = default)
    {
        await EnsureTopicAsync(userId, topicId, ct);
        var sources = await _db.LearningSources
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId == topicId)
            .Select(s => new
            {
                s.Id,
                s.Status,
                s.IsDeleted,
                ActiveChunkCount = s.Chunks.Count(c => !c.IsDeleted)
            })
            .ToListAsync(ct);

        var ready = sources.Count(s => IsReady(s.Status, s.IsDeleted) && s.ActiveChunkCount > 0);
        var stale = sources.Count(s => IsStaleOrDegraded(s.Status, s.IsDeleted));
        var deleted = sources.Count(s => s.IsDeleted || string.Equals(s.Status, "deleted", StringComparison.OrdinalIgnoreCase));
        var failed = sources.Count(s => IsFailed(s.Status, s.IsDeleted));
        var activeChunks = sources
            .Where(s => IsReady(s.Status, s.IsDeleted))
            .Sum(s => s.ActiveChunkCount);
        var status = activeChunks > 0
            ? "source_grounded"
            : stale > 0 ? "stale" :
            failed > 0 ? "degraded" :
            "evidence_insufficient";

        return new SourceLifecycleSummaryDto
        {
            TopicId = topicId,
            SourceCount = sources.Count(s => !s.IsDeleted),
            ReadySourceCount = ready,
            StaleSourceCount = stale,
            DeletedSourceCount = deleted,
            FailedSourceCount = failed,
            ActiveChunkCount = activeChunks,
            EvidenceStatus = status,
            Warnings = BuildLifecycleWarnings(ready, stale, deleted, failed, activeChunks)
        };
    }

    public async Task<bool> MarkSourceStaleAsync(
        Guid userId,
        Guid sourceId,
        string reason,
        CancellationToken ct = default)
    {
        var source = await _db.LearningSources
            .FirstOrDefaultAsync(s => s.Id == sourceId && s.UserId == userId && !s.IsDeleted, ct);
        if (source == null)
        {
            return false;
        }

        source.Status = "stale";
        source.UpdatedAt = DateTime.UtcNow;
        source.Version += 1;
        await RecordLifecycleEventAsync(userId, source.TopicId, source.Id, "stale", reason, "Kaynak stale olarak isaretlendi.", ct);
        await DegradeBundlesAsync(userId, source.TopicId, "stale", reason, ct);
        if (_snapshots != null)
        {
            await _snapshots.MarkActiveLessonSnapshotStaleAsync(userId, source.TopicId, source.SessionId, "source_stale", ct);
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> InvalidateEvidenceForSourceAsync(
        Guid userId,
        Guid sourceId,
        string reason,
        CancellationToken ct = default)
    {
        var source = await _db.LearningSources
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sourceId && s.UserId == userId, ct);
        if (source == null)
        {
            return false;
        }

        await RecordLifecycleEventAsync(userId, source.TopicId, source.Id, "evidence_invalidated", reason, "Kaynak evidence bundle'lari gecersizlestirildi.", ct);
        await DegradeBundlesAsync(userId, source.TopicId, "degraded", reason, ct);
        if (_snapshots != null)
        {
            await _snapshots.MarkActiveLessonSnapshotStaleAsync(userId, source.TopicId, source.SessionId, "source_evidence_invalidated", ct);
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<SourceCitationSetValidationDto> ValidateCitationSetAsync(
        Guid userId,
        ValidateSourceCitationSetRequestDto request,
        CancellationToken ct = default)
    {
        var results = new List<SourceCitationValidationResultDto>();
        foreach (var citation in request.Citations.Take(40))
        {
            var query = _db.SourceChunks
                .AsNoTracking()
                .Include(c => c.LearningSource)
                .Where(c => c.LearningSource.UserId == userId);

            if (citation.ChunkId.HasValue)
            {
                query = query.Where(c => c.Id == citation.ChunkId.Value);
            }
            else if (citation.SourceId.HasValue)
            {
                query = query.Where(c => c.LearningSourceId == citation.SourceId.Value);
                if (citation.PageNumber.HasValue)
                {
                    query = query.Where(c => c.PageNumber == citation.PageNumber.Value);
                }
                if (citation.ChunkIndex.HasValue)
                {
                    query = query.Where(c => c.ChunkIndex == citation.ChunkIndex.Value);
                }
            }
            else
            {
                results.Add(UnsupportedCitation(citation, "citation_missing_source"));
                continue;
            }

            var chunk = await query.OrderBy(c => c.ChunkIndex).FirstOrDefaultAsync(ct);
            if (chunk == null)
            {
                results.Add(UnsupportedCitation(citation, "citation_not_found"));
                continue;
            }

            var active = !chunk.IsDeleted &&
                         !chunk.LearningSource.IsDeleted &&
                         IsReady(chunk.LearningSource.Status, chunk.LearningSource.IsDeleted);
            results.Add(new SourceCitationValidationResultDto
            {
                CitationId = Clean(citation.CitationId, 220) ?? BuildCitationId(chunk),
                Supported = active,
                Status = active ? "supported" : ResolveInactiveCitationStatus(chunk.LearningSource.Status),
                SourceType = "document",
                UserSafeWarning = active ? null : "Bu citation aktif ve guvenilir kaynak parcasi ile eslesmiyor.",
                SourceId = chunk.LearningSourceId,
                ChunkId = chunk.Id,
                PageNumber = chunk.PageNumber
            });
        }

        return new SourceCitationSetValidationDto
        {
            TotalCount = results.Count,
            SupportedCount = results.Count(r => r.Supported),
            UnsupportedCount = results.Count(r => !r.Supported),
            Results = results
        };
    }

    public async Task<WikiKnowledgeNotebookDto> BuildWikiKnowledgeNotebookAsync(
        Guid userId,
        Guid topicId,
        CancellationToken ct = default)
    {
        var topic = await EnsureTopicAsync(userId, topicId, ct);
        var bundle = await GetLatestSourceEvidenceBundleAsync(userId, topicId, null, ct)
                     ?? await BuildSourceEvidenceBundleAsync(userId, topicId, null, topic.Title, ct);

        var wikiBlocks = await _db.WikiBlocks
            .AsNoTracking()
            .Include(b => b.WikiPage)
            .Where(b =>
                b.WikiPage.UserId == userId &&
                b.WikiPage.TopicId == topicId &&
                !b.IsDeleted &&
                !b.WikiPage.IsDeleted &&
                b.WikiPage.PageType != "orkalm_source" &&
                b.WikiPage.PageType != "source_note" &&
                b.WikiPage.PageType != "source_notebook")
            .OrderBy(b => b.WikiPage.OrderIndex)
            .ThenBy(b => b.OrderIndex)
            .Take(80)
            .Select(b => new { b.Id, b.WikiPage.Title, b.Content })
            .ToListAsync(ct);

        var concepts = await LoadConceptKeysAsync(userId, topicId, ct);
        var latestKorteks = await _db.KorteksResearchWorkflows
            .AsNoTracking()
            .Where(w => w.UserId == userId && !w.IsDeleted && w.TopicId == topicId)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new { w.Id, w.GroundingMode, w.SourceCount, w.Status })
            .FirstOrDefaultAsync(ct);

        var sections = new List<WikiNotebookSectionDto>();
        sections.Add(new WikiNotebookSectionDto
        {
            SectionKey = "source-evidence",
            Title = "Kaynak dayanaklari",
            EvidenceItems = bundle.EvidenceItems.Take(8).ToArray(),
            SourceIds = bundle.EvidenceItems.Select(i => i.SourceId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().Take(12).ToArray(),
            Status = bundle.EvidenceStatus
        });

        if (wikiBlocks.Count > 0)
        {
            sections.Add(new WikiNotebookSectionDto
            {
                SectionKey = "wiki-notes",
                Title = "Wiki notlari",
                WikiBlockIds = wikiBlocks.Select(b => b.Id).Take(20).ToArray(),
                Status = "wiki_backed"
            });
        }

        foreach (var concept in concepts.Take(6))
        {
            sections.Add(new WikiNotebookSectionDto
            {
                SectionKey = $"concept-{Slug(concept)}",
                Title = concept,
                ConceptKey = concept,
                EvidenceItems = bundle.EvidenceItems
                    .Where(i => ContainsNormalized(i.SnippetSummary, concept) || ContainsNormalized(i.Title, concept))
                    .Take(4)
                    .ToArray(),
                WikiBlockIds = wikiBlocks
                    .Where(b => ContainsNormalized(b.Content, concept) || ContainsNormalized(b.Title, concept))
                    .Select(b => b.Id)
                    .Take(6)
                    .ToArray(),
                Status = "organized_seed"
            });
        }

        var warnings = bundle.Warnings.ToList();
        if (latestKorteks != null)
        {
            warnings.Add("Korteks synthesis yalnizca external research seed olarak kullanilir; yuklenen kaynak hakikati sayilmaz.");
            sections.Add(new WikiNotebookSectionDto
            {
                SectionKey = "korteks-external-seed",
                Title = "Korteks external research seed",
                Status = latestKorteks.Status == "ready" && latestKorteks.SourceCount > 0
                    ? "external_research_seed"
                    : "degraded"
            });
        }

        var dto = new WikiKnowledgeNotebookDto
        {
            TopicId = topicId,
            Title = topic.Title,
            EvidenceStatus = bundle.EvidenceStatus,
            SourceCoverage = BuildCoverageLabel(bundle.ReadySourceCount, bundle.ChunkCount),
            ConceptCoverage = concepts.Count == 0 ? "concept_graph_missing" : "concept_seeded",
            Sections = sections,
            SourceWarnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            NextActions = BuildWikiNotebookActions(concepts.Count, bundle.EvidenceStatus),
            LastUpdatedAt = DateTime.UtcNow
        };

        var entity = new WikiKnowledgeNotebookSnapshot
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            EvidenceStatus = dto.EvidenceStatus,
            SourceCoverage = dto.SourceCoverage,
            ConceptCoverage = dto.ConceptCoverage,
            SectionsJson = JsonSerializer.Serialize(dto.Sections, JsonOptions),
            SourceWarningsJson = JsonSerializer.Serialize(dto.SourceWarnings, JsonOptions),
            CreatedAt = dto.LastUpdatedAt,
            UpdatedAt = dto.LastUpdatedAt
        };
        _db.WikiKnowledgeNotebookSnapshots.Add(entity);
        await _db.SaveChangesAsync(ct);
        return dto;
    }

    public async Task<WikiKnowledgeNotebookDto?> GetLatestWikiKnowledgeNotebookAsync(
        Guid userId,
        Guid topicId,
        CancellationToken ct = default)
    {
        var topic = await _db.Topics
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == topicId && t.UserId == userId && !t.IsArchived, ct);
        if (topic == null)
        {
            return null;
        }

        var entity = await _db.WikiKnowledgeNotebookSnapshots
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.TopicId == topicId && !n.IsDeleted)
            .OrderByDescending(n => n.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        if (entity == null)
        {
            return null;
        }

        return new WikiKnowledgeNotebookDto
        {
            TopicId = topicId,
            Title = topic.Title,
            EvidenceStatus = entity.EvidenceStatus,
            SourceCoverage = entity.SourceCoverage,
            ConceptCoverage = entity.ConceptCoverage,
            Sections = Parse(entity.SectionsJson, Array.Empty<WikiNotebookSectionDto>()),
            SourceWarnings = Parse(entity.SourceWarningsJson, Array.Empty<string>()),
            NextActions = BuildWikiNotebookActions(Parse(entity.SectionsJson, Array.Empty<WikiNotebookSectionDto>()).Count(s => !string.IsNullOrWhiteSpace(s.ConceptKey)), entity.EvidenceStatus),
            LastUpdatedAt = entity.UpdatedAt
        };
    }

    private static IReadOnlyList<NotebookStudioNextActionDto> BuildWikiNotebookActions(int conceptCount, string? evidenceStatus)
    {
        var actions = new List<NotebookStudioNextActionDto>
        {
            new() { ActionType = "briefing_doc", UserSafeLabel = "Create a Wiki briefing.", Priority = "high" },
            new() { ActionType = "study_guide", UserSafeLabel = "Turn this Wiki context into a study guide.", Priority = "high" },
            new() { ActionType = "glossary", UserSafeLabel = "Extract a Wiki-scoped glossary.", Priority = "normal" },
            new() { ActionType = "timeline", UserSafeLabel = "Create a Wiki-scoped timeline.", Priority = "normal" },
            new() { ActionType = "flashcard_set", UserSafeLabel = "Create Wiki-scoped flashcards.", Priority = "normal" },
            new() { ActionType = "review_quiz", UserSafeLabel = "Start a Wiki-scoped review quiz.", Priority = "normal" },
            new() { ActionType = "mind_map", UserSafeLabel = "Build a Wiki mind map.", Priority = "normal" },
            new() { ActionType = "uml_diagram", UserSafeLabel = "Create a Mermaid/UML diagram for this Wiki context.", Priority = "normal" },
            new() { ActionType = "slide_deck_outline", UserSafeLabel = "Build a Wiki slide outline.", Priority = "normal" },
            new() { ActionType = "search_filter", UserSafeLabel = "Search and filter Wiki notes.", Priority = "normal" },
            new() { ActionType = "templates", UserSafeLabel = "Use Wiki note templates.", Priority = "normal" }
        };
        if (conceptCount <= 0)
        {
            actions.Insert(0, new NotebookStudioNextActionDto
            {
                ActionType = "sync_wiki_graph",
                UserSafeLabel = "Build the Wiki graph before relying on concept coverage.",
                Priority = "high"
            });
        }
        if (string.Equals(evidenceStatus, "stale", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(evidenceStatus, "degraded", StringComparison.OrdinalIgnoreCase))
        {
            actions.Insert(0, new NotebookStudioNextActionDto
            {
                ActionType = "review_wiki_evidence",
                UserSafeLabel = "Review Wiki evidence status before exporting.",
                Priority = "high"
            });
        }
        return actions;
    }

    private async Task<Topic> EnsureTopicAsync(Guid userId, Guid topicId, CancellationToken ct)
    {
        return await _db.Topics
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == topicId && t.UserId == userId && !t.IsArchived, ct)
            ?? throw new InvalidOperationException("Topic not found for source evidence lifecycle.");
    }

    private async Task RecordLifecycleEventAsync(
        Guid userId,
        Guid? topicId,
        Guid? sourceId,
        string eventType,
        string? reason,
        string safeSummary,
        CancellationToken ct)
    {
        _db.SourceLifecycleEvents.Add(new SourceLifecycleEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SourceId = sourceId,
            EventType = Clean(eventType, 96) ?? "updated",
            Reason = Clean(reason, 512),
            SafeSummary = Clean(safeSummary, 1000),
            CreatedAt = DateTime.UtcNow
        });
        await Task.CompletedTask;
    }

    private async Task DegradeBundlesAsync(Guid userId, Guid? topicId, string status, string? reason, CancellationToken ct)
    {
        var bundles = await _db.SourceEvidenceBundles
            .Where(b => b.UserId == userId && !b.IsDeleted && b.TopicId == topicId)
            .ToListAsync(ct);

        foreach (var bundle in bundles)
        {
            bundle.EvidenceStatus = status;
            bundle.UpdatedAt = DateTime.UtcNow;
            bundle.EvidenceJson = AppendWarning(bundle.EvidenceJson, $"source_lifecycle_{status}: {Clean(reason, 160) ?? "kaynak durumu degisti"}");
        }
    }

    private async Task<IReadOnlyList<string>> LoadConceptKeysAsync(Guid userId, Guid topicId, CancellationToken ct)
    {
        var graphId = await _db.ConceptGraphSnapshots
            .AsNoTracking()
            .Where(g => g.UserId == userId && g.TopicId == topicId)
            .OrderByDescending(g => g.CreatedAt)
            .Select(g => (Guid?)g.Id)
            .FirstOrDefaultAsync(ct);
        if (!graphId.HasValue)
        {
            return Array.Empty<string>();
        }

        return await _db.LearningConcepts
            .AsNoTracking()
            .Where(c => c.ConceptGraphSnapshotId == graphId.Value)
            .OrderBy(c => c.Order)
            .Select(c => c.Label)
            .Take(12)
            .ToListAsync(ct);
    }

    private static SourceEvidenceBundleDto ToDto(SourceEvidenceBundle entity)
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

    private static string DetermineEvidenceStatus(int sourceChunkCount, int wikiBlockCount, SourceLifecycleSummaryDto lifecycle)
    {
        if (sourceChunkCount > 0 && wikiBlockCount > 0) return "mixed";
        if (sourceChunkCount > 0) return "source_grounded";
        if (wikiBlockCount > 0) return "wiki_backed";
        if (lifecycle.StaleSourceCount > 0) return "stale";
        if (lifecycle.FailedSourceCount > 0) return "degraded";
        return "evidence_insufficient";
    }

    private static IEnumerable<string> BuildWarnings(
        SourceLifecycleSummaryDto lifecycle,
        WikiEvidenceBundleDto evidence,
        IReadOnlyList<SourceEvidenceItemDto> items)
    {
        foreach (var warning in lifecycle.Warnings)
        {
            yield return warning;
        }
        if (items.Count == 0 && evidence.WikiBlocks.Count == 0)
        {
            yield return "Konu icin aktif kaynak veya Wiki evidence bulunamadi.";
        }
        if (items.Any(i => i.Status == "degraded"))
        {
            yield return "Bazi kaynak parcalarinin eslesme guveni dusuk.";
        }
        if (evidence.UnsupportedCitationCount > 0)
        {
            yield return "Desteklenmeyen citation kayitlari var; kaynak iddiasi temkinli kullanilmali.";
        }
    }

    private static IReadOnlyList<string> BuildLifecycleWarnings(int ready, int stale, int deleted, int failed, int activeChunks)
    {
        var warnings = new List<string>();
        if (ready == 0 || activeChunks == 0) warnings.Add("Aktif kaynak kaniti yetersiz.");
        if (stale > 0) warnings.Add("Stale kaynaklar var; kaynak zemini yenilenmeli.");
        if (deleted > 0) warnings.Add("Silinmis kaynaklar evidence olarak kullanilmaz.");
        if (failed > 0) warnings.Add("Islenemeyen kaynaklar source-grounded evidence sayilmaz.");
        return warnings;
    }

    private static string HashBundle(Guid topicId, Guid? sessionId, string status, IReadOnlyList<SourceEvidenceItemDto> items, SourceLifecycleSummaryDto lifecycle)
    {
        var sessionKey = sessionId?.ToString("N") ?? "global";
        var itemKeys = items.Select(i =>
        {
            var sourceKey = i.SourceId?.ToString("N") ?? "source";
            var chunkKey = i.ChunkId?.ToString("N") ?? "chunk";
            return $"{sourceKey}:{chunkKey}:{i.Confidence:0.0000}";
        });
        var raw = $"{topicId:N}:{sessionKey}:{status}:{lifecycle.ReadySourceCount}:{lifecycle.ActiveChunkCount}:{string.Join("|", itemKeys)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static string AppendWarning(string? json, string warning)
    {
        var payload = Parse(json, new SourceEvidenceBundlePayload(Array.Empty<SourceEvidenceItemDto>(), Array.Empty<string>()));
        var warnings = payload.Warnings.Concat(new[] { warning }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return JsonSerializer.Serialize(new SourceEvidenceBundlePayload(payload.Items, warnings), JsonOptions);
    }

    private static SourceCitationValidationResultDto UnsupportedCitation(ValidateSourceCitationDto citation, string status) => new()
    {
        CitationId = Clean(citation.CitationId, 220) ?? "unknown",
        Supported = false,
        Status = status,
        UserSafeWarning = "Citation aktif kaynak parcasi ile eslesmedi.",
        SourceId = citation.SourceId,
        ChunkId = citation.ChunkId,
        PageNumber = citation.PageNumber
    };

    private static string ResolveInactiveCitationStatus(string? sourceStatus)
    {
        if (string.Equals(sourceStatus, "stale", StringComparison.OrdinalIgnoreCase)) return "stale";
        if (string.Equals(sourceStatus, "deleted", StringComparison.OrdinalIgnoreCase)) return "deleted";
        if (IsFailed(sourceStatus, false)) return "degraded";
        return "inactive";
    }

    private static string BuildCitationId(SourceChunk chunk) => $"[doc:{chunk.LearningSourceId}:p{chunk.PageNumber}]";

    private static bool IsReady(string? status, bool isDeleted) =>
        !isDeleted && string.Equals(status, "ready", StringComparison.OrdinalIgnoreCase);

    private static bool IsStaleOrDegraded(string? status, bool isDeleted) =>
        !isDeleted && (string.Equals(status, "stale", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(status, "degraded", StringComparison.OrdinalIgnoreCase));

    private static bool IsFailed(string? status, bool isDeleted) =>
        !isDeleted && (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(status, "error", StringComparison.OrdinalIgnoreCase));

    private static bool IsLowConfidence(string? status) =>
        string.Equals(status, "low_confidence", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "degraded", StringComparison.OrdinalIgnoreCase);

    private static string Summarize(string? value, int maxLength)
    {
        var normalized = Whitespace.Replace(value ?? string.Empty, " ").Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength].Trim() + "...";
    }

    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = Whitespace.Replace(value.Trim(), " ");
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength].Trim();
    }

    private static string Slug(string value)
    {
        var clean = Clean(value, 80) ?? "concept";
        return string.Concat(clean.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')).Trim('-');
    }

    private static bool ContainsNormalized(string? haystack, string needle) =>
        !string.IsNullOrWhiteSpace(haystack) &&
        !string.IsNullOrWhiteSpace(needle) &&
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string BuildCoverageLabel(int readySourceCount, int chunkCount) =>
        readySourceCount == 0 || chunkCount == 0 ? "no_sources" :
        chunkCount < 3 ? "low_source_coverage" :
        "source_seeded";

    private static T Parse<T>(string? json, T fallback)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return fallback;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private sealed record SourceEvidenceBundlePayload(
        IReadOnlyList<SourceEvidenceItemDto> Items,
        IReadOnlyList<string> Warnings);
}
