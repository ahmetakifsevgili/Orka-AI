using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Smart Router + Yük Devretme Zinciri
///
/// Katman 0 — Groq (Primary)
/// Katman 1 — Gemini (Fallback)
///
/// Her sağlayıcıya 25 saniyelik katı zaman aşımı uygulanır.
/// IsUsableResponse() bilinen hata string'lerini filtreler.
/// </summary>
public class AIServiceChain : IAIServiceChain
{
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(20);

    private readonly IGroqService        _groq;
    private readonly IGeminiService      _gemini;
    private readonly ILogger<AIServiceChain> _logger;

    public AIServiceChain(
        IGroqService         groq,
        IGeminiService       gemini,
        ILogger<AIServiceChain> logger)
    {
        _groq   = groq;
        _gemini = gemini;
        _logger = logger;
    }

    public async Task<string> GenerateWithFallbackAsync(string systemPrompt, string userMessage)
    {
        // ── Katman 0: Groq Smart Router ──────────────────────────────
        using (var cts0 = new CancellationTokenSource(ProviderTimeout))
        {
            try
            {
                _logger.LogDebug("[AIChain] Groq (Smart Router) deneniyor...");
                var primaryResult = await _groq.GenerateResponseAsync(systemPrompt, userMessage, cts0.Token)
                                               .WaitAsync(cts0.Token);
                if (IsUsableResponse(primaryResult))
                {
                    _logger.LogInformation("[AIChain] Groq başarılı.");
                    return primaryResult;
                }
                _logger.LogWarning("[AIChain] Groq hatalı yanıt, fallback zincirine geçiliyor.");
            }
            catch (OperationCanceledException) when (cts0.IsCancellationRequested)
            {
                _logger.LogWarning("[AIChain] Groq 20s timeout, fallback zincirine geçiliyor.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AIChain] Groq başarısız, fallback zincirine geçiliyor. Sebep: {Msg}", ex.Message);
            }
        }

        // ── Katman 1: Gemini Fallback ─────────────────────────────────────────
        using (var cts1 = new CancellationTokenSource(ProviderTimeout))
        {
            try
            {
                _logger.LogDebug("[AIChain] Gemini (Fallback) deneniyor...");
                var geminiResult = await _gemini.GenerateSmartAsync(systemPrompt, userMessage, cts1.Token)
                                               .WaitAsync(cts1.Token);
                if (IsUsableResponse(geminiResult))
                {
                    _logger.LogInformation("[AIChain] Gemini başarılı.");
                    return geminiResult;
                }
                _logger.LogWarning("[AIChain] Gemini hatalı yanıt döndü.");
            }
            catch (OperationCanceledException) when (cts1.IsCancellationRequested)
            {
                _logger.LogWarning("[AIChain] Gemini timeout aşıldı.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AIChain] Gemini başarısız. Sebep: {Msg}", ex.Message);
            }
        }

        throw new InvalidOperationException("Tüm AI sağlayıcıları başarısız oldu. Lütfen daha sonra tekrar deneyin.");
    }

    /// <summary>
    /// Context-aware failover: önce Groq (context-native),
    /// ardından kalan zincir.
    /// </summary>
    public async Task<string> GetResponseWithFallbackAsync(IEnumerable<Message> context, string systemPrompt)
    {
        var contextSummary = BuildContextSummary(context);
        
        // 1. Groq — tam context desteği
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
                _logger.LogWarning("[AIChain] Groq (context) 20s timeout aşıldı, failover başlıyor.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AIChain] Groq failed, falling back to next provider. Sebep: {Msg}", ex.Message);
            }
        }

    // 3. Kalan zincir: context'i metin olarak geçir
        return await GenerateWithFallbackAsync(systemPrompt, contextSummary);
    }

    public async IAsyncEnumerable<string> GetResponseStreamWithFallbackAsync(IEnumerable<Message> context, string systemPrompt, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // 1. Groq — Stream desteği, 10s ilk token timeout
        bool started = false;
        IAsyncEnumerator<string>? groqEnum = null;

        using (var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            probeCts.CancelAfter(ProviderTimeout);
            try
            {
                groqEnum = _groq.GetResponseStreamAsync(context, systemPrompt, probeCts.Token)
                                 .GetAsyncEnumerator(probeCts.Token);
                if (await groqEnum.MoveNextAsync() && IsUsableResponse(groqEnum.Current))
                {
                    started = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[AIChain] Groq Stream başarısız/timeout: {Msg}", ex.Message);
            }
        }

        if (started && groqEnum != null)
        {
            yield return groqEnum.Current;
            while (await groqEnum.MoveNextAsync())
                yield return groqEnum.Current;
            await groqEnum.DisposeAsync();
            yield break;
        }

        if (groqEnum != null) await groqEnum.DisposeAsync();

        // 2. Fallback: Normal zinciri çağır ve sonucu tek parça stream et
        _logger.LogWarning("[AIChain] Groq Stream başarısız, normal fallback zincirine geçiliyor.");
        var fallbackResult = await GetResponseWithFallbackAsync(context, systemPrompt);
        yield return fallbackResult;
    }

    public async IAsyncEnumerable<string> GenerateStreamWithFallbackAsync(string systemPrompt, string userMessage, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // Groq — 10s ilk token timeout
        bool started = false;
        IAsyncEnumerator<string>? groqEnum = null;

        using (var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            probeCts.CancelAfter(ProviderTimeout);
            try
            {
                groqEnum = _groq.GenerateResponseStreamAsync(systemPrompt, userMessage, probeCts.Token)
                                 .GetAsyncEnumerator(probeCts.Token);
                if (await groqEnum.MoveNextAsync() && IsUsableResponse(groqEnum.Current))
                {
                    started = true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[AIChain] Groq Stream (simple) başarısız/timeout: {Msg}", ex.Message);
            }
        }

        if (started && groqEnum != null)
        {
            yield return groqEnum.Current;
            while (await groqEnum.MoveNextAsync())
                yield return groqEnum.Current;
            await groqEnum.DisposeAsync();
            yield break;
        }

        if (groqEnum != null) await groqEnum.DisposeAsync();

        _logger.LogWarning("[AIChain] Groq Stream (simple) başarısız, normal fallback zincirine geçiliyor.");
        var fallbackResult = await GenerateWithFallbackAsync(systemPrompt, userMessage);
        yield return fallbackResult;
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
        if (lower.Contains("kota doldu"))               return false;
        if (lower.Contains("servis şu an meşgul"))      return false;
        
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
