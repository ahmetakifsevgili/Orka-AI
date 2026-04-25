using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

/// <summary>
/// LibreTranslatePlugin — Açık kaynak metin çeviri.
///
/// LibreTranslate API (ücretsiz public instance, API key opsiyonel):
///   - Çoklu dil metin çevirisi
///   - Yabancı kaynaklardan gelen araştırmaları Türkçe'ye çevirmek için
///
/// URL: https://libretranslate.com/
/// </summary>
public class LibreTranslatePlugin
{
    private readonly HttpClient _httpClient;

    public LibreTranslatePlugin(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("LibreTranslate");
    }

    [KernelFunction, Description(
        "Verilen bir metni hedef dile çevirir. " +
        "Araştırmalardaki İngilizce veya diğer dillerdeki sonuçları Türkçe'ye çevirmek için kullan. " +
        "Eğer dil belirtilmezse otomatik algılama yapar. Hedef dil varsayılan olarak 'tr' (Türkçe) dır.")]
    public async Task<string> TranslateText(
        [Description("Çevrilecek metin (en fazla 1-2 paragraf)")] string text,
        [Description("Hedef dil kodu (örn. 'tr', 'en', 'de', 'fr', 'es'). Varsayılan: 'tr'")] string targetLang = "tr",
        [Description("Kaynak dil kodu (dili biliyorsan 'en' ver, bilmiyorsan boş bırak veya 'auto' de)")] string sourceLang = "auto")
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        try
        {
            var requestObj = new
            {
                q = text,
                source = string.IsNullOrWhiteSpace(sourceLang) ? "auto" : sourceLang,
                target = targetLang,
                format = "text"
            };

            var content = new StringContent(JsonSerializer.Serialize(requestObj), Encoding.UTF8, "application/json");
            
            // Public instances can be rate limited, ideally host your own LibreTranslate
            // We use https://libretranslate.de/ as a public fallback endpoint
            var response = await _httpClient.PostAsync("translate", content);

            if (!response.IsSuccessStatusCode)
                return $"[Çeviri hatası: {response.StatusCode} (Public API limitlerine takılmış olabilir)]";

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("translatedText", out var tt))
                return tt.GetString() ?? "";

            return "[Çeviri metni bulunamadı]";
        }
        catch (Exception ex)
        {
            return $"[Çeviri servis hatası: {ex.Message}]";
        }
    }
}
