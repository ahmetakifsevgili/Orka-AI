using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orka.Infrastructure.Data;
using System.Security.Claims;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly OrkaDbContext _dbContext;

    public DashboardController(OrkaDbContext dbContext)
    {
        _dbContext = dbContext;
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
}
