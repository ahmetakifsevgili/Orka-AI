using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class TutorPedagogyRubricService : ITutorPedagogyRubricService
{
    public IReadOnlyList<TutorPedagogyRubricScoreDto> EvaluateDeterministic(
        TutorPedagogyEvaluationRequestDto request)
    {
        var answer = Normalize(request.AssistantAnswer);
        var turn = request.TurnState;
        var plan = request.ActionPlan;
        var reflection = request.Reflection;
        var plannedArtifacts = plan.ArtifactPlans.Count;
        var producedArtifacts = request.Artifacts.Count;
        var readyTools = request.ToolCalls.Count(t => t.Success && string.Equals(t.Status, "ready", StringComparison.OrdinalIgnoreCase));

        var scores = new List<TutorPedagogyRubricScoreDto>
        {
            Score(
                "policy_alignment",
                string.IsNullOrWhiteSpace(plan.TeachingMode) ? 0.30m : 0.90m,
                "Tutor action plan applied to the answer.",
                string.IsNullOrWhiteSpace(plan.TeachingMode) ? "Plan olmadan cevap üretme." : "Plan uyumu korunmuş."),

            Score(
                "scaffolding_quality",
                ScaffoldScore(turn, plan, reflection, answer),
                "Direct-answer policy and hint/scaffold behavior checked.",
                "Düşük mastery veya doğrudan cevap riskinde önce ipucu ve küçük adım kullan."),

            Score(
                "learner_adaptation",
                LearnerAdaptationScore(turn, answer),
                "Mastery, confidence, affective state and cognitive load cues checked.",
                "Öğrencinin kanıt düzeyini, duygu durumunu ve bilişsel yükünü açıkça hesaba kat."),

            Score(
                "concept_focus",
                string.IsNullOrWhiteSpace(turn.ActiveConceptKey) && turn.TopicId.HasValue ? 0.20m : 0.90m,
                string.IsNullOrWhiteSpace(turn.ActiveConceptKey) ? "No active concept was resolved for a topic-bound tutor turn." : "Active concept is present.",
                "Cevabı aktif kavrama bağla; kavram yoksa önce kısa teşhis yap.",
                critical: string.IsNullOrWhiteSpace(turn.ActiveConceptKey) && turn.TopicId.HasValue),

            Score(
                "misconception_repair",
                MisconceptionRepairScore(turn, answer),
                "Recent mistakes and remediation need checked.",
                "Yanlış kavrayış varsa küçük karşı örnek ve düzeltme adımı ekle."),

            Score(
                "micro_check",
                MicroCheckScore(plan, reflection, answer),
                "Micro-check presence checked for remediation/guided practice turns.",
                "Remediation ve guided practice sonunda kısa kontrol sorusu sor."),

            Score(
                "grounding_discipline",
                GroundingScore(turn, reflection, answer),
                "Source claim and citation discipline checked.",
                "Kaynak yoksa kaynak iddiası kurma; kaynak varsa citation kullan.",
                critical: SourceClaimWithoutSource(turn, reflection, answer)),

            Score(
                "tool_artifact_fit",
                ToolArtifactScore(plannedArtifacts, producedArtifacts, readyTools, answer),
                "Planned tools and artifacts checked for teaching use.",
                "Planlanan artifact/tool varsa cevaba veya metadata'ya öğretim amaçlı bağla."),

            Score(
                "clarity_load",
                ClarityScore(turn, answer),
                "Answer length, structure and cognitive-load fit checked.",
                "Yanıtı kısa bloklara böl; öğrencinin seviyesine göre yükü azalt."),

            Score(
                "safety_integrity",
                SafetyIntegrityScore(turn, answer),
                "Mastery/style overclaim and fake evidence checked.",
                "Kanıt düşükken öğrendiğini veya kalıcı öğrenme stilini iddia etme.",
                critical: OverclaimsLearning(turn, answer))
        };

        return scores;
    }

    private static TutorPedagogyRubricScoreDto Score(
        string key,
        decimal score,
        string evidence,
        string recommendation,
        bool critical = false)
    {
        score = Math.Clamp(score, 0m, 1m);
        var severity = critical ? "critical" : score < 0.60m ? "warning" : "info";
        return new TutorPedagogyRubricScoreDto(key, score, severity, critical, evidence, recommendation);
    }

    private static decimal ScaffoldScore(TutorTurnStateDto turn, TutorActionPlanDto plan, TutorReflectionUpdateDto? reflection, string answer)
    {
        var hintFirst = string.Equals(plan.DirectAnswerPolicy, "hint_first_then_scaffold", StringComparison.OrdinalIgnoreCase);
        if (!hintFirst && !turn.DirectAnswerRisk) return 0.90m;
        if (reflection?.DirectAnswerRiskHandled == true) return 0.85m;
        return ContainsAny(answer, "ipucu", "önce", "adım", "birlikte", "deneyelim") ? 0.78m : 0.25m;
    }

    private static decimal LearnerAdaptationScore(TutorTurnStateDto turn, string answer)
    {
        var score = 0.55m;
        if (turn.MasteryProbability.HasValue || turn.Confidence.HasValue) score += 0.12m;
        if (!string.IsNullOrWhiteSpace(turn.AffectiveState) && turn.AffectiveState != "neutral") score += 0.12m;
        if (turn.CognitiveLoad is "high" or "low") score += ContainsAny(answer, "kısa", "adım", "yavaş", "parça") ? 0.12m : -0.10m;
        if (turn.StyleMode == "visual" && ContainsAny(answer, "diyagram", "şema", "görsel", "graf")) score += 0.09m;
        return Math.Clamp(score, 0m, 1m);
    }

    private static decimal MisconceptionRepairScore(TutorTurnStateDto turn, string answer)
    {
        if (turn.RecentMistakes.Count == 0 && turn.RemediationNeed is not "high" and not "medium") return 0.85m;
        return ContainsAny(answer, "yanlış", "karıştır", "düzelt", "hata", "karşı örnek", "örnek") ? 0.82m : 0.45m;
    }

    private static decimal MicroCheckScore(TutorActionPlanDto plan, TutorReflectionUpdateDto? reflection, string answer)
    {
        var needsCheck = plan.TeachingMode is "remediate" or "guided_practice" or "diagnose";
        if (!needsCheck) return 0.85m;
        if (reflection?.MicroCheckAsked == true) return 0.90m;
        return answer.Contains('?') || ContainsAny(answer, "kontrol", "özetler misin", "dener misin") ? 0.80m : 0.40m;
    }

    private static decimal GroundingScore(TutorTurnStateDto turn, TutorReflectionUpdateDto? reflection, string answer)
    {
        if (SourceClaimWithoutSource(turn, reflection, answer)) return 0.10m;
        if (turn.SourceEvidenceCount > 0 && ContainsAny(answer, "[doc:", "[wiki:", "kaynak", "belge")) return 0.92m;
        return turn.SourceEvidenceCount > 0 ? 0.72m : 0.88m;
    }

    private static decimal ToolArtifactScore(int plannedArtifacts, int producedArtifacts, int readyTools, string answer)
    {
        if (plannedArtifacts == 0 && readyTools == 0) return 0.82m;
        if (producedArtifacts > 0 || ContainsAny(answer, "```mermaid", "|---", "tablo", "diyagram", "örnek")) return 0.88m;
        return 0.55m;
    }

    private static decimal ClarityScore(TutorTurnStateDto turn, string answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return 0m;
        var wordCount = answer.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var structured = ContainsAny(answer, "\n-", "\n1.", "###", "|---", "```mermaid");
        var score = wordCount > 650 && turn.CognitiveLoad == "high" ? 0.45m : 0.75m;
        if (structured) score += 0.12m;
        if (ContainsAny(answer, "şimdi", "önce", "sonra", "kısaca")) score += 0.08m;
        return Math.Clamp(score, 0m, 1m);
    }

    private static decimal SafetyIntegrityScore(TutorTurnStateDto turn, string answer)
        => OverclaimsLearning(turn, answer) ? 0.15m : 0.90m;

    private static bool SourceClaimWithoutSource(TutorTurnStateDto turn, TutorReflectionUpdateDto? reflection, string answer)
        => reflection?.SourceClaimWithoutSource == true ||
           (turn.SourceEvidenceCount == 0 && ContainsAny(answer, "kaynağa göre", "kaynaklara göre", "belgeye göre", "dokümana göre", "wikiye göre"));

    private static bool OverclaimsLearning(TutorTurnStateDto turn, string answer)
    {
        var lowEvidence = !turn.Confidence.HasValue || turn.Confidence < 0.60m;
        return lowEvidence && ContainsAny(answer, "artık öğrendin", "bunu öğrendin", "tam biliyorsun", "görsel öğreniyorsun", "sen görsel öğrenen");
    }

    private static bool ContainsAny(string text, params string[] needles)
        => needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string text) => (text ?? string.Empty).Trim().ToLowerInvariant();
}

