using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using System.Linq;

namespace Orka.API.Controllers;

public class UpdateProfileRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
}
public class UpdateSettingsRequest
{
    public string? Theme { get; set; }
    public string? Language { get; set; }
    public string? FontSize { get; set; }
    public bool? QuizReminders { get; set; }
    public bool? WeeklyReport { get; set; }
    public bool? NewContentAlerts { get; set; }
    public bool? SoundsEnabled { get; set; }
}

[Authorize]
[ApiController]
[Route("api/user")]
public class UserController : ControllerBase
{
    private readonly OrkaDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IDataLifecycleService _dataLifecycle;

    public UserController(OrkaDbContext dbContext, IConfiguration configuration, IDataLifecycleService dataLifecycle)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _dataLifecycle = dataLifecycle;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUser()
    {
        var userId = GetUserId();
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null) return NotFound();

        var dailyLimit = user.Plan == UserPlan.Pro
            ? _configuration.GetValue<int>("Limits:ProUserDailyMessages", 500)
            : _configuration.GetValue<int>("Limits:FreeUserDailyMessages", 50);

        return Ok(new
        {
            id = user.Id,
            firstName = user.FirstName,
            lastName = user.LastName,
            email = user.Email,
            plan = user.Plan.ToString(),
            isAdmin = user.IsAdmin,
            storageUsedMB = user.StorageUsedMB,
            storageLimitMB = user.StorageLimitMB,
            dailyMessageCount = user.DailyMessageCount,
            dailyLimit,
            dailyResetAt = user.DailyMessageResetAt,
            createdAt = user.CreatedAt,
            settings = new 
            {
                theme = user.Theme,
                language = user.Language,
                fontSize = user.FontSize,
                quizReminders = user.QuizReminders,
                weeklyReport = user.WeeklyReport,
                newContentAlerts = user.NewContentAlerts,
                soundsEnabled = user.SoundsEnabled
            }
        });
    }

    [HttpPatch("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest req)
    {
        var userId = GetUserId();
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.FirstName)) user.FirstName = req.FirstName;
        if (!string.IsNullOrWhiteSpace(req.LastName)) user.LastName = req.LastName;
        
        // E-mail değişikliği dikkat gerektirir ancak basit tutuyoruz:
        if (!string.IsNullOrWhiteSpace(req.Email)) 
        {
            var exists = await _dbContext.Users.AnyAsync(u => u.Email == req.Email && u.Id != userId);
            if (exists) return BadRequest("Bu e-posta zaten kullanımda.");
            user.Email = req.Email;
        }

        await _dbContext.SaveChangesAsync();
        return Ok(new { Message = "Profil başarıyla güncellendi." });
    }

    [HttpPatch("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateSettingsRequest req)
    {
        var userId = GetUserId();
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null) return NotFound();

        if (req.Theme != null) user.Theme = req.Theme;
        if (req.Language != null) user.Language = req.Language;
        if (req.FontSize != null) user.FontSize = req.FontSize;
        if (req.QuizReminders.HasValue) user.QuizReminders = req.QuizReminders.Value;
        if (req.WeeklyReport.HasValue) user.WeeklyReport = req.WeeklyReport.Value;
        if (req.NewContentAlerts.HasValue) user.NewContentAlerts = req.NewContentAlerts.Value;
        if (req.SoundsEnabled.HasValue) user.SoundsEnabled = req.SoundsEnabled.Value;

        await _dbContext.SaveChangesAsync();
        return Ok(new { Message = "Ayarlar başarıyla kaydedildi." });
    }

    /// <summary>
    /// Kullanıcının gamification istatistiklerini döner: TotalXP, Streak, Level bilgisi.
    /// </summary>
    [HttpGet("gamification")]
    public async Task<IActionResult> GetGamification()
    {
        var userId = GetUserId();
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null) return NotFound();

        // Level hesaplama: her 100 XP = 1 level
        var level     = (user.TotalXP / 100) + 1;
        var xpInLevel = user.TotalXP % 100;
        var xpToNext  = 100 - xpInLevel;
        var badges = await _dbContext.UserBadges
            .AsNoTracking()
            .Include(ub => ub.Badge)
            .Where(ub => ub.UserId == userId)
            .OrderByDescending(ub => ub.EarnedAt)
            .Select(ub => new
            {
                ub.Badge.Id,
                ub.Badge.Code,
                ub.Badge.Name,
                ub.Badge.Description,
                ub.Badge.IconKey,
                ub.Badge.RuleType,
                ub.Badge.Threshold,
                ub.EarnedAt
            })
            .ToListAsync();

        return Ok(new
        {
            totalXP        = user.TotalXP,
            currentStreak  = user.CurrentStreak,
            lastActiveDate = user.LastActiveDate,
            level,
            xpInLevel,
            xpToNextLevel  = xpToNext,
            levelLabel     = level switch
            {
                1 => "Beginner",
                2 => "Learner",
                3 => "Explorer",
                4 => "Scholar",
                5 => "Expert",
                _ => $"Master Lv.{level}"
            },
            badges
        });
    }

    [HttpDelete("account")]
    public async Task<IActionResult> DeleteAccount()
    {
        var userId = GetUserId();
        var deleted = await _dataLifecycle.DeleteAccountAsync(userId, HttpContext.RequestAborted);
        if (!deleted) return NotFound();

        return Ok(new { Message = "Hesap ve tüm ilişkili veriler kalıcı olarak silindi." });
    }
}
