using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Enums;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Gelen mesajı analiz ederek intent, konu ve hedef model kararını verir.
///
/// ╔══════════════════════════════════════════════════════════════════╗
/// ║  CIRCULAR DEPENDENCY GUARD — Bu sınıf için zorunlu kurallar:   ║
/// ║                                                                  ║
/// ║  ✅ ENJEKTE EDİLEBİLİR:                                         ║
/// ║     • IGroqService  (routing prompt'u çalıştırmak için)         ║
/// ║     • IAIService    (genel LLM çağrısı)                         ║
/// ║     • IConfiguration / ILogger                                  ║
/// ║                                                                  ║
/// ║  ❌ ASLA ENJEKTE EDİLMEMELİ:                                    ║
/// ║     • IAgentOrchestrator  → döngü: Orchestrator→Router→Orch    ║
/// ║     • ITutorAgent         → döngü: Router→Agent→Groq→Router    ║
/// ║     • ISummarizerAgent / IQuizAgent / IAnalyzerAgent            ║
/// ║     • ITopicService (Agent zinciri değil, sadece CRUD servisi)  ║
/// ║                                                                  ║
/// ║  Bağımlılık yönü tek taraflı olmalıdır:                        ║
/// ║  Controller → Orchestrator → RouterService → IGroqService       ║
/// ╚══════════════════════════════════════════════════════════════════╝
/// </summary>
public class RouterService : IRouterService
{
    private readonly IGroqService _groqService;
    private readonly ILogger<RouterService> _logger;

    // Intent → MessageType eşlemesi
    private static readonly Dictionary<string, MessageType> IntentMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["greeting"]  = MessageType.Greeting,
        ["new_topic"] = MessageType.NewTopic,
        ["interview"] = MessageType.Interview,
        ["plan"]      = MessageType.Plan,
        ["explain"]   = MessageType.Explain,
        ["research"]  = MessageType.Research,
        ["quiz"]      = MessageType.Quiz,
        ["summary"]   = MessageType.Summarize,
        ["general"]   = MessageType.General,
    };

    // MessageType → model adı eşlemesi
    private static readonly Dictionary<MessageType, string> ModelMap = new()
    {
        [MessageType.Research]   = "OpenRouter/qwen3-next-80b-a3b",   // Wiki / Derin Araştırma
        [MessageType.Interview]  = "OpenRouter/nemotron-nano-9b-v2",  // Analiz / Hızlı yanıt
        [MessageType.Quiz]       = "OpenRouter/nemotron-nano-9b-v2",  // Quiz değerlendirmesi
        [MessageType.Summarize]  = "OpenRouter/gpt-oss-20b",          // Özetleme
        [MessageType.Plan]       = "OpenRouter/gpt-oss-120b",         // DeepPlan
        [MessageType.Explain]    = "OpenRouter/llama-3.3-70b",        // Tutor
        [MessageType.General]    = "OpenRouter/llama-3.3-70b",        // Tutor
        [MessageType.Greeting]   = "OpenRouter/llama-3.3-70b",        // Tutor
        [MessageType.NewTopic]   = "OpenRouter/gpt-oss-120b",         // Yeni konu DeepPlan
    };

    public RouterService(IGroqService groqService, ILogger<RouterService> logger)
    {
        _groqService = groqService;
        _logger = logger;
    }

    public async Task<RoutingResult> RouteMessageAsync(string content, string? currentPhase = "Discovery")
    {
        try
        {
            return await _groqService.SemanticRouteAsync(content, currentPhase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RouterService: SemanticRoute başarısız, fallback'e geçiliyor.");
            return new RoutingResult
            {
                Intent = content.Trim().Length < 10 ? "greeting" : "general",
                Method  = "fallback"
            };
        }
    }

    public MessageType IntentToMessageType(string intent)
        => IntentMap.TryGetValue(intent, out var type) ? type : MessageType.General;

    public string GetModelName(MessageType classification)
        => ModelMap.TryGetValue(classification, out var model) ? model : "Groq/llama-3.3-70b";
}
