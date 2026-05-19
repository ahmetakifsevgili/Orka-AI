using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class TutorResponsePolicyService : ITutorResponsePolicyService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OrkaDbContext _db;

    public TutorResponsePolicyService(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<TutorResponsePolicyDto> BuildPolicyAsync(
        Guid userId,
        TutorResponsePolicyRequestDto request,
        CancellationToken ct = default)
    {
        var turn = await ResolveTurnStateAsync(userId, request, ct);
        var trace = await ResolveActionTraceAsync(userId, request, turn, ct);
        var latestAttempt = await LoadLatestAttemptAsync(userId, request.TopicId ?? turn?.TopicId, request.SessionId ?? turn?.SessionId, ct);
        var latestBundle = await LoadLatestSourceBundleAsync(userId, request.TopicId ?? turn?.TopicId, request.SessionId ?? turn?.SessionId, ct);
        var toolCalls = trace == null
            ? []
            : await _db.TutorToolCalls.AsNoTracking()
                .Where(t => t.UserId == userId && t.TutorActionTraceId == trace.Id)
                .OrderBy(t => t.CreatedAt)
                .ToListAsync(ct);

        return BuildPolicy(turn, trace, latestAttempt, latestBundle, toolCalls, request.ActiveQuizUnsubmitted);
    }

    public async Task<TutorResponseQualityEvaluationDto> EvaluateTutorResponseAsync(
        Guid userId,
        TutorResponseQualityEvaluationRequestDto request,
        CancellationToken ct = default)
    {
        var policy = request.Policy ?? await BuildPolicyAsync(userId, new TutorResponsePolicyRequestDto
        {
            TopicId = request.TopicId,
            SessionId = request.SessionId,
            TutorTurnStateId = request.TutorTurnStateId,
            TutorActionTraceId = request.TutorActionTraceId,
            ActiveQuizUnsubmitted = request.ActiveQuizUnsubmitted
        }, ct);

        var answer = Normalize(request.AssistantAnswer);
        var blocking = new List<TutorResponseQualityIssueDto>();
        var warnings = new List<TutorResponseQualityIssueDto>(policy.Warnings);

        if (policy.ContextUse.Any(c => c.ContextType is "active_lesson" or "student_context" or "plan_step" && c.Status == "available") &&
            ContainsAny(answer, "genel olarak calis", "genel olarak çalış", "bol bol tekrar", "daha cok calis", "daha çok çalış"))
        {
            warnings.Add(Issue("too_generic", "warning", "Cevap mevcut ders baglamina ragmen fazla genel kaliyor."));
        }

        if (request.ActiveQuizUnsubmitted && ContainsAnswerLeak(answer))
        {
            blocking.Add(Issue("answer_key_leak", "blocking", "Aktif quiz bitmeden cevap anahtari verilemez."));
        }

        if (policy.GroundingPolicy is "evidence_insufficient" or "model_assisted_unsourced" or "mention_source_limits" &&
            ContainsSourceOverclaim(answer))
        {
            blocking.Add(Issue("source_overclaim", "blocking", "Kaynak kaniti yetersizken kaynakli kesinlik iddiasi kurulamaz."));
        }

        if (ContainsAny(answer, "kesin basarirsin", "kesin başarırsın", "garanti kazanirsin", "garanti kazanırsın"))
        {
            blocking.Add(Issue("success_guarantee", "blocking", "Basari garantisi verilemez."));
        }

        if (ContainsAny(answer, "resmi osym simulasyonu", "resmi ösym simülasyonu", "resmi meb simulasyonu", "mufredat tamam", "müfredat tamam"))
        {
            blocking.Add(Issue("official_claim", "blocking", "Dogrulanmamis resmi sinav/mufredat iddiasi kurulamaz."));
        }

        if (ContainsAny(answer, "dershane panel", "ogretmen panel", "öğretmen panel", "sinif yonetimi", "sınıf yönetimi"))
        {
            blocking.Add(Issue("teacher_workflow_copy", "blocking", "Teacher/classroom/dershane is akisi bu paketin kapsami degil."));
        }

        if (policy.RemediationPolicy is "guided_repair" or "prerequisite_review" &&
            ContainsAny(answer, "sadece oku", "notlari oku", "notları oku") &&
            !ContainsAny(answer, "kontrol", "deneyelim", "ornek", "örnek", "?"))
        {
            warnings.Add(Issue("passive_only_weak_learner", "warning", "Zayif kavramda pasif okuma tek basina yeterli next action degil."));
        }

        var result = new TutorResponseQualityEvaluationDto
        {
            QualityStatus = blocking.Count > 0 ? "needs_revision" : warnings.Count > 0 ? "usable" : "strong",
            ContextUseScore = Score(policy.ContextUse.Any(c => c.Status == "available"), warnings.Any(i => i.Code == "too_generic")),
            GroundingScore = blocking.Any(i => i.Code == "source_overclaim") ? 0.15m : policy.GroundingPolicy == "cite_sources" ? 0.90m : 0.72m,
            PedagogyScore = warnings.Any(i => i.Code == "too_generic") ? 0.55m : 0.82m,
            RemediationScore = warnings.Any(i => i.Code == "passive_only_weak_learner") ? 0.45m : policy.RemediationPolicy == "none" ? 0.75m : 0.85m,
            SafetyScore = blocking.Count > 0 ? 0.10m : 0.92m,
            ToolUseScore = policy.ToolPolicy is "run_tool_if_allowed" or "degrade_tool" ? 0.82m : 0.75m,
            BlockingIssues = blocking,
            WarningIssues = warnings,
            Policy = policy,
            EvaluatedAt = DateTimeOffset.UtcNow
        };
        result.Policy.QualityStatus = result.QualityStatus;
        result.Policy.SafetyIssues = blocking.Select(i => new TutorAnswerSafetyIssueDto
        {
            Code = i.Code,
            Severity = i.Severity,
            UserSafeMessage = i.UserSafeMessage
        }).ToArray();
        result.Policy.Warnings = warnings;
        return result;
    }

    public async Task<TutorResponseQualityEvaluationDto?> GetLatestResponseQualityAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId = null,
        CancellationToken ct = default)
    {
        var latest = await _db.TutorPedagogyEvaluationRuns
            .AsNoTracking()
            .Where(r => r.UserId == userId &&
                        (!topicId.HasValue || r.TopicId == topicId) &&
                        (!sessionId.HasValue || r.SessionId == sessionId))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (latest == null)
        {
            return null;
        }

        var policy = await BuildPolicyAsync(userId, new TutorResponsePolicyRequestDto
        {
            TopicId = latest.TopicId,
            SessionId = latest.SessionId,
            TutorTurnStateId = latest.TutorTurnStateId,
            TutorActionTraceId = latest.TutorActionTraceId
        }, ct);

        var warnings = await _db.TutorPedagogyRubricScores
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.EvaluationRunId == latest.Id && (s.IsCritical || s.Score < 0.60m))
            .OrderBy(s => s.RubricKey)
            .Select(s => new TutorResponseQualityIssueDto
            {
                Code = s.RubricKey,
                Severity = s.IsCritical ? "blocking" : "warning",
                UserSafeMessage = s.Recommendation
            })
            .ToArrayAsync(ct);

        return new TutorResponseQualityEvaluationDto
        {
            QualityStatus = latest.Status == "healthy" ? "strong" : latest.HasCriticalViolation ? "needs_revision" : "usable",
            ContextUseScore = latest.OverallScore,
            GroundingScore = latest.OverallScore,
            PedagogyScore = latest.OverallScore,
            RemediationScore = latest.OverallScore,
            SafetyScore = latest.HasCriticalViolation ? 0.30m : latest.OverallScore,
            ToolUseScore = latest.OverallScore,
            BlockingIssues = warnings.Where(w => w.Severity == "blocking").ToArray(),
            WarningIssues = warnings.Where(w => w.Severity != "blocking").ToArray(),
            Policy = policy,
            EvaluatedAt = latest.CreatedAt
        };
    }

    public async Task<IReadOnlyList<TutorNextLearningActionDto>> GetTutorNextLearningActionsAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId = null,
        CancellationToken ct = default)
    {
        var policy = await BuildPolicyAsync(userId, new TutorResponsePolicyRequestDto { TopicId = topicId, SessionId = sessionId }, ct);
        return policy.NextActions;
    }

    public static TutorResponsePolicyDto BuildPolicy(
        TutorTurnStateDto? turn,
        TutorActionTrace? trace,
        QuizAttempt? latestAttempt,
        SourceEvidenceBundle? latestBundle,
        IReadOnlyList<TutorToolCall> toolCalls,
        bool activeQuizUnsubmitted = false)
    {
        var mode = turn == null ? "standard" : TutorResponsePolicy.Decide(turn).TutorResponseMode;
        var teachingMove = NormalizeTeachingMove(trace?.TeachingMode, turn?.CurrentPlanTutorMove, turn?.RemediationSeed?.FirstAction, turn?.LearnerState, turn?.MasteryProbability, turn?.Confidence);
        var sourceReadiness = SourceReadiness(turn, latestBundle);
        var grounding = GroundingPolicy(sourceReadiness, turn?.GroundingStatus, turn?.SourceEvidenceCount ?? 0);
        var assessmentMode = ExtractMetadata(latestAttempt?.SourceRefsJson, "assessmentMode") ?? turn?.LatestAssessmentMode ?? "unknown";
        var misconceptionConfidence = turn?.LearningSignalConfidence?.Status ?? turn?.LatestMisconceptionConfidence ?? "none";
        var remediation = RemediationPolicy(turn, latestAttempt);
        var toolPolicy = ToolPolicy(toolCalls);
        var safety = AnswerSafety(activeQuizUnsubmitted, grounding, turn);
        var warnings = BuildWarnings(turn, latestBundle, toolCalls, sourceReadiness, misconceptionConfidence);
        var nextActions = BuildNextActions(turn, teachingMove, remediation, grounding, assessmentMode, toolPolicy);
        var contextUse = BuildContextUse(turn, latestBundle, latestAttempt);

        return new TutorResponsePolicyDto
        {
            TopicId = turn?.TopicId ?? latestAttempt?.TopicId ?? latestBundle?.TopicId,
            SessionId = turn?.SessionId ?? latestAttempt?.SessionId ?? latestBundle?.SessionId,
            TutorTurnStateId = turn?.Id,
            TutorActionTraceId = trace?.Id,
            ActiveLessonSnapshotId = turn?.ActiveLessonSnapshotId,
            StudentContextSnapshotId = turn?.StudentContextSnapshotId,
            PlanQualitySnapshotId = turn?.PlanQualitySnapshotId,
            TeachingMove = teachingMove,
            ResponseDepth = ResponseDepth(mode),
            GroundingPolicy = grounding,
            RemediationPolicy = remediation,
            ToolPolicy = toolPolicy,
            AnswerSafety = safety,
            SourceReadiness = sourceReadiness,
            LatestAssessmentMode = assessmentMode,
            LatestMisconceptionConfidence = misconceptionConfidence,
            QualityStatus = safety == "safe" ? "usable" : "needs_revision",
            ContextUse = contextUse,
            NextActions = nextActions,
            SafetyIssues = safety == "safe" ? [] : [new TutorAnswerSafetyIssueDto { Code = safety, Severity = "blocking", UserSafeMessage = "Tutor cevabi yayinlanmadan once guvenlik riski tasiyor." }],
            Warnings = warnings,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<TutorTurnStateDto?> ResolveTurnStateAsync(Guid userId, TutorResponsePolicyRequestDto request, CancellationToken ct)
    {
        var query = _db.TutorTurnStates.AsNoTracking().Where(t => t.UserId == userId);
        if (request.TutorTurnStateId.HasValue)
            query = query.Where(t => t.Id == request.TutorTurnStateId);
        else
            query = query.Where(t =>
                (!request.TopicId.HasValue || t.TopicId == request.TopicId) &&
                (!request.SessionId.HasValue || t.SessionId == request.SessionId));

        var entity = await query.OrderByDescending(t => t.CreatedAt).FirstOrDefaultAsync(ct);
        if (entity == null) return null;
        try
        {
            return JsonSerializer.Deserialize<TutorTurnStateDto>(entity.StateJson, JsonOptions);
        }
        catch
        {
            return new TutorTurnStateDto
            {
                Id = entity.Id,
                UserId = entity.UserId,
                TopicId = entity.TopicId,
                SessionId = entity.SessionId,
                ActiveConceptKey = entity.ActiveConceptKey,
                StyleMode = entity.StyleMode,
                AffectiveState = entity.AffectiveState,
                CognitiveLoad = entity.CognitiveLoad,
                GroundingStatus = entity.GroundingStatus,
                CreatedAt = entity.CreatedAt
            };
        }
    }

    private async Task<TutorActionTrace?> ResolveActionTraceAsync(Guid userId, TutorResponsePolicyRequestDto request, TutorTurnStateDto? turn, CancellationToken ct)
    {
        var query = _db.TutorActionTraces.AsNoTracking().Where(t => t.UserId == userId);
        if (request.TutorActionTraceId.HasValue)
            query = query.Where(t => t.Id == request.TutorActionTraceId);
        else if (turn != null)
            query = query.Where(t => t.TutorTurnStateId == turn.Id);
        else
            query = query.Where(t =>
                (!request.TopicId.HasValue || t.TopicId == request.TopicId) &&
                (!request.SessionId.HasValue || t.SessionId == request.SessionId));

        return await query.OrderByDescending(t => t.CreatedAt).FirstOrDefaultAsync(ct);
    }

    private async Task<QuizAttempt?> LoadLatestAttemptAsync(Guid userId, Guid? topicId, Guid? sessionId, CancellationToken ct)
    {
        var query = _db.QuizAttempts.AsNoTracking().Where(a => a.UserId == userId);
        if (topicId.HasValue) query = query.Where(a => a.TopicId == topicId);
        if (sessionId.HasValue) query = query.Where(a => a.SessionId == sessionId);
        return await query.OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(ct);
    }

    private async Task<SourceEvidenceBundle?> LoadLatestSourceBundleAsync(Guid userId, Guid? topicId, Guid? sessionId, CancellationToken ct)
    {
        var query = _db.SourceEvidenceBundles.AsNoTracking().Where(b => b.UserId == userId && !b.IsDeleted);
        if (topicId.HasValue) query = query.Where(b => b.TopicId == topicId);
        if (sessionId.HasValue) query = query.Where(b => b.SessionId == sessionId);
        return await query.OrderByDescending(b => b.UpdatedAt).FirstOrDefaultAsync(ct);
    }

    private static IReadOnlyList<TutorContextUseDto> BuildContextUse(TutorTurnStateDto? turn, SourceEvidenceBundle? bundle, QuizAttempt? latestAttempt)
    {
        if (turn == null)
        {
            return [new TutorContextUseDto { ContextType = "turn_state", Status = "not_available", UserSafeSummary = "Tutor turn state bulunamadi; cevap guvenli fallback ile sinirlanmali." }];
        }

        return
        [
            new TutorContextUseDto { ContextType = "active_lesson", Status = turn.ActiveLessonSnapshotId.HasValue ? "available" : "not_available", UserSafeSummary = turn.LessonSnapshotStatus },
            new TutorContextUseDto { ContextType = "student_context", Status = turn.StudentContextSnapshotId.HasValue ? "available" : "not_available", UserSafeSummary = turn.StudentContextConfidenceStatus },
            new TutorContextUseDto { ContextType = "plan_step", Status = string.IsNullOrWhiteSpace(turn.CurrentPlanStepId) ? "not_available" : "available", UserSafeSummary = turn.CurrentPlanStepTitle ?? "plan step yok" },
            new TutorContextUseDto { ContextType = "source_evidence", Status = SourceReadiness(turn, bundle), UserSafeSummary = bundle?.EvidenceStatus ?? turn.GroundingStatus },
            new TutorContextUseDto { ContextType = "latest_assessment", Status = latestAttempt == null ? "not_available" : "available", UserSafeSummary = latestAttempt == null ? "quiz sinyali yok" : (latestAttempt.IsCorrect ? "son cevap dogru" : latestAttempt.WasSkipped ? "son cevap bos" : "son cevap yanlis") }
        ];
    }

    private static IReadOnlyList<TutorResponseQualityIssueDto> BuildWarnings(TutorTurnStateDto? turn, SourceEvidenceBundle? bundle, IReadOnlyList<TutorToolCall> toolCalls, string sourceReadiness, string misconceptionConfidence)
    {
        var warnings = new List<TutorResponseQualityIssueDto>();
        if (turn == null)
            warnings.Add(Issue("missing_turn_state", "warning", "Tutor turn state yok; yanit fallback politikasiyla sinirlanmali."));
        if (turn?.ActiveLessonSnapshotId == null)
            warnings.Add(Issue("missing_active_lesson_snapshot", "warning", "Aktif ders snapshot'i yok."));
        if (turn?.StudentContextSnapshotId == null)
            warnings.Add(Issue("missing_student_context_snapshot", "warning", "Ogrenci context snapshot'i yok."));
        if (string.IsNullOrWhiteSpace(turn?.CurrentPlanStepId))
            warnings.Add(Issue("missing_plan_step", "warning", "Aktif plan adimi yok."));
        if (sourceReadiness is "stale" or "degraded")
            warnings.Add(Issue("stale_or_degraded_evidence", "warning", "Kaynak kaniti sinirli veya eski."));
        if (sourceReadiness == "evidence_insufficient")
            warnings.Add(Issue("evidence_insufficient", "warning", "Kaynak kaniti yetersiz; kaynakli kesinlik kurulmamali."));
        if (misconceptionConfidence is "low" or "observed_only")
            warnings.Add(Issue("low_confidence_misconception", "warning", "Yanilgi sinyali dusuk guvenle ele alinmali."));
        if (toolCalls.Any(t => !t.Success || t.Status is "failed" or "degraded" or "denied"))
            warnings.Add(Issue("tool_degraded", "warning", "Bir veya daha fazla tool guvenli fallback ile sinirlandi."));
        if (bundle?.EvidenceStatus is "stale" or "degraded")
            warnings.Add(Issue("bundle_degraded", "warning", "Evidence bundle guven durumu sinirli."));
        return warnings
            .GroupBy(w => w.Code, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
    }

    private static IReadOnlyList<TutorNextLearningActionDto> BuildNextActions(TutorTurnStateDto? turn, string teachingMove, string remediation, string grounding, string assessmentMode, string toolPolicy)
    {
        var concept = turn?.ActiveConceptKey;
        var actions = new List<TutorNextLearningActionDto>();
        if (remediation is "guided_repair" or "prerequisite_review")
            actions.Add(new TutorNextLearningActionDto { ActionType = "start_micro_quiz", UserSafeLabel = "Kisa mikro kontrol yap", TargetConceptKey = concept, Priority = "high" });
        if (grounding is "cite_sources" or "wiki_context_only")
            actions.Add(new TutorNextLearningActionDto { ActionType = "review_wiki_section", UserSafeLabel = "Wiki/kaynak bolumunu tekrar et", TargetConceptKey = concept, Priority = "normal" });
        if (grounding is "evidence_insufficient" or "mention_source_limits")
            actions.Add(new TutorNextLearningActionDto { ActionType = "open_source_evidence", UserSafeLabel = "Kaynak kanitini kontrol et", TargetConceptKey = concept, Priority = "normal" });
        if (toolPolicy == "run_tool_if_allowed")
            actions.Add(new TutorNextLearningActionDto { ActionType = "run_code_tool", UserSafeLabel = "Gerekli tool'u kontrollu calistir", TargetConceptKey = concept, Priority = "normal" });
        if (teachingMove is "socratic_check" or "retrieval_prompt" || assessmentMode is "retrieval_practice" or "review_check")
            actions.Add(new TutorNextLearningActionDto { ActionType = "ask_socratic_check", UserSafeLabel = "Kisa kontrol sorusu sor", TargetConceptKey = concept, Priority = "normal" });
        actions.Add(new TutorNextLearningActionDto { ActionType = "continue_plan", UserSafeLabel = "Plan adimina devam et", TargetConceptKey = concept, Priority = "normal" });
        return actions
            .GroupBy(a => a.ActionType, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(5)
            .ToArray();
    }

    private static string NormalizeTeachingMove(string? traceMode, string? planTutorMove, string? remediationAction, string? learnerState, decimal? mastery, decimal? confidence)
    {
        if (remediationAction is "prerequisite_review") return "prerequisite_repair";
        if (remediationAction is "practice_quiz") return "guided_practice";
        if (!string.IsNullOrWhiteSpace(planTutorMove) && IsTeachingMove(planTutorMove)) return Normalize(planTutorMove);
        var mode = Normalize(traceMode);
        if (mode is "remediate") return "misconception_repair";
        if (mode is "guided_practice") return "guided_practice";
        if (mode is "challenge") return "socratic_check";
        if (mode is "source_grounded_answer") return "source_review";
        if (mode is "visualize") return "example";
        if (mode is "review") return "retrieval_prompt";
        if (mode is "code_lab") return "tool_guided_help";
        if (learnerState?.Contains("remediation", StringComparison.OrdinalIgnoreCase) == true || mastery < 0.45m || confidence < 0.45m) return "misconception_repair";
        return "explain";
    }

    private static string GroundingPolicy(string sourceReadiness, string? groundingStatus, int evidenceCount)
    {
        if (sourceReadiness == "source_grounded" || Normalize(groundingStatus) == "source_grounded" && evidenceCount > 0) return "cite_sources";
        if (sourceReadiness == "wiki_backed") return "wiki_context_only";
        if (sourceReadiness is "degraded" or "stale" or "mixed") return "mention_source_limits";
        return "evidence_insufficient";
    }

    private static string RemediationPolicy(TutorTurnStateDto? turn, QuizAttempt? latestAttempt)
    {
        if (latestAttempt?.WasSkipped == true) return "prerequisite_review";
        if (latestAttempt is { IsCorrect: false }) return "guided_repair";
        if (turn?.RemediationNeed is "high" or "medium") return "guided_repair";
        if (turn?.RemediationSeed?.FirstAction == "prerequisite_review") return "prerequisite_review";
        if (turn?.LearningSignalConfidence?.Status is "usable" && turn.RemediationSeed != null) return "guided_repair";
        return "none";
    }

    private static string ToolPolicy(IReadOnlyList<TutorToolCall> calls)
    {
        if (calls.Count == 0) return "no_tool";
        if (calls.Any(t => !t.Success || t.Status is "failed" or "denied")) return "degrade_tool";
        if (calls.Any(t => t.Success)) return "run_tool_if_allowed";
        return "suggest_tool";
    }

    private static string AnswerSafety(bool activeQuizUnsubmitted, string groundingPolicy, TutorTurnStateDto? turn)
    {
        if (activeQuizUnsubmitted) return "answer_key_risk";
        if (groundingPolicy is "evidence_insufficient" && turn?.SourceEvidenceCount > 0) return "source_overclaim_risk";
        return "safe";
    }

    private static string SourceReadiness(TutorTurnStateDto? turn, SourceEvidenceBundle? bundle)
    {
        var status = Normalize(bundle?.EvidenceStatus);
        if (status is "source_grounded" or "wiki_backed" or "mixed" or "degraded" or "stale" or "evidence_insufficient") return status;
        var plan = Normalize(turn?.PlanSourceReadiness ?? turn?.SourceReadiness);
        if (plan is "source_grounded" or "wiki_backed" or "mixed" or "degraded" or "stale" or "evidence_insufficient") return plan;
        if (Normalize(turn?.GroundingStatus) == "source_grounded" && turn?.SourceEvidenceCount > 0) return "source_grounded";
        return "evidence_insufficient";
    }

    private static string ResponseDepth(string mode) => Normalize(mode) switch
    {
        "concise" => "concise",
        "deep" => "deep",
        "recovery" => "step_by_step",
        "evidence_limited" => "normal",
        _ => "normal"
    };

    private static decimal Score(bool contextAvailable, bool weak) => !contextAvailable ? 0.45m : weak ? 0.55m : 0.85m;

    private static bool IsTeachingMove(string value) =>
        Normalize(value) is "explain" or "scaffold" or "example" or "analogy" or "socratic_check" or "misconception_repair" or "prerequisite_repair" or "guided_practice" or "retrieval_prompt" or "source_review" or "plan_redirect" or "tool_guided_help" or "confidence_check" or "summarize" or "next_action";

    private static bool ContainsAnswerLeak(string text) =>
        ContainsAny(text, "dogru cevap a", "doğru cevap a", "dogru cevap b", "doğru cevap b", "correct answer", "cevap anahtari", "cevap anahtarı", "secenek a", "seçenek a");

    private static bool ContainsSourceOverclaim(string text) =>
        ContainsAny(text, "kaynaklara gore kesin", "kaynaklara göre kesin", "belgeye gore kesin", "belgeye göre kesin", "kaynakli kesin", "kaynaklı kesin");

    private static string? ExtractMetadata(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty(key, out var value)
                ? value.ToString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static TutorResponseQualityIssueDto Issue(string code, string severity, string message) => new()
    {
        Code = code,
        Severity = severity,
        UserSafeMessage = message
    };

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}
