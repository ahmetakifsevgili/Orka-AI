using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orka.Core.Enums;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Orka LLMOps Değerlendiricisi — multi-dimensional scoring.
///
/// Klasik tek puan (1-10) yerine 3 boyut + overall modeli:
///   Pedagoji  (1-5): Açıklama ne kadar öğretici/seviyeye uygun?
///   Faktual   (1-5): Bilgi doğru mu? (Düşükse hallucination riski)
///   Bağlam    (1-5): Öğrenci sorusuna gerçekten cevap veriyor mu?
/// Overall = normalize((pedagoji + faktual + bağlam) * 2/3 → 1-10).
///
/// Modern LLMOps pattern: RAG-triad (faithfulness/answer-relevance/context-precision)
/// esintili. Hallucination flag ise Faktual &lt; 3 ile tetiklenir.
///
/// Geriye uyumluluk: EvaluateInteractionAsync() tuple (score, feedback) imzası korunur;
/// sub-skorlar feedback string'inin başına "[F:x P:y C:z]" olarak gömülür — böylece Dashboard
/// migration yapmadan görebilir, ileride nullable kolonlar eklenerek SQL tablo zenginleştirilebilir.
/// </summary>
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

    // Podcast tag ve görsel URL'lerini temizle — evaluator içerik skor versin, format değil
    private static readonly System.Text.RegularExpressions.Regex PodcastTagRegex =
        new(@"\[(HOCA|ASİSTAN|ASISTAN)\]:?\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex ImageUrlRegex =
        new(@"!\[[^\]]*\]\([^)]+\)", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex MermaidBlockRegex =
        new(@"```mermaid[\s\S]*?```", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string SanitizeForEvaluation(string text)
    {
        text = PodcastTagRegex.Replace(text, "");
        text = ImageUrlRegex.Replace(text, "[görsel]");
        text = MermaidBlockRegex.Replace(text, "[diyagram]");
        return text.Trim();
    }

    public async Task<(int score, string feedback)> EvaluateInteractionAsync(
        Guid sessionId,
        string userMessage,
        string agentResponse,
        string agentRole,
        Guid? topicId = null,
        string? goalContext = null,
        CancellationToken ct = default)
    {
        // Podcast/görsel etiketlerini temizle — evaluator içerik kalitesini ölçsün
        var cleanedResponse = SanitizeForEvaluation(agentResponse);

        // Faz 16: IDE kodlama cevabı mı yoksa normal ders anlatımı mı?
        bool isCodeInteraction = userMessage.Contains("```") || 
                                  userMessage.Contains("[KOD ÇALIŞTIRMA SONUCU") ||
                                  userMessage.Contains("**Quiz Sorusu:**") && userMessage.Contains("```");

        var prompt = isCodeInteraction 
        ? $$"""
            Sen Orka AI için LLMOps Kod Kalite Kontrol ajanısın. Bir ajanın ({{agentRole}}) kodlama cevabını değerlendir.
            
            KODLAMA DEĞERLENDİRME BOYUTLARI:
              1) pedagogy      (1-5): Açıklama öğretici mi? Kodun neden doğru/yanlış olduğu anlatılmış mı?
              2) factual       (1-5): Kod mantığı doğru mu? Algoritmik olarak istenen çıktıyı veriyor mu?
              3) context       (1-5): Sorunun istediği tam olarak cevaplandı mı? Edge case'ler düşünülmüş mü?
              4) goal_alignment (1-5): Öğrencinin '{{(string.IsNullOrWhiteSpace(goalContext) ? "Genel Başarı" : goalContext)}}' hedefine uygun mu?
              5) code_quality  (1-5): Kod okunabilir mi? Değişken isimleri anlamlı mı? DRY prensibi uygulanmış mı?

            KOD SONUCU VARSA: [KOD ÇALIŞTIRMA SONUCU] bloğunu oku. Eğer kod başarıyla çalıştıysa ve doğru çıktı verdiyse factual puanını yükselt. 
            Eğer kod hata verdiyse factual puanını düşür ve hatanın ne olduğunu feedback'te açıkla.
            
            Overall = normalize((pedagogy + factual + context + goal_alignment + code_quality) / 25 * 10).
            Eğer factual < 3 ise hallucinationRisk=true.

            Öğrencinin Mesajı:
            "{{userMessage}}"

            AI Cevabı (temizlenmiş):
            "{{cleanedResponse}}"

            YALNIZCA AŞAĞIDAKİ JSON ŞEMASINDA CEVAP VER, BAŞKA METİN EKLEME:
            {
              "pedagogy": 4,
              "factual": 5,
              "context": 4,
              "goal_alignment": 4,
              "code_quality": 4,
              "overall": 8,
              "hallucinationRisk": false,
              "feedback": "Kod doğru çalışıyor, bubble sort mantığı doğru ama değişken isimleri daha açıklayıcı olabilirdi."
            }
            """
        : $$"""
            Sen Orka AI için LLMOps Kalite Kontrol ajanısın. Bir ajanın ({{agentRole}}) cevabını
            üç boyutta değerlendir:

              1) pedagogy  (1-5): Açıklama öğretici mi, seviyeye uygun mu, gereksiz gevezelik var mı?
              2) factual   (1-5): İçerik doğru mu, uydurma bilgi/hallucination var mı?
              3) context   (1-5): Kullanıcının sorusuyla gerçekten alakalı mı, konuyu kaçırmış mı?
              4) goal_alignment (1-5): Öğrencinin '{{(string.IsNullOrWhiteSpace(goalContext) ? "Genel Başarı" : goalContext)}}' hedefine uygun bir anlatım veya zorluk seviyesi kullanılmış mı?

            Her boyut için kısa gerekçeyi zihninde tut, sonra 1-10 arası "overall" (genel) puan
            üret. Eğer factual < 3 ise hallucinationRisk=true işaretle. Aynı şekilde goal_alignment çok düşükse overall puanı kır.

            Öğrencinin Mesajı:
            "{{userMessage}}"

            AI Cevabı (temizlenmiş):
            "{{cleanedResponse}}"

            YALNIZCA AŞAĞIDAKİ JSON ŞEMASINDA CEVAP VER, BAŞKA METİN EKLEME:
            {
              "pedagogy": 4,
              "factual": 5,
              "context": 4,
              "goal_alignment": 4,
              "overall": 8,
              "hallucinationRisk": false,
              "feedback": "Kısa, net ve doğru bir açıklama."
            }
            """;

        try
        {
            var response = await _factory.CompleteChatAsync(AgentRole.Evaluator, prompt, "Değerlendir.", ct);
            response = response.Replace("```json", "").Replace("```", "").Trim();

            var detail = ParseDetail(response);
            var combinedFeedback = BuildCombinedFeedback(detail);

            // Hatalar Defteri'ne yaz (tüm ajanlar için)
            if (sessionId != Guid.Empty)
                await _redisService.RecordEvaluationAsync(sessionId, detail.Overall, combinedFeedback);

            // Faz 14: Topic-level kümülatif puanlama (session değişse de korunur)
            if (topicId.HasValue)
                await _redisService.RecordTopicScoreAsync(topicId.Value, detail.Overall, combinedFeedback);

            // Faz 12: 9-10 puan → Altın Örnek olarak kaydet (sadece TutorAgent diyalogları için)
            // Not: Hallucination riski varsa altın örneğe kaydetmeyiz (yanlış bilgi pekiştirmeyelim).
            if (detail.Overall >= 9 && !detail.HallucinationRisk && topicId.HasValue && agentRole == "TutorAgent")
            {
                await _redisService.SaveGoldExampleAsync(topicId.Value, userMessage, agentResponse, detail.Overall);
                _logger.LogInformation(
                    "[EvaluatorAgent] Altın örnek kaydedildi. TopicId={TopicId} Puan={Score}",
                    topicId.Value, detail.Overall);
            }

            if (detail.HallucinationRisk)
            {
                _logger.LogWarning(
                    "[EvaluatorAgent] Hallucination riski tespit edildi. Role={Role} Factual={Factual}/5 Overall={Overall}",
                    agentRole, detail.Factual, detail.Overall);
            }

            _logger.LogInformation(
                "[EvaluatorAgent] Değerlendirme tamamlandı. Rol={Role} Puan={Score}/10 F={F} P={P} C={C}",
                agentRole, detail.Overall, detail.Factual, detail.Pedagogy, detail.Context);

            return (detail.Overall, combinedFeedback);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EvaluatorAgent] Json parse veya ağ hatası: Puanlama başarısız oldu.");
            return (5, "Hata oluştu.");
        }
    }

    // ── Parsing & composition ────────────────────────────────────────────────

    private static EvaluationDetail ParseDetail(string response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            int Read(string prop, int min, int max, int fallback)
            {
                if (!root.TryGetProperty(prop, out var el) || el.ValueKind != JsonValueKind.Number) return fallback;
                var v = el.GetInt32();
                return Math.Clamp(v, min, max);
            }

            var pedagogy = Read("pedagogy", 1, 5, 3);
            var factual  = Read("factual",  1, 5, 3);
            var ctx      = Read("context",  1, 5, 3);
            var goalAl   = Read("goal_alignment", 1, 5, 3);
            var overall  = Read("overall",  1, 10, NormalizeOverall(pedagogy, factual, ctx, goalAl));

            var halluc = root.TryGetProperty("hallucinationRisk", out var hr) && hr.ValueKind == JsonValueKind.True;
            // Fallback inference: factual<3 ise riski zorla
            if (factual < 3) halluc = true;

            var feedback = root.TryGetProperty("feedback", out var fb) && fb.ValueKind == JsonValueKind.String
                ? fb.GetString() ?? string.Empty
                : string.Empty;

            return new EvaluationDetail(pedagogy, factual, ctx, overall, halluc, feedback);
        }
        catch
        {
            return new EvaluationDetail(3, 3, 3, 5, false, "Parse edilemedi");
        }
    }

    // Sub-skorları 1-10 scale'ine normalize eder
    private static int NormalizeOverall(int pedagogy, int factual, int ctx, int goalAl)
        => Math.Clamp((int)Math.Round((pedagogy + factual + ctx + goalAl) / 20.0 * 10.0), 1, 10);

    private static string BuildCombinedFeedback(EvaluationDetail d)
    {
        var prefix = d.HallucinationRisk ? "[HALL] " : string.Empty;
        return $"{prefix}[F:{d.Factual} P:{d.Pedagogy} C:{d.Context}] {d.Feedback}".Trim();
    }

    private sealed record EvaluationDetail(
        int Pedagogy,
        int Factual,
        int Context,
        int Overall,
        bool HallucinationRisk,
        string Feedback);
}
