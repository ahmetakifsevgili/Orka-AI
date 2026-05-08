using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

/// <summary>
/// AcademicSearchPlugin — Semantic Scholar + ArXiv akademik kaynak motoru.
///
/// Korteks'in derin araştırma akışında Wikipedia + Tavily'nin tamamlayıcısı:
///   - Wikipedia: ansiklopedik tanım
///   - Tavily:    güncel web içeriği
///   - Bu plugin: peer-reviewed akademik makaleler (citation veren)
///
/// API: https://api.semanticscholar.org/graph/v1/paper/search (public, key opsiyonel)
///      http://export.arxiv.org/api/query (Atom feed, public)
/// </summary>
public class AcademicSearchPlugin
{
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AcademicSearchPlugin(IHttpClientFactory httpClientFactory)
    {
        // Wikipedia ile aynı User-Agent havuzunu kullan (rate-friendly)
        _httpClient = httpClientFactory.CreateClient("Wikipedia");
    }

    [KernelFunction, Description(
        "Semantic Scholar üzerinden 200M+ peer-reviewed makaleden konuyla ilgili olanları arar. " +
        "Bilimsel iddiaları doğrulamak veya akademik kaynak göstermek için kullan. " +
        "Her sonuçta başlık, yazar, yıl, atıf sayısı ve TLDR özet döner.")]
    public async Task<string> SearchSemanticScholar(
        [Description("Aranacak akademik konu (İngilizce daha iyi sonuç verir)")] string query,
        [Description("Maksimum sonuç sayısı (1-10)")] int limit = 5)
    {
        try
        {
            limit = Math.Clamp(limit, 1, 10);
            var encoded = HttpUtility.UrlEncode(query);
            var url = $"https://api.semanticscholar.org/graph/v1/paper/search?query={encoded}" +
                      $"&limit={limit}&fields=title,authors,year,citationCount,tldr,externalIds,url";

            using var resp = await _httpClient.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
                return $"[Semantic Scholar] Ağ hatası: {resp.StatusCode}";

            var raw = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(raw);

            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.GetArrayLength() == 0)
            {
                return $"[Semantic Scholar] '{query}' için sonuç bulunamadı.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"**Semantic Scholar — {data.GetArrayLength()} akademik kaynak:**\n");

            int idx = 1;
            foreach (var paper in data.EnumerateArray())
            {
                var title = paper.TryGetProperty("title", out var t) ? t.GetString() ?? "Başlıksız" : "Başlıksız";
                var year  = paper.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number ? y.GetInt32() : 0;
                var citations = paper.TryGetProperty("citationCount", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0;

                string authorsStr = "Bilinmeyen yazar(lar)";
                if (paper.TryGetProperty("authors", out var auth) && auth.ValueKind == JsonValueKind.Array)
                {
                    var names = auth.EnumerateArray()
                        .Take(3)
                        .Select(a => a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "")
                        .Where(n => !string.IsNullOrWhiteSpace(n));
                    authorsStr = string.Join(", ", names);
                    if (auth.GetArrayLength() > 3) authorsStr += " et al.";
                }

                string tldr = "";
                if (paper.TryGetProperty("tldr", out var tl) &&
                    tl.ValueKind == JsonValueKind.Object &&
                    tl.TryGetProperty("text", out var tlText))
                {
                    tldr = tlText.GetString() ?? "";
                }

                string url2 = paper.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(url2) &&
                    paper.TryGetProperty("externalIds", out var ext) &&
                    ext.ValueKind == JsonValueKind.Object &&
                    ext.TryGetProperty("DOI", out var doi))
                {
                    url2 = $"https://doi.org/{doi.GetString()}";
                }

                sb.AppendLine($"{idx}. **{title}** ({year}) — {authorsStr}");
                if (citations > 0) sb.AppendLine($"   Atıf: {citations}");
                if (!string.IsNullOrWhiteSpace(tldr)) sb.AppendLine($"   TLDR: {tldr}");
                if (!string.IsNullOrWhiteSpace(url2)) sb.AppendLine($"   Kaynak: {url2}");
                sb.AppendLine();
                idx++;
            }

            return sb.ToString();
        }
        catch (Exception)
        {
            return "[semantic-scholar:degraded] Akademik arama gecici olarak kullanilamiyor.";
        }
    }

