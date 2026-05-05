using System;

namespace Orka.Core.DTOs;

public sealed record PushSubscriptionDto(
    Guid Id,
    string Endpoint,
    string? DeviceLabel,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record UpsertPushSubscriptionRequest(
    string Endpoint,
    string? P256dh,
    string? Auth,
    string? DeviceLabel);
