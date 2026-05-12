using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orka.Core.Interfaces;

public interface IDataLifecycleService
{
    Task<bool> DeleteTopicTreeAsync(Guid userId, Guid topicId, CancellationToken ct = default);

    Task<bool> DeleteAccountAsync(Guid userId, CancellationToken ct = default);
}
