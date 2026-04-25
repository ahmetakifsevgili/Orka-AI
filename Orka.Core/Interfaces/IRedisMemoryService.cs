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
    /// KorteksAgent araştırması tamamlandığında topicId için araştırma raporunu yazar.
    /// TutorAgent bu key'i kontrol ederek güncel araştırmanın içeriğini okur.
    /// TTL: 12 saat.
    /// </summary>
    Task SetKorteksResearchReportAsync(Guid topicId, string reportContent);

    /// <summary>
    /// Korteks araştırmasının rapor özetini döner. Henüz yoksa boş döner.
    /// TTL: 12 saat.
    /// </summary>
    Task<string> GetKorteksResearchReportAsync(Guid topicId);

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

    /// <summary>
    /// Tüm ajanlar üzerindeki provider kullanım dağılımı (GitHub / Groq / Gemini).
    /// HUD "Model Mix" widget'ında sağlık göstergesi olarak kullanılır —
    /// failover'a ne kadar sık düştüğünü gösterir (Primary %85+ = sağlıklı).
    /// </summary>
    Task<IEnumerable<ProviderUsageStat>> GetProviderUsageAsync();

    // ── Faz 14: Topic-level Kümülatif Puanlama ─────────────────────────────

    /// <summary>
    /// Evaluator puanını topic bazında da kaydeder. Session değişse bile topic puanı korunur.
    /// Key: "orka:topic_score:{topicId}" | Max 50 kayıt | TTL: 30 gün.
    /// </summary>
    Task RecordTopicScoreAsync(Guid topicId, int score, string feedback);

    /// <summary>
    /// Topic bazında kümülatif puan ortalaması ve toplam değerlendirme sayısı döner.
    /// </summary>
    Task<(double avgScore, int totalEvals)> GetTopicScoreAsync(Guid topicId);

    // ── Faz 15: Yaşayan Organizasyon (Öğrenci Anlayış Takibi) ────────────────
    
    /// <summary>
    /// AnalyzerAgent (IntentClassifier) aracılığıyla elde edilen öğrenci anlama seviyesi ve zayıf noktaları kaydeder.
    /// Key: "orka:student_profile:{topicId}"
    /// </summary>
    Task RecordStudentProfileAsync(Guid topicId, int understandingScore, string weaknesses);

    /// <summary>
    /// TutorAgent'ın kullanması için topic bazındaki son öğrenci profili notlarını çeker.
    /// </summary>
    Task<(int score, string weaknesses)?> GetStudentProfileAsync(Guid topicId);
}
