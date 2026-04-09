using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Cerebras AI — llama3.1-8b (ultra-hızlı çıkarım için optimize).
/// OpenAI-uyumlu API; OpenAICompatibleService temel sınıfını kullanır.
/// Model adı noktası noktasına: "llama3.1-8b" (tire yok, nokta var).
/// </summary>
public class CerebrasService : OpenAICompatibleService, ICerebrasService
{
    public CerebrasService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<CerebrasService> logger)
        : base(
            httpClient : httpClientFactory.CreateClient("Cerebras"),
            apiKey     : configuration["AI:Cerebras:ApiKey"]  ?? throw new ArgumentException("Cerebras API Key eksik."),
            model      : configuration["AI:Cerebras:Model"]   ?? "llama3.1-8b",
            baseUrl    : configuration["AI:Cerebras:BaseUrl"] ?? "https://api.cerebras.ai/v1/chat/completions",
            logger     : logger)
    { }

    public Task<string> GenerateResponseAsync(string systemPrompt, string userMessage)
        => CallChatAsync(systemPrompt, userMessage);
}
