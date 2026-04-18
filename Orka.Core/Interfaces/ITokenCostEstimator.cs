namespace Orka.Core.Interfaces;

/// <summary>
/// LLM token kullanımı ve USD maliyet tahmini.
/// Heuristic bazlı (char/token oranı) ve model bazlı fiyatlandırma tablosu.
///
/// Neden heuristic? — Provider API'leri token count döndürmüyor (GitHub/Groq/Gemini stream).
/// Gerçek değer ile fark genelde ±%10 içindedir ve Dashboard için yeterlidir.
/// Ileride provider response metadata parse edildiğinde bu servis güncellenebilir.
/// </summary>
public interface ITokenCostEstimator
{
    /// <summary>
    /// Bir metinden token sayısı tahmini (char/3.5 — Türkçe/İngilizce karışık).
    /// </summary>
    int EstimateTokens(string text);

    /// <summary>
    /// Verilen input + output metni için toplam token ve USD maliyeti döner.
    /// Model bilinmiyorsa varsayılan (orta düzey) fiyat kullanılır.
    /// </summary>
    (int totalTokens, decimal costUsd) Estimate(string model, string inputText, string outputText);
}