public sealed class TutorPedagogyQualityGate : ITutorPedagogyQualityGate
{
    public bool RequiresRepair(TutorPedagogyEvaluationRunDto evaluation)
        => evaluation.HasCriticalViolation || evaluation.OverallScore < 0.60m;

    public string BuildRepairPrompt(
        TutorPedagogyEvaluationRunDto evaluation,
        TutorTurnStateDto turnState,
        TutorActionPlanDto actionPlan,
        string assistantAnswer)
    {
        var warnings = string.Join("\n", evaluation.RubricScores
            .Where(s => s.IsCritical || s.Score < 0.60m)
            .Select(s => $"- {s.RubricKey}: {s.Recommendation}"));

        return $"""
            Önceki Tutor cevabı pedagojik kalite kapısından geçmedi.
            Aynı soruya daha iyi bir öğretmen cevabı yaz.

            Kurallar:
            - teachingMode: {actionPlan.TeachingMode}
            - directAnswerPolicy: {actionPlan.DirectAnswerPolicy}
            - activeConceptKey: {turnState.ActiveConceptKey}
            - confidence: {turnState.Confidence}
            - masteryProbability: {turnState.MasteryProbability}
            - groundingStatus: {turnState.GroundingStatus}
            - sourceEvidenceCount: {turnState.SourceEvidenceCount}
            - Kaynak yoksa kaynak iddiası kurma.
            - Düşük mastery veya hint-first durumda önce ipucu ve küçük adım ver.
            - Sonda kısa bir kontrol sorusu sor.

            Düzeltilecek noktalar:
            {warnings}

            Önceki cevap:
            {assistantAnswer}
            """;
    }
}

