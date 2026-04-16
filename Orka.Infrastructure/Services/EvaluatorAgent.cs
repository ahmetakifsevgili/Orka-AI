using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orka.Core.Enums;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public class EvaluatorAgent : IEvaluatorAgent
{
    private readonly IAIAgentFactory _factory;
    private readonly ILogger<EvaluatorAgent> _logger;

    private readonly IRedisMemoryService _redisService;

    public EvaluatorAgent(IAIAgentFactory factory, ILogger<EvaluatorAgent> logger, IRedisMemoryService redisService)
    {
        _factory = factory;
        _logger = logger;
        _redisService = redisService;
    }

    public async Task<(int score, string feedback)> EvaluateInteractionAsync(
        Guid sessionId,
        string userMessage,
        string agentResponse,
        string agentRole,
        Guid? topicId = null,
        CancellationToken ct = default)
    {
        var prompt = $$"""
            Sen Orka AI için Kalite Kontrol (LLMOps Değerlendiricisi) ajanısın.
            Görev: Sistemin bir kullanıcının mesajına (Role: {{agentRole}}) verdiği cevabı kalite, akıcılık ve yardımseverlik açısından 1 ile 10 arasında puanla.
            Not: Modelin Orka karakterine sadık kalması, saygılı olması ve aşırı uzun gevezelik etmemesi puanını artırır. Eğer verilen cevap ile kullanıcı sorusu tamamen uyumsuzsa düşük puan ver.

            Öğrencinin Sorusu/Mesajı:
            "{{userMessage}}"

            AI'ın Verdiği Cevap:
            "{{agentResponse}}"

            LÜTFEN SADECE AŞAĞIDAKİ JSON FORMATINDA CEVAP DÖNDÜR, BAŞKA METİN EKLEME:
            {
               "score": 8,
               "feedback": "Kısa ve net bir cevap olmuş, öğrenciye saygılı."
            }
            """;

        try
        {
            // Evaluator kendi model rotasını kullanır (AgentRole.Grader'dan ayrıldı — Faz 10)
            var response = await _factory.CompleteChatAsync(AgentRole.Evaluator, prompt, "Değerlendir.", ct);

            response = response.Replace("```json", "").Replace("```", "").Trim();

            var result = JsonSerializer.Deserialize<EvaluationResult>(response)
                         ?? new EvaluationResult { score = 5, feedback = "Parse edilemedi" };

            // Hatalar Defteri'ne yaz (tüm ajanlar için)
            if (sessionId != Guid.Empty)
                await _redisService.RecordEvaluationAsync(sessionId, result.score, result.feedback);

            // Faz 12: 9-10 puan → Altın Örnek olarak kaydet (sadece TutorAgent diyalogları için)
            if (result.score >= 9 && topicId.HasValue && agentRole == "TutorAgent")
            {
                await _redisService.SaveGoldExampleAsync(topicId.Value, userMessage, agentResponse, result.score);
                _logger.LogInformation(
                    "[EvaluatorAgent] Altın örnek kaydedildi. TopicId={TopicId} Puan={Score}",
                    topicId.Value, result.score);
            }

            _logger.LogInformation(
                "[EvaluatorAgent] Değerlendirme tamamlandı. Rol={Role} Puan={Score}/10",
                agentRole, result.score);

            return (result.score, result.feedback);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EvaluatorAgent] Json parse veya ağ hatası: Puanlama başarısız oldu.");
            return (5, "Hata oluştu.");
        }
    }

    private class EvaluationResult
    {
        public int score { get; set; }
        public string feedback { get; set; } = string.Empty;
    }
}
