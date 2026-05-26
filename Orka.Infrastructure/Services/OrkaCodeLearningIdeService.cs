using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class OrkaCodeLearningIdeService : IOrkaCodeLearningIdeService
{
    private static readonly string[] SupportedLearningLanguages =
    [
        "csharp", "python", "javascript", "typescript", "java", "go", "rust", "cpp", "c",
        "kotlin", "php", "ruby", "swift", "r", "scala"
    ];

    private static readonly string[] BlockedLanguageFamilies =
    [
        "powershell", "pwsh", "cmd", "shell", "sh", "zsh", "fish"
    ];

    private static readonly string[] BlockedMarkers =
    [
        "rawPrompt", "hiddenPrompt", "systemPrompt", "developerPrompt", "rawProviderPayload",
        "rawSourceChunk", "rawToolPayload", "debugTrace", "localPath", "apiKey", "secret",
        "token", "answerKey", "correctAnswer", "stackTrace", "ownerId", "userId", "rawTranscript"
    ];

    private static readonly string[] CodeSignalTypes =
    [
        LearningSignalTypes.IdeRunCompleted,
        LearningSignalTypes.IdeCompileError,
        LearningSignalTypes.IdeRuntimeError,
        LearningSignalTypes.IdeExecutionTimeout,
        LearningSignalTypes.IdeProviderUnavailable,
        LearningSignalTypes.IdeTestFailure,
        LearningSignalTypes.IdeBlankAttempt
    ];

    private readonly OrkaDbContext _db;
    private readonly IOrkaLearningStateService _orkaState;
    private readonly IOrkaMissionControlService _missionControl;
    private readonly IOrkaStudyCoachService _studyCoach;
    private readonly IOrkaNotebookStudioProService _notebookStudioPro;
    private readonly IToolCapabilityService _toolCapability;

    public OrkaCodeLearningIdeService(
        OrkaDbContext db,
        IOrkaLearningStateService orkaState,
        IOrkaMissionControlService missionControl,
        IOrkaStudyCoachService studyCoach,
        IOrkaNotebookStudioProService notebookStudioPro,
        IToolCapabilityService toolCapability)
    {
        _db = db;
        _orkaState = orkaState;
        _missionControl = missionControl;
        _studyCoach = studyCoach;
        _notebookStudioPro = notebookStudioPro;
        _toolCapability = toolCapability;
    }

    public async Task<OrkaCodeLearningIdeDto?> BuildIdeAsync(
        Guid userId,
        Guid? topicId = null,
        Guid? sessionId = null,
        string? language = null,
        string? exerciseId = null,
        string? mode = null,
        CancellationToken ct = default)
    {
        var context = await ResolveContextAsync(userId, topicId, sessionId, ct);
        if (context == null) return null;

        var requestedLanguage = NormalizeLanguage(language);
        var state = await _orkaState.BuildStateAsync(userId, context.TopicId, context.SessionId, examCode: "KPSS", variantCode: null, ct);
        if (state == null) return null;

        var mission = await _missionControl.BuildFromStateAsync(userId, state, ct);
        var coach = await _studyCoach.BuildFromMissionControlAsync(userId, state, mission, ct);
        var notebook = await _notebookStudioPro.BuildProAsync(
            userId,
            context.TopicId,
            context.SessionId,
            sourceId: null,
            wikiPageId: null,
            examCode: "KPSS",
            variantCode: null,
            packType: null,
            ct);

        var facts = await LoadCodeFactsAsync(userId, context, requestedLanguage, ct);
        var activeLanguage = requestedLanguage ?? facts.ActiveLanguage ?? "csharp";
        var runtime = BuildRuntimeReadiness(activeLanguage);
        var decision = DecideMode(runtime, facts, state, mission, coach, mode);
        var actions = BuildActions(context, activeLanguage, decision, facts, state, mission, notebook)
            .GroupBy(a => $"{a.ActionType}:{a.ConceptKey}:{a.Language}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(a => PriorityScore(a.Priority))
            .ThenBy(a => ActionRank(a.ActionType))
            .Take(10)
            .ToArray();
        var warnings = BuildWarnings(runtime, facts, state, mission, coach, decision)
            .GroupBy(w => w.WarningCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(10)
            .ToArray();
        var missionWarnings = mission.UrgentWarnings
            .Select(w => new CodeLearningWarningDto
            {
                WarningCode = SafeKey(w.WarningCode, "mission_warning"),
                Severity = NormalizeSeverity(w.Severity),
                Label = SafeText(w.Label, "Mission Control uyarisi."),
                TargetRoute = SafeKey(w.TargetRoute, "dashboard"),
                ReasonCodes = SafeReasonCodes(w.ReasonCodes)
            })
            .Take(6)
            .ToArray();
        var activeSkill = ResolveActiveSkill(facts, state, mission);
        var reasonCodes = runtime.ReasonCodes
            .Concat(decision.ReasonCodes)
            .Concat(facts.ReasonCodes)
            .Concat(actions.SelectMany(a => a.ReasonCodes))
            .Concat(warnings.SelectMany(w => w.ReasonCodes))
            .Concat(missionWarnings.SelectMany(w => w.ReasonCodes))
            .Concat(coach.ReasonCodes)
            .Where(NotBlank)
            .Select(r => SafeKey(r, "reason"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToArray();

        return new OrkaCodeLearningIdeDto
        {
            TopicId = context.TopicId,
            SessionId = context.SessionId,
            ReadinessStatus = decision.ReadinessStatus,
            Mode = decision.Mode,
            ActiveLanguage = SafeKey(activeLanguage, "csharp"),
            ActiveTopic = await ResolveTopicTitleAsync(userId, context.TopicId, ct),
            ActiveSkill = SafeOptional(activeSkill),
            RuntimeReadiness = runtime,
            Session = BuildSessionDto(facts),
            ActiveExercise = BuildExerciseDto(exerciseId, activeSkill, decision),
            LastAttemptSummary = BuildLastAttemptDto(facts, activeLanguage),
            RepeatedErrorSummary = BuildErrorSummary(facts, decision),
            CheckpointStatus = decision.CheckpointStatus,
            RepairStatus = decision.RepairStatus,
            RecommendedActions = actions,
            TutorHandoffs = ToHandoffs(actions.Where(a => a.ActionType is "ask_tutor" or "repair_syntax_error" or "repair_runtime_error" or "repair_test_failure"), "ask_tutor", "chat").Take(4).ToArray(),
            QuizHandoffs = ToHandoffs(actions.Where(a => a.ActionType is "take_code_checkpoint" or "start_code_diagnostic"), "take_code_checkpoint", "quiz").Take(4).ToArray(),
            ReviewHandoffs = ToHandoffs(actions.Where(a => a.ActionType == "review_code_concept"), "review_code_concept", "learning").Take(4).ToArray(),
            WikiHandoffs = ToHandoffs(actions.Where(a => a.ActionType == "create_code_note"), "create_code_note", "wiki").Take(4).ToArray(),
            NotebookHandoffs = ToHandoffs(actions.Where(a => a.ActionType is "create_code_repair_pack" or "take_code_checkpoint"), "create_code_repair_pack", "notebook-studio").Take(4).ToArray(),
            MissionControlWarnings = missionWarnings,
            RuntimeWarnings = warnings,
            ReasonCodes = reasonCodes,
            UserSafeSummary = BuildSummary(decision, facts, runtime),
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<CodeIdeContext?> ResolveContextAsync(Guid userId, Guid? topicId, Guid? sessionId, CancellationToken ct)
    {
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

        return new CodeIdeContext(topicId, sessionId);
    }

    private async Task<CodeFacts> LoadCodeFactsAsync(Guid userId, CodeIdeContext context, string? requestedLanguage, CancellationToken ct)
    {
        var signals = await _db.LearningSignals.AsNoTracking()
            .Where(s => s.UserId == userId &&
                        (!context.TopicId.HasValue || s.TopicId == context.TopicId.Value) &&
                        (!context.SessionId.HasValue || s.SessionId == context.SessionId.Value) &&
                        CodeSignalTypes.Contains(s.SignalType))
            .OrderByDescending(s => s.CreatedAt)
            .Take(40)
            .ToListAsync(ct);

        var filtered = string.IsNullOrWhiteSpace(requestedLanguage)
            ? signals
            : signals.Where(s => string.IsNullOrWhiteSpace(s.SkillTag) ||
                                 string.Equals(NormalizeLanguage(s.SkillTag), requestedLanguage, StringComparison.OrdinalIgnoreCase))
                .ToList();
        if (filtered.Count == 0 && signals.Count > 0)
        {
            filtered = signals;
        }

        var compile = CountSignals(filtered, LearningSignalTypes.IdeCompileError, "compile", "syntax");
        var runtime = filtered.Count(s =>
            string.Equals(s.SignalType, LearningSignalTypes.IdeRuntimeError, StringComparison.OrdinalIgnoreCase) ||
            PayloadContains(s.PayloadJson, "runtime_error") ||
            PayloadContains(s.PayloadJson, "runtimeError"));
        var timeout = CountSignals(filtered, LearningSignalTypes.IdeExecutionTimeout, "timeout", "timeout");
        var provider = CountSignals(filtered, LearningSignalTypes.IdeProviderUnavailable, "provider_missing", "provider");
        var test = filtered.Count(s =>
            string.Equals(s.SignalType, LearningSignalTypes.IdeTestFailure, StringComparison.OrdinalIgnoreCase) ||
            PayloadContains(s.PayloadJson, "test_failure"));
        var blank = filtered.Count(s =>
            string.Equals(s.SignalType, LearningSignalTypes.IdeBlankAttempt, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ExtractPayload(s.PayloadJson, "phase"), "blank", StringComparison.OrdinalIgnoreCase) ||
            PayloadContains(s.PayloadJson, "no_attempt") ||
            PayloadContains(s.PayloadJson, "blank_attempt"));
        var success = filtered.Count(s => string.Equals(s.SignalType, LearningSignalTypes.IdeRunCompleted, StringComparison.OrdinalIgnoreCase) || s.IsPositive == true);
        var latest = filtered.FirstOrDefault();
        var activeLanguage = requestedLanguage ?? NormalizeLanguage(latest?.SkillTag);
        var dominant = DominantError(compile, runtime + timeout, test, blank);
        var reasons = new List<string>();
        if (filtered.Count == 0) reasons.Add("thin_evidence");
        if (compile >= 2) reasons.Add("repeated_syntax_error");
        if (runtime + timeout >= 2) reasons.Add("repeated_runtime_error");
        if (test >= 2) reasons.Add("repeated_test_failure");
        if (blank >= 2) reasons.Add("repeated_blank");
        if (success >= 2 && compile + runtime + timeout + test + blank == 0) reasons.Add("stable_recent_success");

        return new CodeFacts(
            Signals: filtered,
            ActiveLanguage: activeLanguage,
            CompileErrorCount: compile,
            RuntimeErrorCount: runtime,
            TimeoutCount: timeout,
            ProviderUnavailableCount: provider,
            TestFailureCount: test,
            BlankAttemptCount: blank,
            SuccessCount: success,
            DominantError: dominant,
            LastSignal: latest,
            ReasonCodes: reasons);
    }

    private CodeLearningRuntimeReadinessDto BuildRuntimeReadiness(string language)
    {
        var capability = _toolCapability.GetCapability("ide_execution", includeInternal: true);
        var languageKey = SafeKey(language, "csharp");
        var languageBlocked = IsBlockedLanguage(languageKey);
        var supported = SupportedLearningLanguages.Contains(languageKey, StringComparer.OrdinalIgnoreCase);
        var enabled = capability?.Status == "Enabled" && !languageBlocked && supported;
        var warnings = new List<string>();
        var reasons = new List<string>();

        if (capability == null || capability.Status != "Enabled")
        {
            warnings.Add("code_runtime_blocked");
            reasons.Add("runtime_blocked");
        }
        else if (languageBlocked)
        {
            warnings.Add("code_runtime_blocked");
            warnings.Add("tool_permission_limited");
            reasons.Add("runtime_blocked");
            reasons.Add("tool_permission_limited");
        }
        else if (!supported)
        {
            warnings.Add("code_runtime_limited");
            reasons.Add("runtime_blocked");
            reasons.Add("unsupported_language");
        }
        else
        {
            warnings.Add("sandbox_required");
            reasons.Add("tool_permission_limited");
        }

        return new CodeLearningRuntimeReadinessDto
        {
            Status = enabled ? "ready" : languageBlocked || !supported ? "blocked" : "limited",
            ToolId = "ide_execution",
            Decision = SafeText(capability?.Decision, "CORE_ENABLED_BEHIND_AUTH_AND_SANDBOX", 80),
            RiskLevel = SafeText(capability?.RiskLevel, "High", 40),
            TimeoutMs = capability?.TimeoutMs ?? 0,
            SupportedLanguages = SupportedLearningLanguages,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ReasonCodes = reasons.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static CodeDecision DecideMode(
        CodeLearningRuntimeReadinessDto runtime,
        CodeFacts facts,
        OrkaLearningStateDto state,
        OrkaMissionControlDto mission,
        OrkaStudyCoachDto coach,
        string? requestedMode)
    {
        var reasons = new List<string>();
        if (runtime.Status == "blocked")
        {
            reasons.AddRange(runtime.ReasonCodes);
            return new CodeDecision("blocked_runtime", "blocked", "not_started", "blocked", reasons);
        }

        if (facts.Signals.Count == 0 && state.SignalSummary.EvidenceCount <= 1)
        {
            reasons.Add("thin_evidence");
            return new CodeDecision("quick_start", "thin_evidence", "not_started", "not_needed", reasons);
        }

        if (facts.CompileErrorCount >= 2)
        {
            reasons.Add("repeated_syntax_error");
            return new CodeDecision("syntax_repair", "repair_ready", "needs_repair", "needs_repair", reasons);
        }

        if (facts.RuntimeErrorCount + facts.TimeoutCount >= 2)
        {
            reasons.Add("repeated_runtime_error");
            return new CodeDecision("runtime_error_repair", "repair_ready", "needs_repair", "needs_repair", reasons);
        }

        if (facts.TestFailureCount >= 2)
        {
            reasons.Add("repeated_test_failure");
            return new CodeDecision("test_failure_repair", "repair_ready", "needs_repair", "needs_repair", reasons);
        }

        if (facts.BlankAttemptCount >= 2)
        {
            reasons.Add("repeated_blank");
            return new CodeDecision("concept_practice", "limited", "needs_review", "diagnostic", reasons);
        }

        if (mission.ReviewLoad is "medium" or "high" || state.SignalSummary.DueReviewCount > 0)
        {
            reasons.Add("due_review");
            return new CodeDecision("review_drill", "ready", "not_started", "not_needed", reasons);
        }

        if (facts.SuccessCount >= 2 &&
            facts.CompileErrorCount + facts.RuntimeErrorCount + facts.TimeoutCount + facts.TestFailureCount + facts.BlankAttemptCount == 0)
        {
            reasons.Add("stable_recent_success");
            return new CodeDecision("continue_project", "ready", "passed", "not_needed", reasons);
        }

        if (facts.CompileErrorCount + facts.RuntimeErrorCount + facts.TimeoutCount + facts.TestFailureCount == 1)
        {
            reasons.Add(facts.CompileErrorCount == 1 ? "single_syntax_error" : "single_runtime_error");
            return new CodeDecision("concept_practice", "limited", "needs_review", "watch", reasons);
        }

        if (string.Equals(requestedMode, "checkpoint_challenge", StringComparison.OrdinalIgnoreCase) ||
            coach.FocusPlan.FocusMode is "continue_plan" or "quick_start")
        {
            reasons.Add("checkpoint_needed");
            return new CodeDecision("checkpoint_challenge", "ready", "not_started", "not_needed", reasons);
        }

        reasons.Add("thin_evidence");
        return new CodeDecision("quick_start", "limited", "not_started", "not_needed", reasons);
    }

    private static IEnumerable<CodeLearningActionDto> BuildActions(
        CodeIdeContext context,
        string language,
        CodeDecision decision,
        CodeFacts facts,
        OrkaLearningStateDto state,
        OrkaMissionControlDto mission,
        OrkaNotebookStudioProDto? notebook)
    {
        if (decision.Mode == "blocked_runtime")
        {
            yield return Action("runtime_blocked", "Runtime sinirli", "Bu dil/runtime guvenli calisma kontratinda kapali veya sinirli.", "urgent", "Code IDE", "code-learning", context, language, null, ["runtime_blocked", "tool_permission_limited"]);
            yield break;
        }

        if (facts.CompileErrorCount >= 2)
        {
            yield return Action("repair_syntax_error", "Syntax hatasini onar", "Tekrarlanan derleme/syntax hatasi repair akisi gerektiriyor.", "high", "Tutor", "chat", context, language, ResolveConcept(state, mission), ["repeated_syntax_error"]);
            yield return Action("create_code_repair_pack", "Code repair pack hazirla", "Tekrarlanan syntax hatasi Notebook pack'e donusebilir.", "normal", "Notebook Studio", "notebook-studio", context, language, ResolveConcept(state, mission), ["repeated_syntax_error", "notebook_pack_ready"]);
        }

        if (facts.RuntimeErrorCount + facts.TimeoutCount >= 2)
        {
            yield return Action("repair_runtime_error", "Runtime hatasini onar", "Tekrarlanan calisma zamani hatasi kisa repair gerektiriyor.", "high", "Tutor", "chat", context, language, ResolveConcept(state, mission), ["repeated_runtime_error"]);
            yield return Action("create_code_repair_pack", "Runtime repair pack hazirla", "Runtime hata oruntusu safe artifact handoff'a donusebilir.", "normal", "Notebook Studio", "notebook-studio", context, language, ResolveConcept(state, mission), ["repeated_runtime_error", "notebook_pack_ready"]);
        }

        if (facts.TestFailureCount >= 2)
        {
            yield return Action("repair_test_failure", "Test failure onar", "Tekrarlanan test failure logic/edge case repair gerektiriyor.", "high", "Tutor", "chat", context, language, ResolveConcept(state, mission), ["repeated_test_failure"]);
            yield return Action("create_code_repair_pack", "Test repair pack hazirla", "Test failure oruntusu Notebook repair pack'e baglanabilir.", "normal", "Notebook Studio", "notebook-studio", context, language, ResolveConcept(state, mission), ["repeated_test_failure", "notebook_pack_ready"]);
        }

        if (facts.BlankAttemptCount >= 2)
        {
            yield return Action("start_code_diagnostic", "Kisa kod tanisi yap", "Bos/no-attempt sinyali kesin yanilgi degil; guided diagnostic daha guvenli.", "high", "Code IDE", "code-learning", context, language, ResolveConcept(state, mission), ["repeated_blank", "thin_evidence"]);
        }

        if (state.SignalSummary.DueReviewCount > 0 || mission.ReviewLoad is "medium" or "high")
        {
            yield return Action("review_code_concept", "Kod kavramini tekrar et", "Review/SRS baskisi kod calismasina kisa tekrar olarak baglandi.", "normal", "Review", "learning", context, language, ResolveConcept(state, mission), ["due_review"]);
        }

        if (facts.Signals.Count == 0)
        {
            yield return Action("start_code_diagnostic", "Kisa kod pratigiyle basla", "Kod ogrenme kaniti ince; once guvenli mini diagnostic gerekir.", "normal", "Code IDE", "code-learning", context, language, ResolveConcept(state, mission), ["thin_evidence"]);
        }

        if (facts.SuccessCount >= 2 && decision.Mode == "continue_project")
        {
            yield return Action("continue_code_project", "Projeye devam et", "Tekrarlanan basarili calisma repair baskisini dusurur; checkpoint ile devam edilebilir.", "normal", "Code IDE", "code-learning", context, language, ResolveConcept(state, mission), ["stable_recent_success"]);
            yield return Action("take_code_checkpoint", "Code checkpoint al", "Basari sinyali sonraki checkpoint icin yeterli.", "normal", "Quiz", "quiz", context, language, ResolveConcept(state, mission), ["checkpoint_needed"]);
        }

        if (facts.Signals.Count > 0 && decision.Mode is "concept_practice" or "checkpoint_challenge")
        {
            yield return Action("practice_code_concept", "Kod kavrami pratigi yap", "Mevcut sinyal agir repair degil; kontrollu pratik uygun.", "normal", "Code IDE", "code-learning", context, language, ResolveConcept(state, mission), ["weak_code_concept"]);
            yield return Action("ask_tutor", "Hatayi Tutor ile acikla", "Tutor sadece guvenli hata kategorisi ve reason code alir.", "normal", "Tutor", "chat", context, language, ResolveConcept(state, mission), ["code_context_ready"]);
        }

        if (notebook?.RecommendedPacks.Any(p => p.PackType.Contains("code", StringComparison.OrdinalIgnoreCase)) != true &&
            facts.Signals.Count > 0)
        {
            yield return Action("create_code_note", "Wiki code notu olustur", "Kod denemesi bounded trace olarak not/handoff olabilir.", "low", "Wiki", "wiki", context, language, ResolveConcept(state, mission), ["code_note_ready"]);
        }
    }

    private static IReadOnlyList<CodeLearningWarningDto> BuildWarnings(
        CodeLearningRuntimeReadinessDto runtime,
        CodeFacts facts,
        OrkaLearningStateDto state,
        OrkaMissionControlDto mission,
        OrkaStudyCoachDto coach,
        CodeDecision decision)
    {
        var warnings = new List<CodeLearningWarningDto>();

        foreach (var warning in runtime.Warnings)
        {
            warnings.Add(Warning(warning, warning.Contains("blocked", StringComparison.OrdinalIgnoreCase) ? "warning" : "info", RuntimeWarningLabel(warning), "code-learning", [warning]));
        }

        if (facts.Signals.Count == 0 || decision.ReadinessStatus == "thin_evidence")
        {
            warnings.Add(Warning("thin_evidence", "info", "Kod ogrenme kaniti ince; kisa diagnostic daha guvenli.", "code-learning", ["thin_evidence"]));
        }

        if (facts.LastSignal?.PayloadJson is { } payload &&
            (ContainsBlockedMarker(payload) || LooksLikeUnsafeRuntimePayload(payload)))
        {
            warnings.Add(Warning("unsafe_payload_blocked", "warning", "Runtime metni public kontratta redakte edildi.", "code-learning", ["unsafe_payload_blocked"]));
            warnings.Add(Warning("stack_trace_redacted", "info", "Stack trace ayrintisi public kontrata alinmadi.", "code-learning", ["stack_trace_redacted"]));
            warnings.Add(Warning("local_path_redacted", "info", "Yerel path bilgisi public kontrata alinmadi.", "code-learning", ["local_path_redacted"]));
        }

        if (mission.PrimaryMission.ActionType is "repair_concept" or "repair_prerequisite" &&
            decision.Mode == "continue_project")
        {
            warnings.Add(Warning("code_priority_conflict", "warning", "Mission repair derken IDE continue sinyali uretti; repair onceligi korunur.", "dashboard", ["code_priority_conflict"]));
        }

        if (coach.FocusPlan.FocusMode == "study_room_lesson" && !state.TopicId.HasValue)
        {
            warnings.Add(Warning("missing_topic_context", "warning", "Kod pratigi icin once guvenli konu baglami gerekir.", "dashboard", ["missing_topic_context"]));
        }

        return warnings;
    }

    private static CodeLearningSessionDto BuildSessionDto(CodeFacts facts) => new()
    {
        SessionStatus = facts.Signals.Count == 0 ? "thin_evidence" : facts.SuccessCount > 0 ? "active" : "repair_needed",
        SignalCount = facts.Signals.Count,
        SuccessCount = facts.SuccessCount,
        CompileErrorCount = facts.CompileErrorCount,
        RuntimeErrorCount = facts.RuntimeErrorCount,
        TimeoutCount = facts.TimeoutCount,
        TestFailureCount = facts.TestFailureCount,
        BlankAttemptCount = facts.BlankAttemptCount,
        LastSignalAt = facts.LastSignal?.CreatedAt
    };

    private static CodeLearningExerciseDto BuildExerciseDto(string? exerciseId, string? conceptKey, CodeDecision decision) => new()
    {
        ExerciseId = SafeOptional(exerciseId),
        ExerciseStatus = decision.CheckpointStatus is "needs_repair" or "needs_review" ? "repair_suggested" : "suggested",
        ExerciseType = decision.Mode is "syntax_repair" or "runtime_error_repair" or "test_failure_repair" ? "repair_drill" : "checkpoint_challenge",
        SourceBasis = "learning_metadata",
        ConceptKey = SafeOptional(conceptKey),
        PreSubmitKeyVisible = false,
        ReasonCodes = SafeReasonCodes(decision.ReasonCodes.Concat(["answer_key_guard"]))
    };

    private static CodeLearningAttemptDto BuildLastAttemptDto(CodeFacts facts, string language)
    {
        var latest = facts.LastSignal;
        if (latest == null)
        {
            return new CodeLearningAttemptDto
            {
                Language = SafeKey(language, "csharp"),
                ReasonCodes = ["thin_evidence"]
            };
        }

        var phase = SafeKey(ExtractPayload(latest.PayloadJson, "phase") ?? SignalPhase(latest.SignalType), "run");
        var category = SafeErrorCategory(latest.SignalType, latest.PayloadJson);
        return new CodeLearningAttemptDto
        {
            Status = latest.IsPositive == true ? "passed" : "needs_review",
            Phase = phase,
            Success = latest.IsPositive == true,
            Language = SafeKey(latest.SkillTag ?? language, "csharp"),
            SafeErrorCategory = category,
            SafeTutorSummary = SafeText(ExtractPayload(latest.PayloadJson, "safeTutorSummary"), SummaryForCategory(category), 220),
            DurationMs = ExtractLongPayload(latest.PayloadJson, "durationMs") ?? 0,
            OutputTruncated = ExtractBoolPayload(latest.PayloadJson, "truncated") ?? false,
            CreatedAt = latest.CreatedAt,
            ReasonCodes = SafeReasonCodes([category, phase, latest.SignalType])
        };
    }

    private static CodeLearningErrorSummaryDto BuildErrorSummary(CodeFacts facts, CodeDecision decision) => new()
    {
        DominantErrorType = facts.DominantError,
        RepetitionCount = facts.DominantError switch
        {
            "syntax" => facts.CompileErrorCount,
            "runtime" => facts.RuntimeErrorCount + facts.TimeoutCount,
            "test_failure" => facts.TestFailureCount,
            "blank" => facts.BlankAttemptCount,
            _ => 0
        },
        RepairSuggestion = decision.Mode switch
        {
            "syntax_repair" => "Once syntax/import/tip hatasini kucuk ornekle onar.",
            "runtime_error_repair" => "Once runtime durumunu ve edge case'i izole et.",
            "test_failure_repair" => "Once beklenen davranisi ve failing case'i kucuklestir.",
            "concept_practice" => "Kisa diagnostic ve guided practice ile devam et.",
            _ => "Kisa checkpoint ile ilerle."
        },
        ReasonCodes = SafeReasonCodes(facts.ReasonCodes.Concat(decision.ReasonCodes))
    };

    private static IEnumerable<CodeLearningHandoffDto> ToHandoffs(IEnumerable<CodeLearningActionDto> actions, string handoffType, string route)
    {
        foreach (var action in actions)
        {
            yield return new CodeLearningHandoffDto
            {
                HandoffType = SafeKey(handoffType, action.ActionType),
                Label = action.Label,
                TargetRoute = SafeKey(route, action.TargetRoute),
                Priority = action.Priority,
                TopicId = action.TopicId,
                SessionId = action.SessionId,
                Language = action.Language,
                ConceptKey = action.ConceptKey,
                ReasonCodes = action.ReasonCodes
            };
        }
    }

    private async Task<string?> ResolveTopicTitleAsync(Guid userId, Guid? topicId, CancellationToken ct)
    {
        if (!topicId.HasValue) return null;
        var title = await _db.Topics.AsNoTracking()
            .Where(t => t.Id == topicId.Value && t.UserId == userId)
            .Select(t => t.Title)
            .FirstOrDefaultAsync(ct);
        return SafeOptional(title);
    }

    private static string? ResolveActiveSkill(CodeFacts facts, OrkaLearningStateDto state, OrkaMissionControlDto mission) =>
        SafeOptional(facts.LastSignal?.TopicPath) ??
        SafeOptional(mission.PrimaryMission.ConceptKey) ??
        SafeOptional(state.PrimaryNextAction.ConceptKey) ??
        SafeOptional(facts.LastSignal?.SkillTag);

    private static string? ResolveConcept(OrkaLearningStateDto state, OrkaMissionControlDto mission) =>
        SafeOptional(mission.PrimaryMission.ConceptKey) ?? SafeOptional(state.PrimaryNextAction.ConceptKey);

    private static CodeLearningActionDto Action(
        string actionType,
        string label,
        string reason,
        string priority,
        string entryPoint,
        string route,
        CodeIdeContext context,
        string language,
        string? conceptKey,
        IReadOnlyList<string> reasonCodes) => new()
    {
        ActionType = SafeKey(actionType, "start_code_diagnostic"),
        Label = SafeText(label, "Code IDE action"),
        Reason = SafeText(reason, "Kod ogrenme kanitindan guvenli aksiyon."),
        Priority = NormalizePriority(priority),
        EntryPoint = SafeText(entryPoint, "Code IDE", 80),
        TargetRoute = SafeKey(route, "code-learning"),
        TopicId = context.TopicId,
        SessionId = context.SessionId,
        Language = SafeKey(language, "csharp"),
        ConceptKey = SafeOptional(conceptKey),
        ReasonCodes = SafeReasonCodes(reasonCodes)
    };

    private static CodeLearningWarningDto Warning(string code, string severity, string label, string route, IReadOnlyList<string> reasonCodes) => new()
    {
        WarningCode = SafeKey(code, "code_warning"),
        Severity = NormalizeSeverity(severity),
        Label = SafeText(label, "Code IDE uyarisi."),
        TargetRoute = SafeKey(route, "code-learning"),
        ReasonCodes = SafeReasonCodes(reasonCodes)
    };

    private static int CountSignals(IReadOnlyList<LearningSignal> signals, string signalType, string phase, string marker) =>
        signals.Count(s =>
            string.Equals(s.SignalType, signalType, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ExtractPayload(s.PayloadJson, "phase"), phase, StringComparison.OrdinalIgnoreCase) ||
            PayloadContains(s.PayloadJson, marker));

    private static string DominantError(int compile, int runtime, int test, int blank)
    {
        var values = new[] { ("syntax", compile), ("runtime", runtime), ("test_failure", test), ("blank", blank) }
            .OrderByDescending(v => v.Item2)
            .ToArray();
        return values[0].Item2 == 0 ? "none" : values[0].Item1;
    }

    private static string SafeErrorCategory(string signalType, string? payloadJson)
    {
        if (string.Equals(signalType, LearningSignalTypes.IdeCompileError, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ExtractPayload(payloadJson, "phase"), "compile", StringComparison.OrdinalIgnoreCase))
        {
            return "syntax";
        }

        if (string.Equals(signalType, LearningSignalTypes.IdeExecutionTimeout, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(ExtractPayload(payloadJson, "phase"), "timeout", StringComparison.OrdinalIgnoreCase))
        {
            return "timeout";
        }

        if (string.Equals(signalType, LearningSignalTypes.IdeTestFailure, StringComparison.OrdinalIgnoreCase) ||
            PayloadContains(payloadJson, "test_failure"))
        {
            return "test_failure";
        }

        if (string.Equals(signalType, LearningSignalTypes.IdeBlankAttempt, StringComparison.OrdinalIgnoreCase) ||
            PayloadContains(payloadJson, "blank"))
        {
            return "blank";
        }

        if (string.Equals(signalType, LearningSignalTypes.IdeRuntimeError, StringComparison.OrdinalIgnoreCase))
        {
            return "runtime";
        }

        return "none";
    }

    private static string SignalPhase(string signalType) => signalType switch
    {
        LearningSignalTypes.IdeCompileError => "compile",
        LearningSignalTypes.IdeExecutionTimeout => "timeout",
        LearningSignalTypes.IdeProviderUnavailable => "provider_missing",
        LearningSignalTypes.IdeRunCompleted => "run",
        _ => "run"
    };

    private static string SummaryForCategory(string category) => category switch
    {
        "syntax" => "Syntax/derleme hatasi var; kucuk ornekle onar.",
        "runtime" => "Runtime hatasi var; calisma zamani durumunu izole et.",
        "timeout" => "Kod zaman asimina dustu; dongu veya karmasiklik kontrolu yap.",
        "test_failure" => "Test failure var; beklenen davranisi ve failing case'i ayir.",
        "blank" => "Deneme bos kaldi; kesin yanilgi demeden guided diagnostic yap.",
        _ => "Kod denemesi guvenli ozetle izlendi."
    };

    private static string BuildSummary(CodeDecision decision, CodeFacts facts, CodeLearningRuntimeReadinessDto runtime)
    {
        if (runtime.Status == "blocked")
        {
            return "Code IDE runtime bu dil veya arac icin sinirli; guvenli runtime acilmadan kod calistirma onerilmez.";
        }

        if (decision.Mode is "syntax_repair" or "runtime_error_repair" or "test_failure_repair")
        {
            return $"Code IDE {facts.DominantError} oruntusunu repair aksiyonuna cevirdi; raw trace veya path public kontrata eklenmedi.";
        }

        if (decision.Mode == "continue_project")
        {
            return "Tekrarlanan basarili kod denemeleri var; proje akisi checkpoint ile surdurulebilir.";
        }

        return "Code IDE mevcut ogrenme sinyallerinden guvenli pratik/checkpoint akisi hazirladi.";
    }

    private static string? ExtractPayload(string? payloadJson, string property)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (!doc.RootElement.TryGetProperty(property, out var value)) return null;
            return value.ValueKind switch
            {
                JsonValueKind.String => SafeOptional(value.GetString()),
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static long? ExtractLongPayload(string? payloadJson, string property)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (!doc.RootElement.TryGetProperty(property, out var value)) return null;
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool? ExtractBoolPayload(string? payloadJson, string property)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (!doc.RootElement.TryGetProperty(property, out var value)) return null;
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool PayloadContains(string? payloadJson, string marker) =>
        !string.IsNullOrWhiteSpace(payloadJson) &&
        payloadJson.Contains(marker, StringComparison.OrdinalIgnoreCase);

    private static bool IsBlockedLanguage(string? language)
    {
        var key = SafeKey(language, string.Empty);
        return string.IsNullOrWhiteSpace(key) ||
               BlockedLanguageFamilies.Contains(key, StringComparer.OrdinalIgnoreCase);
    }

    private static string? NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;
        var key = SafeKey(language.Trim().ToLowerInvariant(), string.Empty);
        return string.IsNullOrWhiteSpace(key) ? null : key;
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

    private static int PriorityScore(string? priority) => priority switch
    {
        "urgent" => 4,
        "high" => 3,
        "normal" or "medium" => 2,
        "low" => 1,
        _ => 0
    };

    private static int ActionRank(string? actionType) => actionType switch
    {
        "runtime_blocked" => 0,
        "repair_syntax_error" => 1,
        "repair_runtime_error" => 2,
        "repair_test_failure" => 3,
        "start_code_diagnostic" => 4,
        "review_code_concept" => 5,
        "take_code_checkpoint" => 6,
        "create_code_repair_pack" => 7,
        "ask_tutor" => 8,
        _ => 20
    };

    private static string RuntimeWarningLabel(string code) => code switch
    {
        "code_runtime_blocked" => "Kod runtime bu istek icin kapali veya guvenli degil.",
        "code_runtime_limited" => "Kod runtime dil/yetki nedeniyle sinirli.",
        "tool_permission_limited" => "IDE execution sadece auth ve sandbox arkasinda calisir.",
        "sandbox_required" => "Kod calistirma sandbox disina cikmaz.",
        _ => code
    };

    private static IReadOnlyList<string> SafeReasonCodes(IEnumerable<string> values) =>
        values.Where(NotBlank)
            .Select(v => SafeKey(v, "reason"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();

    private static string SafeText(string? value, string fallback, int maxLength = 220)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (var marker in BlockedMarkers)
        {
            text = Regex.Replace(text, Regex.Escape(marker), "[redacted]", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(2000));
        }

        text = Regex.Replace(text, @"[A-Za-z]:\\[^\s,;]+", "[redacted_path]", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(2000));
        text = Regex.Replace(text, @"/(?:home|users|var|tmp|workspace|app)/[^\s,;]+", "[redacted_path]", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(2000));
        text = Regex.Replace(text, @"(?i)(api[_-]?key|secret|token)\s*[:=]\s*['""]?[^'""\s,;]+", "[redacted_credential]", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(2000));
        text = Regex.Replace(text, @"(?im)^\s*at\s+.+$", "[redacted_trace]", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(2000));
        text = Regex.Replace(text, @"(?is)traceback\s*\(most recent call last\):.*", "[redacted_trace]", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(2000));
        text = text.Replace("\r", " ").Replace("\n", " ");
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static string? SafeOptional(string? value)
    {
        var safe = SafeText(value, string.Empty, 120);
        return string.IsNullOrWhiteSpace(safe) ? null : safe;
    }

    private static string SafeKey(string? value, string fallback)
    {
        var safe = SafeText(value, fallback, 100).ToLowerInvariant();
        safe = Regex.Replace(safe, @"[^a-z0-9_\-]+", "_", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(2000)).Trim('_');
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
    }

    private static bool ContainsBlockedMarker(string value) =>
        BlockedMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeUnsafeRuntimePayload(string value) =>
        value.Contains("C:\\", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Traceback", StringComparison.OrdinalIgnoreCase) ||
        Regex.IsMatch(value, @"(?im)^\s*at\s+", RegexOptions.None, TimeSpan.FromMilliseconds(2000));

    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);

    private sealed record CodeIdeContext(Guid? TopicId, Guid? SessionId);

    private sealed record CodeFacts(
        IReadOnlyList<LearningSignal> Signals,
        string? ActiveLanguage,
        int CompileErrorCount,
        int RuntimeErrorCount,
        int TimeoutCount,
        int ProviderUnavailableCount,
        int TestFailureCount,
        int BlankAttemptCount,
        int SuccessCount,
        string DominantError,
        LearningSignal? LastSignal,
        IReadOnlyList<string> ReasonCodes);

    private sealed record CodeDecision(
        string Mode,
        string ReadinessStatus,
        string CheckpointStatus,
        string RepairStatus,
        IReadOnlyList<string> ReasonCodes);
}
