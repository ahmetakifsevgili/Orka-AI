using Orka.Core.Entities;

namespace Orka.Core.Interfaces;

/// <summary>
/// Servis-arası failover zinciri.
/// Groq başarısız olursa SambaNova'ya, o da başarısız olursa Cerebras/OpenRouter/Mistral'e geçer.
/// </summary>
public interface IAIServiceChain
{
    /// <summary>
    /// Zinciri sırayla dener; ilk başarılı yanıtı döner.
    /// Tüm servisler başarısız olursa son exception fırlatılır.
    /// </summary>
    Task<string> GenerateWithFallbackAsync(string systemPrompt, string userMessage);

    Task<string> GetResponseWithFallbackAsync(IEnumerable<Message> context, string systemPrompt);

    /// <summary>
    /// Zinciri sırayla dener ve ilk başarılı stream'i döner.
    /// </summary>
    IAsyncEnumerable<string> GenerateStreamWithFallbackAsync(string systemPrompt, string userMessage, CancellationToken ct = default);

    /// <summary>
    /// Konuşma geçmişi ile birlikte stream yanıtı döner.
    /// </summary>
    IAsyncEnumerable<string> GetResponseStreamWithFallbackAsync(IEnumerable<Message> context, string systemPrompt, CancellationToken ct = default);
}
