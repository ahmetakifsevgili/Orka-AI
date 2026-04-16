using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Niyet Çözümleyici Ajan (Intent Classifier)
///
/// Rolü:
///   Kullanıcının son 6 mesajını okuyarak tek bir LLM çağrısıyla
///   {intent, confidence, reasoning} JSON üretir.
///
/// Bu çıktı iki ayrı kanalda kullanılır:
///   1. AnalyzerAgent → intent == UNDERSTOOD && confidence >= 0.65 → IsComplete = true
///   2. SupervisorAgent → intent == QUIZ_REQUEST → QUIZ rotasına, CHANGE_TOPIC → özel handling
///
/// Araştırma Temeli (Multi-Turn NLU):
///   - Son 6 mesaj: 3 ajan cevabı + 3 kullanıcı mesajı. Bu pencere tek cümlenin anlamsız
///     kalabileceği durumlarda bağlam zenginliği sağlar (örn. "Tamam" tek başına zayıf sinyal,
///     ama önceki "Anlamadım"  + "Hmm" + "Tamam" zinciri CONFUSED → UNDERSTOOD dönüşümünü gösterir).
///   - Confidence thresholds: 0.65 altında belirsiz sinyal → IsComplete hiçbir zaman true olmaz.
///   - Self-correction: Eğer confidence < 0.50 ise sistem CONTINUE döner (ne ileri, ne geri).
/// </summary>
public class IntentClassifierAgent : IIntentClassifierAgent
{
    private readonly IAIAgentFactory _factory;
    private readonly ILogger<IntentClassifierAgent> _logger;

    // Geçerli intent kategorileri
    private static readonly HashSet<string> ValidIntents = new(StringComparer.OrdinalIgnoreCase)
    {
        "UNDERSTOOD", "CONFUSED", "CHANGE_TOPIC", "QUIZ_REQUEST", "CONTINUE"
    };

    public IntentClassifierAgent(IAIAgentFactory factory, ILogger<IntentClassifierAgent> logger)
    {
        _factory = factory;
        _logger  = logger;
    }

    public async Task<IntentResult> ClassifyAsync(
        IEnumerable<Message> recentMessages,
        CancellationToken ct = default)
    {
        // ── Son 6 mesajı al (çift taraflı diyalog bağlamı) ──────────────────
        var lastSix = recentMessages
            .TakeLast(6)
            .Select(m => $"[{m.Role.ToUpper()}]: {m.Content.Substring(0, Math.Min(m.Content.Length, 300))}");

        var conversationContext = string.Join("\n---\n", lastSix);

        var systemPrompt = """
            Sen Orka AI'nın Niyet Analizcisisin (Intent Classifier).
            Sana verilen öğrenci-ajan diyaloğunu okuyarak kullanıcının SON niyetini tespit et.

            SINIFLANDIRMA KATEGORİLERİ:
            - UNDERSTOOD   : Kullanıcı konuyu kavradı, ilerlemek istiyor.
            - CONFUSED     : Kullanıcı anlamadı, sinyal zayıf veya güçlü förk etmez (soru işaretleri, sessizlik, kısa olumsuz yanıtlar).
            - CHANGE_TOPIC : Kullanıcı tamamen farklı bir konuya geçmek istiyor.
            - QUIZ_REQUEST : Kullanıcı test veya sınav istiyor.
            - CONTINUE     : Belirsiz, ne ileri ne geri. Pasif devam.

            GÜVEN SKORU KURALLARI:
            - 0.90 - 1.00 : Tek mesajda net niyet ("Anladım geçelim", "Sen soru sor")
            - 0.70 - 0.89 : Genel eğilim var ama kesin değil
            - 0.50 - 0.69 : Belirsiz sinyal. CONTINUE tercih edebilirsin.
            - 0.50 altı   : Sistem kararı erteledi.

            KRİTİK KURAL: Belirsiz kısa semboller veya tek harfler CONFUSED'dur, CONTINUE değeil.
            '???' üç soru işareti güçlü CONFUSED sinyalidir. Minimum 0.80 confidence at.
            '!!!' veya 'ne' veya 'haa' veya 'anlamadım' da CONFUSED, en az 0.75 confidence.
            'hmm' veya '...' belirsizdir — CONTINUE veya düşük-confidence CONFUSED olabilir.

            FEW-SHOT ÖRNEKLER (bunu base al):
            Mesaj: "Anladım, geçelim"  → {"intent": "UNDERSTOOD", "confidence": 0.95, "reasoning": "Net geçiş ifadesi."}
            Mesaj: "???"               → {"intent": "CONFUSED",   "confidence": 0.88, "reasoning": "Üç soru işareti güçlü anlamaılık sinyali."}
            Mesaj: "anlamadım"        → {"intent": "CONFUSED",   "confidence": 0.92, "reasoning": "Doğrudan anlayamama bildirimi."}
            Mesaj: "hmm"              → {"intent": "CONTINUE",   "confidence": 0.55, "reasoning": "Belirsiz; pasif devam."}
            Mesaj: "soru sor bana"    → {"intent": "QUIZ_REQUEST","confidence": 0.91, "reasoning": "Açık quiz talebi."}
            Mesaj: "tamam peki"       → {"intent": "UNDERSTOOD",  "confidence": 0.70, "reasoning": "Kabul var ama kesin değil."}

            SADECE ŞU JSON FORMATINDA DÖN:
            {
              "intent": "CONFUSED",
              "confidence": 0.87,
              "reasoning": "Kullanıcı '???' yazdı, bu güçlü CONFUSED sinyali."
            }
            """;

        var userMessage = $"Diyalog:\n\n{conversationContext}";

        try
        {
            var response = await _factory.CompleteChatAsync(AgentRole.Analyzer, systemPrompt, userMessage, ct);
            var cleanJson = response.Replace("```json", "").Replace("```", "").Trim();

            var parsed = JsonSerializer.Deserialize<IntentJsonFormat>(cleanJson);

            if (parsed != null && ValidIntents.Contains(parsed.intent))
            {
                var result = new IntentResult(
                    Intent:     parsed.intent.ToUpper(),
                    Confidence: Math.Clamp(parsed.confidence, 0.0, 1.0),
                    Reasoning:  parsed.reasoning
                );

                _logger.LogInformation(
                    "[IntentClassifier] Intent: {Intent} | Confidence: {Confidence:P0} | Sebep: {Reason}",
                    result.Intent, result.Confidence, result.Reasoning);

                return result;
            }

            _logger.LogWarning("[IntentClassifier] Geçersiz intent değeri. Fail-safe CONTINUE döndürülüyor.");
            return new IntentResult("CONTINUE", 0.0, "Parse hatası — fail-safe.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[IntentClassifier] LLM hatası. Fail-safe CONTINUE döndürülüyor.");
            return new IntentResult("CONTINUE", 0.0, "LLM hatası — fail-safe.");
        }
    }

    private class IntentJsonFormat
    {
        public string intent     { get; set; } = "CONTINUE";
        public double confidence { get; set; } = 0.5;
        public string reasoning  { get; set; } = string.Empty;
    }
}
