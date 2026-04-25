using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orka.Core.Entities;

namespace Orka.Core.Interfaces;

public interface IKorteksSwarmOrchestrator
{
    Task<Guid> EnqueueResearchJobAsync(
        Guid userId,
        string query,
        Guid? topicId = null,
        string? documentContext = null,
        bool requiresWebSearch = true);

    Task<ResearchJob?> GetJobStatusAsync(Guid jobId);
    Task ExecuteResearchJobAsync(Guid jobId); // Hangfire tarafından çağrılacak
    Task<IEnumerable<ResearchJob>> GetUserLibraryAsync(Guid userId, int take = 20);
}
