using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class OrkaStudyCoachService : IOrkaStudyCoachService
{
    private static readonly TimeSpan ComebackSuggestedGap = TimeSpan.FromDays(4);
    private static readonly TimeSpan ComebackNeededGap = TimeSpan.FromDays(10);

    private readonly OrkaDbContext _db;
    private readonly IOrkaLearningStateService _orkaState;
    private readonly IOrkaMissionControlService _missionControl;

    public OrkaStudyCoachService(
        OrkaDbContext db,
        IOrkaLearningStateService orkaState,
        IOrkaMissionControlService missionControl)
    {
        _db = db;
        _orkaState = orkaState;
        _missionControl = missionControl;
    }

    public async Task<OrkaStudyCoachDto?> BuildStudyCoachAsync(
        Guid userId,
        Guid? topicId = null,
        Guid? sessionId = null,
        string? examCode = "KPSS",
        string? variantCode = null,
        CancellationToken ct = default)
    {
        var state = await _orkaState.BuildStateAsync(userId, topicId, sessionId, examCode, variantCode, ct);
        if (state == null)
        {
            return null;
        }

        var mission = await _missionControl.BuildFromStateAsync(userId, state, ct);
        return await BuildFromMissionControlAsync(userId, state, mission, ct);
    }

    public async Task<OrkaStudyCoachDto> BuildFromMissionControlAsync(
        Guid userId,
        OrkaLearningStateDto state,
        OrkaMissionControlDto missionControl,
        CancellationToken ct = default)
    {
        var activity = await LoadActivityFactsAsync(userId, state.TopicId, state.SessionId, ct);
        var workload = BuildWorkload(state, missionControl);
        var rhythm = ResolveRhythm(state, missionControl, workload, activity);
        var focusPlan = BuildFocusPlan(state, missionControl, rhythm, workload);
        var comebackPlan = BuildComebackPlan(missionControl, rhythm, focusPlan, activity);
        var warnings = BuildWarnings(state, missionControl, rhythm, workload, focusPlan, activity);
        var actions = BuildActions(missionControl, focusPlan)
            .GroupBy(a => $"{a.ActionType}:{a.ConceptKey}:{a.TargetRoute}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(a => PriorityScore(a.Priority))
            .ThenBy(a => ActionRank(a.ActionType))
            .Take(8)
            .ToArray();
        var reasonCodes = rhythm.ReasonCodes
            .Concat(workload.OverallLoad == "high" ? ["workload_high"] : Array.Empty<string>())
            .Concat(focusPlan.ReasonCodes)
            .Concat(comebackPlan.ReasonCodes)
            .Concat(warnings.SelectMany(w => w.ReasonCodes))
            .Concat(actions.SelectMany(a => a.ReasonCodes))
            .Where(NotBlank)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(18)
            .ToArray();

        return new OrkaStudyCoachDto
        {
            TopicId = state.TopicId,
            SessionId = state.SessionId,
            ScopeStatus = state.ScopeStatus,
            RhythmStatus = rhythm.RhythmStatus,
            RecommendedPace = rhythm.RecommendedPace,
            TodayPlan = rhythm.TodayPlan,
            WeeklyPlan = rhythm.WeeklyPlan,
            Workload = workload,
            FocusPlan = focusPlan,
            ComebackPlan = comebackPlan,
            Actions = actions,
            Warnings = warnings,
            ReasonCodes = reasonCodes,
            UserSafeSummary = BuildSummary(rhythm, workload, comebackPlan),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<ActivityFacts> LoadActivityFactsAsync(Guid userId, Guid? topicId, Guid? sessionId, CancellationToken ct)
    {
        var quiz = await _db.QuizAttempts.AsNoTracking()
            .Where(a => a.UserId == userId &&
                        (!topicId.HasValue || a.TopicId == topicId.Value) &&
                        (!sessionId.HasValue || a.SessionId == sessionId.Value))
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => (DateTime?)a.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var signal = await _db.LearningSignals.AsNoTracking()
            .Where(s => s.UserId == userId &&
                        (!topicId.HasValue || s.TopicId == topicId.Value) &&
                        (!sessionId.HasValue || s.SessionId == sessionId.Value))
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => (DateTime?)s.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var review = await _db.ReviewItems.AsNoTracking()
            .Where(r => r.UserId == userId &&
                        (!topicId.HasValue || r.TopicId == topicId.Value))
            .OrderByDescending(r => r.UpdatedAt)
            .Select(r => (DateTime?)r.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        var source = await _db.LearningSources.AsNoTracking()
            .Where(s => s.UserId == userId &&
                        !s.IsDeleted &&
                        (!topicId.HasValue || s.TopicId == topicId.Value) &&
                        (!sessionId.HasValue || s.SessionId == sessionId.Value))
            .OrderByDescending(s => s.UpdatedAt)
            .Select(s => (DateTime?)s.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        var wiki = await _db.WikiPages.AsNoTracking()
            .Where(p => p.UserId == userId &&
                        !p.IsDeleted &&
                        (!topicId.HasValue || p.TopicId == topicId.Value) &&
                        (!sessionId.HasValue || p.SessionId == sessionId.Value))
            .OrderByDescending(p => p.UpdatedAt)
            .Select(p => (DateTime?)p.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        var studyRoom = await _db.ClassroomSessions.AsNoTracking()
            .Where(c => c.UserId == userId &&
                        (!topicId.HasValue || c.TopicId == topicId.Value) &&
                        (!sessionId.HasValue || c.SessionId == sessionId.Value))
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => (DateTime?)c.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        var last = new[] { quiz, signal, review, source, wiki, studyRoom }
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty()
            .Max();
        if (last == default)
        {
            return new ActivityFacts(null, null);
        }

        var days = Math.Max(0, (int)Math.Floor((DateTime.UtcNow - last).TotalDays));
        return new ActivityFacts(last, days);
    }

    private static OrkaStudyLoadDto BuildWorkload(OrkaLearningStateDto state, OrkaMissionControlDto mission)
    {
        var longTermRhythm = state.LongTermLearningProfile.WeeklyRhythm;
        var newLearningLoad = ResolveNewLearningLoad(state, mission, longTermRhythm);
        var score =
            LoadScore(mission.ReviewLoad) +
            LoadScore(mission.RepairLoad) +
            LoadScore(mission.ExamLoad) +
            LoadScore(mission.SourceWikiLoad) +
            LoadScore(newLearningLoad);
        var highBuckets = new[] { mission.ReviewLoad, mission.RepairLoad, mission.ExamLoad, mission.SourceWikiLoad }
            .Count(load => LoadScore(load) >= 3);
        var mediumBuckets = new[] { mission.ReviewLoad, mission.RepairLoad, mission.ExamLoad, mission.SourceWikiLoad }
            .Count(load => LoadScore(load) >= 2);

        var overall = score >= 8 || highBuckets >= 2
            ? "high"
            : score >= 5 || mediumBuckets >= 2
                ? "medium"
                : score >= 2 ? "light" : "none";

        return new OrkaStudyLoadDto
        {
            ReviewLoad = NormalizeLoad(mission.ReviewLoad),
            RepairLoad = NormalizeLoad(mission.RepairLoad),
            ExamLoad = NormalizeLoad(mission.ExamLoad),
            SourceWikiLoad = NormalizeLoad(mission.SourceWikiLoad),
            NewLearningLoad = NormalizeLoad(newLearningLoad),
            OverallLoad = overall,
            LoadScore = score
        };
    }

    private static RhythmDecision ResolveRhythm(
        OrkaLearningStateDto state,
        OrkaMissionControlDto mission,
        OrkaStudyLoadDto workload,
        ActivityFacts activity)
    {
        var reasons = new List<string>();
        var primary = mission.PrimaryMission.ActionType;
        var thinEvidence = !state.SignalSummary.HasRealLearningData || state.SignalSummary.EvidenceCount == 0;
        var hasRepeatedWrong = state.SignalSummary.WrongAttemptCount >= 2 ||
                               HasReason(state, mission, "repeated_wrong");
        var hasRepeatedBlank = state.SignalSummary.BlankOrSkippedAttemptCount >= 2 ||
                               HasReason(state, mission, "repeated_blank");
        var comebackNeeded = activity.InactivityDays.HasValue &&
                             activity.InactivityDays.Value >= (int)ComebackNeededGap.TotalDays;
        var comebackSuggested = activity.InactivityDays.HasValue &&
                                activity.InactivityDays.Value >= (int)ComebackSuggestedGap.TotalDays;

        if (thinEvidence)
        {
            reasons.Add("thin_evidence");
            return new RhythmDecision(
                "thin_evidence",
                "light",
                "Kisa kontrol veya tek baslangic adimiyle veri biriktir.",
                "Bu hafta once guvenli ogrenme kaniti biriktir.",
                reasons);
        }

        if (comebackNeeded || comebackSuggested)
        {
            reasons.Add(comebackNeeded ? "comeback_needed" : "inactivity_gap");
            reasons.Add("limited_scope");
            return new RhythmDecision(
                "comeback",
                "comeback_ramp",
                "Bugun tek kisa donus adimi sec; tum birikeni ayni anda kapatma.",
                "Bu hafta once tekrar ve tek repair hedefiyle ritmi yeniden kur.",
                reasons);
        }

        if (workload.SourceWikiLoad == "high" &&
            (primary is "source_review" or "citation_review" ||
             mission.UrgentWarnings.Any(w => w.WarningCode is "source_grounding_blocked" or "source_grounded_claim_blocked")))
        {
            reasons.Add("source_evidence_limited");
            return new RhythmDecision(
                "source_cleanup",
                "short_repair",
                "Kaynak/citation uyarisini temizle; sonra kaynakli calismaya gec.",
                "Bu hafta kaynak kaniti ve Wiki not sagligini netlestir.",
                reasons);
        }

        if ((workload.ExamLoad == "high" ||
             primary is "practice_exam_outcome" or "review_deneme_mistakes") &&
            primary is "practice_exam_outcome" or "review_deneme_mistakes")
        {
            reasons.Add(primary == "review_deneme_mistakes" ? "deneme_mistake_cluster" : "exam_weak_outcome");
            return new RhythmDecision(
                "exam_heavy",
                "deep_focus",
                "Bugun sinav outcome/pratik blokunu one al; garanti iddiasi olmadan checkpoint ile kapat.",
                "Bu hafta zayif outcome ve deneme hata kumelerini takip et.",
                reasons);
        }

        if (workload.RepairLoad == "high" || hasRepeatedWrong || hasRepeatedBlank)
        {
            reasons.Add(hasRepeatedBlank ? "repeated_blank" : "repeated_wrong");
            reasons.Add(hasRepeatedBlank ? "prerequisite_gap" : "repair_pending");
            return new RhythmDecision(
                "repair_heavy",
                workload.OverallLoad == "high" ? "short_repair" : "deep_focus",
                "Bugun yeni konuyu sinirla; once tek repair/prerequisite hedefini toparla.",
                "Bu hafta repair kuyrugunu kucuk checkpointlerle azalt.",
                reasons);
        }

        if (state.SignalSummary.DueReviewCount > 0 || workload.ReviewLoad is "medium" or "high")
        {
            reasons.Add("due_review");
            return new RhythmDecision(
                "review_heavy",
                "review_sprint",
                "Bugun zamani gelen tekrari kapat; sonra kisa checkpoint yap.",
                "Bu hafta due review baskisini kucuk tekrar bloklariyla dusur.",
                reasons);
        }

        if (workload.ExamLoad == "high" ||
            primary is "practice_exam_outcome" or "review_deneme_mistakes")
        {
            reasons.Add(primary == "review_deneme_mistakes" ? "deneme_mistake_cluster" : "exam_weak_outcome");
            return new RhythmDecision(
                "exam_heavy",
                "deep_focus",
                "Bugun sinav outcome/pratik blokunu one al; garanti iddiasi olmadan checkpoint ile kapat.",
                "Bu hafta zayif outcome ve deneme hata kumelerini takip et.",
                reasons);
        }

        if (primary == "open_study_room" && state.TopicId.HasValue)
        {
            reasons.Add("study_room_available");
            return new RhythmDecision(
                "focused",
                "deep_focus",
                "Bugun Study Room ile tek konu uzerinden sesli ders/repair akisi yap.",
                "Bu hafta Study Room ciktilarini review ve Wiki notuna bagla.",
                reasons);
        }

        if (HasReason(state, mission, "stable_recent_success") && workload.OverallLoad is "none" or "light")
        {
            reasons.Add("stable_recent_success");
            return new RhythmDecision(
                "normal",
                "normal",
                "Bugun plan akisini surdur; kisa checkpoint ile bitir.",
                "Bu hafta yeni ogrenme ve hafif tekrar dengesi korunabilir.",
                reasons);
        }

        reasons.Add("continue_plan");
        return new RhythmDecision(
            workload.OverallLoad == "medium" ? "focused" : "normal",
            workload.OverallLoad == "medium" ? "deep_focus" : "normal",
            "Bugun Mission Control'un ana gorevini tek odak olarak uygula.",
            "Bu hafta review, repair ve yeni ogrenme dengesini koru.",
            reasons);
    }

    private static OrkaFocusPlanDto BuildFocusPlan(
        OrkaLearningStateDto state,
        OrkaMissionControlDto mission,
        RhythmDecision rhythm,
        OrkaStudyLoadDto workload)
    {
        var primary = mission.PrimaryMission;
        var actionType = primary.ActionType;
        var canUseStudyRoom = state.TopicId.HasValue && mission.StudyRoomSuggestion != null;

        var focusMode = rhythm.RhythmStatus switch
        {
            "source_cleanup" => "source_cleanup",
            "repair_heavy" => canUseStudyRoom ? "study_room_lesson" : "repair_block",
            "review_heavy" => "review_sprint",
            "exam_heavy" => "exam_block",
            "comeback" => "quick_start",
            "thin_evidence" => "quick_start",
            _ when actionType == "open_study_room" && canUseStudyRoom => "study_room_lesson",
            _ when actionType is "repair_concept" or "repair_prerequisite" => "repair_block",
            _ when actionType == "review_due_concept" => "review_sprint",
            _ when actionType is "practice_exam_outcome" or "review_deneme_mistakes" => "exam_block",
            _ => "continue_plan"
        };

        var duration = focusMode switch
        {
            "quick_start" => "short",
            "source_cleanup" => "short",
            "repair_block" => "short",
            "review_sprint" => workload.ReviewLoad == "high" ? "medium" : "short",
            "study_room_lesson" => "medium",
            "exam_block" => "medium",
            _ => "medium"
        };

        var (entryPoint, route) = focusMode switch
        {
            "study_room_lesson" => ("open_study_room", "classroom"),
            "review_sprint" => ("review_due_concept", "learning"),
            "exam_block" => ("practice_exam_outcome", "central-exams"),
            "source_cleanup" => ("source_review", "sources"),
            "repair_block" => (primary.EntryPoint, primary.TargetRoute),
            "quick_start" => ("ask_tutor", "chat"),
            _ => (primary.EntryPoint, primary.TargetRoute)
        };

        if (focusMode == "study_room_lesson" && !canUseStudyRoom)
        {
            focusMode = "repair_block";
            entryPoint = primary.EntryPoint;
            route = primary.TargetRoute;
        }

        return new OrkaFocusPlanDto
        {
            FocusMode = focusMode,
            DurationBand = duration,
            EntryPoint = entryPoint,
            TargetRoute = route,
            Steps = StepsForFocus(focusMode, primary.Label),
            StopCondition = StopConditionForFocus(focusMode),
            ReasonCodes = rhythm.ReasonCodes
                .Concat(primary.ReasonCodes)
                .Where(NotBlank)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToArray()
        };
    }

    private static OrkaComebackPlanDto BuildComebackPlan(
        OrkaMissionControlDto mission,
        RhythmDecision rhythm,
        OrkaFocusPlanDto focusPlan,
        ActivityFacts activity)
    {
        if (!activity.InactivityDays.HasValue)
        {
            return new OrkaComebackPlanDto
            {
                ComebackStatus = "thin_evidence",
                FirstStep = "Kisa kontrol ile basla",
                SecondStep = "Sonuca gore tek sonraki adimi sec",
                AvoidToday = "Kanitsiz buyuk plan yapma.",
                ReasonCodes = ["thin_evidence"],
                UserSafeSummary = "Donus kaniti henuz sinirli; kisa baslangic onerilir."
            };
        }

        var status = activity.InactivityDays.Value >= ComebackNeededGap.TotalDays
            ? "needed"
            : activity.InactivityDays.Value >= ComebackSuggestedGap.TotalDays
                ? "suggested"
                : "not_needed";

        var reasons = status == "not_needed"
            ? Array.Empty<string>()
            : new[] { status == "needed" ? "comeback_needed" : "inactivity_gap" };

        return new OrkaComebackPlanDto
        {
            ComebackStatus = status,
            FirstStep = status == "not_needed"
                ? "Mission Control ana adimini uygula"
                : $"Kisa donus: {mission.PrimaryMission.Label}",
            SecondStep = status == "not_needed"
                ? "Checkpoint ile kapat"
                : focusPlan.FocusMode == "review_sprint" ? "Bir mini tekrar daha sec" : "Kisa checkpoint ile kapat",
            AvoidToday = status == "not_needed"
                ? "Ayni anda gereksiz ek is acma."
                : "Ayni anda tum review/repair kuyrugunu bitirmeye calisma.",
            ReasonCodes = reasons,
            UserSafeSummary = status == "not_needed"
                ? "Donus plani gerekmiyor; normal ritim yeterli."
                : "Donus plani sadece pratik calisma temposunu kucultur."
        };
    }

    private static IReadOnlyList<OrkaStudyCoachWarningDto> BuildWarnings(
        OrkaLearningStateDto state,
        OrkaMissionControlDto mission,
        RhythmDecision rhythm,
        OrkaStudyLoadDto workload,
        OrkaFocusPlanDto focusPlan,
        ActivityFacts activity)
    {
        var warnings = new List<OrkaStudyCoachWarningDto>();

        warnings.AddRange(mission.UrgentWarnings.Select(w => new OrkaStudyCoachWarningDto
        {
            WarningCode = w.WarningCode,
            Severity = w.Severity,
            Label = w.Label,
            TargetRoute = w.TargetRoute,
            ReasonCodes = w.ReasonCodes
        }));

        if (state.SignalSummary.EvidenceCount == 0 || rhythm.RhythmStatus == "thin_evidence")
        {
            warnings.Add(Warning("thin_evidence", "info", "Ogrenme kaniti az; kisa kontrol en guvenli baslangic.", "chat", ["thin_evidence"]));
        }

        if (workload.OverallLoad == "high" || workload.LoadScore >= 8)
        {
            warnings.Add(Warning("overload_risk", "warning", "Birden fazla yuk var; bugun kapsami sinirla.", "dashboard", ["workload_high", "overload_risk"]));
        }

        if (activity.InactivityDays.HasValue &&
            activity.InactivityDays.Value >= (int)ComebackSuggestedGap.TotalDays)
        {
            warnings.Add(Warning("inactivity_gap", "info", "Aradan zaman gecmis; donus adimini kucuk tut.", "dashboard", ["inactivity_gap"]));
        }

        if (focusPlan.FocusMode == "study_room_lesson" && !state.TopicId.HasValue)
        {
            warnings.Add(Warning("missing_topic_context", "warning", "Study Room icin guvenli konu baglami gerekir.", "dashboard", ["missing_topic_context"]));
        }

        if (mission.PrimaryMission.ActionType is "repair_concept" or "repair_prerequisite" &&
            rhythm.RhythmStatus is "normal" or "light")
        {
            warnings.Add(Warning("mission_mismatch", "warning", "Mission repair derken ritim normal kaldi; tekrar kontrol edilmeli.", "dashboard", ["mission_mismatch"]));
        }

        if (state.SourceWikiIntelligenceProfile?.CanClaimSourceGrounded == false &&
            state.SourceWikiIntelligenceProfile.Warnings.Any(w => w.Contains("source_grounded", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add(Warning("source_grounding_blocked", "warning", "Kaynak kaniti sinirli; source-grounded iddia kurulmaz.", "sources", ["source_evidence_limited"]));
        }

        return warnings
            .GroupBy(w => w.WarningCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(10)
            .ToArray();
    }

    private static IEnumerable<OrkaStudyCoachActionDto> BuildActions(
        OrkaMissionControlDto mission,
        OrkaFocusPlanDto focusPlan)
    {
        yield return ToStudyAction(mission.PrimaryMission, focusPlan.DurationBand);

        foreach (var action in mission.SecondaryActions.Take(6))
        {
            yield return ToStudyAction(action, DurationForAction(action.ActionType));
        }

        if (mission.StudyRoomSuggestion != null)
        {
            yield return ToStudyAction(mission.StudyRoomSuggestion, "medium");
        }
    }

    private static OrkaStudyCoachActionDto ToStudyAction(OrkaTodayMissionDto action, string durationBand) => new()
    {
        ActionType = action.ActionType,
        Label = action.Label,
        Reason = action.Reason,
        Priority = action.Priority,
        EntryPoint = action.EntryPoint,
        TargetRoute = action.TargetRoute,
        DurationBand = durationBand,
        TopicId = action.TopicId,
        ConceptKey = action.ConceptKey,
        ReasonCodes = action.ReasonCodes
    };

    private static OrkaStudyCoachActionDto ToStudyAction(OrkaMissionActionDto action, string durationBand) => new()
    {
        ActionType = action.ActionType,
        Label = action.Label,
        Reason = action.Reason,
        Priority = action.Priority,
        EntryPoint = action.EntryPoint,
        TargetRoute = action.TargetRoute,
        DurationBand = durationBand,
        TopicId = action.TopicId,
        ConceptKey = action.ConceptKey,
        ReasonCodes = action.ReasonCodes
    };

    private static IReadOnlyList<string> StepsForFocus(string focusMode, string primaryLabel) => focusMode switch
    {
        "repair_block" =>
        [
            primaryLabel,
            "Tek ornek uzerinden hatayi ayir",
            "Kisa checkpoint ile kapat"
        ],
        "study_room_lesson" =>
        [
            "Study Room'u konu baglamiyla ac",
            "Hoca anlatimini tek hedefe sinirla",
            "Ders sonunda kisa checkpoint yap"
        ],
        "review_sprint" =>
        [
            "Due review kuyruğunu ac",
            "Once en eski tekrar kalemini bitir",
            "Yanlis cikarsa repair'e don"
        ],
        "exam_block" =>
        [
            "Zayif outcome veya deneme hata kumesini ac",
            "Kisa pratik coz",
            "Sonucu review/repair sinyaline bagla"
        ],
        "source_cleanup" =>
        [
            "Kaynak/citation uyarisini ac",
            "Stale veya insufficient kaniti kontrol et",
            "Kaynakli calismaya gecmeden once uyariyi kapat"
        ],
        "quick_start" =>
        [
            "Kisa kontrol veya Tutor kontrolu ac",
            "Tek cevapla kanit biriktir",
            "Sonuca gore sonraki adimi sec"
        ],
        _ =>
        [
            primaryLabel,
            "Kisa checkpoint ile ilerlemeyi kontrol et"
        ]
    };

    private static string StopConditionForFocus(string focusMode) => focusMode switch
    {
        "repair_block" => "after one repair",
        "study_room_lesson" => "after one study room lesson",
        "review_sprint" => "after due review queue",
        "exam_block" => "after one checkpoint",
        "source_cleanup" => "after source warning resolved",
        "quick_start" => "after short check",
        _ => "after checkpoint"
    };

    private static string BuildSummary(RhythmDecision rhythm, OrkaStudyLoadDto workload, OrkaComebackPlanDto comeback)
    {
        var comebackText = comeback.ComebackStatus is "needed" or "suggested"
            ? " Donus icin adimi kucuk tut."
            : string.Empty;
        return $"Bugunku ritim {rhythm.RhythmStatus}; tempo {rhythm.RecommendedPace}; toplam yuk {workload.OverallLoad}.{comebackText}";
    }

    private static string ResolveNewLearningLoad(
        OrkaLearningStateDto state,
        OrkaMissionControlDto mission,
        AdaptiveLearningRhythmDto rhythm)
    {
        if (!string.IsNullOrWhiteSpace(rhythm.NewLearningLoad))
        {
            return rhythm.NewLearningLoad;
        }

        if (mission.PrimaryMission.ActionType == "continue_plan" &&
            mission.ReviewLoad == "none" &&
            mission.RepairLoad == "none" &&
            mission.SourceWikiLoad == "none")
        {
            return "medium";
        }

        return state.SignalSummary.HasRealLearningData ? "light" : "none";
    }

    private static OrkaStudyCoachWarningDto Warning(
        string code,
        string severity,
        string label,
        string route,
        IReadOnlyList<string> reasonCodes) => new()
        {
            WarningCode = code,
            Severity = severity,
            Label = label,
            TargetRoute = route,
            ReasonCodes = reasonCodes
        };

    private static string DurationForAction(string actionType) => actionType switch
    {
        "review_due_concept" or "source_review" or "citation_review" or "take_checkpoint_quiz" => "short",
        "open_study_room" or "practice_exam_outcome" or "review_deneme_mistakes" => "medium",
        "repair_concept" or "repair_prerequisite" => "short",
        _ => "short"
    };

    private static bool HasReason(OrkaLearningStateDto state, OrkaMissionControlDto mission, string reasonCode) =>
        state.ReasonCodes.Contains(reasonCode, StringComparer.OrdinalIgnoreCase) ||
        mission.ReasonCodes.Contains(reasonCode, StringComparer.OrdinalIgnoreCase) ||
        mission.PrimaryMission.ReasonCodes.Contains(reasonCode, StringComparer.OrdinalIgnoreCase) ||
        mission.SecondaryActions.Any(a => a.ReasonCodes.Contains(reasonCode, StringComparer.OrdinalIgnoreCase));

    private static int LoadScore(string? load) => load switch
    {
        "urgent" or "high" or "heavy" => 3,
        "medium" or "normal" => 2,
        "light" => 1,
        _ => 0
    };

    private static string NormalizeLoad(string? load) => load switch
    {
        "urgent" or "heavy" => "high",
        "high" or "medium" or "normal" or "light" or "none" => load,
        _ => "none"
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

    private static int ActionRank(string? actionType) => actionType switch
    {
        "source_review" or "citation_review" => 0,
        "repair_concept" or "repair_prerequisite" => 1,
        "review_due_concept" => 2,
        "practice_exam_outcome" or "review_deneme_mistakes" => 3,
        "open_study_room" => 4,
        "take_checkpoint_quiz" => 5,
        _ => 10
    };

    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);

    private sealed record ActivityFacts(DateTime? LastActivityAt, int? InactivityDays);

    private sealed record RhythmDecision(
        string RhythmStatus,
        string RecommendedPace,
        string TodayPlan,
        string WeeklyPlan,
        IReadOnlyList<string> ReasonCodes);
}
