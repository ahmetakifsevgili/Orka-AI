using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Orka.API.Services;

public static class RefreshTokenCookie
{
    public const string DefaultName = "orka_refresh";
    public const string DefaultPath = "/api/auth";

    public static string Name(IConfiguration configuration) =>
        Read(configuration, "Name", DefaultName);

    public static string? Read(HttpRequest request, IConfiguration configuration) =>
        request.Cookies.TryGetValue(Name(configuration), out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    public static void Set(
        HttpResponse response,
        IConfiguration configuration,
        IHostEnvironment environment,
        string refreshToken)
    {
        response.Cookies.Append(Name(configuration), refreshToken, BuildOptions(configuration, environment, includeExpiry: true));
    }

    public static void Clear(
        HttpResponse response,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        response.Cookies.Delete(Name(configuration), BuildOptions(configuration, environment, includeExpiry: false));
    }

    public static bool TryParseSameSite(string? value, out SameSiteMode mode)
    {
        mode = SameSiteMode.Lax;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        return value.Trim().ToLowerInvariant() switch
        {
            "strict" => SetMode(SameSiteMode.Strict, out mode),
            "lax" => SetMode(SameSiteMode.Lax, out mode),
            "none" => SetMode(SameSiteMode.None, out mode),
            "unspecified" => SetMode(SameSiteMode.Unspecified, out mode),
            _ => false
        };
    }

    private static CookieOptions BuildOptions(
        IConfiguration configuration,
        IHostEnvironment environment,
        bool includeExpiry)
    {
        var options = new CookieOptions
        {
            HttpOnly = true,
            Secure = ReadSecure(configuration, environment),
            SameSite = ReadSameSite(configuration),
            Path = Read(configuration, "Path", DefaultPath)
        };

        var domain = Read(configuration, "Domain", string.Empty);
        if (!string.IsNullOrWhiteSpace(domain))
            options.Domain = domain.Trim();

        if (includeExpiry)
        {
            var expiryDays = configuration.GetValue("JWT:RefreshTokenExpiryDays", 30);
            options.Expires = DateTimeOffset.UtcNow.AddDays(expiryDays);
        }

        return options;
    }

    private static SameSiteMode ReadSameSite(IConfiguration configuration) =>
        TryParseSameSite(Read(configuration, "SameSite", "Strict"), out var mode)
            ? mode
            : SameSiteMode.Strict;

    private static bool ReadSecure(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration.GetSection("Auth:RefreshCookie").GetValue<bool?>("Secure");
        return configured ?? !environment.IsDevelopment();
    }

    private static string Read(IConfiguration configuration, string key, string fallback) =>
        configuration[$"Auth:RefreshCookie:{key}"]?.Trim() is { Length: > 0 } value
            ? value
            : fallback;

    private static bool SetMode(SameSiteMode value, out SameSiteMode mode)
    {
        mode = value;
        return true;
    }
}
