using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Security;

namespace Orka.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly OrkaDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public AuthService(OrkaDbContext dbContext, IConfiguration configuration, IHostEnvironment environment)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _environment = environment;
    }

    public async Task<(string Token, string RefreshToken, User User)> RegisterAsync(string firstName, string lastName, string email, string password)
    {
        var existingUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (existingUser != null)
            throw new Exception("Bu email adresi zaten kullanımda.");

        var freeStorageMb = double.Parse(_configuration["Limits:FreeStorageMB"] ?? "3072");

        var user = new User
        {
            Id = Guid.NewGuid(),
            FirstName = string.IsNullOrWhiteSpace(firstName) ? "Yeni" : firstName,
            LastName = string.IsNullOrWhiteSpace(lastName) ? "Kullanıcı" : lastName,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Plan = UserPlan.Free,
            StorageLimitMB = freeStorageMb,
            DailyMessageCount = 0, // Eksik olan başlatma
            CreatedAt = DateTime.UtcNow,
            DailyMessageResetAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        return await GenerateTokensAsync(user);
    }

    public async Task<(string Token, string RefreshToken, User User)> LoginAsync(string email, string password)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
            throw new NotFoundException("Bu email adresiyle kayıtlı kullanıcı bulunamadı.");

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            throw new UnauthorizedException("Şifre hatalı.");

        user.LastLoginAt = DateTime.UtcNow;

        // Günlük sıfırlama
        if (user.DailyMessageResetAt.Date < DateTime.UtcNow.Date)
        {
            user.DailyMessageCount = 0;
            user.DailyMessageResetAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();

        return await GenerateTokensAsync(user);
    }

    public async Task<(string Token, string RefreshToken)> RefreshAsync(string refreshToken)
    {
        var storedToken = await _dbContext.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

        if (storedToken == null || storedToken.IsRevoked || storedToken.ExpiresAt <= DateTime.UtcNow)
            throw new UnauthorizedException("Geçersiz veya süresi dolmuş refresh token.");

        storedToken.IsRevoked = true;
        _dbContext.RefreshTokens.Update(storedToken);

        var (newToken, newRefreshToken, _) = await GenerateTokensAsync(storedToken.User);
        return (newToken, newRefreshToken);
    }

    public async Task RevokeAsync(string refreshToken)
    {
        var storedToken = await _dbContext.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == refreshToken);
        if (storedToken != null)
        {
            storedToken.IsRevoked = true;
            _dbContext.RefreshTokens.Update(storedToken);
            await _dbContext.SaveChangesAsync();
        }
    }

    private async Task<(string Token, string RefreshToken, User User)> GenerateTokensAsync(User user)
    {
        var key = JwtKeyResolver.Resolve(_configuration, _environment.IsDevelopment());
        var creds = new SigningCredentials(key.SigningKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("plan", user.Plan.ToString()),
            new Claim(ClaimTypes.Role, user.IsAdmin ? "Admin" : "User"),
            new Claim("isAdmin", user.IsAdmin.ToString().ToLowerInvariant())
        };

        var expiryMinutes = double.Parse(_configuration["JWT:AccessTokenExpiryMinutes"] ?? "60");
        var token = new JwtSecurityToken(
            issuer: _configuration["JWT:Issuer"],
            audience: _configuration["JWT:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: creds
        );

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var expiryDays = double.Parse(_configuration["JWT:RefreshTokenExpiryDays"] ?? "30");

        var tokenEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Token = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddDays(expiryDays),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.RefreshTokens.Add(tokenEntity);
        await _dbContext.SaveChangesAsync();

        return (accessToken, refreshToken, user);
    }
}

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}

public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message) { }
}
