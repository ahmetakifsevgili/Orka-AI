using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orka.Core.Constants;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/daily-challenge")]
public class DailyChallengeController : ControllerBase
{
    private readonly OrkaDbContext _db;
    private readonly ILearningSignalService _signals;

    public DailyChallengeController(OrkaDbContext db, ILearningSignalService signals)
    {
        _db = db;
        _signals = signals;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] Guid? topicId)
    {
        var userId = GetUserId();
        var weakSkill = topicId.HasValue
            ? await _db.StudyRecommendations
                .AsNoTracking()
                .Where(r => r.UserId == userId && r.TopicId == topicId.Value && !r.IsDone)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => r.SkillTag ?? r.Title)
                .FirstOrDefaultAsync(HttpContext.RequestAborted)
            : null;

        weakSkill ??= topicId.HasValue
            ? await _db.QuizAttempts
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.TopicId == topicId.Value && !a.IsCorrect)
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => a.SkillTag ?? a.TopicPath ?? "genel tekrar")
                .FirstOrDefaultAsync(HttpContext.RequestAborted)
            : null;

        weakSkill = string.IsNullOrWhiteSpace(weakSkill) ? "genel tekrar" : weakSkill.Trim();
        var challengeId = BuildChallengeId(userId, topicId, weakSkill);

        await _signals.RecordSignalAsync(
            userId,
            topicId,
            sessionId: null,
            signalType: LearningSignalTypes.DailyChallengeAssigned,
            skillTag: weakSkill,
            topicPath: weakSkill,
            payloadJson: JsonSerializer.Serialize(new { challengeId, weakSkill }),
            ct: HttpContext.RequestAborted);

        return Ok(new
        {
            id = challengeId,
            topicId,
            skillTag = weakSkill,
            prompt = $"{weakSkill} icin 2 dakikalik retrieval practice yap: once tanimla, sonra mini ornek coz.",
            source = weakSkill == "genel tekrar" ? "fallback" : "weak-skill"
        });
    }

    [HttpPost("{challengeId}/submit")]
    public async Task<IActionResult> Submit(string challengeId, [FromBody] DailyChallengeSubmitRequest request)
    {
        if (string.IsNullOrWhiteSpace(challengeId))
            return BadRequest(new { message = "challengeId zorunlu." });

        var userId = GetUserId();
        var eventKey = $"daily:{DateTime.UtcNow:yyyyMMdd}:{challengeId}";
        var alreadyAwarded = await _db.LearningSignals
            .AsNoTracking()
            .AnyAsync(s =>
                s.UserId == userId &&
                s.SignalType == LearningSignalTypes.DailyChallengeCompleted &&
                s.PayloadJson != null &&
                s.PayloadJson.Contains(eventKey),
                HttpContext.RequestAborted);

        var xpAwarded = 0;
        if (!alreadyAwarded)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, HttpContext.RequestAborted);
            if (user != null)
            {
                xpAwarded = Math.Clamp(request.Quality, 1, 5) * 5;
                user.TotalXP += xpAwarded;
                var today = DateTime.UtcNow.Date;
                if (user.LastActiveDate?.Date == today.AddDays(-1)) user.CurrentStreak += 1;
                else if (user.LastActiveDate?.Date != today) user.CurrentStreak = 1;
                user.LastActiveDate = DateTime.UtcNow;
            }
        }

        await _signals.RecordSignalAsync(
            userId,
            request.TopicId,
            sessionId: null,
            signalType: LearningSignalTypes.DailyChallengeCompleted,
            skillTag: request.SkillTag,
            topicPath: request.SkillTag,
            score: Math.Clamp(request.Quality, 0, 5) * 20,
            isPositive: request.Quality >= 3,
            payloadJson: JsonSerializer.Serialize(new { eventKey, challengeId, request.Quality, request.Answer }),
            ct: HttpContext.RequestAborted);

        await _db.SaveChangesAsync(HttpContext.RequestAborted);
        var totalXp = await _db.Users.Where(u => u.Id == userId).Select(u => u.TotalXP).FirstOrDefaultAsync(HttpContext.RequestAborted);

        return Ok(new
        {
            accepted = true,
            challengeId,
            xpAwarded,
            duplicate = alreadyAwarded,
            totalXP = totalXp
        });
    }

    private static string BuildChallengeId(Guid userId, Guid? topicId, string weakSkill)
    {
        var raw = $"{userId:N}:{(topicId.HasValue ? topicId.Value.ToString("N") : "none")}:{DateTime.UtcNow:yyyyMMdd}:{weakSkill.ToLowerInvariant()}";
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)))[..16].ToLowerInvariant();
    }
}

public sealed class DailyChallengeSubmitRequest
{
    public Guid? TopicId { get; set; }
    public string? SkillTag { get; set; }
    public string Answer { get; set; } = string.Empty;
    public int Quality { get; set; } = 3;
}
