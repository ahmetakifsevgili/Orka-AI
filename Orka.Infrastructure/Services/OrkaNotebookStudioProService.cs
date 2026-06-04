using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class OrkaNotebookStudioProService : IOrkaNotebookStudioProService
{
    private static readonly string[] BlockedMarkers =
    [
        "rawPrompt", "hiddenPrompt", "systemPrompt", "developerPrompt", "rawProviderPayload",
        "rawSourceChunk", "rawToolPayload", "debugTrace", "localPath", "apiKey", "secret",
        "token", "answerKey", "correctAnswer", "stackTrace", "ownerId", "userId", "rawTranscript"
    ];

    private readonly OrkaDbContext _db;
    private readonly IOrkaLearningStateService _orkaState;
    private readonly IOrkaMissionControlService _missionControl;
    private readonly IOrkaStudyCoachService _studyCoach;
    private readonly IOrkaExamWarRoomService _examWarRoom;
    private readonly IOrkaSourceWikiProService _sourceWikiPro;
    private readonly IOrkaStudyRoomService _studyRoom;

    public OrkaNotebookStudioProService(
        OrkaDbContext db,
        IOrkaLearningStateService orkaState,
        IOrkaMissionControlService missionControl,
        IOrkaStudyCoachService studyCoach,
        IOrkaExamWarRoomService examWarRoom,
        IOrkaSourceWikiProService sourceWikiPro,
        IOrkaStudyRoomService studyRoom)
    {
        _db = db;
        _orkaState = orkaState;
        _missionControl = missionControl;
        _studyCoach = studyCoach;
        _examWarRoom = examWarRoom;
        _sourceWikiPro = sourceWikiPro;
        _studyRoom = studyRoom;
    }

    public async Task<OrkaNotebookStudioProDto?> BuildProAsync(
        Guid userId,
        Guid? topicId = null,
        Guid? sessionId = null,
        Guid? sourceId = null,
        Guid? wikiPageId = null,
        string? examCode = "KPSS",
        string? variantCode = null,
        string? packType = null,
        CancellationToken ct = default)
    {
        var context = await ResolveContextAsync(userId, topicId, sessionId, sourceId, wikiPageId, ct);
        if (context == null) return null;

        var state = await _orkaState.BuildStateAsync(userId, context.TopicId, context.SessionId, examCode, variantCode, ct);
        if (state == null) return null;

        var mission = await _missionControl.BuildFromStateAsync(userId, state, ct);
        var coach = await _studyCoach.BuildFromMissionControlAsync(userId, state, mission, ct);
        var warRoom = string.IsNullOrWhiteSpace(examCode)
            ? null
            : await _examWarRoom.BuildWarRoomAsync(userId, examCode!, variantCode, null, null, ct);
        var sourcePro = await _sourceWikiPro.BuildProAsync(userId, context.TopicId, context.SourceId, context.WikiPageId, examCode, variantCode, ct);
        var studyRoom = await _studyRoom.BuildStudyRoomAsync(userId, context.TopicId, context.SessionId, examCode, variantCode, context.SourceId, context.WikiPageId, null, ct);

        var packs = await LoadPacksAsync(userId, context, packType, ct);
        var artifacts = await LoadArtifactsAsync(userId, context, packs, ct);
        var recentStudyRoomTrace = await LoadStudyRoomTraceAsync(userId, context, ct);
        var codeFacts = await LoadCodeNotebookFactsAsync(userId, context, ct);
        var actionFacts = BuildActionFacts(state, mission, coach, warRoom, sourcePro, studyRoom, context, recentStudyRoomTrace, codeFacts);
        var actions = BuildActions(context, actionFacts, packs, artifacts, packType)
            .GroupBy(a => $"{a.ActionType}:{a.PackId}:{a.ArtifactId}:{a.SourceId}:{a.WikiPageId}:{a.ConceptKey}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(a => PriorityScore(a.Priority))
            .ThenBy(a => ActionRank(a.ActionType))
            .Take(12)
            .ToArray();
        var warnings = BuildWarnings(context, state, mission, sourcePro, actionFacts, packs, artifacts).ToArray();
        var recommendedPacks = BuildRecommendedPacks(context, packs, actions, actionFacts, packType)
            .OrderByDescending(p => PriorityScore(p.Priority))
            .ThenBy(p => PackRank(p.PackType))
            .Take(8)
            .ToArray();
        var activePack = recommendedPacks.FirstOrDefault() ?? packs.Select(p => ToPackDto(p, "normal")).FirstOrDefault();
        var artifactQueue = BuildArtifactQueue(artifacts, actions, activePack, actionFacts).Take(12).ToArray();
        var exportPreviews = BuildExportPreviews(packs, artifacts, warnings).Take(8).ToArray();
        var sourceLinks = BuildSourceLinks(sourcePro, context, packs).ToArray();
        var wikiLinks = BuildWikiLinks(sourcePro, context, packs).ToArray();
        var conceptLinks = BuildConceptLinks(state, sourcePro, packs, artifacts).ToArray();
        var examLinks = BuildExamLinks(warRoom, packs).ToArray();
        var studyRoomLinks = BuildStudyRoomLinks(recentStudyRoomTrace, packs).ToArray();

        var reasonCodes = state.ReasonCodes
            .Concat(mission.ReasonCodes)
            .Concat(coach.ReasonCodes)
            .Concat(warRoom?.ReasonCodes ?? [])
            .Concat(sourcePro?.ReasonCodes ?? [])
            .Concat(studyRoom?.ReasonCodes ?? [])
            .Concat(actions.SelectMany(a => a.ReasonCodes))
            .Concat(warnings.SelectMany(w => w.ReasonCodes))
            .Concat(recommendedPacks.SelectMany(p => p.ReasonCodes))
            .Where(NotBlank)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(36)
            .ToArray();

        return new OrkaNotebookStudioProDto
        {
            TopicId = context.TopicId,
            SessionId = context.SessionId,
            SourceId = context.SourceId,
            WikiPageId = context.WikiPageId,
            ExamCode = SafeDisplay(examCode, 40),
            VariantCode = SafeDisplay(variantCode, 40),
            ReadinessStatus = ResolveReadinessStatus(actions, warnings, packs, artifacts, actionFacts),
            PackReadiness = ResolvePackReadiness(packs, artifacts, actionFacts),
            RecommendedPacks = recommendedPacks,
            ActivePack = activePack,
            ArtifactQueue = artifactQueue,
            ExportPreviews = exportPreviews,
            SourceEvidenceLinks = sourceLinks,
            WikiEvidenceLinks = wikiLinks,
            ConceptLinks = conceptLinks,
            ExamOutcomeLinks = examLinks,
            StudyRoomTraceLinks = studyRoomLinks,
            TutorHandoffs = actions.Where(a => a.ActionType is "create_repair_pack" or "create_code_repair_pack" or "create_exam_pack" or "create_source_study_pack" or "create_wiki_cleanup_pack").Select(a => CloneAction(a, "ask_tutor", "Tutor ile paketi onar", "chat")).Take(4).ToArray(),
            ReviewHandoffs = actions.Where(a => a.ActionType is "create_review_pack" or "create_flashcards" or "create_code_checkpoint_pack").Take(4).ToArray(),
            SourceWikiHandoffs = actions.Where(a => a.ActionType is "create_source_study_pack" or "create_wiki_cleanup_pack" or "create_slide_outline" or "source_review" or "citation_review").Take(4).ToArray(),
            ExamWarRoomHandoffs = actions.Where(a => a.ActionType is "create_exam_pack" or "create_deneme_mistake_pack").Take(4).ToArray(),
            StudyRoomHandoffs = actions.Where(a => a.ActionType is "create_study_room_summary" or "create_repair_pack" or "create_review_pack").Select(a => CloneAction(a, "open_study_room", "Study Room ile isle", "classroom")).Take(4).ToArray(),
            MissionControlWarnings = mission.UrgentWarnings.Select(w => new NotebookStudioPackWarningDto
            {
                WarningCode = SafeKey(w.WarningCode, "mission_warning"),
                Severity = NormalizeSeverity(w.Severity),
                Label = SafeText(w.Label, "Mission Control uyarisi."),
                Source = "mission_control",
                ReasonCodes = SafeReasonCodes(w.ReasonCodes)
            }).Take(6).ToArray(),
            Warnings = warnings,
            ReasonCodes = reasonCodes,
            UserSafeSummary = BuildSummary(activePack, actions, warnings, actionFacts),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<NotebookContext?> ResolveContextAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        Guid? sourceId,
        Guid? wikiPageId,
        CancellationToken ct)
    {
        if (sourceId.HasValue)
        {
            var source = await _db.LearningSources.AsNoTracking()
                .Where(s => s.Id == sourceId.Value && s.UserId == userId && !s.IsDeleted)
                .Select(s => new { s.TopicId, s.SessionId })
                .FirstOrDefaultAsync(ct);
            if (source == null) return null;
            topicId ??= source.TopicId;
            sessionId ??= source.SessionId;
        }

        if (wikiPageId.HasValue)
        {
            var page = await _db.WikiPages.AsNoTracking()
                .Where(p => p.Id == wikiPageId.Value && p.UserId == userId && !p.IsDeleted)
                .Select(p => new { p.TopicId, p.SessionId })
                .FirstOrDefaultAsync(ct);
            if (page == null) return null;
            topicId ??= page.TopicId;
            sessionId ??= page.SessionId;
        }

        if (sessionId.HasValue)
        {
            var session = await _db.Sessions.AsNoTracking()
                .Where(s => s.Id == sessionId.Value && s.UserId == userId)
                .Select(s => new { s.TopicId })
                .FirstOrDefaultAsync(ct);
            if (session == null) return null;
            topicId ??= session.TopicId;
        }

        if (topicId.HasValue)
        {
            var ownsTopic = await _db.Topics.AsNoTracking()
                .AnyAsync(t => t.Id == topicId.Value && t.UserId == userId && !t.IsArchived, ct);
            if (!ownsTopic) return null;
        }
        else
        {
            topicId = await _db.Topics.AsNoTracking()
                .Where(t => t.UserId == userId && !t.IsArchived && t.ParentTopicId == null)
                .OrderByDescending(t => t.LastAccessedAt)
                .ThenByDescending(t => t.CreatedAt)
                .Select(t => (Guid?)t.Id)
                .FirstOrDefaultAsync(ct);
        }

        return new NotebookContext(topicId, sessionId, sourceId, wikiPageId);
    }

    private async Task<IReadOnlyList<LearningNotebookPack>> LoadPacksAsync(
        Guid userId,
        NotebookContext context,
        string? packType,
        CancellationToken ct)
    {
        var query = _db.LearningNotebookPacks.AsNoTracking()
            .Where(p => p.UserId == userId && !p.IsDeleted);

        if (context.TopicId.HasValue) query = query.Where(p => p.TopicId == context.TopicId.Value);
        if (context.SessionId.HasValue) query = query.Where(p => p.SessionId == context.SessionId.Value || p.SessionId == null);
        if (context.WikiPageId.HasValue) query = query.Where(p => p.WikiPageId == context.WikiPageId.Value || p.WikiPageId == null);
        if (!string.IsNullOrWhiteSpace(packType)) query = query.Where(p => p.PackType == packType);

        return await query
            .OrderByDescending(p => p.UpdatedAt)
            .ThenByDescending(p => p.CreatedAt)
            .Take(16)
            .ToListAsync(ct);
    }

    private async Task<IReadOnlyList<LearningArtifact>> LoadArtifactsAsync(
        Guid userId,
        NotebookContext context,
        IReadOnlyList<LearningNotebookPack> packs,
        CancellationToken ct)
    {
        var packArtifactIds = packs
            .SelectMany(p => ReadGuidList(p.ArtifactIdsJson))
            .Distinct()
            .ToArray();
        var query = _db.LearningArtifacts.AsNoTracking()
            .Where(a => a.UserId == userId && !a.IsDeleted);

        if (packArtifactIds.Length > 0)
        {
            query = query.Where(a => packArtifactIds.Contains(a.Id) ||
                                     (!context.TopicId.HasValue || a.TopicId == context.TopicId.Value));
        }
        else if (context.TopicId.HasValue)
        {
            query = query.Where(a => a.TopicId == context.TopicId.Value);
        }

        if (context.SessionId.HasValue)
        {
            query = query.Where(a => a.SessionId == context.SessionId.Value || a.SessionId == null);
        }

        return await query
            .OrderByDescending(a => a.UpdatedAt)
            .ThenByDescending(a => a.CreatedAt)
            .Take(24)
            .ToListAsync(ct);
    }

    private async Task<StudyRoomTrace?> LoadStudyRoomTraceAsync(Guid userId, NotebookContext context, CancellationToken ct)
    {
        var query = _db.ClassroomSessions.AsNoTracking()
            .Where(s => s.UserId == userId);

        if (context.TopicId.HasValue) query = query.Where(s => s.TopicId == context.TopicId.Value);
        if (context.SessionId.HasValue) query = query.Where(s => s.SessionId == context.SessionId.Value || s.SessionId == null);

        return await query
            .OrderByDescending(s => s.UpdatedAt)
            .Select(s => new StudyRoomTrace(s.Id, s.TopicId, s.SessionId, s.Status, s.LastSegment, s.UpdatedAt))
            .FirstOrDefaultAsync(ct);
    }

    private async Task<CodeNotebookFacts> LoadCodeNotebookFactsAsync(Guid userId, NotebookContext context, CancellationToken ct)
    {
        var signals = await _db.LearningSignals.AsNoTracking()
            .Where(s => s.UserId == userId &&
                        (!context.TopicId.HasValue || s.TopicId == context.TopicId.Value) &&
                        (!context.SessionId.HasValue || s.SessionId == context.SessionId.Value) &&
                        (s.SignalType == LearningSignalTypes.IdeCompileError ||
                         s.SignalType == LearningSignalTypes.IdeRuntimeError ||
                         s.SignalType == LearningSignalTypes.IdeExecutionTimeout ||
                         s.SignalType == LearningSignalTypes.IdeTestFailure ||
                         s.SignalType == LearningSignalTypes.IdeBlankAttempt ||
                         s.SignalType == LearningSignalTypes.IdeRunCompleted))
            .OrderByDescending(s => s.CreatedAt)
            .Take(24)
            .ToListAsync(ct);

        var repeatedError = signals.Count(s => s.SignalType is LearningSignalTypes.IdeCompileError or LearningSignalTypes.IdeRuntimeError or LearningSignalTypes.IdeExecutionTimeout or LearningSignalTypes.IdeTestFailure) >= 2;
        var stableSuccess = signals.Count(s => s.SignalType == LearningSignalTypes.IdeRunCompleted || s.IsPositive == true) >= 2 && !repeatedError;
        var blank = signals.Count(s => s.SignalType == LearningSignalTypes.IdeBlankAttempt) >= 2;
        var reasons = new List<string>();
        if (repeatedError) reasons.Add("code_repair_needed");
        if (stableSuccess) reasons.Add("stable_code_success");
        if (blank) reasons.Add("repeated_blank");
        return new CodeNotebookFacts(repeatedError, stableSuccess, blank, reasons);
    }

    private static ActionFacts BuildActionFacts(
        OrkaLearningStateDto state,
        OrkaMissionControlDto mission,
        OrkaStudyCoachDto coach,
        OrkaExamWarRoomDto? warRoom,
        OrkaSourceWikiProDto? sourcePro,
        OrkaStudyRoomDto? studyRoom,
        NotebookContext context,
        StudyRoomTrace? studyRoomTrace,
        CodeNotebookFacts codeFacts)
    {
        var reasons = state.ReasonCodes
            .Concat(mission.ReasonCodes)
            .Concat(coach.ReasonCodes)
            .Concat(warRoom?.ReasonCodes ?? [])
            .Concat(sourcePro?.ReasonCodes ?? [])
            .Concat(studyRoom?.ReasonCodes ?? [])
            .Concat(codeFacts.ReasonCodes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var primary = mission.PrimaryMission.ActionType;
        var wrong = state.SignalSummary.WrongAttemptCount >= 2 || HasReason(reasons, "repeated_wrong");
        var blank = state.SignalSummary.BlankOrSkippedAttemptCount >= 2 || HasReason(reasons, "repeated_blank");
        var due = mission.ReviewLoad is "medium" or "high" || primary == "review_due_concept" || HasReason(reasons, "due_review") || HasReason(reasons, "due_srs");
        var exam = warRoom?.TodayExamMission.ActionType is "repair_exam_outcome" or "review_deneme_mistakes" or "practice_question_type" ||
                   mission.ExamLoad is "medium" or "high" ||
                   HasReason(reasons, "exam_weak_outcome") ||
                   HasReason(reasons, "deneme_mistake_cluster");
        var sourceBlocked = sourcePro is not null &&
                            (context.SourceId.HasValue ||
                             sourcePro.CitationWarnings.Count > 0 ||
                             sourcePro.StaleSources.Count > 0 ||
                             sourcePro.InsufficientSources.Count > 0 ||
                             sourcePro.DegradedSources.Count > 0) &&
                            !sourcePro.EvidenceMap.CanClaimSourceGrounded &&
                            (sourcePro.ConflictWarnings.Any(w => w.WarningCode is "source_grounding_blocked" or "source_grounded_claim_blocked") ||
                             sourcePro.CitationWarnings.Any(w => w.WarningCode is "source_grounding_blocked" or "source_grounded_claim_blocked" or "citation_unsupported" or "citation_missing") ||
                             HasReason(reasons, "source_evidence_limited") ||
                             HasReason(reasons, "source_stale"));
        var wikiRepair = sourcePro?.WikiRepairPages.Count > 0 ||
                         sourcePro?.RecommendedActions.Any(a => a.ActionType is "repair_wiki_page" or "repair_source_limited_concept") == true ||
                         HasReason(reasons, "wiki_repair_pending");
        var studyRoomReady = studyRoomTrace is not null ||
                             studyRoom?.CurrentTurn.TurnStatus is "completed" or "submitted" ||
                             studyRoom?.ReasonCodes.Contains("study_room_available", StringComparer.OrdinalIgnoreCase) == true;

        return new ActionFacts(
            PrimaryAction: primary,
            HasRepeatedWrong: wrong,
            HasRepeatedBlank: blank,
            HasDueReview: due,
            HasExamPressure: exam,
            HasSourceBlocker: sourceBlocked,
            HasWikiRepair: wikiRepair,
            HasStudyRoomTrace: studyRoomReady,
            HasCodeRepair: codeFacts.HasRepeatedCodeError || codeFacts.HasRepeatedBlank,
            HasCodeCheckpoint: codeFacts.HasStableCodeSuccess,
            HasTopicContext: context.TopicId.HasValue,
            HasExistingEvidence: state.SignalSummary.HasRealLearningData || state.SignalSummary.EvidenceCount > 0,
            ReasonCodes: reasons);
    }

    private static IEnumerable<NotebookStudioPackActionDto> BuildActions(
        NotebookContext context,
        ActionFacts facts,
        IReadOnlyList<LearningNotebookPack> packs,
        IReadOnlyList<LearningArtifact> artifacts,
        string? requestedPackType)
    {
        if (facts.HasSourceBlocker)
        {
            yield return Action("create_source_study_pack", "Kaynak calisma paketi hazirla", "Kaynak/citation kaniti sinirli; kanitli paket etiketi dusuruldu.", "urgent", "Sources", "sources", context, ["source_grounding_blocked", "source_evidence_limited"]);
            yield return Action("citation_review", "Citation kontrolu yap", "Source-backed paket icin citation durumu once gozden gecmeli.", "high", "Sources", "sources", context, ["citation_review", "source_grounding_blocked"]);
        }

        if (facts.HasRepeatedWrong)
        {
            yield return Action("create_repair_pack", "Repair pack olustur", "Tekrarlanan yanlislar on kosul/telafi paketi gerektiriyor.", "urgent", "Tutor", "notebook-studio", context, ["repeated_wrong", "prerequisite_gap"]);
        }

        if (facts.HasRepeatedBlank)
        {
            yield return Action("create_repair_pack", "Guided repair pack olustur", "Bos/atlanmis cevaplar kesin yanilgi degil; tani ve on kosul paketi daha guvenli.", "high", "Tutor", "notebook-studio", context, ["repeated_blank", "prerequisite_gap"]);
        }

        if (facts.HasDueReview)
        {
            yield return Action("create_review_pack", "Review pack hazirla", "Zamani gelen tekrarlar artifact/review paketine donusebilir.", "high", "Review", "review", context, ["due_review", "likely_forgotten"]);
            yield return Action("create_flashcards", "Flashcard paketi hazirla", "Due review yukunu kisa kartlara bol.", "normal", "Review", "flashcards", context, ["due_review"]);
        }

        if (facts.HasExamPressure)
        {
            var actionType = HasReason(facts.ReasonCodes, "deneme_mistake_cluster") ? "create_deneme_mistake_pack" : "create_exam_pack";
            yield return Action(actionType, "Sinav pack hazirla", "Zayif kazanimi/deneme hatasini guvenli pratik paketine cevir.", "high", "Exam War Room", "central-exams", context, ["exam_weak_outcome", "deneme_mistake_cluster"]);
        }

        if (facts.HasWikiRepair)
        {
            yield return Action("create_wiki_cleanup_pack", "Wiki cleanup pack hazirla", "Repair-pending veya source-limited Wiki notlari temizlenmeli.", "high", "Wiki", "wiki", context, ["wiki_repair_pending", "source_limited"]);
        }

        if (facts.HasStudyRoomTrace)
        {
            yield return Action("create_study_room_summary", "Study Room ozeti hazirla", "Tamamlanan veya aktif Study Room izi bounded summary pack olabilir.", "normal", "Study Room", "classroom", context, ["study_room_trace_ready"]);
        }

        if (facts.HasCodeRepair)
        {
            yield return Action("create_code_repair_pack", "Code repair pack hazirla", "Tekrarlanan kod hata/no-attempt sinyali safe repair pack'e donusebilir.", "high", "Code IDE", "notebook-studio", context, ["code_repair_needed", "repeated_blank"]);
        }

        if (facts.HasCodeCheckpoint)
        {
            yield return Action("create_code_checkpoint_pack", "Code checkpoint pack hazirla", "Basarili kod denemeleri checkpoint/artifact pack'e baglanabilir.", "normal", "Code IDE", "notebook-studio", context, ["stable_code_success", "checkpoint_needed"]);
        }

        if (artifacts.Any(a => a.ArtifactType is "slide_deck_outline" or "slide_export_manifest"))
        {
            yield return Action("create_slide_outline", "Slide outline preview ac", "Bu sadece preview/outline; gercek PPTX/video uretilmez.", "normal", "Notebook Studio", "notebook-studio", context, ["export_preview_only"]);
        }

        if (packs.Count > 0)
        {
            foreach (var pack in packs.Take(3))
            {
                yield return Action("open_existing_pack", SafeText(pack.Title, "Mevcut pack'i ac"), "Mevcut paket guvenli metadata ile tekrar acilabilir.", "normal", "Notebook Studio", "notebook-studio", context with { PackId = pack.Id }, ReadStringList(pack.NextActionsJson).DefaultIfEmpty("notebook_pack_ready").ToArray());
            }
        }

        if (!string.IsNullOrWhiteSpace(requestedPackType))
        {
            yield return Action("open_existing_pack", $"{SafeKey(requestedPackType, "artifact_collection")} paketi", "Istenen pack tipi icin mevcut/suggested kontrat hazirlandi.", "normal", "Notebook Studio", "notebook-studio", context, ["notebook_pack_ready"]);
        }

        if (!facts.HasExistingEvidence)
        {
            yield return Action("create_checkpoint_quiz", "Checkpoint quiz blueprint hazirla", "Kanit ince; once kisa checkpoint veya starter pack gerekir.", "normal", "Quiz", "quiz", context, ["thin_evidence"]);
        }

        yield return Action("continue_learning", "Artifact koleksiyonuna devam et", "Yeni kanit geldikce paketler guvenli sekilde genisler.", "low", "Notebook Studio", "notebook-studio", context, ["notebook_pack_ready"]);
    }

    private static IEnumerable<NotebookStudioPackDto> BuildRecommendedPacks(
        NotebookContext context,
        IReadOnlyList<LearningNotebookPack> existing,
        IReadOnlyList<NotebookStudioPackActionDto> actions,
        ActionFacts facts,
        string? requestedPackType)
    {
        foreach (var pack in existing.Take(6))
        {
            yield return ToPackDto(pack, "normal");
        }

        foreach (var action in actions.Where(a => a.ActionType.StartsWith("create_", StringComparison.OrdinalIgnoreCase)).Take(8))
        {
            var packType = PackTypeForAction(action.ActionType, requestedPackType);
            yield return new NotebookStudioPackDto
            {
                PackType = packType,
                Status = "suggested",
                Title = PackTitle(packType),
                Summary = SafeText(action.Reason, "Bu paket mevcut kanittan onerildi."),
                Priority = action.Priority,
                TopicId = context.TopicId,
                SessionId = context.SessionId,
                SourceId = context.SourceId,
                WikiPageId = context.WikiPageId,
                ConceptKeys = action.ConceptKey is null ? Array.Empty<string>() : [action.ConceptKey],
                WarningCodes = action.ReasonCodes.Where(IsWarningReason).Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToArray(),
                ReasonCodes = SafeReasonCodes(action.ReasonCodes.Concat(facts.ReasonCodes).Take(10)),
                Actions = [action]
            };
        }
    }

    private static IEnumerable<NotebookStudioArtifactDto> BuildArtifactQueue(
        IReadOnlyList<LearningArtifact> artifacts,
        IReadOnlyList<NotebookStudioPackActionDto> actions,
        NotebookStudioPackDto? activePack,
        ActionFacts facts)
    {
        foreach (var artifact in artifacts.Take(12))
        {
            yield return new NotebookStudioArtifactDto
            {
                ArtifactId = artifact.Id,
                PackId = activePack?.PackId,
                ArtifactType = SafeKey(artifact.ArtifactType, "artifact"),
                Status = SafeKey(artifact.ArtifactStatus, "draft"),
                Origin = SafeKey(artifact.Origin, "artifact"),
                RenderFormat = SafeKey(artifact.RenderFormat, "metadata"),
                Title = SafeText(artifact.Title, "Artifact"),
                SourceBasis = SafeKey(artifact.SourceBasis, "evidence_insufficient"),
                PreviewOnly = artifact.ArtifactType is "slide_deck_outline" or "slide_export_manifest" or "mind_map" or "uml_diagram",
                ReasonCodes = SafeReasonCodes(ReadStringList(artifact.SafetyWarningsJson).Concat([artifact.SourceBasis])),
                Warnings = ReadStringList(artifact.SafetyWarningsJson).Where(NotBlank).Take(8).ToArray()
            };
        }

        foreach (var action in actions.Where(a => a.ActionType.StartsWith("create_", StringComparison.OrdinalIgnoreCase)).Take(6))
        {
            yield return new NotebookStudioArtifactDto
            {
                PackId = activePack?.PackId,
                ArtifactType = ArtifactTypeForAction(action.ActionType),
                Status = "suggested",
                Origin = "notebook_studio_pro",
                RenderFormat = action.ActionType is "create_slide_outline" ? "outline" : "metadata",
                Title = action.Label,
                SourceBasis = facts.HasSourceBlocker ? "evidence_insufficient" : "derived_metadata",
                PreviewOnly = action.ActionType is "create_slide_outline",
                ReasonCodes = SafeReasonCodes(action.ReasonCodes),
                Warnings = action.ActionType is "create_slide_outline" ? ["export_preview_only"] : Array.Empty<string>()
            };
        }
    }

    private static IEnumerable<NotebookStudioExportPreviewDto> BuildExportPreviews(
        IReadOnlyList<LearningNotebookPack> packs,
        IReadOnlyList<LearningArtifact> artifacts,
        IReadOnlyList<NotebookStudioPackWarningDto> warnings)
    {
        var artifactCount = artifacts.Count;
        var sourceWarning = warnings.Any(w => w.WarningCode is "source_grounding_blocked" or "stale_source_affects_pack")
            ? "source_warning"
            : "none";

        if (packs.Count > 0 || artifacts.Count > 0)
        {
            yield return new NotebookStudioExportPreviewDto
            {
                PreviewType = "manifest",
                ReadinessStatus = "preview_ready",
                PackId = packs.FirstOrDefault()?.Id,
                ArtifactCount = artifactCount,
                SourceWarning = sourceWarning,
                AccessibilityWarning = artifactCount == 0 ? "review_required" : "metadata_only",
                ExportLimitations = ["real_pptx_not_enabled", "video_generation_not_enabled"],
                ReasonCodes = ["export_preview_only"]
            };
        }

        foreach (var artifact in artifacts.Where(a => a.ArtifactType is "slide_deck_outline" or "slide_export_manifest").Take(4))
        {
            yield return new NotebookStudioExportPreviewDto
            {
                PreviewType = "slide_outline",
                ReadinessStatus = "preview_only",
                ArtifactId = artifact.Id,
                ArtifactCount = 1,
                SourceWarning = sourceWarning,
                AccessibilityWarning = "review_required",
                ExportLimitations = ["real_pptx_not_enabled", "video_generation_not_enabled"],
                ReasonCodes = ["export_preview_only"]
            };
        }
    }

    private static IEnumerable<NotebookStudioEvidenceLinkDto> BuildSourceLinks(OrkaSourceWikiProDto? sourcePro, NotebookContext context, IReadOnlyList<LearningNotebookPack> packs)
    {
        if (sourcePro == null) yield break;

        foreach (var source in sourcePro.SourceReadinessItems.Take(8))
        {
            yield return new NotebookStudioEvidenceLinkDto
            {
                LinkType = "source",
                Status = SafeKey(source.EvidenceStatus, "limited"),
                Label = SafeText(source.Title, "Source"),
                TopicId = context.TopicId,
                SourceId = source.SourceId,
                PackId = packs.FirstOrDefault()?.Id,
                SourceBasis = SafeKey(source.EvidenceStatus, "evidence_insufficient"),
                ReasonCodes = SafeReasonCodes(source.Warnings.Concat([source.SourceReadiness, source.EvidenceStatus]))
            };
        }
    }

    private static IEnumerable<NotebookStudioEvidenceLinkDto> BuildWikiLinks(OrkaSourceWikiProDto? sourcePro, NotebookContext context, IReadOnlyList<LearningNotebookPack> packs)
    {
        if (sourcePro == null) yield break;

        foreach (var page in sourcePro.WikiReadinessItems.Take(8))
        {
            yield return new NotebookStudioEvidenceLinkDto
            {
                LinkType = "wiki_page",
                Status = SafeKey(page.EvidenceStatus, "limited"),
                Label = SafeText(page.Title, "Wiki page"),
                TopicId = context.TopicId,
                WikiPageId = page.WikiPageId,
                PackId = packs.FirstOrDefault(p => p.WikiPageId == page.WikiPageId)?.Id,
                ConceptKey = SafeOptional(page.ConceptKey),
                SourceBasis = SafeKey(page.EvidenceStatus, "evidence_insufficient"),
                ReasonCodes = SafeReasonCodes(page.Warnings.Concat([page.CurationStatus, page.SourceReadiness]))
            };
        }
    }

    private static IEnumerable<NotebookStudioEvidenceLinkDto> BuildConceptLinks(
        OrkaLearningStateDto state,
        OrkaSourceWikiProDto? sourcePro,
        IReadOnlyList<LearningNotebookPack> packs,
        IReadOnlyList<LearningArtifact> artifacts)
    {
        foreach (var concept in state.LongTermLearningProfile.Concepts.Take(8))
        {
            yield return new NotebookStudioEvidenceLinkDto
            {
                LinkType = "concept",
                Status = SafeKey(concept.State, "limited"),
                Label = SafeText(concept.Label, concept.ConceptKey),
                TopicId = concept.TopicId,
                PackId = packs.FirstOrDefault(p => ReadStringList(p.WeakConceptKeysJson).Contains(concept.ConceptKey, StringComparer.OrdinalIgnoreCase) ||
                                                   ReadStringList(p.CompletedConceptKeysJson).Contains(concept.ConceptKey, StringComparer.OrdinalIgnoreCase))?.Id,
                ArtifactId = artifacts.FirstOrDefault(a => string.Equals(a.ConceptKey, concept.ConceptKey, StringComparison.OrdinalIgnoreCase))?.Id,
                ConceptKey = SafeOptional(concept.ConceptKey),
                SourceBasis = sourcePro?.SourceBackedConcepts.Any(c => string.Equals(c.ConceptKey, concept.ConceptKey, StringComparison.OrdinalIgnoreCase)) == true
                    ? "source_backed_metadata"
                    : "derived_learning_metadata",
                ReasonCodes = SafeReasonCodes(concept.ReasonCodes)
            };
        }
    }

    private static IEnumerable<NotebookStudioEvidenceLinkDto> BuildExamLinks(OrkaExamWarRoomDto? warRoom, IReadOnlyList<LearningNotebookPack> packs)
    {
        if (warRoom == null) yield break;

        foreach (var outcome in warRoom.WeakOutcomes.Concat(warRoom.DueOutcomes).Take(8))
        {
            yield return new NotebookStudioEvidenceLinkDto
            {
                LinkType = "exam_outcome",
                Status = SafeKey(outcome.ReadinessStatus, "limited"),
                Label = SafeText(outcome.Label, outcome.OutcomeCode),
                PackId = packs.FirstOrDefault(p => p.PackType.Contains("exam", StringComparison.OrdinalIgnoreCase))?.Id,
                ConceptKey = SafeOptional(outcome.TopicCode),
                ExamOutcomeKey = SafeOptional(outcome.OutcomeCode),
                SourceBasis = "exam_profile_metadata",
                ReasonCodes = SafeReasonCodes(outcome.ReasonCodes)
            };
        }
    }

    private static IEnumerable<NotebookStudioEvidenceLinkDto> BuildStudyRoomLinks(StudyRoomTrace? trace, IReadOnlyList<LearningNotebookPack> packs)
    {
        if (trace == null) yield break;

        yield return new NotebookStudioEvidenceLinkDto
        {
            LinkType = "study_room_trace",
            Status = SafeKey(trace.Status, "active"),
            Label = "Study Room bounded trace",
            TopicId = trace.TopicId,
            PackId = packs.FirstOrDefault(p => p.PackType.Contains("study", StringComparison.OrdinalIgnoreCase))?.Id,
            SourceBasis = "bounded_session_trace",
            ReasonCodes = ["study_room_trace_ready"]
        };
    }

    private static IEnumerable<NotebookStudioPackWarningDto> BuildWarnings(
        NotebookContext context,
        OrkaLearningStateDto state,
        OrkaMissionControlDto mission,
        OrkaSourceWikiProDto? sourcePro,
        ActionFacts facts,
        IReadOnlyList<LearningNotebookPack> packs,
        IReadOnlyList<LearningArtifact> artifacts)
    {
        yield return Warning("export_preview_only", "info", "Notebook Studio Pro bu fazda gercek PPTX/video uretmez; preview/outline/manifest metadata gosterir.", "notebook_studio_pro", ["export_preview_only"]);
        yield return Warning("raw_payload_guard", "info", "Raw prompt, kaynak parcasi, Wiki block body ve transcript public kontrata eklenmez.", "notebook_studio_pro", ["raw_payload_guard"]);
        yield return Warning("answer_key_guard", "info", "Checkpoint/quiz answer key submit oncesi artifact payloadina konmaz.", "notebook_studio_pro", ["answer_key_guard"]);

        if (!facts.HasTopicContext)
        {
            yield return Warning("thin_evidence", "info", "Konu baglami sinirli; pack onerileri metadata seviyesinde kalir.", "notebook_studio_pro", ["thin_evidence"]);
        }

        if (facts.HasSourceBlocker)
        {
            yield return Warning("source_grounding_blocked", "warning", "Kanitli pack iddiasi icin kaynak/citation kaniti yeterli degil.", "source_wiki_pro", ["source_grounding_blocked", "source_evidence_limited"]);
        }

        if (sourcePro?.StaleSources.Count > 0)
        {
            yield return Warning("stale_source_affects_pack", "warning", "Stale kaynak mevcut; source study pack kanitli diye sunulamaz.", "source_wiki_pro", ["source_stale"]);
        }

        if (facts.HasWikiRepair)
        {
            yield return Warning("wiki_source_backing_conflict", "warning", "Wiki repair/source-limited state artifact pack uzerinde gorunur tutuldu.", "source_wiki_pro", ["wiki_repair_pending", "source_limited"]);
        }

        if (mission.PrimaryMission.ActionType is "repair_concept" or "repair_prerequisite" &&
            packs.Any(p => p.PackType.Contains("source", StringComparison.OrdinalIgnoreCase)) &&
            !packs.Any(p => p.PackType.Contains("repair", StringComparison.OrdinalIgnoreCase)))
        {
            yield return Warning("notebook_priority_conflict", "warning", "Mission repair isterken mevcut notebook agirligi source pack tarafinda; repair pack onerildi.", "mission_control", ["notebook_priority_conflict"]);
        }

        if (artifacts.Any(a => ContainsBlockedMarker(a.Title) || ContainsBlockedMarker(a.SafetyWarningsJson)))
        {
            yield return Warning("raw_payload_guard", "warning", "Artifact metadata guard unsafe marker yakaladi; raw content public DTO'ya alinmadi.", "notebook_studio_pro", ["raw_payload_guard"]);
        }

        if (state.SafetyWarnings.Any(w => w.Contains("official", StringComparison.OrdinalIgnoreCase)))
        {
            yield return Warning("official_claim_blocked", "warning", "Resmi alignment iddiasi verified metadata olmadan paketlenmez.", "learning_state", ["official_claim_blocked"]);
        }

        if (context.SourceId.HasValue && sourcePro?.EvidenceMap.CanClaimSourceGrounded != true)
        {
            yield return Warning("source_grounding_blocked", "warning", "Bu kaynak icin kanitli artifact ancak evidence hazir oldugunda gosterilir.", "source_wiki_pro", ["source_grounding_blocked"]);
        }
    }

    private static NotebookStudioPackDto ToPackDto(LearningNotebookPack pack, string priority) => new()
    {
        PackId = pack.Id,
        PackType = SafeKey(pack.PackType, "artifact_collection"),
        Status = SafeKey(pack.PackStatus, "draft"),
        Title = SafeText(pack.Title, "Notebook pack"),
        Summary = SafeText(pack.Summary, "Guvenli notebook pack metadata."),
        Priority = priority,
        TopicId = pack.TopicId,
        SessionId = pack.SessionId,
        WikiPageId = pack.WikiPageId,
        ConceptKeys = ReadStringList(pack.CompletedConceptKeysJson).Concat(ReadStringList(pack.WeakConceptKeysJson)).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray(),
        WarningCodes = ReadStringList(pack.WarningsJson).Where(NotBlank).Take(8).ToArray(),
        ReasonCodes = ReadStringList(pack.NextActionsJson).Concat(ReadStringList(pack.WarningsJson)).Where(NotBlank).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray(),
        Actions = ReadStringList(pack.NextActionsJson).Take(3).Select(a => new NotebookStudioPackActionDto
        {
            ActionType = SafeKey(a, "continue_learning"),
            Label = SafeText(a.Replace('_', ' '), "Notebook action"),
            Reason = "Mevcut pack metadata'sindan guvenli handoff.",
            Priority = "normal",
            EntryPoint = "Notebook Studio",
            TargetRoute = "notebook-studio",
            TopicId = pack.TopicId,
            SessionId = pack.SessionId,
            WikiPageId = pack.WikiPageId,
            PackId = pack.Id,
            ReasonCodes = [SafeKey(a, "notebook_pack_ready")]
        }).ToArray()
    };

    private static NotebookStudioPackActionDto Action(
        string actionType,
        string label,
        string reason,
        string priority,
        string entryPoint,
        string route,
        NotebookContext context,
        IReadOnlyList<string> reasonCodes) => new()
    {
        ActionType = SafeKey(actionType, "continue_learning"),
        Label = SafeText(label, "Notebook action"),
        Reason = SafeText(reason, "Mevcut kanitla guvenli notebook handoff."),
        Priority = NormalizePriority(priority),
        EntryPoint = SafeText(entryPoint, "Notebook Studio"),
        TargetRoute = SafeKey(route, "notebook-studio"),
        TopicId = context.TopicId,
        SessionId = context.SessionId,
        SourceId = context.SourceId,
        WikiPageId = context.WikiPageId,
        PackId = context.PackId,
        ReasonCodes = SafeReasonCodes(reasonCodes)
    };

    private static NotebookStudioPackActionDto CloneAction(NotebookStudioPackActionDto action, string actionType, string label, string route) => new()
    {
        ActionType = SafeKey(actionType, action.ActionType),
        Label = SafeText(label, action.Label),
        Reason = action.Reason,
        Priority = action.Priority,
        EntryPoint = label,
        TargetRoute = route,
        TopicId = action.TopicId,
        SessionId = action.SessionId,
        SourceId = action.SourceId,
        WikiPageId = action.WikiPageId,
        PackId = action.PackId,
        ArtifactId = action.ArtifactId,
        ConceptKey = action.ConceptKey,
        ExamOutcomeKey = action.ExamOutcomeKey,
        ReasonCodes = action.ReasonCodes
    };

    private static NotebookStudioPackWarningDto Warning(string code, string severity, string label, string source, IReadOnlyList<string> reasons) => new()
    {
        WarningCode = SafeKey(code, "notebook_warning"),
        Severity = NormalizeSeverity(severity),
        Label = SafeText(label, "Notebook Studio Pro uyarisi."),
        Source = SafeKey(source, "notebook_studio_pro"),
        ReasonCodes = SafeReasonCodes(reasons)
    };

    private static string ResolveReadinessStatus(
        IReadOnlyList<NotebookStudioPackActionDto> actions,
        IReadOnlyList<NotebookStudioPackWarningDto> warnings,
        IReadOnlyList<LearningNotebookPack> packs,
        IReadOnlyList<LearningArtifact> artifacts,
        ActionFacts facts)
    {
        if (warnings.Any(w => w.WarningCode == "source_grounding_blocked")) return "blocked";
        if (actions.Any(a => a.Priority == "urgent")) return "urgent";
        if (packs.Count > 0 || artifacts.Count > 0) return "ready";
        return facts.HasExistingEvidence ? "limited" : "thin_evidence";
    }

    private static string ResolvePackReadiness(IReadOnlyList<LearningNotebookPack> packs, IReadOnlyList<LearningArtifact> artifacts, ActionFacts facts)
    {
        if (packs.Count > 0 && artifacts.Count > 0) return "ready";
        if (packs.Count > 0) return "pack_ready";
        if (artifacts.Count > 0) return "artifact_ready";
        return facts.HasExistingEvidence ? "suggested" : "thin_evidence";
    }

    private static string BuildSummary(NotebookStudioPackDto? pack, IReadOnlyList<NotebookStudioPackActionDto> actions, IReadOnlyList<NotebookStudioPackWarningDto> warnings, ActionFacts facts)
    {
        if (warnings.Any(w => w.WarningCode == "source_grounding_blocked"))
        {
            return "Kanitli artifact iddiasi icin kanit sinirli; once kaynak/citation temizligi oneriliyor.";
        }

        if (pack is not null && actions.Count > 0)
        {
            return $"{pack.Title}: {actions[0].Label}.";
        }

        return facts.HasExistingEvidence
            ? "Notebook Studio Pro mevcut ogrenme kanitindan guvenli pack onerileri hazirladi."
            : "Kanit ince; kisa checkpoint veya starter artifact ile baslamak daha guvenli.";
    }

    private static string PackTypeForAction(string actionType, string? requested) => !string.IsNullOrWhiteSpace(requested)
        ? SafeKey(requested, "artifact_collection")
        : actionType switch
        {
            "create_repair_pack" => "repair_pack",
            "create_review_pack" => "review_pack",
            "create_exam_pack" => "exam_outcome_pack",
            "create_deneme_mistake_pack" => "deneme_mistake_pack",
            "create_source_study_pack" => "source_study_pack",
            "create_wiki_cleanup_pack" => "wiki_cleanup_pack",
            "create_study_room_summary" => "study_room_summary_pack",
            "create_code_repair_pack" => "code_repair_pack",
            "create_code_checkpoint_pack" => "code_checkpoint_pack",
            "create_flashcards" => "flashcard_pack",
            "create_checkpoint_quiz" => "checkpoint_quiz_pack",
            "create_slide_outline" => "slide_outline_pack",
            "create_audio_script" => "audio_script_pack",
            _ => "artifact_collection"
        };

    private static string ArtifactTypeForAction(string actionType) => actionType switch
    {
        "create_repair_pack" => "misconception_repair_pack",
        "create_review_pack" => "retrieval_card_set",
        "create_exam_pack" => "exam_outcome_practice_pack",
        "create_deneme_mistake_pack" => "deneme_mistake_pack",
        "create_source_study_pack" => "source_digest",
        "create_wiki_cleanup_pack" => "wiki_cleanup_manifest",
        "create_study_room_summary" => "study_room_summary",
        "create_code_repair_pack" => "code_repair_pack",
        "create_code_checkpoint_pack" => "code_checkpoint_blueprint",
        "create_flashcards" => "flashcard_set",
        "create_checkpoint_quiz" => "review_quiz",
        "create_slide_outline" => "slide_deck_outline",
        "create_audio_script" => "audio_script",
        _ => "study_guide"
    };

    private static string PackTitle(string packType) => packType switch
    {
        "repair_pack" => "Repair pack",
        "review_pack" => "Review pack",
        "exam_outcome_pack" => "Exam outcome pack",
        "deneme_mistake_pack" => "Deneme mistake pack",
        "source_study_pack" => "Source study pack",
        "wiki_cleanup_pack" => "Wiki cleanup pack",
        "study_room_summary_pack" => "Study Room summary pack",
        "code_repair_pack" => "Code repair pack",
        "code_checkpoint_pack" => "Code checkpoint pack",
        "flashcard_pack" => "Flashcard pack",
        "checkpoint_quiz_pack" => "Checkpoint quiz pack",
        "slide_outline_pack" => "Slide outline pack",
        "audio_script_pack" => "Audio script pack",
        _ => "Artifact collection"
    };

    private static int PackRank(string packType) => packType switch
    {
        "repair_pack" => 0,
        "review_pack" => 1,
        "exam_outcome_pack" or "deneme_mistake_pack" => 2,
        "source_study_pack" or "wiki_cleanup_pack" => 3,
        "study_room_summary_pack" => 4,
        "code_repair_pack" or "code_checkpoint_pack" => 5,
        "flashcard_pack" or "checkpoint_quiz_pack" => 6,
        "slide_outline_pack" or "audio_script_pack" => 7,
        _ => 10
    };

    private static int ActionRank(string actionType) => actionType switch
    {
        "create_source_study_pack" when true => 0,
        "citation_review" => 1,
        "create_repair_pack" => 2,
        "create_review_pack" => 3,
        "create_exam_pack" or "create_deneme_mistake_pack" => 4,
        "create_wiki_cleanup_pack" => 5,
        "create_study_room_summary" => 6,
        "create_code_repair_pack" => 7,
        "create_code_checkpoint_pack" => 8,
        "create_flashcards" => 9,
        "create_checkpoint_quiz" => 10,
        _ => 20
    };

    private static int PriorityScore(string? priority) => priority switch
    {
        "urgent" => 4,
        "high" => 3,
        "normal" or "medium" => 2,
        "low" => 1,
        _ => 0
    };

    private static bool IsWarningReason(string reason) => reason.Contains("blocked", StringComparison.OrdinalIgnoreCase) ||
                                                          reason.Contains("limited", StringComparison.OrdinalIgnoreCase) ||
                                                          reason.Contains("stale", StringComparison.OrdinalIgnoreCase) ||
                                                          reason.Contains("warning", StringComparison.OrdinalIgnoreCase);

    private static bool HasReason(IEnumerable<string> reasons, string reason) =>
        reasons.Any(r => string.Equals(r, reason, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> ReadStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            var values = JsonSerializer.Deserialize<string[]>(json);
            return values?.Where(NotBlank).Select(v => SafeKey(v, "metadata")).Take(24).ToArray() ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<Guid> ReadGuidList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<Guid>();
        try
        {
            var values = JsonSerializer.Deserialize<Guid[]>(json);
            return values?.Where(id => id != Guid.Empty).Take(64).ToArray() ?? Array.Empty<Guid>();
        }
        catch
        {
            return Array.Empty<Guid>();
        }
    }

    private static IReadOnlyList<string> SafeReasonCodes(IEnumerable<string> values) =>
        values.Where(NotBlank)
            .Select(v => SafeKey(v, "reason"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

    private static string SafeText(string? value, string fallback, int maxLength = 180)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (var marker in BlockedMarkers)
        {
            text = Regex.Replace(text, Regex.Escape(marker), "[redacted]", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(2000));
        }

        text = Regex.Replace(text, @"[A-Za-z]:\\[^\s]+", "[redacted_path]", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(2000));
        text = Regex.Replace(text, "source-grounded", "kanitli", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(2000));
        text = text.Replace("\r", " ").Replace("\n", " ");
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static string SafeDisplay(string? value, int maxLength) => SafeText(value, string.Empty, maxLength);

    private static string? SafeOptional(string? value)
    {
        var safe = SafeText(value, string.Empty, 100);
        return string.IsNullOrWhiteSpace(safe) ? null : safe;
    }

    private static string SafeKey(string? value, string fallback)
    {
        var safe = SafeText(value, fallback, 100).ToLowerInvariant();
        safe = Regex.Replace(safe, @"[^a-z0-9_\-]+", "_", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(2000)).Trim('_');
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
    }

    private static string NormalizePriority(string? value) => value switch
    {
        "urgent" or "high" or "normal" or "medium" or "low" => value,
        _ => "normal"
    };

    private static string NormalizeSeverity(string? value) => value switch
    {
        "critical" or "warning" or "info" => value,
        _ => "info"
    };

    private static bool ContainsBlockedMarker(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        BlockedMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);

    private sealed record NotebookContext(Guid? TopicId, Guid? SessionId, Guid? SourceId, Guid? WikiPageId)
    {
        public Guid? PackId { get; init; }
    }

    private sealed record ActionFacts(
        string PrimaryAction,
        bool HasRepeatedWrong,
        bool HasRepeatedBlank,
        bool HasDueReview,
        bool HasExamPressure,
        bool HasSourceBlocker,
        bool HasWikiRepair,
        bool HasStudyRoomTrace,
        bool HasCodeRepair,
        bool HasCodeCheckpoint,
        bool HasTopicContext,
        bool HasExistingEvidence,
        IReadOnlyList<string> ReasonCodes);

    private sealed record CodeNotebookFacts(
        bool HasRepeatedCodeError,
        bool HasStableCodeSuccess,
        bool HasRepeatedBlank,
        IReadOnlyList<string> ReasonCodes);

    private sealed record StudyRoomTrace(Guid Id, Guid? TopicId, Guid? SessionId, string Status, string? LastSegment, DateTime UpdatedAt);
}
