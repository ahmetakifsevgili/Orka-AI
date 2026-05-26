using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public sealed class SrsReminderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SrsReminderWorker> _logger;

    public SrsReminderWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<SrsReminderWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsEnabled("Workers:SrsReminder:Enabled"))
        {
            _logger.LogInformation("[SrsReminderWorker] Hosted worker disabled.");
            return;
        }

        var interval = TimeSpan.FromMinutes(ReadInt("Workers:SrsReminder:IntervalMinutes", 60, 5, 1440));
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task RunDisabledProofAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISrsReminderWorkerService>().RunOnceAsync(stoppingToken);
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISrsReminderWorkerService>().RunOnceAsync(stoppingToken);
    }

    private bool IsEnabled(string key) => bool.TryParse(_configuration[key], out var enabled) && enabled;

    private int ReadInt(string key, int fallback, int min, int max)
    {
        if (!int.TryParse(_configuration[key], out var value)) return fallback;
        return Math.Clamp(value, min, max);
    }
}

public sealed class DailyChallengeWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DailyChallengeWorker> _logger;

    public DailyChallengeWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<DailyChallengeWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsEnabled("Workers:DailyChallenge:Enabled"))
        {
            _logger.LogInformation("[DailyChallengeWorker] Hosted worker disabled.");
            return;
        }

        var interval = TimeSpan.FromMinutes(ReadInt("Workers:DailyChallenge:IntervalMinutes", 60, 5, 1440));
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);
            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task RunDisabledProofAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDailyChallengeWorkerService>().RunOnceAsync(stoppingToken);
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IDailyChallengeWorkerService>().RunOnceAsync(stoppingToken);
    }

    private bool IsEnabled(string key) => bool.TryParse(_configuration[key], out var enabled) && enabled;

    private int ReadInt(string key, int fallback, int min, int max)
    {
        if (!int.TryParse(_configuration[key], out var value)) return fallback;
        return Math.Clamp(value, min, max);
    }
}
