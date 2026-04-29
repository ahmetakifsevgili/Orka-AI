using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Orka.Core.DTOs.Auth;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Services;

namespace Orka.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IConfiguration _configuration;

    public AuthController(IAuthService authService, IConfiguration configuration)
    {
        _authService = authService;
        _configuration = configuration;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            var result = await _authService.RegisterAsync(request.FirstName, request.LastName, request.Email, request.Password);
            return Ok(new AuthResponse
            {
                Token = result.Token,
                RefreshToken = result.RefreshToken,
                User = ToUserDto(result.User)
            });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Istek islenemedi." });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var result = await _authService.LoginAsync(request.Email, request.Password);
            return Ok(new AuthResponse
            {
                Token = result.Token,
                RefreshToken = result.RefreshToken,
                User = ToUserDto(result.User)
            });
        }
        catch (NotFoundException)
        {
            return NotFound(new { message = "Kayit bulunamadi." });
        }
        catch (UnauthorizedException)
        {
            return Unauthorized(new { message = "Kimlik dogrulama basarisiz." });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Istek islenemedi." });
        }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        try
        {
            var result = await _authService.RefreshAsync(request.RefreshToken);
            return Ok(new { token = result.Token, refreshToken = result.RefreshToken });
        }
        catch (Exception)
        {
            return Unauthorized(new { message = "GeÃ§ersiz refresh token." });
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest request)
    {
        await _authService.RevokeAsync(request.RefreshToken);
        return Ok();
    }

    private UserDto ToUserDto(User user) => new()
    {
        Id = user.Id.ToString(),
        FirstName = user.FirstName,
        LastName = user.LastName,
        Email = user.Email,
        Plan = user.Plan.ToString(),
        DailyMessageCount = user.DailyMessageCount,
        DailyLimit = GetDailyLimit(user.Plan),
        IsAdmin = user.IsAdmin,
        Settings = new UserSettingsDto
        {
            Theme = user.Theme,
            Language = user.Language,
            FontSize = user.FontSize,
            QuizReminders = user.QuizReminders,
            WeeklyReport = user.WeeklyReport,
            NewContentAlerts = user.NewContentAlerts,
            SoundsEnabled = user.SoundsEnabled
        }
    };

    private int GetDailyLimit(UserPlan plan)
    {
        return plan == UserPlan.Pro
            ? _configuration.GetValue("Limits:ProUserDailyMessages", 500)
            : _configuration.GetValue("Limits:FreeUserDailyMessages", 50);
    }
}
