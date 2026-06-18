using System;
using Orka.Core.Enums;
using Orka.Core.Interfaces;

namespace Orka.Core.Entities;

public class Message : IMustHaveTenant
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
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
    public string? MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; }
}
