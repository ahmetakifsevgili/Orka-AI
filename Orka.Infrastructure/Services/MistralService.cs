using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public class MistralService : IMistralService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<MistralService> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public MistralService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<MistralService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Mistral");
        _apiKey = configuration["AI:Mistral:ApiKey"] ?? throw new ArgumentException("Mistral API Key eksik.");
        _model = configuration["AI:Mistral:Model"] ?? "mistral-small-latest";
        _logger = logger;
    }

    // IAIService
    public Task<string> GenerateResponseAsync(string systemPrompt, string userMessage, string? model = null, CancellationToken ct = default)
        => CallMistralApiAsync(userMessage, systemPrompt, ct);

    public async Task<string> GeneratePlanAsync(string topicTitle, string intent = "genel öğrenme", string level = "orta")
    {
        var prompt = $@"Kullanıcı ""{topicTitle}"" konusunu öğrenmek istiyor.
Bu konu için {level} seviyesinde, {intent} amacı doğrultusunda 5-8 alt konudan oluşan bir öğrenme planı çıkar.
SADECE şu JSON formatında yanıt ver, başka hiçbir şey yazma:
[""Alt konu 1"",""Alt konu 2"",""Alt konu 3"",""Alt konu 4"",""Alt konu 5""]";

        return await CallMistralApiAsync(prompt, "Sen bir eğitim planlayıcısısın.");
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

        return await CallMistralChatApiAsync(messages, ct);
    }

    public async Task<string> SummarizeSessionAsync(IEnumerable<Message> messages)
    {
        var text = string.Join("\n", messages.Select(c => $"{c.Role}: {c.Content}"));
        return await CallMistralApiAsync(
            $"Aşağıdaki öğrenme oturumunu 2-3 cümleyle özetle. Ne öğrenildi ve nerede bırakıldı?\n\n{text}",
            "Sen bir özetleme asistanısın.");
    }

    public async Task<string> ExtractWikiBlocksAsync(string conversationHistory, string topicTitle)
    {
        var prompt = $@"Aşağıdaki sohbet '{topicTitle}' konusu üzerine yapıldı.
Sohbetten öğrenilen en önemli kavramları çıkar. JSON array formatında dön, başka açıklama yazma.
Örnek:
[
  {{ ""blockType"": ""concept"", ""title"": ""Değişken Nedir?"", ""content"": ""..."" }},
  {{ ""blockType"": ""note"", ""title"": ""Önemli İpucu"", ""content"": ""..."" }}
]

Sohbet:
{conversationHistory}";

        return await CallMistralApiAsync(prompt, "Sen bir wiki kuratörüsün. SADECE JSON formatında cevap verirsin.");
    }

    public async Task<string> GenerateReinforcementQuestionsAsync(string content)
    {
        var prompt = $@"Aşağıdaki ders içeriğine dayanarak 3-5 adet pekiştirme sorusu hazırla.
Sorular kısa ve net cevaplı olmalı. Sadece soruları maddeler halinde dön.

Ders İçeriği:
{content}";

        return await CallMistralApiAsync(prompt, "Sen bir eğitim küratörüsün. Sadece pekiştirme soruları hazırlarsın.");
    }

    private async Task<string> CallMistralApiAsync(string prompt, string systemRole, CancellationToken ct = default)
    {
        var messages = new List<object>
        {
            new { role = "system", content = string.IsNullOrWhiteSpace(systemRole) ? "Sen yardımcı bir asistansın." : systemRole },
            new { role = "user", content = prompt }
        };
        return await CallMistralChatApiAsync(messages, ct);
    }

    private async Task<string> CallMistralChatApiAsync(object messages, CancellationToken ct = default)
    {
        const string RequestUrl = "https://api.mistral.ai/v1/chat/completions";

        var requestBody = new
        {
            model = _model,
            messages,
            temperature = 0.5,
            
        };

        var jsonBody = JsonSerializer.Serialize(requestBody, _jsonOptions);
        AiDebugLogger.LogRequest("MISTRAL", $"URL: {RequestUrl}\nModel: {_model}\n{jsonBody}");

        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, RequestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = content;

        try
        {
            var response = await _httpClient.SendAsync(request, ct);
            var responseString = await response.Content.ReadAsStringAsync(ct);
            AiDebugLogger.LogResponse("MISTRAL", $"Status: {(int)response.StatusCode}\n{responseString}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Mistral API Hatası: {Status} - {Error}", response.StatusCode, responseString);
                throw new HttpRequestException($"Mistral API hatası: {response.StatusCode} — {responseString}");
            }

            using var jsonDoc = JsonDocument.Parse(responseString);
            return jsonDoc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AiDebugLogger.LogError("MISTRAL", ex.Message);
            _logger.LogError(ex, "Mistral API çağrısı başarısız.");
            return "AI yanıtı işlenirken hata oluştu.";
        }
    }

    public async IAsyncEnumerable<string> GenerateResponseStreamAsync(string systemPrompt, string userMessage, [System.Runtime.CompilerServices.EnumeratorCancellation] string? model = null, CancellationToken ct = default)
    {
        const string RequestUrl = "https://api.mistral.ai/v1/chat/completions";
        var messages = new[]
        {
            new { role = "system", content = string.IsNullOrWhiteSpace(systemPrompt) ? "Sen yardımcı bir asistansın." : systemPrompt },
            new { role = "user", content = userMessage }
        };

        var requestBody = new { model = _model, messages, temperature = 0.5,  stream = true };
        var jsonBody = JsonSerializer.Serialize(requestBody, _jsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, RequestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Mistral Stream error: {response.StatusCode} - {err}");
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
                var content = doc.RootElement
                                 .GetProperty("choices")[0]
                                 .GetProperty("delta")
                                 .TryGetProperty("content", out var textProp) ? textProp.GetString() : null;

                if (!string.IsNullOrEmpty(content))
                    yield return content;
            }
        }
    }
}

