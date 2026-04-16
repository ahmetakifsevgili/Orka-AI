using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orka.Core.DTOs;

namespace Orka.Core.Interfaces;

public interface IRedisMemoryService
{
    // Caching/Storing Feedback
    Task RecordEvaluationAsync(Guid sessionId, int score, string feedback);
    Task<IEnumerable<string>> GetRecentFeedbackAsync(Guid sessionId, int count = 5);

    // Rate Limiting (Token Bucket)
    Task<bool> CheckRateLimitAsync(string clientIp, int maxRequests, TimeSpan window);

    // Global Policies / Pub-Sub Hazırlığı
    Task SetGlobalPolicyAsync(string policyText);
    Task<string> GetGlobalPolicyAsync();

    // ── Faz 11: TutorAgent Bağlantı Katmanı ─────────────────────────────────

    /// <summary>
    /// Piston'dan dönen kod çalıştırma sonucunu session'a bağlı Redis key'ine yazar.
    /// TutorAgent bir sonraki mesajda bu sonucu okuyarak bağlamsal yorum yapabilir.
    /// TTL: 30 dakika (aktif session süresiyle uyumlu).
    /// </summary>
    Task SetLastPistonResultAsync(Guid sessionId, string code, string stdout, string stderr, string language);

    /// <summary>
    /// Session'ın son Piston çalıştırma sonucunu JSON olarak döner.
    /// Sonuç yoksa veya TTL dolmuşsa boş string döner.
    /// </summary>
    Task<string> GetLastPistonResultAsync(Guid sessionId);

    /// <summary>
    /// KorteksAgent araştırması tamamlandığında topicId için sinyal yazar.
    /// TutorAgent bu key'i kontrol ederek güncel araştırmanın mevcut olduğunu öğrenir.
    /// TTL: 1 saat.
    /// </summary>
    Task SetWikiReadyAsync(Guid topicId);

    // ── Faz 12: Dynamic Few-Shot — Altın Örnek Kütüphanesi ──────────────────

    /// <summary>
    /// 9-10 puan alan başarılı diyalog çiftini Redis listesine yazar.
    /// Key: "orka:gold:{topicId}" | Max 10 kayıt (LTRIM) | TTL: 30 gün.
    /// </summary>
    Task SaveGoldExampleAsync(Guid topicId, string userMessage, string agentResponse, int score);

    /// <summary>
    /// Konu için kaydedilmiş altın örnekleri döner.
    /// TutorAgent bunları few-shot olarak system prompt'a enjekte eder.
    /// </summary>
    Task<IEnumerable<GoldExample>> GetGoldExamplesAsync(Guid topicId, int count = 2);

    // ── HUD: Gerçek Zamanlı Ajan Telemetrisi ────────────────────────────────

    /// <summary>
    /// Bir ajanın tamamladığı istek için gerçek gecikme (ms) ve başarı/hata durumunu Redis'e yazar.
    /// Key: "orka:metrics:{agentRole}" | Max 100 kayıt (LTRIM) | TTL: 24 saat.
    /// </summary>
    Task RecordAgentMetricAsync(string agentRole, long latencyMs, bool isSuccess, string? provider = null);

    /// <summary>
    /// Tüm ajanlar için Redis'ten ortalama latency, çağrı sayısı ve hata oranı döner.
    /// </summary>
    Task<IEnumerable<AgentMetricSummary>> GetSystemMetricsAsync();

    /// <summary>
    /// Session bazında EvaluatorAgent'ın verdiği ham puanların genelini döner (Dashboard LLMOps Log).
    /// </summary>
    Task<IEnumerable<EvaluatorLogEntry>> GetRecentEvaluatorLogsAsync(int count = 20);
}
