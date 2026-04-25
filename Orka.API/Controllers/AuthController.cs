using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs.Auth;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Services;

namespace Orka.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            var profile = new UserProfileDraft
            {
                Age = request.Age,
                EducationLevel = request.EducationLevel,
                LearningGoal = request.LearningGoal,
                LearningTone = request.LearningTone,
                DailyStudyMinutes = request.DailyStudyMinutes
            };
            var result = await _authService.RegisterAsync(request.FirstName, request.LastName, request.Email, request.Password, profile);
            var freeLimit = 50;
            return Ok(new AuthResponse
            {
                Token = result.Token,
                RefreshToken = result.RefreshToken,
                User = MapUser(result.User, freeLimit)
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var result = await _authService.LoginAsync(request.Email, request.Password);
            var limit = result.User.Plan.ToString() == "Pro" ? 500 : 50;
            return Ok(new AuthResponse
            {
                Token = result.Token,
                RefreshToken = result.RefreshToken,
                User = MapUser(result.User, limit)
            });
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
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
            return Unauthorized(new { message = "Geçersiz refresh token." });
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest request)
    {
        await _authService.RevokeAsync(request.RefreshToken);
        return Ok();
    }

    private static UserDto MapUser(Orka.Core.Entities.User user, int dailyLimit) => new()
    {
        Id = user.Id.ToString(),
        Email = user.Email,
        Plan = user.Plan.ToString(),
        DailyMessageCount = user.DailyMessageCount,
        DailyLimit = dailyLimit,
        IsAdmin = user.IsAdmin,
        ProfileCompleted = user.ProfileCompleted,
        Age = user.Age,
        EducationLevel = user.EducationLevel,
        LearningGoal = user.LearningGoal,
        LearningTone = user.LearningTone,
        DailyStudyMinutes = user.DailyStudyMinutes
    };
}
