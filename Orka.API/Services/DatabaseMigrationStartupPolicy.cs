using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Orka.API.Services;

public static class DatabaseMigrationStartupPolicy
{
    private const string AutoMigrateKey = "Database:AutoMigrateOnStartup";
    private const string RequireAppliedMigrationsKey = "Database:RequireAppliedMigrationsForReadiness";

    public static bool ShouldRunStartupMigration(
        IConfiguration configuration,
        IHostEnvironment environment,
        bool useInMemoryDatabase)
    {
        if (useInMemoryDatabase)
            return false;

        var autoMigrate = configuration.GetValue<bool?>(AutoMigrateKey) ?? false;
        if (autoMigrate && IsProtectedEnvironment(environment))
        {
            throw new InvalidOperationException(
                "Startup database auto-migration is disabled in Staging and Production. Run EF migrations as a controlled deployment step.");
        }

        return environment.IsDevelopment() && autoMigrate;
    }

    public static bool RequireAppliedMigrationsForReadiness(
        IConfiguration configuration,
        IHostEnvironment environment,
        bool useInMemoryDatabase)
    {
        if (useInMemoryDatabase)
            return false;

        var configured = configuration.GetValue<bool?>(RequireAppliedMigrationsKey);
        return configured ?? IsProtectedEnvironment(environment);
    }

    private static bool IsProtectedEnvironment(IHostEnvironment environment) =>
        environment.IsProduction() || environment.IsStaging();
}
