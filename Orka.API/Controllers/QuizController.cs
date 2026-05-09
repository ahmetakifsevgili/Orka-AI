using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.DTOs.PlanDiagnostic;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using System.Security.Claims;
using System.Text.Json;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/quiz")]
public class QuizController : ControllerBase
{
    private readonly OrkaDbContext _db;
    private readonly IQuizAttemptRecorder _quizRecorder;
    private readonly IDeepPlanAgent _deepPlan;
    private readonly IPlanDiagnosticService _planDiagnostic;
    private readonly IStudyIntentAnalyzer _studyIntentAnalyzer;
    private readonly IAdaptiveAssessmentSessionService _adaptiveAssessment;
    private readonly ILogger<QuizController> _logger;

    public QuizController(
        OrkaDbContext db,
        IQuizAttemptRecorder quizRecorder,
        IDeepPlanAgent deepPlan,
        IPlanDiagnosticService planDiagnostic,
        IStudyIntentAnalyzer studyIntentAnalyzer,
        IAdaptiveAssessmentSessionService adaptiveAssessment,
        ILogger<QuizController> logger)
    {
        _db = db;
        _quizRecorder = quizRecorder;
        _deepPlan = deepPlan;
        _planDiagnostic = planDiagnostic;
        _studyIntentAnalyzer = studyIntentAnalyzer;
        _adaptiveAssessment = adaptiveAssessment;
        _logger = logger;
    }

    [HttpGet("generate")]
    public async Task<IActionResult> Generate([FromQuery] Guid? topicId)
    {
        if (topicId == null) return BadRequest(new { error = "topicId zorunlu." });

        var topic = await _db.Topics.FindAsync(topicId);
        if (topic == null) return NotFound(new { error = "Konu bulunamadı." });

        try
        {
            var rawJson = await _deepPlan.GenerateBaselineQuizAsync(topic.Title);

            // LLM bazen JSON blokları (```json ... ```) içine sarar veya metin ekler, temizleyelim
            var cleaned = rawJson.Trim();
            if (cleaned.Contains("```"))
            {
                var lines = cleaned.Split('\n');
                cleaned = string.Join("\n", lines.Where(l => !l.Trim().StartsWith("```")));
            }

            var s = cleaned.IndexOf('[');
            var e = cleaned.LastIndexOf(']');
            if (s >= 0 && e > s) cleaned = cleaned[s..(e + 1)];

            var questions = System.Text.Json.JsonSerializer.Deserialize<object>(cleaned);

            return Ok(new { topicId, questions });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Quiz üretimi başarısız. TopicId={TopicId}", topicId);
            return StatusCode(500, new { error = "Quiz üretilemedi." });
        }
    }

    [HttpPost("attempt")]
    public async Task<IActionResult> RecordAttempt([FromBody] RecordQuizAttemptRequest request)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            var result = await _quizRecorder.RecordAsync(userId, request, HttpContext.RequestAborted);
            var attempt = result.Attempt;
            var xpResult = result.Xp;

