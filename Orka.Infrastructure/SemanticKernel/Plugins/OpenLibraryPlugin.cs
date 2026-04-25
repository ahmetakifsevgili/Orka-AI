using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

/// <summary>
/// OpenLibraryPlugin — Kitap arama ve referans bulma.
///
/// Open Library API (tamamen ücretsiz, API key gerekmez):
///   - Milyonlarca kitap kaydı
///   - Başlık, yazar, yıl, kapak görseli, ISBN
///   - Akademik raporlarda kitap referansı oluşturmak için ideal
///
/// URL: https://openlibrary.org/dev/docs/api/search
/// </summary>
public class OpenLibraryPlugin
{
    private readonly HttpClient _httpClient;

    public OpenLibraryPlugin(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("OpenLibrary");
    }

    [KernelFunction, Description(
        "Kitap arar. Başlık, yazar, yayın yılı ve kapak görseli döner. " +
        "Araştırma raporlarında kitap referansı eklemek için kullan. " +
        "Ders kitapları, akademik kitaplar ve genel kaynakları bulmak için idealdir.")]
    public async Task<string> SearchBooks(
        [Description("Aranacak kitap konusu, başlığı veya yazar adı (İngilizce tercih edilir)")] string query,
        [Description("Döndürülecek maksimum sonuç sayısı (varsayılan: 5)")] int limit = 5)
    {
        try
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var url = $"search.json?q={encodedQuery}&limit={Math.Min(limit, 10)}&fields=title,author_name,first_publish_year,cover_i,isbn,subject,language,edition_count,key";

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return $"[Open Library hatası: {response.StatusCode}]";

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("docs", out var docs) || docs.GetArrayLength() == 0)
                return $"['{query}' için kitap bulunamadı]";

            var totalFound = doc.RootElement.TryGetProperty("numFound", out var nf) && nf.ValueKind == JsonValueKind.Number
                ? nf.GetInt32() : 0;

            var results = new StringBuilder();
            results.AppendLine($"**Kitap Arama Sonuçları** — \"{query}\" ({totalFound} toplam, {Math.Min(docs.GetArrayLength(), limit)} gösterildi)");
            results.AppendLine();

            int index = 1;
            foreach (var book in docs.EnumerateArray().Take(limit))
            {
                var title = book.TryGetProperty("title", out var t) ? t.GetString() : "Başlıksız";
                var year = book.TryGetProperty("first_publish_year", out var y) && y.ValueKind == JsonValueKind.Number
                    ? y.GetInt32().ToString() : "?";
                var editions = book.TryGetProperty("edition_count", out var ec) && ec.ValueKind == JsonValueKind.Number
                    ? ec.GetInt32() : 0;

                // Authors
                var authors = new List<string>();
                if (book.TryGetProperty("author_name", out var an) && an.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in an.EnumerateArray().Take(3))
                        authors.Add(a.GetString() ?? "");
                }
                var authorStr = authors.Count > 0 ? string.Join(", ", authors) : "Bilinmiyor";

                // Cover
                var coverUrl = "";
                if (book.TryGetProperty("cover_i", out var ci) && ci.ValueKind == JsonValueKind.Number)
                    coverUrl = $"https://covers.openlibrary.org/b/id/{ci.GetInt32()}-M.jpg";

                // Open Library URL
                var olKey = book.TryGetProperty("key", out var k) ? k.GetString() : "";
                var olUrl = !string.IsNullOrEmpty(olKey) ? $"https://openlibrary.org{olKey}" : "";

                // Subjects (ilk 5)
                var subjects = new List<string>();
                if (book.TryGetProperty("subject", out var subj) && subj.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in subj.EnumerateArray().Take(5))
                        subjects.Add(s.GetString() ?? "");
                }

                results.AppendLine($"[Kitap {index}] **{title}**");
                results.AppendLine($"  Yazar: {authorStr} | İlk Yayın: {year} | Edisyon: {editions}");
                if (!string.IsNullOrEmpty(olUrl))
                    results.AppendLine($"  URL: {olUrl}");
                if (!string.IsNullOrEmpty(coverUrl))
                    results.AppendLine($"  Kapak: {coverUrl}");
                if (subjects.Count > 0)
                    results.AppendLine($"  Konular: {string.Join(", ", subjects)}");

                results.AppendLine();
                index++;
            }

            return results.ToString();
        }
        catch (Exception ex)
        {
            return $"[Open Library servis hatası: {ex.Message}]";
        }
    }
}
