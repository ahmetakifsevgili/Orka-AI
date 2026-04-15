using System;

namespace Orka.Core.DTOs.Chat;

public class ChatMessageResponse
{
    public Guid MessageId { get; set; }
    public Guid SessionId { get; set; }
    public Guid TopicId { get; set; }
    public string Content { get; set; } = string.Empty;
    public string ModelUsed { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public bool WikiUpdated { get; set; }
    public Guid? WikiPageId { get; set; }
    public bool IsNewTopic { get; set; }
    public bool PlanCreated { get; set; }
    public string? TopicTitle { get; set; }
    public string Role { get; set; } = "assistant";
    public DateTime CreatedAt { get; set; }

    // Analytics
    public int? TokensUsed { get; set; }
    public decimal? TotalCostUSD { get; set; }
}
