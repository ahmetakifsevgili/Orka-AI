using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// SambaNova Cloud — Meta-Llama-3.3-70B-Instruct.
/// OpenAI-uyumlu API; OpenAICompatibleService temel sınıfını kullanır.
/// </summary>
public class SambaNovaService : OpenAICompatibleService, ISambaNovaService
{
    public SambaNovaService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<SambaNovaService> logger)
        : base(
            httpClient : httpClientFactory.CreateClient("SambaNova"),
            apiKey     : configuration["AI:SambaNova:ApiKey"]  ?? throw new ArgumentException("SambaNova API Key eksik."),
            model      : configuration["AI:SambaNova:Model"]   ?? "Meta-Llama-3.3-70B-Instruct",
            baseUrl    : configuration["AI:SambaNova:BaseUrl"] ?? "https://api.sambanova.ai/v1/chat/completions",
            logger     : logger)
    { }

    public Task<string> GenerateResponseAsync(string systemPrompt, string userMessage, string? model = null, CancellationToken ct = default)
        => CallChatAsync(systemPrompt, userMessage, ct: ct);

    public IAsyncEnumerable<string> GenerateResponseStreamAsync(string systemPrompt, string userMessage, string? model = null, CancellationToken ct = default)
        => CallChatStreamAsync(systemPrompt, userMessage, ct: ct);
}

