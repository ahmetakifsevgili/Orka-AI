using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

/// <summary>
/// OpenAI chat/completions formatını kullanan tüm provider'lar için ortak temel sınıf.
/// SambaNova, Cerebras (ve Groq) bu formatı paylaşır.
/// </summary>
public abstract class OpenAICompatibleService
{
    protected readonly HttpClient HttpClient;
    protected readonly string ApiKey;
    protected readonly string Model;
    protected readonly string BaseUrl;
    protected readonly ILogger Logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    protected OpenAICompatibleService(
        HttpClient httpClient, string apiKey, string model, string baseUrl, ILogger logger)
    {
        HttpClient = httpClient;
        ApiKey     = apiKey;
        Model      = model;
        BaseUrl    = baseUrl;
        Logger     = logger;
    }

    protected async Task<string> CallChatAsync(
        string systemPrompt,
        string userMessage,
        int maxTokens = 2048,
        double temperature = 0.7,
        CancellationToken ct = default)
    {
        var messages = new[]
        {
            new { role = "system", content = string.IsNullOrWhiteSpace(systemPrompt)
                ? "Sen yardımcı bir asistansın." : systemPrompt },
            new { role = "user", content = string.IsNullOrWhiteSpace(userMessage)
                ? "Merhaba." : userMessage }
        };

        return await CallChatWithMessagesAsync(messages, maxTokens, temperature, ct);
    }

    protected async Task<string> CallChatWithMessagesAsync(
        object messages, int maxTokens = 2048, double temperature = 0.7, CancellationToken ct = default)
    {
        var requestBody = new { model = Model, messages,  temperature };
        var jsonBody    = JsonSerializer.Serialize(requestBody, JsonOptions);
        var providerTag = GetType().Name.Replace("Service", "").ToUpperInvariant();

        AiDebugLogger.LogRequest(providerTag, $"URL: {BaseUrl}\nModel: {Model}\n{jsonBody}");

        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        try
        {
            var response = await HttpClient.SendAsync(request, ct);
            var body     = await response.Content.ReadAsStringAsync(ct);
            AiDebugLogger.LogResponse(providerTag, $"Status: {(int)response.StatusCode}\n{body}");

            if (!response.IsSuccessStatusCode)
            {
                Logger.LogError("[{Provider}] API Hatası: {Status} - {Body}", providerTag, response.StatusCode, body);
                throw new HttpRequestException($"{providerTag} API hatası: {response.StatusCode} — {body}");
            }

            using var doc = JsonDocument.Parse(body);
            return doc.RootElement
                       .GetProperty("choices")[0]
                       .GetProperty("message")
                       .GetProperty("content")
                       .GetString() ?? string.Empty;
        }
        catch (HttpRequestException) { throw; }
        catch (Exception ex)
        {
            AiDebugLogger.LogError(providerTag, ex.Message);
            Logger.LogError(ex, "[{Provider}] İstek başarısız.", providerTag);
            throw new HttpRequestException($"{providerTag} isteği başarısız: {ex.Message}", ex);
        }
    }

    protected async IAsyncEnumerable<string> CallChatStreamAsync(
        string systemPrompt,
        string userMessage,
        int maxTokens = 2048,
        double temperature = 0.7,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var messages = new[]
        {
            new { role = "system", content = string.IsNullOrWhiteSpace(systemPrompt) ? "Sen yardımcı bir asistansın." : systemPrompt },
            new { role = "user", content = string.IsNullOrWhiteSpace(userMessage) ? "Merhaba." : userMessage }
        };

        var requestBody = new { model = Model, messages,  temperature, stream = true };
        var jsonBody = JsonSerializer.Serialize(requestBody, JsonOptions);
        var providerTag = GetType().Name.Replace("Service", "").ToUpperInvariant();

        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"{providerTag} Stream error: {response.StatusCode} - {body}");
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
