using Microsoft.Extensions.Hosting;

namespace Orka.API.Services;

public static class ProductionSafetyStartupPolicy
{
    private static readonly IReadOnlyDictionary<string, string> ProviderCredentialKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GitHubModels"] = "AI:GitHubModels:Token",
            ["Groq"] = "AI:Groq:ApiKey",
            ["Gemini"] = "AI:Gemini:ApiKey",
            ["OpenRouter"] = "AI:OpenRouter:ApiKey",
            ["Cerebras"] = "AI:Cerebras:ApiKey",
            ["Mistral"] = "AI:Mistral:ApiKey",
            ["SambaNova"] = "AI:SambaNova:ApiKey",
            ["Cohere"] = "AI:Cohere:ApiKey"
        };

    public static void Validate(
        IConfiguration configuration,
        IHostEnvironment environment,
        bool useInMemoryDatabase)
    {
        if (!IsProtected(environment))
            return;

        var errors = new List<string>();

        ValidateSecret(configuration, "JWT:Secret", errors);
        ValidateSecret(configuration, "JWT:RefreshTokenHashSecret", errors);
        ValidateDatabase(configuration, useInMemoryDatabase, errors);
        ValidateRedis(configuration, errors);
        ValidateCors(configuration, environment, errors);
        ValidateAllowedHosts(configuration, errors);
        ValidateAuthRateLimit(configuration, environment, errors);
        ValidateRefreshCookie(configuration, errors);
        ValidateAiCost(configuration, errors);
        ValidateAiProviders(configuration, errors);

        if (errors.Count > 0)
            throw new InvalidOperationException("Production safety validation failed: " + string.Join("; ", errors));
    }

    private static bool IsProtected(IHostEnvironment environment) =>
        environment.IsProduction() || environment.IsStaging();

    private static void ValidateSecret(IConfiguration configuration, string key, List<string> errors)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{key} is required");
            return;
        }

        if (System.Text.Encoding.UTF8.GetByteCount(value.Trim()) < 32)
            errors.Add($"{key} must be at least 32 bytes");
    }

    private static void ValidateDatabase(IConfiguration configuration, bool useInMemoryDatabase, List<string> errors)
    {
        if (useInMemoryDatabase)
            errors.Add("Database:Provider=InMemory is not allowed in Staging/Production");

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            errors.Add("ConnectionStrings:DefaultConnection is required");
            return;
        }

        if (connectionString.Contains("(localdb)", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("AttachDbFilename", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("ConnectionStrings:DefaultConnection must not use local development database settings");
        }
    }

    private static void ValidateRedis(IConfiguration configuration, List<string> errors)
    {
        var redis = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redis))
        {
            errors.Add("ConnectionStrings:Redis is required");
            return;
        }

        if (redis.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
            redis.Contains("127.0.0.1") ||
            redis.StartsWith("::1"))
        {
            errors.Add("ConnectionStrings:Redis must not point to localhost in Staging/Production");
        }
    }

    private static void ValidateCors(
        IConfiguration configuration,
        IHostEnvironment environment,
        List<string> errors)
    {
        try
        {
            _ = CorsStartupPolicyResolver.Resolve(configuration, environment);
        }
        catch (InvalidOperationException ex)
        {
            errors.Add(ex.Message);
        }
    }

    private static void ValidateAllowedHosts(IConfiguration configuration, List<string> errors)
    {
        var allowedHosts = configuration["AllowedHosts"];
        var hosts = (allowedHosts ?? string.Empty)
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (hosts.Length == 0 || hosts.Any(host => host == "*"))
            errors.Add("AllowedHosts must contain explicit hosts in Staging/Production");
    }

    private static void ValidateAuthRateLimit(
        IConfiguration configuration,
        IHostEnvironment environment,
        List<string> errors)
    {
        try
        {
            var policy = AuthRateLimitStartupPolicy.Resolve(configuration, environment);
            if (!policy.Backend.Equals(AuthRateLimitStartupPolicy.BackendRedis, StringComparison.OrdinalIgnoreCase))
                errors.Add("RateLimits:Auth:Backend must be Redis in Staging/Production");
            if (policy.AllowInMemoryFallback)
                errors.Add("RateLimits:Auth:AllowInMemoryFallback must be false in Staging/Production");
        }
        catch (InvalidOperationException ex)
        {
            errors.Add(ex.Message);
        }
    }

    private static void ValidateRefreshCookie(IConfiguration configuration, List<string> errors)
    {
        var name = configuration["Auth:RefreshCookie:Name"] ?? RefreshTokenCookie.DefaultName;
        if (string.IsNullOrWhiteSpace(name))
            errors.Add("Auth:RefreshCookie:Name is required");

        var path = configuration["Auth:RefreshCookie:Path"] ?? RefreshTokenCookie.DefaultPath;
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/'))
            errors.Add("Auth:RefreshCookie:Path must start with /");

        var sameSite = configuration["Auth:RefreshCookie:SameSite"] ?? "Lax";
        if (!RefreshTokenCookie.TryParseSameSite(sameSite, out var sameSiteMode))
        {
            errors.Add("Auth:RefreshCookie:SameSite must be Strict, Lax, None, or Unspecified");
            sameSiteMode = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
        }

        var secure = configuration.GetSection("Auth:RefreshCookie").GetValue<bool?>("Secure") ?? true;
        if (!secure)
            errors.Add("Auth:RefreshCookie:Secure must be true in Staging/Production");

        if (sameSiteMode == Microsoft.AspNetCore.Http.SameSiteMode.None && !secure)
            errors.Add("Auth:RefreshCookie:Secure must be true when SameSite=None");

        var domain = configuration["Auth:RefreshCookie:Domain"];
        if (!string.IsNullOrWhiteSpace(domain) &&
            domain.Contains("localhost", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Auth:RefreshCookie:Domain must not use localhost in Staging/Production");
        }
    }

    private static void ValidateAiCost(IConfiguration configuration, List<string> errors)
    {
        if (!configuration.GetValue("AI:Cost:Enabled", true))
            errors.Add("AI:Cost:Enabled must be true in Staging/Production");

        if (!HasPositiveDecimal(configuration, "AI:Cost:GlobalDailyUsdLimit") &&
            !HasPositiveInt(configuration, "AI:Cost:GlobalDailyTokenLimit"))
        {
            errors.Add("AI:Cost global daily USD or token limit is required");
        }

        if (!HasPositiveDecimal(configuration, "AI:Cost:UserDailyUsdLimit") &&
            !HasPositiveInt(configuration, "AI:Cost:UserDailyTokenLimit"))
        {
            errors.Add("AI:Cost user daily USD or token limit is required");
        }
    }

    private static void ValidateAiProviders(IConfiguration configuration, List<string> errors)
    {
        if (configuration.GetValue<bool>("AI:ProductionSafety:AllowMissingProviderCredentials"))
            return;

        var routedProviders = configuration.GetSection("AI:AgentRouting")
            .GetChildren()
            .Where(section => !section.Key.StartsWith('_'))
            .Select(section => section["Provider"])
            .Where(provider => !string.IsNullOrWhiteSpace(provider))
            .Select(provider => provider!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (routedProviders.Length == 0)
        {
            var primary = configuration["AI:Primary"];
            routedProviders = string.IsNullOrWhiteSpace(primary) ? [] : [primary.Trim()];
        }

        var missing = routedProviders
            .Where(provider =>
                ProviderCredentialKeys.TryGetValue(provider, out var key) &&
                string.IsNullOrWhiteSpace(configuration[key]))
            .OrderBy(provider => provider, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missing.Length > 0)
            errors.Add("AI provider credentials are missing for routed providers: " + string.Join(", ", missing));
    }

    private static bool HasPositiveDecimal(IConfiguration configuration, string key) =>
        decimal.TryParse(configuration[key], out var value) && value > 0m;

    private static bool HasPositiveInt(IConfiguration configuration, string key) =>
        int.TryParse(configuration[key], out var value) && value > 0;
}
