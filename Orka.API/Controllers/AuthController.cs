using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orka.API.Services;
using Orka.Core.DTOs.Auth;
using Orka.Core.Entities;
using Orka.Core.Exceptions;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Services;

namespace Orka.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<AuthController> _logger;
    private readonly IAuthAttemptLimiter _rateLimiter;
    private readonly OrkaDbContext _dbContext;

    public AuthController(
        IAuthService authService,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<AuthController> logger,
        IAuthAttemptLimiter rateLimiter,
        OrkaDbContext dbContext)
    {
        _authService = authService;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
        _rateLimiter = rateLimiter;
        _dbContext = dbContext;
    }

    [HttpPost("register")]
    [HttpPost("/api/register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest? request)
    {
        if (request == null)
            return BadRequest(new { message = "Üyelik bilgileri zorunlu." });

        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var password = request.Password ?? string.Empty;
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { message = "E-posta zorunlu." });
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            return BadRequest(new { message = "Şifre en az 8 karakter olmalı." });

        var firstName = request.FirstName?.Trim() ?? string.Empty;
        var lastName = request.LastName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(firstName) && !string.IsNullOrWhiteSpace(request.Name))
        {
            var parts = request.Name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            firstName = parts.FirstOrDefault() ?? string.Empty;
            lastName = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : lastName;
        }

        var registerLimit = await EnforceAuthAttemptAsync("register", GetClientPartition(), "Register");
        if (registerLimit != null)
            return registerLimit;

        _logger.LogInformation("[Auth] Register attempt");

        (string Token, string RefreshToken, User User) result;
        try
        {
            result = await _authService.RegisterAsync(
                firstName, lastName, email, password);
        }
        catch (ConflictException)
        {
            return StatusCode(409, new { message = "Kayıt işlemi tamamlanamadı.", statusCode = 409 });
        }

        SetRefreshCookie(result.RefreshToken);

        return StatusCode(201, new AuthResponse
        {
            Token = result.Token,
            UserId = result.User.Id.ToString(),
            User = await CurrentUserDtoFactory.CreateAsync(result.User, _dbContext, _configuration, HttpContext.RequestAborted)
        });
    }

    [HttpPost("login")]
    [HttpPost("/api/login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest? request)
    {
        if (request == null)
            return BadRequest(new { message = "Giriş bilgileri zorunlu." });

        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var password = request.Password ?? string.Empty;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return BadRequest(new { message = "E-posta ve şifre zorunlu." });

        var loginLimit = await EnforceAuthAttemptAsync("login", $"{GetClientPartition()}:{HashPartition(email)}", "Login");
        if (loginLimit != null)
            return loginLimit;

        _logger.LogInformation("[Auth] Login attempt");

        (string Token, string RefreshToken, User User) result;
        try
        {
            result = await _authService.LoginAsync(email, password);
        }
        catch (NotFoundException)
        {
            return InvalidLogin();
        }
        catch (UnauthorizedException)
        {
            return InvalidLogin();
        }

        SetRefreshCookie(result.RefreshToken);

        return Ok(new AuthResponse
        {
            Token = result.Token,
            UserId = result.User.Id.ToString(),
            User = await CurrentUserDtoFactory.CreateAsync(result.User, _dbContext, _configuration, HttpContext.RequestAborted)
        });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest? request)
    {
        var refreshToken = ResolveRefreshToken(request);
        if (string.IsNullOrWhiteSpace(refreshToken))
            return BadRequest(new { message = "Refresh token zorunlu." });

        var refreshLimit = await EnforceAuthAttemptAsync("refresh", GetClientPartition(), "Refresh");
        if (refreshLimit != null)
            return refreshLimit;

        await ApplyRefreshRaceDelayForTestsAsync();
        var result = await _authService.RefreshAsync(refreshToken, ShouldSuppressRefreshReplayFamilyRevocationForTests());
        SetRefreshCookie(result.RefreshToken);

        return Ok(new
        {
            token = result.Token,
            jwt = result.Token,
            access_token = result.Token
        });
    }

    private async Task ApplyRefreshRaceDelayForTestsAsync()
    {
        if (!_environment.IsDevelopment())
            return;

        if (!Request.Headers.TryGetValue("X-Orka-Test-Refresh-Race-Delay-Ms", out var rawDelay))
            return;

        if (!int.TryParse(rawDelay.FirstOrDefault(), out var delayMs))
            return;

        if (delayMs is <= 0 or > 1000)
            return;

        await Task.Delay(delayMs);
    }

    private bool ShouldSuppressRefreshReplayFamilyRevocationForTests() =>
        _environment.IsDevelopment() &&
        // Development-only hook for deterministic refresh-token race tests; production still revokes true replays.
        Request.Headers.ContainsKey("X-Orka-Test-Refresh-Race");

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest? request)
    {
        var refreshToken = ResolveRefreshToken(request);
        ClearRefreshCookie();

        if (!string.IsNullOrWhiteSpace(refreshToken))
            await _authService.RevokeAsync(refreshToken);

        return Ok();
    }

    private string? ResolveRefreshToken(RefreshRequest? request)
    {
        var cookieToken = RefreshTokenCookie.Read(Request, _configuration);
        if (!string.IsNullOrWhiteSpace(cookieToken))
            return cookieToken;
        return request?.RefreshToken;
    }

    private void SetRefreshCookie(string refreshToken) =>
        RefreshTokenCookie.Set(Response, _configuration, _environment, refreshToken);

    private void ClearRefreshCookie() =>
        RefreshTokenCookie.Clear(Response, _configuration, _environment);

    private async Task<IActionResult?> EnforceAuthAttemptAsync(string purpose, string partition, string configName)
    {
        var permitLimit = _configuration.GetValue($"RateLimits:Auth:{configName}:PermitLimit", DefaultPermitLimit(configName));
        var windowMinutes = _configuration.GetValue($"RateLimits:Auth:{configName}:WindowMinutes", DefaultWindowMinutes(configName));
        var result = await _rateLimiter.TryConsumeAsync(
            $"auth:{purpose}:{partition}",
            permitLimit,
            TimeSpan.FromMinutes(windowMinutes),
            HttpContext.RequestAborted);

        if (result.Allowed)
            return null;

        return result.LimiterUnavailable
            ? AuthProtectionUnavailable()
            : TooManyAuthAttempts();
    }

    private string GetClientPartition()
    {
        // Only trust X-Forwarded-For in Development; production must use RemoteIpAddress
        // to prevent rate-limit bypass via header spoofing.
        if (_environment.IsDevelopment())
        {
            var forwardedFor = Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(forwardedFor))
                return HashPartition(forwardedFor.Split(',')[0].Trim());
        }

        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        return HashPartition(string.IsNullOrWhiteSpace(remoteIp) ? "unknown" : remoteIp);
    }

    private static int DefaultPermitLimit(string configName) => configName switch
    {
        "Register" => 3,
        "Refresh" => 20,
        _ => 5
    };

    private static int DefaultWindowMinutes(string configName) => configName switch
    {
        "Register" => 15,
        _ => 5
    };

    private static string HashPartition(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static IActionResult InvalidLogin() =>
        new ObjectResult(new { message = "E-posta veya şifre hatalı.", statusCode = 401 })
        {
            StatusCode = 401
        };

    private static IActionResult TooManyAuthAttempts() =>
        new ObjectResult(new { message = "Çok fazla deneme. Lütfen biraz sonra tekrar deneyin.", statusCode = 429 })
        {
            StatusCode = 429
        };

    private static IActionResult AuthProtectionUnavailable() =>
        new ObjectResult(new { message = "Kimlik dogrulama korumasi gecici olarak kullanilamiyor.", statusCode = 503 })
        {
            StatusCode = 503
        };
}
