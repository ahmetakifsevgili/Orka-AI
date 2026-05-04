using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class NotificationService : INotificationService
{
    private readonly OrkaDbContext _db;

    public NotificationService(OrkaDbContext db)
    {
        _db = db;
    }

    public async Task<NotificationDto> CreateAsync(Guid userId, CreateNotificationRequest request, CancellationToken ct = default)
    {
        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = Normalize(request.Type, "general"),
            Title = string.IsNullOrWhiteSpace(request.Title) ? "Orka" : request.Title.Trim(),
            Body = string.IsNullOrWhiteSpace(request.Body) ? string.Empty : request.Body.Trim(),
            Severity = Normalize(request.Severity, "info"),
            RelatedEntityType = request.RelatedEntityType,
            RelatedEntityId = request.RelatedEntityId,
            Channel = "in-app",
            PushStatus = "skipped",
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = request.ExpiresAt
        };

        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync(ct);
        return ToDto(notification);
    }

    public async Task<IReadOnlyList<NotificationDto>> ListAsync(Guid userId, bool includeRead = false, CancellationToken ct = default)
    {
        var query = _db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (!includeRead) query = query.Where(n => n.Status != "read");
        return await query.OrderByDescending(n => n.CreatedAt).Take(50).Select(n => ToDto(n)).ToListAsync(ct);
    }

    public async Task<NotificationDto?> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken ct = default)
    {
        var notification = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId, ct);
        if (notification == null) return null;
        notification.Status = "read";
        notification.ReadAt ??= DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToDto(notification);
    }

    public async Task<int> MarkAllReadAsync(Guid userId, CancellationToken ct = default)
    {
        var notifications = await _db.Notifications.Where(n => n.UserId == userId && n.Status != "read").ToListAsync(ct);
        foreach (var n in notifications)
        {
            n.Status = "read";
            n.ReadAt ??= DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        return notifications.Count;
    }

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToLowerInvariant();

    private static NotificationDto ToDto(Notification n) =>
        new(n.Id, n.Type, n.Title, n.Body, n.Status, n.Severity, n.RelatedEntityType, n.RelatedEntityId, n.Channel, n.PushStatus, n.CreatedAt, n.ReadAt);
}
