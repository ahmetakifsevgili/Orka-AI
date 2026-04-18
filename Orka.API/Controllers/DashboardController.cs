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
    /// SQL AgentEvaluations + Redis metrics birleşik veri kaynağı.
    /// Yalnızca admin hesapları erişebilir.
    /// </summary>
    [HttpGet("system-health")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetSystemHealth()
    {
        var userId = GetUserId();

        // 1. Token ve Maliyet (SQL Sessions)
        var tokenData = await _dbContext.Sessions
            .Where(s => s.UserId == userId)
            .Select(s => new { s.TotalTokensUsed, s.TotalCostUSD })
            .ToListAsync();

        var totalTokens  = tokenData.Sum(s => s.TotalTokensUsed);
        var totalCostUSD = tokenData.Sum(s => (double)s.TotalCostUSD);

        // 2. Pedagoji Skoru — SkillMasteries tablosu (quiz başarı oranı)
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

        // 4. SQL AgentEvaluations — Ajan başına gerçek kalite puanları
        var sqlEvals = await _dbContext.AgentEvaluations
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(200) // En son 200 kayıt
            .Select(e => new
            {
                e.AgentRole,
                e.EvaluationScore,
                e.EvaluatorFeedback,
                e.CreatedAt
            })
            .ToListAsync();

        // 4a. Ajan başına ortalama kalite puanı (SQL'den)
        var agentQuality = sqlEvals
            .GroupBy(e => e.AgentRole)
            .Select(g => new
            {
                agentRole      = g.Key,
                avgQuality     = Math.Round(g.Average(e => (double)e.EvaluationScore), 2),
                totalEvals     = g.Count(),
                lastEvalAt     = g.Max(e => e.CreatedAt).ToString("O"),
                goldCount      = g.Count(e => e.EvaluationScore >= 9),
                warnCount      = g.Count(e => e.EvaluationScore < 5),
            })
            .ToList();

        // 4b. Son 20 evaluator logu — SQL öncelikli, Redis'e fallback yok (artık SQL'de var)
        var recentSqlLogs = sqlEvals
            .Take(20)
            .Select(e => new
            {
                score      = e.EvaluationScore,
                agentRole  = e.AgentRole,
                feedback   = e.EvaluatorFeedback.Length > 200
                                 ? e.EvaluatorFeedback[..200] + "..."
                                 : e.EvaluatorFeedback,
                recordedAt = e.CreatedAt.ToString("HH:mm:ss"),
                quality    = e.EvaluationScore >= 9 ? "gold"
                           : e.EvaluationScore >= 7 ? "good"
                           : e.EvaluationScore >= 5 ? "ok"
                           : "warn"
            })
            .ToList();

        // 4c. Genel LLMOps ortalaması
        var avgEvaluatorScore = sqlEvals.Count > 0
            ? Math.Round(sqlEvals.Average(e => (double)e.EvaluationScore), 2)
            : 0.0;

        // 5. Redis: Ajan gecikme / hata metrikleri (TTFT + non-stream)
        var agentMetrics = await _redis.GetSystemMetricsAsync();

        // 5b. Provider kullanım dağılımı — HUD Model Mix widget
        var providerUsage = await _redis.GetProviderUsageAsync();

        // 6. Redis + SQL birleşimi: Agent kartlarına SQL kalite verisi ekle
        var agentQualityMap = agentQuality.ToDictionary(a => a.agentRole, a => a);
        var enrichedAgents = agentMetrics.Select(a =>
        {
            agentQualityMap.TryGetValue(a.AgentRole, out var quality);
            return new
            {
                a.AgentRole,
                a.AvgLatencyMs,
                a.TotalCalls,
                a.ErrorCount,
                a.ErrorRatePct,
                a.LastProvider,
                avgQualityScore = quality?.avgQuality ?? 0.0,
                totalEvals      = quality?.totalEvals ?? 0,
                goldCount       = quality?.goldCount ?? 0,
                warnCount       = quality?.warnCount ?? 0,
                status = a.TotalCalls == 0 ? "idle"
                       : a.ErrorRatePct > 50 ? "critical"
                       : a.ErrorRatePct > 20 ? "degraded"
                       : "online",
            };
        });

        return Ok(new
        {
            tokens = new
            {
                total   = totalTokens,
                costUSD = Math.Round(totalCostUSD, 4),
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
                totalEvaluations = sqlEvals.Count,
                recentLogs       = recentSqlLogs,
            },
            agents   = enrichedAgents,
            modelMix = providerUsage,
        });
    }
}

