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

    /// <summary>
    /// Tüm ajanlar üzerindeki provider kullanım dağılımı (GitHub / Groq / Gemini).
    /// HUD "Model Mix" widget'ında sağlık göstergesi olarak kullanılır —
    /// failover'a ne kadar sık düştüğünü gösterir (Primary %85+ = sağlıklı).
    /// </summary>
    Task<IEnumerable<ProviderUsageStat>> GetProviderUsageAsync();

    // ── Cache / Health / Invalidation ───────────────────────────────────────

    Task<string?> GetJsonAsync(string key);

    Task SetJsonAsync(string key, string payload, TimeSpan ttl);

    Task DeleteKeyAsync(string key);

    Task<long> GetTopicVersionAsync(Guid topicId);

    Task<long> BumpTopicVersionAsync(Guid topicId, string reason);

    Task InvalidateLearningCachesAsync(Guid userId, Guid topicId, string reason);

    Task RecordCacheMetricAsync(string area, bool hit, string? tool = null, double? latencyMs = null);

    Task<IEnumerable<CacheMetricSummary>> GetCacheMetricsAsync();

    Task<RedisHealthDto> GetRedisHealthAsync();

    Task<IReadOnlyList<string>> GetRecentQuestionHashesAsync(Guid userId, Guid topicId, int count = 80);

    Task RememberQuestionHashesAsync(Guid userId, Guid topicId, IEnumerable<string> hashes);

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
    /// 30 günlük TTL ile expire eder (weakness decay).
    /// </summary>
    Task<(int score, string weaknesses)?> GetStudentProfileAsync(Guid topicId);

    // ── Faz 16: Anlık Müdahale (EvaluatorAgent → TutorAgent) ────────────────

    /// <summary>
    /// EvaluatorAgent düşük puan (≤ 6) verdiğinde TutorAgent'ın bir sonraki cevabında
    /// stil düzeltmesi için ham feedback'i kısa bir bayrakla saklar.
    /// Key: orka:lowquality:{sessionId} | TTL: 5 dk (tek-kullanımlık).
    /// </summary>
    Task SetLowQualityFeedbackAsync(Guid sessionId, int score, string feedback);

    /// <summary>
    /// TutorAgent prompt enjekte ettikten sonra okuduğu flag'i atomik olarak siler
    /// (StringGetDelete) — aynı uyarı iki kez kullanılmaz.
    /// </summary>
    Task<(int score, string feedback)?> GetAndClearLowQualityFeedbackAsync(Guid sessionId);

    // ── Faz 16: Korteks → Quiz Köprüsü ──────────────────────────────────────

    /// <summary>
    /// KorteksAgent araştırma raporunun özetini topicId için saklar.
    /// QuizAgent ve TutorAgent quiz üretirken bu bağlamı enjekte eder.
    /// Key: orka:korteks:{topicId} | TTL: 2 saat.
    /// </summary>
    Task SaveKorteksResearchReportAsync(Guid topicId, string report);

    /// <summary>
    /// Topic için kaydedilmiş Korteks raporunu döndürür. Yoksa null.
    /// </summary>
    Task<string?> GetKorteksResearchReportAsync(Guid topicId);

    // ── YouTube RAG: Cache-First Strateji ────────────────────────────────────

    /// <summary>
    /// YouTube video arama sonuçları ve transcript'i topicId için cache'ler.
    /// DeepPlan oluşturma veya TutorAgent ilk ders anında yazılır.
    /// Key: orka:youtube:{topicId} | TTL: 24 saat.
    /// </summary>
    Task SaveYouTubeContextAsync(Guid topicId, string payload);

    /// <summary>
    /// Topic için cache'lenmiş YouTube context'i döndürür. Yoksa null.
    /// TutorAgent her mesajda API çağırmak yerine buradan okur (kota tasarrufu).
    /// </summary>
    Task<string?> GetYouTubeContextAsync(Guid topicId);
}
