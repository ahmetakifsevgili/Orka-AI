using Orka.Core.DTOs.Chat;
using Orka.Core.Entities;

namespace Orka.Core.Interfaces;

public interface ITutorToolRuntime
{
    Task<IReadOnlyList<UsedToolDto>> DetectToolUsageAsync(
        Guid userId,
        string userMessage,
        Session session,
        string assistantResponse,
        CancellationToken ct = default);
}
