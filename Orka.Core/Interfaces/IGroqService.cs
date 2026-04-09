using System.Collections.Generic;
using System.Threading.Tasks;
using Orka.Core.DTOs;
using Orka.Core.Entities;

namespace Orka.Core.Interfaces;

public interface IGroqService : IAIService
{
    Task<string> GetResponseAsync(IEnumerable<Message> context, string systemPrompt, CancellationToken ct = default);
    Task<string> SummarizeSessionAsync(IEnumerable<Message> messages);
    Task<RoutingResult> SemanticRouteAsync(string message, string? currentPhase = "Discovery");
    Task<string> ResearchAsync(string topic, string depth = "normal");
    Task<string> GeneratePlanAsync(string topicTitle, string intent = "genel öğrenme", string level = "orta");
}
