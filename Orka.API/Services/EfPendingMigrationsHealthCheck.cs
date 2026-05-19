using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orka.Infrastructure.Utilities;

namespace Orka.API.Services;

public sealed class EfPendingMigrationsHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public EfPendingMigrationsHealthCheck(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _environment = environment;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var useInMemoryDatabase = string.Equals(
            _configuration["Database:Provider"],
            "InMemory",
            StringComparison.OrdinalIgnoreCase);

        if (!DatabaseMigrationStartupPolicy.RequireAppliedMigrationsForReadiness(
                _configuration,
                _environment,
                useInMemoryDatabase))
        {
            return HealthCheckResult.Healthy("Migration readiness check is disabled.");
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var reader = scope.ServiceProvider.GetRequiredService<IPendingEfMigrationsReader>();
            var pending = await reader.GetPendingMigrationsAsync(cancellationToken);

            return pending.Count == 0
                ? HealthCheckResult.Healthy("All EF migrations are applied.")
                : HealthCheckResult.Unhealthy($"{pending.Count} pending EF migrations detected.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "EF migration readiness check failed.",
                exception: null,
                data: new Dictionary<string, object>
                {
                    ["errorType"] = LogPrivacyGuard.SafeExceptionType(ex)
                });
        }
    }
}
