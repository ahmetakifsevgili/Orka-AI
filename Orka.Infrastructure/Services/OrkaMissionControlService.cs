using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class OrkaMissionControlService : IOrkaMissionControlService
{
    private readonly OrkaDbContext _db;
    private readonly IOrkaLearningStateService _orkaState;

    public OrkaMissionControlService(
        OrkaDbContext db,
        IOrkaLearningStateService orkaState)
    {
        _db = db;
        _orkaState = orkaState;
    }

    public async Task<OrkaMissionControlDto?> BuildMissionControlAsync(
        Guid userId,
        Guid? topicId = null,
        Guid? sessionId = null,
        string? examCode = "KPSS",
        string? variantCode = null,
        CancellationToken ct = default)
    {
        var state = await _orkaState.BuildStateAsync(userId, topicId, sessionId, examCode, variantCode, ct);
        return state == null ? null : await BuildFromStateAsync(userId, state, ct);
    }

    public async Task<OrkaMissionControlDto> BuildFromStateAsync(
        Guid userId,
        OrkaLearningStateDto state,
        CancellationToken ct = default)
    {
        var notebookPackCount = await _db.LearningNotebookPacks
            .AsNoTracking()
            .CountAsync(p => p.UserId == userId &&
                             !p.IsDeleted &&
                             (!state.TopicId.HasValue || p.TopicId == state.TopicId.Value), ct);

        var rawActions = new[] { state.PrimaryNextAction }
            .Concat(state.SecondaryNextActions)
            .Where(a => !string.IsNullOrWhiteSpace(a.ActionType))
            .ToArray();
        var selectedPrimary = SelectPrimaryAction(state, rawActions);
        var primary = ToMissionAction(selectedPrimary, isPrimary: true);
        var secondary = rawActions
            .Where(a => !SameAction(a, selectedPrimary))
            .Select(a => ToMissionAction(a, isPrimary: false))
            .GroupBy(a => $"{a.ActionType}:{a.ConceptKey}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(a => PriorityScore(a.Priority))
            .ThenBy(a => SectionRank(SectionForAction(a.ActionType)))
            .Take(8)
            .ToArray();
        var warnings = BuildWarnings(state);
        var allMissionActions = new[] { primary }.Concat(secondary).ToArray();
        var studyRoomSuggestion = allMissionActions.FirstOrDefault(a =>
            string.Equals(a.ActionType, "open_study_room", StringComparison.OrdinalIgnoreCase) &&
            state.TopicId.HasValue);
        var reviewLoad = ResolveReviewLoad(state, allMissionActions);
        var repairLoad = ResolveRepairLoad(state, allMissionActions);
        var examLoad = ResolveExamLoad(state, allMissionActions);
        var sourceWikiLoad = ResolveSourceWikiLoad(state, allMissionActions, warnings);
        var evidenceConfidence = ResolveEvidenceConfidence(state);
        var sections = BuildSections(allMissionActions, warnings);
        var cards = BuildModuleCards(state, allMissionActions, warnings, notebookPackCount, studyRoomSuggestion);
        var reasonCodes = state.ReasonCodes
            .Concat(primary.ReasonCodes)
            .Concat(secondary.SelectMany(a => a.ReasonCodes))
            .Concat(warnings.SelectMany(w => w.ReasonCodes))
            .Where(NotBlank)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToArray();

        return new OrkaMissionControlDto
        {
            TopicId = state.TopicId,
            SessionId = state.SessionId,
            ScopeStatus = state.ScopeStatus,
            PrimaryMission = new OrkaTodayMissionDto
            {
                MissionKey = SectionForAction(primary.ActionType),
                ActionType = primary.ActionType,
                Label = primary.Label,
                Reason = primary.Reason,
                Priority = primary.Priority,
                EntryPoint = primary.EntryPoint,
                TargetRoute = primary.TargetRoute,
                TopicId = primary.TopicId,
                ConceptKey = primary.ConceptKey,
                ReasonCodes = primary.ReasonCodes
            },
            PrimaryEntryPoint = primary.EntryPoint,
            SecondaryActions = secondary,
            UrgentWarnings = warnings,
            TodayFocus = primary.Label,
            ReviewLoad = reviewLoad,
            RepairLoad = repairLoad,
            ExamLoad = examLoad,
            SourceWikiLoad = sourceWikiLoad,
            StudyRoomSuggestion = studyRoomSuggestion,
            ModuleCards = cards,
            Sections = sections,
            EvidenceConfidence = evidenceConfidence,
            ReasonCodes = reasonCodes,
            UserSafeSummary = BuildSummary(primary, evidenceConfidence, warnings),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static OrkaUnifiedNextActionDto SelectPrimaryAction(
        OrkaLearningStateDto state,
        IReadOnlyList<OrkaUnifiedNextActionDto> actions)
    {
        var primary = state.PrimaryNextAction;
        if (!state.TopicId.HasValue &&
            string.Equals(primary.ActionType, "open_study_room", StringComparison.OrdinalIgnoreCase))
        {
            primary = actions.FirstOrDefault(a => !string.Equals(a.ActionType, "open_study_room", StringComparison.OrdinalIgnoreCase))
                      ?? state.PrimaryNextAction;
        }

        var sourceBlocked = HasWarning(state, "source_grounding_blocked") ||
                            HasWarning(state, "source_grounded_claim_blocked");
        if (sourceBlocked && primary.ActionType is "continue_plan" or "start_diagnostic")
        {
            primary = actions.FirstOrDefault(a => a.ActionType is "source_review" or "citation_review")
                      ?? SourceReviewFallback(state.TopicId);
        }

        return string.IsNullOrWhiteSpace(primary.ActionType)
            ? SourceReviewFallback(state.TopicId)
            : primary;
    }

    private static OrkaUnifiedNextActionDto SourceReviewFallback(Guid? topicId) => new()
    {
        ActionType = "source_review",
        Label = "Kaynak kanitini toparla",
        Reason = "Kaynak kaniti sinirli; once kaynak/citation kontrolu gerekir.",
        Priority = "high",
        TopicId = topicId,
        Source = "mission_control",
        ReasonCodes = ["source_evidence_limited", "source_grounding_blocked"],
        AppliesTo = ["sources", "wiki", "dashboard"]
    };

    private static OrkaMissionActionDto ToMissionAction(OrkaUnifiedNextActionDto action, bool isPrimary) => new()
    {
        ActionType = action.ActionType,
        Label = SafeText(action.Label, DefaultLabel(action.ActionType)),
        Reason = SafeText(action.Reason, "Bu adim mevcut ogrenme kanitlarindan turetildi."),
        Priority = NormalizePriority(action.Priority),
        EntryPoint = EntryPointForAction(action.ActionType),
        TargetRoute = RouteForAction(action.ActionType),
        TopicId = action.TopicId,
        ConceptKey = SafeOptional(action.ConceptKey),
        IsPrimary = isPrimary,
        ReasonCodes = action.ReasonCodes
            .Where(NotBlank)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray()
    };

    private static IReadOnlyList<OrkaMissionWarningDto> BuildWarnings(OrkaLearningStateDto state)
    {
        var warnings = new List<OrkaMissionWarningDto>();

        foreach (var warning in state.SafetyWarnings.Where(NotBlank))
        {
            warnings.Add(Warning(
                warning,
                warning.Contains("blocked", StringComparison.OrdinalIgnoreCase) ? "warning" : "info",
                WarningLabel(warning),
                RouteForWarning(warning),
                [warning]));
        }

        foreach (var conflict in state.ConflictWarnings)
        {
            warnings.Add(Warning(
                conflict.ConflictCode,
                conflict.Severity,
                SafeText(conflict.UserSafeSummary, WarningLabel(conflict.ConflictCode)),
                RouteForWarning(conflict.ConflictCode),
                conflict.ReasonCodes));
        }

        if (state.SignalSummary.EvidenceCount <= 1)
        {
            warnings.Add(Warning(
                "thin_evidence",
                "info",
                "Kaniti ince; Orka kesin ustalik veya basari iddiasi kurmaz.",
                "dashboard",
                ["thin_evidence"]));
        }

        return warnings
            .GroupBy(w => w.WarningCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(w => SeverityScore(w.Severity))
            .Take(8)
            .ToArray();
    }

    private static OrkaMissionWarningDto Warning(
        string code,
        string severity,
        string label,
        string route,
        IReadOnlyList<string> reasonCodes) => new()
    {
        WarningCode = code,
        Severity = string.IsNullOrWhiteSpace(severity) ? "info" : severity,
        Label = label,
        TargetRoute = route,
        ReasonCodes = reasonCodes.Where(NotBlank).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray()
    };

    private static IReadOnlyList<OrkaMissionSectionDto> BuildSections(
        IReadOnlyList<OrkaMissionActionDto> actions,
        IReadOnlyList<OrkaMissionWarningDto> warnings)
    {
        var sectionKeys = new[]
        {
            "start_here",
            "repair_today",
            "review_due",
            "exam_focus",
            "source_wiki_attention",
            "continue_learning",
            "study_room",
            "notebook_artifacts",
            "progress_snapshot"
        };

        return sectionKeys
            .Select((key, index) =>
            {
                var sectionActions = actions
                    .Where(a => string.Equals(SectionForAction(a.ActionType), key, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(a => PriorityScore(a.Priority))
                    .Take(4)
                    .ToArray();
                var sectionWarnings = warnings
                    .Where(w => string.Equals(SectionForWarning(w.WarningCode), key, StringComparison.OrdinalIgnoreCase))
                    .Take(3)
                    .ToArray();
                var status = sectionActions.Length > 0
                    ? sectionActions.Any(a => a.Priority is "urgent" or "high") ? "ready" : "limited"
                    : sectionWarnings.Length > 0 ? "blocked" : "empty";

                return new OrkaMissionSectionDto
                {
                    SectionKey = key,
                    Status = status,
                    Label = SectionLabel(key),
                    Priority = sectionActions.Length > 0
                        ? sectionActions.Max(a => PriorityScore(a.Priority))
                        : sectionWarnings.Length > 0 ? 3 : Math.Max(0, 1 - index),
                    TargetRoute = sectionActions.FirstOrDefault()?.TargetRoute ?? RouteForSection(key),
                    Actions = sectionActions,
                    Warnings = sectionWarnings,
                    ReasonCodes = sectionActions.SelectMany(a => a.ReasonCodes)
                        .Concat(sectionWarnings.SelectMany(w => w.ReasonCodes))
                        .Where(NotBlank)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(12)
                        .ToArray()
                };
            })
            .OrderByDescending(s => s.Priority)
            .ThenBy(s => SectionRank(s.SectionKey))
            .ToArray();
    }

    private static IReadOnlyList<OrkaMissionModuleCardDto> BuildModuleCards(
        OrkaLearningStateDto state,
        IReadOnlyList<OrkaMissionActionDto> actions,
        IReadOnlyList<OrkaMissionWarningDto> warnings,
        int notebookPackCount,
        OrkaMissionActionDto? studyRoomSuggestion)
    {
        var sourceWarnings = warnings.Count(w => SectionForWarning(w.WarningCode) == "source_wiki_attention");
        var repairActions = actions.Where(a => SectionForAction(a.ActionType) == "repair_today").ToArray();
        var reviewActions = actions.Where(a => SectionForAction(a.ActionType) == "review_due").ToArray();
        var examActions = actions.Where(a => SectionForAction(a.ActionType) == "exam_focus").ToArray();
        var sourceActions = actions.Where(a => SectionForAction(a.ActionType) == "source_wiki_attention").ToArray();

        return
        [
            Card("tutor", "Tutor", "ready", "ask_tutor", "chat", TopPriority(actions), "Tutor bugunku ana gorevi aciklama, repair veya checkpoint'e cevirebilir.", actions.Count(a => a.TargetRoute == "chat"), 0, ["tutor_consumes_unified_state"]),
            Card("study_room", "Study Room", studyRoomSuggestion != null ? "ready" : state.TopicId.HasValue ? "available" : "limited", "open_study_room", "classroom", studyRoomSuggestion?.Priority ?? "normal", state.TopicId.HasValue ? "Kisisel AI study room konu baglami varsa acilabilir." : "Study Room icin once guvenli konu baglami gerekir.", studyRoomSuggestion == null ? 0 : 1, state.TopicId.HasValue ? 0 : 1, state.TopicId.HasValue ? ["study_room_available"] : ["missing_topic_context"]),
            Card("review", "Review", reviewActions.Length > 0 || state.SignalSummary.DueReviewCount > 0 ? "ready" : "empty", "review_due_concept", "learning", reviewActions.FirstOrDefault()?.Priority ?? "normal", state.SignalSummary.DueReviewCount > 0 ? "Zamani gelen tekrar var." : "Zamani gelen tekrar yok.", reviewActions.Length, 0, state.SignalSummary.DueReviewCount > 0 ? ["due_review"] : []),
            Card("exam", "Exam", examActions.Length > 0 ? "ready" : state.ExamLearningProfile?.EvidenceCount > 0 ? "limited" : "empty", "practice_exam_outcome", "central-exams", examActions.FirstOrDefault()?.Priority ?? "normal", examActions.Length > 0 ? "Sinav calismasi icin zayif outcome/pratik sinyali var." : "Sinav kaniti henuz sinirli.", examActions.Length, 0, state.ExamLearningProfile?.ReasonCodes ?? []),
            Card("sources", "Sources", sourceWarnings > 0 ? "blocked" : state.SignalSummary.SourceCount > 0 ? "limited" : "empty", "source_review", "sources", sourceActions.FirstOrDefault()?.Priority ?? (sourceWarnings > 0 ? "high" : "normal"), sourceWarnings > 0 ? "Kaynak/citation kaniti dikkat istiyor." : state.SignalSummary.SourceCount > 0 ? "Kaynaklar var; kanit durumu izleniyor." : "Kaynak yok.", sourceActions.Length, sourceWarnings, state.SourceWikiIntelligenceProfile?.ReasonCodes ?? []),
            Card("wiki", "Wiki", state.SignalSummary.WikiPageCount > 0 ? "limited" : "empty", "update_wiki_note", "wiki", "normal", state.SignalSummary.WikiPageCount > 0 ? "Wiki notlari learning state icinde izleniyor." : "Wiki notu henuz yok.", actions.Count(a => a.TargetRoute == "wiki"), sourceWarnings, state.SourceWikiIntelligenceProfile?.Warnings ?? []),
            Card("notebook_studio", "Notebook Studio", notebookPackCount > 0 ? "ready" : state.SignalSummary.SourceCount > 0 || state.SignalSummary.WikiPageCount > 0 ? "available" : "empty", "open_notebook_pack", "notebook-studio", notebookPackCount > 0 ? "normal" : "low", notebookPackCount > 0 ? "Mevcut notebook packleri acilabilir." : "Kaynak/Wiki kaniti olusunca pack onerilir.", notebookPackCount, 0, notebookPackCount > 0 ? ["notebook_pack_available"] : []),
            Card("quiz_checkpoint", "Quiz / Checkpoint", actions.Any(a => a.ActionType == "take_checkpoint_quiz") ? "ready" : repairActions.Length > 0 ? "available" : "empty", "take_checkpoint_quiz", "chat", repairActions.Length > 0 ? "high" : "normal", repairActions.Length > 0 ? "Repair sonrasi kisa checkpoint uygun." : "Checkpoint icin yeterli repair/review baskisi yok.", actions.Count(a => a.ActionType == "take_checkpoint_quiz"), 0, repairActions.SelectMany(a => a.ReasonCodes).ToArray()),
            Card("progress", "Progress", state.SignalSummary.EvidenceCount > 0 ? "ready" : "limited", "continue_plan", "dashboard", "normal", state.SignalSummary.EvidenceCount > 0 ? "Ilerleme sinyalleri ozetlenebilir." : "Ilerleme icin once ogrenme kaniti gerekir.", 1, warnings.Count(w => w.WarningCode == "thin_evidence"), state.ReasonCodes)
        ];
    }

    private static OrkaMissionModuleCardDto Card(
        string key,
        string label,
        string status,
        string entryPoint,
        string route,
        string priority,
        string summary,
        int actionCount,
        int warningCount,
        IReadOnlyList<string> reasonCodes) => new()
    {
        ModuleKey = key,
        Label = label,
        Status = status,
        EntryPoint = entryPoint,
        TargetRoute = route,
        Priority = NormalizePriority(priority),
        UserSafeSummary = summary,
        ActionCount = actionCount,
        WarningCount = warningCount,
        ReasonCodes = reasonCodes.Where(NotBlank).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray()
    };

    private static string ResolveReviewLoad(OrkaLearningStateDto state, IReadOnlyList<OrkaMissionActionDto> actions)
    {
        if (state.SignalSummary.DueReviewCount >= 3 || actions.Any(a => a.ActionType == "review_due_concept" && a.Priority is "urgent" or "high"))
        {
            return "high";
        }

        return state.SignalSummary.DueReviewCount > 0 || actions.Any(a => a.ActionType == "review_due_concept") ? "medium" : "none";
    }

    private static string ResolveRepairLoad(OrkaLearningStateDto state, IReadOnlyList<OrkaMissionActionDto> actions)
    {
        if (state.SignalSummary.WrongAttemptCount >= 3 ||
            actions.Any(a => (a.ActionType is "repair_concept" or "repair_prerequisite") && a.Priority is "urgent" or "high"))
        {
            return "high";
        }

        return state.SignalSummary.WrongAttemptCount > 0 ||
               state.SignalSummary.BlankOrSkippedAttemptCount > 0 ||
               actions.Any(a => a.ActionType is "repair_concept" or "repair_prerequisite")
            ? "medium"
            : "none";
    }

    private static string ResolveExamLoad(OrkaLearningStateDto state, IReadOnlyList<OrkaMissionActionDto> actions)
    {
        if (actions.Any(a => (a.ActionType is "practice_exam_outcome" or "review_deneme_mistakes") && a.Priority is "urgent" or "high"))
        {
            return "high";
        }

        return actions.Any(a => a.ActionType is "practice_exam_outcome" or "review_deneme_mistakes") ||
               state.ExamLearningProfile?.EvidenceCount > 0 == true
            ? "medium"
            : "none";
    }

    private static string ResolveSourceWikiLoad(
        OrkaLearningStateDto state,
        IReadOnlyList<OrkaMissionActionDto> actions,
        IReadOnlyList<OrkaMissionWarningDto> warnings)
    {
        if (warnings.Any(w => SectionForWarning(w.WarningCode) == "source_wiki_attention" && w.Severity is "warning" or "blocker") ||
            actions.Any(a => (a.ActionType is "source_review" or "citation_review") && a.Priority is "urgent" or "high"))
        {
            return "high";
        }

        return state.SignalSummary.SourceCount > 0 ||
               state.SignalSummary.WikiPageCount > 0 ||
               actions.Any(a => a.ActionType is "source_review" or "citation_review" or "update_wiki_note")
            ? "medium"
            : "none";
    }

    private static string ResolveEvidenceConfidence(OrkaLearningStateDto state)
    {
        if (HasWarning(state, "source_grounding_blocked") || HasWarning(state, "source_grounded_claim_blocked"))
        {
            return "limited_evidence";
        }

        return state.SignalSummary.EvidenceCount <= 1 ||
               state.ConflictWarnings.Any(c => c.ConflictCode == "thin_evidence")
            ? "thin_evidence"
            : "enough_evidence";
    }

    private static string BuildSummary(
        OrkaMissionActionDto primary,
        string evidenceConfidence,
        IReadOnlyList<OrkaMissionWarningDto> warnings)
    {
        var warningNote = warnings.Any(w => w.Severity is "warning" or "blocker")
            ? " Uyari var; once sinirli kaniti dikkate al."
            : string.Empty;
        return $"{primary.Label}: {primary.Reason} Kanit durumu: {evidenceConfidence}.{warningNote}";
    }

    private static bool HasWarning(OrkaLearningStateDto state, string code) =>
        state.SafetyWarnings.Contains(code, StringComparer.OrdinalIgnoreCase) ||
        state.ConflictWarnings.Any(c => string.Equals(c.ConflictCode, code, StringComparison.OrdinalIgnoreCase));

    private static bool SameAction(OrkaUnifiedNextActionDto left, OrkaUnifiedNextActionDto right) =>
        string.Equals(left.ActionType, right.ActionType, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.ConceptKey, right.ConceptKey, StringComparison.OrdinalIgnoreCase);

    private static string SectionForAction(string? actionType) => actionType switch
    {
        "start_diagnostic" => "start_here",
        "repair_concept" or "repair_prerequisite" => "repair_today",
        "review_due_concept" or "create_flashcards" => "review_due",
        "practice_exam_outcome" or "review_deneme_mistakes" => "exam_focus",
        "source_review" or "citation_review" => "source_wiki_attention",
        "open_study_room" => "study_room",
        "update_wiki_note" => "source_wiki_attention",
        "open_notebook_pack" => "notebook_artifacts",
        "take_checkpoint_quiz" => "repair_today",
        "continue_plan" => "continue_learning",
        _ => "start_here"
    };

    private static string SectionForWarning(string? code) =>
        code is not null && (code.Contains("source", StringComparison.OrdinalIgnoreCase) ||
                             code.Contains("citation", StringComparison.OrdinalIgnoreCase) ||
                             code.Contains("wiki", StringComparison.OrdinalIgnoreCase))
            ? "source_wiki_attention"
            : code is not null && code.Contains("exam", StringComparison.OrdinalIgnoreCase)
                ? "exam_focus"
                : code is not null && code.Contains("topic", StringComparison.OrdinalIgnoreCase)
                    ? "study_room"
                    : "start_here";

    private static string EntryPointForAction(string? actionType) => actionType switch
    {
        "open_study_room" => "open_study_room",
        "review_due_concept" => "review_due_concept",
        "practice_exam_outcome" => "practice_exam_outcome",
        "review_deneme_mistakes" => "review_deneme_mistakes",
        "source_review" => "source_review",
        "citation_review" => "citation_review",
        "update_wiki_note" => "update_wiki_note",
        "open_notebook_pack" => "open_notebook_pack",
        "create_flashcards" => "create_flashcards",
        "take_checkpoint_quiz" => "take_checkpoint_quiz",
        "continue_plan" => "continue_plan",
        _ => "ask_tutor"
    };

    private static string RouteForAction(string? actionType) => actionType switch
    {
        "review_due_concept" or "create_flashcards" => "learning",
        "source_review" or "citation_review" => "sources",
        "practice_exam_outcome" or "review_deneme_mistakes" => "central-exams",
        "open_study_room" => "classroom",
        "update_wiki_note" => "wiki",
        "open_notebook_pack" => "notebook-studio",
        "continue_plan" => "dashboard",
        _ => "chat"
    };

    private static string RouteForSection(string key) => key switch
    {
        "review_due" => "learning",
        "exam_focus" => "central-exams",
        "source_wiki_attention" => "sources",
        "study_room" => "classroom",
        "notebook_artifacts" => "notebook-studio",
        "progress_snapshot" => "dashboard",
        _ => "chat"
    };

    private static string RouteForWarning(string code) => SectionForWarning(code) switch
    {
        "source_wiki_attention" => "sources",
        "exam_focus" => "central-exams",
        "study_room" => "classroom",
        _ => "dashboard"
    };

    private static string SectionLabel(string key) => key switch
    {
        "start_here" => "Buradan basla",
        "repair_today" => "Bugunku tamir",
        "review_due" => "Tekrar zamani",
        "exam_focus" => "Sinav odagi",
        "source_wiki_attention" => "Kaynak ve Wiki dikkati",
        "continue_learning" => "Ogrenmeye devam",
        "study_room" => "Study Room",
        "notebook_artifacts" => "Notebook Studio",
        "progress_snapshot" => "Ilerleme ozeti",
        _ => "Gorevler"
    };

    private static string WarningLabel(string code) => code switch
    {
        "source_grounding_blocked" or "source_grounded_claim_blocked" => "Kaynak kaniti sinirli; source-grounded iddia kurulmaz.",
        "official_claim_blocked_without_verified_metadata" => "Resmi/sinav iddiasi dogrulanmis metadata olmadan kapali.",
        "exam_learning_conflict" => "Sinav ve uzun vadeli ogrenme sinyalleri ayrisiyor; repair baskisi korunur.",
        "next_action_conflict" => "Moduller farkli sinyal uretiyor; Orka guvenli onceligi secer.",
        "missing_topic_context" => "Bu aksiyon icin once konu/session baglami gerekir.",
        "thin_evidence" => "Kanit ince; kisa tani veya guvenli baslangic gerekir.",
        _ => code
    };

    private static string DefaultLabel(string actionType) => actionType switch
    {
        "repair_concept" => "Kavrami tamir et",
        "repair_prerequisite" => "On kosulu toparla",
        "review_due_concept" => "Tekrari bitir",
        "practice_exam_outcome" => "Sinav kazanimi calis",
        "review_deneme_mistakes" => "Deneme hatalarini incele",
        "source_review" => "Kaynaklari gozden gecir",
        "citation_review" => "Citation kontrolu yap",
        "open_study_room" => "Study Room ac",
        "take_checkpoint_quiz" => "Kisa checkpoint cozumle",
        "create_flashcards" => "Flashcard olustur",
        "update_wiki_note" => "Wiki notunu duzenle",
        "continue_plan" => "Plana devam et",
        _ => "Kisa tani ile basla"
    };

    private static int SectionRank(string key) => key switch
    {
        "start_here" => 0,
        "repair_today" => 1,
        "review_due" => 2,
        "exam_focus" => 3,
        "source_wiki_attention" => 4,
        "study_room" => 5,
        "continue_learning" => 6,
        "notebook_artifacts" => 7,
        "progress_snapshot" => 8,
        _ => 99
    };

    private static int PriorityScore(string? priority) => priority switch
    {
        "urgent" => 5,
        "high" => 4,
        "medium" => 3,
        "normal" => 2,
        "low" => 1,
        _ => 0
    };

    private static int SeverityScore(string? severity) => severity switch
    {
        "blocker" => 4,
        "warning" => 3,
        "info" => 1,
        _ => 0
    };

    private static string TopPriority(IReadOnlyList<OrkaMissionActionDto> actions) =>
        actions.OrderByDescending(a => PriorityScore(a.Priority)).FirstOrDefault()?.Priority ?? "normal";

    private static string NormalizePriority(string? priority) => priority switch
    {
        "urgent" or "high" or "medium" or "normal" or "low" => priority,
        _ => "normal"
    };

    private static string SafeText(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 220 ? trimmed : trimmed[..220];
    }

    private static string? SafeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 96)];

    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);
}