public sealed class TutorPedagogyFeedbackService : ITutorPedagogyFeedbackService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OrkaDbContext _db;
    private readonly IRedisMemoryService _redis;
    private readonly ITutorWorkingMemoryService _workingMemory;
    private readonly ILearningEventSchemaService _eventSchema;

    public TutorPedagogyFeedbackService(
        OrkaDbContext db,
        IRedisMemoryService redis,
        ITutorWorkingMemoryService workingMemory,
        ILearningEventSchemaService eventSchema)
    {
        _db = db;
        _redis = redis;
        _workingMemory = workingMemory;
        _eventSchema = eventSchema;
    }

    public async Task<TutorMemoryPatchDto?> WriteFeedbackPatchAsync(
        TutorPedagogyEvaluationRunDto evaluation,
        CancellationToken ct = default)
    {
        if (evaluation.Status == "healthy") return null;

        var feedback = BuildFeedback(evaluation);
        var expiresAt = DateTime.UtcNow.AddHours(24);
        _db.TutorPedagogyFeedbackPatches.Add(new TutorPedagogyFeedbackPatch
        {
            Id = Guid.NewGuid(),
            UserId = evaluation.UserId,
            TopicId = evaluation.TopicId,
            SessionId = evaluation.SessionId,
            TutorPedagogyEvaluationRunId = evaluation.Id,
            Feedback = feedback,
            PatchJson = JsonSerializer.Serialize(new
            {
                schemaVersion = "orka.tutor-pedagogy-feedback.v1",
                evaluation.Id,
                evaluation.Status,
                evaluation.OverallScore,
                warnings = evaluation.RubricScores.Where(s => s.IsCritical || s.Score < 0.60m).Select(s => s.RubricKey).ToArray()
            }, JsonOptions),
            ExpiresAt = expiresAt
        });

        var patch = await _workingMemory.ApplyPatchAsync(
            evaluation.UserId,
            evaluation.TopicId,
            evaluation.SessionId,
            "pedagogy_feedback",
            new { evaluation.Id, evaluation.Status, feedback },
            ct);

        var learningEvent = new LearningEvent
        {
            Id = Guid.NewGuid(),
            UserId = evaluation.UserId,
            TopicId = evaluation.TopicId,
            SessionId = evaluation.SessionId,
            EventType = "tutor.feedback.patch.created",
            Actor = "system",
            Verb = "created",
            ObjectType = "tutor_pedagogy_feedback_patch",
            ObjectId = evaluation.Id.ToString(),
            IsPositive = false,
            PayloadJson = JsonSerializer.Serialize(new
            {
                schemaVersion = "orka.learning-event.v2",
                evaluationRunId = evaluation.Id,
                evaluation.Status,
                evaluation.OverallScore
            }, JsonOptions),
            OccurredAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _db.LearningEvents.Add(learningEvent);

        var key = $"orka:v3:tutor-pedagogy-feedback:{evaluation.UserId:N}:{(evaluation.TopicId.HasValue ? evaluation.TopicId.Value.ToString("N") : "global")}";
        await _redis.SetJsonAsync(key, JsonSerializer.Serialize(new
        {
            schemaVersion = "orka.tutor-pedagogy-feedback.v1",
            evaluationRunId = evaluation.Id,
            evaluation.Status,
            evaluation.OverallScore,
            feedback
        }, JsonOptions), TimeSpan.FromHours(24));

        await _db.SaveChangesAsync(ct);
        await _eventSchema.ValidateAndLogAsync(learningEvent, ct);

        return patch;
    }

    private static string BuildFeedback(TutorPedagogyEvaluationRunDto evaluation)
    {
        var fixes = evaluation.RubricScores
            .Where(s => s.IsCritical || s.Score < 0.60m)
            .Take(4)
            .Select(s => $"{s.RubricKey}: {s.Recommendation}");
        return $"Son Tutor cevabı {evaluation.Status} durumda. Bir sonraki cevapta şunları düzelt: {string.Join(" | ", fixes)}";
    }
}

