using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public sealed class PushDeliveryService : IPushDeliveryService
{
    private readonly IConfiguration _configuration;
    private readonly IRuntimeTelemetryService _telemetry;
    private readonly ILogger<PushDeliveryService> _logger;

    public PushDeliveryService(
        IConfiguration configuration,
        IRuntimeTelemetryService telemetry,
        ILogger<PushDeliveryService> logger)
    {
        _configuration = configuration;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<PushDeliveryResultDto> SendAsync(
        Guid userId,
        PushSubscription? subscription,
        Notification notification,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var provider = "firebase";

        PushDeliveryResultDto result;
        try
        {
            if (!IsEnabled("Notifications:Push:Enabled") && !IsEnabled("Firebase:Enabled"))
            {
                result = new PushDeliveryResultDto(
                    false,
                    "disabled",
                    "Push delivery is disabled; in-app notification was saved.",
                    provider,
                    0,
                    "provider_disabled");
                return await RecordAndReturnAsync(userId, notification, result, sw, ct);
            }

            if (subscription == null || string.IsNullOrWhiteSpace(subscription.Endpoint))
            {
                result = new PushDeliveryResultDto(
                    false,
                    "invalid_token",
                    "No active push subscription is available.",
                    provider,
                    0,
                    "invalid_token");
                return await RecordAndReturnAsync(userId, notification, result, sw, ct);
            }

            var projectId = _configuration["Firebase:ProjectId"];
            var credentialsPath = _configuration["Firebase:CredentialsPath"];
            if (string.IsNullOrWhiteSpace(projectId) && string.IsNullOrWhiteSpace(credentialsPath))
            {
                result = new PushDeliveryResultDto(
                    false,
                    "provider_missing",
                    "Firebase provider configuration is missing; in-app notification was saved.",
                    provider,
                    0,
                    "provider_missing");
                return await RecordAndReturnAsync(userId, notification, result, sw, ct);
            }

            result = new PushDeliveryResultDto(
                false,
                "disabled",
                "Live Firebase delivery is gated until provider credentials are explicitly enabled.",
                provider,
                0,
                "provider_gated");
            return await RecordAndReturnAsync(userId, notification, result, sw, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PushDelivery] Push attempt failed safely. User={UserId} Notification={NotificationId}", userId, notification.Id);
            result = new PushDeliveryResultDto(
                false,
                "unknown_failure",
                "Push delivery failed safely; in-app notification was saved.",
                provider,
                0,
                "unknown_failure");
            return await RecordAndReturnAsync(userId, notification, result, sw, CancellationToken.None);
        }
    }

    private async Task<PushDeliveryResultDto> RecordAndReturnAsync(
        Guid userId,
        Notification notification,
        PushDeliveryResultDto result,
        Stopwatch sw,
        CancellationToken ct)
    {
        sw.Stop();
        var final = result with { LatencyMs = Math.Max(0, sw.ElapsedMilliseconds) };

        await _telemetry.RecordToolEventAsync(new ToolTelemetryEventRequest(
            UserId: userId,
            SessionId: null,
            TopicId: null,
            ToolId: "push_delivery",
            CapabilityStatus: final.Status,
            Provider: final.Provider,
            Model: null,
            LatencyMs: final.LatencyMs,
            Success: final.Success,
            ErrorCode: final.ErrorCode,
            FallbackUsed: !final.Success,
            CorrelationId: null,
            MetadataJson: $$"""{"notificationId":"{{notification.Id}}","type":"{{notification.Type}}"}"""), ct);

        return final;
    }

    private bool IsEnabled(string key) =>
        bool.TryParse(_configuration[key], out var enabled) && enabled;
}
