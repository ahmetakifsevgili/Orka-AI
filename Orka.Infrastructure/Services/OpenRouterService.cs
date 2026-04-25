using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public class OpenRouterService : IOpenRouterService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _defaultModel;
    private readonly ILogger<OpenRouterService> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public OpenRouterService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<OpenRouterService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("OpenRouter");
        _apiKey = configuration["AI:OpenRouter:ApiKey"] ?? throw new ArgumentException("OpenRouter API Key eksik.");
        _defaultModel = configuration["AI:OpenRouter:Model"] ?? "anthropic/claude-3-5-haiku";
        _logger = logger;
    }

    // IAIService
    public Task<string> GenerateResponseAsync(string systemPrompt, string userMessage, string? model = null, CancellationToken ct = default)
        => ChatCompletionWithKeyAsync(systemPrompt, userMessage, model: null, apiKey: null, ct: ct);

    public Task<string> ChatCompletionAsync(string systemPrompt, string userMessage, string? model = null, CancellationToken ct = default)
        => ChatCompletionWithKeyAsync(systemPrompt, userMessage, model, apiKey: null, ct: ct);

    /// <summary>
    /// Belirli bir model ve API key ile OpenRouter çağrısı yapar.
    /// apiKey null ise varsayılan (appsettings) key kullanılır.
    /// model null ise varsayılan model kullanılır.
    /// </summary>
    public async Task<string> ChatCompletionWithKeyAsync(string systemPrompt, string userMessage, string? model = null, string? apiKey = null, CancellationToken ct = default)
    {
        var targetModel = model ?? _defaultModel;
        var targetKey = apiKey ?? _apiKey;
        const string RequestUrl = "https://openrouter.ai/api/v1/chat/completions";

        var requestBody = new
        {
            model = targetModel,
            messages = new[]
            {
                new { role = "system", content = string.IsNullOrWhiteSpace(systemPrompt) ? "Sen yardımcı bir asistansın." : systemPrompt },
                new { role = "user", content = string.IsNullOrWhiteSpace(userMessage) ? "Merhaba." : userMessage }
            },
            
            temperature = 0.7
        };

        var jsonBody = JsonSerializer.Serialize(requestBody, _jsonOptions);
        AiDebugLogger.LogRequest("OPENROUTER", $"URL: {RequestUrl}\nModel: {targetModel}\n{jsonBody}");

        using var request = new HttpRequestMessage(HttpMethod.Post, RequestUrl);
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        request.Headers.Add("Authorization", $"Bearer {targetKey}");
        request.Headers.Add("HTTP-Referer", "https://orka.app");
        request.Headers.Add("X-Title", "Orka AI");

        try
        {
            var response = await _httpClient.SendAsync(request, ct);
            var responseString = await response.Content.ReadAsStringAsync(ct);
            AiDebugLogger.LogResponse("OPENROUTER", $"Status: {(int)response.StatusCode}\n{responseString}");

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(responseString);
                return doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString() ?? "AI yanıtı alınamadı.";
            }

            _logger.LogWarning("OpenRouter API Hatası. Status: {Status}, Model: {Model}, Error: {Error}",
                response.StatusCode, targetModel, responseString);

            // Fallback: Haiku üzerinden dene (sadece varsayılan key ile)
            if (targetModel != "anthropic/claude-3-5-haiku")
            {
                _logger.LogInformation("Claude Haiku fallback'e geçiliyor...");
                return await ChatCompletionWithKeyAsync(systemPrompt, userMessage, "anthropic/claude-3-5-haiku", null);
            }

            throw new HttpRequestException($"OpenRouter API hatası: {response.StatusCode} — {responseString}");
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AiDebugLogger.LogError("OPENROUTER", ex.Message);
            _logger.LogError(ex, "OpenRouter çağrısı başarısız. Model: {Model}", targetModel);
            throw new HttpRequestException($"OpenRouter isteği başarısız: {ex.Message}", ex);
        }
    }

    public IAsyncEnumerable<string> GenerateResponseStreamAsync(string systemPrompt, string userMessage, string? model = null, CancellationToken ct = default)
        => GenerateResponseStreamWithKeyAsync(systemPrompt, userMessage, model: null, apiKey: null, ct: ct);

    public async IAsyncEnumerable<string> GenerateResponseStreamWithKeyAsync(string systemPrompt, string userMessage, string? model = null, string? apiKey = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var targetModel = model ?? _defaultModel;
        var targetKey = apiKey ?? _apiKey;
        const string RequestUrl = "https://openrouter.ai/api/v1/chat/completions";
        var messages = new[]
        {
            new { role = "system", content = string.IsNullOrWhiteSpace(systemPrompt) ? "Sen yardımcı bir asistansın." : systemPrompt },
            new { role = "user", content = userMessage }
        };

        var requestBody = new { model = targetModel, messages, temperature = 0.7,  stream = true };
        var jsonBody = JsonSerializer.Serialize(requestBody, _jsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, RequestUrl);
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        request.Headers.Add("Authorization", $"Bearer {targetKey}");
        request.Headers.Add("HTTP-Referer", "https://orka.app");
        request.Headers.Add("X-Title", "Orka AI");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"OpenRouter Stream error: {response.StatusCode} - {err}");
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

