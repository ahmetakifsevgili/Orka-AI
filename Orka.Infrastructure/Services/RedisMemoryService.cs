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
            string entry = $"[{DateTime.UtcNow:HH:mm:ss}] Puan: {score} - Not: {feedback}";
            
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

    public async Task<IEnumerable<string>> GetRecentFeedbackAsync(Guid sessionId, int count = 5)
    {
        try
        {
            string key = $"orka:feedback:{sessionId}";
            var items = await _db.ListRangeAsync(key, 0, count - 1);
            return items.Select(x => x.ToString());
        }
        catch(Exception ex)
        {
             _logger.LogError(ex, "[Redis] Geçmiş geri bildirimler çekilirken hata oluştu.");
             return Enumerable.Empty<string>();
        }
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
                    // Format: "[HH:mm:ss] Puan: {score} - Not: {feedback}"
                    var raw = item.ToString();
                    var timeEnd = raw.IndexOf(']');
                    var recordedAt = timeEnd > 0 ? raw[1..timeEnd] : "—";

                    var scoreIdx = raw.IndexOf("Puan: ");
                    var noteIdx  = raw.IndexOf(" - Not: ");

                    if (scoreIdx < 0) continue;

                    var scoreStr = noteIdx > scoreIdx
                        ? raw[(scoreIdx + 6)..noteIdx]
                        : raw[(scoreIdx + 6)..];

                    var feedback = noteIdx > 0 ? raw[(noteIdx + 8)..] : "";

                    if (int.TryParse(scoreStr.Trim(), out var score))
                    {
                        allEntries.Add(new EvaluatorLogEntry(score, feedback.Trim(), recordedAt));
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
}
