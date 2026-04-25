using System;
using System.Collections.Generic;

namespace Orka.Core.DTOs.Chat;

// ─── Mevcut DTO'lar (Korunuyor) ─────────────────────────────────────────────

public class SendMessageRequest
{
    public string Content { get; set; } = string.Empty;
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public bool IsPlanMode { get; set; }
    public bool IsVoiceMode { get; set; }
}

public class EndSessionRequest
{
    public Guid SessionId { get; set; }
}

// ─── FAZ 1: Barge-In (Metin Modu Kesintisi) ──────────────────────────────

/// <summary>
/// Kullanıcı LLM stream'ı keserken gönderdigi mesajı taşır.
/// Backend bu mesajı bağlama enjekte eder (Context Reconstruction).
/// </summary>
public class InterruptRequest
{
    public string UserMessage { get; set; } = string.Empty;
}

// ─── FAZ 2: AgentGroupChat (Otonom Sınıf) ──────────────────────────────

/// <summary>
/// NotebookLM tarzı podcast/sınıf simülasıyonunu başlatır.
/// Tutor ve Peer ajan kendi aralarında konuşur; kullanıcı istediği an araya girebilir.
/// </summary>
public class ClassroomStartRequest
{
    public string Topic { get; set; } = string.Empty;
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public bool IsVoiceMode { get; set; }
}

// ─── FAZ 3: Çok Modlu (Multimodal) ────────────────────────────────────

public enum ContentType { Text, ImageUrl }

/// <summary>
/// Polimorfik içerik öğesi. Metin veya Görsel URL taşır.
/// Görsel URL'ler Azure Blob SAS Token veya local upload URL'ı olmalıdır.
/// </summary>
public record ContentItemDto
{
    public required ContentType Type { get; init; }
    public string? Text { get; init; }
    public string? ImageUrl { get; init; }
}

/// <summary>
/// Metin + Görsel içerebilen çok modlu mesaj isteği.
/// ContentItems listesi boş değilse standart SendMessageRequest yerine bu kullanılır.
/// </summary>
public class MultimodalSendMessageRequest
{
    public required List<ContentItemDto> ContentItems { get; set; }
    public Guid? TopicId { get; set; }
    public Guid? SessionId { get; set; }
    public bool IsPlanMode { get; set; }
}
