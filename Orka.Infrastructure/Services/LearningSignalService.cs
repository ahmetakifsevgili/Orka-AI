using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public class LearningSignalService : ILearningSignalService
{
    private static readonly TimeSpan LearningCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RedisInvalidationTimeout = TimeSpan.FromMilliseconds(750);

    private readonly OrkaDbContext _db;
    private readonly IRedisMemoryService _redis;
    private readonly ILogger<LearningSignalService> _logger;

    public LearningSignalService(
        OrkaDbContext db,
        IRedisMemoryService redis,
        ILogger<LearningSignalService> logger)
    {
        _db = db;
        _redis = redis;
        _logger = logger;
    }

    public async Task RecordQuizAnsweredAsync(QuizAttempt attempt, CancellationToken ct = default)
    {
        var skill = NormalizeSkill(attempt.SkillTag, attempt.TopicPath);
        var payload = JsonSerializer.Serialize(new
        {
            attempt.QuestionId,
            attempt.Question,
            attempt.UserAnswer,
            attempt.Difficulty,
            attempt.CognitiveType,
            attempt.QuestionHash,
            attempt.SourceRefsJson
        });

        _db.LearningSignals.Add(new LearningSignal
        {
            Id = Guid.NewGuid(),
            UserId = attempt.UserId,
            TopicId = attempt.TopicId,
            SessionId = attempt.SessionId,
            QuizAttemptId = attempt.Id,
            SignalType = LearningSignalTypes.QuizAnswered,
            SkillTag = skill,
            TopicPath = attempt.TopicPath,
            Score = attempt.IsCorrect ? 100 : 0,
            IsPositive = attempt.IsCorrect,
            PayloadJson = payload,
            CreatedAt = DateTime.UtcNow
        });

        if (!attempt.IsCorrect)
        {
            _db.LearningSignals.Add(new LearningSignal
            {
                Id = Guid.NewGuid(),
                UserId = attempt.UserId,
                TopicId = attempt.TopicId,
                SessionId = attempt.SessionId,
                QuizAttemptId = attempt.Id,
                SignalType = LearningSignalTypes.WeaknessDetected,
                SkillTag = skill,
                TopicPath = attempt.TopicPath,
                Score = 0,
                IsPositive = false,
                PayloadJson = payload,
                CreatedAt = DateTime.UtcNow
            });

            if (attempt.TopicId.HasValue)
            {
                await EnsureRecommendationAsync(
                    attempt.UserId,
                    attempt.TopicId.Value,
                    skill,
                    attempt.TopicPath,
                    "Yanlis cevaplanan quiz sorusu",
                    ct);
            }
        }

        if (attempt.QuizRunId.HasValue)
        {
            var run = await _db.QuizRuns.FirstOrDefaultAsync(r => r.Id == attempt.QuizRunId.Value, ct);
            if (run != null)
            {
                run.CorrectCount = await _db.QuizAttempts.CountAsync(a => a.QuizRunId == run.Id && a.IsCorrect, ct);
                var attempts = await _db.QuizAttempts.Where(a => a.QuizRunId == run.Id).ToListAsync(ct);
                run.TotalQuestions = Math.Max(run.TotalQuestions, attempts.Count);
                run.FailedSkillsJson = JsonSerializer.Serialize(
                    attempts
                        .Where(a => !a.IsCorrect)
                        .Select(a => NormalizeSkill(a.SkillTag, a.TopicPath))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList());
                run.CompletedAt ??= attempts.Count >= run.TotalQuestions && run.TotalQuestions > 0
                    ? DateTime.UtcNow
                    : null;
                run.Status = run.CompletedAt.HasValue ? "completed" : "active";
            }
        }

        await _db.SaveChangesAsync(ct);
        await InvalidateLearningStateAsync(attempt.UserId, attempt.TopicId, "quiz-answered", ct);
    }

    public async Task RecordSignalAsync(
        Guid userId,
        Guid? topicId,
        Guid? sessionId,
        string signalType,
        string? skillTag = null,
        string? topicPath = null,
        int? score = null,
        bool? isPositive = null,
        string? payloadJson = null,
        CancellationToken ct = default)
    {
        _db.LearningSignals.Add(new LearningSignal
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            SignalType = LearningSignalTypes.Normalize(signalType),
            SkillTag = NormalizeSkill(skillTag, topicPath),
            TopicPath = topicPath,
            Score = score,
            IsPositive = isPositive,
            PayloadJson = payloadJson,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        await InvalidateLearningStateAsync(userId, topicId, LearningSignalTypes.Normalize(signalType), ct);
    }

    public async Task<LearningTopicSummaryDto> GetTopicSummaryAsync(Guid userId, Guid topicId, CancellationToken ct = default)
    {
        var cacheKey = RedisMemoryService.LearningSummaryKey(userId, topicId);
        var sw = Stopwatch.StartNew();
        var cached = await _redis.GetJsonAsync(cacheKey);
        sw.Stop();

        if (!string.IsNullOrWhiteSpace(cached))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<LearningTopicSummaryDto>(cached);
                if (parsed != null)
                {
                    await _redis.RecordCacheMetricAsync("learning-summary", hit: true, latencyMs: sw.Elapsed.TotalMilliseconds);
                    return parsed with
                    {
                        Cache = new CacheMetaDto(true, "redis", parsed.Cache?.GeneratedAt ?? DateTime.UtcNow, DateTime.UtcNow)
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[LearningSignal] Summary cache parse failed. Topic={TopicId}", topicId);
            }
        }

        await _redis.RecordCacheMetricAsync("learning-summary", hit: false, latencyMs: sw.Elapsed.TotalMilliseconds);
        var summary = await BuildTopicSummaryFromSqlAsync(userId, topicId, ct);
        var cacheable = summary with
        {
            Cache = new CacheMetaDto(false, "sql", DateTime.UtcNow)
        };
        await _redis.SetJsonAsync(cacheKey, JsonSerializer.Serialize(cacheable), LearningCacheTtl);
        return cacheable;
    }

    public async Task<IReadOnlyList<StudyRecommendationDto>> GetRecommendationsAsync(Guid userId, Guid topicId, CancellationToken ct = default)
    {
        var cacheKey = RedisMemoryService.LearningRecommendationsKey(userId, topicId);
        var sw = Stopwatch.StartNew();
        var cached = await _redis.GetJsonAsync(cacheKey);
        sw.Stop();

        if (!string.IsNullOrWhiteSpace(cached))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<StudyRecommendationDto>>(cached);
                if (parsed != null)
                {
                    await _redis.RecordCacheMetricAsync("learning-recommendations", hit: true, latencyMs: sw.Elapsed.TotalMilliseconds);
                    return parsed;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[LearningSignal] Recommendation cache parse failed. Topic={TopicId}", topicId);
            }
        }

        await _redis.RecordCacheMetricAsync("learning-recommendations", hit: false, latencyMs: sw.Elapsed.TotalMilliseconds);
        var summary = await GetTopicSummaryAsync(userId, topicId, ct);
        foreach (var weak in summary.WeakSkills.Take(5))
        {
            await EnsureRecommendationAsync(userId, topicId, weak.SkillTag, weak.TopicPath, $"Dogru orani %{Math.Round(weak.Accuracy * 100)}", ct);
        }

        await _db.SaveChangesAsync(ct);

        var items = await _db.StudyRecommendations
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.TopicId == topicId)
            .OrderBy(r => r.IsDone)
            .ThenByDescending(r => r.CreatedAt)
            .Take(12)
            .Select(r => new StudyRecommendationDto(
                r.Id,
                r.RecommendationType,
                r.Title,
                r.Reason,
                r.SkillTag,
                r.ActionPrompt,
                r.IsDone,
                r.CreatedAt))
            .ToListAsync(ct);

        await _redis.SetJsonAsync(cacheKey, JsonSerializer.Serialize(items), LearningCacheTtl);
        return items;
    }

    private async Task<LearningTopicSummaryDto> BuildTopicSummaryFromSqlAsync(Guid userId, Guid topicId, CancellationToken ct)
    {
        var scopeIds = await GetTopicScopeIdsAsync(userId, topicId, ct);
        var attempts = await _db.QuizAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.TopicId.HasValue && scopeIds.Contains(a.TopicId.Value))
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

        var weakSkills = attempts
            .GroupBy(a => NormalizeSkill(a.SkillTag, a.TopicPath), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var total = g.Count();
                var correct = g.Count(a => a.IsCorrect);
                var wrong = total - correct;
                return new WeakSkillDto(
                    g.Key,
                    g.Select(a => a.TopicPath).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? g.Key,
                    wrong,
                    total,
                    total == 0 ? 0 : Math.Round(correct / (double)total, 2),
                    g.Max(a => a.CreatedAt));
            })
            .Where(x => x.WrongCount > 0 || x.Accuracy < 0.7)
            .OrderBy(x => x.Accuracy)
            .ThenByDescending(x => x.WrongCount)
            .Take(8)
            .ToList();

        var signals = await _db.LearningSignals
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.TopicId.HasValue && scopeIds.Contains(s.TopicId.Value))
            .OrderByDescending(s => s.CreatedAt)
            .Take(8)
            .Select(s => $"{s.SignalType}: {s.SkillTag ?? s.TopicPath ?? "genel"}")
            .ToListAsync(ct);

        var correctAttempts = attempts.Count(a => a.IsCorrect);
        return new LearningTopicSummaryDto(
            topicId,
            attempts.Count,
            correctAttempts,
            attempts.Count == 0 ? 0 : Math.Round(correctAttempts / (double)attempts.Count, 2),
            weakSkills,
            signals);
    }

    private async Task EnsureRecommendationAsync(
        Guid userId,
        Guid topicId,
        string skillTag,
        string? topicPath,
        string reason,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(skillTag)) skillTag = "genel";

        var exists = await _db.StudyRecommendations.AnyAsync(
            r => r.UserId == userId && r.TopicId == topicId && r.SkillTag == skillTag && !r.IsDone,
            ct);

        if (exists) return;

        _db.StudyRecommendations.Add(new StudyRecommendation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            RecommendationType = "remedial-practice",
            Title = $"{skillTag} icin telafi ve pekistirme",
            Reason = $"{reason}. Bu beceriye ozel kisa tekrar, ornek ve mikro quiz onerilir.",
            SkillTag = skillTag,
            ActionPrompt = $"{topicPath ?? skillTag} konusunu once cok basit anlat, sonra 3 uygulama sorusu ile pekistir.",
            CreatedAt = DateTime.UtcNow
        });

        _logger.LogInformation("[LearningSignal] StudyRecommendation created. User={UserId} Topic={TopicId} Skill={Skill}",
            userId, topicId, skillTag);
    }

    private async Task<HashSet<Guid>> GetTopicScopeIdsAsync(Guid userId, Guid topicId, CancellationToken ct)
    {
        var topics = await _db.Topics
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => new { t.Id, t.ParentTopicId })
            .ToListAsync(ct);

        var result = new HashSet<Guid> { topicId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var topic in topics)
            {
                if (topic.ParentTopicId.HasValue && result.Contains(topic.ParentTopicId.Value) && result.Add(topic.Id))
                    changed = true;
            }
        }

        return result;
    }

    private async Task InvalidateLearningStateAsync(Guid userId, Guid? topicId, string reason, CancellationToken ct)
    {
        if (!topicId.HasValue) return;

        var affected = await GetTopicAndAncestorIdsAsync(userId, topicId.Value, ct);
        foreach (var id in affected)
        {
            await RunRedisInvalidationAsync(
                () => _redis.InvalidateLearningCachesAsync(userId, id, reason),
                "learning-cache",
                id,
                reason,
                ct);

            await RunRedisInvalidationAsync(
                async () =>
                {
                    await _redis.BumpTopicVersionAsync(id, reason);
                },
                "notebook-version",
                id,
                reason,
                ct);
        }
    }

    private async Task RunRedisInvalidationAsync(Func<Task> action, string area, Guid topicId, string reason, CancellationToken ct)
    {
        try
        {
            await action().WaitAsync(RedisInvalidationTimeout, ct);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "[LearningSignal] Redis invalidation timeout. Area={Area} Topic={TopicId} Reason={Reason}",
                area,
                topicId,
                reason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[LearningSignal] Redis invalidation skipped. Area={Area} Topic={TopicId} Reason={Reason}",
                area,
                topicId,
                reason);
        }
    }

    private async Task<IReadOnlyList<Guid>> GetTopicAndAncestorIdsAsync(Guid userId, Guid topicId, CancellationToken ct)
    {
        var topics = await _db.Topics
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => new { t.Id, t.ParentTopicId })
            .ToListAsync(ct);

        var byId = topics.ToDictionary(t => t.Id, t => t.ParentTopicId);
        var result = new List<Guid>();
        var current = topicId;
        while (current != Guid.Empty && result.All(id => id != current))
        {
            result.Add(current);
            if (!byId.TryGetValue(current, out var parent) || !parent.HasValue) break;
            current = parent.Value;
        }

        return result;
    }

    private static string NormalizeSkill(string? skillTag, string? topicPath)
    {
        var raw = !string.IsNullOrWhiteSpace(skillTag)
            ? skillTag
            : !string.IsNullOrWhiteSpace(topicPath)
                ? topicPath
                : "unknown skill";

        return raw.Trim().Length > 120 ? raw.Trim()[..120] : raw.Trim();
    }
}
