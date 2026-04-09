using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Altı Sağlayıcılı Yük Devretme Zinciri (Altılı Ekip):
///   Groq → SambaNova → Cerebras → Cohere → HuggingFace → Mistral
///
/// Her sağlayıcıya 10 saniyelik katı zaman aşımı uygulanır.
/// Timeout veya hata durumunda bir sonraki sağlayıcıya geçilir.
/// IsUsableResponse() tüm bilinen hata string'lerini filtreler.
/// </summary>
public class AIServiceChain : IAIServiceChain
{
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(10);

    private readonly IGroqService         _groq;
    private readonly ISambaNovaService    _sambaNova;
    private readonly ICerebrasService     _cerebras;
    private readonly ICohereService       _cohere;
    private readonly IHuggingFaceService  _huggingFace;
    private readonly IMistralService      _mistral;
    private readonly ILogger<AIServiceChain> _logger;

    public AIServiceChain(
        IGroqService         groq,
        ISambaNovaService    sambaNova,
        ICerebrasService     cerebras,
        ICohereService       cohere,
        IHuggingFaceService  huggingFace,
        IMistralService      mistral,
        ILogger<AIServiceChain> logger)
    {
        _groq        = groq;
        _sambaNova   = sambaNova;
        _cerebras    = cerebras;
        _cohere      = cohere;
        _huggingFace = huggingFace;
        _mistral     = mistral;
        _logger      = logger;
    }

    public async Task<string> GenerateWithFallbackAsync(string systemPrompt, string userMessage)
    {
        var chain = new (string Name, Func<Task<string>> Call)[]
        {
            ("Groq",        () => _groq.GenerateResponseAsync(systemPrompt, userMessage)),
            ("SambaNova",   () => _sambaNova.GenerateResponseAsync(systemPrompt, userMessage)),
            ("Cerebras",    () => _cerebras.GenerateResponseAsync(systemPrompt, userMessage)),
            ("Cohere",      () => _cohere.GenerateResponseAsync(systemPrompt, userMessage)),
            ("HuggingFace", () => _huggingFace.GenerateResponseAsync(systemPrompt, userMessage)),
            ("Mistral",     () => _mistral.GenerateResponseAsync(systemPrompt, userMessage)),
        };

        Exception? lastEx = null;

        foreach (var (name, call) in chain)
        {
            using var cts = new CancellationTokenSource(ProviderTimeout);
            try
            {
                _logger.LogDebug("[AIChain] {Provider} deneniyor...", name);
                var result = await call().WaitAsync(cts.Token);

                if (IsUsableResponse(result))
                {
                    _logger.LogInformation("[AIChain] {Provider} başarılı.", name);
                    return result;
                }

                _logger.LogWarning("[AIChain] {Provider} hatalı yanıt döndü, sonraki deneniyor. İçerik: {R}",
                    name, result?.Length > 80 ? result[..80] : result);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                _logger.LogWarning("[AIChain] {Provider} 10 saniyelik timeout aşıldı, sonraki deneniyor.", name);
            }
            catch (Exception ex)
            {
                lastEx = ex;
                _logger.LogWarning(ex, "[AIChain] {Provider} başarısız, sonraki provider'a geçiliyor.", name);
            }
        }

        throw new InvalidOperationException(
            "Tüm AI sağlayıcıları başarısız oldu. Lütfen daha sonra tekrar deneyin.",
            lastEx);
    }

    /// <summary>
    /// Context-aware failover: Groq'u context mesajlarıyla çağırır.
    /// Başarısız olursa context'i düz metne serialize edip zincirin geri kalanına aktarır.
    /// </summary>
    public async Task<string> GetResponseWithFallbackAsync(IEnumerable<Message> context, string systemPrompt)
    {
        // 1. Groq — tam context desteği, 10s timeout
        using var cts = new CancellationTokenSource(ProviderTimeout);
        try
        {
            _logger.LogDebug("[AIChain] Groq (context-aware) deneniyor...");
            var result = await _groq.GetResponseAsync(context, systemPrompt).WaitAsync(cts.Token);

            if (IsUsableResponse(result))
            {
                _logger.LogInformation("[AIChain] Groq başarılı (context).");
                return result;
            }

            _logger.LogWarning("[AIChain] Groq boş/hatalı yanıt döndü, failover başlıyor.");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            _logger.LogWarning("[AIChain] Groq (context) 10s timeout aşıldı, failover başlıyor.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AIChain] Groq failed, falling back to next provider. Sebep: {Msg}", ex.Message);
        }

        // 2. Fallback: context'i metin haline getir, zincirin geri kalanını dene
        var contextSummary = BuildContextSummary(context);
        return await GenerateWithFallbackAsync(systemPrompt, contextSummary);
    }

    /// <summary>
    /// Yanıtın kullanılabilir olup olmadığını denetler.
    /// Servisler exception atmak yerine hata string'i döndürebilir; bunları filtreler.
    /// </summary>
    private static bool IsUsableResponse(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return false;

        var lower = result.ToLowerInvariant();
        if (lower.Contains("api hatası"))               return false;
        if (lower.Contains("zihnim biraz karıştı"))     return false;
        if (lower.Contains("kota doldu"))               return false;
        if (lower.Contains("fikirlerimi toparlamakta")) return false;
        if (lower.Contains("servis şu an meşgul"))      return false;
        if (lower.Contains("bir sorun oluştu"))         return false;
        if (lower.Contains("ai yanıtı işlenirken"))     return false;

        return true;
    }

    /// <summary>Context mesajlarını tek bir kullanıcı metni olarak özetler.</summary>
    private static string BuildContextSummary(IEnumerable<Message> context)
    {
        var msgs = context
            .Where(m => !string.IsNullOrWhiteSpace(m.Content))
            .TakeLast(6)
            .ToList();

        if (msgs.Count == 0) return "(Yeni konuşma — önceki mesaj yok)";

        return string.Join("\n", msgs.Select(m =>
            $"{(m.Role?.ToLower() == "user" ? "Kullanıcı" : "Asistan")}: {m.Content}"));
    }
}
