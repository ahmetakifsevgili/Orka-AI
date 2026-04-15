using System.Threading.Tasks;

namespace Orka.Core.Interfaces;

public interface IOpenRouterService : IAIService
{
    /// <summary>
    /// Varsayılan key ve model ile OpenRouter çağrısı yapar.
    /// </summary>
    Task<string> ChatCompletionAsync(string systemPrompt, string userMessage, string? model = null, CancellationToken ct = default);

    /// <summary>
    /// Belirli bir model ve API key ile OpenRouter çağrısı yapar.
    /// Default model ve key yerine bu parametreler kullanılır.
    /// </summary>
    Task<string> ChatCompletionWithKeyAsync(string systemPrompt, string userMessage, string? model = null, string? apiKey = null, CancellationToken ct = default);

    /// <summary>
    /// Varsayılan model ve key ile stream yanıtı döndürür.
    /// </summary>
    IAsyncEnumerable<string> GenerateResponseStreamAsync(string systemPrompt, string userMessage, CancellationToken ct = default);

    /// <summary>
    /// OpenRouter üzerinden belirli bir model ve API key ile stream yanıtı döndürür.
    /// </summary>
    IAsyncEnumerable<string> GenerateResponseStreamWithKeyAsync(string systemPrompt, string userMessage, string? model = null, string? apiKey = null, CancellationToken ct = default);
}
