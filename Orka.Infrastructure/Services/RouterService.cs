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
        [MessageType.Research]   = "Groq/llama-3.3-70b",
        [MessageType.Interview]  = "OpenRouter/claude-3-5-haiku",
        [MessageType.Quiz]       = "OpenRouter/qwen-2.5-72b",
        [MessageType.Summarize]  = "OpenRouter/claude-3-5-haiku",
        [MessageType.Plan]       = "Groq/llama-3.3-70b",
        [MessageType.Explain]    = "Groq/llama-3.3-70b",
        [MessageType.General]    = "Groq/llama-3.3-70b",
        [MessageType.Greeting]   = "Groq/llama-3.3-70b",
        [MessageType.NewTopic]   = "Groq/llama-3.3-70b",
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
