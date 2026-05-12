using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications)
    {
        _notifications = notifications;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool includeRead = false)
    {
        var result = await _notifications.ListAsync(GetUserId(), includeRead, HttpContext.RequestAborted);
        return Ok(result);
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        var result = await _notifications.MarkReadAsync(GetUserId(), id, HttpContext.RequestAborted);
        return result == null ? NotFound(new { message = "Notification bulunamadı." }) : Ok(result);
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var count = await _notifications.MarkAllReadAsync(GetUserId(), HttpContext.RequestAborted);
        return Ok(new { markedRead = count });
    }
}
