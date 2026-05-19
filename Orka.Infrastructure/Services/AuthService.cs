using System;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Exceptions;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Security;

namespace Orka.Infrastructure.Services;

public class AuthService : IAuthService
{
    private const string RevokedReasonRotated = "Rotated";
    private const string RevokedReasonReplayDetected = "ReplayDetected";
    private const string RevokedReasonLogout = "Logout";
    private static readonly ConcurrentDictionary<string, RefreshTokenGate> RefreshTokenLocks = new(StringComparer.Ordinal);

    private readonly OrkaDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly byte[] _refreshTokenHashSecret;

    public AuthService(OrkaDbContext dbContext, IConfiguration configuration, IHostEnvironment environment)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _environment = environment;
        _refreshTokenHashSecret = RefreshTokenHashSecretResolver.Resolve(configuration, environment);
    }

    public async Task<(string Token, string RefreshToken, User User)> RegisterAsync(string firstName, string lastName, string email, string password)
    {
        var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
        var existingUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);
        if (existingUser != null)
        {
            throw new ConflictException("Email exists. Bu email adresi zaten kullanımda.");
        }

        var freeStorageMb = double.Parse(_configuration["Limits:FreeStorageMB"] ?? "3072");

        var user = new User
        {
            Id = Guid.NewGuid(),
            FirstName = string.IsNullOrWhiteSpace(firstName) ? "Yeni" : firstName,
            LastName = string.IsNullOrWhiteSpace(lastName) ? "Kullanıcı" : lastName,
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Plan = UserPlan.Free,
            StorageLimitMB = freeStorageMb,
            DailyMessageCount = 0,
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
        var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

        if (user == null)
            throw new NotFoundException("Bu email adresiyle kayıtlı kullanıcı bulunamadı.");

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            throw new UnauthorizedException("Şifre hatalı.");

        user.LastLoginAt = DateTime.UtcNow;

        if (user.DailyMessageResetAt.Date < DateTime.UtcNow.Date)
        {
            user.DailyMessageCount = 0;
            user.DailyMessageResetAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();

        return await GenerateTokensAsync(user);
    }

    public async Task<(string Token, string RefreshToken)> RefreshAsync(string refreshToken, bool suppressReplayFamilyRevocation = false)
    {
        var tokenHash = HashRefreshToken(refreshToken);
        var tokenGate = RefreshTokenLocks.GetOrAdd(tokenHash, _ => new RefreshTokenGate());
        var suppressReplayFamilyRevocationFromWaiter = false;

        if (!await tokenGate.Lock.WaitAsync(0))
        {
            Interlocked.Increment(ref tokenGate.WaiterCount);
            await tokenGate.Lock.WaitAsync();
            Interlocked.Decrement(ref tokenGate.WaiterCount);
            suppressReplayFamilyRevocationFromWaiter = true;
        }

        try
        {
            return await RefreshLockedAsync(tokenHash, suppressReplayFamilyRevocation || suppressReplayFamilyRevocationFromWaiter);
        }
        finally
        {
            tokenGate.Lock.Release();
            if (tokenGate.Lock.CurrentCount == 1 && tokenGate.WaiterCount == 0)
                RefreshTokenLocks.TryRemove(tokenHash, out _);
        }
    }

    private async Task<(string Token, string RefreshToken)> RefreshLockedAsync(string tokenHash, bool suppressReplayFamilyRevocation)
    {
        var now = DateTime.UtcNow;
        var useTransaction = !_dbContext.Database.IsInMemory();
        await using var transaction = useTransaction
            ? await _dbContext.Database.BeginTransactionAsync()
            : null;

        try
        {
            var storedToken = await _dbContext.RefreshTokens
                .Include(rt => rt.User)
                .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);

            if (storedToken == null)
                throw new UnauthorizedException("Geçersiz veya süresi dolmuş refresh token.");

            if (storedToken.IsRevoked || storedToken.ExpiresAt <= now)
            {
                if (!suppressReplayFamilyRevocation && IsRotatedTokenReplay(storedToken))
                {
                    await RevokeActiveTokenFamilyAsync(
                        storedToken.TokenFamilyId,
                        now,
                        RevokedReasonReplayDetected);
                    await _dbContext.SaveChangesAsync();

                    if (transaction != null)
                        await transaction.CommitAsync();
                }

                throw new UnauthorizedException("Geçersiz veya süresi dolmuş refresh token.");
            }

            var accessToken = GenerateAccessToken(storedToken.User);
            var (newRefreshToken, newRefreshTokenEntity) =
                CreateRefreshToken(storedToken.User, storedToken.TokenFamilyId);

            storedToken.IsRevoked = true;
            storedToken.RevokedAt = now;
            storedToken.RevokedReason = RevokedReasonRotated;
            storedToken.ReplacedByTokenHash = newRefreshTokenEntity.TokenHash;
            storedToken.RowVersion = NewConcurrencyToken();

            _dbContext.RefreshTokens.Add(newRefreshTokenEntity);
            await _dbContext.SaveChangesAsync();

            if (transaction != null)
                await transaction.CommitAsync();

            return (accessToken, newRefreshToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction != null)
                await transaction.RollbackAsync();

            throw new UnauthorizedException("Geçersiz veya süresi dolmuş refresh token.");
        }
    }

    public async Task RevokeAsync(string refreshToken)
    {
        var tokenHash = HashRefreshToken(refreshToken);
        var storedToken = await _dbContext.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash);
        if (storedToken != null)
        {
            storedToken.IsRevoked = true;
            storedToken.RevokedAt = DateTime.UtcNow;
            storedToken.RevokedReason = RevokedReasonLogout;
            storedToken.RowVersion = NewConcurrencyToken();
            _dbContext.RefreshTokens.Update(storedToken);
            await _dbContext.SaveChangesAsync();
        }
    }

    private async Task<(string Token, string RefreshToken, User User)> GenerateTokensAsync(User user)
    {
        var accessToken = GenerateAccessToken(user);
        var (refreshToken, tokenEntity) = CreateRefreshToken(user, Guid.NewGuid());

        _dbContext.RefreshTokens.Add(tokenEntity);
        await _dbContext.SaveChangesAsync();

        return (accessToken, refreshToken, user);
    }

    private string GenerateAccessToken(User user)
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

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private (string RawToken, RefreshToken Entity) CreateRefreshToken(User user, Guid tokenFamilyId)
    {
        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var expiryDays = double.Parse(_configuration["JWT:RefreshTokenExpiryDays"] ?? "30");

        var tokenEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashRefreshToken(refreshToken),
            TokenFamilyId = tokenFamilyId,
            ExpiresAt = DateTime.UtcNow.AddDays(expiryDays),
            CreatedAt = DateTime.UtcNow,
            RowVersion = NewConcurrencyToken()
        };

        return (refreshToken, tokenEntity);
    }

    private string HashRefreshToken(string refreshToken) =>
        RefreshTokenHashSecretResolver.HashToken(refreshToken, _refreshTokenHashSecret);

    private static byte[] NewConcurrencyToken() => RandomNumberGenerator.GetBytes(16);

    private static bool IsRotatedTokenReplay(RefreshToken token) =>
        token.IsRevoked &&
        string.Equals(token.RevokedReason, RevokedReasonRotated, StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(token.ReplacedByTokenHash);

    private async Task RevokeActiveTokenFamilyAsync(Guid tokenFamilyId, DateTime now, string reason)
    {
        var activeTokens = await _dbContext.RefreshTokens
            .Where(rt => rt.TokenFamilyId == tokenFamilyId && !rt.IsRevoked && rt.ExpiresAt > now)
            .ToListAsync();

        foreach (var token in activeTokens)
        {
            token.IsRevoked = true;
            token.RevokedAt = now;
            token.RevokedReason = reason;
            token.RowVersion = NewConcurrencyToken();
        }
    }

    private sealed class RefreshTokenGate
    {
        public SemaphoreSlim Lock { get; } = new(1, 1);
        public int WaiterCount;
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

public class BadRequestException : Exception
{
    public BadRequestException(string message) : base(message) { }
}
