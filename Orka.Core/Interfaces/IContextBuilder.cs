using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orka.Core.Entities;

namespace Orka.Core.Interfaces;

public interface IContextBuilder
{
    Task<IEnumerable<Message>> BuildContextAsync(Guid topicId, Guid sessionId);

    /// <summary>
    /// maxMessages = 0 → appsettings'den okunan Limits:MaxContextMessages değerini kullanır.
    /// </summary>
    Task<IEnumerable<Message>> BuildConversationContextAsync(Session session, int maxMessages = 0);
}
