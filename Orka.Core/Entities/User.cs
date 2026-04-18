using System;
using System.Collections.Generic;
using Orka.Core.Enums;

namespace Orka.Core.Entities;

public class User
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserPlan Plan { get; set; } = UserPlan.Free;
    public double StorageUsedMB { get; set; }
    public double StorageLimitMB { get; set; }
    public int DailyMessageCount { get; set; }
    public DateTime DailyMessageResetAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Sistem izleme paneli (System Health HUD, LLMOps dashboard, denetim scriptleri)
    /// yalnızca admin=true hesaplara açıktır. Normal kullanıcılar yalnızca kendi
    /// öğrenme karnesini görür — LLMOps verisi iş süreci sayılır ve gizli tutulur.
    /// </summary>
    public bool IsAdmin { get; set; } = false;

    public ICollection<Topic> Topics { get; set; } = new List<Topic>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    // ── Settings ────────────────────────────────────────────────────────────
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "English";
    public string FontSize { get; set; } = "Medium";
    
    // Notifications
    public bool QuizReminders { get; set; } = true;
    public bool WeeklyReport { get; set; } = true;
    public bool NewContentAlerts { get; set; } = false;
    public bool SoundsEnabled { get; set; } = true;

    // ── Gamification ────────────────────────────────────────────────────────
    /// <summary>Kullanıcının toplam XP puanı (doğru quiz cevabı başına +20).</summary>
    public int TotalXP { get; set; } = 0;
    /// <summary>Ardışık günlük aktif gün sayısı.</summary>
    public int CurrentStreak { get; set; } = 0;
    /// <summary>Son aktivite tarihi; streak hesabında kullanılır.</summary>
    public DateTime? LastActiveDate { get; set; }
}