public sealed class TutorPedagogyEvaluationService : ITutorPedagogyEvaluationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OrkaDbContext _db;
    private readonly ITutorPedagogyRubricService _rubrics;
    private readonly ITutorPedagogyFeedbackService _feedback;
    private readonly ILearningEventSchemaService _eventSchema;
    private readonly ITutorWorkingMemoryService _workingMemory;
    private readonly IAIAgentFactory _factory;
    private readonly IConfiguration _config;
    private readonly ILogger<TutorPedagogyEvaluationService> _logger;

    public TutorPedagogyEvaluationService(
        OrkaDbContext db,
        ITutorPedagogyRubricService rubrics,
        ITutorPedagogyFeedbackService feedback,
        ILearningEventSchemaService eventSchema,
        ITutorWorkingMemoryService workingMemory,
        IAIAgentFactory factory,
        IConfiguration config,
        ILogger<TutorPedagogyEvaluationService> logger)
    {
        _db = db;
        _rubrics = rubrics;
        _feedback = feedback;
        _eventSchema = eventSchema;
        _workingMemory = workingMemory;
        _factory = factory;
        _config = config;
        _logger = logger;
    }

    public async Task<TutorPedagogyEvaluationRunDto> EvaluateAsync(
        TutorPedagogyEvaluationRequestDto request,
        CancellationToken ct = default)
    {
        var scores = _rubrics.EvaluateDeterministic(request).ToList();
        var llmUsed = false;
        if (request.AllowLlmJudge && _config.GetValue<bool>("TutorPedagogy:EnableLlmJudge"))
        {
            var judge = await TryJudgeAsync(request, ct);
            if (judge != null)
            {
                llmUsed = true;
                scores.Add(judge);
            }
        }

        var criticalCount = scores.Count(s => s.IsCritical);
        var warningCount = scores.Count(s => !s.IsCritical && s.Score < 0.80m);
        var average = scores.Count == 0 ? 0m : Math.Round(scores.Average(s => s.Score), 4);
        var status = criticalCount > 0 || average < 0.60m
            ? "degraded"
            : average < 0.80m || warningCount > 0
                ? "watch"
                : "healthy";

        var run = new TutorPedagogyEvaluationRun
        {
            Id = Guid.NewGuid(),
            UserId = request.TurnState.UserId,
            TopicId = request.TurnState.TopicId,
            SessionId = request.TurnState.SessionId,
            TutorTurnStateId = request.TurnState.Id,
            TutorActionTraceId = request.ActionPlan.Id,
            TutorReflectionUpdateId = request.Reflection?.Id,
            Status = status,
            OverallScore = average,
            HasCriticalViolation = criticalCount > 0,
            WarningCount = warningCount,
            CriticalViolationCount = criticalCount,
            LlmJudgeUsed = llmUsed,
            Summary = BuildSummary(status, average, criticalCount, warningCount),
            Recommendation = BuildRecommendation(scores),
            RunJson = JsonSerializer.Serialize(new
            {
                schemaVersion = "orka.tutor-pedagogy-evaluation.v1",
                request.TurnState.ActiveConceptKey,
                request.ActionPlan.TeachingMode,
                request.ActionPlan.DirectAnswerPolicy,
                request.ActionPlan.GroundingPolicy,
                request.TurnState.MasteryProbability,
                request.TurnState.Confidence
            }, JsonOptions),
            CreatedAt = DateTime.UtcNow
        };

        _db.TutorPedagogyEvaluationRuns.Add(run);
        _db.TutorPedagogyEvaluationItems.Add(new TutorPedagogyEvaluationItem
        {
            Id = Guid.NewGuid(),
            EvaluationRunId = run.Id,
            UserId = run.UserId,
            TopicId = run.TopicId,
            SessionId = run.SessionId,
            TutorTurnStateId = run.TutorTurnStateId,
            TutorActionTraceId = run.TutorActionTraceId,
            UserMessage = request.TurnState.UserMessage,
            AssistantAnswer = request.AssistantAnswer,
            TeachingMode = request.ActionPlan.TeachingMode,
            DirectAnswerPolicy = request.ActionPlan.DirectAnswerPolicy,
            GroundingPolicy = request.ActionPlan.GroundingPolicy,
            ActiveConceptKey = request.TurnState.ActiveConceptKey,
            ItemJson = JsonSerializer.Serialize(new
            {
                schemaVersion = "orka.tutor-pedagogy-item.v1",
                toolCount = request.ToolCalls.Count,
                artifactCount = request.Artifacts.Count,
                sourceEvidenceCount = request.TurnState.SourceEvidenceCount
            }, JsonOptions),
            CreatedAt = DateTime.UtcNow
        });

        foreach (var score in scores)
        {
            _db.TutorPedagogyRubricScores.Add(ToEntity(run, score));
        }

        var learningEvent = new LearningEvent
        {
            Id = Guid.NewGuid(),
            UserId = run.UserId,
            TopicId = run.TopicId,
            SessionId = run.SessionId,
            EventType = "tutor.pedagogy.evaluated",
            Actor = "system",
            Verb = "evaluated",
            ObjectType = "tutor_pedagogy_evaluation",
            ObjectId = run.Id.ToString(),
            ConceptKey = request.TurnState.ActiveConceptKey,
            IsPositive = status == "healthy",
            PayloadJson = JsonSerializer.Serialize(new
            {
                schemaVersion = "orka.learning-event.v2",
                evaluationRunId = run.Id,
                status,
                overallScore = average,
                criticalViolationCount = criticalCount,
                warningCount
            }, JsonOptions),
            OccurredAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _db.LearningEvents.Add(learningEvent);

        LearningEvent? violationEvent = null;
        if (criticalCount > 0)
        {
            violationEvent = new LearningEvent
            {
                Id = Guid.NewGuid(),
                UserId = run.UserId,
                TopicId = run.TopicId,
                SessionId = run.SessionId,
                EventType = "tutor.pedagogy.violation.detected",
                Actor = "system",
                Verb = "detected",
                ObjectType = "tutor_pedagogy_evaluation",
                ObjectId = run.Id.ToString(),
                ConceptKey = request.TurnState.ActiveConceptKey,
                IsPositive = false,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    schemaVersion = "orka.learning-event.v2",
                    evaluationRunId = run.Id,
                    criticalRubrics = scores.Where(s => s.IsCritical).Select(s => s.RubricKey).ToArray()
                }, JsonOptions),
                OccurredAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            _db.LearningEvents.Add(violationEvent);
        }

        await _db.SaveChangesAsync(ct);
        await _eventSchema.ValidateAndLogAsync(learningEvent, ct);
        if (violationEvent != null)
        {
            await _eventSchema.ValidateAndLogAsync(violationEvent, ct);
        }

        var dto = ToDto(run, scores);
        if (status != "healthy")
        {
            await _feedback.WriteFeedbackPatchAsync(dto, ct);
            await _db.SaveChangesAsync(ct);
        }

        if (run.SessionId.HasValue)
        {
            await _workingMemory.RecordStreamEventAsync(run.SessionId.Value, "tutor.pedagogy_evaluation.ready", new Dictionary<string, string>
            {
                ["evaluationRunId"] = run.Id.ToString(),
                ["status"] = status,
                ["overallScore"] = average.ToString("0.####"),
                ["criticalViolationCount"] = criticalCount.ToString()
            }, ct);
        }

        return dto;
    }

    public async Task<TutorPedagogyEvaluationRunDto?> GetRunAsync(Guid userId, Guid runId, CancellationToken ct = default)
    {
        var run = await _db.TutorPedagogyEvaluationRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId && r.UserId == userId, ct);
        if (run == null) return null;
        var scores = await _db.TutorPedagogyRubricScores.AsNoTracking()
            .Where(s => s.EvaluationRunId == run.Id)
            .OrderBy(s => s.RubricKey)
            .Select(s => new TutorPedagogyRubricScoreDto(s.RubricKey, s.Score, s.Severity, s.IsCritical, s.Evidence, s.Recommendation))
            .ToListAsync(ct);
        return ToDto(run, scores);
    }

    public async Task<TutorPedagogyTopicSummaryDto> GetTopicSummaryAsync(Guid userId, Guid? topicId, CancellationToken ct = default)
    {
        var runs = await _db.TutorPedagogyEvaluationRuns.AsNoTracking()
            .Where(r => r.UserId == userId && (!topicId.HasValue || r.TopicId == topicId.Value))
            .OrderByDescending(r => r.CreatedAt)
            .Take(12)
            .ToListAsync(ct);

        var runIds = runs.Select(r => r.Id).ToArray();
        var scores = await _db.TutorPedagogyRubricScores.AsNoTracking()
            .Where(s => runIds.Contains(s.EvaluationRunId))
            .ToListAsync(ct);

        var recent = runs.Select(r => ToDto(r, scores
            .Where(s => s.EvaluationRunId == r.Id)
            .Select(s => new TutorPedagogyRubricScoreDto(s.RubricKey, s.Score, s.Severity, s.IsCritical, s.Evidence, s.Recommendation))
            .ToList())).ToArray();

        var avg = runs.Count == 0 ? 0m : Math.Round(runs.Average(r => r.OverallScore), 4);
        var critical = runs.Sum(r => r.CriticalViolationCount);
        var status = runs.Count == 0 ? "unknown" : critical > 0 || avg < 0.60m ? "degraded" : avg < 0.80m ? "watch" : "healthy";
        return new TutorPedagogyTopicSummaryDto
        {
            UserId = userId,
            TopicId = topicId,
            Status = status,
            AverageScore = avg,
            RunCount = runs.Count,
            CriticalViolationCount = critical,
            RecentRuns = recent
        };
    }

    public async Task<TutorPedagogyEvaluationRunDto?> EvaluateRecentAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId = null,
        CancellationToken ct = default)
    {
        var action = await _db.TutorActionTraces.AsNoTracking()
            .Where(a => a.UserId == userId && (!topicId.HasValue || a.TopicId == topicId) && (!sessionId.HasValue || a.SessionId == sessionId))
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (action == null || !action.TutorTurnStateId.HasValue) return null;

        var turnEntity = await _db.TutorTurnStates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == action.TutorTurnStateId.Value && t.UserId == userId, ct);
        if (turnEntity == null) return null;

        var turn = JsonSerializer.Deserialize<TutorTurnStateDto>(turnEntity.StateJson, JsonOptions);
        if (turn == null) return null;

        var answer = await _db.Messages.AsNoTracking()
            .Where(m => m.SessionId == action.SessionId && m.Role == "assistant")
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => m.Content)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        var reflection = await _db.TutorReflectionUpdates.AsNoTracking()
            .Where(r => r.TutorActionTraceId == action.Id)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new TutorReflectionUpdateDto
            {
                Id = r.Id,
                TutorActionTraceId = r.TutorActionTraceId,
                TutorTurnStateId = r.TutorTurnStateId,
                PolicyApplied = r.PolicyApplied,
                SourceClaimWithoutSource = r.SourceClaimWithoutSource,
                DirectAnswerRiskHandled = r.DirectAnswerRiskHandled,
                ArtifactRendered = r.ArtifactRendered,
                MicroCheckAsked = r.MicroCheckAsked,
                CreatedAt = r.CreatedAt
            })
            .FirstOrDefaultAsync(ct);

        return await EvaluateAsync(new TutorPedagogyEvaluationRequestDto
        {
            TurnState = turn,
            ActionPlan = ToPlan(action),
            Reflection = reflection,
            AssistantAnswer = answer,
            AllowLlmJudge = true
        }, ct);
    }

    private async Task<TutorPedagogyRubricScoreDto?> TryJudgeAsync(TutorPedagogyEvaluationRequestDto request, CancellationToken ct)
    {
        try
        {
            var prompt = $$"""
                Evaluate the tutor answer as pedagogy, not just correctness.
                Return only JSON: {"score":0.0,"evidence":"short","recommendation":"short"}
                Check: Socratic guidance, scaffolding, learner adaptation, cognitive load, and whether it avoids answer-dumping.

                User: {{request.TurnState.UserMessage}}
                TeachingMode: {{request.ActionPlan.TeachingMode}}
                DirectAnswerPolicy: {{request.ActionPlan.DirectAnswerPolicy}}
                LearnerState: {{request.TurnState.LearnerState}}
                Answer:
                {{request.AssistantAnswer}}
                """;
            var raw = await _factory.CompleteChatAsync(AgentRole.Evaluator, prompt, "Evaluate.", ct);
            raw = raw.Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("```", string.Empty).Trim();
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var score = root.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number ? (decimal)s.GetDouble() : 0.70m;
            var evidence = root.TryGetProperty("evidence", out var e) ? e.GetString() ?? "LLM judge completed." : "LLM judge completed.";
            var recommendation = root.TryGetProperty("recommendation", out var r) ? r.GetString() ?? string.Empty : string.Empty;
            score = Math.Clamp(score, 0m, 1m);
            return new TutorPedagogyRubricScoreDto("llm_pedagogy_judge", score, score < 0.60m ? "warning" : "info", false, evidence, recommendation);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[TutorPedagogy] Optional LLM judge skipped.");
            return null;
        }
    }

    private static TutorActionPlanDto ToPlan(TutorActionTrace trace) => new()
    {
        Id = trace.Id,
        TutorTurnStateId = trace.TutorTurnStateId ?? Guid.Empty,
        UserId = trace.UserId,
        TopicId = trace.TopicId,
        SessionId = trace.SessionId,
        TeachingMode = trace.TeachingMode,
        ActiveConceptKey = trace.ActiveConceptKey,
        StyleMode = trace.StyleMode,
        DirectAnswerPolicy = trace.DirectAnswerPolicy,
        GroundingPolicy = trace.GroundingPolicy,
        NextCheckPrompt = trace.NextCheckPrompt
    };

    private static TutorPedagogyRubricScore ToEntity(TutorPedagogyEvaluationRun run, TutorPedagogyRubricScoreDto score) => new()
    {
        Id = Guid.NewGuid(),
        EvaluationRunId = run.Id,
        UserId = run.UserId,
        TopicId = run.TopicId,
        TutorActionTraceId = run.TutorActionTraceId,
        RubricKey = score.RubricKey,
        Score = score.Score,
        Severity = score.Severity,
        IsCritical = score.IsCritical,
        Evidence = score.Evidence,
        Recommendation = score.Recommendation,
        CreatedAt = DateTime.UtcNow
    };

    private static TutorPedagogyEvaluationRunDto ToDto(TutorPedagogyEvaluationRun run, IReadOnlyList<TutorPedagogyRubricScoreDto> scores) => new()
    {
        Id = run.Id,
        UserId = run.UserId,
        TopicId = run.TopicId,
        SessionId = run.SessionId,
        TutorTurnStateId = run.TutorTurnStateId,
        TutorActionTraceId = run.TutorActionTraceId,
        TutorReflectionUpdateId = run.TutorReflectionUpdateId,
        Status = run.Status,
        OverallScore = run.OverallScore,
        HasCriticalViolation = run.HasCriticalViolation,
        WarningCount = run.WarningCount,
        CriticalViolationCount = run.CriticalViolationCount,
        LlmJudgeUsed = run.LlmJudgeUsed,
        Summary = run.Summary,
        Recommendation = run.Recommendation,
        RubricScores = scores,
        CreatedAt = run.CreatedAt
    };

    private static string BuildSummary(string status, decimal average, int criticalCount, int warningCount)
        => $"Tutor pedagogy status={status}, score={average:0.##}, critical={criticalCount}, warnings={warningCount}.";

    private static string BuildRecommendation(IEnumerable<TutorPedagogyRubricScoreDto> scores)
    {
        var fixes = scores.Where(s => s.IsCritical || s.Score < 0.60m).Take(3).Select(s => s.Recommendation).ToArray();
        return fixes.Length == 0 ? "Pedagojik davranış korunabilir." : string.Join(" ", fixes);
    }
}

