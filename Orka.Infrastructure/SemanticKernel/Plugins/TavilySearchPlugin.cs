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
        // Korteks V2: derin araştırma için varsayılan arttırıldı (8 → 12).
        _maxResults   = int.TryParse(configuration["AI:Albert:MaxSearchResults"], out var msr) ? msr : 12;
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
    /// 5 farklı açıdan paralel arama — derin araştırma için ilk dalga.
    /// Her sorgu bağımsız çalışır, sonuçlar birleştirilir.
    /// </summary>
    [KernelFunction, Description(
        "Aynı konuyu 5 farklı açıdan paralel olarak araştırır (ilk dalga). " +
        "Daha kapsamlı ve çok kaynaklı sonuçlar üretir. " +
        "Virgülle ayrılmış 3-5 sorgu gönder: 'sorgu1, sorgu2, sorgu3, sorgu4, sorgu5'. " +
        "İyi bir ilk dalga: tanım / tarihçe / teknik / pratik / güncel gelişmeler.")]
    public async Task<string> SearchWebDeep(
        [Description("Virgülle ayrılmış 3-5 farklı arama sorgusu")] string queries)
    {
        var queryList = queries
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(5)
            .ToArray();

        if (queryList.Length == 0) return await ExecuteSearchAsync(queries);

        var tasks   = queryList.Select(q => ExecuteSearchAsync(q)).ToArray();
        var results = await Task.WhenAll(tasks);

        var combined = new StringBuilder();
        for (int i = 0; i < queryList.Length; i++)
        {
            combined.AppendLine($"### İlk Dalga — Arama {i + 1}: \"{queryList[i]}\"");
            combined.AppendLine(results[i]);
            combined.AppendLine();
        }
        return combined.ToString();
    }

    /// <summary>
    /// İlk dalgadan sonra kalan boşlukları doldurmak için takip araması.
    /// İlk dalgada eksik kalan alt-konuları derinleştirmek amaçlı — AI karar verir.
    /// </summary>
    [KernelFunction, Description(
        "İlk dalgadan sonra kalan boşlukları doldurmak için ikinci dalga paralel arama. " +
        "İlk araştırmada yetersiz/eksik/çelişkili çıkan alt-konuları daha spesifik ara. " +
        "Virgülle ayrılmış 2-4 takip sorgusu gönder.")]
    public async Task<string> SearchWebFollowUp(
        [Description("Virgülle ayrılmış 2-4 takip sorgusu — boşluk dolduran")] string queries)
    {
        var queryList = queries
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(4)
            .ToArray();

        if (queryList.Length == 0) return "[Takip sorgu listesi boş]";

        var tasks   = queryList.Select(q => ExecuteSearchAsync(q)).ToArray();
        var results = await Task.WhenAll(tasks);

        var combined = new StringBuilder();
        for (int i = 0; i < queryList.Length; i++)
        {
            combined.AppendLine($"### İkinci Dalga (Takip) — Arama {i + 1}: \"{queryList[i]}\"");
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
                    // Korteks V2: Derin araştırma için 4000 karakter — akademik düzeyde kaynak okuma
                    content = rawText.Length > 4000 ? rawText[..4000] + "..." : rawText;
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

    // ── Korteks V2: Tavily Extract — URL'lerden tam sayfa içeriği çekme ──────

    /// <summary>
    /// Belirli URL'lerden tam sayfa içeriğini (tablolar, yapısal veri dahil) çeker.
    /// Arama sonuçlarından en değerli kaynakları seçip derinlemesine okumak için kullanılır.
    /// </summary>
    [KernelFunction, Description(
        "Belirli URL'lerden tam sayfa içeriğini çeker. Tablolar ve yapısal veri dahildir. " +
        "SearchWeb ile bulunan en değerli 3-5 kaynağın URL'lerini virgülle ayırarak gönder. " +
        "Akademik makaleler, teknik belgeler ve veri tabloları için idealdir.")]
    public async Task<string> ExtractFromUrls(
        [Description("Virgülle ayrılmış URL listesi (maks 5)")] string urls)
    {
        var urlList = urls
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(5)
            .ToArray();

        if (urlList.Length == 0) return "[URL listesi boş]";

        try
        {
            var requestBody = new
            {
                api_key = _tavilyApiKey,
                urls = urlList
            };

            var json = JsonSerializer.Serialize(requestBody, _jsonOpts);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tavily.com/extract");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _tavilyClient.SendAsync(request);
            var respStr = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return $"[Extract hatası: {response.StatusCode}]";

            using var doc = JsonDocument.Parse(respStr);
            var results = new StringBuilder();

            if (doc.RootElement.TryGetProperty("results", out var resultsArr))
            {
                int index = 1;
                foreach (var item in resultsArr.EnumerateArray())
                {
                    var url = item.TryGetProperty("url", out var u) ? u.GetString() : "";
                    var rawContent = item.TryGetProperty("raw_content", out var rc) ? rc.GetString() : "";

                    if (!string.IsNullOrWhiteSpace(rawContent))
                    {
                        // Tam sayfa içeriği — maks 6000 karakter (akademik derinlik)
                        var truncated = rawContent.Length > 6000 ? rawContent[..6000] + "\n[...devamı kırpıldı]" : rawContent;
                        results.AppendLine($"### [Derin Kaynak {index}] — {url}");
                        results.AppendLine(truncated);
                        results.AppendLine();
                    }
                    index++;
                }
            }

            if (doc.RootElement.TryGetProperty("failed_results", out var failedArr))
            {
                foreach (var fail in failedArr.EnumerateArray())
                {
                    var failUrl = fail.TryGetProperty("url", out var fu) ? fu.GetString() : "?";
                    var failErr = fail.TryGetProperty("error", out var fe) ? fe.GetString() : "Bilinmiyor";
                    results.AppendLine($"⚠️ Erişilemedi: {failUrl} — {failErr}");
                }
            }

            return results.Length > 0 ? results.ToString() : "[Extract sonuç döndürmedi]";
        }
        catch (Exception ex)
        {
            return $"[Extract servisi hatası: {ex.Message}]";
        }
    }
}
