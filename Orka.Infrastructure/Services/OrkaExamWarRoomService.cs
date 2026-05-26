using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public sealed class OrkaExamWarRoomService : IOrkaExamWarRoomService
{
    private readonly IExamLearningProfileService _examLearningProfile;
    private readonly IExamFrameworkService _examFramework;
    private readonly IOrkaLearningStateService _orkaState;
    private readonly IOrkaMissionControlService _missionControl;
    private readonly IOrkaStudyCoachService _studyCoach;

    public OrkaExamWarRoomService(
        IExamLearningProfileService examLearningProfile,
        IExamFrameworkService examFramework,
        IOrkaLearningStateService orkaState,
        IOrkaMissionControlService missionControl,
        IOrkaStudyCoachService studyCoach)
    {
        _examLearningProfile = examLearningProfile;
        _examFramework = examFramework;
        _orkaState = orkaState;
        _missionControl = missionControl;
        _studyCoach = studyCoach;
    }

    public async Task<OrkaExamWarRoomDto?> BuildWarRoomAsync(
        Guid userId,
        string examCode,
        string? variantCode = null,
        Guid? examTopicId = null,
        Guid? examOutcomeId = null,
        CancellationToken ct = default)
    {
        var normalizedExamCode = NormalizeCode(examCode);
        if (string.IsNullOrWhiteSpace(normalizedExamCode))
        {
            return null;
        }

        var profile = await _examLearningProfile.BuildProfileAsync(
            userId,
            normalizedExamCode,
            variantCode,
            examTopicId,
            examOutcomeId,
            ct);
        if (profile is null)
        {
            return null;
        }

        var tree = await _examFramework.CreateSystemSkeletonAsync(normalizedExamCode, ct);
        var outcomeMap = tree is null
            ? new Dictionary<string, OutcomePath>(StringComparer.OrdinalIgnoreCase)
            : Flatten(tree, variantCode)
                .GroupBy(p => p.Outcome.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var state = await _orkaState.BuildStateAsync(userId, topicId: null, sessionId: null, normalizedExamCode, variantCode, ct);
        OrkaMissionControlDto? mission = null;
        OrkaStudyCoachDto? coach = null;
        if (state is not null)
        {
            mission = await _missionControl.BuildFromStateAsync(userId, state, ct);
            coach = await _studyCoach.BuildFromMissionControlAsync(userId, state, mission, ct);
        }

        var outcomes = profile.Outcomes
            .Select(o => ToWarRoomOutcome(o))
            .ToArray();
        var weakOutcomes = outcomes
            .Where(o => o.ReadinessStatus is "weak" or "prerequisite_gap" or "watch")
            .OrderByDescending(o => PriorityScore(o.ReviewPriority))
            .ThenByDescending(o => o.DenemeMistakeCount)
            .Take(8)
            .ToArray();
        var dueOutcomes = outcomes
            .Where(o => o.ReadinessStatus == "due_for_review")
            .OrderByDescending(o => PriorityScore(o.ReviewPriority))
            .Take(8)
            .ToArray();
        var stableOutcomes = outcomes
            .Where(o => o.ReadinessStatus == "stable")
            .OrderByDescending(o => o.CorrectCount)
            .Take(8)
            .ToArray();
        var practice = profile.PracticeReadiness
            .Select(ToPracticePlan)
            .OrderByDescending(p => PriorityScore(p.Priority))
            .ThenBy(p => p.QuestionType)
            .ToArray();
        var denemeClusters = outcomes
            .Where(o => o.DenemeMistakeCount >= 2 || HasReason(o.ReasonCodes, "deneme_mistake_cluster"))
            .OrderByDescending(o => o.DenemeMistakeCount)
            .Take(6)
            .Select(o => new ExamWarRoomDenemeInsightDto
            {
                OutcomeCode = o.OutcomeCode,
                TopicCode = o.TopicCode,
                Label = o.Label,
                MistakeCount = o.DenemeMistakeCount,
                Priority = "urgent",
                RecommendedAction = "review_deneme_mistakes",
                ReasonCodes = ["deneme_mistake_cluster"]
            })
            .ToArray();
        var actions = BuildActions(profile, practice)
            .OrderByDescending(a => PriorityScore(a.Priority))
            .ThenBy(a => ActionRank(a.ActionType))
            .Take(10)
            .ToArray();
        var primary = SelectPrimaryAction(actions, profile);
        var warnings = BuildWarnings(profile);
        var conflicts = BuildConflicts(profile, primary, state, mission, coach);
        var tutorHandoffs = actions
            .Where(a => a.ActionType is "repair_exam_outcome" or "run_exam_diagnostic" or "review_deneme_mistakes" or "review_due_outcome")
            .Select(a => CloneAction(a, a.ActionType, "ask_tutor", "chat"))
            .Take(4)
            .ToArray();
        var studyRoomHandoffs = state?.TopicId.HasValue == true
            ? actions
                .Where(a => a.ActionType is "repair_exam_outcome" or "review_deneme_mistakes" or "review_due_outcome")
                .Select(a => CloneAction(a, "open_study_room", "open_study_room", "classroom", ["study_room_available"]))
                .Take(3)
                .ToArray()
            : Array.Empty<ExamWarRoomActionDto>();

        var reasonCodes = profile.ReasonCodes
            .Concat(primary.ReasonCodes)
            .Concat(warnings.SelectMany(w => w.ReasonCodes))
            .Concat(conflicts.SelectMany(w => w.ReasonCodes))
            .Concat(studyRoomHandoffs.SelectMany(a => a.ReasonCodes))
            .Where(NotBlank)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToArray();

        return new OrkaExamWarRoomDto
        {
            ActiveExam = new ExamWarRoomActiveExamDto
            {
                ExamCode = profile.ExamCode,
                VariantCode = profile.VariantCode,
                DisplayName = profile.DisplayName,
                VerificationStatus = profile.VerificationStatus,
                CanClaimOfficial = profile.CanClaimOfficial,
                UserSafeVerificationLabel = profile.UserSafeVerificationLabel
            },
            Variant = profile.VariantCode,
            ReadinessStatus = ResolveReadinessStatus(profile, primary, warnings),
            WeakSubjects = BuildSubjects(outcomes, outcomeMap, statusFilter: static o => o.ReadinessStatus is "weak" or "prerequisite_gap" or "watch"),
            WeakTopics = BuildTopics(outcomes, statusFilter: static o => o.ReadinessStatus is "weak" or "prerequisite_gap" or "watch"),
            WeakOutcomes = weakOutcomes,
            DueOutcomes = dueOutcomes,
            StableOutcomes = stableOutcomes,
            WeakQuestionTypes = practice.Where(p => p.ReadinessStatus == "weak").Take(6).ToArray(),
            DenemeMistakeClusters = denemeClusters,
            PracticeReadiness = practice.Take(10).ToArray(),
            TodayExamMission = primary,
            WeeklyExamPlan = actions.Take(5).ToArray(),
            RecommendedPracticeQueue = actions.Where(a => a.TargetRoute == "central-exams").Take(6).ToArray(),
            TutorRepairHandoffs = tutorHandoffs,
            StudyRoomHandoffs = studyRoomHandoffs,
            SourceWikiWarnings = warnings.Where(w => IsSourceWarning(w.WarningCode)).ToArray(),
            CurriculumCoverageWarnings = warnings.Where(w => !IsSourceWarning(w.WarningCode)).ToArray(),
            ConflictWarnings = conflicts,
            ReasonCodes = reasonCodes,
            UserSafeSummary = BuildSummary(primary, profile, warnings, conflicts),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private static IReadOnlyList<ExamWarRoomActionDto> BuildActions(
        ExamLearningProfileDto profile,
        IReadOnlyList<ExamWarRoomPracticePlanDto> practice)
    {
        var actions = profile.NextActions
            .Select(ToWarRoomAction)
            .ToList();

        foreach (var item in practice.Where(p => p.ReadinessStatus == "weak"))
        {
            actions.Add(new ExamWarRoomActionDto
            {
                ActionType = item.RecommendedAction == "run_diagnostic" ? "run_exam_diagnostic" : "practice_question_type",
                Label = $"{item.QuestionType}: soru tipi pratigi",
                Reason = item.BlankCount >= 2
                    ? "Bu soru tipinde bos/atlanan cevap tekrar etti; tani veya on kosul kontrolu gerekir."
                    : "Bu soru tipinde zayif cevap sinyali birikti.",
                Priority = item.Priority,
                EntryPoint = "practice_question_type",
                TargetRoute = "central-exams",
                QuestionType = item.QuestionType,
                ReasonCodes = item.ReasonCodes
            });
        }

        foreach (var outcome in profile.Outcomes
            .Where(o => o.ReadinessStatus == "stable")
            .Take(3))
        {
            actions.Add(new ExamWarRoomActionDto
            {
                ActionType = "continue_exam_plan",
                Label = $"{outcome.Label}: plana devam",
                Reason = "Tekrarlanan basari bu kazanimin dusuk oncelikle ilerleyebilecegini gosterir; bu basari garantisi degildir.",
                Priority = "low",
                EntryPoint = "continue_exam_plan",
                TargetRoute = "central-exams",
                OutcomeCode = outcome.OutcomeCode,
                TopicCode = outcome.TopicCode,
                QuestionType = outcome.QuestionTypes.FirstOrDefault(),
                ReasonCodes = outcome.ReasonCodes.Where(NotBlank).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray(),
                ExamContext = new ExamLearningContextDto
                {
                    ExamCode = profile.ExamCode,
                    ExamOutcomeId = outcome.ExamOutcomeId,
                    OutcomeCode = outcome.OutcomeCode,
                    TopicCode = outcome.TopicCode
                }
            });
        }

        if (actions.Count == 0)
        {
            actions.Add(DefaultAction(profile));
        }

        return actions
            .GroupBy(a => $"{a.ActionType}:{a.OutcomeCode}:{a.QuestionType}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
    }

    private static ExamWarRoomActionDto SelectPrimaryAction(
        IReadOnlyList<ExamWarRoomActionDto> actions,
        ExamLearningProfileDto profile)
    {
        var ranked = actions
            .OrderByDescending(a => PriorityScore(a.Priority))
            .ThenBy(a => ActionRank(a.ActionType))
            .FirstOrDefault();
        if (ranked is not null)
        {
            return ranked;
        }

        return DefaultAction(profile);
    }

    private static ExamWarRoomActionDto DefaultAction(ExamLearningProfileDto profile) => new()
    {
        ActionType = profile.HasEnoughEvidence ? "continue_exam_plan" : "run_exam_diagnostic",
        Label = profile.HasEnoughEvidence ? "Sinav planina devam et" : "Kisa sinav tani kontrolu",
        Reason = profile.HasEnoughEvidence
            ? "Belirgin zayif sinyal yok; sinav planina dikkatli devam edilebilir."
            : "Sinav kaniti henuz ince; once kisa tani/pratik gerekir.",
        Priority = "medium",
        EntryPoint = profile.HasEnoughEvidence ? "continue_exam_plan" : "run_exam_diagnostic",
        TargetRoute = "central-exams",
        ReasonCodes = profile.HasEnoughEvidence ? ["stable_recent_success"] : ["thin_evidence", "diagnostic_needed"],
        ExamContext = new ExamLearningContextDto { ExamCode = profile.ExamCode }
    };

    private static ExamWarRoomOutcomeDto ToWarRoomOutcome(ExamOutcomeReadinessDto outcome) => new()
    {
        ExamOutcomeId = outcome.ExamOutcomeId,
        OutcomeCode = outcome.OutcomeCode,
        Label = outcome.Label,
        TopicCode = outcome.TopicCode,
        TopicLabel = outcome.TopicLabel,
        ReadinessStatus = outcome.ReadinessStatus,
        ReviewPriority = outcome.ReviewPriority,
        RecommendedAction = NormalizeAction(outcome.RecommendedAction),
        AttemptCount = outcome.AttemptCount,
        CorrectCount = outcome.CorrectCount,
        WrongCount = outcome.WrongCount,
        BlankCount = outcome.BlankCount,
        DenemeMistakeCount = outcome.DenemeMistakeCount,
        PublishedQuestionCount = outcome.PublishedQuestionCount,
        CorrectnessRate = outcome.CorrectnessRate,
        QuestionCoverageStatus = outcome.QuestionCoverageStatus,
        SourceEvidenceStatus = outcome.SourceEvidenceStatus,
        QuestionTypes = outcome.QuestionTypes,
        ReasonCodes = outcome.ReasonCodes,
        UserSafeSummary = outcome.UserSafeSummary
    };

    private static ExamWarRoomPracticePlanDto ToPracticePlan(ExamPracticeReadinessDto item) => new()
    {
        QuestionType = item.QuestionType,
        ReadinessStatus = item.ReadinessStatus,
        RecommendedAction = NormalizeAction(item.RecommendedAction),
        PublishedQuestionCount = item.PublishedQuestionCount,
        AttemptCount = item.AttemptCount,
        CorrectCount = item.CorrectCount,
        WrongCount = item.WrongCount,
        BlankCount = item.BlankCount,
        Priority = ResolvePracticePriority(item),
        ReasonCodes = item.ReasonCodes
    };

    private static ExamWarRoomActionDto ToWarRoomAction(ExamNextActionDto action)
    {
        var normalized = NormalizeAction(action.ActionType);
        return new ExamWarRoomActionDto
        {
            ActionType = normalized,
            Label = SafeText(action.Label, DefaultLabel(normalized)),
            Reason = SafeText(action.Reason, "Sinav kanitindan turetilen guvenli sonraki adim."),
            Priority = NormalizePriority(action.Priority),
            EntryPoint = EntryPointForAction(normalized),
            TargetRoute = RouteForAction(normalized),
            OutcomeCode = SafeOptional(action.OutcomeCode),
            TopicCode = SafeOptional(action.TopicCode),
            QuestionType = SafeOptional(action.QuestionType),
            ExamContext = action.ExamContext,
            ReasonCodes = action.ReasonCodes.Where(NotBlank).Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray()
        };
    }

    private static ExamWarRoomActionDto CloneAction(
        ExamWarRoomActionDto action,
        string actionType,
        string entryPoint,
        string route,
        IReadOnlyList<string>? extraReasons = null) => new()
    {
        ActionType = actionType,
        Label = action.Label,
        Reason = action.Reason,
        Priority = action.Priority,
        EntryPoint = entryPoint,
        TargetRoute = route,
        OutcomeCode = action.OutcomeCode,
        TopicCode = action.TopicCode,
        QuestionType = action.QuestionType,
        ExamContext = action.ExamContext,
        ReasonCodes = action.ReasonCodes
            .Concat(extraReasons ?? Array.Empty<string>())
            .Where(NotBlank)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray()
    };

    private static IReadOnlyList<ExamWarRoomSubjectDto> BuildSubjects(
        IReadOnlyList<ExamWarRoomOutcomeDto> outcomes,
        IReadOnlyDictionary<string, OutcomePath> outcomeMap,
        Func<ExamWarRoomOutcomeDto, bool> statusFilter)
    {
        return outcomes
            .Where(statusFilter)
            .GroupBy(o => outcomeMap.TryGetValue(o.OutcomeCode, out var path) ? path.Subject.Code : o.TopicCode, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var path = outcomeMap.TryGetValue(first.OutcomeCode, out var mapped) ? mapped : null;
                var items = g.ToArray();
                return new ExamWarRoomSubjectDto
                {
                    SubjectCode = g.Key,
                    Label = path?.Subject.Name ?? first.TopicLabel,
                    WeakOutcomeCount = items.Count(o => o.ReadinessStatus is "weak" or "prerequisite_gap" or "watch"),
                    DueOutcomeCount = items.Count(o => o.ReadinessStatus == "due_for_review"),
                    DenemeMistakeCount = items.Sum(o => o.DenemeMistakeCount),
                    Priority = HighestPriority(items.Select(o => o.ReviewPriority)),
                    ReasonCodes = items.SelectMany(o => o.ReasonCodes).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray()
                };
            })
            .OrderByDescending(s => PriorityScore(s.Priority))
            .ThenByDescending(s => s.DenemeMistakeCount)
            .Take(8)
            .ToArray();
    }

    private static IReadOnlyList<ExamWarRoomTopicDto> BuildTopics(
        IReadOnlyList<ExamWarRoomOutcomeDto> outcomes,
        Func<ExamWarRoomOutcomeDto, bool> statusFilter)
    {
        return outcomes
            .Where(statusFilter)
            .GroupBy(o => o.TopicCode, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var items = g.ToArray();
                return new ExamWarRoomTopicDto
                {
                    TopicCode = g.Key,
                    Label = items.First().TopicLabel,
                    WeakOutcomeCount = items.Count(o => o.ReadinessStatus is "weak" or "prerequisite_gap" or "watch"),
                    DueOutcomeCount = items.Count(o => o.ReadinessStatus == "due_for_review"),
                    DenemeMistakeCount = items.Sum(o => o.DenemeMistakeCount),
                    Priority = HighestPriority(items.Select(o => o.ReviewPriority)),
                    ReasonCodes = items.SelectMany(o => o.ReasonCodes).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray()
                };
            })
            .OrderByDescending(t => PriorityScore(t.Priority))
            .ThenByDescending(t => t.DenemeMistakeCount)
            .Take(8)
            .ToArray();
    }

    private static IReadOnlyList<ExamWarRoomWarningDto> BuildWarnings(ExamLearningProfileDto profile)
    {
        var warnings = new List<ExamWarRoomWarningDto>();

        foreach (var warning in profile.Warnings.Where(NotBlank))
        {
            warnings.Add(Warning(
                warning,
                warning is "source_unverified" or "source_evidence_limited" or "question_coverage_limited" ? "warning" : "info",
                WarningLabel(warning),
                RouteForWarning(warning),
                [warning]));
        }

        if (!profile.CanClaimOfficial)
        {
            warnings.Add(Warning(
                "official_claim_blocked",
                "warning",
                "Dogrulanmis metadata olmadan resmi uyum veya basari iddiasi kurulmaz.",
                "central-exams",
                ["source_unverified", "official_claim_blocked"]));
        }

        warnings.Add(Warning(
            "answer_key_guard",
            "info",
            "Cevap anahtari submit oncesi War Room kontratina girmez.",
            "central-exams",
            ["answer_key_guard"]));

        if (!profile.HasEnoughEvidence)
        {
            warnings.Add(Warning(
                "thin_exam_evidence",
                "info",
                "Sinav kaniti ince; tani/pratik onerisi kesin basari iddiasi degildir.",
                "central-exams",
                ["thin_evidence", "diagnostic_needed"]));
        }

        return warnings
            .GroupBy(w => w.WarningCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(w => SeverityScore(w.Severity))
            .Take(10)
            .ToArray();
    }

    private static IReadOnlyList<ExamWarRoomWarningDto> BuildConflicts(
        ExamLearningProfileDto profile,
        ExamWarRoomActionDto primary,
        OrkaLearningStateDto? state,
        OrkaMissionControlDto? mission,
        OrkaStudyCoachDto? coach)
    {
        var warnings = new List<ExamWarRoomWarningDto>();
        var primaryIsExamRepair = primary.ActionType is "repair_exam_outcome" or "review_deneme_mistakes" or "practice_question_type" or "run_exam_diagnostic";

        if (mission is not null &&
            primaryIsExamRepair &&
            mission.PrimaryMission.ActionType is "continue_plan")
        {
            warnings.Add(Warning(
                "exam_priority_conflict",
                "warning",
                "Exam War Room sinav repair/pratik derken Mission Control plana devam diyor; sinav uyarisi gorunur tutuldu.",
                "central-exams",
                ["exam_priority_conflict", "weak_outcome"]));
        }

        if (coach is not null &&
            primary.ActionType is "review_deneme_mistakes" or "repair_exam_outcome" &&
            coach.RhythmStatus is "normal" or "light")
        {
            warnings.Add(Warning(
                "exam_priority_conflict",
                "warning",
                "Sinav onceligi yuksek ama Study Coach ritmi hafif; sinav odagi warning olarak korunur.",
                "central-exams",
                ["exam_priority_conflict"]));
        }

        if (state?.SourceWikiIntelligenceProfile?.CanClaimSourceGrounded == false &&
            profile.Warnings.Any(w => w.Contains("source", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add(Warning(
                "source_unverified",
                "warning",
                "Kaynak/curriculum kaniti sinirli; source-backed veya resmi iddia kurulmaz.",
                "sources",
                ["source_unverified", "source_evidence_limited"]));
        }

        if (state is not null &&
            !state.TopicId.HasValue &&
            primary.ActionType is "open_study_room")
        {
            warnings.Add(Warning(
                "missing_topic_context",
                "warning",
                "Study Room icin guvenli konu/session baglami gerekir.",
                "central-exams",
                ["missing_topic_context"]));
        }

        return warnings
            .GroupBy(w => w.WarningCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
    }

    private static string ResolveReadinessStatus(
        ExamLearningProfileDto profile,
        ExamWarRoomActionDto primary,
        IReadOnlyList<ExamWarRoomWarningDto> warnings)
    {
        if (warnings.Any(w => w.WarningCode is "source_unverified" or "source_evidence_limited"))
        {
            return "verification_limited";
        }

        return primary.ActionType switch
        {
            "review_deneme_mistakes" => "needs_deneme_review",
            "repair_exam_outcome" => "needs_repair",
            "run_exam_diagnostic" => "diagnostic_needed",
            "review_due_outcome" => "review_due",
            "practice_question_type" => "practice_needed",
            "continue_exam_plan" when profile.HasEnoughEvidence => "ready_to_continue",
            _ => profile.HasEnoughEvidence ? "ready_to_continue" : "thin_exam_evidence"
        };
    }

    private static ExamWarRoomWarningDto Warning(
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
        ReasonCodes = reasonCodes.Where(NotBlank).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray()
    };

    private static string NormalizeAction(string? actionType) => actionType switch
    {
        "repair_outcome" => "repair_exam_outcome",
        "run_diagnostic" => "run_exam_diagnostic",
        "review_deneme_mistakes" => "review_deneme_mistakes",
        "review_due_outcome" => "review_due_outcome",
        "practice_question_type" => "practice_question_type",
        "source_review" => "source_review",
        "citation_review" => "citation_review",
        "create_flashcards" => "create_flashcards",
        "continue_exam_plan" => "continue_exam_plan",
        _ => string.IsNullOrWhiteSpace(actionType) ? "run_exam_diagnostic" : actionType
    };

    private static string EntryPointForAction(string? actionType) => actionType switch
    {
        "repair_exam_outcome" => "ask_tutor",
        "review_deneme_mistakes" => "review_deneme_mistakes",
        "practice_question_type" => "practice_question_type",
        "review_due_outcome" => "review_due_outcome",
        "run_exam_diagnostic" => "run_exam_diagnostic",
        "source_review" => "source_review",
        "citation_review" => "citation_review",
        "create_flashcards" => "create_flashcards",
        "continue_exam_plan" => "continue_exam_plan",
        _ => "run_exam_diagnostic"
    };

    private static string RouteForAction(string? actionType) => actionType switch
    {
        "repair_exam_outcome" => "chat",
        "source_review" or "citation_review" => "sources",
        "create_flashcards" => "learning",
        _ => "central-exams"
    };

    private static string RouteForWarning(string code) =>
        IsSourceWarning(code) ? "sources" : "central-exams";

    private static bool IsSourceWarning(string code) =>
        code.Contains("source", StringComparison.OrdinalIgnoreCase) ||
        code.Contains("curriculum", StringComparison.OrdinalIgnoreCase) ||
        code.Contains("official", StringComparison.OrdinalIgnoreCase) ||
        code.Contains("citation", StringComparison.OrdinalIgnoreCase);

    private static string WarningLabel(string code) => code switch
    {
        "source_unverified" => "Curriculum/source metadata dogrulanmadikca resmi uyum iddiasi kurulmaz.",
        "source_evidence_limited" => "Kaynak kaniti sinirli; source-backed iddia dikkat ister.",
        "question_coverage_limited" => "Soru kapsami sinirli; tani/pratik onceligi korunur.",
        "coverage_limited" => "Kapsam sinirli; sonuc garanti veya kesin seviye iddiasi degildir.",
        _ => code
    };

    private static string BuildSummary(
        ExamWarRoomActionDto primary,
        ExamLearningProfileDto profile,
        IReadOnlyList<ExamWarRoomWarningDto> warnings,
        IReadOnlyList<ExamWarRoomWarningDto> conflicts)
    {
        var warningNote = warnings.Any(w => w.Severity == "warning")
            ? " Kaynak/kapsam uyarisi var; resmi veya basari iddiasi kurulmaz."
            : string.Empty;
        var conflictNote = conflicts.Count > 0
            ? " Modul ayrismasi warning olarak gorunur."
            : string.Empty;
        var evidence = profile.HasEnoughEvidence ? "yeterli" : "ince";
        return $"{primary.Label}: {primary.Reason} Sinav kaniti {evidence}.{warningNote}{conflictNote}";
    }

    private static string ResolvePracticePriority(ExamPracticeReadinessDto item)
    {
        if (item.ReasonCodes.Contains("repeated_wrong", StringComparer.OrdinalIgnoreCase) ||
            item.ReasonCodes.Contains("repeated_blank", StringComparer.OrdinalIgnoreCase))
        {
            return "high";
        }

        return item.ReadinessStatus switch
        {
            "weak" => "high",
            "diagnostic_needed" or "coverage_limited" => "medium",
            "stable" => "low",
            _ => "normal"
        };
    }

    private static string HighestPriority(IEnumerable<string> priorities) =>
        priorities.OrderByDescending(PriorityScore).FirstOrDefault() ?? "normal";

    private static int ActionRank(string? actionType) => actionType switch
    {
        "source_review" or "citation_review" => 0,
        "review_deneme_mistakes" => 1,
        "repair_exam_outcome" => 2,
        "run_exam_diagnostic" => 3,
        "review_due_outcome" => 4,
        "practice_question_type" => 5,
        "create_flashcards" => 6,
        "continue_exam_plan" => 7,
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

    private static string NormalizePriority(string? priority) => priority switch
    {
        "urgent" or "high" or "medium" or "normal" or "low" => priority,
        _ => "normal"
    };

    private static string DefaultLabel(string actionType) => actionType switch
    {
        "repair_exam_outcome" => "Sinav kazanimi telafi et",
        "review_deneme_mistakes" => "Deneme hatalarini incele",
        "practice_question_type" => "Soru tipi pratigi yap",
        "review_due_outcome" => "Sinav kazanimi tekrar et",
        "source_review" => "Kaynak/kapsam kontrolu yap",
        "continue_exam_plan" => "Sinav planina devam et",
        _ => "Kisa sinav tani kontrolu"
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

    private static string? SafeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 96 ? trimmed : trimmed[..96];
    }

    private static string NormalizeCode(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static bool HasReason(IEnumerable<string> reasons, string code) =>
        reasons.Contains(code, StringComparer.OrdinalIgnoreCase);

    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);

    private static IReadOnlyList<OutcomePath> Flatten(ExamDefinitionDto tree, string? variantCode)
    {
        var normalizedVariant = string.IsNullOrWhiteSpace(variantCode) ? null : variantCode.Trim();
        var paths = new List<OutcomePath>();
        foreach (var variant in tree.Variants.Where(v => normalizedVariant is null || string.Equals(v.Code, normalizedVariant, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var section in variant.Sections)
            foreach (var subject in section.Subjects)
            foreach (var topic in FlattenTopic(subject.Topics))
            foreach (var outcome in topic.Outcomes)
            {
                paths.Add(new OutcomePath(variant, section, subject, topic, outcome));
            }
        }

        return paths;
    }

    private static IEnumerable<ExamTopicDto> FlattenTopic(IEnumerable<ExamTopicDto> topics)
    {
        foreach (var topic in topics)
        {
            yield return topic;
            foreach (var child in FlattenTopic(topic.Children))
            {
                yield return child;
            }
        }
    }

    private sealed record OutcomePath(
        ExamVariantDto Variant,
        ExamSectionDto Section,
        ExamSubjectDto Subject,
        ExamTopicDto Topic,
        ExamOutcomeDto Outcome);
}
