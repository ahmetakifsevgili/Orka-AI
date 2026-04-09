using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public class GroqService : IGroqService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<GroqService> _logger;
    private readonly IChaosContext _chaos;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public GroqService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GroqService> logger,
        IChaosContext chaos)
    {
        _httpClient = httpClientFactory.CreateClient("Groq");
        _apiKey = configuration["AI:Groq:ApiKey"] ?? throw new ArgumentException("Groq API Key eksik.");
        _model = configuration["AI:Groq:Model"] ?? "llama-3.3-70b-versatile";
        _logger = logger;
        _chaos = chaos;
    }

    // IAIService
    public Task<string> GenerateResponseAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
        => CallGroqApiAsync(userMessage, systemPrompt, ct);

    public async Task<RoutingResult> SemanticRouteAsync(string message, string? currentPhase = "Discovery")
    {
        var cleaned = "";
        try
        {
            var prompt = $@"Sen Orka AI'nın niyet analiz motorusun. Kullanıcı mesajını analiz et ve SADECE JSON döndür.

Kullanıcı Mesajı: ""{message}""
Mevcut Öğrenme Fazı: {currentPhase ?? "Discovery"}

KURALLAR:
1. ""intent"" SADECE şunlardan biri: greeting | new_topic | interview | plan | explain | research | quiz | summary | general
2. ""extractedTopic"": SADECE TEK bir konu adı. Hibrit konular yasaktır.
3. ""requiresNewPlan"": Yeni konu başlıyorsa true.
4. ""understoodConcept"": Kullanıcı kavramı anladığını belirtiyorsa true.

SADECE bu JSON formatında yanıt ver:
{{""intent"": ""greeting"", ""extractedTopic"": null, ""requiresNewPlan"": false, ""understoodConcept"": false}}";

            var result = await GetResponseAsync(new List<Message>(), prompt);
            cleaned = result.Trim();
            if (cleaned.StartsWith("```"))
                cleaned = cleaned.Replace("```json", "").Replace("```", "").Trim();

            var startIdx = cleaned.IndexOf('{');
            var endIdx = cleaned.LastIndexOf('}');
            if (startIdx >= 0 && endIdx > startIdx)
                cleaned = cleaned[startIdx..(endIdx + 1)];

            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            return new RoutingResult
            {
                Intent = root.TryGetProperty("intent", out var intentProp) ? intentProp.GetString()?.ToLower() ?? "general" : "general",
                ExtractedTopic = root.TryGetProperty("extractedTopic", out var topicProp) && topicProp.ValueKind != JsonValueKind.Null ? topicProp.GetString() : null,
                RequiresNewPlan = root.TryGetProperty("requiresNewPlan", out var planProp) && planProp.ValueKind == JsonValueKind.True,
                UnderstoodConcept = root.TryGetProperty("understoodConcept", out var underProp) && underProp.ValueKind == JsonValueKind.True,
                Method = "semantic"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Groq SemanticRoute parse hatası. Ham içerik: {Raw}", cleaned);
            return new RoutingResult
            {
                Intent = message.Trim().Length < 10 ? "greeting" : "general",
                Method = message.Trim().Length < 10 ? "heuristic" : "fallback"
            };
        }
    }

    public async Task<string> GeneratePlanAsync(string topicTitle, string intent = "genel öğrenme", string level = "orta")
    {
        var prompt = $@"Kullanıcı ""{topicTitle}"" konusunu ""{intent}"" amacı ve ""{level}"" seviyesinde öğrenmek istiyor.
Bu konu için 5-8 alt konudan oluşan bir öğrenme planı çıkar.
SADECE şu JSON formatında yanıt ver, başka hiçbir şey yazma:
[""Alt konu 1"",""Alt konu 2"",""Alt konu 3"",""Alt konu 4"",""Alt konu 5""]";

        return await CallGroqApiAsync(prompt, "Sen bir eğitim planlayıcısısın.");
    }

    public async Task<string> GetResponseAsync(IEnumerable<Message> context, string systemPrompt, CancellationToken ct = default)
    {
        var messages = new List<object>
        {
            new { role = "system", content = string.IsNullOrWhiteSpace(systemPrompt) ? "Sen yardımcı bir asistansın." : systemPrompt }
        };

        foreach (var msg in context.Where(m => !string.IsNullOrWhiteSpace(m.Content)))
        {
            var role = msg.Role?.ToLower() == "user" ? "user" : "assistant";
            messages.Add(new { role, content = msg.Content });
        }

        return await CallGroqChatApiAsync(messages, ct: ct);
    }

    public async Task<string> SummarizeSessionAsync(IEnumerable<Message> messages)
    {
        var text = string.Join("\n", messages.Select(c => $"{c.Role}: {c.Content}"));
        return await CallGroqApiAsync(
            $"Aşağıdaki öğrenme oturumunu 2-3 cümleyle özetle. Ne öğrenildi ve nerede bırakıldı?\n\n{text}",
            "Sen bir özetleme asistanısın.");
    }

    public async Task<string> ResearchAsync(string query, string depth = "medium")
    {
        var systemPrompt = depth == "deep"
            ? "Sen uzman bir araştırmacısın. Konu hakkında derinlemesine, detaylı ve yapılandırılmış bir araştırma raporu hazırla. Türkçe yaz."
            : "Sen bir bilgi asistanısın. Verilen konu hakkında kısa, net ve bilgilendirici bir özet hazırla. Türkçe yaz.";

        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = $"Şu konuyu araştır: {query}" }
        };

        return await CallGroqChatApiAsync(messages, depth == "deep" ? 4096 : 2048);
    }

    private async Task<string> CallGroqApiAsync(string prompt, string systemRole, CancellationToken ct = default)
    {
        var messages = new List<object>
        {
            new { role = "system", content = string.IsNullOrWhiteSpace(systemRole) ? "Sen yardımcı bir asistansın." : systemRole },
            new { role = "user", content = prompt }
        };
        return await CallGroqChatApiAsync(messages, ct: ct);
    }

    private async Task<string> CallGroqChatApiAsync(object messages, int maxTokens = 2048, CancellationToken ct = default)
    {
        // ── CHAOS MONKEY ────────────────────────────────────────────────────────
        // X-Chaos-Fail: Groq header değeri ChaosContext üzerinden gelir
        if (_chaos.IsProviderFailing("Groq"))
        {
            _logger.LogWarning("[CHAOS MONKEY] 🐒 Groq servis başarısızlığı simüle ediliyor!");
            throw new HttpRequestException("CHAOS MONKEY: Groq is down!");
        }
        // ────────────────────────────────────────────────────────────────────────

        const string RequestUrl = "https://api.groq.com/openai/v1/chat/completions";

        var requestBody = new
        {
            model = _model,
            messages,
            temperature = 0.7,
            max_tokens = maxTokens
        };

        var jsonBody = JsonSerializer.Serialize(requestBody, _jsonOptions);
        AiDebugLogger.LogRequest("GROQ", $"URL: {RequestUrl}\nModel: {_model}\n{jsonBody}");

        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, RequestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = content;

        try
        {
            var response = await _httpClient.SendAsync(request, ct);
            var responseString = await response.Content.ReadAsStringAsync(ct);
            AiDebugLogger.LogResponse("GROQ", $"Status: {(int)response.StatusCode}\n{responseString}");

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("Groq Rate Limit (429) aşıldı.");
                return "Mentorunuz şu an çok yoğun ders çalışıyor (kota doldu), lütfen bir dakika sonra tekrar konuşur musun? 🧘‍♂️";
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Groq API Hatası: {Status} - {Error}", response.StatusCode, responseString);
                return "Şu an fikirlerimi toparlamakta zorlanıyorum, lütfen tekrar dener misin? (API Hatası)";
            }

            using var jsonDoc = JsonDocument.Parse(responseString);
            return jsonDoc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            // ADIM 1 (Diagnostic): Gerçek hata tipini konsola yaz
            Console.WriteLine($"[GROQ-EXCEPTION] {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
            AiDebugLogger.LogError("GROQ", $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            _logger.LogError(ex, "Groq API çağrısı başarısız. Tip: {ExType}", ex.GetType().Name);
            return "Zihnim biraz karıştı, hemen kendime geliyorum. Lütfen tekrar sor.";
        }
    }
}
