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
    private readonly string? _apiKey;
    private readonly string _baseUrl;
    private readonly bool _useVertexAi;

    // Görev-model eşleştirmeleri (appsettings'ten okunur, aynı model bile olsa config'den gelir)
    private readonly string _modelDeepPlan;
    private readonly string _modelTutor;
    private readonly string _modelQuiz;
    private readonly int _maxTokensDeepPlan;
    private readonly int _maxTokensQuiz;
    private readonly int _maxTokensTutor;

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
        _useVertexAi    = configuration.GetValue<bool>("AI:Gemini:UseVertexAi") ||
                           (configuration["AI:Gemini:BaseUrl"]?.Contains("aiplatform.googleapis.com") ?? false);

        _apiKey         = configuration["AI:Gemini:ApiKey"];
        if (string.IsNullOrEmpty(_apiKey) && !_useVertexAi)
        {
            throw new ArgumentException("Gemini API Key veya Vertex AI yapılandırması eksik.");
        }

        _baseUrl        = configuration["AI:Gemini:BaseUrl"]       ?? "https://generativelanguage.googleapis.com/v1beta/models";
        _modelDeepPlan  = configuration["AI:Gemini:ModelDeepPlan"] ?? configuration["AI:Gemini:Model"] ?? "gemini-3.1-pro-preview";
        _modelTutor     = configuration["AI:Gemini:ModelTutor"]    ?? configuration["AI:Gemini:Model"] ?? "gemini-3.1-pro-preview";
        _modelQuiz      = configuration["AI:Gemini:ModelQuiz"]     ?? configuration["AI:Gemini:Model"] ?? "gemini-3.1-pro-preview";
        _maxTokensDeepPlan = Math.Max(configuration.GetValue<int?>("AI:Cost:RoleBudgets:DeepPlan:MaxOutputTokens") ?? 16384, 16384);
        _maxTokensQuiz     = Math.Max(configuration.GetValue<int?>("AI:Cost:RoleBudgets:Quiz:MaxOutputTokens") ?? 32768, 32768);
        _maxTokensTutor    = Math.Max(configuration.GetValue<int?>("AI:Cost:RoleBudgets:Tutor:MaxOutputTokens") ?? 2048, 2048);
        _logger         = logger;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        var credential = await Google.Apis.Auth.OAuth2.GoogleCredential.GetApplicationDefaultAsync();
        if (credential.IsCreateScopedRequired)
        {
            credential = credential.CreateScoped("https://www.googleapis.com/auth/cloud-platform");
        }
        return await credential.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct);
    }

    public Task<string> GenerateSmartAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        systemPrompt = PublicTextNormalizer.RepairMojibake(systemPrompt);
        userMessage = PublicTextNormalizer.RepairMojibake(userMessage);
        var (taskType, model, temperature, maxTokens, topP, topK, stopSequences) = DetectTask(systemPrompt);
        _logger.LogInformation(
            "[GEMINI SMART ROUTER] Görev={Task} → Model={Model} (temp={Temp}, maxTokens={Tokens}, topP={TopP}, topK={TopK})",
            taskType, model, temperature, maxTokens, topP, topK);

        return CallGeminiAsync(systemPrompt, userMessage, model, temperature, maxTokens, topP, topK, stopSequences, ct);
    }

    public Task<string> GenerateWithModelAsync(string model, string systemPrompt, string userMessage, CancellationToken ct = default, int? maxOutputTokens = null)
    {
        systemPrompt = PublicTextNormalizer.RepairMojibake(systemPrompt);
        userMessage = PublicTextNormalizer.RepairMojibake(userMessage);
        _logger.LogInformation("[GEMINI DIRECT MODEL] Görev → Model={Model}", model);
        // Arka plan işlerinde daha deterministik ve uzun çıktılar istenir (maxTokens:4096, temp:0.3)
        return CallGeminiAsync(systemPrompt, userMessage, model, 0.3, maxOutputTokens ?? 4096, 0.85, 40, null, ct);
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
            return ("Quiz", _modelQuiz, 0.2, _maxTokensQuiz, 0.80, 20, null);
        }

        // 2. Deep Plan / Müfredat
        if (lower.Contains("müfredat")    ||
            lower.Contains("alt başlık")  ||
            lower.Contains("planlayıcı")  ||
            lower.Contains("deepplan")    ||
            lower.Contains("eğitim planı"))
        {
            return ("DeepPlan", _modelDeepPlan, 0.3, _maxTokensDeepPlan, 0.85, 40, null);
        }

        // 3. Tutor / Ders Anlatımı (varsayılan)
        return ("Tutor", _modelTutor, 0.7, _maxTokensTutor, 0.95, 40, null);
    }

    // ── Gemini generateContent API ─────────────────────────────────────────────
    private string GetCleanModel(string model)
    {
        if (_baseUrl.TrimEnd('/').EndsWith("/models", StringComparison.OrdinalIgnoreCase) && model.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            return model.Substring("models/".Length);
        }
        return model;
    }

    private async Task<string> CallGeminiAsync(
        string systemPrompt, string userMessage,
        string model, double temperature, int maxTokens,
        double topP = 0.95, int topK = 40, string[]? stopSequences = null,
        CancellationToken ct = default)
    {
        var cleanModel = GetCleanModel(model);
        string endpoint;
        if (_useVertexAi)
        {
            endpoint = $"{_baseUrl.TrimEnd('/')}/{cleanModel}:generateContent";
        }
        else
        {
            endpoint = $"{_baseUrl.TrimEnd('/')}/{cleanModel}:generateContent?key={_apiKey}";
        }

        // Gemini native format: system instruction ayrı, contents'da sadece user mesajı
        var responseMimeType = WantsJsonResponse(systemPrompt, userMessage) ? "application/json" : "text/plain";
        var generationConfig = new Dictionary<string, object?>
        {
            ["temperature"] = temperature,
            ["maxOutputTokens"] = maxTokens,
            ["topP"] = topP,
            ["topK"] = topK,
            ["candidateCount"] = 1,
            ["responseMimeType"] = responseMimeType,
            ["stopSequences"] = stopSequences ?? Array.Empty<string>()
        };

        var thinkingLevel = ResolveThinkingLevel(cleanModel, systemPrompt, userMessage);
        if (!string.IsNullOrWhiteSpace(thinkingLevel))
        {
            generationConfig["thinkingConfig"] = new { thinkingLevel };
        }

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
            generationConfig
        };

        var json = JsonSerializer.Serialize(requestBody, _jsonOpts);
        var safeEndpointForLog = $"{_baseUrl.TrimEnd('/')}/{cleanModel}:generateContent";
        AiDebugLogger.LogRequest("GEMINI", $"URL: {safeEndpointForLog}\nModel: {cleanModel}\n{json}");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.ExpectContinue = false;
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        if (_useVertexAi)
        {
            var token = await GetAccessTokenAsync(ct);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        try
         {
            var response = await _httpClient.SendAsync(request, ct);
            var respStr  = await response.Content.ReadAsStringAsync(ct);
            AiDebugLogger.LogResponse("GEMINI", $"Status: {(int)response.StatusCode}\n{respStr[..Math.Min(respStr.Length, 500)]}");

            if (!response.IsSuccessStatusCode)
            {
                respStr = AiDebugLogger.BuildSafeLogPreview("GEMINI", "ERROR", respStr);
                _logger.LogError("Gemini API Hatası: {Status} — {Error}", response.StatusCode,
                    respStr[..Math.Min(respStr.Length, 300)]);
                throw AiProviderFailureMapper.FromResponse("Gemini", cleanModel, response, respStr);
            }

            using var doc = JsonDocument.Parse(respStr);
            var text = ExtractTextResponse(doc.RootElement);
            if (string.IsNullOrWhiteSpace(text))
            {
                var finishReason = TryGetFirstCandidateFinishReason(doc.RootElement);
                throw new InvalidOperationException($"Gemini response did not contain text output. FinishReason={finishReason ?? "unknown"}.");
            }

            return PublicTextNormalizer.RepairMojibake(text);
        }
        catch (HttpRequestException) { throw; }
        catch (Exception ex)
        {
            AiDebugLogger.LogError("GEMINI", ex.Message);
            _logger.LogError("Gemini API request failed. ExceptionType={ExceptionType}", ex.GetType().Name);
            throw AiProviderFailureMapper.FromException("Gemini", cleanModel, ex);
        }
    }

    private static string ExtractTextResponse(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array ||
            candidates.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        var first = candidates[0];
        if (!first.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.Object &&
                part.TryGetProperty("text", out var text) &&
                text.ValueKind == JsonValueKind.String)
            {
                sb.Append(text.GetString());
            }
        }

        return sb.ToString();
    }

    private static string? TryGetFirstCandidateFinishReason(JsonElement root)
    {
        if (root.TryGetProperty("candidates", out var candidates) &&
            candidates.ValueKind == JsonValueKind.Array &&
            candidates.GetArrayLength() > 0 &&
            candidates[0].TryGetProperty("finishReason", out var finishReason) &&
            finishReason.ValueKind == JsonValueKind.String)
        {
            return finishReason.GetString();
        }

        return null;
    }

    private static string? ResolveThinkingLevel(string model, string systemPrompt, string userMessage)
    {
        if (!model.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var text = $"{systemPrompt}\n{userMessage}";
        if (text.Contains("quiz", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("diagnostic", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("SADECE JSON", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("ONLY JSON", StringComparison.OrdinalIgnoreCase))
        {
            return "low";
        }

        if (text.Contains("deepplan", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("curriculum", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("plan", StringComparison.OrdinalIgnoreCase))
        {
            return "medium";
        }

        return "low";
    }

    private static bool WantsJsonResponse(string systemPrompt, string userMessage)
    {
        var text = $"{systemPrompt}\n{userMessage}";
        return text.Contains("SADECE JSON", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("ONLY JSON", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("JSON array", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("JSON object", StringComparison.OrdinalIgnoreCase);
    }

    public async IAsyncEnumerable<string> StreamSmartAsync(string systemPrompt, string userMessage, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        systemPrompt = PublicTextNormalizer.RepairMojibake(systemPrompt);
        userMessage = PublicTextNormalizer.RepairMojibake(userMessage);
        var (taskType, model, temperature, maxTokens, topP, topK, stopSequences) = DetectTask(systemPrompt);
        _logger.LogInformation("[GEMINI STREAM] Görev={Task} → Model={Model}", taskType, model);

        var cleanModel = GetCleanModel(model);
        string endpoint;
        if (_useVertexAi)
        {
            endpoint = $"{_baseUrl.TrimEnd('/')}/{cleanModel}:streamGenerateContent?alt=sse";
        }
        else
        {
            endpoint = $"{_baseUrl.TrimEnd('/')}/{cleanModel}:streamGenerateContent?alt=sse&key={_apiKey}";
        }

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
                maxOutputTokens = maxTokens,
                topP,
                topK,
                candidateCount = 1,
                responseMimeType = "text/plain",
                stopSequences = stopSequences ?? Array.Empty<string>()
            }
        };

        var json = JsonSerializer.Serialize(requestBody, _jsonOpts);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.ExpectContinue = false;
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        if (_useVertexAi)
        {
            var token = await GetAccessTokenAsync(ct);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            throw AiProviderFailureMapper.FromResponse("Gemini", cleanModel, response, err);
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
                    yield return PublicTextNormalizer.RepairMojibake(text);
                }
            }
        }
    }

    public async IAsyncEnumerable<string> StreamWithModelAsync(string model, string systemPrompt, string userMessage, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default, int? maxOutputTokens = null)
    {
        systemPrompt = PublicTextNormalizer.RepairMojibake(systemPrompt);
        userMessage = PublicTextNormalizer.RepairMojibake(userMessage);
        var (taskType, _, temperature, maxTokens, topP, topK, stopSequences) = DetectTask(systemPrompt);
        _logger.LogInformation("[GEMINI STREAM DIRECT] Görev={Task} → Model={Model}", taskType, model);

        var cleanModel = GetCleanModel(model);
        string endpoint;
        if (_useVertexAi)
        {
            endpoint = $"{_baseUrl.TrimEnd('/')}/{cleanModel}:streamGenerateContent?alt=sse";
        }
        else
        {
            endpoint = $"{_baseUrl.TrimEnd('/')}/{cleanModel}:streamGenerateContent?alt=sse&key={_apiKey}";
        }

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
                maxOutputTokens = maxOutputTokens ?? maxTokens,
                topP,
                topK,
                candidateCount = 1,
                responseMimeType = "text/plain",
                stopSequences = stopSequences ?? Array.Empty<string>()
            }
        };

        var json = JsonSerializer.Serialize(requestBody, _jsonOpts);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.ExpectContinue = false;
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        if (_useVertexAi)
        {
            var token = await GetAccessTokenAsync(ct);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            throw AiProviderFailureMapper.FromResponse("Gemini", cleanModel, response, err);
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
                
                var text = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                if (!string.IsNullOrEmpty(text))
                {
                    yield return PublicTextNormalizer.RepairMojibake(text);
                }
            }
        }
    }
}
