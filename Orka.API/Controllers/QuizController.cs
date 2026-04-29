using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/quiz")]
public class QuizController : ControllerBase
{
    private readonly OrkaDbContext _db;
    private readonly ILearningSignalService _signals;
    private readonly ISummarizerAgent _summarizer;
    private readonly IRedisMemoryService _redis;

    public QuizController(
        OrkaDbContext db,
        ILearningSignalService signals,
        ISummarizerAgent summarizer,
        IRedisMemoryService redis)
    {
        _db = db;
        _signals = signals;
        _summarizer = summarizer;
        _redis = redis;
    }

    [HttpPost("attempt")]
    public async Task<IActionResult> RecordAttempt([FromBody] RecordQuizAttemptRequest request)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            var sessionId = NormalizeGuid(request.SessionId);
            var topicId = NormalizeGuid(request.TopicId);

            if (!topicId.HasValue && sessionId.HasValue)
            {
                topicId = await _db.Sessions
                    .Where(s => s.Id == sessionId.Value && s.UserId == userId)
                    .Select(s => s.TopicId)
                    .FirstOrDefaultAsync();
            }

            var quizRunId = NormalizeGuid(request.QuizRunId);
            if (quizRunId.HasValue && !await _db.QuizRuns.AnyAsync(r => r.Id == quizRunId.Value && r.UserId == userId))
            {
                _db.QuizRuns.Add(new QuizRun
                {
                    Id = quizRunId.Value,
                    UserId = userId,
                    TopicId = topicId,
                    SessionId = sessionId,
                    QuizType = request.MessageId?.Contains("baseline", StringComparison.OrdinalIgnoreCase) == true ? "baseline" : "lesson",
                    Status = "active",
                    MetadataJson = JsonSerializer.Serialize(new { request.MessageId }),
                    CreatedAt = DateTime.UtcNow
                });
            }

            var attempt = new QuizAttempt
            {
                Id = Guid.NewGuid(),
                QuizRunId = quizRunId,
                SessionId = sessionId,
                TopicId = topicId,
                UserId = userId,
                QuestionId = request.QuestionId,
                Question = request.Question ?? "",
                UserAnswer = request.SelectedOptionId ?? "",
                IsCorrect = request.IsCorrect,
                Explanation = request.Explanation ?? "",
                SkillTag = NormalizeText(request.SkillTag),
                TopicPath = NormalizeText(request.TopicPath),
                Difficulty = NormalizeText(request.Difficulty),
                CognitiveType = NormalizeText(request.CognitiveType),
                QuestionHash = string.IsNullOrWhiteSpace(request.QuestionHash)
                    ? ComputeQuestionHash(request.Question ?? "")
                    : request.QuestionHash.Trim(),
                SourceRefsJson = request.SourceRefsJson,
                CreatedAt = DateTime.UtcNow
            };

            _db.QuizAttempts.Add(attempt);
            await _db.SaveChangesAsync();
            await _signals.RecordQuizAnsweredAsync(attempt, HttpContext.RequestAborted);

            if (topicId.HasValue)
            {
                _summarizer.InvalidateNotebookTools(topicId.Value);
                if (!string.IsNullOrWhiteSpace(attempt.QuestionHash))
                    await _redis.RememberQuestionHashesAsync(userId, topicId.Value, [attempt.QuestionHash]);
            }

            return Ok(new
            {
                attempt.Id,
                attempt.QuizRunId,
                attempt.TopicId,
                attempt.SkillTag,
                attempt.QuestionHash
            });
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Quiz sonucu kaydedilemedi." });
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

    private static Guid? NormalizeGuid(Guid? value) =>
        value.HasValue && value.Value != Guid.Empty ? value.Value : null;

    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ComputeQuestionHash(string question)
    {
        var normalized = string.Join(' ', (question ?? string.Empty)
            .ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
