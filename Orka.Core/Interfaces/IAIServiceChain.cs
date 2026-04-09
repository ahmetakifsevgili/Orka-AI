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

    /// <summary>
    /// Konuşma geçmişi (context) ile birlikte zinciri dener.
    /// Groq context-aware çağrı yapar; diğer sağlayıcılar context'i text'e serialize eder.
    /// </summary>
    Task<string> GetResponseWithFallbackAsync(IEnumerable<Message> context, string systemPrompt);
}
