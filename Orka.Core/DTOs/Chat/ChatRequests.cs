using System;

namespace Orka.Core.DTOs.Chat;

public class SendMessageRequest
{
    public string Content { get; set; } = string.Empty;
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public bool IsPlanMode { get; set; }
}

public class EndSessionRequest
{
    public Guid SessionId { get; set; }
}
