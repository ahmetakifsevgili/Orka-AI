using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.Exceptions;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public sealed class CohereService : ICohereService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _endpoint;
    private readonly ILogger<CohereService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CohereService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<CohereService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Cohere");
        _apiKey = configuration["AI:Cohere:ApiKey"] ?? throw new ArgumentException("Cohere API Key eksik.");
        _model = configuration["AI:Cohere:Model"] ?? "command-a-03-2025";
        _endpoint = NormalizeChatEndpoint(configuration["AI:Cohere:BaseUrl"]);
        _logger = logger;
    }

    public Task<string> GenerateResponseAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default,
        int? maxOutputTokens = null) =>
        CallChatAsync(systemPrompt, userMessage, maxOutputTokens ?? 2048, ct);

    public async IAsyncEnumerable<string> GenerateResponseStreamAsync(
        string systemPrompt,
        string userMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default,
        int? maxOutputTokens = null)
    {
        var requestBody = BuildRequestBody(systemPrompt, userMessage, maxOutputTokens ?? 2048, stream: true);
        var jsonBody = JsonSerializer.Serialize(requestBody, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw AiProviderFailureMapper.FromResponse("Cohere", _model, response, body);
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var data = line[6..].Trim();
            if (data == "[DONE]")
                break;

            using var doc = JsonDocument.Parse(data);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("delta")
                .TryGetProperty("content", out var textProp)
                    ? textProp.GetString()
                    : null;

            if (!string.IsNullOrEmpty(content))
                yield return content;
        }
    }

    private async Task<string> CallChatAsync(
        string systemPrompt,
        string userMessage,
        int maxOutputTokens,
        CancellationToken ct)
    {
        var requestBody = BuildRequestBody(systemPrompt, userMessage, maxOutputTokens, stream: false);
        var jsonBody = JsonSerializer.Serialize(requestBody, JsonOptions);
        AiDebugLogger.LogRequest("COHERE", $"URL: {_endpoint}\nModel: {_model}\n{jsonBody}");

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Headers.Add("Authorization", $"Bearer {_apiKey}");
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            AiDebugLogger.LogResponse("COHERE", $"Status: {(int)response.StatusCode}\n{body}");

            if (!response.IsSuccessStatusCode)
            {
                body = AiDebugLogger.BuildSafeLogPreview("COHERE", "ERROR", body);
                _logger.LogError("Cohere API hatasi. Status={Status} Body={Body}", response.StatusCode, body);
                throw AiProviderFailureMapper.FromResponse("Cohere", _model, response, body);
            }

            using var doc = JsonDocument.Parse(body);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (AiProviderCallException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AiDebugLogger.LogError("COHERE", ex.Message);
            _logger.LogError("Cohere request failed. ExceptionType={ExceptionType}", ex.GetType().Name);
            throw AiProviderFailureMapper.FromException("Cohere", _model, ex);
        }
    }

    private object BuildRequestBody(string systemPrompt, string userMessage, int maxOutputTokens, bool stream)
    {
        return new
        {
            model = _model,
            messages = new[]
            {
                new { role = "developer", content = string.IsNullOrWhiteSpace(systemPrompt) ? "Sen yardimci bir asistansin." : systemPrompt },
                new { role = "user", content = string.IsNullOrWhiteSpace(userMessage) ? "Merhaba." : userMessage }
            },
            temperature = 0.7,
            max_tokens = maxOutputTokens,
            stream
        };
    }

    private static string NormalizeChatEndpoint(string? baseUrl)
    {
        var value = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://api.cohere.ai/compatibility/v1"
            : baseUrl.TrimEnd('/');

        return value.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? value
            : $"{value}/chat/completions";
    }
}
