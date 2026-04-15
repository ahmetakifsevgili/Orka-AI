using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

/// <summary>
/// TavilySearchPlugin — Korteks'in web araştırma gözleri.
///
/// include_raw_content = true  → Tam sayfa içeriği (snippet değil)
/// include_answer = true       → Tavily'nin kendi AI özeti
/// SearchWebDeep               → 3 sorguyu paralel çalıştır
///
/// Hallucination önleme: Her sonuç için URL + başlık zorunlu döndürülür.
/// </summary>
public class TavilySearchPlugin
{
    private readonly HttpClient _tavilyClient;
    private readonly string _tavilyApiKey;
    private readonly int _maxResults;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public TavilySearchPlugin(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _tavilyClient = httpClientFactory.CreateClient("Tavily");
        _tavilyApiKey = configuration["AI:Tavily:ApiKey"] ?? throw new ArgumentException("Tavily API Key eksik.");
        _maxResults   = int.TryParse(configuration["AI:Albert:MaxSearchResults"], out var msr) ? msr : 5;
    }

    /// <summary>
    /// Tek sorgulu web araması. Tam sayfa içeriği + Tavily AI özeti döner.
    /// Her sonuç URL ile birlikte gelir — citation için kullanılır.
    /// </summary>
    [KernelFunction, Description(
        "Web üzerinde kapsamlı arama yapar. Her sonuçla birlikte URL ve tam içerik döner. " +
        "Bilgiyi doğrulamak ve kaynak göstermek için URL'leri kullan.")]
    public async Task<string> SearchWeb(
        [Description("Aranacak anahtar kelime veya soru")] string query)
    {
        return await ExecuteSearchAsync(query);
    }

    /// <summary>
    /// 3 farklı açıdan paralel arama — derin araştırma için.
    /// Her sorgu bağımsız çalışır, sonuçlar birleştirilir.
    /// </summary>
    [KernelFunction, Description(
        "Aynı konuyu 3 farklı açıdan paralel olarak araştırır. " +
        "Daha kapsamlı ve çok kaynaklı sonuçlar üretir. " +
        "Virgülle ayrılmış 3 sorgu gönder: 'sorgu1, sorgu2, sorgu3'")]
    public async Task<string> SearchWebDeep(
        [Description("Virgülle ayrılmış 2-3 farklı arama sorgusu")] string queries)
    {
        var queryList = queries
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(3)
            .ToArray();

        if (queryList.Length == 0) return await ExecuteSearchAsync(queries);

        // Paralel çalıştır
        var tasks   = queryList.Select(q => ExecuteSearchAsync(q)).ToArray();
        var results = await Task.WhenAll(tasks);

        var combined = new StringBuilder();
        for (int i = 0; i < queryList.Length; i++)
        {
            combined.AppendLine($"### Arama {i + 1}: \"{queryList[i]}\"");
            combined.AppendLine(results[i]);
            combined.AppendLine();
        }

        return combined.ToString();
    }

    // ── Ortak arama motoru ────────────────────────────────────────────────────

    private async Task<string> ExecuteSearchAsync(string query)
    {
        try
        {
            var requestBody = new
            {
                api_key             = _tavilyApiKey,
                query,
                search_depth        = "advanced",
                include_answer      = true,          // Tavily'nin AI özeti
                include_raw_content = true,          // Tam sayfa içeriği (hallucination önleme)
                max_results         = _maxResults
            };

            var json = JsonSerializer.Serialize(requestBody, _jsonOpts);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tavily.com/search");
            request.Content   = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _tavilyClient.SendAsync(request);
            var respStr  = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return $"[Arama hatası: {response.StatusCode}]";

            using var doc     = JsonDocument.Parse(respStr);
            var       results = new StringBuilder();

            // Tavily'nin kendi AI yanıtı (varsa)
            if (doc.RootElement.TryGetProperty("answer", out var answer) &&
                !string.IsNullOrWhiteSpace(answer.GetString()))
            {
                results.AppendLine($"**Tavily Özeti:** {answer.GetString()}");
                results.AppendLine();
            }

            if (!doc.RootElement.TryGetProperty("results", out var resultsArr))
                return "[Sonuç bulunamadı]";

            int index = 1;
            foreach (var item in resultsArr.EnumerateArray())
            {
                var title   = item.TryGetProperty("title",   out var t) ? t.GetString() : "Başlıksız";
                var url     = item.TryGetProperty("url",     out var u) ? u.GetString() : "";
                var content = item.TryGetProperty("content", out var c) ? c.GetString() : "";

                // raw_content varsa snippet yerine onu kullan (daha uzun, daha güvenilir)
                if (item.TryGetProperty("raw_content", out var raw) &&
                    !string.IsNullOrWhiteSpace(raw.GetString()))
                {
                    var rawText = raw.GetString()!;
                    // İlk 800 karakteri al — fazlası token israfı
                    content = rawText.Length > 800 ? rawText[..800] + "..." : rawText;
                }

                results.AppendLine($"[Kaynak {index}] {title}");
                results.AppendLine($"URL: {url}");
                results.AppendLine($"İçerik: {content}");
                results.AppendLine();
                index++;
            }

            return results.ToString();
        }
        catch (Exception ex)
        {
            return $"[Arama servisi hatası: {ex.Message}]";
        }
    }
}
