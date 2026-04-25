using Orka.Core.Enums;

namespace Orka.Core.DTOs.Auth;

public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public UserDto User { get; set; } = new();
}

public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Plan { get; set; } = string.Empty;
    public int DailyMessageCount { get; set; }
    public int DailyLimit { get; set; }
    public bool IsAdmin { get; set; }

    // ── Öğrenci Profili (frontend onboarding banner tetikleyici) ─────────────
    public bool ProfileCompleted { get; set; }
    public int? Age { get; set; }
    public EducationLevel? EducationLevel { get; set; }
    public LearningGoal? LearningGoal { get; set; }
    public LearningTone? LearningTone { get; set; }
    public int? DailyStudyMinutes { get; set; }
}
