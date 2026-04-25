using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

/// <summary>
/// FreeDictionaryPlugin — İngilizce kelime sözlüğü.
///
/// Free Dictionary API (tamamen ücretsiz, API key gerekmez):
///   - Kelime tanımı, eş anlamlılar, fonetik, sesli telaffuz URL'si.
///   - TutorAgent teknik terimleri hızlıca açıklamak için kullanabilir.
///
/// URL: https://api.dictionaryapi.dev/
/// </summary>
public class FreeDictionaryPlugin
{
    private readonly HttpClient _httpClient;

    public FreeDictionaryPlugin(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("FreeDictionary");
    }

    [KernelFunction, Description(
        "İngilizce bir kelimenin tam sözlük anlamını, eş anlamlılarını ve fonetik okunuşunu getirir. " +
        "Tutor ajanının öğrenciye yeni, teknik kelimeleri veya kavramları net şekilde açıklaması için kullan.")]
    public async Task<string> DefineWord(
        [Description("Sözlükte aranacak kelime (İngilizce)")] string word)
    {
        try
        {
            var encodedWord = Uri.EscapeDataString(word);
            var url = $"api/v2/entries/en/{encodedWord}";

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return $"[Sözlük hatası: Kelime bulunamadı veya {response.StatusCode}]";

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var entries = doc.RootElement.EnumerateArray().ToList();
            if (entries.Count == 0)
                return $"['{word}' sözlükte bulunamadı]";

            var result = new StringBuilder();
            var firstEntry = entries[0];
            
            var wordText = firstEntry.TryGetProperty("word", out var w) ? w.GetString() : word;
            var phonetic = firstEntry.TryGetProperty("phonetic", out var ph) ? ph.GetString() : "";

            result.AppendLine($"**Kelime Anlamı:** {wordText} " + (!string.IsNullOrEmpty(phonetic) ? $"({phonetic})" : ""));

            if (firstEntry.TryGetProperty("meanings", out var meanings) && meanings.ValueKind == JsonValueKind.Array)
            {
                foreach (var meaning in meanings.EnumerateArray())
                {
                    var partOfSpeech = meaning.TryGetProperty("partOfSpeech", out var pos) ? pos.GetString() : "";
                    result.AppendLine($"\n_Tür: {partOfSpeech}_");

                    if (meaning.TryGetProperty("definitions", out var defs) && defs.ValueKind == JsonValueKind.Array)
                    {
                        int i = 1;
                        foreach (var def in defs.EnumerateArray().Take(3))
                        {
                            var definition = def.TryGetProperty("definition", out var d) ? d.GetString() : "";
                            if (!string.IsNullOrEmpty(definition))
                            {
                                result.AppendLine($"  {i}. {definition}");
                                i++;
                            }
                        }
                    }

                    if (meaning.TryGetProperty("synonyms", out var syns) && syns.ValueKind == JsonValueKind.Array && syns.GetArrayLength() > 0)
                    {
                        var synList = syns.EnumerateArray().Select(s => s.GetString()).ToList();
                        result.AppendLine($"  Eş Anlamlılar: {string.Join(", ", synList)}");
                    }
                }
            }

            return result.ToString();
        }
        catch (Exception ex)
        {
            return $"[Sözlük servis hatası: {ex.Message}]";
        }
    }
}
