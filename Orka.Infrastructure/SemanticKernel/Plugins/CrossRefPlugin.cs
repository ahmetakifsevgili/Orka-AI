using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

/// <summary>
/// CrossRefPlugin — Korteks V2 akademik metadata motoru.
///
/// CrossRef API (tamamen ücretsiz, API key gerekmez):
///   - 100M+ akademik makale metadata'sı
///   - DOI çözümleme, atıf ağları, yayıncı bilgisi
///   - Semantic Scholar'ı tamamlar (farklı veri kaynağı)
///
/// URL: https://api.crossref.org/
/// </summary>
public class CrossRefPlugin
{
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CrossRefPlugin(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("CrossRef");
    }

    /// <summary>
    /// Akademik makaleleri CrossRef veritabanında arar and DOI, yayıncı bilgisi döner.
    /// </summary>
    [KernelFunction, Description(
        "CrossRef akademik veritabanında makale arar. " +
        "100M+ makale — DOI, başlık, yazar, dergi, atıf sayısı, yayın yılı ve URL döner. " +
        "Semantic Scholar ile birlikte kullanarak daha geniş akademik kapsam sağla.")]
    public async Task<string> SearchWorks(
        [Description("Aranacak akademik konu veya anahtar kelime (İngilizce tercih edilir)")] string query,
        [Description("Döndürülecek maksimum sonuç sayısı (varsayılan: 6)")] int limit = 6)
    {
        try
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var url = $"works?query={encodedQuery}&rows={Math.Min(limit, 10)}&select=DOI,title,author,published-print,container-title,is-referenced-by-count,URL,abstract";

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return $"[CrossRef arama hatası: {response.StatusCode}]";

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("message", out var message) ||
                !message.TryGetProperty("items", out var items) ||
                items.GetArrayLength() == 0)
                return $"['{query}' için CrossRef'te makale bulunamadı]";

            var results = new StringBuilder();
            results.AppendLine($"**CrossRef Akademik Sonuçlar** — \"{query}\" ({items.GetArrayLength()} sonuç)");
            results.AppendLine();

            int index = 1;
            foreach (var item in items.EnumerateArray())
            {
                // Title
                var title = "Başlıksız";
                if (item.TryGetProperty("title", out var titleArr) && titleArr.ValueKind == JsonValueKind.Array && titleArr.GetArrayLength() > 0)
                    title = titleArr[0].GetString() ?? "Başlıksız";

                // DOI
                var doi = item.TryGetProperty("DOI", out var d) ? d.GetString() : "";

                // Year
                var year = "?";
                if (item.TryGetProperty("published-print", out var pp) && pp.TryGetProperty("date-parts", out var dp) &&
                    dp.ValueKind == JsonValueKind.Array && dp.GetArrayLength() > 0)
                {
                    var firstPart = dp[0];
                    if (firstPart.ValueKind == JsonValueKind.Array && firstPart.GetArrayLength() > 0)
                        year = firstPart[0].GetInt32().ToString();
                }

                // Citation count
                var citations = item.TryGetProperty("is-referenced-by-count", out var cc) && cc.ValueKind == JsonValueKind.Number
                    ? cc.GetInt32() : 0;

                // Journal
                var journal = "";
                if (item.TryGetProperty("container-title", out var ct) && ct.ValueKind == JsonValueKind.Array && ct.GetArrayLength() > 0)
                    journal = ct[0].GetString() ?? "";

                // Authors
                var authorNames = new List<string>();
                if (item.TryGetProperty("author", out var authors) && authors.ValueKind == JsonValueKind.Array)
                {
                    foreach (var author in authors.EnumerateArray().Take(3))
                    {
                        var family = author.TryGetProperty("family", out var f) ? f.GetString() : "";
                        var given = author.TryGetProperty("given", out var g) ? g.GetString() : "";
                        if (!string.IsNullOrEmpty(family))
                            authorNames.Add($"{given} {family}".Trim());
                    }
                }
                var authorStr = authorNames.Count > 0 ? string.Join(", ", authorNames) : "Bilinmiyor";

                // URL
                var url2 = item.TryGetProperty("URL", out var u) ? u.GetString() : $"https://doi.org/{doi}";

                results.AppendLine($"[CrossRef {index}] **{title}**");
                results.AppendLine($"  Yazarlar: {authorStr} | Yıl: {year} | Atıf: {citations}");
                if (!string.IsNullOrWhiteSpace(journal))
                    results.AppendLine($"  Dergi: {journal}");
                results.AppendLine($"  DOI: {doi}");
                results.AppendLine($"  URL: {url2}");

                // Abstract (varsa)
                if (item.TryGetProperty("abstract", out var ab) && !string.IsNullOrWhiteSpace(ab.GetString()))
                {
                    var abstractText = ab.GetString()!;
                    // CrossRef abstract'ları XML tag içerebilir, temizle
                    abstractText = System.Text.RegularExpressions.Regex.Replace(abstractText, "<[^>]+>", "");
                    if (abstractText.Length > 1500)
                        abstractText = abstractText[..1500] + "...";
                    results.AppendLine($"  Özet: {abstractText}");
                }

                results.AppendLine();
                index++;
            }

            return results.ToString();
        }
        catch (Exception ex)
        {
            return $"[CrossRef servis hatası: {ex.Message}]";
        }
    }
}
