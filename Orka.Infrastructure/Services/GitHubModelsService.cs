using System.Runtime.CompilerServices;
using Azure;
using Azure.AI.Inference;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// GitHub Models — Azure AI Inference SDK üzerinden chat tamamlama.
///
/// Endpoint : https://models.inference.ai.azure.com
/// Auth      : GitHub Personal Access Token (AzureKeyCredential)
/// SDK       : Azure.AI.Inference 1.0.0-beta.5
/// </summary>
public class GitHubModelsService : IGitHubModelsService
{
    private readonly ChatCompletionsClient _client;
    private readonly ILogger<GitHubModelsService> _logger;

    public GitHubModelsService(
        IConfiguration configuration,
        ILogger<GitHubModelsService> logger)
    {
        _logger = logger;

        var token   = configuration["AI:GitHubModels:Token"]   ?? throw new ArgumentException("AI:GitHubModels:Token eksik.");
        var baseUrl = configuration["AI:GitHubModels:BaseUrl"] ?? "https://models.inference.ai.azure.com";

        _client = new ChatCompletionsClient(
            new Uri(baseUrl),
            new AzureKeyCredential(token));
    }

    /// <inheritdoc/>
    public async Task<string> ChatAsync(
        string systemPrompt,
        string userMessage,
        string model,
        CancellationToken ct = default)
    {
        var options = BuildOptions(systemPrompt, userMessage, model);

        _logger.LogDebug("[GitHubModels] ChatAsync model={Model}", model);

        var response = await _client.CompleteAsync(options, ct);

        // beta.5: Content is a convenience property on ChatCompletions
        return response.Value.Content ?? string.Empty;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> ChatStreamAsync(
        string systemPrompt,
        string userMessage,
        string model,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var options = BuildOptions(systemPrompt, userMessage, model);

        _logger.LogDebug("[GitHubModels] ChatStreamAsync model={Model}", model);

        var stream = await _client.CompleteStreamingAsync(options, ct);

        await foreach (var update in stream.WithCancellation(ct))
        {
            // beta.5: ContentUpdate is the token text for this chunk
            var text = update.ContentUpdate;
            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ChatCompletionsOptions BuildOptions(
        string systemPrompt,
        string userMessage,
        string model)
    {
        var opts = new ChatCompletionsOptions
        {
            Model       = model,
            Temperature = 0.7f,
            MaxTokens   = 4096
        };
        opts.Messages.Add(new ChatRequestSystemMessage(systemPrompt));
        opts.Messages.Add(new ChatRequestUserMessage(userMessage));
        return opts;
    }
}
