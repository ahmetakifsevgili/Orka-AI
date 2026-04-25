using System.Threading;
using System.Threading.Tasks;

namespace Orka.Core.Interfaces;

public interface IEvaluatorAgent
{
    /// <summary>
    /// Değerlendirici ajan, kullanıcının sorusuna karşın Ajanın ürettiği cevabı inceler.
    /// Formatlanmış bir JSON olarak 1-10 arası puan ve feedback döner.
    /// topicId sağlanırsa ve puan >= 9 ise Redis'e altın örnek olarak kaydedilir (Faz 12).
    /// </summary>
    Task<(int score, string feedback)> EvaluateInteractionAsync(
        Guid sessionId,
        string userMessage,
        string agentResponse,
        string agentRole,
        Guid? topicId = null,
        string? goalContext = null,
        CancellationToken ct = default);
}
