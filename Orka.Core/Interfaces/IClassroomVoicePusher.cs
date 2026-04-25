using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orka.Core.Interfaces;

/// <summary>
/// Abstract boundary to allow Infrastructure layer to push real-time audio chunks 
/// to the connected frontend clients (e.g. via SignalR), without referencing API namespaces.
/// </summary>
public interface IClassroomVoicePusher
{
    Task PushAudioChunkAsync(Guid sessionId, string base64Audio, string speakerId, CancellationToken cancellationToken = default);
}
