using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using System.Security.Claims;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly OrkaDbContext _dbContext;
    private readonly IRedisMemoryService _redis;

    public DashboardController(OrkaDbContext dbContext, IRedisMemoryService redis)
    {
        _dbContext = dbContext;
        _redis     = redis;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var userId = GetUserId();

        // 1. Gamification (User tablosundan gerçek XP + Streak)
        var user = await _dbContext.Users.FindAsync(userId);
        var totalXP      = user?.TotalXP      ?? 0;
        var currentStreak = user?.CurrentStreak ?? 0;

        // 2. Konu istatistikleri
        var allTopics = await _dbContext.Topics
            .Where(t => t.UserId == userId && t.ParentTopicId == null) // yalnızca parent topic'ler
            .Select(t => new { t.ProgressPercentage, t.IsArchived })
            .ToListAsync();

        var completedTopics  = allTopics.Count(t => t.ProgressPercentage >= 100);
        var activeLearning   = allTopics.Count(t => t.ProgressPercentage > 0 && t.ProgressPercentage < 100);
        var totalTopics      = allTopics.Count;

        // 3. Bölüm ilerlemesi (alt topic dahil tüm konular)
        var sectionData = await _dbContext.Topics
            .Where(t => t.UserId == userId)
            .Select(t => new { t.TotalSections, t.CompletedSections })
            .ToListAsync();

        var totalSections     = sectionData.Sum(t => t.TotalSections);
        var completedSections = sectionData.Sum(t => t.CompletedSections);
        var progressPercentage = totalSections > 0 ? Math.Round((double)completedSections / totalSections * 100, 1) : 0.0;

        // 4. Haftalık Aktivite (Son 7 gün, mesaj bazlı)
        var weekAgo = DateTime.UtcNow.Date.AddDays(-7);
        var recentMessages = await _dbContext.Messages
            .Where(m => m.UserId == userId && m.CreatedAt >= weekAgo)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        var activity = recentMessages
            .GroupBy(m => m.CreatedAt.Date)
            .Select(g => new { date = g.Key, count = g.Count() })
            .ToList();

        // 5. Wiki sayısı
        var wikisCount = await _dbContext.WikiPages.CountAsync(w => w.UserId == userId);

        return Ok(new
        {
            totalXP,
            currentStreak,
            completedTopics,
            activeLearning,
            totalTopics,
            completedSections,
            totalSections,
            progressPercentage,
            wikisCount,
            activity
        });
    }

    [HttpGet("recent-activity")]
    public async Task<IActionResult> GetRecentActivity()
    {
        var userId = GetUserId();
        
        var recentTopics = await _dbContext.Topics
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.LastAccessedAt)
            .Take(5)
            .Select(t => new { t.Id, t.Title, t.Emoji, t.LastAccessedAt })
            .ToListAsync();

        return Ok(recentTopics);
    }

    /// <summary>
    /// Gerçek zamanlı sistem sağlığı — Tek kişilik dev kadrosu için LLMOps İzleme Paneli.
    /// Ajanların latency, hata oranı, token/maliyet ve pedagoji skoru döner.
    /// </summary>
    [HttpGet("system-health")]
    public async Task<IActionResult> GetSystemHealth()
    {
        var userId = GetUserId();

        // 1. Token ve Maliyet (SQL Sessions)
        var tokenData = await _dbContext.Sessions
            .Where(s => s.UserId == userId)
            .Select(s => new { s.TotalTokensUsed, s.TotalCostUSD })
            .ToListAsync();

        var totalTokens = tokenData.Sum(s => s.TotalTokensUsed);
        var totalCostUSD = tokenData.Sum(s => (double)s.TotalCostUSD);

        // 2. Pedagoji Skoru — SkillMasteries üzerinden öğrenci başarı ortalaması
        var masteries = await _dbContext.SkillMasteries
            .Where(sm => sm.UserId == userId)
            .Select(sm => sm.QuizScore)
            .ToListAsync();

        var pedagogyScore = masteries.Count > 0
            ? Math.Round(masteries.Average(), 1)
            : 0.0;

        // 3. Oturum istatistikleri
        var sessionCount = await _dbContext.Sessions
            .CountAsync(s => s.UserId == userId);

        var lastSessionDate = await _dbContext.Sessions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => (DateTime?)s.CreatedAt)
            .FirstOrDefaultAsync();

        // 4. Redis: Ajan gecikme / hata metrikleri (GERÇEK)
        var agentMetrics = await _redis.GetSystemMetricsAsync();

        // 5. Redis: Son Evaluator LLMOps log kayıtları (GERÇEK)
        var evaluatorLogs = await _redis.GetRecentEvaluatorLogsAsync(20);

        // 6. Evaluator Ortalama Puanı (LLMOps kalite skoru)
        var logList = evaluatorLogs.ToList();
        var avgEvaluatorScore = logList.Count > 0
            ? Math.Round(logList.Average(e => (double)e.Score), 2)
            : 0.0;

        return Ok(new
        {
            tokens = new
            {
                total    = totalTokens,
                costUSD  = Math.Round(totalCostUSD, 4),
            },
            pedagogy = new
            {
                score          = pedagogyScore,
                masteredTopics = masteries.Count,
            },
            sessions = new
            {
                total    = sessionCount,
                lastDate = lastSessionDate?.ToString("O"),
            },
            llmops = new
            {
                avgEvaluatorScore,
                recentLogs = logList.Take(20).Select(e => new
                {
                    e.Score,
                    e.Feedback,
                    e.RecordedAt,
                    quality = e.Score >= 9 ? "gold" : e.Score >= 7 ? "good" : e.Score >= 5 ? "ok" : "warn"
                }),
            },
            agents = agentMetrics.Select(a => new
            {
                a.AgentRole,
                a.AvgLatencyMs,
                a.TotalCalls,
                a.ErrorCount,
                a.ErrorRatePct,
                a.LastProvider,
                status = a.TotalCalls == 0 ? "idle"
                       : a.ErrorRatePct > 50 ? "critical"
                       : a.ErrorRatePct > 20 ? "degraded"
                       : "online",
            }),
        });
    }
}
