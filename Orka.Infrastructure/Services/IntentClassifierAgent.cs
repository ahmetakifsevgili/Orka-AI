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

    // Faz 16: Intent Cache — Aynı session+mesaj hash'i için duplicated LLM call önleme
    // Supervisor ve Analyzer aynı mesaj setini ayrı ayrı ClassifyAsync ile çağırıyordu → 2x maliyet.
    // Şimdi ilk çağrıda cache'leniyor, ikinci çağrıda cache'ten dönüyor.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (IntentResult Result, DateTime CachedAt)> _intentCache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2); // 2 dakika sonra expire

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

        // Faz 16: Cache kontrolü — aynı bağlam için duplicated LLM call önle
        var cacheKey = conversationContext.GetHashCode().ToString();
        
        // Eski cache girdilerini temizle (memory leak önlemi)
        foreach (var expired in _intentCache.Where(kv => DateTime.UtcNow - kv.Value.CachedAt > CacheTtl).Select(kv => kv.Key).ToList())
            _intentCache.TryRemove(expired, out _);

        if (_intentCache.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.CachedAt < CacheTtl)
        {
            _logger.LogInformation("[IntentClassifier] Cache HIT — duplicated LLM call önlendi. Intent={Intent}", cached.Result.Intent);
            return cached.Result;
        }

        var systemPrompt = """
            Sen Orka AI'nın Öğrenci Profil Geliştiricisi ve Niyet Analizcisisin (Intent Classifier & Student Profiler).
            Sana verilen öğrenci-ajan diyaloğunu okuyarak kullanıcının SON niyetini ve genel anlama seviyesini tespit et.

            SINIFLANDIRMA KATEGORİLERİ (Intent):
            - UNDERSTOOD   : Kullanıcı konuyu kavradı, ilerlemek istiyor.
            - CONFUSED     : Kullanıcı anlamadı (soru işaretleri, sessizlik, kısa olumsuz yanıtlar).
            - CHANGE_TOPIC : Kullanıcı tamamen farklı bir konuya geçmek istiyor.
            - QUIZ_REQUEST : Kullanıcı test veya sınav istiyor.
            - CONTINUE     : Belirsiz, pasif devam.

            GÜVEN SKORU KURALLARI (Confidence):
            - 0.90 - 1.00 : Tek mesajda net niyet
            - 0.50 altı   : Sistem kararı erteledi.

            ÖĞRENCİ PROFİLİ (Yaşayan Organizasyon):
            - 'understandingScore' (1-10): Öğrencinin konuyu ne kadar kavradığı. 1=Hiç anlamadı, 10=Tamamen kavradı.
            - 'weaknesses': Öğrencinin zorlandığı veya yanlış anladığı tespit edilen spesifik kavramlar (string). Yoksa boş bırak.

            SADECE ŞU JSON FORMATINDA DÖN:
            {
              "intent": "CONFUSED",
              "confidence": 0.87,
              "reasoning": "Kullanıcı '???' yazdı.",
              "understandingScore": 3,
              "weaknesses": "For döngüsünün syntax'ını ve ne zaman duracağını (şart bloğunu) anlamadı."
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
                    Intent:             parsed.intent.ToUpper(),
                    Confidence:         Math.Clamp(parsed.confidence, 0.0, 1.0),
                    Reasoning:          parsed.reasoning,
                    UnderstandingScore: Math.Clamp(parsed.understandingScore, 1, 10),
                    Weaknesses:         parsed.weaknesses ?? string.Empty
                );

                _logger.LogInformation(
                    "[IntentClassifier] Intent: {Intent} | Conf: {Confidence:P0} | Score: {Score}/10 | Weaknesses: {Weaknesses}",
                    result.Intent, result.Confidence, result.UnderstandingScore, result.Weaknesses);

                // Faz 16: Cache'e yaz — Supervisor ve Analyzer ikinci kez çağırdığında LLM call skip
                _intentCache[cacheKey] = (result, DateTime.UtcNow);

                return result;
            }

            _logger.LogWarning("[IntentClassifier] Geçersiz intent değeri. Fail-safe CONTINUE döndürülüyor.");
            return new IntentResult("CONTINUE", 0.0, "Parse hatası — fail-safe.", 5, "");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[IntentClassifier] LLM hatası. Fail-safe CONTINUE döndürülüyor.");
            return new IntentResult("CONTINUE", 0.0, "LLM hatası — fail-safe.", 5, "");
        }
    }

    private class IntentJsonFormat
    {
        public string intent             { get; set; } = "CONTINUE";
        public double confidence         { get; set; } = 0.5;
        public string reasoning          { get; set; } = string.Empty;
        public int understandingScore    { get; set; } = 5;
        public string weaknesses         { get; set; } = string.Empty;
    }
}
