using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// LLM token/maliyet tahmini servisi.
///
/// Token tahmini: Karakter sayısı / 3.5 (İngilizce ~4, Türkçe ~3, ortalama 3.5)
///
/// Fiyatlandırma (2026 Nisan - USD per 1M tokens):
///   GPT-4o              : input $2.50   output $10.00
///   GPT-4o-mini         : input $0.15   output $0.60
///   Meta-Llama-3.1-405B : input $3.00   output $3.00
///   Llama 3.3 70B (Groq): input $0.59   output $0.79
///   Gemini 2.5 Flash    : input $0.075  output $0.30
///
/// Notlar:
/// - GitHub Models free-tier sunar ancak underlying Azure OpenAI fiyatını kullanırız (raporlama amaçlı).
/// - Groq şu an free tier'da, ancak fair comparison için liste fiyat kullanılır.
/// </summary>
public sealed class TokenCostEstimator : ITokenCostEstimator
{
    private record ModelPrice(decimal InputPer1M, decimal OutputPer1M);

    private static readonly Dictionary<string, ModelPrice> Prices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gpt-4o"]                     = new(2.50m, 10.00m),
        ["gpt-4o-mini"]                = new(0.15m, 0.60m),
        ["Meta-Llama-3.1-405B-Instruct"] = new(3.00m, 3.00m),
        ["llama-3.3-70b-versatile"]   = new(0.59m, 0.79m),
        ["llama-3.3-70b"]              = new(0.59m, 0.79m),
        ["gemini-2.5-flash"]           = new(0.075m, 0.30m),
        ["gemini-1.5-flash"]           = new(0.075m, 0.30m),
    };

    // Bilinmeyen model → orta düzey varsayılan (gpt-4o-mini seviyesi)
    private static readonly ModelPrice Fallback = new(0.30m, 1.00m);

    private const double CharsPerToken = 3.5;

    public int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return (int)Math.Ceiling(text.Length / CharsPerToken);
    }

    public (int totalTokens, decimal costUsd) Estimate(string model, string inputText, string outputText)
    {
        var price = ResolvePrice(model);

        var inputTokens  = EstimateTokens(inputText);
        var outputTokens = EstimateTokens(outputText);

        var inputCost  = (decimal)inputTokens  * price.InputPer1M  / 1_000_000m;
        var outputCost = (decimal)outputTokens * price.OutputPer1M / 1_000_000m;

        // USD maliyetini 6 ondalık basamağa yuvarla (micro-cents düzeyi)
        var totalCost = Math.Round(inputCost + outputCost, 6);

        return (inputTokens + outputTokens, totalCost);
    }

    private static ModelPrice ResolvePrice(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return Fallback;

        if (Prices.TryGetValue(model, out var exact)) return exact;

        // Partial match — model string farklı formatta gelebilir
        foreach (var kv in Prices)
        {
            if (model.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }

        return Fallback;
    }
}
