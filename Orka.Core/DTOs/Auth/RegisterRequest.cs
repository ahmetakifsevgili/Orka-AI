using Orka.Core.Enums;

namespace Orka.Core.DTOs.Auth;

public class RegisterRequest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    // ── Öğrenci Profili (opsiyonel) ─────────────────────────────────────────
    // Frontend register akışı 2. adımda bu alanları toplar. Geriye dönük uyum için
    // null kabul edilir — kullanıcı adım atlayabilir, Settings'ten sonra doldurabilir.
    public int? Age { get; set; }
    public EducationLevel? EducationLevel { get; set; }
    public LearningGoal? LearningGoal { get; set; }
    public LearningTone? LearningTone { get; set; }
    public int? DailyStudyMinutes { get; set; }
}
