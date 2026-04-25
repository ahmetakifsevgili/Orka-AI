using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

/// <summary>
/// DatamusePlugin — Kelime keşif ve arama terimi zenginleştirme.
///
/// Datamuse API (tamamen ücretsiz, API key gerekmez):
///   - Anlamsal olarak ilişkili kelimeler (means like)
///   - Eş anlamlı, çağrışımlar, kafiyeler
///   - Arama terimlerini çeşitlendirmek için ideal
///
/// URL: https://api.datamuse.com/
/// </summary>
public class DatamusePlugin
{
    private readonly HttpClient _httpClient;

    public DatamusePlugin(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("Datamuse");
    }

    /// <summary>
    /// Bir kavramla anlamsal olarak ilişkili kelimeleri bulur.
    /// </summary>
    [KernelFunction, Description(
        "Bir kavram veya kelimeyle anlamsal olarak ilişkili terimleri bulur. " +
        "Araştırma sırasında arama terimlerini çeşitlendirmek için kullan. " +
        "Örneğin 'sorting algorithm' sorgusu quicksort, mergesort gibi ilişkili kavramları döner.")]
    public async Task<string> FindRelatedWords(
        [Description("İlişkili kelimeleri bulmak istediğin kavram (İngilizce)")] string concept,
        [Description("Döndürülecek maksimum sonuç sayısı (varsayılan: 10)")] int limit = 10)
    {
        try
        {
            var encodedConcept = Uri.EscapeDataString(concept);
            var url = $"words?ml={encodedConcept}&max={Math.Min(limit, 20)}";

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return $"[Datamuse hatası: {response.StatusCode}]";

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var words = doc.RootElement.EnumerateArray().ToList();

            if (words.Count == 0)
                return $"['{concept}' için ilişkili kelime bulunamadı]";

            var results = new StringBuilder();
            results.AppendLine($"**\"{concept}\" ile İlişkili Kavramlar** ({words.Count} sonuç):");

            foreach (var word in words)
            {
                var w = word.TryGetProperty("word", out var ww) ? ww.GetString() : "";
                var score = word.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0;

                if (!string.IsNullOrEmpty(w))
                    results.AppendLine($"  - {w} (ilişki skoru: {score})");
            }

            return results.ToString();
        }
        catch (Exception ex)
        {
            return $"[Datamuse servis hatası: {ex.Message}]";
        }
    }
}
