using System;
using System.Threading.Tasks;
using Orka.Core.DTOs.Chat;

namespace Orka.Core.Interfaces;

public interface IChatService
{
    Task<ChatMessageResponse> ProcessMessageAsync(Guid userId, string content, Guid? topicId, Guid? sessionId, bool isPlanMode = false);
    Task EndSessionAsync(Guid sessionId, Guid userId);
}