public sealed class TutorGoldenScenarioService : ITutorGoldenScenarioService
{
    public IReadOnlyList<TutorGoldenScenarioDto> GetCanonicalScenarios() =>
    [
        new("low_mastery_direct_answer", "Düşük mastery direkt cevap", "general", "Direkt cevabı ver, anlamadım.", "remediate", "Hint-first scaffold and micro-check.", ["scaffolding_quality", "micro_check"]),
        new("high_mastery_challenge", "Yüksek mastery challenge", "general", "Bunu biliyorum, zorlaştır.", "challenge", "Concise challenge with reasoning check.", ["policy_alignment", "micro_check"]),
        new("confused_affective", "Kafası karışmış öğrenci", "general", "Anlamadım, çok karıştı.", "remediate", "Calm step-by-step explanation.", ["learner_adaptation", "clarity_load"]),
        new("source_with_citation", "Kaynaklı cevap", "source", "Belgeye göre açıklar mısın?", "source_grounded_answer", "Cited source-grounded answer.", ["grounding_discipline"]),
        new("source_missing", "Kaynak yok", "source", "Kaynağa göre anlat.", "explain", "No source claim without evidence.", ["grounding_discipline"]),
        new("ide_error", "IDE hata öğretimi", "programming", "Bu hata neden oluyor?", "code_lab", "Explain error concept and give small task.", ["concept_focus", "micro_check"]),
        new("visual_request", "Görsel anlatım", "general", "Şema çizerek anlat.", "visualize", "Use diagram/artifact.", ["tool_artifact_fit"]),
        new("kpss_paragraph", "KPSS paragraf stratejisi", "exam", "Paragraf sorusunun cevabını ver.", "guided_practice", "Teach reasoning strategy instead of answer dump.", ["scaffolding_quality"]),
        new("history_timeline", "Selçuklu zaman çizgisi", "history", "Selçuklu'yu sırayla anlat.", "visualize", "Timeline with concept focus.", ["concept_focus", "tool_artifact_fit"]),
        new("diagnostic_skip", "Diagnostic skip güvenliği", "general", "Testi atla, zayıfım de.", "diagnose", "No fake weakness or mastery.", ["safety_integrity"])
    ];
}
