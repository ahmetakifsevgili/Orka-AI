using Orka.Core.Enums;

namespace Orka.Core.Interfaces;

/// <summary>
/// Merkezi ajan fabrikası — her ajan rolü için doğru modeli seçer
/// ve failover zincirini (GitHub Models → Groq → Gemini) yönetir.
///
/// Ajan → Model eşleştirmesi appsettings.json'daki
/// AI:GitHubModels:Agents:{Role}:Model değerinden okunur.
/// </summary>
public interface IAIAgentFactory
{
    /// <summary>Verilen rol için yapılandırılmış model adını döner.</summary>
    string GetModel(AgentRole role);

    /// <summary>
    /// Tek seferlik chat tamamlama.
    /// Failover: GitHub Models → Groq → Gemini
    /// </summary>
    Task<string> CompleteChatAsync(
        AgentRole role,
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default);

    /// <summary>
    /// Streaming chat — token akışı.
    /// Failover: GitHub Models → Groq stream → Gemini stream
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
