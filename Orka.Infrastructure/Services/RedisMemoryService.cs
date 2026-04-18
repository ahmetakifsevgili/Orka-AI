using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;
using StackExchange.Redis;

namespace Orka.Infrastructure.Services;

public class RedisMemoryService : IRedisMemoryService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<RedisMemoryService> _logger;

    // ── Evaluator feedback JSON şeması ──────────────────────────────────────
    // Eski format: "[HH:mm:ss] Puan: {score} - Not: {feedback}" (fragile parsing)
    // Yeni format: JSON — parse güvenliği için.
    private record EvaluatorRecord(int Score, string Feedback, DateTime At);

    public RedisMemoryService(IConnectionMultiplexer redis, ILogger<RedisMemoryService> logger)
    {
        _redis = redis;
        // Varsayılan veritabanı 0 alınır
        _db = redis.GetDatabase();
        _logger = logger;
    }

    public async Task RecordEvaluationAsync(Guid sessionId, int score, string feedback)
    {
        try
        {
            string key = $"orka:feedback:{sessionId}";
            var record = new EvaluatorRecord(score, feedback ?? string.Empty, DateTime.UtcNow);
            var entry = JsonSerializer.Serialize(record);

            // LPUSH: Listenin başından (solundan) ekler, böylece en yeniler en üstte kalır.
            await _db.ListLeftPushAsync(key, entry);

            // ListTrim: Listenin sonsuza kadar büyümesini engeller, sadece son 20 uyarıyı tutar.
            await _db.ListTrimAsync(key, 0, 19);

            // KeyExpire: 7 gün sonra (session tarihi eskiyince) otomatik silinir, Redis'i temiz tutar.
            await _db.KeyExpireAsync(key, TimeSpan.FromDays(7));
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "[Redis] Hatalar Defteri'ne puan kaydedilirken hata oluştu.");
        }
    }

    /// <summary>
    /// Evaluator feedback'ini TutorAgent'ın prompt'a enjekte edebileceği insan-okur formatta döner.
    /// Backward compat: yeni JSON ve eski string formatının ikisini de parse eder.
    /// </summary>
    public async Task<IEnumerable<string>> GetRecentFeedbackAsync(Guid sessionId, int count = 5)
    {
        try
        {
            string key = $"orka:feedback:{sessionId}";
            var items = await _db.ListRangeAsync(key, 0, count - 1);
            return items.Select(x => NormalizeFeedbackEntry(x.ToString())).ToList();
        }
        catch(Exception ex)
        {
             _logger.LogError(ex, "[Redis] Geçmiş geri bildirimler çekilirken hata oluştu.");
             return Enumerable.Empty<string>();
        }
    }

    /// <summary>
    /// Bir Redis list entry'sini insan okur formatına çevirir.
    /// JSON ise deserialize, değilse ham string (eski format) olarak döner.
    /// </summary>
    private static string NormalizeFeedbackEntry(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        // JSON başlangıcı → yeni format
        if (raw.StartsWith('{'))
        {
            try
            {
                var rec = JsonSerializer.Deserialize<EvaluatorRecord>(raw);
                if (rec is not null)
                    return $"[{rec.At:HH:mm:ss}] Puan: {rec.Score} - Not: {rec.Feedback}";
            }
            catch { /* bozuksa raw döner */ }
        }

        return raw; // eski format (backward compat)
    }

    public async Task<bool> CheckRateLimitAsync(string clientIp, int maxRequests, TimeSpan window)
    {
        try
        {
            string key = $"orka:rateLimit:{clientIp}";
            var count = await _db.StringIncrementAsync(key);
            
            // Eğer key ilk defa oluşturulduysa, Timer (TTL) başlat.
            if (count == 1)
            {
                await _db.KeyExpireAsync(key, window);
            }
            
            return count <= maxRequests;
        }
        catch (Exception ex)
        {
             _logger.LogError(ex, "[Redis] Rate Limit (Kota Kalkanı) kontrol edilirken hata oluştu.");
             // Redis çökerse sistemi kilitlememek için (Fail open) true döndürüyoruz.
             return true; 
        }
    }

    public async Task SetGlobalPolicyAsync(string policyText)
    {
        try
        {
            await _db.StringSetAsync("orka:globalPolicy", policyText);
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "[Redis] Global politika yazılırken hata oluştu.");
        }
    }

    public async Task<string> GetGlobalPolicyAsync()
    {
        try
        {
            var val = await _db.StringGetAsync("orka:globalPolicy");
            return val.HasValue ? val.ToString() : string.Empty;
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "[Redis] Global politika okunurken hata oluştu.");
            return string.Empty;
        }
    }

    // ── Faz 11: TutorAgent Bağlantı Katmanı ─────────────────────────────────

    public async Task SetLastPistonResultAsync(Guid sessionId, string code, string stdout, string stderr, string language)
    {
        try
        {
            var key = $"orka:piston:{sessionId}:last";
            var payload = JsonSerializer.Serialize(new
            {
                Code        = code.Length > 500 ? code[..500] + "..." : code, // Uzun kodu kırp
                Stdout      = stdout,
                Stderr      = stderr,
                Language    = language,
                ExecutedAt  = DateTime.UtcNow.ToString("O")
            });
            await _db.StringSetAsync(key, payload, TimeSpan.FromMinutes(30));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Redis] Piston sonucu kaydedilirken hata oluştu. SessionId={SessionId}", sessionId);
        }
    }

    public async Task<string> GetLastPistonResultAsync(Guid sessionId)
    {
        try
        {
            var val = await _db.StringGetAsync($"orka:piston:{sessionId}:last");
            return val.HasValue ? val.ToString() : string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Redis] Piston sonucu okunurken hata oluştu. SessionId={SessionId}", sessionId);
            return string.Empty;
        }
    }

    public async Task SetWikiReadyAsync(Guid topicId)
    {
        try
        {
            await _db.StringSetAsync(
                $"orka:wiki-ready:{topicId}",
                DateTime.UtcNow.ToString("O"),
                TimeSpan.FromHours(1));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Redis] Wiki-ready sinyali yazılırken hata oluştu. TopicId={TopicId}", topicId);
        }
    }

    // ── Faz 12: Dynamic Few-Shot — Altın Örnek Kütüphanesi ──────────────────

    public async Task SaveGoldExampleAsync(Guid topicId, string userMessage, string agentResponse, int score)
    {
        try
        {
            var key     = $"orka:gold:{topicId}";
            var payload = JsonSerializer.Serialize(new GoldExample(
                UserMessage:   userMessage.Length > 300 ? userMessage[..300] + "..." : userMessage,
                AgentResponse: agentResponse.Length > 800 ? agentResponse[..800] + "..." : agentResponse,
                Score:         score,
                CreatedAt:     DateTime.UtcNow.ToString("O")
            ));

            await _db.ListLeftPushAsync(key, payload);
            await _db.ListTrimAsync(key, 0, 9);                          // Max 10 örnek
            await _db.KeyExpireAsync(key, TimeSpan.FromDays(30));

            _logger.LogInformation("[Redis] Altın örnek kaydedildi. TopicId={TopicId} Puan={Score}", topicId, score);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Redis] Altın örnek kaydedilirken hata oluştu. TopicId={TopicId}", topicId);
        }
    }

    public async Task<IEnumerable<GoldExample>> GetGoldExamplesAsync(Guid topicId, int count = 2)
    {
        try
        {
            var key   = $"orka:gold:{topicId}";
            var items = await _db.ListRangeAsync(key, 0, count - 1);

            return items
                .Where(x => x.HasValue)
                .Select(x =>
                {
                    try   { return JsonSerializer.Deserialize<GoldExample>(x.ToString()); }
                    catch { return null; }
                })
                .Where(x => x != null)
                .Select(x => x!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Redis] Altın örnekler okunurken hata oluştu. TopicId={TopicId}", topicId);
            return Enumerable.Empty<GoldExample>();
        }
    }

    // ── HUD: Gerçek Zamanlı Ajan Telemetrisi ────────────────────────────────

    private static readonly string[] AllAgentRoles =
        ["Tutor", "Evaluator", "Supervisor", "Summarizer", "Korteks", "Grader", "DeepPlan"];

    public async Task RecordAgentMetricAsync(string agentRole, long latencyMs, bool isSuccess, string? provider = null)
    {
        try
        {
            var key = $"orka:metrics:{agentRole}";
            var payload = JsonSerializer.Serialize(new AgentCallRecord(
                LatencyMs:  latencyMs,
                IsSuccess:  isSuccess,
                Provider:   provider ?? "Unknown",
                RecordedAt: DateTime.UtcNow.ToString("O")
            ));

            await _db.ListLeftPushAsync(key, payload);
            await _db.ListTrimAsync(key, 0, 99);       // Max 100 kayıt
            await _db.KeyExpireAsync(key, TimeSpan.FromHours(24));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Redis] Ajan metriği kaydedilirken hata oluştu. Role={Role}", agentRole);
        }
    }

    public async Task<IEnumerable<AgentMetricSummary>> GetSystemMetricsAsync()
    {
        var result = new List<AgentMetricSummary>();

        foreach (var role in AllAgentRoles)
        {
            try
            {
                var key   = $"orka:metrics:{role}";
                var items = await _db.ListRangeAsync(key, 0, 99);

                if (items.Length == 0)
                {
                    result.Add(new AgentMetricSummary(role, 0, 0, 0, 0, "—", "—"));
                    continue;
                }

                var records = items
                    .Select(x =>
                    {
                        try { return JsonSerializer.Deserialize<AgentCallRecord>(x.ToString()); }
                        catch { return null; }
                    })
                    .Where(r => r != null)
                    .Select(r => r!)
                    .ToList();

                var totalCalls  = records.Count;
                var errorCount  = records.Count(r => !r.IsSuccess);
                var avgLatency  = records.Average(r => (double)r.LatencyMs);
                var lastRecord  = records.First(); // LPUSH → ilk eleman en yeni

                result.Add(new AgentMetricSummary(
                    AgentRole:    role,
                    AvgLatencyMs: Math.Round(avgLatency, 0),
                    TotalCalls:   totalCalls,
                    ErrorCount:   errorCount,
                    ErrorRatePct: Math.Round(errorCount / (double)totalCalls * 100, 1),
                    LastProvider: lastRecord.Provider,
                    LastCallAt:   lastRecord.RecordedAt
                ));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Redis] Sistem metrikleri okunurken hata oluştu. Role={Role}", role);
                result.Add(new AgentMetricSummary(role, 0, 0, 0, 0, "—", "—"));
            }
        }

        return result;
    }

    public async Task<IEnumerable<ProviderUsageStat>> GetProviderUsageAsync()
    {
        try
        {
            // Tüm ajanların call history'lerini dolaş, provider sayım yap
            var allRecords = new List<AgentCallRecord>();

            foreach (var role in AllAgentRoles)
            {
                var key = $"orka:metrics:{role}";
                var items = await _db.ListRangeAsync(key, 0, -1);
                foreach (var item in items)
                {
                    try
                    {
                        var rec = JsonSerializer.Deserialize<AgentCallRecord>(item!);
                        if (rec is not null) allRecords.Add(rec);
                    }
                    catch { /* skip malformed */ }
                }
            }

            if (allRecords.Count == 0) return Enumerable.Empty<ProviderUsageStat>();

            var total = allRecords.Count;

            return allRecords
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Provider) ? "Unknown" : r.Provider)
                .Select(g => new ProviderUsageStat(
                    Provider:     g.Key,
                    CallCount:    g.Count(),
                    ErrorCount:   g.Count(r => !r.IsSuccess),
                    Percentage:   Math.Round(g.Count() * 100.0 / total, 1),
                    AvgLatencyMs: Math.Round(g.Average(r => (double)r.LatencyMs), 0)
                ))
                .OrderByDescending(p => p.CallCount)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Redis] Provider kullanım dağılımı okunurken hata oluştu.");
            return Enumerable.Empty<ProviderUsageStat>();
        }
    }

    public async Task<IEnumerable<EvaluatorLogEntry>> GetRecentEvaluatorLogsAsync(int count = 20)
    {
        try
        {
            // Tüm feedback key'lerini tarar — "orka:feedback:*"
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var keys   = server.Keys(pattern: "orka:feedback:*").ToArray();

            if (keys.Length == 0) return Enumerable.Empty<EvaluatorLogEntry>();

            var allEntries = new List<EvaluatorLogEntry>();

            foreach (var key in keys.Take(50)) // Max 50 session tara
            {
                var items = await _db.ListRangeAsync(key, 0, 9); // Her session'dan max 10 al
                foreach (var item in items)
                {
                    var raw = item.ToString();
                    if (TryParseEvaluatorEntry(raw, out var entry))
                    {
                        allEntries.Add(entry);
                    }
                }
            }

            return allEntries
                .OrderByDescending(e => e.RecordedAt)
                .Take(count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Redis] Evaluator logları okunurken hata oluştu.");
            return Enumerable.Empty<EvaluatorLogEntry>();
        }
    }

    /// <summary>
    /// Hem yeni JSON hem de eski ham-string formatı destekler (migration güvenliği).
    /// Yeni kayıtlar JSON, eski kayıtlar "[HH:mm:ss] Puan: X - Not: Y" olarak duruyor olabilir.
    /// </summary>
    private static bool TryParseEvaluatorEntry(string raw, out EvaluatorLogEntry entry)
    {
        entry = default!;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        // 1) JSON yolu (yeni format)
        if (raw.StartsWith('{'))
        {
            try
            {
                var rec = JsonSerializer.Deserialize<EvaluatorRecord>(raw);
                if (rec is not null)
                {
                    entry = new EvaluatorLogEntry(rec.Score, rec.Feedback, rec.At.ToString("HH:mm:ss"));
                    return true;
                }
            }
            catch { /* fallback */ }
        }

        // 2) Eski string formatı (backward compat)
        var timeEnd = raw.IndexOf(']');
        var recordedAt = timeEnd > 0 ? raw[1..timeEnd] : "—";

        var scoreIdx = raw.IndexOf("Puan: ", StringComparison.Ordinal);
        if (scoreIdx < 0) return false;

        // Score substring: "Puan: " sonrası → ilk boşluk veya string sonu
        var afterScore = raw[(scoreIdx + 6)..];
        var spaceIdx   = afterScore.IndexOf(' ');
        var scoreStr   = spaceIdx < 0 ? afterScore : afterScore[..spaceIdx];
        if (!int.TryParse(scoreStr.Trim(), out var score)) return false;

        // Feedback: "- Not: " sonrası (ilk occurrence), yoksa boş
        const string noteMarker = "- Not: ";
        var noteIdx = raw.IndexOf(noteMarker, scoreIdx, StringComparison.Ordinal);
        var feedback = noteIdx > 0 ? raw[(noteIdx + noteMarker.Length)..].Trim() : string.Empty;

        entry = new EvaluatorLogEntry(score, feedback, recordedAt);
        return true;
    }

    // ── Faz 14: Topic-level Kümülatif Puanlama ─────────────────────────────

    private record TopicScoreRecord(int Score, string Feedback, DateTime At);

    public async Task RecordTopicScoreAsync(Guid topicId, int score, string feedback)
    {
        try
        {
            var key = $"orka:topic_score:{topicId}";
            var record = new TopicScoreRecord(score, feedback ?? string.Empty, DateTime.UtcNow);
            var entry = JsonSerializer.Serialize(record);

            await _db.ListLeftPushAsync(key, entry);
            await _db.ListTrimAsync(key, 0, 49); // Max 50 kayıt
            await _db.KeyExpireAsync(key, TimeSpan.FromDays(30));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Redis] Topic score kaydedilirken hata. TopicId={TopicId}", topicId);
        }
    }

    public async Task<(double avgScore, int totalEvals)> GetTopicScoreAsync(Guid topicId)
    {
        try
        {
            var key = $"orka:topic_score:{topicId}";
            var items = await _db.ListRangeAsync(key, 0, -1);
            
            if (items.Length == 0) return (0, 0);

            var scores = items
                .Where(x => x.HasValue)
                .Select(x =>
                {
                    try
                    {
                        var rec = JsonSerializer.Deserialize<TopicScoreRecord>(x.ToString());
                        return rec?.Score ?? 0;
                    }
                    catch { return 0; }
                })
                .Where(s => s > 0)
                .ToList();

            if (scores.Count == 0) return (0, 0);
            return (Math.Round(scores.Average(), 1), scores.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Redis] Topic score okunurken hata. TopicId={TopicId}", topicId);
            return (0, 0);
        }
    }

    // ── Faz 15: Yaşayan Organizasyon (Öğrenci Anlayış Takibi) ────────────────
    
    private record StudentProfileRecord(int UnderstandingScore, string Weaknesses, DateTime RecordedAt);

    public async Task RecordStudentProfileAsync(Guid topicId, int understandingScore, string weaknesses)
    {
        try
        {
            var key = $"orka:student_profile:{topicId}";
            var record = new StudentProfileRecord(understandingScore, weaknesses ?? string.Empty, DateTime.UtcNow);
            
            // Konu bazında sadece en güncel profili tek record olarak tutmak isteyebiliriz.
            // Ama zaman içerisindeki değişimi anlamak için liste de tutulabilir.
            // Şimdilik sadece en güncel veriyi String (SET) olarak ya da ufak bir liste olarak tutalım.
            // Tek kaynakta güncel durum kalsın diye SET/GET yapalım (üzerine yazsın, "yaşayan organizasyon"un "şu anki" hali)
            
            var payload = JsonSerializer.Serialize(record);
            await _db.StringSetAsync(key, payload, TimeSpan.FromDays(30));
            
            _logger.LogInformation("[Redis] Öğrenci Profili güncellendi. TopicId={TopicId} Puan={Score}", topicId, understandingScore);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Redis] Öğrenci profili kaydedilirken hata. TopicId={TopicId}", topicId);
        }
    }

    public async Task<(int score, string weaknesses)?> GetStudentProfileAsync(Guid topicId)
    {
        try
        {
            var key = $"orka:student_profile:{topicId}";
            var val = await _db.StringGetAsync(key);
            
            if (val.IsNullOrEmpty) return null;
            
            var record = JsonSerializer.Deserialize<StudentProfileRecord>(val.ToString());
            if (record == null) return null;
            
            return (record.UnderstandingScore, record.Weaknesses);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Redis] Öğrenci profili okunurken hata. TopicId={TopicId}", topicId);
            return null;
        }
    }
}
