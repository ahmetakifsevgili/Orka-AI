using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class TutorTraceProjectionService : ITutorTraceProjectionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OrkaDbContext _db;
    private readonly IRedisMemoryService _redis;
    private readonly ILogger<TutorTraceProjectionService> _logger;

    public TutorTraceProjectionService(
        OrkaDbContext db,
        IRedisMemoryService redis,
        ILogger<TutorTraceProjectionService> logger)
    {
        _db = db;
        _redis = redis;
        _logger = logger;
    }

    public async Task<TutorTraceTimelineDto> GetTimelineAsync(
        Guid userId,
        Guid sessionId,
        string afterId = "0-0",
        int take = 50,
        CancellationToken ct = default)
    {
        var session = await _db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session == null) throw new InvalidOperationException("Tutor session not found.");

        var streamKey = $"orka:v3:tutor-events:{sessionId}";
        var streamEvents = await _redis.ReadStreamEventsAsync(streamKey, afterId, Math.Clamp(take, 1, 100));
        var projected = new List<TutorTraceProjection>();
        foreach (var streamEvent in streamEvents)
        {
            var projection = BuildProjection(userId, sessionId, session.TopicId, streamKey, streamEvent);
            var exists = await _db.TutorTraceProjections
                .AsNoTracking()
                .AnyAsync(p => p.SessionId == sessionId && p.StreamId == projection.StreamId, ct);
            if (!exists)
            {
                _db.TutorTraceProjections.Add(projection);
                projected.Add(projection);
            }
        }

        if (projected.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        if (streamEvents.Count > 0)
        {
            return new TutorTraceTimelineDto
            {
                SessionId = sessionId,
                After = afterId,
                LastEventId = streamEvents.Last().Id,
                Source = "redis",
                TraceHealth = "live",
                Events = streamEvents.Select(e => ToDto(BuildProjection(userId, sessionId, session.TopicId, streamKey, e), e.Values)).ToArray()
            };
        }

        var sqlEvents = await _db.TutorTraceProjections
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.SessionId == sessionId)
            .OrderByDescending(p => p.OccurredAt)
            .Take(Math.Clamp(take, 1, 100))
            .OrderBy(p => p.OccurredAt)
            .ToListAsync(ct);

        return new TutorTraceTimelineDto
        {
            SessionId = sessionId,
            After = afterId,
            LastEventId = sqlEvents.LastOrDefault()?.StreamId ?? afterId,
            Source = "sql_projection",
            TraceHealth = sqlEvents.Count > 0 ? "replayed" : "empty",
            Events = sqlEvents.Select(p => ToDto(p, DeserializeValues(p.PayloadJson))).ToArray()
        };
    }

    private TutorTraceProjection BuildProjection(Guid userId, Guid sessionId, Guid? topicId, string streamKey, RedisStreamEventDto streamEvent)
    {
        var eventType = Read(streamEvent.Values, "eventType") ?? Read(streamEvent.Values, "type") ?? "tutor.event";
        var (group, label, detail, severity) = Describe(eventType, streamEvent.Values);
        return new TutorTraceProjection
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SessionId = sessionId,
            TopicId = topicId,
            StreamKey = streamKey,
            StreamId = streamEvent.Id,
            EventType = eventType,
            EventGroup = group,
            UserSafeLabel = label,
            UserSafeDetail = detail,
            Severity = severity,
            PayloadJson = JsonSerializer.Serialize(streamEvent.Values, JsonOptions),
            OccurredAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static (string Group, string Label, string Detail, string Severity) Describe(string eventType, IReadOnlyDictionary<string, string> values)
    {
        var type = eventType.ToLowerInvariant();
        var concept = Read(values, "activeConceptKey") ?? Read(values, "conceptKey");
        var toolId = Read(values, "toolId");
        var artifactType = Read(values, "artifactType");
        var status = Read(values, "status");

        if (type.Contains("turn_state")) return ("state", "Kavram ve öğrenci durumu hazırlandı", Safe($"Aktif kavram: {concept}", "Tutor bu tur için öğrenme durumunu topladı."), "info");
        if (type.Contains("action_plan")) return ("plan", "Öğretim modu seçildi", Safe($"Mod: {Read(values, "teachingMode")}", "Tutor cevaptan önce öğretim yolunu seçti."), "info");
        if (type.Contains("tool.started")) return ("tool", "Araç çalışmaya başladı", Safe(toolId, "Tutor ek kanıt veya bağlam için araç çalıştırıyor."), "info");
        if (type.Contains("tool.finished")) return ("tool", "Araç sonucu hazır", Safe($"{toolId} {status}", "Araç sonucu öğretim bağlamına eklendi."), status == "ready" ? "success" : "warning");
        if (type.Contains("artifact")) return ("artifact", "Öğretim görseli/kartı hazır", Safe(artifactType, "Tutor anlatımı destekleyen bir artifact hazırladı."), "success");
        if (type.Contains("pedagogy")) return ("quality", "Öğretim kalitesi ölçüldü", Safe(status, "Tutor cevabı pedagojik kalite kapısından geçirildi."), status == "degraded" ? "warning" : "info");
        if (type.Contains("reflection")) return ("quality", "Tutor kendini kontrol etti", "Cevap sonrası kaynak, ipucu ve kontrol sorusu davranışı değerlendirildi.", "info");
        if (type.Contains("evidence")) return ("evidence", "Gerçek dünya kanıtı eklendi", Safe(Read(values, "provider"), "Tutor anlatımı kaynaklı bir örnekle güçlendirdi."), "info");
        return ("state", "Tutor olayı kaydedildi", eventType.Replace('.', ' '), "info");
    }

    private static TutorTraceTimelineEventDto ToDto(TutorTraceProjection projection, Dictionary<string, string> values) => new()
    {
        Id = projection.Id,
        StreamId = projection.StreamId,
        EventType = projection.EventType,
        EventGroup = projection.EventGroup,
        UserSafeLabel = projection.UserSafeLabel,
        UserSafeDetail = projection.UserSafeDetail,
        Severity = projection.Severity,
        Values = values,
        OccurredAt = projection.OccurredAt
    };

    private static string? Read(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static string Safe(string? preferred, string fallback) => string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

    private static Dictionary<string, string> DeserializeValues(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
