using System;
using MediatR;

namespace Orka.Core.Events;

public class TopicCompletedEvent : INotification
{
    public Guid SessionId { get; set; }
    public Guid TopicId { get; set; }
    public Guid UserId { get; set; }
}
