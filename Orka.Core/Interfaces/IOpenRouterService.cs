using System.Threading.Tasks;

namespace Orka.Core.Interfaces;

public interface IOpenRouterService : IAIService
{
    /// <summary>
    /// OpenRouter üzerinden chat completion çağrısı yapar.
    /// interview ve quiz modları için kullanılır.
    /// </summary>
    Task<string> ChatCompletionAsync(string systemPrompt, string userMessage, string? model = null, CancellationToken ct = default);
}
