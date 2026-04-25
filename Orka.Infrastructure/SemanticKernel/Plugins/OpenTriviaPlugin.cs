using System.ComponentModel;
using System.Text.Json;
using System.Text;
using Microsoft.SemanticKernel;
using System.Web;

namespace Orka.Infrastructure.SemanticKernel.Plugins;

/// <summary>
/// OpenTriviaPlugin — Quiz soruları.
///
/// Open Trivia DB API (tamamen ücretsiz, API key gerekmez):
///   - 4000+ hazır çoktan seçmeli/doğru-yanlış soru.
///   - CS, Math, Science kategorileri mevcut.
///
/// URL: https://opentdb.com/
/// </summary>
public class OpenTriviaPlugin
{
    private readonly HttpClient _httpClient;

    public OpenTriviaPlugin(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("OpenTrivia");
    }

    [KernelFunction, Description(
        "Open Trivia veritabanından hazır quiz soruları getirir. " +
        "Kullanılabilir kategori ID'leri: Computer Science (18), Mathematics (19), Science & Nature (17). " +
        "Zorluk (difficulty): easy, medium, hard. " +
        "Tutor ajanının öğrenciye hızlı çoktan seçmeli sorular sorması için kullan.")]
    public async Task<string> GetTriviaQuestions(
        [Description("Kategori ID'si (18: CS, 19: Math, 17: Science)")] int category,
        [Description("Zorluk derecesi (easy, medium, hard)")] string difficulty,
        [Description("Soru sayısı (maks 10)")] int amount = 3)
    {
        try
        {
            var url = $"api.php?amount={Math.Min(amount, 10)}&category={category}&difficulty={difficulty}&type=multiple";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return $"[Trivia API hatası: {response.StatusCode}]";

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                return "[Soru bulunamadı]";

            var sb = new StringBuilder();
            sb.AppendLine("**Hazır Quiz Soruları**");
            sb.AppendLine();

            int i = 1;
            foreach (var q in results.EnumerateArray())
            {
                var question = q.TryGetProperty("question", out var qq) ? HttpUtility.HtmlDecode(qq.GetString() ?? "") : "";
                var correctAnswer = q.TryGetProperty("correct_answer", out var ca) ? HttpUtility.HtmlDecode(ca.GetString() ?? "") : "";
                
                var incorrectAnswers = new List<string>();
                if (q.TryGetProperty("incorrect_answers", out var ia) && ia.ValueKind == JsonValueKind.Array)
                {
                    foreach (var inc in ia.EnumerateArray())
                    {
                        incorrectAnswers.Add(HttpUtility.HtmlDecode(inc.GetString() ?? ""));
                    }
                }

                sb.AppendLine($"Soru {i}: {question}");
                sb.AppendLine($"Doğru Cevap: {correctAnswer}");
                sb.AppendLine($"Yanlış Cevaplar: {string.Join(", ", incorrectAnswers)}");
                sb.AppendLine();
                i++;
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"[Trivia servis hatası: {ex.Message}]";
        }
    }
}
