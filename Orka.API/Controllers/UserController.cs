using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Orka.Core.Enums;
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

public class UpdateLearningProfileRequest
{
    public int? Age { get; set; }
    public EducationLevel? EducationLevel { get; set; }
    public LearningGoal? LearningGoal { get; set; }
    public LearningTone? LearningTone { get; set; }
    public int? DailyStudyMinutes { get; set; }
}

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private readonly OrkaDbContext _dbContext;
    private readonly IConfiguration _configuration;

    public UserController(OrkaDbContext dbContext, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _configuration = configuration;
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
            },
            learningProfile = new
            {
                profileCompleted  = user.ProfileCompleted,
                age               = user.Age,
                educationLevel    = user.EducationLevel,
                learningGoal      = user.LearningGoal,
                learningTone      = user.LearningTone,
                dailyStudyMinutes = user.DailyStudyMinutes
            }
        });
    }

    /// <summary>
    /// Öğrenci profilini günceller (yaş, eğitim, hedef, üslup, günlük çalışma).
    /// Signup'ta adım atlayan veya mevcut kullanıcılar bu endpoint ile Settings'ten doldurur.
    /// Herhangi bir alan set edilirse ProfileCompleted = true yapılır.
    /// </summary>
    [HttpPatch("learning-profile")]
    public async Task<IActionResult> UpdateLearningProfile([FromBody] UpdateLearningProfileRequest req)
    {
        var userId = GetUserId();
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null) return NotFound();

        bool touched = false;

        if (req.Age.HasValue)
        {
            if (req.Age.Value is <= 5 or >= 120) return BadRequest("Yaş değeri geçersiz.");
            user.Age = req.Age;
            touched = true;
        }
        if (req.EducationLevel.HasValue) { user.EducationLevel = req.EducationLevel.Value; touched = true; }
        if (req.LearningGoal.HasValue)   { user.LearningGoal   = req.LearningGoal.Value;   touched = true; }
        if (req.LearningTone.HasValue)   { user.LearningTone   = req.LearningTone.Value;   touched = true; }
        if (req.DailyStudyMinutes.HasValue)
        {
            if (req.DailyStudyMinutes.Value is <= 0 or > 600) return BadRequest("Günlük çalışma süresi geçersiz.");
            user.DailyStudyMinutes = req.DailyStudyMinutes;
            touched = true;
        }

        if (touched) user.ProfileCompleted = true;
        await _dbContext.SaveChangesAsync();

        return Ok(new
        {
            profileCompleted  = user.ProfileCompleted,
            age               = user.Age,
            educationLevel    = user.EducationLevel,
            learningGoal      = user.LearningGoal,
            learningTone      = user.LearningTone,
            dailyStudyMinutes = user.DailyStudyMinutes
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
            }
        });
    }

    [HttpDelete("account")]
    public async Task<IActionResult> DeleteAccount()
    {
        var userId = GetUserId();
        
        // Hiyerarşik Silme (Hata-6 ve Cascade Önlemi)
        // 1. Refresh Tokenlar
        var tokens = await _dbContext.RefreshTokens.Where(rt => rt.UserId == userId).ToListAsync();
        _dbContext.RefreshTokens.RemoveRange(tokens);

        // 2. Quiz Denemeleri
        var attempts = await _dbContext.QuizAttempts.Where(qa => qa.UserId == userId).ToListAsync();
        _dbContext.QuizAttempts.RemoveRange(attempts);

        // 3. Mesajlar ve Sessionlar
        var sessions = await _dbContext.Sessions.Include(s => s.Messages).Where(s => s.UserId == userId).ToListAsync();
        foreach(var session in sessions)
        {
            _dbContext.Messages.RemoveRange(session.Messages);
        }
        _dbContext.Sessions.RemoveRange(sessions);

        // 4. Wiki Blokları ve Sayfaları
        var wikis = await _dbContext.WikiPages.Include(w => w.Blocks).Where(w => w.UserId == userId).ToListAsync();
        foreach(var wiki in wikis)
        {
            _dbContext.WikiBlocks.RemoveRange(wiki.Blocks);
        }
        _dbContext.WikiPages.RemoveRange(wikis);

        // 5. Konular ve Kullanıcı (Kök)
        var user = await _dbContext.Users.Include(u => u.Topics).FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return NotFound();

        _dbContext.Topics.RemoveRange(user.Topics);
        _dbContext.Users.Remove(user);
        
        await _dbContext.SaveChangesAsync();
        return Ok(new { Message = "Hesap ve tüm ilişkili veriler kalıcı olarak silindi." });
    }
}
