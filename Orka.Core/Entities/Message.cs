using System;
using Orka.Core.Enums;

namespace Orka.Core.Entities;

public class Message
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public Session Session { get; set; } = null!;
    public bool IsNewTopic { get; set; }
    public string? TopicTitle { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "assistant";
    public string Content { get; set; } = string.Empty;
    
    // YENİ: Bağlam Takip Alanları
    public MessageType MessageType { get; set; }
    public string? Intent { get; set; }        // AI'nın belirlediği niyet (örn: explain, quiz)
    public string? PhaseAtTime { get; set; } // Mesaj anındaki konu fazı (TopicPhase)
    
    public string? ModelUsed { get; set; }
    public int TokensUsed { get; set; }
    public decimal CostUSD { get; set; }
    public DateTime CreatedAt { get; set; }
}
