using System.ComponentModel;
using System.Text;
using System.Xml.Linq;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

/// <summary>
/// ArXivPlugin — Korteks V2 preprint akademik makale arama.
///
/// arXiv API (tamamen ücretsiz, API key gerekmez):
///   - Fizik, Matematik, Bilgisayar Bilimi, Biyoloji, Finans, İstatistik
///   - Tam özet, yazarlar, PDF linki, kategori
///   - Atom XML formatında yanıt
///
/// URL: https://export.arxiv.org/api/
/// </summary>
public class ArXivPlugin
{
    private readonly HttpClient _httpClient;

    public ArXivPlugin(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("ArXiv");
    }

    /// <summary>
    /// arXiv'de preprint akademik makale arar. CS, Matematik, Fizik vb. alanlarda.
    /// </summary>
    [KernelFunction, Description(
        "arXiv preprint veritabanında bilimsel makale arar. " +
        "Bilgisayar Bilimi, Matematik, Fizik, Biyoloji, İstatistik vb. alanlarda 2M+ preprint makale. " +
        "Her sonuçla birlikte tam özet, yazarlar, PDF linki ve kategori döner. " +
        "En güncel araştırmaları (henüz hakemli dergide yayımlanmamış olanları) bulmak için kullan.")]
    public async Task<string> SearchPapers(
        [Description("Aranacak konu veya anahtar kelime (İngilizce)")] string query,
        [Description("Döndürülecek maksimum sonuç sayısı (varsayılan: 5)")] int limit = 5)
    {
        try
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var url = $"query?search_query=all:{encodedQuery}&max_results={Math.Min(limit, 10)}&sortBy=relevance&sortOrder=descending";

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return $"[arXiv arama hatası: {response.StatusCode}]";

            var xml = await response.Content.ReadAsStringAsync();
            var xdoc = XDocument.Parse(xml);

            XNamespace atom = "http://www.w3.org/2005/Atom";
            XNamespace arxiv = "http://arxiv.org/schemas/atom";

            var entries = xdoc.Descendants(atom + "entry").ToList();

            if (entries.Count == 0)
                return $"['{query}' için arXiv'de makale bulunamadı]";

            var results = new StringBuilder();
            results.AppendLine($"**arXiv Preprint Sonuçları** — \"{query}\" ({entries.Count} sonuç)");
            results.AppendLine();

            int index = 1;
            foreach (var entry in entries)
            {
                var title = entry.Element(atom + "title")?.Value?.Trim().Replace("\n", " ").Replace("  ", " ") ?? "Başlıksız";
                var summary = entry.Element(atom + "summary")?.Value?.Trim().Replace("\n", " ").Replace("  ", " ") ?? "";
                var published = entry.Element(atom + "published")?.Value ?? "";
                var id = entry.Element(atom + "id")?.Value ?? "";

                // Yazarlar
                var authors = entry.Elements(atom + "author")
                    .Select(a => a.Element(atom + "name")?.Value ?? "")
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Take(4)
                    .ToList();
                var authorStr = authors.Count > 0 ? string.Join(", ", authors) : "Bilinmiyor";

                // Kategoriler
                var categories = entry.Elements(atom + "category")
                    .Select(c => c.Attribute("term")?.Value ?? "")
                    .Where(c => !string.IsNullOrEmpty(c))
                    .Take(3)
                    .ToList();
                var categoryStr = categories.Count > 0 ? string.Join(", ", categories) : "";

                // PDF link
                var pdfLink = entry.Elements(atom + "link")
                    .FirstOrDefault(l => l.Attribute("title")?.Value == "pdf")
                    ?.Attribute("href")?.Value ?? "";

                // Year
                var year = published.Length >= 4 ? published[..4] : "?";

                results.AppendLine($"[arXiv {index}] **{title}**");
                results.AppendLine($"  Yazarlar: {authorStr} | Yıl: {year}");
                if (!string.IsNullOrEmpty(categoryStr))
                    results.AppendLine($"  Kategoriler: {categoryStr}");
                results.AppendLine($"  URL: {id}");
                if (!string.IsNullOrEmpty(pdfLink))
                    results.AppendLine($"  PDF: {pdfLink}");

                // Özet (2000 karaktere kırp)
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    if (summary.Length > 2000)
                        summary = summary[..2000] + "...";
                    results.AppendLine($"  Özet: {summary}");
                }

                results.AppendLine();
                index++;
            }

            return results.ToString();
        }
        catch (Exception ex)
        {
            return $"[arXiv servis hatası: {ex.Message}]";
        }
    }
}
