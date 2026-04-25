using System;
using System.Threading.Tasks;

namespace Orka.Core.Interfaces;

public interface INotificationService
{
    Task NotifyJobPhaseUpdatedAsync(Guid userId, Guid jobId, string phase, string? logs);
}
