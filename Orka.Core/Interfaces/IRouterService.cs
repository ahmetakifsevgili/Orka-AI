using System.Threading.Tasks;
using Orka.Core.DTOs;
using Orka.Core.Enums;

namespace Orka.Core.Interfaces;

public interface IRouterService
{
    /// <summary>
    /// Semantic Router: Mesajı LLM ile analiz eder, intent + konu + plan gereksinimi döner.
    /// </summary>
    Task<RoutingResult> RouteMessageAsync(string content, string? currentPhase = "Discovery");

    /// <summary>
    /// RoutingResult'taki intent string'ini MessageType enum'a çevirir.
    /// </summary>
    MessageType IntentToMessageType(string intent);

    /// <summary>
    /// MessageType'a göre model adını döner.
    /// </summary>
    string GetModelName(MessageType classification);
}
