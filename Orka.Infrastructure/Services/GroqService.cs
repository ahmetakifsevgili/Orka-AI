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
    public Task<string> GenerateResponseAsync(string systemPrompt, string userMessage, string? model = null, CancellationToken ct = default)
        => CallGroqApiAsync(userMessage, systemPrompt, ct);

    public async IAsyncEnumerable<string> GenerateResponseStreamAsync(string systemPrompt, string userMessage, [System.Runtime.CompilerServices.EnumeratorCancellation] string? model = null, CancellationToken ct = default)
    {
        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userMessage }
        };

        await foreach (var chunk in CallGroqChatStreamApiAsync(messages, ct: ct))
        {
            yield return chunk;
        }
    }

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
2. ""extractedTopic"": SADECE TEK bir konu adı. Konu bir sohbet (""Nasılsın"", ""Gündem"", ""Sohbet"") ise kısa bir sohbet etiketi yaz.
3. ""requiresNewPlan"": Eğer kullanıcı gerçekten yeni ve KAPSAMLI bir müfredat (Örn: C#, Felsefe) istiyorsa true dön. Sadece selam verdiyse veya sohbet ettiyse false dön.
4. ""category"": Eğer öğrenmeye/müfredata yönelik detaylı bir alansa ""Plan"" dön. Eğer sadece sıradan bir sohbete niyetlendiyse (Nasılsın vs) veya requiresNewPlan false ise ""Chat"" dön.
5. ""understoodConcept"": Kullanıcı kavramı anladığını belirtiyorsa true.

SADECE bu JSON formatında yanıt ver:
{{""intent"": ""greeting"", ""extractedTopic"": ""Genel Sohbet"", ""requiresNewPlan"": false, ""category"": ""Chat"", ""understoodConcept"": false}}";

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
                Category = root.TryGetProperty("category", out var catProp) && catProp.ValueKind != JsonValueKind.Null ? (catProp.GetString() ?? "Chat") : "Chat",
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
        var messages = PrepareMessages(context, systemPrompt);
        return await CallGroqChatApiAsync(messages, ct: ct);
    }

    public async IAsyncEnumerable<string> GetResponseStreamAsync(IEnumerable<Message> context, string systemPrompt, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = PrepareMessages(context, systemPrompt);
        await foreach (var chunk in CallGroqChatStreamApiAsync(messages, ct: ct))
        {
            yield return chunk;
        }
    }

    private List<object> PrepareMessages(IEnumerable<Message> context, string systemPrompt)
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

        return messages;
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

    private async IAsyncEnumerable<string> CallGroqChatStreamApiAsync(object messages, int maxTokens = 2048, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_chaos.IsProviderFailing("Groq"))
        {
            _logger.LogWarning("[CHAOS MONKEY] 🐒 Groq stream failure simulated!");
            yield return "Hata: [CHAOS MONKEY] Groq is down!";
            yield break;
        }

        const string RequestUrl = "https://api.groq.com/openai/v1/chat/completions";

        var requestBody = new
        {
            model = _model,
            messages,
            temperature = 0.7,
            
            stream = true
        };

        var jsonBody = JsonSerializer.Serialize(requestBody, _jsonOptions);
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, RequestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = content;

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Groq Stream API Hatası: {Status} - {Error}", response.StatusCode, err);
            yield return "Bir hata oluştu, lütfen tekrar deneyin.";
            yield break;
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
                if (data == "[DONE]") break;

                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices[0].TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var contentProp))
                {
                    yield return contentProp.GetString() ?? "";
                }
            }
        }
    }
}

