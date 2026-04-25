using System.ComponentModel;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

/// <summary>
/// NewtonMathPlugin — Matematik işlemlerini doğrulama ve hesaplama motoru.
///
/// Newton API (tamamen ücretsiz, API key gerekmez):
///   - simplify, factor, derive, integrate, zeroes, tangent vb.
///   - TutorAgent için matematik problemleri çözüm doğrulama
///
/// URL: https://newton.vercel.app/
/// </summary>
public class NewtonMathPlugin
{
    private readonly HttpClient _httpClient;

    public NewtonMathPlugin(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("NewtonMath");
    }

    [KernelFunction, Description(
        "Matematiksel bir ifadeyi çözer veya basitleştirir. " +
        "Kullanılabilecek operasyonlar: simplify, factor, derive, integrate, zeroes, tangent, area, min, max, pi, abs. " +
        "Örnek: operation='simplify', expression='2^2+2(2)' -> Sonuç '8'. " +
        "Tutor ajanının öğrenci çözümlerini doğrulaması için idealdir.")]
    public async Task<string> CalculateMath(
        [Description("Yapılacak matematiksel işlem (simplify, factor, derive, integrate vb.)")] string operation,
        [Description("Çözülecek matematiksel ifade ör. 'x^2 - 4' veya '2^2+2(2)'")] string expression)
    {
        try
        {
            var encodedExpression = Uri.EscapeDataString(expression);
            var url = $"api/v2/{operation}/{encodedExpression}";

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
                return $"[Newton Math hatası: {response.StatusCode}]";

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("result", out var res))
            {
                var resultText = res.GetString() ?? "";
                return $"İşlem Sonucu ({operation} -> {expression}): {resultText}";
            }

            return $"[{operation} işlemi için sonuç bulunamadı]";
        }
        catch (Exception ex)
        {
            return $"[Newton Math servis hatası: {ex.Message}]";
        }
    }
}
