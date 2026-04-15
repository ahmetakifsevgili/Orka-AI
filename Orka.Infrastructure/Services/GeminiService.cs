using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Google Gemini — Primary Smart Router.
///
/// Görev Tespiti (systemPrompt anahtar kelimeleri):
///   • "müfredat" | "alt başlık" | "deepplan" | "planlayıcı"
///       → Deep Plan modu  [temp:0.3, maxTokens:1024] — deterministik JSON
///   • "sınav" | "quiz" | "DOĞRU" | "YANLIŞ" | "değerlendir" | "pekiştirme"
///       → Quiz modu       [temp:0.2, maxTokens:512]  — seri, kesin yanıt
///   • Diğer tüm durumlar
///       → Tutor modu      [temp:0.7, maxTokens:2048] — akıcı ders anlatımı
///
/// Hata veya timeout → AIServiceChain üst katmanı fallback zincirine geçer.
/// </summary>
public class GeminiService : IGeminiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;

    // Görev-model eşleştirmeleri (appsettings'ten okunur, aynı model bile olsa config'den gelir)
    private readonly string _modelDeepPlan;
    private readonly string _modelTutor;
    private readonly string _modelQuiz;

    private readonly ILogger<GeminiService> _logger;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public GeminiService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GeminiService> logger)
    {
        _httpClient     = httpClientFactory.CreateClient("Gemini");
        _apiKey         = configuration["AI:Gemini:ApiKey"]       ?? throw new ArgumentException("Gemini API Key eksik.");
        _baseUrl        = configuration["AI:Gemini:BaseUrl"]       ?? "https://generativelanguage.googleapis.com/v1beta/models";
        _modelDeepPlan  = configuration["AI:Gemini:ModelDeepPlan"] ?? "gemini-2.5-flash";
        _modelTutor     = configuration["AI:Gemini:ModelTutor"]    ?? "gemini-2.5-flash";
        _modelQuiz      = configuration["AI:Gemini:ModelQuiz"]     ?? "gemini-2.5-flash";
        _logger         = logger;
    }

    public Task<string> GenerateSmartAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        var (taskType, model, temperature, maxTokens, topP, topK, stopSequences) = DetectTask(systemPrompt);
        _logger.LogInformation(
            "[GEMINI SMART ROUTER] Görev={Task} → Model={Model} (temp={Temp}, maxTokens={Tokens}, topP={TopP}, topK={TopK})",
            taskType, model, temperature, maxTokens, topP, topK);

        return CallGeminiAsync(systemPrompt, userMessage, model, temperature, maxTokens, topP, topK, stopSequences, ct);
    }

    public Task<string> GenerateWithModelAsync(string model, string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        _logger.LogInformation("[GEMINI DIRECT MODEL] Görev → Model={Model}", model);
        // Arka plan işlerinde daha deterministik ve uzun çıktılar istenir (maxTokens:4096, temp:0.3)
        return CallGeminiAsync(systemPrompt, userMessage, model, 0.3, 4096, 0.85, 40, null, ct);
    }

    // ── Görev tespiti — Öncelik: Quiz > DeepPlan > Tutor ─────────────────────
    private (string TaskType, string Model, double Temperature, int MaxTokens, double TopP, int TopK, string[]? StopSequences) DetectTask(string systemPrompt)
    {
        var lower = systemPrompt.ToLowerInvariant();

        // 1. Quiz / Değerlendirme (en kısıtlı, önce)
        if (lower.Contains("sınav")       ||
            lower.Contains("quiz")        ||
            lower.Contains("doğru")       ||
            lower.Contains("yanlış")      ||
            lower.Contains("değerlendir") ||
            lower.Contains("pekiştirme"))
        {
            return ("Quiz", _modelQuiz, 0.2, 512, 0.80, 20, new[] { "```\n\n" });
        }

        // 2. Deep Plan / Müfredat
        if (lower.Contains("müfredat")    ||
            lower.Contains("alt başlık")  ||
            lower.Contains("planlayıcı")  ||
            lower.Contains("deepplan")    ||
            lower.Contains("eğitim planı"))
        {
            return ("DeepPlan", _modelDeepPlan, 0.3, 1024, 0.85, 40, null);
        }

        // 3. Tutor / Ders Anlatımı (varsayılan)
        return ("Tutor", _modelTutor, 0.7, 2048, 0.95, 40, null);
    }

    // ── Gemini generateContent API ─────────────────────────────────────────────
    private async Task<string> CallGeminiAsync(
        string systemPrompt, string userMessage,
        string model, double temperature, int maxTokens,
        double topP = 0.95, int topK = 40, string[]? stopSequences = null,
        CancellationToken ct = default)
    {
        var endpoint = $"{_baseUrl.TrimEnd('/')}/{model}:generateContent?key={_apiKey}";

        // Gemini native format: system instruction ayrı, contents'da sadece user mesajı
        var requestBody = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = string.IsNullOrWhiteSpace(systemPrompt)
                    ? "Sen yardımcı bir asistansın."
                    : systemPrompt } }
            },
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = userMessage } } }
            },
            generationConfig = new
            {
                temperature,
                
                topP,
                topK,
                candidateCount   = 1,
                responseMimeType = "text/plain",
                stopSequences    = stopSequences ?? Array.Empty<string>()
            }
        };

        var json = JsonSerializer.Serialize(requestBody, _jsonOpts);
        AiDebugLogger.LogRequest("GEMINI", $"URL: {endpoint}\nModel: {model}\n{json[..Math.Min(json.Length, 400)]}");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.SendAsync(request, ct);
            var respStr  = await response.Content.ReadAsStringAsync(ct);
            AiDebugLogger.LogResponse("GEMINI", $"Status: {(int)response.StatusCode}\n{respStr[..Math.Min(respStr.Length, 500)]}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Gemini API Hatası: {Status} — {Error}", response.StatusCode,
                    respStr[..Math.Min(respStr.Length, 300)]);
                throw new HttpRequestException($"Gemini API hatası: {response.StatusCode} — {respStr[..Math.Min(respStr.Length, 200)]}");
            }

            using var doc = JsonDocument.Parse(respStr);
            return doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? string.Empty;
        }
        catch (HttpRequestException) { throw; }
        catch (Exception ex)
        {
            AiDebugLogger.LogError("GEMINI", ex.Message);
            _logger.LogError(ex, "Gemini API çağrısı başarısız.");
            throw new HttpRequestException($"Gemini servis hatası: {ex.Message}", ex);
        }
    }

    public async IAsyncEnumerable<string> StreamSmartAsync(string systemPrompt, string userMessage, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var (taskType, model, temperature, maxTokens, topP, topK, stopSequences) = DetectTask(systemPrompt);
        _logger.LogInformation("[GEMINI STREAM] Görev={Task} → Model={Model}", taskType, model);

        var endpoint = $"{_baseUrl.TrimEnd('/')}/{model}:streamGenerateContent?alt=sse&key={_apiKey}";

        var requestBody = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = string.IsNullOrWhiteSpace(systemPrompt) ? "Sen yardımcı bir asistansın." : systemPrompt } }
            },
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = userMessage } } }
            },
            generationConfig = new
            {
                temperature,
                
                topP,
                topK,
                candidateCount = 1,
                responseMimeType = "text/plain",
                stopSequences = stopSequences ?? Array.Empty<string>()
            }
        };

        var json = JsonSerializer.Serialize(requestBody, _jsonOpts);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Gemini Stream error: {response.StatusCode} - {err}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("data: "))
            {
                var data = line.Substring(6).Trim();
                using var doc = JsonDocument.Parse(data);
                
                // Gemini stream response pattern is slightly different
                var text = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                if (!string.IsNullOrEmpty(text))
                {
                    yield return text;
                }
            }
        }
    }
}
