using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

/// <summary>
/// HuggingFace Router — Llama-3.1-8B-Instruct üzerinden OpenAI-uyumlu API.
/// Zincirdeki 5. halka: Groq → SambaNova → Cerebras → Cohere → HuggingFace → Mistral
/// </summary>
public class HuggingFaceService : IHuggingFaceService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _baseUrl;
    private readonly ILogger<HuggingFaceService> _logger;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public HuggingFaceService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<HuggingFaceService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("HuggingFace");
        _apiKey     = configuration["AI:HuggingFace:ApiKey"]  ?? throw new ArgumentException("HuggingFace API Key eksik.");
        _model      = configuration["AI:HuggingFace:Model"]   ?? "meta-llama/Llama-3.1-8B-Instruct";
        _baseUrl    = configuration["AI:HuggingFace:BaseUrl"] ?? "https://router.huggingface.co/v1";
        _logger     = logger;
    }

    public Task<string> GenerateResponseAsync(string systemPrompt, string userMessage)
        => CallChatApiAsync(systemPrompt, userMessage);

    private async Task<string> CallChatApiAsync(string systemPrompt, string userMessage)
    {
        var endpoint = $"{_baseUrl.TrimEnd('/')}/chat/completions";

        var messages = new object[]
        {
            new { role = "system", content = string.IsNullOrWhiteSpace(systemPrompt) ? "Sen yardımcı bir asistansın." : systemPrompt },
            new { role = "user",   content = userMessage }
        };

        var body = new
        {
            model       = _model,
            messages,
            temperature = 0.5,
            max_tokens  = 2048
        };

        var json = JsonSerializer.Serialize(body, _jsonOpts);
        AiDebugLogger.LogRequest("HUGGINGFACE", $"URL: {endpoint}\nModel: {_model}\n{json}");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.SendAsync(request);
            var respStr  = await response.Content.ReadAsStringAsync();
            AiDebugLogger.LogResponse("HUGGINGFACE", $"Status: {(int)response.StatusCode}\n{respStr}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("HuggingFace API Hatası: {Status} — {Error}", response.StatusCode, respStr);
                throw new HttpRequestException($"HuggingFace API hatası: {response.StatusCode} — {respStr}");
            }

            using var doc = JsonDocument.Parse(respStr);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        }
        catch (HttpRequestException) { throw; }
        catch (Exception ex)
        {
            AiDebugLogger.LogError("HUGGINGFACE", ex.Message);
            _logger.LogError(ex, "HuggingFace API çağrısı başarısız.");
            throw new HttpRequestException($"HuggingFace servis hatası: {ex.Message}", ex);
        }
    }
}
