using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

/// <summary>
/// SemanticScholarPlugin — Korteks V2'nin akademik araştırma gözleri.
///
/// Semantic Scholar API (ücretsiz, API key opsiyonel):
///   - 200M+ akademik makale (hakemli)
///   - Atıf ağları, yazarlar, özetler
///   - arXiv, PubMed, ACM, IEEE vb. kaynaklardan beslenir
///
/// Hallucination Önleme: Tüm veriler gerçek akademik makalelerden gelir.
/// URL: https://api.semanticscholar.org/
/// </summary>
public class SemanticScholarPlugin
{
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SemanticScholarPlugin(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("SemanticScholar");
        _httpClient.BaseAddress = new Uri("https://api.semanticscholar.org/graph/v1/");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "OrkaAI-Korteks/2.0");
    }

    /// <summary>
    /// Akademik makale arar. Başlık, özet, yıl, atıf sayısı ve URL döner.
    /// </summary>
    [KernelFunction, Description(
        "Akademik makaleleri (hakemli bilimsel yayınlar) arar. " +
        "arXiv, PubMed, IEEE, ACM gibi akademik veritabanlarından sonuç getirir. " +
        "Her sonuçla birlikte başlık, özet, yıl, atıf sayısı ve makale URL'si döner. " +
        "Tez, araştırma raporu veya bilimsel doğrulama için kullan.")]
    public async Task<string> SearchPapers(
        [Description("Aranacak akademik konu veya anahtar kelime (İngilizce tercih edilir)")] string query,
        [Description("Döndürülecek maksimum sonuç sayısı (varsayılan: 8)")] int limit = 8)
    {
        try
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var url = $"paper/search?query={encodedQuery}&limit={Math.Min(limit, 15)}" +
                      "&fields=title,abstract,year,citationCount,url,authors,publicationTypes,journal";

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return $"[Akademik arama hatası: {response.StatusCode}]";

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
                return $"['{query}' için akademik makale bulunamadı]";

            var results = new StringBuilder();
            results.AppendLine($"**Akademik Makale Sonuçları** — \"{query}\" ({data.GetArrayLength()} sonuç)");
            results.AppendLine();

            int index = 1;
            foreach (var paper in data.EnumerateArray())
            {
                var title = paper.TryGetProperty("title", out var t) ? t.GetString() : "Başlıksız";
                var year = paper.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number ? y.GetInt32().ToString() : "?";
                var citations = paper.TryGetProperty("citationCount", out var cc) && cc.ValueKind == JsonValueKind.Number ? cc.GetInt32() : 0;
                var paperUrl = paper.TryGetProperty("url", out var u) ? u.GetString() : "";
                var abstractText = paper.TryGetProperty("abstract", out var ab) ? ab.GetString() : "";
                var paperId = paper.TryGetProperty("paperId", out var pid) ? pid.GetString() : "";

                // Yazarlar
                var authorNames = new List<string>();
                if (paper.TryGetProperty("authors", out var authors) && authors.ValueKind == JsonValueKind.Array)
                {
                    foreach (var author in authors.EnumerateArray().Take(3))
                    {
                        if (author.TryGetProperty("name", out var name))
                            authorNames.Add(name.GetString() ?? "");
                    }
                }
                var authorStr = authorNames.Count > 0 ? string.Join(", ", authorNames) : "Bilinmiyor";

                // Dergi
                var journalName = "";
                if (paper.TryGetProperty("journal", out var journal) && journal.TryGetProperty("name", out var jn))
                    journalName = jn.GetString() ?? "";

                results.AppendLine($"[Makale {index}] **{title}**");
                results.AppendLine($"  Yazarlar: {authorStr} | Yıl: {year} | Atıf: {citations}");
                if (!string.IsNullOrWhiteSpace(journalName))
                    results.AppendLine($"  Dergi: {journalName}");
                results.AppendLine($"  URL: {paperUrl}");
                if (!string.IsNullOrWhiteSpace(paperId))
                    results.AppendLine($"  PaperId: {paperId}");

                // Özet (2000 karaktere kırp)
                if (!string.IsNullOrWhiteSpace(abstractText))
                {
                    var truncAbstract = abstractText.Length > 2000 ? abstractText[..2000] + "..." : abstractText;
                    results.AppendLine($"  Özet: {truncAbstract}");
                }
                results.AppendLine();
                index++;
            }

            return results.ToString();
        }
        catch (Exception ex)
        {
            return $"[Akademik arama servisi hatası: {ex.Message}]";
        }
    }

    /// <summary>
    /// Belirli bir makalenin detaylarını, tam özetini ve atıf ağını getirir.
    /// </summary>
    [KernelFunction, Description(
        "Belirli bir akademik makalenin detaylı bilgisini getirir. " +
        "PaperId değerini SearchPapers sonucundan al. " +
        "Tam özet, tüm yazarlar, atıf yapılan ve atıf alan makaleler döner. " +
        "Bir makalenin güvenilirliğini ve etkisini değerlendirmek için kullan.")]
    public async Task<string> GetPaperDetails(
        [Description("Semantic Scholar Paper ID (SearchPapers sonucundan alınır)")] string paperId)
    {
        try
        {
            var url = $"paper/{Uri.EscapeDataString(paperId)}" +
                      "?fields=title,abstract,year,citationCount,referenceCount,influentialCitationCount," +
                      "url,authors,journal,publicationTypes,tldr,citations.title,citations.year,references.title";

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return $"[Makale detay hatası: {response.StatusCode}]";

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var result = new StringBuilder();

            var title = root.TryGetProperty("title", out var t) ? t.GetString() : "Başlıksız";
            var year = root.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number ? y.GetInt32().ToString() : "?";
            var citations = root.TryGetProperty("citationCount", out var cc) && cc.ValueKind == JsonValueKind.Number ? cc.GetInt32() : 0;
            var influential = root.TryGetProperty("influentialCitationCount", out var ic) && ic.ValueKind == JsonValueKind.Number ? ic.GetInt32() : 0;
            var refs = root.TryGetProperty("referenceCount", out var rc) && rc.ValueKind == JsonValueKind.Number ? rc.GetInt32() : 0;
            var paperUrl = root.TryGetProperty("url", out var u) ? u.GetString() : "";

            result.AppendLine($"## 📄 {title}");
            result.AppendLine($"**Yıl:** {year} | **Atıf Sayısı:** {citations} | **Etki Skoru:** {influential} | **Referans Sayısı:** {refs}");
            result.AppendLine($"**URL:** {paperUrl}");

            // TL;DR
            if (root.TryGetProperty("tldr", out var tldr) && tldr.TryGetProperty("text", out var tldrText))
                result.AppendLine($"\n**Kısa Özet (TL;DR):** {tldrText.GetString()}");

            // Full Abstract
            if (root.TryGetProperty("abstract", out var ab) && !string.IsNullOrWhiteSpace(ab.GetString()))
                result.AppendLine($"\n**Tam Özet:**\n{ab.GetString()}");

            // Authors
            if (root.TryGetProperty("authors", out var authors) && authors.ValueKind == JsonValueKind.Array)
            {
                var names = new List<string>();
                foreach (var a in authors.EnumerateArray().Take(10))
                {
                    if (a.TryGetProperty("name", out var n))
                        names.Add(n.GetString() ?? "");
                }
                result.AppendLine($"\n**Yazarlar:** {string.Join(", ", names)}");
            }

            // Top citing papers
            if (root.TryGetProperty("citations", out var cits) && cits.ValueKind == JsonValueKind.Array)
            {
                var citList = cits.EnumerateArray().Take(5).ToList();
                if (citList.Count > 0)
                {
                    result.AppendLine("\n**Bu Makaleye Atıf Yapan Önemli Çalışmalar:**");
                    foreach (var c in citList)
                    {
                        var cTitle = c.TryGetProperty("title", out var ct) ? ct.GetString() : "?";
                        var cYear = c.TryGetProperty("year", out var cy) && cy.ValueKind == JsonValueKind.Number ? cy.GetInt32().ToString() : "?";
                        result.AppendLine($"  - {cTitle} ({cYear})");
                    }
                }
            }

            return result.ToString();
        }
        catch (Exception ex)
        {
            return $"[Makale detay servisi hatası: {ex.Message}]";
        }
    }
}
