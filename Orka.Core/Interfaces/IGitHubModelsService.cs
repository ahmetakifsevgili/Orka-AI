namespace Orka.Core.Interfaces;

/// <summary>
/// GitHub Models (Azure AI Inference endpoint) ile chat tamamlama.
/// Primary provider: https://models.inference.ai.azure.com
/// Kimlik doğrulama: GitHub Personal Access Token (AzureKeyCredential).
/// </summary>
public interface IGitHubModelsService
{
    /// <summary>Tek seferlik chat tamamlama — tam yanıt string olarak döner.</summary>
    Task<string> ChatAsync(
        string systemPrompt,
        string userMessage,
        string model,
        CancellationToken ct = default);

    /// <summary>Streaming chat — token token IAsyncEnumerable olarak akar.</summary>
    IAsyncEnumerable<string> ChatStreamAsync(
        string systemPrompt,
        string userMessage,
        string model,
        CancellationToken ct = default);
}
