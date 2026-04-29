using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Orka.Infrastructure.Security;

public static class JwtKeyResolver
{
    private const string DevelopmentSecret = "ORKA_DEV_SECRET_KEY_FOR_LOCAL_AUTH_ONLY_64_CHARS_2026_01";

    public static JwtKeyMaterial Resolve(IConfiguration configuration, bool isDevelopment)
    {
        var configured = configuration["JWT:Secret"];
        var secret = string.IsNullOrWhiteSpace(configured)
            ? isDevelopment ? DevelopmentSecret : null
            : configured.Trim();

        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("JWT:Secret is required outside Development.");

        var bytes = Encoding.UTF8.GetBytes(secret);
        if (bytes.Length < 32)
            throw new InvalidOperationException("JWT:Secret must be at least 32 bytes for HS256 signing.");

        return new JwtKeyMaterial(secret, new SymmetricSecurityKey(bytes));
    }
}

public sealed record JwtKeyMaterial(string Secret, SymmetricSecurityKey SigningKey);
