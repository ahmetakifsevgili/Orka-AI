using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Orka.Core.Interfaces;
using Orka.API.Hubs;

namespace Orka.API.Services;

public class ClassroomVoicePusher : IClassroomVoicePusher
{
    private readonly IHubContext<ClassroomHub> _hubContext;

    public ClassroomVoicePusher(IHubContext<ClassroomHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task PushAudioChunkAsync(Guid sessionId, string base64Audio, string speakerId, CancellationToken cancellationToken = default)
    {
        await _hubContext.Clients.Group(sessionId.ToString())
            .SendAsync("ReceiveAudioChunk", new { Base64Audio = base64Audio, Speaker = speakerId }, cancellationToken);
    }
}
