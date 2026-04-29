using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public sealed class BackgroundTaskQueue : BackgroundService, IBackgroundTaskQueue
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);
    private readonly Channel<BackgroundTaskItem> _queue;
    private readonly ILogger<BackgroundTaskQueue> _logger;

    public BackgroundTaskQueue(ILogger<BackgroundTaskQueue> logger)
    {
        _logger = logger;
        _queue = Channel.CreateBounded<BackgroundTaskItem>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ValueTask QueueAsync(BackgroundTaskItem item, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(item.JobType))
            throw new ArgumentException("Background job type is required.", nameof(item));

        return _queue.Writer.WriteAsync(item, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            await RunItemAsync(item, stoppingToken);
        }
    }

    private async Task RunItemAsync(BackgroundTaskItem item, CancellationToken stoppingToken)
    {
        var attempts = Math.Max(1, item.MaxAttempts);
        for (var attempt = 1; attempt <= attempts && !stoppingToken.IsCancellationRequested; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(item.Timeout ?? DefaultTimeout);
            try
            {
                _logger.LogInformation(
                    "[BackgroundQueue] Job started. Type={JobType} Attempt={Attempt}/{Attempts} User={UserId} Correlation={CorrelationId}",
                    item.JobType,
                    attempt,
                    attempts,
                    item.UserId,
                    item.CorrelationId);

                await item.Work(timeout.Token);

                _logger.LogInformation(
                    "[BackgroundQueue] Job succeeded. Type={JobType} Attempt={Attempt}/{Attempts} User={UserId} Correlation={CorrelationId}",
                    item.JobType,
                    attempt,
                    attempts,
                    item.UserId,
                    item.CorrelationId);
                return;
            }
            catch (OperationCanceledException ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "[BackgroundQueue] Job timed out. Type={JobType} Attempt={Attempt}/{Attempts} User={UserId} Correlation={CorrelationId}",
                    item.JobType,
                    attempt,
                    attempts,
                    item.UserId,
                    item.CorrelationId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "[BackgroundQueue] Job failed. Type={JobType} Attempt={Attempt}/{Attempts} User={UserId} Correlation={CorrelationId}",
                    item.JobType,
                    attempt,
                    attempts,
                    item.UserId,
                    item.CorrelationId);
            }

            if (attempt < attempts)
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), stoppingToken);
        }
    }
}
