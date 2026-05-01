using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Orka.Infrastructure.Security;

public static class JwtKeyResolver
{
    private static readonly object _lock = new();
    private static string? _devCachedSecret;

    public static JwtKeyMaterial Resolve(
        IConfiguration configuration,
        bool isDevelopment,
        ILogger? logger = null)
    {
        var configured = configuration["JWT:Secret"];

        string secret;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            secret = configured.Trim();
        }
        else if (isDevelopment)
        {
            secret = ResolveDevSecret(logger);
        }
        else
        {
            throw new InvalidOperationException(
                "JWT:Secret is required outside Development. Configure via dotnet user-secrets or environment variable.");
        }

        var bytes = Encoding.UTF8.GetBytes(secret);
        if (bytes.Length < 32)
            throw new InvalidOperationException("JWT:Secret must be at least 32 bytes for HS256 signing.");

        return new JwtKeyMaterial(secret, new SymmetricSecurityKey(bytes));
    }

    private static string ResolveDevSecret(ILogger? logger)
    {
        // Dev fallback: process ömrü boyunca cache'lenmiş rastgele secret.
        // Avantaj: const sızıntı riski sıfır, repo temiz. Dezavantaj: API restart token'ları geçersiz kılar.
        // Stabil token için: dotnet user-secrets set "JWT:Secret" "<64+ char value>"
        lock (_lock)
        {
            if (_devCachedSecret is null)
            {
                _devCachedSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
                logger?.LogWarning(
                    "[JWT] Development fallback aktif: rastgele runtime secret üretildi (process ömrü). " +
                    "API restart sonrası mevcut tokenlar geçersizleşir. " +
                    "Stabil token için: dotnet user-secrets set \"JWT:Secret\" \"<32+ byte value>\"");
            }
            return _devCachedSecret;
        }
    }
}

public sealed record JwtKeyMaterial(string Secret, SymmetricSecurityKey SigningKey);
