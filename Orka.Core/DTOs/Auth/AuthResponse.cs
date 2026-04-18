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
}
