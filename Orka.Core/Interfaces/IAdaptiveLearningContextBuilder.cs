using System;
using System.Threading;
using System.Threading.Tasks;
using Orka.Core.DTOs;

namespace Orka.Core.Interfaces;

public interface IAdaptiveLearningContextBuilder
{
    Task<AdaptiveLearningContext> BuildAsync(
        Guid userId,
        Guid? topicId,
        string topicTitle,
        string userLevel = "Bilinmiyor",
        CancellationToken ct = default);
}
