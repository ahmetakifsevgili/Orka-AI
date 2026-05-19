using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class WikiAutoCurationService : IWikiAutoCurationService
{
    private static readonly TimeSpan StaleWindow = TimeSpan.FromDays(45);
    private readonly OrkaDbContext _db;

    public WikiAutoCurationService(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<WikiCurationSummaryDto?> BuildPageSummaryAsync(
        Guid userId,
        Guid pageId,
        CancellationToken ct = default)
    {
        var page = await _db.WikiPages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pageId && p.UserId == userId && !p.IsDeleted, ct);
        if (page == null) return null;

        var blocks = await _db.WikiBlocks.AsNoTracking()
            .Where(b => b.WikiPageId == pageId && !b.IsDeleted)
            .OrderBy(b => b.OrderIndex)
            .ThenBy(b => b.CreatedAt)
            .Take(120)
            .ToListAsync(ct);

        return BuildSummary(page, blocks);
    }

    public static WikiCurationSummaryDto BuildSummary(WikiPage page, IReadOnlyCollection<WikiBlock>? blocks)
    {
        var visibleBlocks = (blocks ?? Array.Empty<WikiBlock>())
            .Where(b => !b.IsDeleted && !string.Equals(b.Visibility, "hidden_system", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var duplicateGroups = visibleBlocks
            .Where(b => !string.IsNullOrWhiteSpace(b.Content))
            .GroupBy(b => $"{b.BlockType}|{b.ConceptKey}|{NormalizeForDedupe(b.Title)}|{NormalizeForDedupe(b.Content)}", StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToArray();

        var duplicateCount = duplicateGroups.Sum(g => g.Count() - 1);
        var repairCount = visibleBlocks.Count(b => b.BlockType is WikiBlockType.RepairNote or WikiBlockType.MisconceptionNote);
        var questionCount = visibleBlocks.Count(b => b.BlockType == WikiBlockType.StudentQuestion);
        var sourceLimitedCount = visibleBlocks.Count(IsSourceLimited) + (IsStatusLimited(page.SourceReadiness) || IsStatusLimited(page.EvidenceStatus) ? 1 : 0);
        var staleCount = visibleBlocks.Count(b => b.CreatedAt <= DateTime.UtcNow.Subtract(StaleWindow)) +
                         (page.UpdatedAt <= DateTime.UtcNow.Subtract(StaleWindow) ? 1 : 0);

        var retainedSignals = BuildRetainedSignals(visibleBlocks, repairCount, questionCount).ToArray();
        var mergedSignals = duplicateGroups
            .Select(g => $"merged_{ToSafeKey(g.First().BlockType)}_{g.Count()}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
        var suppressedSignals = duplicateCount > 0
            ? new[] { $"duplicate_trace:{duplicateCount}" }
            : Array.Empty<string>();
        var staleSignals = staleCount > 0
            ? new[] { $"stale_trace:{Math.Min(staleCount, 99)}" }
            : Array.Empty<string>();
        var warnings = BuildWarnings(visibleBlocks.Length, duplicateCount, repairCount, sourceLimitedCount, staleCount).ToArray();
        var status = DetermineStatus(visibleBlocks.Length, duplicateCount, repairCount, sourceLimitedCount, staleCount);

        return new WikiCurationSummaryDto
        {
            PageId = page.Id,
            PageKey = string.IsNullOrWhiteSpace(page.PageKey) ? page.Id.ToString("N") : page.PageKey,
            ConceptKey = page.ConceptKey,
            CurationStatus = status,
            RetainedSignalCount = visibleBlocks.Length,
            MergedSignalCount = duplicateCount,
            SuppressedSignalCount = duplicateCount,
            StaleSignalCount = staleCount,
            RetainedSignals = retainedSignals,
            MergedSignals = mergedSignals,
            SuppressedSignals = suppressedSignals,
            StaleSignals = staleSignals,
            Warnings = warnings,
            StudentVisibleSummary = BuildStudentSummary(status, visibleBlocks.Length, repairCount, duplicateCount, sourceLimitedCount),
            NextAction = NextActionFor(status)
        };
    }

    private static IEnumerable<string> BuildRetainedSignals(IReadOnlyCollection<WikiBlock> blocks, int repairCount, int questionCount)
    {
        if (blocks.Count > 0) yield return $"notebook_blocks:{blocks.Count}";
        if (questionCount > 0) yield return $"student_questions:{questionCount}";
        if (repairCount > 0) yield return $"repair_signals:{repairCount}";
        foreach (var type in blocks.Select(b => ToSafeKey(b.BlockType)).Distinct(StringComparer.OrdinalIgnoreCase).Take(6))
        {
            yield return $"block_type:{type}";
        }
    }

    private static IEnumerable<string> BuildWarnings(int blockCount, int duplicateCount, int repairCount, int sourceLimitedCount, int staleCount)
    {
        if (blockCount == 0) yield return "wiki_page_empty";
        if (duplicateCount > 0) yield return "duplicate_trace_suppressed";
        if (repairCount > 0) yield return "repair_memory_pending";
        if (sourceLimitedCount > 0) yield return "source_limited_or_degraded";
        if (staleCount > 0) yield return "stale_trace_review_needed";
    }

    private static string DetermineStatus(int blockCount, int duplicateCount, int repairCount, int sourceLimitedCount, int staleCount)
    {
        if (blockCount == 0) return "degraded";
        if (sourceLimitedCount > 0) return "source_limited";
        if (duplicateCount > 0) return "duplicate_trace";
        if (staleCount > 0) return "stale_trace";
        if (repairCount > 0) return "repair_pending";
        return "clean";
    }

    private static string BuildStudentSummary(string status, int blockCount, int repairCount, int duplicateCount, int sourceLimitedCount) =>
        status switch
        {
            "degraded" => "Bu Wiki sayfasinda henuz anlamli ogrenme izi yok.",
            "source_limited" => "Bu sayfada kaynak zemini sinirli veya eski olan notlar var; kaynak iddiasi abartilmiyor.",
            "duplicate_trace" => $"Bu sayfada {duplicateCount} tekrar eden iz bastirildi; defter daha okunabilir tutuluyor.",
            "stale_trace" => "Bu sayfada eski ogrenme izleri var; sonraki calisma turunda guncelleme onerilir.",
            "repair_pending" => $"Bu sayfada {repairCount} telafi/onarim izi var; once kisa repair kontrolu onerilir.",
            _ => $"Bu Wiki sayfasi {blockCount} guvenli ogrenme iziyle temiz gorunuyor."
        };

    private static string NextActionFor(string status) => status switch
    {
        "degraded" => "add_learning_trace",
        "source_limited" => "review_source_evidence",
        "duplicate_trace" => "continue_with_curated_notes",
        "stale_trace" => "refresh_page_context",
        "repair_pending" => "run_repair_checkpoint",
        _ => "continue_learning"
    };

    private static bool IsSourceLimited(WikiBlock block) =>
        IsStatusLimited(block.SourceBasis) ||
        ParseWarnings(block.SafetyWarningsJson).Any(w => w.Contains("source", StringComparison.OrdinalIgnoreCase) ||
                                                        w.Contains("stale", StringComparison.OrdinalIgnoreCase) ||
                                                        w.Contains("degraded", StringComparison.OrdinalIgnoreCase));

    private static bool IsStatusLimited(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;
        return status.Contains("insufficient", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("stale", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("degraded", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("deleted", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("limited", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ParseWarnings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<IReadOnlyList<string>>(json) ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string ToSafeKey(WikiBlockType type) =>
        Regex.Replace(type.ToString(), "([a-z])([A-Z])", "$1_$2").ToLowerInvariant();

    private static string NormalizeForDedupe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = Regex.Replace(value.Trim(), @"\s+", " ");
        return normalized.Length <= 600 ? normalized : normalized[..600];
    }
}