            return Ok(new
            {
                attempt.Id,
                attempt.QuizRunId,
                attempt.TopicId,
                attempt.SkillTag,
                attempt.QuestionHash,
                knowledgeTracingStateId = TryGuid(ExtractMetadata(attempt.SourceRefsJson, "knowledgeTracingStateId")),
                masteryProbability = TryDecimal(ExtractMetadata(attempt.SourceRefsJson, "masteryProbability")),
                itemQualityStatus = ExtractMetadata(attempt.SourceRefsJson, "itemQualityStatus"),
                xp = xpResult is null
                    ? null
                    : new
                    {
                        xpResult.Awarded,
                        xpResult.XpAwarded,
                        xpResult.TotalXP,
                        xpResult.CurrentStreak,
                        Badges = xpResult.NewlyEarnedBadges
                    },
                review = result.Review,
                mistake = result.Mistake
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QuizController] Quiz sonucu kaydedilemedi. UserId={UserId}", userId);
            return StatusCode(500, new { error = "Quiz sonucu kaydedilemedi." });
        }
    }

    [HttpPost("adaptive/start")]
    public async Task<IActionResult> StartAdaptive([FromBody] AdaptiveAssessmentStartRequest request)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            var result = await _adaptiveAssessment.StartAsync(userId, request, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QuizController] Adaptive assessment start failed. UserId={UserId}", userId);
            return StatusCode(500, new { error = "Adaptive practice could not be started." });
        }
    }

    [HttpGet("adaptive/{adaptiveSessionId:guid}/next")]
    public async Task<IActionResult> GetAdaptiveNext(Guid adaptiveSessionId)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            var result = await _adaptiveAssessment.GetNextAsync(userId, adaptiveSessionId, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QuizController] Adaptive assessment next failed. UserId={UserId} SessionId={SessionId}", userId, adaptiveSessionId);
            return StatusCode(500, new { error = "Next adaptive question could not be selected." });
        }
    }

    [HttpPost("adaptive/{adaptiveSessionId:guid}/answer")]
    public async Task<IActionResult> RecordAdaptiveAnswer(Guid adaptiveSessionId, [FromBody] AdaptiveAssessmentAnswerRequest request)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            var result = await _adaptiveAssessment.RecordAnswerAsync(userId, adaptiveSessionId, request, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QuizController] Adaptive assessment answer failed. UserId={UserId} SessionId={SessionId}", userId, adaptiveSessionId);
            return StatusCode(500, new { error = "Adaptive answer could not be recorded." });
        }
    }

    [HttpPost("plan-diagnostic/start")]
    public async Task<IActionResult> StartPlanDiagnostic()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            using var requestDoc = await JsonDocument.ParseAsync(HttpContext.Request.Body, cancellationToken: HttpContext.RequestAborted);
            var request = ParseStartPlanDiagnosticRequest(requestDoc.RootElement);
            var result = await _planDiagnostic.StartAsync(userId, request, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "[QuizController] Plan diagnostic start rejected. UserId={UserId}", userId);
            return BadRequest(new { error = "Plan diagnostic request could not be accepted." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QuizController] Plan diagnostic start failed. UserId={UserId}", userId);
            return StatusCode(500, new { error = "Plan diagnostic could not be started." });
        }
    }

    [HttpPost("plan-diagnostic/intent")]
    public async Task<IActionResult> AnalyzePlanIntent([FromBody] AnalyzeStudyIntentRequest request)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            var result = await _studyIntentAnalyzer.AnalyzeAsync(userId, request, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "[QuizController] Study intent analysis rejected. UserId={UserId}", userId);
            return BadRequest(new { error = "Study intent request is required." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QuizController] Study intent analysis failed. UserId={UserId}", userId);
            return StatusCode(500, new { error = "Study intent could not be analyzed." });
        }
    }

    [HttpPost("plan-diagnostic/{planRequestId:guid}/attempt")]
    public async Task<IActionResult> RecordPlanDiagnosticAttempt(Guid planRequestId, [FromBody] RecordQuizAttemptRequest request)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            var result = await _planDiagnostic.RecordAnswerAsync(userId, planRequestId, request, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QuizController] Plan diagnostic answer failed. UserId={UserId} PlanRequestId={PlanRequestId}", userId, planRequestId);
            return StatusCode(500, new { error = "Plan diagnostic answer could not be recorded." });
        }
    }

    [HttpPost("plan-diagnostic/finalize")]
    public async Task<IActionResult> FinalizePlanDiagnostic([FromBody] FinalizePlanDiagnosticRequest request)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            var result = await _planDiagnostic.FinalizeAsync(userId, request, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QuizController] Plan diagnostic finalize failed. UserId={UserId} PlanRequestId={PlanRequestId}", userId, request.PlanRequestId);
            return StatusCode(500, new { error = "Plan diagnostic could not be finalized." });
        }
    }

    [HttpPost("plan-diagnostic/{planRequestId:guid}/skip")]
    public async Task<IActionResult> SkipPlanDiagnostic(Guid planRequestId)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            var result = await _planDiagnostic.SkipAndGenerateAsync(userId, planRequestId, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QuizController] Plan diagnostic skip failed. UserId={UserId} PlanRequestId={PlanRequestId}", userId, planRequestId);
            return StatusCode(500, new { error = "Plan diagnostic could not be skipped safely." });
        }
    }

    [HttpGet("history/{topicId}")]
    public async Task<ActionResult<IEnumerable<QuizAttemptDto>>> GetQuizHistory(Guid topicId)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var attempts = await _db.QuizAttempts
            .AsNoTracking()
            .Where(a => a.TopicId == topicId && a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new QuizAttemptDto
            {
                Id = a.Id,
                QuizRunId = a.QuizRunId,
                QuestionId = a.QuestionId,
                Question = a.Question,
                UserAnswer = a.UserAnswer,
                IsCorrect = a.IsCorrect,
                Explanation = a.Explanation,
                SkillTag = a.SkillTag,
                AssessmentItemId = a.AssessmentItemId,
                ConceptKey = ExtractMetadata(a.SourceRefsJson, "conceptKey"),
                ConceptTag = ExtractMetadata(a.SourceRefsJson, "conceptTag"),
                CognitiveSkill = ExtractMetadata(a.SourceRefsJson, "cognitiveSkill"),
                MisconceptionTarget = ExtractMetadata(a.SourceRefsJson, "misconceptionTarget"),
                EvidenceExpected = ExtractMetadata(a.SourceRefsJson, "evidenceExpected"),
                ScoringRule = ExtractMetadata(a.SourceRefsJson, "scoringRule"),
                LearningOutcomeIdsJson = ExtractMetadata(a.SourceRefsJson, "learningOutcomeIds"),
                KnowledgeTracingStateId = TryGuid(ExtractMetadata(a.SourceRefsJson, "knowledgeTracingStateId")),
                MasteryProbability = TryDecimal(ExtractMetadata(a.SourceRefsJson, "masteryProbability")),
                ItemQualityStatus = ExtractMetadata(a.SourceRefsJson, "itemQualityStatus"),
                LearningObjective = ExtractMetadata(a.SourceRefsJson, "learningObjective"),
                QuestionType = ExtractMetadata(a.SourceRefsJson, "questionType"),
                MistakeCategory = ExtractMetadata(a.SourceRefsJson, "mistakeCategory"),
                TopicPath = a.TopicPath,
                Difficulty = a.Difficulty,
                CognitiveType = a.CognitiveType,
                QuestionHash = a.QuestionHash,
                SourceRefsJson = a.SourceRefsJson,
                ResponseTimeMs = a.ResponseTimeMs,
                WasSkipped = a.WasSkipped,
                ConfidenceSelfRating = a.ConfidenceSelfRating,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        return Ok(attempts);
    }

    [HttpGet("stats")]
    public async Task<ActionResult> GetGlobalStats()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var totalAttempts = await _db.QuizAttempts.CountAsync(a => a.UserId == userId);
        var correctAttempts = await _db.QuizAttempts.CountAsync(a => a.UserId == userId && a.IsCorrect);

        var accuracy = totalAttempts > 0 ? (double)correctAttempts / totalAttempts : 0;

        // Son 7 günün günlük başarısı
        var last7Days = Enumerable.Range(0, 7)
            .Select(i => DateTime.UtcNow.Date.AddDays(-i))
            .Reverse()
            .ToList();

        var startDate = last7Days.First();
        var endDate = last7Days.Last().AddDays(1);
        var groupedAttempts = await _db.QuizAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.CreatedAt >= startDate && a.CreatedAt < endDate)
            .GroupBy(a => a.CreatedAt.Date)
            .Select(g => new
            {
                Date = g.Key,
                Total = g.Count(),
                Correct = g.Count(a => a.IsCorrect)
            })
            .ToListAsync();

        var byDay = groupedAttempts.ToDictionary(x => x.Date);
        var dailyProgress = last7Days.Select(day =>
        {
            byDay.TryGetValue(day, out var item);
            var dayTotal = item?.Total ?? 0;
            var dayCorrect = item?.Correct ?? 0;
            return new
            {
                Date = day.ToString("MM/dd"),
                Total = dayTotal,
                Correct = dayCorrect,
                Accuracy = dayTotal > 0 ? Math.Round((double)dayCorrect / dayTotal * 100, 1) : 0
            };
        }).ToList<object>();

        return Ok(new
        {
            TotalQuizzes = totalAttempts,
            CorrectAnswers = correctAttempts,
            Accuracy = Math.Round(accuracy * 100, 2),
            DailyProgress = dailyProgress
        });
    }

    private static string? ExtractMetadata(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(key, out var value))
            {
                return value.ValueKind == System.Text.Json.JsonValueKind.String
                    ? value.GetString()
                    : value.GetRawText();
            }

            return null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static Guid? TryGuid(string? value) =>
        Guid.TryParse(value?.Trim('"'), out var id) ? id : null;

    private static decimal? TryDecimal(string? value) =>
        decimal.TryParse(value?.Trim('"'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static StartPlanDiagnosticRequest ParseStartPlanDiagnosticRequest(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Plan diagnostic request body is required.");
        }

        return new StartPlanDiagnosticRequest
        {
            TopicId = ReadGuid(body, "topicId") ?? Guid.Empty,
            SessionId = ReadGuid(body, "sessionId"),
            TopicTitle = ReadString(body, "topicTitle"),
            UserLevel = ReadString(body, "userLevel"),
            IntentRequestId = ReadGuid(body, "intentRequestId"),
            RawStudyRequest = ReadString(body, "rawStudyRequest"),
            ApprovedMainTopic = ReadString(body, "approvedMainTopic"),
            ApprovedFocusArea = ReadString(body, "approvedFocusArea"),
            ApprovedStudyGoal = ReadString(body, "approvedStudyGoal"),
            ApprovedResearchIntent = ReadString(body, "approvedResearchIntent")
        };
    }

    private static string? ReadString(JsonElement body, string camelName)
    {
        if (!TryGetPropertyCaseInsensitive(body, camelName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static Guid? ReadGuid(JsonElement body, string camelName)
    {
        var text = ReadString(body, camelName);
        return Guid.TryParse(text, out var value) ? value : null;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement body, string camelName, out JsonElement value)
    {
        foreach (var property in body.EnumerateObject())
        {
            if (property.Name.Equals(camelName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
