using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

/// <summary>
/// TavilySearchPlugin â€” Korteks'in web araÅŸtÄ±rma gÃ¶zleri.
///
/// include_raw_content = true  â†’ Tam sayfa iÃ§eriÄŸi (snippet deÄŸil)
/// include_answer = true       â†’ Tavily'nin kendi AI Ã¶zeti
/// SearchWebDeep               â†’ 3 sorguyu paralel Ã§alÄ±ÅŸtÄ±r
///
/// Hallucination Ã¶nleme: Her sonuÃ§ iÃ§in URL + baÅŸlÄ±k zorunlu dÃ¶ndÃ¼rÃ¼lÃ¼r.
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
    /// Tek sorgulu web aramasÄ±. Tam sayfa iÃ§eriÄŸi + Tavily AI Ã¶zeti dÃ¶ner.
    /// Her sonuÃ§ URL ile birlikte gelir â€” citation iÃ§in kullanÄ±lÄ±r.
    /// </summary>
    [KernelFunction, Description(
        "Web Ã¼zerinde kapsamlÄ± arama yapar. Her sonuÃ§la birlikte URL ve tam iÃ§erik dÃ¶ner. " +
        "Bilgiyi doÄŸrulamak ve kaynak gÃ¶stermek iÃ§in URL'leri kullan.")]
    public async Task<string> SearchWeb(
        [Description("Aranacak anahtar kelime veya soru")] string query)
    {
        return await ExecuteSearchAsync(query);
    }

    /// <summary>
    /// 3 farklÄ± aÃ§Ä±dan paralel arama â€” derin araÅŸtÄ±rma iÃ§in.
    /// Her sorgu baÄŸÄ±msÄ±z Ã§alÄ±ÅŸÄ±r, sonuÃ§lar birleÅŸtirilir.
    /// </summary>
    [KernelFunction, Description(
        "AynÄ± konuyu 3 farklÄ± aÃ§Ä±dan paralel olarak araÅŸtÄ±rÄ±r. " +
        "Daha kapsamlÄ± ve Ã§ok kaynaklÄ± sonuÃ§lar Ã¼retir. " +
        "VirgÃ¼lle ayrÄ±lmÄ±ÅŸ 3 sorgu gÃ¶nder: 'sorgu1, sorgu2, sorgu3'")]
    public async Task<string> SearchWebDeep(
        [Description("VirgÃ¼lle ayrÄ±lmÄ±ÅŸ 2-3 farklÄ± arama sorgusu")] string queries)
    {
        var queryList = queries
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(3)
            .ToArray();

        if (queryList.Length == 0) return await ExecuteSearchAsync(queries);

        // Paralel Ã§alÄ±ÅŸtÄ±r
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

    // â”€â”€ Ortak arama motoru â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private async Task<string> ExecuteSearchAsync(string query)
    {
        try
        {
            var requestBody = new
            {
                api_key             = _tavilyApiKey,
                query,
                search_depth        = "advanced",
                include_answer      = true,          // Tavily'nin AI Ã¶zeti
                include_raw_content = true,          // Tam sayfa iÃ§eriÄŸi (hallucination Ã¶nleme)
                max_results         = _maxResults
            };

            var json = JsonSerializer.Serialize(requestBody, _jsonOpts);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tavily.com/search");
            request.Content   = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _tavilyClient.SendAsync(request);
            var respStr  = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return $"[Arama hatasÄ±: {response.StatusCode}]";

            using var doc     = JsonDocument.Parse(respStr);
            var       results = new StringBuilder();

            // Tavily'nin kendi AI yanÄ±tÄ± (varsa)
            if (doc.RootElement.TryGetProperty("answer", out var answer) &&
                !string.IsNullOrWhiteSpace(answer.GetString()))
            {
                results.AppendLine($"**Tavily Ã–zeti:** {answer.GetString()}");
                results.AppendLine();
            }

            if (!doc.RootElement.TryGetProperty("results", out var resultsArr))
                return "[SonuÃ§ bulunamadÄ±]";

            int index = 1;
            foreach (var item in resultsArr.EnumerateArray())
            {
                var title   = item.TryGetProperty("title",   out var t) ? t.GetString() : "BaÅŸlÄ±ksÄ±z";
                var url     = item.TryGetProperty("url",     out var u) ? u.GetString() : "";
                var content = item.TryGetProperty("content", out var c) ? c.GetString() : "";

                // raw_content varsa snippet yerine onu kullan (daha uzun, daha gÃ¼venilir)
                if (item.TryGetProperty("raw_content", out var raw) &&
                    !string.IsNullOrWhiteSpace(raw.GetString()))
                {
                    var rawText = raw.GetString()!;
                    // Ä°lk 800 karakteri al â€” fazlasÄ± token israfÄ±
                    content = rawText.Length > 800 ? rawText[..800] + "..." : rawText;
                }

                results.AppendLine($"[Kaynak {index}] {title}");
                results.AppendLine($"URL: {url}");
                results.AppendLine($"Ä°Ã§erik: {content}");
                results.AppendLine();
                index++;
            }

            return results.ToString();
        }
        catch (Exception)
        {
            return "[web:degraded] Arama servisi gecici olarak kullanilamiyor.";
        }
    }
}