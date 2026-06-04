using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Utilities;
using StackExchange.Redis;

namespace Orka.Infrastructure.Services;

public class RedisMemoryService : IRedisMemoryService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<RedisMemoryService> _logger;
    private static int _streamsUnsupported;

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
            _logger.LogError(
                "[Redis] Hatalar Defteri'ne puan kaydedilirken hata olustu. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
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
             _logger.LogError(
                 "[Redis] Gecmis geri bildirimler cekilirken hata olustu. ErrorType={ErrorType}",
                 LogPrivacyGuard.SafeExceptionType(ex));
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
             _logger.LogError(
                 "[Redis] Rate Limit (Kota Kalkani) kontrol edilirken hata olustu. ErrorType={ErrorType}",
                 LogPrivacyGuard.SafeExceptionType(ex));
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
            _logger.LogError(
                "[Redis] Global politika yazilirken hata olustu. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
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
            _logger.LogError(
                "[Redis] Global politika okunurken hata olustu. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
            return string.Empty;
        }
    }

    // ── Faz 11: TutorAgent Bağlantı Katmanı ─────────────────────────────────

    public async Task SetLastPistonResultAsync(
        Guid sessionId,
        string code,
        string stdout,
        string stderr,
        string language,
        string phase = "run",
        string? compileError = null,
        string? runtimeError = null,
        bool success = true,
        string? safeTutorSummary = null)
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
                  Phase       = phase,
                  CompileError = compileError,
                  RuntimeError = runtimeError,
                  Success     = success,
                  SafeTutorSummary = safeTutorSummary,
                  ExecutedAt  = DateTime.UtcNow.ToString("O")
              });
            await _db.StringSetAsync(key, payload, TimeSpan.FromMinutes(30));
        }
        catch (Exception ex)
        {
            _logger.LogError("[Redis] Piston sonucu kaydedilirken hata olustu. SessionRef={SessionRef} ErrorType={ErrorType}",
                SessionRef(sessionId), LogPrivacyGuard.SafeExceptionType(ex));
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
            _logger.LogError("[Redis] Piston sonucu okunurken hata olustu. SessionRef={SessionRef} ErrorType={ErrorType}",
                SessionRef(sessionId), LogPrivacyGuard.SafeExceptionType(ex));
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
            _logger.LogError("[Redis] Wiki-ready sinyali yazilirken hata olustu. TopicRef={TopicRef} ErrorType={ErrorType}",
                TopicRef(topicId), LogPrivacyGuard.SafeExceptionType(ex));
        }
    }

    // ── PII Scrubbing — Redis'e yazılan kullanıcı/ajan metinlerinde PII temizliği ──

    private static string ScrubPii(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var s = text.Trim();
        // Email
        s = Regex.Replace(s, @"[\w.+-]+@[\w-]+\.[\w.]+", "[redacted_email]", RegexOptions.None, TimeSpan.FromMilliseconds(500));
        // Phone
        s = Regex.Replace(s, @"\+?\d[\d\s\-()]{7,14}\d", "[redacted_phone]", RegexOptions.None, TimeSpan.FromMilliseconds(500));
        // Windows paths
        s = Regex.Replace(s, @"[A-Za-z]:\\[^\s,;]+", "[redacted_path]", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));
        // Unix paths
        s = Regex.Replace(s, @"/(?:home|users|var|tmp|workspace|app)/[^\s,;]+", "[redacted_path]", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));
        // Credentials
        s = Regex.Replace(s, @"(?i)(api[_-]?key|secret|token|password)\s*[:=]\s*['""]?[^'""\s,;]+", "[redacted_credential]", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));
        return s.Length <= maxLength ? s : s[..maxLength];
    }

    // ── Faz 12: Dynamic Few-Shot — Altın Örnek Kütüphanesi ──────────────────

    public async Task SaveGoldExampleAsync(Guid userId, Guid topicId, string userMessage, string agentResponse, int score)
    {
        try
        {
            var key     = $"orka:gold:{userId}:{topicId}";
            var scrubbedUser = ScrubPii(userMessage, 100000);
            var truncatedUser = scrubbedUser.Length > 300 ? scrubbedUser[..300] + "..." : scrubbedUser;
            var scrubbedAgent = ScrubPii(agentResponse, 100000);
            var truncatedAgent = scrubbedAgent.Length > 800 ? scrubbedAgent[..800] + "..." : scrubbedAgent;

            var payload = JsonSerializer.Serialize(new GoldExample(
                UserMessage:   truncatedUser,
                AgentResponse: truncatedAgent,
                Score:         score,
                CreatedAt:     DateTime.UtcNow.ToString("O")
            ));

            await _db.ListLeftPushAsync(key, payload);
            await _db.ListTrimAsync(key, 0, 9);                          // Max 10 örnek
            await _db.KeyExpireAsync(key, TimeSpan.FromDays(30));

            _logger.LogInformation("[Redis] Altin ornek kaydedildi. TopicRef={TopicRef} Puan={Score}", TopicRef(topicId), score);
        }
        catch (Exception ex)
        {
            _logger.LogError("[Redis] Altin ornek kaydedilirken hata olustu. TopicRef={TopicRef} ErrorType={ErrorType}",
                TopicRef(topicId), LogPrivacyGuard.SafeExceptionType(ex));
        }
    }

    public async Task<IEnumerable<GoldExample>> GetGoldExamplesAsync(Guid userId, Guid topicId, int count = 2)
    {
        try
        {
            var key   = $"orka:gold:{userId}:{topicId}";
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
            _logger.LogError("[Redis] Altin ornekler okunurken hata olustu. TopicRef={TopicRef} ErrorType={ErrorType}",
                TopicRef(topicId), LogPrivacyGuard.SafeExceptionType(ex));
            return Enumerable.Empty<GoldExample>();
        }
    }

    // ── HUD: Gerçek Zamanlı Ajan Telemetrisi ────────────────────────────────

    private static readonly string[] AllAgentRoles = Enum.GetNames<AgentRole>();
    private static readonly string[] DefaultCacheMetricAreas =
    [
        "learning-summary",
        "learning-recommendations",
        "notebook-briefing",
        "notebook-glossary",
        "notebook-timeline",
        "notebook-mindmap",
        "notebook-study-cards"
    ];

    // Weakness decay süresi — öğrenci zayıf noktalarının 30 gün sonra otomatik temizlenmesi
    // Üst-üste kayıt olan tüm profiles bu TTL ile expire eder.
    private static readonly TimeSpan WeaknessDecayPeriod = TimeSpan.FromDays(30);

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
            _logger.LogError(
                "[Redis] Ajan metrigi kaydedilirken hata olustu. Role={Role} ErrorType={ErrorType}",
                agentRole,
                LogPrivacyGuard.SafeExceptionType(ex));
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
                _logger.LogError(
                    "[Redis] Sistem metrikleri okunurken hata olustu. Role={Role} ErrorType={ErrorType}",
                    role,
                    LogPrivacyGuard.SafeExceptionType(ex));
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
            _logger.LogError(
                "[Redis] Provider kullanim dagilimi okunurken hata olustu. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
            return Enumerable.Empty<ProviderUsageStat>();
        }
    }

    public async Task<string?> GetJsonAsync(string key)
    {
        try
        {
            var val = await _db.StringGetAsync(key);
            return val.HasValue ? val.ToString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Redis] Cache okunamadi. KeyRef={KeyRef} ErrorType={ErrorType}",
                KeyRef(key), LogPrivacyGuard.SafeExceptionType(ex));
            return null;
        }
    }

    public async Task SetJsonAsync(string key, string payload, TimeSpan ttl)
    {
        try
        {
            await _db.StringSetAsync(key, payload, ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Redis] Cache yazilamadi. KeyRef={KeyRef} ErrorType={ErrorType}",
                KeyRef(key), LogPrivacyGuard.SafeExceptionType(ex));
        }
    }

    public async Task AddStreamEventAsync(string key, IReadOnlyDictionary<string, string> values, TimeSpan? ttl = null)
    {
        if (StreamsAreUnsupported())
        {
            return;
        }

        try
        {
            var fields = values.Count == 0
                ? new[] { new NameValueEntry("event", "empty") }
                : values.Select(kv => new NameValueEntry(kv.Key, kv.Value ?? string.Empty)).ToArray();

            await _db.StreamAddAsync(
                key,
                fields,
                maxLength: 500,
                useApproximateMaxLength: true);

            if (ttl.HasValue)
            {
                await _db.KeyExpireAsync(key, ttl.Value);
            }
        }
        catch (RedisServerException ex) when (IsUnsupportedStreamCommand(ex))
        {
            MarkStreamsUnsupported(key, ex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Redis] Stream event yazilamadi. KeyRef={KeyRef} ErrorType={ErrorType}",
                KeyRef(key), LogPrivacyGuard.SafeExceptionType(ex));
        }
    }

    public async Task<IReadOnlyList<RedisStreamEventDto>> ReadStreamEventsAsync(string key, string afterId = "0-0", int count = 50)
    {
        if (StreamsAreUnsupported())
        {
            return [];
        }

        try
        {
            var start = string.IsNullOrWhiteSpace(afterId) ? "0-0" : afterId;
            var entries = await _db.StreamReadAsync(key, start, Math.Clamp(count, 1, 200));
            return entries
                .Where(e => !string.Equals(e.Id, start, StringComparison.Ordinal))
                .Select(ToStreamDto)
                .ToList();
        }
        catch (RedisServerException ex) when (IsUnsupportedStreamCommand(ex))
        {
            MarkStreamsUnsupported(key, ex);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Redis] Stream okunamadi. KeyRef={KeyRef} ErrorType={ErrorType}",
                KeyRef(key), LogPrivacyGuard.SafeExceptionType(ex));
            return [];
        }
    }

    public async Task<bool> EnsureConsumerGroupAsync(string key, string group, string startId = "0-0")
    {
        if (StreamsAreUnsupported())
        {
            return false;
        }

        try
        {
            await _db.StreamCreateConsumerGroupAsync(key, group, startId, createStream: true);
            return true;
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        catch (RedisServerException ex) when (IsUnsupportedStreamCommand(ex))
        {
            MarkStreamsUnsupported(key, ex);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Redis] Consumer group olusturulamadi. KeyRef={KeyRef} Group={Group} ErrorType={ErrorType}",
                KeyRef(key), LogPrivacyGuard.SafeMessage(group, 80), LogPrivacyGuard.SafeExceptionType(ex));
            return false;
        }
    }

    public async Task<IReadOnlyList<RedisStreamEventDto>> ReadConsumerGroupAsync(string key, string group, string consumer, int count = 50, string streamId = ">")
    {
        if (StreamsAreUnsupported())
        {
            return [];
        }

        try
        {
            var entries = await _db.StreamReadGroupAsync(key, group, consumer, streamId, Math.Clamp(count, 1, 200));
            return entries.Select(ToStreamDto).ToList();
        }
        catch (RedisServerException ex) when (IsUnsupportedStreamCommand(ex))
        {
            MarkStreamsUnsupported(key, ex);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Redis] Consumer group stream okunamadi. KeyRef={KeyRef} Group={Group} ErrorType={ErrorType}",
                KeyRef(key), LogPrivacyGuard.SafeMessage(group, 80), LogPrivacyGuard.SafeExceptionType(ex));
            return [];
        }
    }

    public async Task AckStreamEventsAsync(string key, string group, IEnumerable<string> eventIds)
    {
        if (StreamsAreUnsupported())
        {
            return;
        }

        var ids = eventIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => (RedisValue)id)
            .ToArray();
        if (ids.Length == 0) return;

        try
        {
            await _db.StreamAcknowledgeAsync(key, group, ids);
        }
        catch (RedisServerException ex) when (IsUnsupportedStreamCommand(ex))
        {
            MarkStreamsUnsupported(key, ex);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Redis] Stream ack basarisiz. KeyRef={KeyRef} Group={Group} ErrorType={ErrorType}",
                KeyRef(key), LogPrivacyGuard.SafeMessage(group, 80), LogPrivacyGuard.SafeExceptionType(ex));
        }
    }

    public async Task<long> TrimStreamAsync(string key, long maxLength, bool approximate = true)
    {
        if (StreamsAreUnsupported())
        {
            return 0;
        }

        try
        {
            return await _db.StreamTrimAsync(key, Math.Max(1, maxLength), useApproximateMaxLength: approximate);
        }
        catch (RedisServerException ex) when (IsUnsupportedStreamCommand(ex))
        {
            MarkStreamsUnsupported(key, ex);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Redis] Stream trim basarisiz. KeyRef={KeyRef} ErrorType={ErrorType}",
                KeyRef(key), LogPrivacyGuard.SafeExceptionType(ex));
            return 0;
        }
    }

    public async Task<IReadOnlyList<string>> ScanKeysAsync(string pattern, int take = 100)
    {
        try
        {
            var keyList = new List<string>();
            var endpoints = _redis.GetEndPoints();
            var boundedTake = Math.Clamp(take, 1, 1000);

            foreach (var endpoint in endpoints)
            {
                if (keyList.Count >= boundedTake)
                    break;

                var server = _redis.GetServer(endpoint);
                if (!server.IsConnected || server.IsReplica)
                    continue;

                await foreach (var key in server.KeysAsync(database: _db.Database, pattern: pattern, pageSize: Math.Clamp(boundedTake, 10, 1000)))
                {
                    if (keyList.Count >= boundedTake)
                        break;

                    var keyStr = key.ToString();
                    if (!string.IsNullOrWhiteSpace(keyStr))
                    {
                        keyList.Add(keyStr);
                    }
                }
            }

            return keyList;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Redis] Key scan basarisiz. PatternRef={PatternRef} ErrorType={ErrorType}",
                KeyRef(pattern), LogPrivacyGuard.SafeExceptionType(ex));
            return Array.Empty<string>();
        }
    }

    public async Task<bool> SupportsVectorSearchAsync()
    {
        try
        {
            await _db.ExecuteAsync("FT._LIST");
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task DeleteKeyAsync(string key)
    {
        try
        {
            await _db.ExecuteAsync("UNLINK", key);
        }
        catch (RedisServerException ex) when (IsUnknownCommand(ex, "UNLINK"))
        {
            try
            {
                await _db.KeyDeleteAsync(key);
            }
            catch (Exception fallbackEx)
            {
                _logger.LogWarning("[Redis] Cache silinemedi. KeyRef={KeyRef} ErrorType={ErrorType}",
                    KeyRef(key), LogPrivacyGuard.SafeExceptionType(fallbackEx));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Redis] Cache silinemedi. KeyRef={KeyRef} ErrorType={ErrorType}",
                KeyRef(key), LogPrivacyGuard.SafeExceptionType(ex));
        }
    }

    public async Task<long> GetTopicVersionAsync(Guid topicId)
    {
        try
        {
            var key = NotebookVersionKey(topicId);
            var val = await _db.StringGetAsync(key);
            return val.HasValue && long.TryParse(val.ToString(), out var version) ? version : 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Redis] Notebook version okunamadi. TopicRef={TopicRef} ErrorType={ErrorType}",
                TopicRef(topicId), LogPrivacyGuard.SafeExceptionType(ex));
            return 0;
        }
    }

    public async Task<long> BumpTopicVersionAsync(Guid topicId, string reason)
    {
        try
        {
            var key = NotebookVersionKey(topicId);
            var version = await _db.StringIncrementAsync(key);
            await _db.KeyExpireAsync(key, TimeSpan.FromDays(30));
            await RecordCacheMetricAsync("notebook-invalidation", hit: false, tool: reason);
            _logger.LogInformation("[Redis] Notebook cache version guncellendi. TopicRef={TopicRef} Version={Version} Reason={Reason}",
                TopicRef(topicId), version, LogPrivacyGuard.SafeMessage(reason, 80));
            return version;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Redis] Notebook version artirilamadi. TopicRef={TopicRef} Reason={Reason} ErrorType={ErrorType}",
                TopicRef(topicId), LogPrivacyGuard.SafeMessage(reason, 80), LogPrivacyGuard.SafeExceptionType(ex));
            return 0;
        }
    }

    public async Task InvalidateLearningCachesAsync(Guid userId, Guid topicId, string reason)
    {
        try
        {
            await _db.KeyDeleteAsync(new RedisKey[]
            {
                LearningSummaryKey(userId, topicId),
                LearningRecommendationsKey(userId, topicId)
            });
            await RecordCacheMetricAsync("learning-invalidation", hit: false, tool: reason);
            _logger.LogInformation("[Redis] Learning cache temizlendi. UserRef={UserRef} TopicRef={TopicRef} Reason={Reason}",
                UserRef(userId), TopicRef(topicId), LogPrivacyGuard.SafeMessage(reason, 80));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Redis] Learning cache temizlenemedi. UserRef={UserRef} TopicRef={TopicRef} ErrorType={ErrorType}",
                UserRef(userId), TopicRef(topicId), LogPrivacyGuard.SafeExceptionType(ex));
        }
    }

    public async Task PurgeUserCachesAsync(Guid userId, IEnumerable<Guid> topicIds, string reason, int maxKeysPerPattern = 100)
    {
        var boundedTake = Math.Clamp(maxKeysPerPattern, 1, 1000);
        var scopedTopicIds = topicIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        var patterns = new List<string>
        {
            $"orka:*:{userId:D}*",
            $"orka:*:{userId:N}*"
        };

        foreach (var topicId in scopedTopicIds)
        {
            patterns.Add($"orka:*:{topicId:D}*");
            patterns.Add($"orka:*:{topicId:N}*");
        }

        var deleted = 0;
        foreach (var pattern in patterns.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var keys = await ScanKeysAsync(pattern, boundedTake);
            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                await DeleteKeyAsync(key);
                deleted++;
            }
        }

        await RecordCacheMetricAsync("broad-purge", hit: false, tool: reason);
        _logger.LogInformation(
            "[Redis] User/topic cache purge tamamlandi. UserRef={UserRef} TopicCount={TopicCount} Deleted={Deleted} Reason={Reason}",
            UserRef(userId),
            scopedTopicIds.Length,
            deleted,
            LogPrivacyGuard.SafeMessage(reason, 80));
    }

    public async Task RecordCacheMetricAsync(string area, bool hit, string? tool = null, double? latencyMs = null)
    {
        try
        {
            var normalizedArea = NormalizeMetricPart(area);
            var normalizedTool = NormalizeMetricPart(tool ?? area);
            var record = new CacheMetricRecord(
                normalizedArea,
                normalizedTool,
                hit,
                latencyMs,
                DateTime.UtcNow);
            var payload = JsonSerializer.Serialize(record);
            var key = CacheMetricKey(normalizedArea);

            await _db.ListLeftPushAsync(key, payload);
            await _db.ListTrimAsync(key, 0, 199);
            await _db.KeyExpireAsync(key, TimeSpan.FromHours(24));
            await _db.SetAddAsync("orka:cache_metrics:areas", normalizedArea);
            await _db.KeyExpireAsync("orka:cache_metrics:areas", TimeSpan.FromDays(7));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                "[Redis] Cache metrigi yazilamadi. Area={Area} ErrorType={ErrorType}",
                area,
                LogPrivacyGuard.SafeExceptionType(ex));
        }
    }

    public async Task<IEnumerable<CacheMetricSummary>> GetCacheMetricsAsync()
    {
        try
        {
            var areas = (await _db.SetMembersAsync("orka:cache_metrics:areas"))
                .Select(x => x.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Concat(DefaultCacheMetricAreas)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var summaries = new List<CacheMetricSummary>();
            foreach (var area in areas)
            {
                var items = await _db.ListRangeAsync(CacheMetricKey(area), 0, 199);
                var records = items
                    .Select(x =>
                    {
                        try { return JsonSerializer.Deserialize<CacheMetricRecord>(x.ToString()); }
                        catch { return null; }
                    })
                    .Where(x => x != null)
                    .Select(x => x!)
                    .ToList();

                if (records.Count == 0)
                {
                    summaries.Add(new CacheMetricSummary(area, area, 0, 0, 0, 0, "—"));
                    continue;
                }

                summaries.AddRange(records
                    .GroupBy(r => new { r.Area, r.Tool })
                    .Select(g =>
                    {
                        var hits = g.Count(x => x.Hit);
                        var misses = g.Count() - hits;
                        var withLatency = g.Where(x => x.LatencyMs.HasValue).Select(x => x.LatencyMs!.Value).ToList();
                        return new CacheMetricSummary(
                            g.Key.Area,
                            g.Key.Tool,
                            hits,
                            misses,
                            Math.Round(hits * 100.0 / g.Count(), 1),
                            withLatency.Count == 0 ? 0 : Math.Round(withLatency.Average(), 0),
                            g.Max(x => x.RecordedAt).ToString("O"));
                    }));
            }

            return summaries
                .OrderBy(s => s.Area)
                .ThenBy(s => s.Tool)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[Redis] Cache metrikleri okunamadi. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
            return Enumerable.Empty<CacheMetricSummary>();
        }
    }

    public async Task<RedisHealthDto> GetRedisHealthAsync()
    {
        var checkedAt = DateTime.UtcNow;
        try
        {
            var ping = await _db.PingAsync();
            var connected = _redis.IsConnected;
            var endpointCount = _redis.GetEndPoints().Length;
            return new RedisHealthDto(
                connected,
                Math.Round(ping.TotalMilliseconds, 2),
                endpointCount,
                connected ? "online" : "degraded",
                null,
                checkedAt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[Redis] Health check basarisiz. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
            return new RedisHealthDto(false, 0, 0, "offline", ex.Message, checkedAt);
        }
    }

    public async Task<IReadOnlyList<string>> GetRecentQuestionHashesAsync(Guid userId, Guid topicId, int count = 80)
    {
        try
        {
            var key = QuizHashKey(userId, topicId);
            var items = await _db.ListRangeAsync(key, 0, Math.Max(0, count - 1));
            await RecordCacheMetricAsync("quiz-anti-repeat", hit: items.Length > 0, tool: "read");
            return items
                .Select(x => x.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(count)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Redis] Quiz hash hafizasi okunamadi. UserRef={UserRef} TopicRef={TopicRef} ErrorType={ErrorType}",
                UserRef(userId), TopicRef(topicId), LogPrivacyGuard.SafeExceptionType(ex));
            return [];
        }
    }

    public async Task RememberQuestionHashesAsync(Guid userId, Guid topicId, IEnumerable<string> hashes)
    {
        var clean = hashes
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(h => (RedisValue)h)
            .ToArray();

        if (clean.Length == 0) return;

        try
        {
            var key = QuizHashKey(userId, topicId);
            await _db.ListLeftPushAsync(key, clean);
            await _db.ListTrimAsync(key, 0, 199);
            await _db.KeyExpireAsync(key, TimeSpan.FromDays(30));
            await RecordCacheMetricAsync("quiz-anti-repeat", hit: false, tool: "remember");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[Redis] Quiz hash hafizasi yazilamadi. UserRef={UserRef} TopicRef={TopicRef} ErrorType={ErrorType}",
                UserRef(userId), TopicRef(topicId), LogPrivacyGuard.SafeExceptionType(ex));
        }
    }

    public async Task<IEnumerable<EvaluatorLogEntry>> GetRecentEvaluatorLogsAsync(int count = 20)
    {
        try
        {
            // Tüm feedback key'lerini tarar — "orka:feedback:*"
            var keys = (await ScanKeysAsync("orka:feedback:*", 50)).ToArray();

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
            _logger.LogError(
                "[Redis] Evaluator loglari okunurken hata olustu. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
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
            _logger.LogError("[Redis] Topic score kaydedilirken hata. TopicRef={TopicRef} ErrorType={ErrorType}",
                TopicRef(topicId), LogPrivacyGuard.SafeExceptionType(ex));
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
            _logger.LogError("[Redis] Topic score okunurken hata. TopicRef={TopicRef} ErrorType={ErrorType}",
                TopicRef(topicId), LogPrivacyGuard.SafeExceptionType(ex));
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
            var record = new StudentProfileRecord(understandingScore, ScrubPii(weaknesses ?? string.Empty, 500), DateTime.UtcNow);

            // Konu bazında sadece en güncel profili tek record olarak tutmak isteyebiliriz.
            // Ama zaman içerisindeki değişimi anlamak için liste de tutulabilir.
            // Şimdilik sadece en güncel veriyi String (SET) olarak ya da ufak bir liste olarak tutalım.
            // Tek kaynakta güncel durum kalsın diye SET/GET yapalım (üzerine yazsın, "yaşayan organizasyon"un "şu anki" hali)

            var payload = JsonSerializer.Serialize(record);
            await _db.StringSetAsync(key, payload, WeaknessDecayPeriod);

            _logger.LogInformation("[Redis] Ogrenci profili guncellendi. TopicRef={TopicRef} Puan={Score} TTL={Days}gun",
                TopicRef(topicId), understandingScore, WeaknessDecayPeriod.TotalDays);
        }
        catch (Exception ex)
        {
            _logger.LogError("[Redis] Ogrenci profili kaydedilirken hata. TopicRef={TopicRef} ErrorType={ErrorType}",
                TopicRef(topicId), LogPrivacyGuard.SafeExceptionType(ex));
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
            _logger.LogError("[Redis] Ogrenci profili okunurken hata. TopicRef={TopicRef} ErrorType={ErrorType}",
                TopicRef(topicId), LogPrivacyGuard.SafeExceptionType(ex));
            return null;
        }
    }

    // ── Faz 16: EvaluatorAgent → TutorAgent Anlık Müdahale ───────────────────

    private record LowQualityFeedbackRecord(int Score, string Feedback, DateTime At);

    public async Task SetLowQualityFeedbackAsync(Guid sessionId, int score, string feedback)
    {
        try
        {
            var key = $"orka:lowquality:{sessionId}";
            var record = new LowQualityFeedbackRecord(score, feedback ?? string.Empty, DateTime.UtcNow);
            var payload = JsonSerializer.Serialize(record);

            // 5 dakika TTL — bir sonraki yanıtta tüketilmezse expire olur, eski uyarılar yığılmaz.
            await _db.StringSetAsync(key, payload, TimeSpan.FromMinutes(5));

            _logger.LogInformation("[Redis] Dusuk kalite uyarisi set edildi. SessionRef={SessionRef} Score={Score}", SessionRef(sessionId), score);
        }
        catch (Exception ex)
        {
            _logger.LogError("[Redis] Dusuk kalite uyarisi yazilirken hata. SessionRef={SessionRef} ErrorType={ErrorType}",
                SessionRef(sessionId), LogPrivacyGuard.SafeExceptionType(ex));
        }
    }

    public async Task<(int score, string feedback)?> GetAndClearLowQualityFeedbackAsync(Guid sessionId)
    {
        try
        {
            var key = $"orka:lowquality:{sessionId}";
            // StringGetDelete: atomik tek-kullanımlık okuma + silme.
            RedisValue val;
            try
            {
                // Prefer atomic one-shot read where the Redis server supports GETDEL.
                val = await _db.StringGetDeleteAsync(key);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("unknown command", StringComparison.OrdinalIgnoreCase))
            {
                // Older Redis-compatible local servers may not support GETDEL. Keep Tutor personalization
                // working with a best-effort read + delete fallback instead of logging an error every answer.
                val = await _db.StringGetAsync(key);
                if (!val.IsNullOrEmpty)
                    await _db.KeyDeleteAsync(key);
            }

            if (val.IsNullOrEmpty) return null;

            var record = JsonSerializer.Deserialize<LowQualityFeedbackRecord>(val.ToString());
            if (record == null) return null;

            return (record.Score, record.Feedback);
        }
        catch (Exception ex)
        {
            _logger.LogError("[Redis] Dusuk kalite uyarisi okunurken hata. SessionRef={SessionRef} ErrorType={ErrorType}",
                SessionRef(sessionId), LogPrivacyGuard.SafeExceptionType(ex));
            return null;
        }
    }

    // ── Faz 16: Korteks → Quiz/Tutor Köprüsü ─────────────────────────────────

    public async Task SaveKorteksResearchReportAsync(Guid topicId, string report)
    {
        try
        {
            var key = $"orka:korteks:{topicId}";
            // Çok büyük raporları kırp — quiz/tutor 2-3K karakter yeter.
            var trimmed = string.IsNullOrEmpty(report)
                ? string.Empty
                : (report.Length > 4000 ? report[..4000] + "..." : report);

            await _db.StringSetAsync(key, trimmed, TimeSpan.FromHours(2));
            await BumpTopicVersionAsync(topicId, "korteks-report");
            _logger.LogInformation("[Redis] Korteks raporu kaydedildi. TopicRef={TopicRef} Length={Length}", TopicRef(topicId), trimmed.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError("[Redis] Korteks raporu yazilirken hata. TopicRef={TopicRef} ErrorType={ErrorType}",
                TopicRef(topicId), LogPrivacyGuard.SafeExceptionType(ex));
        }
    }

    public async Task<string?> GetKorteksResearchReportAsync(Guid topicId)
    {
        try
        {
            var key = $"orka:korteks:{topicId}";
            var val = await _db.StringGetAsync(key);
            return val.HasValue ? val.ToString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogError("[Redis] Korteks raporu okunurken hata. TopicRef={TopicRef} ErrorType={ErrorType}",
                TopicRef(topicId), LogPrivacyGuard.SafeExceptionType(ex));
            return null;
        }
    }

    // ── YouTube RAG: Cache-First Strateji ─────────────────────────────────────

    public async Task SaveYouTubeContextAsync(Guid topicId, string payload)
    {
        try
        {
            var key = $"orka:youtube:{topicId}";
            // 24 saat TTL — YouTube içeriği sık değişmez, kota tasarrufu kritik
            await _db.StringSetAsync(key, payload, TimeSpan.FromHours(24));
            _logger.LogInformation("[Redis] YouTube context cache'lendi. TopicRef={TopicRef} Length={Length}", TopicRef(topicId), payload.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError("[Redis] YouTube context yazilirken hata. TopicRef={TopicRef} ErrorType={ErrorType}",
                TopicRef(topicId), LogPrivacyGuard.SafeExceptionType(ex));
        }
    }

    public async Task<string?> GetYouTubeContextAsync(Guid topicId)
    {
        try
        {
            var key = $"orka:youtube:{topicId}";
            var val = await _db.StringGetAsync(key);
            if (val.HasValue)
            {
                await RecordCacheMetricAsync("youtube-context", hit: true);
                return val.ToString();
            }
            await RecordCacheMetricAsync("youtube-context", hit: false);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError("[Redis] YouTube context okunurken hata. TopicRef={TopicRef} ErrorType={ErrorType}",
                TopicRef(topicId), LogPrivacyGuard.SafeExceptionType(ex));
            return null;
        }
    }

    private static string UserRef(Guid userId) => LogPrivacyGuard.SafeId(userId, "usr");

    private static string TopicRef(Guid topicId) => LogPrivacyGuard.SafeId(topicId, "topic");

    private static string SessionRef(Guid sessionId) => LogPrivacyGuard.SafeId(sessionId, "session");

    private static string KeyRef(string? key) => LogPrivacyGuard.SafeTextRef(key, "redis");

    public static string LearningSummaryKey(Guid userId, Guid topicId) =>
        $"orka:v1:learning:summary:{userId}:{topicId}";

    public static string LearningRecommendationsKey(Guid userId, Guid topicId) =>
        $"orka:v1:learning:recommendations:{userId}:{topicId}";

    public static string NotebookToolKey(string tool, Guid userId, Guid topicId, long version) =>
        $"orka:v1:notebook:{NormalizeMetricPart(tool)}:{userId}:{topicId}:v{version}";

    private static string QuizHashKey(Guid userId, Guid topicId) =>
        $"orka:v1:quiz:hashes:{userId}:{topicId}";

    public static string ComputeQuestionHash(string question, string? skillTag, string? topicPath, string? topic, string? difficulty)
    {
        var tag = !string.IsNullOrWhiteSpace(skillTag) ? skillTag :
                  !string.IsNullOrWhiteSpace(topicPath) ? topicPath :
                  !string.IsNullOrWhiteSpace(topic) ? topic : "unknown";
        var diff = !string.IsNullOrWhiteSpace(difficulty) ? difficulty : "orta";
        var raw = $"{question}|{tag}|{diff}".ToLowerInvariant();
        raw = Regex.Replace(raw, @"\s+", " ");
        if (raw.Length > 180)
        {
            raw = raw[..180];
        }
        return raw;
    }

    private static string NotebookVersionKey(Guid topicId) =>
        $"orka:v1:notebook:version:{topicId}";

    private static string CacheMetricKey(string area) =>
        $"orka:v1:metrics:cache:{NormalizeMetricPart(area)}";

    private static string NormalizeMetricPart(string value)
    {
        var clean = new string((value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '-')
            .ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "unknown" : clean;
    }

    private static RedisStreamEventDto ToStreamDto(StreamEntry entry) => new()
    {
        Id = entry.Id.ToString(),
        Values = entry.Values.ToDictionary(
            x => x.Name.ToString(),
            x => x.Value.ToString(),
            StringComparer.OrdinalIgnoreCase)
    };

    private bool StreamsAreUnsupported() =>
        Volatile.Read(ref _streamsUnsupported) == 1;

    private void MarkStreamsUnsupported(string key, RedisServerException ex)
    {
        if (Interlocked.Exchange(ref _streamsUnsupported, 1) == 0)
        {
            _logger.LogInformation(
                "[Redis] Redis Streams desteklenmiyor; stream telemetry devre disi birakildi. KeyRef={KeyRef} ErrorType={ErrorType}",
                KeyRef(key),
                LogPrivacyGuard.SafeExceptionType(ex));
        }
    }

    private static bool IsUnsupportedStreamCommand(RedisServerException ex) =>
        IsUnknownCommand(ex, "XADD") ||
        IsUnknownCommand(ex, "XREAD") ||
        IsUnknownCommand(ex, "XGROUP") ||
        IsUnknownCommand(ex, "XREADGROUP") ||
        IsUnknownCommand(ex, "XACK") ||
        IsUnknownCommand(ex, "XTRIM");

    private static bool IsUnknownCommand(RedisServerException ex, string command) =>
        ex.Message.Contains("unknown command", StringComparison.OrdinalIgnoreCase) &&
        ex.Message.Contains(command, StringComparison.OrdinalIgnoreCase);

    public async Task<bool> AcquireLockAsync(string key, string value, TimeSpan expiry)
    {
        try
        {
            return await _db.StringSetAsync(key, value, expiry, When.NotExists);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Redis] Failed to acquire lock for key {LockKey}.", key);
            return false;
        }
    }

    public async Task<bool> RenewLockAsync(string key, string value, TimeSpan expiry)
    {
        try
        {
            var script = @"
                if redis.call('get', KEYS[1]) == ARGV[1] then
                    return redis.call('pexpire', KEYS[1], ARGV[2])
                else
                    return 0
                end";
            var milliseconds = Math.Max(1, (long)expiry.TotalMilliseconds);
            var result = await _db.ScriptEvaluateAsync(
                script,
                new RedisKey[] { key },
                new RedisValue[] { value, milliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            return (long)result == 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Redis] Failed to renew lock for key {LockKey}.", key);
            return false;
        }
    }

    public async Task ReleaseLockAsync(string key, string value)
    {
        try
        {
            var script = @"
                if redis.call('get', KEYS[1]) == ARGV[1] then
                    return redis.call('del', KEYS[1])
                else
                    return 0
                end";
            await _db.ScriptEvaluateAsync(script, [new RedisKey(key)], [(RedisValue)value]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Redis] Failed to release lock for key {LockKey}.", key);
        }
    }
}
