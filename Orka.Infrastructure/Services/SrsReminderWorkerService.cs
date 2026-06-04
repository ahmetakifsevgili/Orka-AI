using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class SrsReminderWorkerService : ISrsReminderWorkerService
{
    private readonly OrkaDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IPushDeliveryService _push;
    private readonly IRuntimeTelemetryService _telemetry;
    private readonly IConfiguration _configuration;
    private readonly IRedisMemoryService _redisMemory;
    private readonly ILogger<SrsReminderWorkerService> _logger;

    public SrsReminderWorkerService(
        OrkaDbContext db,
        INotificationService notifications,
        IPushDeliveryService push,
        IRuntimeTelemetryService telemetry,
        IConfiguration configuration,
        IRedisMemoryService redisMemory,
        ILogger<SrsReminderWorkerService> logger)
    {
        _db = db;
        _notifications = notifications;
        _push = push;
        _telemetry = telemetry;
        _configuration = configuration;
        _redisMemory = redisMemory;
        _logger = logger;
    }

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        if (!IsEnabled("Workers:SrsReminder:Enabled"))
        {
            await RecordWorkerEventAsync("disabled", false, "worker_disabled", 0, ct);
            _logger.LogInformation("[SrsReminderWorker] Disabled by configuration.");
            return 0;
        }

        var lockKey = "orka:lock:srs_reminder_worker";
        var lockValue = Guid.NewGuid().ToString();
        var lockExpiry = TimeSpan.FromMinutes(5);

        var acquired = await _redisMemory.AcquireLockAsync(lockKey, lockValue, lockExpiry);
        if (!acquired)
        {
            _logger.LogInformation("[SrsReminderWorker] Lock already acquired by another instance. Skipping run.");
            return 0;
        }

        using var lockRenewal = StartLockRenewal(lockKey, lockValue, lockExpiry, ct);
        try
        {
            var sw = Stopwatch.StartNew();
            var sent = 0;
            try
            {
                var now = DateTime.UtcNow;
                var batchSize = ReadInt("Workers:SrsReminder:BatchSize", 25, 1, 100);
                var duplicateWindow = TimeSpan.FromHours(ReadInt("Workers:SrsReminder:DuplicateWindowHours", 20, 1, 168));
                var duplicateCutoff = now.Subtract(duplicateWindow);

                var dueItems = await _db.ReviewItems
                    .AsNoTracking()
                    .Where(r => r.Status == "active" && r.DueAt <= now)
                    .OrderBy(r => r.DueAt)
                    .Take(batchSize)
                    .ToListAsync(ct);

                foreach (var item in dueItems)
                {
                    ct.ThrowIfCancellationRequested();

                    var alreadyNotified = await _db.Notifications.AsNoTracking().AnyAsync(n =>
                        n.UserId == item.UserId &&
                        n.Type == "srs-reminder" &&
                        n.RelatedEntityType == "ReviewItem" &&
                        n.RelatedEntityId == item.Id &&
                        n.CreatedAt >= duplicateCutoff,
                        ct);

                    if (alreadyNotified) continue;

                    var label = item.ConceptTag ?? item.SkillTag ?? item.LearningObjective ?? "bugunku tekrar";
                    var dto = await _notifications.CreateAsync(
                        item.UserId,
                        new CreateNotificationRequest(
                            "srs-reminder",
                            "Tekrar zamani",
                            $"{label} icin kisa bir tekrar hazir.",
                            "info",
                            "ReviewItem",
                            item.Id,
                            now.AddDays(1)),
                        ct);

                    var subscriptions = await _db.PushSubscriptions.AsNoTracking()
                        .Where(p => p.UserId == item.UserId && p.Status == "active")
                        .Take(3)
                        .ToListAsync(ct);

                    if (subscriptions.Count == 0)
                    {
                        await _push.SendAsync(item.UserId, null, ToNotificationEntity(dto, item.UserId), ct);
                    }
                    else
                    {
                        foreach (var subscription in subscriptions)
                            await _push.SendAsync(item.UserId, subscription, ToNotificationEntity(dto, item.UserId), ct);
                    }

                    sent++;
                }

                sw.Stop();
                await RecordWorkerEventAsync("enabled", true, null, sw.ElapsedMilliseconds, ct, sent);
                return sent;
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                await RecordWorkerEventAsync("cancelled", false, "cancelled", sw.ElapsedMilliseconds, CancellationToken.None, sent);
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogWarning(
                    "[SrsReminderWorker] Batch failed safely. ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeExceptionType(ex));
                await RecordWorkerEventAsync("failed", false, "unknown_failure", sw.ElapsedMilliseconds, CancellationToken.None, sent);
                return sent;
            }
        }
        finally
        {
            lockRenewal.Cancel();
            await _redisMemory.ReleaseLockAsync(lockKey, lockValue);
        }
    }

    private CancellationTokenSource StartLockRenewal(string lockKey, string lockValue, TimeSpan lockExpiry, CancellationToken ct)
    {
        var renewalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var interval = TimeSpan.FromMilliseconds(Math.Max(1000, lockExpiry.TotalMilliseconds / 3));

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(interval);
            try
            {
                while (await timer.WaitForNextTickAsync(renewalCts.Token))
                {
                    var renewed = await _redisMemory.RenewLockAsync(lockKey, lockValue, lockExpiry);
                    if (!renewed)
                    {
                        _logger.LogWarning("[SrsReminderWorker] Lock renewal failed; another instance may acquire after TTL.");
                        return;
                    }
                }
            }
            catch (OperationCanceledException) when (renewalCts.IsCancellationRequested)
            {
                // Expected when the worker run completes.
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[SrsReminderWorker] Lock renewal loop failed. ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeExceptionType(ex));
            }
        }, CancellationToken.None);

        return renewalCts;
    }

    private async Task RecordWorkerEventAsync(
        string status,
        bool success,
        string? errorCode,
        long latencyMs,
        CancellationToken ct,
        int notificationCount = 0)
    {
        await _telemetry.RecordToolEventAsync(new ToolTelemetryEventRequest(
            UserId: null,
            SessionId: null,
            TopicId: null,
            ToolId: "srs_reminder_worker",
            CapabilityStatus: status,
            Provider: "background-worker",
            Model: null,
            LatencyMs: latencyMs,
            Success: success,
            ErrorCode: errorCode,
            FallbackUsed: !success,
            CorrelationId: null,
            MetadataJson: JsonSerializer.Serialize(new { notificationCount })), ct);
    }

    private bool IsEnabled(string key) => bool.TryParse(_configuration[key], out var enabled) && enabled;

    private int ReadInt(string key, int fallback, int min, int max)
    {
        if (!int.TryParse(_configuration[key], out var value)) return fallback;
        return Math.Clamp(value, min, max);
    }

    private static Notification ToNotificationEntity(NotificationDto dto, Guid userId) => new()
    {
        Id = dto.Id,
        UserId = userId,
        Type = dto.Type,
        Title = dto.Title,
        Body = dto.Body,
        Status = dto.Status,
        Severity = dto.Severity,
        RelatedEntityType = dto.RelatedEntityType,
        RelatedEntityId = dto.RelatedEntityId,
        Channel = dto.Channel,
        PushStatus = dto.PushStatus,
        CreatedAt = dto.CreatedAt,
        ReadAt = dto.ReadAt
    };
}
