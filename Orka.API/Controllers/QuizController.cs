using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Infrastructure.Data;
using System.Security.Claims;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class QuizController : ControllerBase
{
    private readonly OrkaDbContext _db;

    public QuizController(OrkaDbContext db)
    {
        _db = db;
    }

    [HttpPost("attempt")]
    public async Task<IActionResult> RecordAttempt([FromBody] RecordQuizAttemptRequest request)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var attempt = new Orka.Core.Entities.QuizAttempt
        {
            Id = Guid.NewGuid(),
            SessionId = request.SessionId ?? Guid.Empty,
            TopicId = request.TopicId ?? Guid.Empty,
            UserId = userId,
            Question = request.Question ?? "",
            UserAnswer = request.SelectedOptionId ?? "",
            IsCorrect = request.IsCorrect,
            Explanation = request.Explanation ?? "",
            CreatedAt = DateTime.UtcNow
        };

        _db.QuizAttempts.Add(attempt);
        await _db.SaveChangesAsync();

        return Ok(new { attempt.Id });
    }

    [HttpGet("history/{topicId}")]
    public async Task<ActionResult<IEnumerable<QuizAttemptDto>>> GetQuizHistory(Guid topicId)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var attempts = await _db.QuizAttempts
            .Where(a => a.TopicId == topicId && a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new QuizAttemptDto
            {
                Id = a.Id,
                Question = a.Question,
                UserAnswer = a.UserAnswer,
                IsCorrect = a.IsCorrect,
                Explanation = a.Explanation,
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

        var dailyProgress = new List<object>();
        foreach (var day in last7Days)
        {
            var nextDay = day.AddDays(1);
            var dayTotal = await _db.QuizAttempts.CountAsync(a => a.UserId == userId && a.CreatedAt >= day && a.CreatedAt < nextDay);
            var dayCorrect = await _db.QuizAttempts.CountAsync(a => a.UserId == userId && a.IsCorrect && a.CreatedAt >= day && a.CreatedAt < nextDay);
            
            dailyProgress.Add(new
            {
                Date = day.ToString("MM/dd"),
                Total = dayTotal,
                Correct = dayCorrect,
                Accuracy = dayTotal > 0 ? Math.Round((double)dayCorrect / dayTotal * 100, 1) : 0
            });
        }

        return Ok(new
        {
            TotalQuizzes = totalAttempts,
            CorrectAnswers = correctAttempts,
            Accuracy = Math.Round(accuracy * 100, 2),
            DailyProgress = dailyProgress
        });
    }
}
