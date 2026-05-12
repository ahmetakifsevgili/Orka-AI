using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Infrastructure.Data;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/notifications/subscriptions")]
[Route("api/push/subscriptions")]
public class PushSubscriptionsController : ControllerBase
{
    private readonly OrkaDbContext _db;

    public PushSubscriptionsController(OrkaDbContext db)
    {
        _db = db;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var userId = GetUserId();
        var items = await _db.PushSubscriptions
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.Status == "active")
            .OrderByDescending(p => p.UpdatedAt)
            .Select(p => new PushSubscriptionDto(p.Id, p.Endpoint, p.DeviceLabel, p.Status, p.CreatedAt, p.UpdatedAt))
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] UpsertPushSubscriptionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint))
            return BadRequest(new { message = "Endpoint gerekli." });

        if (request.Endpoint.Length > 2048)
            return BadRequest(new { message = "Endpoint cok uzun." });

        var userId = GetUserId();
        var now = DateTime.UtcNow;
        var item = await _db.PushSubscriptions
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Endpoint == request.Endpoint && p.Status == "active", ct);

        if (item == null)
        {
            item = new PushSubscription
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Endpoint = request.Endpoint.Trim(),
                Status = "active",
                CreatedAt = now
            };
            _db.PushSubscriptions.Add(item);
        }

        item.P256dh = Normalize(request.P256dh, 512);
        item.Auth = Normalize(request.Auth, 512);
        item.DeviceLabel = Normalize(request.DeviceLabel, 160);
        item.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
        return Ok(new PushSubscriptionDto(item.Id, item.Endpoint, item.DeviceLabel, item.Status, item.CreatedAt, item.UpdatedAt));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = GetUserId();
        var item = await _db.PushSubscriptions
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId && p.Status == "active", ct);

        if (item == null)
            return NotFound(new { message = "Push subscription bulunamadı." });

        item.Status = "deleted";
        item.DeletedAt = DateTime.UtcNow;
        item.UpdatedAt = item.DeletedAt.Value;
        await _db.SaveChangesAsync(ct);
        return Ok(new { deleted = true, id });
    }

    private static string? Normalize(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
