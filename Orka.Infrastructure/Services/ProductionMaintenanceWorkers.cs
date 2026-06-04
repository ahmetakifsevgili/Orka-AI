using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class RetentionCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RetentionCleanupWorker> _logger;

    public RetentionCleanupWorker(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<RetentionCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("Retention:CleanupWorkerEnabled", false))
        {
            _logger.LogInformation("[RetentionCleanupWorker] Disabled by configuration.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var cleanup = scope.ServiceProvider.GetRequiredService<IRetentionCleanupService>();
                await cleanup.PurgeExpiredAudioAsync(stoppingToken);
                await cleanup.PurgeExpiredTelemetryAndTracesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "[RetentionCleanupWorker] Cleanup pass failed. ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeExceptionType(ex));
            }

            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }
}

public sealed class RedisStreamMaintenanceWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RedisStreamMaintenanceWorker> _logger;

    public RedisStreamMaintenanceWorker(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<RedisStreamMaintenanceWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("Redis:Streams:MaintenanceWorkerEnabled", false))
        {
            _logger.LogInformation("[RedisStreamMaintenanceWorker] Disabled by configuration.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var maintenance = scope.ServiceProvider.GetRequiredService<IRedisStreamMaintenanceService>();
                await maintenance.TrimTutorEventStreamsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "[RedisStreamMaintenanceWorker] Maintenance pass failed. ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeExceptionType(ex));
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
