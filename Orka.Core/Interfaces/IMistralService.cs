using System.Collections.Generic;
using System.Threading.Tasks;
using Orka.Core.Entities;

namespace Orka.Core.Interfaces;

public interface IMistralService : IAIService
{
    Task<string> GetResponseAsync(IEnumerable<Message> context, string systemPrompt, CancellationToken ct = default);
    Task<string> SummarizeSessionAsync(IEnumerable<Message> messages);
    Task<string> ExtractWikiBlocksAsync(string conversation, string topicTitle);
    Task<string> GenerateReinforcementQuestionsAsync(string content);
}
