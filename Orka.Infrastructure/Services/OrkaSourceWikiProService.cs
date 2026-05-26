using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class OrkaSourceWikiProService : IOrkaSourceWikiProService
{
    private static readonly string[] BlockedMarkers =
    [
        "rawPrompt", "hiddenPrompt", "systemPrompt", "developerPrompt", "rawProviderPayload",
        "rawSourceChunk", "rawToolPayload", "debugTrace", "localPath", "apiKey", "secret",
        "token", "answerKey", "correctAnswer", "stackTrace", "ownerId", "userId"
    ];

    private readonly OrkaDbContext _db;
    private readonly ISourceWikiIntelligenceService _sourceWikiIntelligence;
    private readonly ISourceCompareService _sourceCompare;
    private readonly IOrkaLearningStateService _orkaState;
    private readonly IOrkaMissionControlService _missionControl;
    private readonly IOrkaStudyCoachService _studyCoach;
    private readonly IOrkaExamWarRoomService _examWarRoom;

    public OrkaSourceWikiProService(
        OrkaDbContext db,
        ISourceWikiIntelligenceService sourceWikiIntelligence,
        ISourceCompareService sourceCompare,
        IOrkaLearningStateService orkaState,
        IOrkaMissionControlService missionControl,
        IOrkaStudyCoachService studyCoach,
        IOrkaExamWarRoomService examWarRoom)
    {
        _db = db;
        _sourceWikiIntelligence = sourceWikiIntelligence;
        _sourceCompare = sourceCompare;
        _orkaState = orkaState;
        _missionControl = missionControl;
        _studyCoach = studyCoach;
        _examWarRoom = examWarRoom;
    }

    public async Task<OrkaSourceWikiProDto?> BuildProAsync(
        Guid userId,
        Guid? topicId = null,
        Guid? sourceId = null,
        Guid? wikiPageId = null,
        string? examCode = "KPSS",
        string? variantCode = null,
        CancellationToken ct = default)
    {
        var profile = await _sourceWikiIntelligence.BuildProfileAsync(userId, topicId, sourceId, wikiPageId, ct);
        if (profile is null) return null;

        var resolvedTopicId = profile.TopicId ?? topicId;
        var citationReview = await LoadCitationReviewAsync(userId, resolvedTopicId, sourceId, ct);
        var state = await _orkaState.BuildStateAsync(userId, resolvedTopicId, null, examCode, variantCode, ct);
        OrkaMissionControlDto? mission = null;
        OrkaStudyCoachDto? coach = null;
        if (state is not null)
        {
            mission = await _missionControl.BuildFromStateAsync(userId, state, ct);
            coach = await _studyCoach.BuildFromMissionControlAsync(userId, state, mission, ct);
        }

        var warRoom = string.IsNullOrWhiteSpace(examCode)
            ? null
            : await _examWarRoom.BuildWarRoomAsync(userId, examCode!, variantCode, null, null, ct);
        var blockSignals = await LoadWikiBlockSignalsAsync(userId, profile.WikiPages.Select(p => p.WikiPageId).ToArray(), ct);
        var notebook = await LoadNotebookReadinessAsync(userId, resolvedTopicId, sourceId, wikiPageId, ct);

        var sources = profile.EvidenceReadiness.Select(ToSource).ToArray();
        var wikiPages = profile.WikiPages.Select(p => ToWikiPage(p, blockSignals.TryGetValue(p.WikiPageId, out var s) ? s : WikiBlockSignals.Empty)).ToArray();
        var citations = citationReview?.Items.Select(ToCitation).ToArray() ?? Array.Empty<SourceWikiProCitationDto>();
        var linkedConcepts = profile.LinkedConcepts.Select(ToConceptLink).ToArray();
        var sourceBackedConcepts = linkedConcepts.Where(c => c.IsSourceBacked).Take(12).ToArray();
        var sourceLimitedConcepts = linkedConcepts.Where(c => c.IsLimited).Take(12).ToArray();
        var linkedExamOutcomes = BuildLinkedExamOutcomes(warRoom, linkedConcepts).ToArray();
        var citationWarnings = BuildCitationWarnings(profile, citationReview).ToArray();
        var examWarnings = BuildExamWarnings(warRoom).ToArray();
        var missionWarnings = BuildMissionWarnings(mission).ToArray();
        var warnings = BuildConflictWarnings(profile, mission, coach, warRoom, linkedConcepts).ToArray();
        var actions = BuildActions(profile, sources, wikiPages, citations, linkedConcepts, notebook, resolvedTopicId)
            .GroupBy(a => $"{a.ActionType}:{a.SourceId}:{a.WikiPageId}:{a.ConceptKey}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(a => PriorityScore(a.Priority))
            .ThenBy(a => ActionRank(a.ActionType))
            .Take(10)
            .ToArray();
        var primary = actions.FirstOrDefault() ?? Action(
            "continue_learning",
            "Planli ogrenmeye devam et",
            "Kaynak/Wiki kaniti izleniyor; yeni kaynak iddiasi kurulmadan devam edilebilir.",
            "low",
            "continue_learning",
            "dashboard",
            resolvedTopicId,
            null,
            null,
            null,
            ["clean_evidence"]);

        var reasonCodes = profile.ReasonCodes
            .Concat(primary.ReasonCodes)
            .Concat(actions.SelectMany(a => a.ReasonCodes))
            .Concat(citationWarnings.SelectMany(w => w.ReasonCodes))
            .Concat(examWarnings.SelectMany(w => w.ReasonCodes))
            .Concat(missionWarnings.SelectMany(w => w.ReasonCodes))
            .Concat(warnings.SelectMany(w => w.ReasonCodes))
            .Where(NotBlank)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToArray();

        return new OrkaSourceWikiProDto
        {
            TopicId = resolvedTopicId,
            SourceId = profile.SourceId ?? sourceId,
            WikiPageId = profile.WikiPageId ?? wikiPageId,
            ExamCode = SafeDisplay(examCode, 40),
            VariantCode = SafeDisplay(variantCode, 40),
            ReadinessStatus = ResolveReadinessStatus(profile, primary, warnings),
            SourceReadiness = SafeDisplay(profile.SourceReadiness, 80),
            WikiReadiness = ResolveWikiReadiness(profile, wikiPages),
            CitationReadiness = SafeDisplay(profile.CitationReadiness, 80),
            EvidenceMap = new SourceWikiProEvidenceMapDto
            {
                UploadedSourceCount = profile.SourceCount,
                ReadySourceCount = profile.ReadySourceCount,
                WikiPageCount = profile.WikiPageCount,
                ManualNoteCount = wikiPages.Count(p => p.ManualNotePreserved),
                TutorTraceCount = wikiPages.Count(p => p.HasTutorTrace),
                SourceBackedPageCount = wikiPages.Count(IsSourceBacked),
                LinkedConceptCount = linkedConcepts.Length,
                LinkedExamOutcomeCount = linkedExamOutcomes.Length,
                CitationWarningCount = profile.CitationWarningCount,
                CanClaimSourceGrounded = profile.CanClaimSourceGrounded,
                ProviderOutputCountsAsEvidence = false,
                WikiMemoryCountsAsCitationEvidence = false
            },
            SourceReadinessItems = sources,
            WikiReadinessItems = wikiPages,
            CitationReadinessItems = citations,
            LinkedConcepts = linkedConcepts,
            LinkedExamOutcomes = linkedExamOutcomes,
            SourceBackedConcepts = sourceBackedConcepts,
            SourceLimitedConcepts = sourceLimitedConcepts,
            StaleSources = sources.Where(s => IsStatus(s, "stale") || HasWarning(s.Warnings, "stale")).ToArray(),
            DeletedSources = sources.Where(s => IsStatus(s, "deleted") || HasWarning(s.Warnings, "deleted")).ToArray(),
            InsufficientSources = sources.Where(s => IsLimited(s.SourceReadiness) || IsLimited(s.EvidenceStatus)).ToArray(),
            DegradedSources = sources.Where(s => IsStatus(s, "degraded") || IsStatus(s, "failed") || IsStatus(s, "error")).ToArray(),
            CitationWarnings = citationWarnings,
            WikiRepairPages = wikiPages.Where(p => p.CurationStatus == "repair_pending" || p.RepairSignalCount > 0).ToArray(),
            DuplicateTracePages = wikiPages.Where(p => p.CurationStatus is "duplicate_trace" or "stale_trace").ToArray(),
            ManualNotePages = wikiPages.Where(p => p.ManualNotePreserved).ToArray(),
            TutorTracePages = wikiPages.Where(p => p.HasTutorTrace).ToArray(),
            SourceBackedPages = wikiPages.Where(IsSourceBacked).ToArray(),
            NotebookPackReadiness = notebook.Status,
            TodaySourceWikiMission = primary,
            RecommendedActions = actions,
            TutorHandoffs = BuildTutorHandoffs(actions).ToArray(),
            StudyRoomHandoffs = BuildStudyRoomHandoffs(actions, resolvedTopicId).ToArray(),
            NotebookHandoffs = actions.Where(a => a.ActionType is "open_notebook_pack" or "create_flashcards" or "update_wiki_note" or "repair_wiki_page").Take(4).ToArray(),
            ExamWarRoomWarnings = examWarnings,
            MissionControlWarnings = missionWarnings,
            ConflictWarnings = warnings,
            ReasonCodes = reasonCodes,
            UserSafeSummary = BuildSummary(primary, profile, notebook, warnings),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<CitationReviewResultDto?> LoadCitationReviewAsync(Guid userId, Guid? topicId, Guid? sourceId, CancellationToken ct)
    {
        if (sourceId.HasValue) return await _sourceCompare.GetSourceCitationReviewAsync(userId, sourceId.Value, ct);
        if (topicId.HasValue) return await _sourceCompare.GetTopicCitationReviewAsync(userId, topicId.Value, ct);
        return null;
    }

    private async Task<IReadOnlyDictionary<Guid, WikiBlockSignals>> LoadWikiBlockSignalsAsync(
        Guid userId,
        IReadOnlyList<Guid> pageIds,
        CancellationToken ct)
    {
        if (pageIds.Count == 0) return new Dictionary<Guid, WikiBlockSignals>();

        var blocks = await _db.WikiBlocks.AsNoTracking()
            .Where(b => pageIds.Contains(b.WikiPageId) && !b.IsDeleted)
            .OrderBy(b => b.WikiPageId)
            .ThenBy(b => b.OrderIndex)
            .Take(240)
            .Select(b => new
            {
                b.WikiPageId,
                b.BlockType,
                b.Visibility,
                b.SourceBasis,
                b.SafetyWarningsJson
            })
            .ToListAsync(ct);

        var ownedPageIds = await _db.WikiPages.AsNoTracking()
            .Where(p => pageIds.Contains(p.Id) && p.UserId == userId && !p.IsDeleted)
            .Select(p => p.Id)
            .ToListAsync(ct);
        var owned = ownedPageIds.ToHashSet();

        return blocks
            .Where(b => owned.Contains(b.WikiPageId) && !string.Equals(b.Visibility, "hidden_system", StringComparison.OrdinalIgnoreCase))
            .GroupBy(b => b.WikiPageId)
            .ToDictionary(
                g => g.Key,
                g => new WikiBlockSignals(
                    g.Any(b => b.BlockType.ToString().Contains("Tutor", StringComparison.OrdinalIgnoreCase) ||
                               b.BlockType.ToString().Contains("Trace", StringComparison.OrdinalIgnoreCase)),
                    g.Any(b => b.BlockType.ToString().Contains("Source", StringComparison.OrdinalIgnoreCase) ||
                               IsReady(b.SourceBasis)),
                    g.Any(b => IsLimited(b.SourceBasis) || HasWarning([b.SafetyWarningsJson ?? string.Empty], "source"))));
    }

    private async Task<NotebookReadiness> LoadNotebookReadinessAsync(
        Guid userId,
        Guid? topicId,
        Guid? sourceId,
        Guid? wikiPageId,
        CancellationToken ct)
    {
        var query = _db.LearningNotebookPacks.AsNoTracking()
            .Where(p => p.UserId == userId && !p.IsDeleted);
        if (topicId.HasValue) query = query.Where(p => p.TopicId == topicId.Value);
        if (wikiPageId.HasValue) query = query.Where(p => p.WikiPageId == wikiPageId.Value);
        if (sourceId.HasValue)
        {
            var sourceText = sourceId.Value.ToString();
            query = query.Where(p => p.SafeMetadataJson.Contains(sourceText) ||
                                     p.PackType == "source_digest" ||
                                     p.PackType == "source_notebook" ||
                                     p.PackType == "source_review");
        }

        var latest = await query
            .OrderByDescending(p => p.UpdatedAt)
            .Select(p => new { p.PackStatus, p.PackType })
            .FirstOrDefaultAsync(ct);
        var count = await query.CountAsync(ct);
        if (count == 0) return new NotebookReadiness("not_requested", 0);
        return new NotebookReadiness(string.IsNullOrWhiteSpace(latest?.PackStatus) ? "ready" : latest.PackStatus, count);
    }

    private static SourceWikiProSourceDto ToSource(SourceWikiEvidenceReadinessDto source) => new()
    {
        SourceId = source.SourceId,
        TopicId = source.TopicId,
        Title = SafeDisplay(source.Title, 160),
        Status = SafeDisplay(source.Status, 80),
        SourceReadiness = SafeDisplay(source.SourceReadiness, 80),
        EvidenceStatus = SafeDisplay(source.EvidenceStatus, 80),
        CitationReadiness = SafeDisplay(source.CitationReadiness, 80),
        PageCount = source.PageCount,
        ChunkCount = source.ChunkCount,
        LinkedConceptCount = source.LinkedConceptCount,
        Warnings = source.Warnings.Select(w => SafeDisplay(w, 120)).Where(NotBlank).Take(8).ToArray()
    };

    private static SourceWikiProWikiPageDto ToWikiPage(WikiLearningPageReadinessDto page, WikiBlockSignals signals) => new()
    {
        WikiPageId = page.WikiPageId,
        TopicId = page.TopicId,
        Title = SafeDisplay(page.Title, 160),
        PageType = SafeDisplay(page.PageType, 80),
        ConceptKey = SafeDisplay(page.ConceptKey, 120),
        SourceReadiness = SafeDisplay(page.SourceReadiness, 80),
        EvidenceStatus = SafeDisplay(page.EvidenceStatus, 80),
        CurationStatus = SafeDisplay(page.CurationStatus, 80),
        BlockCount = page.BlockCount,
        RepairSignalCount = page.RepairSignalCount,
        SourceLimitedSignalCount = page.SourceLimitedSignalCount,
        ManualNotePreserved = page.ManualNotePreserved,
        HasTutorTrace = signals.HasTutorTrace,
        NextAction = SafeDisplay(page.NextAction, 120),
        Warnings = page.Warnings
            .Concat(signals.HasSourceLimitedBlock ? ["source_limited"] : Array.Empty<string>())
            .Select(w => SafeDisplay(w, 120))
            .Where(NotBlank)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray()
    };

    private static SourceWikiProCitationDto ToCitation(CitationReviewItemDto item) => new()
    {
        CitationCheckId = item.Id,
        CitationId = SafeDisplay(item.CitationId, 80),
        SourceId = item.SourceId,
        SourceTitle = SafeDisplay(item.SourceTitle, 160),
        SourceReadiness = SafeDisplay(item.SourceReadiness, 80),
        EvidenceStatus = SafeDisplay(item.EvidenceStatus, 80),
        CitationStatus = SafeDisplay(item.CitationStatus, 80),
        Confidence = item.Confidence,
        UserSafeWarning = SafeDisplay(item.UserSafeWarning, 220)
    };

    private static SourceWikiProConceptLinkDto ToConceptLink(SourceConceptLinkDto link)
    {
        var isLimited = IsLimited(link.SourceReadiness) || IsLimited(link.EvidenceStatus) || HasWarning(link.Warnings, "limited");
        var isSourceBacked = !link.IsSuggestion && !isLimited && IsReady(link.SourceReadiness, link.EvidenceStatus);
        return new SourceWikiProConceptLinkDto
        {
            SourceId = link.SourceId,
            WikiPageId = link.WikiPageId,
            ConceptKey = SafeDisplay(link.ConceptKey, 120),
            ConceptTitle = SafeDisplay(link.ConceptTitle, 160),
            SourceTitle = SafeDisplay(link.SourceTitle, 160),
            LinkType = SafeDisplay(link.LinkType, 80),
            Confidence = SafeDisplay(link.Confidence, 40),
            ConfidenceScore = link.ConfidenceScore,
            Basis = SafeDisplay(link.Basis, 120),
            SourceReadiness = SafeDisplay(link.SourceReadiness, 80),
            EvidenceStatus = SafeDisplay(link.EvidenceStatus, 80),
            IsSuggestion = link.IsSuggestion,
            IsSourceBacked = isSourceBacked,
            IsLimited = isLimited,
            Warnings = link.Warnings.Select(w => SafeDisplay(w, 120)).Where(NotBlank).Take(8).ToArray()
        };
    }

    private static IEnumerable<string> BuildLinkedExamOutcomes(OrkaExamWarRoomDto? warRoom, IReadOnlyList<SourceWikiProConceptLinkDto> links)
    {
        if (warRoom is null) yield break;
        var conceptKeys = links.Select(l => l.ConceptKey).Where(NotBlank).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var outcome in warRoom.WeakOutcomes.Concat(warRoom.DueOutcomes).Concat(warRoom.StableOutcomes))
        {
            if (conceptKeys.Count == 0 || conceptKeys.Any(k =>
                    outcome.OutcomeCode.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                    outcome.Label.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                yield return SafeDisplay(outcome.OutcomeCode, 120);
            }
        }
    }

    private static IEnumerable<SourceWikiProWarningDto> BuildCitationWarnings(
        SourceWikiIntelligenceProfileDto profile,
        CitationReviewResultDto? review)
    {
        foreach (var item in review?.Items ?? Array.Empty<CitationReviewItemDto>())
        {
            if (IsCitationWarning(item.CitationStatus))
            {
                yield return Warning(
                    WarningCodeForCitation(item.CitationStatus),
                    "warning",
                    string.IsNullOrWhiteSpace(item.UserSafeWarning) ? "Citation durumu gozden gecmeli." : item.UserSafeWarning,
                    "sources",
                    [WarningCodeForCitation(item.CitationStatus), "citation_review"]);
            }
        }

        if (profile.CitationWarningCount > 0 && review?.Items.Count is null or 0)
        {
            yield return Warning("citation_review_needed", "warning", "Citation uyarilari var; kaynak iddiasi sinirlanir.", "sources", ["citation_review_needed"]);
        }
    }

    private static IEnumerable<SourceWikiProWarningDto> BuildExamWarnings(OrkaExamWarRoomDto? warRoom)
    {
        if (warRoom is null) yield break;

        foreach (var warning in warRoom.SourceWikiWarnings.Concat(warRoom.CurriculumCoverageWarnings)
                     .Where(w => w.WarningCode.Contains("source", StringComparison.OrdinalIgnoreCase) ||
                                 w.WarningCode.Contains("curriculum", StringComparison.OrdinalIgnoreCase) ||
                                 w.WarningCode.Contains("official", StringComparison.OrdinalIgnoreCase)))
        {
            yield return Warning(
                warning.WarningCode,
                warning.Severity,
                warning.Label,
                warning.TargetRoute,
                warning.ReasonCodes);
        }
    }

    private static IEnumerable<SourceWikiProWarningDto> BuildMissionWarnings(OrkaMissionControlDto? mission)
    {
        if (mission is null) yield break;

        foreach (var warning in mission.UrgentWarnings
                     .Where(w => w.TargetRoute == "sources" ||
                                 w.WarningCode.Contains("source", StringComparison.OrdinalIgnoreCase) ||
                                 w.WarningCode.Contains("citation", StringComparison.OrdinalIgnoreCase) ||
                                 w.WarningCode.Contains("wiki", StringComparison.OrdinalIgnoreCase)))
        {
            yield return Warning(
                warning.WarningCode,
                warning.Severity,
                warning.Label,
                warning.TargetRoute,
                warning.ReasonCodes);
        }
    }

    private static IEnumerable<SourceWikiProWarningDto> BuildConflictWarnings(
        SourceWikiIntelligenceProfileDto profile,
        OrkaMissionControlDto? mission,
        OrkaStudyCoachDto? coach,
        OrkaExamWarRoomDto? warRoom,
        IReadOnlyList<SourceWikiProConceptLinkDto> links)
    {
        var sourceBlocked = !profile.CanClaimSourceGrounded &&
                            (profile.SourceCount > 0 || profile.Warnings.Any(w => w.Contains("source", StringComparison.OrdinalIgnoreCase)));
        if (sourceBlocked)
        {
            yield return Warning(
                "source_grounding_blocked",
                "warning",
                "Kaynak zemini sinirli; source-grounded iddia kurulmaz.",
                "sources",
                ["source_grounding_blocked"]);
        }

        if (mission?.PrimaryMission.ActionType == "continue_plan" && sourceBlocked)
        {
            yield return Warning(
                "source_wiki_priority_conflict",
                "warning",
                "Mission Control plana devam derken kaynak kaniti dikkat istiyor.",
                "sources",
                ["source_wiki_priority_conflict", "source_grounding_blocked"]);
        }

        if (coach?.RhythmStatus == "source_cleanup" && !sourceBlocked && profile.CitationWarningCount == 0)
        {
            yield return Warning(
                "source_cleanup_unneeded",
                "info",
                "Study Coach kaynak temizligi oneriyor ama Source/Wiki Pro kritik uyari gormuyor.",
                "dashboard",
                ["source_wiki_priority_conflict"]);
        }

        if (warRoom?.SourceWikiWarnings.Any() == true && profile.SourceCount == 0)
        {
            yield return Warning(
                "stale_source_affects_exam",
                "warning",
                "Exam War Room kaynak/kapsam uyarisini Source/Wiki Pro'ya tasidi.",
                "central-exams",
                ["stale_source_affects_exam", "source_unverified"]);
        }

        if (links.Any(l => l.IsSourceBacked && IsLimited(l.EvidenceStatus)))
        {
            yield return Warning(
                "wiki_source_backing_conflict",
                "warning",
                "Bazi Wiki/source linkleri kaynakli gibi gorunse de evidence sinirli.",
                "wiki",
                ["wiki_source_backing_conflict"]);
        }
    }

    private static IEnumerable<SourceWikiProActionDto> BuildActions(
        SourceWikiIntelligenceProfileDto profile,
        IReadOnlyList<SourceWikiProSourceDto> sources,
        IReadOnlyList<SourceWikiProWikiPageDto> wikiPages,
        IReadOnlyList<SourceWikiProCitationDto> citations,
        IReadOnlyList<SourceWikiProConceptLinkDto> links,
        NotebookReadiness notebook,
        Guid? topicId)
    {
        if (profile.SourceCount == 0)
        {
            yield return Action("source_review", "Kaynak ekle veya sec", "Bu calisma alaninda kaynak kaniti yok; kaynakli iddia kurulmaz.", "high", "source_review", "sources", topicId, null, null, null, ["source_missing", "thin_evidence"]);
        }

        if (!profile.CanClaimSourceGrounded && profile.SourceCount > 0)
        {
            yield return Action("source_review", "Kaynak kanitini toparla", "Kaynak zemini sinirli; once kaynak durumunu kontrol et.", "high", "source_review", "sources", topicId, sources.FirstOrDefault()?.SourceId, null, null, ["source_grounding_blocked", "source_evidence_limited"]);
        }

        var stale = sources.FirstOrDefault(s => IsStatus(s, "stale") || HasWarning(s.Warnings, "stale"));
        if (stale != null)
        {
            yield return Action("source_review", "Stale kaynagi yenile", "Stale kaynak guclu kanit sayilmaz.", "urgent", "source_review", "sources", topicId, stale.SourceId, null, null, ["source_stale", "source_grounding_blocked"]);
        }

        var deleted = sources.FirstOrDefault(s => IsStatus(s, "deleted") || HasWarning(s.Warnings, "deleted"));
        if (deleted != null)
        {
            yield return Action("source_review", "Silinmis kaynak etkisini temizle", "Silinmis kaynak downstream source-backed iddia tasiyamaz.", "urgent", "source_review", "sources", topicId, deleted.SourceId, null, null, ["source_deleted", "source_grounding_blocked"]);
        }

        if (citations.Any(c => IsCitationWarning(c.CitationStatus)) || profile.CitationWarningCount > 0)
        {
            yield return Action("citation_review", "Citation review yap", "Eksik/desteklenmeyen citation kaynakli iddiayi sinirlar.", "high", "citation_review", "sources", topicId, sources.FirstOrDefault()?.SourceId, null, null, ["citation_review", "citation_missing"]);
        }

        var insufficient = sources.FirstOrDefault(s => IsLimited(s.SourceReadiness) || IsLimited(s.EvidenceStatus));
        if (insufficient != null)
        {
            yield return Action("repair_source_limited_concept", "Kaynak sinirli kavrami onar", "Kavram calismasi icin kaynak kaniti sinirli; once evidence kontrolu gerekir.", "high", "source_review", "sources", topicId, insufficient.SourceId, null, null, ["source_insufficient", "source_limited"]);
        }

        var repairPage = wikiPages.FirstOrDefault(p => p.CurationStatus == "repair_pending" || p.RepairSignalCount > 0);
        if (repairPage != null)
        {
            yield return Action("repair_wiki_page", "Wiki repair sayfasini toparla", "Wiki sayfasinda repair/telafi izi var; manuel notlar korunarak hedefli onarim onerilir.", "high", "ask_tutor", "wiki", topicId, null, repairPage.WikiPageId, repairPage.ConceptKey, ["wiki_repair_pending"]);
        }

        var noisyPage = wikiPages.FirstOrDefault(p => p.CurationStatus is "duplicate_trace" or "stale_trace");
        if (noisyPage != null)
        {
            yield return Action("update_wiki_note", "Wiki izlerini temizle", "Duplicate/stale trace var; manuel not silmeden curation kontrolu onerilir.", "normal", "update_wiki_note", "wiki", topicId, null, noisyPage.WikiPageId, noisyPage.ConceptKey, [noisyPage.CurationStatus]);
        }

        if (profile.SourceCount > 0 && links.Count == 0)
        {
            yield return Action("link_source_to_concept", "Kaynak-kavram bagini kur", "Kaynak var ama kavram baglari henuz sinirli.", "normal", "link_source_to_concept", "sources", topicId, sources.FirstOrDefault()?.SourceId, null, null, ["source_concept_link_missing"]);
        }

        if (profile.SourceCount >= 2)
        {
            yield return Action("compare_sources", "Kaynaklari karsilastir", "Birden fazla kaynak var; destek durumu compare ile gozden gecirilebilir.", "normal", "compare_sources", "sources", topicId, null, null, null, ["multi_source_ready"]);
        }

        if (profile.SourceQuestionThreadCount > 0)
        {
            yield return Action("ask_source", "Kaynak Q&A hafizasini kullan", "Source Q&A gecmisi safe summary ile devam edebilir; raw chunk/transcript tasinmaz.", "normal", "ask_source", "sources", topicId, sources.FirstOrDefault()?.SourceId, null, null, ["source_qa_memory_available"]);
        }

        if (notebook.Count > 0 || wikiPages.Count > 0 || profile.SourceCount > 0)
        {
            yield return Action("open_notebook_pack", "Notebook pack ac", notebook.Count > 0 ? "Mevcut Notebook Studio pack'i acilabilir." : "Kaynak/Wiki kanitindan yeni pack hazirlanabilir.", "low", "open_notebook_pack", "notebook-studio", topicId, sources.FirstOrDefault()?.SourceId, wikiPages.FirstOrDefault()?.WikiPageId, wikiPages.FirstOrDefault()?.ConceptKey, notebook.Count > 0 ? ["notebook_pack_ready"] : ["notebook_pack_available"]);
        }

        var backed = links.FirstOrDefault(l => l.IsSourceBacked);
        if (backed != null)
        {
            yield return Action("review_source_backed_concept", "Kaynak destekli kavrami tekrar et", "Bu kavram icin source-backed metadata guvenle tasinabilir.", "low", "source_review", "sources", topicId, backed.SourceId, backed.WikiPageId, backed.ConceptKey, ["source_backed_concept"]);
        }

        yield return Action("continue_learning", "Planli calismaya devam et", "Kritik kaynak/Wiki bloklayicisi yoksa normal ogrenme akisi surebilir.", "low", "continue_learning", "dashboard", topicId, null, null, null, ["continue_learning"]);
    }

    private static IEnumerable<SourceWikiProActionDto> BuildTutorHandoffs(IReadOnlyList<SourceWikiProActionDto> actions) =>
        actions
            .Where(a => a.ActionType is "repair_wiki_page" or "repair_source_limited_concept" or "review_source_backed_concept" or "source_review" or "citation_review")
            .Select(a => CloneAction(a, "ask_tutor", "ask_tutor", "chat", ["tutor_safe_source_wiki_handoff"]))
            .Take(4);

    private static IEnumerable<SourceWikiProActionDto> BuildStudyRoomHandoffs(IReadOnlyList<SourceWikiProActionDto> actions, Guid? topicId)
    {
        if (!topicId.HasValue) yield break;

        foreach (var action in actions.Where(a => a.ActionType is "repair_wiki_page" or "review_source_backed_concept" or "repair_source_limited_concept").Take(3))
        {
            yield return CloneAction(action, "open_study_room", "open_study_room", "classroom", ["study_room_available", "personal_ai_study_room"]);
        }
    }

    private static SourceWikiProActionDto CloneAction(
        SourceWikiProActionDto action,
        string actionType,
        string entryPoint,
        string route,
        IReadOnlyList<string> extraReasons) => new()
    {
        ActionType = actionType,
        Label = action.Label,
        Reason = action.Reason,
        Priority = action.Priority,
        EntryPoint = entryPoint,
        TargetRoute = route,
        TopicId = action.TopicId,
        SourceId = action.SourceId,
        WikiPageId = action.WikiPageId,
        ConceptKey = action.ConceptKey,
        ReasonCodes = action.ReasonCodes.Concat(extraReasons).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray()
    };

    private static SourceWikiProActionDto Action(
        string actionType,
        string label,
        string reason,
        string priority,
        string entryPoint,
        string route,
        Guid? topicId,
        Guid? sourceId,
        Guid? wikiPageId,
        string? conceptKey,
        IReadOnlyList<string> reasonCodes) => new()
    {
        ActionType = SafeDisplay(actionType, 80),
        Label = SafeDisplay(label, 180),
        Reason = SafeDisplay(reason, 260),
        Priority = NormalizePriority(priority),
        EntryPoint = SafeDisplay(entryPoint, 80),
        TargetRoute = SafeDisplay(route, 80),
        TopicId = topicId,
        SourceId = sourceId,
        WikiPageId = wikiPageId,
        ConceptKey = SafeDisplay(conceptKey, 120),
        ReasonCodes = reasonCodes.Select(r => SafeDisplay(r, 120)).Where(NotBlank).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray()
    };

    private static SourceWikiProWarningDto Warning(
        string code,
        string severity,
        string label,
        string route,
        IReadOnlyList<string> reasonCodes) => new()
    {
        WarningCode = SafeDisplay(code, 120),
        Severity = string.IsNullOrWhiteSpace(severity) ? "info" : SafeDisplay(severity, 40),
        Label = SafeDisplay(label, 220),
        TargetRoute = SafeDisplay(route, 80),
        ReasonCodes = reasonCodes.Select(r => SafeDisplay(r, 120)).Where(NotBlank).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray()
    };

    private static string ResolveReadinessStatus(
        SourceWikiIntelligenceProfileDto profile,
        SourceWikiProActionDto primary,
        IReadOnlyList<SourceWikiProWarningDto> warnings)
    {
        if (profile.SourceCount == 0 && profile.WikiPageCount == 0) return "thin_evidence";
        if (warnings.Any(w => w.WarningCode == "source_grounding_blocked") || primary.ActionType is "source_review" or "citation_review") return "needs_attention";
        if (profile.CanClaimSourceGrounded && profile.WikiHealthStatus == "clean") return "ready";
        if (profile.ProfileStatus is "limited" or "needs_review") return profile.ProfileStatus;
        return "ready";
    }

    private static string ResolveWikiReadiness(SourceWikiIntelligenceProfileDto profile, IReadOnlyList<SourceWikiProWikiPageDto> pages)
    {
        if (pages.Count == 0) return "empty";
        if (pages.Any(p => p.CurationStatus == "source_limited")) return "source_limited";
        if (pages.Any(p => p.CurationStatus == "repair_pending")) return "repair_pending";
        if (pages.Any(p => p.CurationStatus is "duplicate_trace" or "stale_trace")) return "needs_curation";
        return SafeDisplay(profile.WikiHealthStatus, 80);
    }

    private static string BuildSummary(
        SourceWikiProActionDto primary,
        SourceWikiIntelligenceProfileDto profile,
        NotebookReadiness notebook,
        IReadOnlyList<SourceWikiProWarningDto> warnings)
    {
        if (profile.SourceCount == 0 && profile.WikiPageCount == 0)
        {
            return "Kaynak/Wiki kaniti henuz ince; source-grounded iddia kurulmadan kaynak veya not ekleme onerilir.";
        }

        if (warnings.Any(w => w.WarningCode == "source_grounding_blocked"))
        {
            return $"Source / Wiki Pro once {primary.Label.ToLowerInvariant()} adimini oneriyor; kaynakli iddia kanitla sinirli tutulur.";
        }

        if (notebook.Count > 0)
        {
            return "Kaynak, Wiki ve Notebook Studio kanitlari tek safe evidence workspace olarak hazir.";
        }

        return $"Source / Wiki Pro bugunku guvenli adimi secti: {primary.Label}.";
    }

    private static bool IsSourceBacked(SourceWikiProWikiPageDto page) =>
        IsReady(page.SourceReadiness, page.EvidenceStatus) && page.SourceLimitedSignalCount == 0;

    private static bool IsReady(params string?[] statuses) =>
        statuses.Any(status => !string.IsNullOrWhiteSpace(status) &&
                               (status.Contains("source_grounded", StringComparison.OrdinalIgnoreCase) ||
                                status.Contains("ready", StringComparison.OrdinalIgnoreCase) ||
                                status.Contains("supported", StringComparison.OrdinalIgnoreCase)));

    private static bool IsLimited(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;
        return status.Contains("insufficient", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("stale", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("degraded", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("deleted", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("limited", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("missing", StringComparison.OrdinalIgnoreCase) ||
               status.Contains("unsupported", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCitationWarning(string? status) =>
        !string.IsNullOrWhiteSpace(status) &&
        (status.Contains("missing", StringComparison.OrdinalIgnoreCase) ||
         status.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
         status.Contains("stale", StringComparison.OrdinalIgnoreCase) ||
         status.Contains("review", StringComparison.OrdinalIgnoreCase));

    private static string WarningCodeForCitation(string? status)
    {
        if (status?.Contains("missing", StringComparison.OrdinalIgnoreCase) == true) return "citation_missing";
        if (status?.Contains("unsupported", StringComparison.OrdinalIgnoreCase) == true) return "citation_unsupported";
        if (status?.Contains("stale", StringComparison.OrdinalIgnoreCase) == true) return "citation_stale";
        return "citation_review";
    }

    private static bool IsStatus(SourceWikiProSourceDto source, string status) =>
        source.Status.Contains(status, StringComparison.OrdinalIgnoreCase) ||
        source.SourceReadiness.Contains(status, StringComparison.OrdinalIgnoreCase) ||
        source.EvidenceStatus.Contains(status, StringComparison.OrdinalIgnoreCase);

    private static bool HasWarning(IEnumerable<string> warnings, string needle) =>
        warnings.Any(w => !string.IsNullOrWhiteSpace(w) && w.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static int ActionRank(string action) => action switch
    {
        "source_review" => 0,
        "citation_review" => 1,
        "repair_source_limited_concept" => 2,
        "repair_wiki_page" => 3,
        "update_wiki_note" => 4,
        "link_source_to_concept" => 5,
        "compare_sources" => 6,
        "ask_source" => 7,
        "open_notebook_pack" => 8,
        "review_source_backed_concept" => 9,
        _ => 20
    };

    private static int PriorityScore(string priority) => priority switch
    {
        "urgent" => 4,
        "high" => 3,
        "medium" or "normal" => 2,
        _ => 1
    };

    private static string NormalizePriority(string? priority) => priority switch
    {
        "urgent" => "urgent",
        "high" => "high",
        "medium" => "medium",
        "normal" => "normal",
        "low" => "low",
        _ => "normal"
    };

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

    private sealed record WikiBlockSignals(bool HasTutorTrace, bool HasSourceBlock, bool HasSourceLimitedBlock)
    {
        public static WikiBlockSignals Empty { get; } = new(false, false, false);
    }

    private sealed record NotebookReadiness(string Status, int Count);
}
