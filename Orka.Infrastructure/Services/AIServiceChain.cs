using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Smart Router + Yük Devretme Zinciri
///
/// Katman 0 — Google Gemini (Primary Smart Router):
///   systemPrompt içeriğine göre görev tespiti yapar ve uygun model/config seçer.
///   Hata veya timeout → Katman 1'e geçer.
///
/// Katman 1 — Altılı Yedek Zinciri:
///   Groq → SambaNova → Cerebras → Cohere → HuggingFace → Mistral
///
/// Her sağlayıcıya 10 saniyelik katı zaman aşımı uygulanır.
/// IsUsableResponse() bilinen hata string'lerini filtreler.
/// </summary>
public class AIServiceChain : IAIServiceChain
{
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(10);

    private readonly IGeminiService      _gemini;
    private readonly IGroqService        _groq;
    private readonly ISambaNovaService   _sambaNova;
    private readonly ICerebrasService    _cerebras;
    private readonly ICohereService      _cohere;
    private readonly IHuggingFaceService _huggingFace;
    private readonly IMistralService     _mistral;
    private readonly ILogger<AIServiceChain> _logger;

    public AIServiceChain(
        IGeminiService       gemini,
        IGroqService         groq,
        ISambaNovaService    sambaNova,
        ICerebrasService     cerebras,
        ICohereService       cohere,
        IHuggingFaceService  huggingFace,
        IMistralService      mistral,
        ILogger<AIServiceChain> logger)
    {
        _gemini      = gemini;
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
        // ── Katman 0: Google Gemini Smart Router ──────────────────────────────
        using (var cts0 = new CancellationTokenSource(ProviderTimeout))
        {
            try
            {
                _logger.LogDebug("[AIChain] Gemini (Smart Router) deneniyor...");
                var geminiResult = await _gemini.GenerateSmartAsync(systemPrompt, userMessage, cts0.Token)
                                               .WaitAsync(cts0.Token);
                if (IsUsableResponse(geminiResult))
                {
                    _logger.LogInformation("[AIChain] Gemini başarılı.");
                    return geminiResult;
                }
                _logger.LogWarning("[AIChain] Gemini hatalı yanıt, fallback zincirine geçiliyor.");
            }
            catch (OperationCanceledException) when (cts0.IsCancellationRequested)
            {
                _logger.LogWarning("[AIChain] Gemini 10s timeout, fallback zincirine geçiliyor.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AIChain] Gemini başarısız, fallback zincirine geçiliyor. Sebep: {Msg}", ex.Message);
            }
        }

        // ── Katman 1: Altılı Yedek Zinciri ────────────────────────────────────
        var chain = new (string Name, Func<CancellationToken, Task<string>> Call)[]
        {
            ("Groq",        ct => _groq.GenerateResponseAsync(systemPrompt, userMessage, ct)),
            ("SambaNova",   ct => _sambaNova.GenerateResponseAsync(systemPrompt, userMessage, ct)),
            ("Cerebras",    ct => _cerebras.GenerateResponseAsync(systemPrompt, userMessage, ct)),
            ("Cohere",      ct => _cohere.GenerateResponseAsync(systemPrompt, userMessage, ct)),
            ("HuggingFace", ct => _huggingFace.GenerateResponseAsync(systemPrompt, userMessage, ct)),
            ("Mistral",     ct => _mistral.GenerateResponseAsync(systemPrompt, userMessage, ct)),
        };

        Exception? lastEx = null;

        foreach (var (name, call) in chain)
        {
            using var cts = new CancellationTokenSource(ProviderTimeout);
            try
            {
                _logger.LogDebug("[AIChain] {Provider} deneniyor...", name);
                var result = await call(cts.Token).WaitAsync(cts.Token);

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
    /// Context-aware failover: önce Gemini'yi dener, sonra Groq (context-native),
    /// ardından kalan zincir.
    /// </summary>
    public async Task<string> GetResponseWithFallbackAsync(IEnumerable<Message> context, string systemPrompt)
    {
        // 1. Gemini Smart Router — context özeti ile
        var contextSummary = BuildContextSummary(context);
        using (var cts0 = new CancellationTokenSource(ProviderTimeout))
        {
            try
            {
                _logger.LogDebug("[AIChain] Gemini (context-aware) deneniyor...");
                var geminiResult = await _gemini.GenerateSmartAsync(systemPrompt, contextSummary, cts0.Token)
                                               .WaitAsync(cts0.Token);
                if (IsUsableResponse(geminiResult))
                {
                    _logger.LogInformation("[AIChain] Gemini başarılı (context).");
                    return geminiResult;
                }
                _logger.LogWarning("[AIChain] Gemini boş/hatalı yanıt, Groq fallback'e geçiliyor.");
            }
            catch (OperationCanceledException) when (cts0.IsCancellationRequested)
            {
                _logger.LogWarning("[AIChain] Gemini (context) 10s timeout, Groq'a geçiliyor.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AIChain] Gemini failed, falling back to next provider. Sebep: {Msg}", ex.Message);
            }
        }

        // 2. Groq — tam context desteği
        using (var cts1 = new CancellationTokenSource(ProviderTimeout))
        {
            try
            {
                _logger.LogDebug("[AIChain] Groq (context-aware) deneniyor...");
                var groqResult = await _groq.GetResponseAsync(context, systemPrompt, cts1.Token).WaitAsync(cts1.Token);
                if (IsUsableResponse(groqResult))
                {
                    _logger.LogInformation("[AIChain] Groq başarılı (context).");
                    return groqResult;
                }
                _logger.LogWarning("[AIChain] Groq boş/hatalı yanıt döndü, fallover başlıyor.");
            }
            catch (OperationCanceledException) when (cts1.IsCancellationRequested)
            {
                _logger.LogWarning("[AIChain] Groq (context) 10s timeout aşıldı, failover başlıyor.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AIChain] Groq failed, falling back to next provider. Sebep: {Msg}", ex.Message);
            }
        }

        // 3. Kalan zincir: context'i metin olarak geçir
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
