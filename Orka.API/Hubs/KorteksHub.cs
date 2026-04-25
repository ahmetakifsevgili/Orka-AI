using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Orka.API.Hubs;

[Authorize]
public class KorteksHub : Hub
{
    private readonly ILogger<KorteksHub> _logger;

    public KorteksHub(ILogger<KorteksHub> logger)
    {
        _logger = logger;
    }

    public override Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        _logger.LogInformation("SignalR Client Connected. ConnectionId: {ConnectionId}, UserId: {UserId}", Context.ConnectionId, userId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        _logger.LogInformation("SignalR Client Disconnected. ConnectionId: {ConnectionId}, UserId: {UserId}", Context.ConnectionId, userId);
        return base.OnDisconnectedAsync(exception);
    }
}
