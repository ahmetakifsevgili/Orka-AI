using Orka.Core.Entities;
using Orka.Core.DTOs.Korteks;

namespace Orka.Core.Interfaces;

/// <summary>
/// Yeni bir konu başladığında 4 mantıksal alt başlık üretir ve Topics tablosuna kaydeder.
///
/// CIRCULAR DEPENDENCY GUARD:
///   ✅ Inject edilebilir: IGroqService, ICerebrasService, IAIService, IServiceScopeFactory
///   ❌ Inject edilemez : IAgentOrchestrator, ITutorAgent, diğer Agent'lar
/// </summary>
public interface IDeepPlanAgent
{
    /// <summary>
    /// parentTopicId konusunu 4 alt başlığa böler.
    /// Alt başlıkları Topic kaydı olarak DB'ye yazar.
    /// </summary>
    Task<List<Topic>> GenerateAndSaveDeepPlanAsync(
        Guid parentTopicId,
        string topicTitle,
        Guid userId,
        string userLevel = "Bilinmiyor",
        string? researchContext = null,
        string? failedTopics = null);

    Task<DeepPlanGenerationWithGroundingResultDto> GenerateAndSaveDeepPlanWithGroundingAsync(
        Guid parentTopicId,
        string topicTitle,
        Guid userId,
        string userLevel = "Bilinmiyor",
        string? researchContext = null,
        string? failedTopics = null);

    Task<DeepPlanGenerationWithGroundingResultDto> GenerateAndSaveDeepPlanFromDiagnosticAsync(
        Guid parentTopicId,
        string topicTitle,
        Guid userId,
        string compressedResearchPromptBlock,
        string diagnosticQuizSummary,
        string userLevel = "Bilinmiyor");

    Task<string> GenerateBaselineQuizAsync(string topicTitle);
}
