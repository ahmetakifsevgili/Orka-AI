using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Orka.API.Hubs;
using Orka.Core.Interfaces;

namespace Orka.API.Services;

public class SignalRNotificationService : INotificationService
{
    private readonly IHubContext<KorteksHub> _hubContext;

    public SignalRNotificationService(IHubContext<KorteksHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task NotifyJobPhaseUpdatedAsync(Guid userId, Guid jobId, string phase, string? logs)
    {
        // NameIdentifier (UserId) uzerinden sadece o kullaniciya bildirim yolla
        await _hubContext.Clients.User(userId.ToString()).SendAsync("JobPhaseUpdated", new
        {
            jobId,
            phase,
            logs
        });
    }
}
