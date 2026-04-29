using Orka.Core.Enums;

namespace Orka.Core.Interfaces;

/// <summary>
/// Merkezi ajan fabrikası — her ajan rolü için doğru provider+model kombinasyonunu seçer
/// ve failover zincirini yönetir.
///
/// Ajan → Provider/Model eşleştirmesi appsettings.json içinde:
///   AI:AgentRouting:{Role}:Provider, AI:AgentRouting:{Role}:Model
/// </summary>
public interface IAIAgentFactory
{
    /// <summary>Verilen rol için yapılandırılmış model adını döner.</summary>
    string GetModel(AgentRole role);

    /// <summary>Verilen rol için seçili provider adını döner (HUD/log için).</summary>
    string GetProvider(AgentRole role);

    /// <summary>
    /// Tek seferlik chat tamamlama.
    /// Failover: Primary → Groq → Mistral
    /// </summary>
    Task<string> CompleteChatAsync(
        AgentRole role,
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default);

    /// <summary>
    /// Streaming chat — token akışı.
    /// Failover: Primary → Gemini → Mistral
    /// </summary>
    IAsyncEnumerable<string> StreamChatAsync(
        AgentRole role,
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default);

    /// <summary>
    /// Çoklu mesaj geçmişi ile chat tamamlama (context-aware).
    /// messages: [("user"|"assistant", içerik), ...]
    /// </summary>
    Task<string> CompleteChatWithHistoryAsync(
        AgentRole role,
        string systemPrompt,
        IEnumerable<(string Role, string Content)> messages,
        CancellationToken ct = default);
}
