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

    // ── Öğrenci Profili (Faz B) ─────────────────────────────────────────────
    // DeepPlanAgent / TutorAgent / WikiAgent prompt'ları bu alanları okur.
    // Tüm alanlar NULLABLE — mevcut hesapları kırmamak için migration sırasında eski
    // kullanıcılar Unknown değerlerle doldurulur, Settings'ten güncellenebilir.

    /// <summary>Yaş — küçük kullanıcılar için kısa cümle + oyunsu örnek tetikleyici.</summary>
    public int? Age { get; set; }

    /// <summary>Eğitim seviyesi — jargon yoğunluğu ve örnek zorluğu için.</summary>
    public EducationLevel EducationLevel { get; set; } = EducationLevel.Unknown;

    /// <summary>Öğrenme amacı — müfredat derinliği ve quiz ağırlığı için.</summary>
    public LearningGoal LearningGoal { get; set; } = LearningGoal.Unknown;

    /// <summary>Anlatım üslubu tercihi — yaş tek başına yeterli sinyal değil.</summary>
    public LearningTone LearningTone { get; set; } = LearningTone.Unknown;

    /// <summary>Günlük tahmini çalışma dakikası — DeepPlan haftalık ders dağıtımı için.</summary>
    public int? DailyStudyMinutes { get; set; }

    /// <summary>Profil tamamlandı mı — false ise Home'da tek seferlik onboarding kartı.</summary>
    public bool ProfileCompleted { get; set; } = false;
}
