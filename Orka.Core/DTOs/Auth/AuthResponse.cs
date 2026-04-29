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
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Plan { get; set; } = string.Empty;
    public int DailyMessageCount { get; set; }
    public int DailyLimit { get; set; }
    public bool IsAdmin { get; set; }
    public UserSettingsDto Settings { get; set; } = new();
}

public class UserSettingsDto
{
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "English";
    public string FontSize { get; set; } = "Medium";
    public bool QuizReminders { get; set; } = true;
    public bool WeeklyReport { get; set; } = true;
    public bool NewContentAlerts { get; set; }
    public bool SoundsEnabled { get; set; } = true;
}