    [KernelFunction, Description(
        "ArXiv preprint sunucusundan en güncel araştırma makalelerini arar. " +
        "Yeni teknolojiler, henüz peer-review edilmemiş ama önemli makaleler için kullan. " +
        "AI, fizik, matematik, CS alanlarında özellikle güçlü.")]
    public async Task<string> SearchArXiv(
        [Description("Aranacak konu (İngilizce daha iyi)")] string query,
        [Description("Maksimum sonuç (1-10)")] int limit = 5)
    {
        try
        {
            limit = Math.Clamp(limit, 1, 10);
            var encoded = HttpUtility.UrlEncode(query);
            var url = $"http://export.arxiv.org/api/query?search_query=all:{encoded}" +
                      $"&start=0&max_results={limit}&sortBy=relevance&sortOrder=descending";

            using var resp = await _httpClient.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
                return $"[ArXiv] Ağ hatası: {resp.StatusCode}";

            var xml = await resp.Content.ReadAsStringAsync();
            // Hafif XML parse — büyük dependency çekmemek için manuel
            var entries = ExtractArxivEntries(xml).Take(limit).ToList();
            if (entries.Count == 0)
                return $"[ArXiv] '{query}' için sonuç bulunamadı.";

            var sb = new StringBuilder();
            sb.AppendLine($"**ArXiv — {entries.Count} preprint:**\n");
            int idx = 1;
            foreach (var e in entries)
            {
                sb.AppendLine($"{idx}. **{e.Title}** ({e.Published})");
                if (!string.IsNullOrWhiteSpace(e.Authors)) sb.AppendLine($"   Yazarlar: {e.Authors}");
                if (!string.IsNullOrWhiteSpace(e.Summary))
                {
                    var summary = e.Summary.Length > 300 ? e.Summary[..300] + "..." : e.Summary;
                    sb.AppendLine($"   Özet: {summary}");
                }
                if (!string.IsNullOrWhiteSpace(e.Link)) sb.AppendLine($"   Kaynak: {e.Link}");
                sb.AppendLine();
                idx++;
            }
            return sb.ToString();
        }
        catch (Exception)
        {
            return "[arxiv:degraded] ArXiv aramasi gecici olarak kullanilamiyor.";
        }
    }

    private record ArxivEntry(string Title, string Authors, string Summary, string Link, string Published);

    private static List<ArxivEntry> ExtractArxivEntries(string xml)
    {
        var list = new List<ArxivEntry>();
        try
        {
            var xdoc = System.Xml.Linq.XDocument.Parse(xml);
            System.Xml.Linq.XNamespace atom = "http://www.w3.org/2005/Atom";
            foreach (var entry in xdoc.Descendants(atom + "entry"))
            {
                var title = (entry.Element(atom + "title")?.Value ?? "").Replace("\n", " ").Trim();
                var summary = (entry.Element(atom + "summary")?.Value ?? "").Replace("\n", " ").Trim();
                var link = entry.Element(atom + "id")?.Value ?? "";
                var publishedRaw = entry.Element(atom + "published")?.Value ?? "";
                var published = publishedRaw.Length >= 10 ? publishedRaw[..10] : publishedRaw;

                var authorList = entry.Elements(atom + "author")
                    .Select(a => a.Element(atom + "name")?.Value ?? "")
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Take(3)
                    .ToList();
                var authors = string.Join(", ", authorList);
                if (entry.Elements(atom + "author").Count() > 3) authors += " et al.";

                list.Add(new ArxivEntry(title, authors, summary, link, published));
            }
        }
        catch
        {
            // Parse fail → boş liste döner; üstteki try-catch sonucu yakalar
        }
        return list;
    }
}