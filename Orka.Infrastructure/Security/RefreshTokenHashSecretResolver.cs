using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Orka.Infrastructure.Security;

public static class RefreshTokenHashSecretResolver
{
    private const int MinimumSecretBytes = 32;

    public static byte[] Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredSecret =
            configuration["JWT:RefreshTokenHashSecret"] ??
            configuration["RefreshTokenHashSecret"];

        if (!string.IsNullOrWhiteSpace(configuredSecret))
        {
            var secretBytes = Encoding.UTF8.GetBytes(configuredSecret);
            if (secretBytes.Length < MinimumSecretBytes)
            {
                throw new InvalidOperationException(
                    "Refresh token hash secret must be at least 32 bytes.");
            }

            return secretBytes;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Refresh token hash secret is required outside Development.");
        }

        var jwtKey = JwtKeyResolver.Resolve(configuration, isDevelopment: true);
        using var hmac = new HMACSHA256(jwtKey.SigningKey.Key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes("orka-refresh-token-hash-secret-v1"));
    }

    public static string HashToken(string refreshToken, byte[] secret)
    {
        using var hmac = new HMACSHA256(secret);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
