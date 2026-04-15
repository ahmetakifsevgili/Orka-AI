using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

/// <summary>
/// WikipediaPlugin — Hallucination önlemenin birinci kalkanı.
///
/// Wikipedia, editoryal denetimden geçmiş, güvenilir ansiklopedik içerik sağlar.
/// Web aramasında belirsiz kalan kavramlar, tanımlar ve tarihsel bilgiler için
/// Wikipedia'yı otorite kaynak olarak kullan.
///
/// API: https://en.wikipedia.org/api/rest_v1/ (public, token gerektirmez)
///      https://tr.wikipedia.org/api/rest_v1/ (Türkçe)
/// </summary>
public class WikipediaPlugin
{
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WikipediaPlugin(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("Wikipedia");
    }

    /// <summary>
    /// Wikipedia'da arama yapar ve en iyi eşleşmeleri döner.
    /// Hangi makaleyi okuyacağını seçmek için bunu kullan.
    /// </summary>
    [KernelFunction, Description(
        "Wikipedia'da arama yapar. Türkçe ve İngilizce sonuçları döner. " +
        "Bir kavram veya konu hakkında güvenilir temel bilgi almak için kullan. " +
        "Sonuç başlıklarından birini seçip GetWikipediaArticle ile tam makaleyi çek.")]
    public async Task<string> SearchWikipedia(
        [Description("Aranacak kavram veya konu")] string query)
    {
        var results = new StringBuilder();

        // Türkçe Wikipedia önce dene
        var trResult = await SearchAsync("tr", query);
        if (!string.IsNullOrWhiteSpace(trResult))
        {
            results.AppendLine("**Türkçe Wikipedia:**");
            results.AppendLine(trResult);
            results.AppendLine();
        }

        // İngilizce Wikipedia — daha kapsamlı
        var enResult = await SearchAsync("en", query);
        if (!string.IsNullOrWhiteSpace(enResult))
        {
            results.AppendLine("**İngilizce Wikipedia:**");
            results.AppendLine(enResult);
        }

        return results.Length > 0 ? results.ToString() : "[Wikipedia'da sonuç bulunamadı]";
    }

    /// <summary>
    /// Wikipedia makalesinin özetini ve temel bilgilerini getirir.
    /// Bir kavramın tanımı, tarihçesi veya temel gerçekleri için kullan.
    /// </summary>
    [KernelFunction, Description(
        "Belirli bir Wikipedia makalesinin tam özetini getirir. " +
        "Konu başlığını (Wikipedia URL'sindeki gibi) gir. " +
        "Güvenilir, kaynaklı tanımlar ve gerçekler için birincil kaynak olarak kullan.")]
    public async Task<string> GetWikipediaArticle(
        [Description("Wikipedia makale başlığı (örn: 'Machine_learning', 'Python_programlama_dili')")] string title,
        [Description("Dil kodu: 'tr' (Türkçe) veya 'en' (İngilizce). Varsayılan: tr")] string lang = "tr")
    {
        try
        {
            var encodedTitle = Uri.EscapeDataString(title.Replace(" ", "_"));
            var url          = $"https://{lang}.wikipedia.org/api/rest_v1/page/summary/{encodedTitle}";

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                // Türkçe yoksa İngilizce dene
                if (lang == "tr")
                    return await GetWikipediaArticle(title, "en");

                return $"[Wikipedia: '{title}' makalesi bulunamadı]";
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root      = doc.RootElement;

            var articleTitle = root.TryGetProperty("title",   out var t) ? t.GetString() : title;
            var extract      = root.TryGetProperty("extract", out var e) ? e.GetString() : "";
            var pageUrl      = root.TryGetProperty("content_urls", out var cu)
                               && cu.TryGetProperty("desktop", out var d)
                               && d.TryGetProperty("page", out var p)
                               ? p.GetString() : $"https://{lang}.wikipedia.org/wiki/{encodedTitle}";

            var result = new StringBuilder();
            result.AppendLine($"**Wikipedia: {articleTitle}**");
            result.AppendLine($"Kaynak: {pageUrl}");
            result.AppendLine();

            // Extract çok uzunsa ilk 1200 karakteri al
            if (!string.IsNullOrWhiteSpace(extract))
            {
                result.AppendLine(extract.Length > 1200 ? extract[..1200] + "..." : extract);
            }

            return result.ToString();
        }
        catch (Exception ex)
        {
            return $"[Wikipedia erişim hatası: {ex.Message}]";
        }
    }

    // ── Ortak arama ──────────────────────────────────────────────────────────

    private async Task<string> SearchAsync(string lang, string query)
    {
        try
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var url = $"https://{lang}.wikipedia.org/w/api.php" +
                      $"?action=opensearch&search={encodedQuery}&limit=3&format=json&redirects=resolve";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return "";

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            // opensearch formatı: [query, [titles], [descriptions], [urls]]
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() < 4)
                return "";

            var titles = doc.RootElement[1];
            var urls   = doc.RootElement[3];

            if (titles.GetArrayLength() == 0) return "";

            var sb = new StringBuilder();
            int i  = 0;
            foreach (var titleEl in titles.EnumerateArray())
            {
                var t = titleEl.GetString();
                var u = urls.GetArrayLength() > i ? urls[i].GetString() : "";
                sb.AppendLine($"- {t} → {u}");
                i++;
            }

            return sb.ToString();
        }
        catch
        {
            return "";
        }
    }
}
