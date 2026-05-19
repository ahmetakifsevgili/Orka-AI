using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class WikiCopilotService : IWikiCopilotService
{
    private readonly OrkaDbContext _db;

    public WikiCopilotService(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<WikiCopilotContextDto?> BuildPageContextAsync(
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

        var curation = WikiAutoCurationService.BuildSummary(page, blocks);
        var latestPackStatus = await _db.LearningNotebookPacks.AsNoTracking()
            .Where(p => p.UserId == userId && p.WikiPageId == pageId && !p.IsDeleted)
            .OrderByDescending(p => p.UpdatedAt)
            .Select(p => p.PackStatus)
            .FirstOrDefaultAsync(ct);
        var packCount = await _db.LearningNotebookPacks.AsNoTracking()
            .CountAsync(p => p.UserId == userId && p.WikiPageId == pageId && !p.IsDeleted, ct);
        var blockArtifactIds = blocks
            .Where(b => b.LearningArtifactId.HasValue)
            .Select(b => b.LearningArtifactId!.Value)
            .Distinct()
            .Count();
        var relatedArtifactCount = await _db.LearningArtifacts.AsNoTracking()
            .CountAsync(a =>
                a.UserId == userId &&
                !a.IsDeleted &&
                (a.TopicId == page.TopicId || (page.ConceptKey != null && a.ConceptKey == page.ConceptKey)), ct);

        var signals = AnalyzeSignals(page, blocks, curation);
        var suggestions = BuildSuggestions(page, curation, signals, packCount).ToArray();
        var primary = suggestions.FirstOrDefault(a => a.Availability == "available") ?? suggestions.FirstOrDefault();
        var warnings = BuildWarnings(curation, signals).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();

        return new WikiCopilotContextDto
        {
            PageId = page.Id,
            PageKey = string.IsNullOrWhiteSpace(page.PageKey) ? page.Id.ToString("N") : page.PageKey,
            ConceptKey = page.ConceptKey,
            PageTitle = page.Title,
            PageType = string.IsNullOrWhiteSpace(page.PageType) ? "concept" : page.PageType,
            CurationStatus = curation.CurationStatus,
            SourceReadiness = string.IsNullOrWhiteSpace(page.SourceReadiness) ? "evidence_insufficient" : page.SourceReadiness,
            EvidenceStatus = string.IsNullOrWhiteSpace(page.EvidenceStatus) ? "evidence_insufficient" : page.EvidenceStatus,
            MasteryStatus = signals.MasteryStatus,
            WeakConcepts = signals.WeakConcepts,
            RepairState = signals.RepairState,
            ArtifactCount = blockArtifactIds + relatedArtifactCount,
            NotebookPackStatus = packCount > 0 ? (latestPackStatus ?? "ready") : "not_requested",
            PrimaryAction = primary,
            SuggestedActions = suggestions,
            Warnings = warnings,
            StudentVisibleSummary = BuildStudentSummary(page, curation, signals, primary),
            NextAction = primary?.ActionType ?? curation.NextAction,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static CopilotSignals AnalyzeSignals(WikiPage page, IReadOnlyCollection<WikiBlock> blocks, WikiCurationSummaryDto curation)
    {
        var visibleBlocks = blocks
            .Where(b => !string.Equals(b.Visibility, "hidden_system", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var repairBlocks = visibleBlocks
            .Where(b => b.BlockType is WikiBlockType.RepairNote or WikiBlockType.MisconceptionNote)
            .ToArray();
        var weakConcepts = repairBlocks
            .Select(b => b.ConceptKey)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(v => v!)
            .ToArray();
        var sourceReady = IsReady(page.SourceReadiness) || IsReady(page.EvidenceStatus) ||
                          visibleBlocks.Any(b => IsReady(b.SourceBasis) && b.SourceEvidenceBundleId.HasValue);
        var sourceLimited = IsLimited(page.SourceReadiness) || IsLimited(page.EvidenceStatus) ||
                            curation.CurationStatus is "source_limited" or "degraded";
        var hasSourceBlocks = visibleBlocks.Any(b => b.BlockType is WikiBlockType.SourceNote or WikiBlockType.SourceExcerptSummary);
        var hasMeaningfulBlocks = visibleBlocks.Length > 0;
        var hasNoisyTrace = curation.CurationStatus is "duplicate_trace" or "stale_trace";
        var repairState = repairBlocks.Length > 0 || curation.CurationStatus == "repair_pending"
            ? "repair_pending"
            : "none";
        var masteryStatus = repairState == "repair_pending"
            ? "needs_review"
            : sourceLimited
                ? "source_limited"
                : hasMeaningfulBlocks
                    ? "developing"
                    : "unknown";

        return new CopilotSignals(
            hasMeaningfulBlocks,
            repairState,
            weakConcepts,
            sourceReady,
            sourceLimited,
            hasSourceBlocks,
            hasNoisyTrace,
            masteryStatus);
    }

    private static IEnumerable<WikiCopilotActionDto> BuildSuggestions(
        WikiPage page,
        WikiCurationSummaryDto curation,
        CopilotSignals signals,
        int packCount)
    {
        if (signals.RepairState == "repair_pending")
        {
            yield return Action("start_repair", "Telafiyi surdur", "Bu sayfada bekleyen repair izi var; once kisa checkpoint ile toparla.", "tutor", "available", "repair_pending");
            yield return Action("generate_checkpoint", "Kisa checkpoint al", "Yanilgiyi buyutmeden bir mini kontrol sorusu ile durumu yokla.", "quiz", "available", "repair_pending");
        }

        if (signals.WeakConcepts.Count > 0)
        {
            yield return Action("review_weak_concept", "Zayif kavrami tekrar et", "Bu sayfadaki zayif kavram sinyalini Tutor ile hedefli tekrar et.", "tutor", "available", "weak_concept");
        }

        if (signals.SourceReady)
        {
            yield return Action("ask_source", "Kaynaklara sor", "Hazir kaynak kaniti varsa bu sayfa icin kaynak destekli soru sor.", "source", "available", "source_ready");
            if (signals.HasSourceBlocks)
            {
                yield return Action("inspect_citations", "Citationlari incele", "Bu sayfadaki kaynak notlarinin kanit durumunu kontrol et.", "source", "available", "source_ready");
            }
        }
        else if (signals.SourceLimited)
        {
            yield return Action("ask_source", "Kaynaklara sor", "Kaynak zemini sinirli oldugu icin kaynakli yanit guvenle acilmaz.", "source", "blocked", "source_limited", "source_grounded_action_blocked");
            yield return Action("inspect_citations", "Citationlari incele", "Once kaynak veya citation durumunu kontrol et.", "source", "blocked", "source_limited", "source_grounded_action_blocked");
        }

        if (signals.HasMeaningfulBlocks)
        {
            yield return Action("summarize_page", "Sayfayi ozetle", "Bu sayfadaki temiz notlardan kisa calisma ozeti cikar.", "wiki", "available", "has_curated_blocks");
            yield return Action("create_study_pack", "Study pack hazirla", packCount > 0
                ? "Bu sayfaya bagli pack var; Notebook Studio'da gozden gecir veya yenile."
                : "Bu sayfadan Notebook Studio calisma paketi olustur.",
                "notebook_studio", "available", "has_curated_blocks");
            yield return Action("create_flashcards", "Kart uret", "Kavram ve repair izlerinden guvenli tekrar kartlari hazirla.", "notebook_studio", "available", "has_curated_blocks");
        }
        else
        {
            yield return Action("ask_tutor_about_page", "Tutor'a bu sayfayi actir", "Sayfada henuz yeterli iz yok; Tutor'dan kisa baslangic anlatimi iste.", "tutor", "available", "thin_page");
            yield return Action("explain_page", "Sayfayi aciklat", "Once temel kavrami model-assisted olarak anlattir; kaynak iddiasi kurma.", "tutor", "available", "thin_page");
        }

        if (signals.HasNoisyTrace)
        {
            yield return Action("clean_page", "Sayfayi temiz tut", "Tekrarlari bastir ve manuel notlara dokunmadan curation durumunu izle.", "wiki", "available", curation.CurationStatus);
        }

        yield return Action("ask_tutor_about_page", "Tutor'a sor", "Bu sayfayi aktif baglam olarak Tutor'a tasiyarak devam et.", "tutor", "available", "page_context");
    }

    private static IEnumerable<string> BuildWarnings(WikiCurationSummaryDto curation, CopilotSignals signals)
    {
        foreach (var warning in curation.Warnings)
        {
            yield return warning;
        }

        if (signals.SourceLimited) yield return "source_grounded_actions_degraded";
        if (!signals.HasMeaningfulBlocks) yield return "wiki_page_thin";
        if (signals.RepairState == "repair_pending") yield return "repair_pending";
    }

    private static string BuildStudentSummary(
        WikiPage page,
        WikiCurationSummaryDto curation,
        CopilotSignals signals,
        WikiCopilotActionDto? primary)
    {
        if (!signals.HasMeaningfulBlocks)
        {
            return "Bu sayfa henuz ince; Copilot once Tutor aciklamasi veya kisa baslangic oneriyor.";
        }

        if (signals.RepairState == "repair_pending")
        {
            return "Bu sayfada telafi izi var; Copilot once repair/checkpoint ile ilerlemeyi oneriyor.";
        }

        if (signals.SourceLimited)
        {
            return "Bu sayfada kaynak zemini sinirli; kaynakli iddia yerine Tutor/Wiki calismasi oneriliyor.";
        }

        var title = string.IsNullOrWhiteSpace(page.Title) ? "bu sayfa" : page.Title;
        return primary == null
            ? curation.StudentVisibleSummary
            : $"{title} icin siradaki guvenli adim: {primary.UserSafeLabel}.";
    }

    private static WikiCopilotActionDto Action(
        string actionType,
        string label,
        string description,
        string targetSurface,
        string availability,
        params string[] reasonCodes) =>
        new()
        {
            ActionType = actionType,
            UserSafeLabel = label,
            UserSafeDescription = description,
            TargetSurface = targetSurface,
            Availability = availability,
            ReasonCodes = reasonCodes,
            SafetyWarnings = availability == "blocked"
                ? new[] { "action_degraded_for_safety" }
                : Array.Empty<string>()
        };

    private static bool IsReady(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;
        return status.Contains("source_grounded", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("source_ready", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("ready", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("wiki_backed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLimited(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;
        return status.Contains("insufficient", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("stale", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("degraded", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("deleted", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("limited", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CopilotSignals(
        bool HasMeaningfulBlocks,
        string RepairState,
        IReadOnlyList<string> WeakConcepts,
        bool SourceReady,
        bool SourceLimited,
        bool HasSourceBlocks,
        bool HasNoisyTrace,
        string MasteryStatus);
}
