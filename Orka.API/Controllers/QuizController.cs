using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.DTOs.PlanDiagnostic;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using System.Security.Claims;

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
    private readonly ILogger<QuizController> _logger;

    public QuizController(
        OrkaDbContext db,
        IQuizAttemptRecorder quizRecorder,
        IDeepPlanAgent deepPlan,
        IPlanDiagnosticService planDiagnostic,
        ILogger<QuizController> logger)
    {
        _db = db;
        _quizRecorder = quizRecorder;
        _deepPlan = deepPlan;
        _planDiagnostic = planDiagnostic;
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

    [HttpPost("plan-diagnostic/start")]
    public async Task<IActionResult> StartPlanDiagnostic([FromBody] StartPlanDiagnosticRequest request)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            var result = await _planDiagnostic.StartAsync(userId, request, HttpContext.RequestAborted);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[QuizController] Plan diagnostic start failed. UserId={UserId}", userId);
            return StatusCode(500, new { error = "Plan diagnostic could not be started." });
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
                TopicPath = a.TopicPath,
                Difficulty = a.Difficulty,
                CognitiveType = a.CognitiveType,
                QuestionHash = a.QuestionHash,
                SourceRefsJson = a.SourceRefsJson,
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

        // Son 7 gÃ¼nÃ¼n gÃ¼nlÃ¼k baÅŸarÄ±sÄ±
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

}
