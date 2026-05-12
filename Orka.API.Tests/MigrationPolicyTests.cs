using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Orka.API.Services;
using Xunit;

namespace Orka.API.Tests;

public sealed class MigrationPolicyTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void StartupAutoMigration_IsForbiddenInProtectedEnvironments(string environmentName)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Database:AutoMigrateOnStartup"] = "true"
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseMigrationStartupPolicy.ShouldRunStartupMigration(
                configuration,
                new TestEnvironment(environmentName),
                useInMemoryDatabase: false));

        Assert.DoesNotContain("ConnectionStrings", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("disabled", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StartupAutoMigration_IsDisabledForInMemoryEvenWhenDevelopmentOptInIsTrue()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Database:AutoMigrateOnStartup"] = "true"
        });

        var shouldRun = DatabaseMigrationStartupPolicy.ShouldRunStartupMigration(
            configuration,
            new TestEnvironment("Development"),
            useInMemoryDatabase: true);

        Assert.False(shouldRun);
    }

    [Fact]
    public void StartupAutoMigration_DefaultsToFalseInDevelopment()
    {
        var shouldRun = DatabaseMigrationStartupPolicy.ShouldRunStartupMigration(
            Configuration(),
            new TestEnvironment("Development"),
            useInMemoryDatabase: false);

        Assert.False(shouldRun);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void AppliedMigrationsReadiness_DefaultsToRequiredInProtectedEnvironments(string environmentName)
    {
        var required = DatabaseMigrationStartupPolicy.RequireAppliedMigrationsForReadiness(
            Configuration(),
            new TestEnvironment(environmentName),
            useInMemoryDatabase: false);

        Assert.True(required);
    }

    [Fact]
    public async Task PendingMigrationHealthCheck_CanBeDisabledByConfiguration()
    {
        var result = await RunHealthCheckAsync(
            new TestEnvironment("Production"),
            Configuration(new Dictionary<string, string?>
            {
                ["Database:RequireAppliedMigrationsForReadiness"] = "false"
            }),
            pendingMigrations: ["20260511160650_HardenRefreshTokenStorage"]);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("disabled", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PendingMigrationHealthCheck_IsHealthyWhenNoPendingMigrationsExist()
    {
        var result = await RunHealthCheckAsync(
            new TestEnvironment("Production"),
            Configuration(),
            pendingMigrations: []);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task PendingMigrationHealthCheck_IsUnhealthyWhenPendingMigrationsExist()
    {
        var result = await RunHealthCheckAsync(
            new TestEnvironment("Production"),
            Configuration(),
            pendingMigrations: ["20260511160650_HardenRefreshTokenStorage"]);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("pending EF migrations", result.Description, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HealthCheckResult> RunHealthCheckAsync(
        IHostEnvironment environment,
        IConfiguration configuration,
        IReadOnlyCollection<string> pendingMigrations)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPendingEfMigrationsReader>(new FakePendingMigrationsReader(pendingMigrations));

        await using var provider = services.BuildServiceProvider();
        var check = new EfPendingMigrationsHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>(),
            configuration,
            environment);

        return await check.CheckHealthAsync(new HealthCheckContext());
    }

    private static IConfiguration Configuration(IReadOnlyDictionary<string, string?>? values = null)
    {
        var builder = new ConfigurationBuilder();
        if (values != null)
            builder.AddInMemoryCollection(values);
        return builder.Build();
    }

    private sealed class FakePendingMigrationsReader : IPendingEfMigrationsReader
    {
        private readonly IReadOnlyCollection<string> _pendingMigrations;

        public FakePendingMigrationsReader(IReadOnlyCollection<string> pendingMigrations)
        {
            _pendingMigrations = pendingMigrations;
        }

        public Task<IReadOnlyCollection<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_pendingMigrations);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public TestEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Orka.API.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
