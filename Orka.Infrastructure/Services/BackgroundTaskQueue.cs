using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class BackgroundTaskQueue : BackgroundService, IBackgroundTaskQueue
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);
    private readonly Channel<BackgroundTaskItem> _queue;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IAiRequestContextAccessor _aiRequestContext;
    private readonly ILogger<BackgroundTaskQueue> _logger;
    private readonly int _maxConcurrency;

    public BackgroundTaskQueue(
        IServiceScopeFactory scopeFactory,
        IAiRequestContextAccessor aiRequestContext,
        ILogger<BackgroundTaskQueue> logger)
        : this(scopeFactory, aiRequestContext, logger, maxConcurrency: 4)
    {
    }

    public BackgroundTaskQueue(
        IAiRequestContextAccessor aiRequestContext,
        ILogger<BackgroundTaskQueue> logger)
        : this(null, aiRequestContext, logger, maxConcurrency: 4)
    {
    }

    public BackgroundTaskQueue(
        IServiceScopeFactory? scopeFactory,
        IAiRequestContextAccessor aiRequestContext,
        ILogger<BackgroundTaskQueue> logger,
        int maxConcurrency)
    {
        _scopeFactory = scopeFactory;
        _aiRequestContext = aiRequestContext;
        _logger = logger;
        _maxConcurrency = Math.Clamp(maxConcurrency, 1, 64);
        _queue = Channel.CreateBounded<BackgroundTaskItem>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
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
        var semaphore = new SemaphoreSlim(_maxConcurrency);
        var activeTasks = new System.Collections.Concurrent.ConcurrentDictionary<Task, byte>();

        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                await semaphore.WaitAsync(stoppingToken);

                var task = Task.Run(async () =>
                {
                    try
                    {
                        await RunItemAsync(item, stoppingToken);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, stoppingToken);

                activeTasks.TryAdd(task, 0);
                _ = task.ContinueWith(t => activeTasks.TryRemove(t, out _), CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected shutdown
        }
        finally
        {
            var remaining = activeTasks.Keys.ToArray();
            if (remaining.Length > 0)
            {
                try
                {
                    await Task.WhenAll(remaining).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                }
                catch
                {
                    // Ignore shutdown exceptions
                }
            }
            semaphore.Dispose();
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
                    "[BackgroundQueue] Job started. Type={JobType} Attempt={Attempt}/{Attempts} UserRef={UserRef} CorrelationRef={CorrelationRef}",
                    LogPrivacyGuard.SafeMessage(item.JobType, 80),
                    attempt,
                    attempts,
                    LogPrivacyGuard.SafeId(item.UserId, "usr"),
                    LogPrivacyGuard.SafeTextRef(item.CorrelationId, "corr"));

                using var aiContext = _aiRequestContext.Push(new AiRequestContext(
                    UserId: item.UserId,
                    CorrelationId: item.CorrelationId,
                    Source: item.JobType,
                    IsBackground: true));

                if (item.ScopedWork != null)
                {
                    if (_scopeFactory == null)
                    {
                        throw new InvalidOperationException("Background scoped work requires an IServiceScopeFactory.");
                    }

                    using var scope = _scopeFactory.CreateScope();
                    await item.ScopedWork(scope.ServiceProvider, timeout.Token);
                }
                else
                {
                    await item.Work(timeout.Token);
                }

                _logger.LogInformation(
                    "[BackgroundQueue] Job succeeded. Type={JobType} Attempt={Attempt}/{Attempts} UserRef={UserRef} CorrelationRef={CorrelationRef}",
                    LogPrivacyGuard.SafeMessage(item.JobType, 80),
                    attempt,
                    attempts,
                    LogPrivacyGuard.SafeId(item.UserId, "usr"),
                    LogPrivacyGuard.SafeTextRef(item.CorrelationId, "corr"));
                return;
            }
            catch (OperationCanceledException ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "[BackgroundQueue] Job timed out. Type={JobType} Attempt={Attempt}/{Attempts} UserRef={UserRef} CorrelationRef={CorrelationRef} ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeMessage(item.JobType, 80),
                    attempt,
                    attempts,
                    LogPrivacyGuard.SafeId(item.UserId, "usr"),
                    LogPrivacyGuard.SafeTextRef(item.CorrelationId, "corr"),
                    LogPrivacyGuard.SafeExceptionType(ex));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "[BackgroundQueue] Job failed. Type={JobType} Attempt={Attempt}/{Attempts} UserRef={UserRef} CorrelationRef={CorrelationRef} ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeMessage(item.JobType, 80),
                    attempt,
                    attempts,
                    LogPrivacyGuard.SafeId(item.UserId, "usr"),
                    LogPrivacyGuard.SafeTextRef(item.CorrelationId, "corr"),
                    LogPrivacyGuard.SafeExceptionType(ex));
            }

            if (attempt < attempts)
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), stoppingToken);
        }
    }
}
