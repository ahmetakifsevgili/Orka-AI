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

public sealed class DailyChallengeWorkerService : IDailyChallengeWorkerService
{
    private readonly OrkaDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IPushDeliveryService _push;
    private readonly IRuntimeTelemetryService _telemetry;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DailyChallengeWorkerService> _logger;

    public DailyChallengeWorkerService(
        OrkaDbContext db,
        INotificationService notifications,
        IPushDeliveryService push,
        IRuntimeTelemetryService telemetry,
        IConfiguration configuration,
        ILogger<DailyChallengeWorkerService> logger)
    {
        _db = db;
        _notifications = notifications;
        _push = push;
        _telemetry = telemetry;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        if (!IsEnabled("Workers:DailyChallenge:Enabled"))
        {
            await RecordWorkerEventAsync("disabled", false, "worker_disabled", 0, ct);
            _logger.LogInformation("[DailyChallengeWorker] Disabled by configuration.");
            return 0;
        }

        var sw = Stopwatch.StartNew();
        var sent = 0;
        try
        {
            var now = DateTime.UtcNow;
            var today = now.Date;
            var batchSize = ReadInt("Workers:DailyChallenge:BatchSize", 25, 1, 100);
            var duplicateCutoff = now.AddHours(-ReadInt("Workers:DailyChallenge:DuplicateWindowHours", 20, 1, 168));

            var challenges = await _db.DailyChallenges.AsNoTracking()
                .Where(c => c.Date == today && c.Status == "active")
                .OrderBy(c => c.CreatedAt)
                .Take(batchSize)
                .ToListAsync(ct);

            foreach (var challenge in challenges)
            {
                ct.ThrowIfCancellationRequested();

                var alreadyNotified = await _db.Notifications.AsNoTracking().AnyAsync(n =>
                    n.UserId == challenge.UserId &&
                    n.Type == "daily-challenge" &&
                    n.RelatedEntityType == "DailyChallenge" &&
                    n.RelatedEntityId == challenge.Id &&
                    n.CreatedAt >= duplicateCutoff,
                    ct);

                if (alreadyNotified) continue;

                var label = challenge.SourceConceptTag ?? challenge.SourceSkillTag ?? "gunluk pratik";
                var dto = await _notifications.CreateAsync(
                    challenge.UserId,
                    new CreateNotificationRequest(
                        "daily-challenge",
                        "Günlük pratik hazır",
                        $"{label} icin bugunku kisa alistirma seni bekliyor.",
                        "info",
                        "DailyChallenge",
                        challenge.Id,
                        today.AddDays(1)),
                    ct);

                var subscriptions = await _db.PushSubscriptions.AsNoTracking()
                    .Where(p => p.UserId == challenge.UserId && p.Status == "active")
                    .Take(3)
                    .ToListAsync(ct);

                if (subscriptions.Count == 0)
                {
                    await _push.SendAsync(challenge.UserId, null, ToNotificationEntity(dto, challenge.UserId), ct);
                }
                else
                {
                    foreach (var subscription in subscriptions)
                        await _push.SendAsync(challenge.UserId, subscription, ToNotificationEntity(dto, challenge.UserId), ct);
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
                "[DailyChallengeWorker] Batch failed safely. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
            await RecordWorkerEventAsync("failed", false, "unknown_failure", sw.ElapsedMilliseconds, CancellationToken.None, sent);
            return sent;
        }
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
            ToolId: "daily_challenge_worker",
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
