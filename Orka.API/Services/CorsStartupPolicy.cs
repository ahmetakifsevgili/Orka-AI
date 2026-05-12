namespace Orka.API.Services;

public sealed record CorsStartupPolicy(bool AllowAnyOrigin, string[] AllowedOrigins);

public static class CorsStartupPolicyResolver
{
    private static readonly string[] DevelopmentOrigins =
    [
        "http://localhost:3000",
        "http://127.0.0.1:3000"
    ];

    public static CorsStartupPolicy Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var allowAnyInDevelopment = configuration.GetValue("Cors:AllowAnyOriginInDevelopment", false);
        var origins = configuration.GetSection("Cors:AllowedOrigins")
            .Get<string[]>()
            ?.Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.Trim().TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        if (environment.IsDevelopment())
        {
            return allowAnyInDevelopment
                ? new CorsStartupPolicy(true, [])
                : new CorsStartupPolicy(false, origins.Length == 0 ? DevelopmentOrigins : origins);
        }

        if (allowAnyInDevelopment || origins.Length == 0 || origins.Any(origin => origin == "*"))
        {
            throw new InvalidOperationException("Cors:AllowedOrigins must contain explicit origins in Staging/Production.");
        }

        return new CorsStartupPolicy(false, origins);
    }
}
