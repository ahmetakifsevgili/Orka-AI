using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orka.Core.Constants;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using System.Security.Claims;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly OrkaDbContext _dbContext;
    private readonly IRedisMemoryService _redis;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        OrkaDbContext dbContext,
        IRedisMemoryService redis,
        IWebHostEnvironment environment,
        ILogger<DashboardController> logger)
    {
        _dbContext = dbContext;
        _redis = redis;
        _environment = environment;
        _logger = logger;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        var userStats = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.TotalXP, u.CurrentStreak })
            .FirstOrDefaultAsync(cancellationToken);

        var totalXP = userStats?.TotalXP ?? 0;
        var currentStreak = userStats?.CurrentStreak ?? 0;

        var allTopics = await _dbContext.Topics
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.ParentTopicId == null)
            .Select(t => new { t.ProgressPercentage, t.IsArchived })
            .ToListAsync(cancellationToken);

        var completedTopics = allTopics.Count(t => t.ProgressPercentage >= 100);
        var activeLearning = allTopics.Count(t => t.ProgressPercentage > 0 && t.ProgressPercentage < 100);
        var totalTopics = allTopics.Count;

        var sectionData = await _dbContext.Topics
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => new { t.TotalSections, t.CompletedSections })
            .ToListAsync(cancellationToken);

        var totalSections = sectionData.Sum(t => t.TotalSections);
        var completedSections = sectionData.Sum(t => t.CompletedSections);
        var progressPercentage = totalSections > 0
            ? Math.Round((double)completedSections / totalSections * 100, 1)
            : 0.0;

        // 7 günlük aktivite — SQL-side GroupBy.
        // EF Core SQL Server provider, m.CreatedAt.Date'i CONVERT(date, [m].[CreatedAt]) olarak çevirir;
        // tüm satırları çekmek yerine günlük count'lar projeksiyonla döner.
        var weekAgo = DateTime.UtcNow.Date.AddDays(-7);
        var activity = await _dbContext.Messages
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.CreatedAt >= weekAgo)
            .GroupBy(m => m.CreatedAt.Date)
            .Select(g => new { date = g.Key, count = g.Count() })
            .OrderBy(g => g.date)
            .ToListAsync(cancellationToken);

        var wikisCount = await _dbContext.WikiPages
            .AsNoTracking()
            .CountAsync(w => w.UserId == userId, cancellationToken);
        var recentQuizAttempts = await _dbContext.QuizAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(200)
            .Select(a => new
            {
                a.SkillTag,
                a.TopicPath,
                a.IsCorrect,
                a.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var weakSkills = recentQuizAttempts
            .GroupBy(a => string.IsNullOrWhiteSpace(a.SkillTag)
                ? string.IsNullOrWhiteSpace(a.TopicPath) ? "unknown skill" : a.TopicPath!
                : a.SkillTag!)
            .Select(g =>
            {
                var total = g.Count();
                var correct = g.Count(a => a.IsCorrect);
                return new
                {
                    skillTag = g.Key,
                    topicPath = g.Select(a => a.TopicPath).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? g.Key,
                    wrongCount = total - correct,
                    totalCount = total,
                    accuracy = total == 0 ? 0 : Math.Round(correct * 100.0 / total, 1),
                    lastSeenAt = g.Max(a => a.CreatedAt).ToString("O")
                };
            })
            .Where(x => x.wrongCount > 0 || x.accuracy < 70)
            .OrderBy(x => x.accuracy)
            .ThenByDescending(x => x.wrongCount)
            .Take(5)
            .ToList();

        var recentLearningSignals = await _dbContext.LearningSignals
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(6)
            .Select(s => new
            {
                s.SignalType,
                s.SkillTag,
                s.TopicPath,
                s.IsPositive,
                createdAt = s.CreatedAt.ToString("O")
            })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            totalXP,
            currentStreak,
            completedTopics,
            activeLearning,
            totalTopics,
            completedSections,
            totalSections,
            progressPercentage,
            wikisCount,
            activity,
            learningSignalBook = new
            {
                weakSkills,
                recentSignals = recentLearningSignals,
                totalRecentAttempts = recentQuizAttempts.Count,
                summary = weakSkills.Count > 0
                    ? $"{weakSkills[0].skillTag} becerisi öncelikli tekrar istiyor."
                    : "Henüz belirgin zayıf beceri sinyali yok."
            }
        });
    }

    [HttpGet("recent-activity")]
    public async Task<IActionResult> GetRecentActivity(CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        var recentTopics = await _dbContext.Topics
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.LastAccessedAt)
            .Take(5)
            .Select(t => new { t.Id, t.Title, t.Emoji, t.LastAccessedAt })
            .ToListAsync(cancellationToken);

        return Ok(recentTopics);
    }

    [HttpGet("system-health")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetSystemHealth(CancellationToken cancellationToken)
    {
        // Admin HUD is system-wide. Per-user learning dashboard remains /stats.
        // Sessions aggregate: tokens + cost + count + last date — tek round-trip.
        var sessionAgg = await _dbContext.Sessions
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalTokens = g.Sum(s => (int?)s.TotalTokensUsed) ?? 0,
                TotalCostUSD = g.Sum(s => (decimal?)s.TotalCostUSD) ?? 0m,
                Count = g.Count(),
                LastDate = (DateTime?)g.Max(s => s.CreatedAt)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var totalTokens = sessionAgg?.TotalTokens ?? 0;
        var totalCostUSD = sessionAgg?.TotalCostUSD ?? 0m;
        var sessionCount = sessionAgg?.Count ?? 0;
        var lastSessionDate = sessionAgg?.LastDate;

        // Mastery count + ortalama tek projeksiyonda.
        var masteryAgg = await _dbContext.SkillMasteries
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Avg = (double?)g.Average(sm => (double)sm.QuizScore)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var masteryCount = masteryAgg?.Count ?? 0;
        var pedagogyScore = masteryCount > 0
            ? Math.Round(masteryAgg!.Avg ?? 0.0, 1)
            : 0.0;

        var sqlEvals = await _dbContext.AgentEvaluations
            .AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .Take(200)
            .Select(e => new
            {
                e.AgentRole,
                e.EvaluationScore,
                e.EvaluatorFeedback,
                e.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var agentQuality = sqlEvals
            .GroupBy(e => e.AgentRole)
            .Select(g => new
            {
                agentRole = g.Key,
                avgQuality = Math.Round(g.Average(e => (double)e.EvaluationScore), 2),
                totalEvals = g.Count(),
                lastEvalAt = g.Max(e => e.CreatedAt).ToString("O"),
                goldCount = g.Count(e => e.EvaluationScore >= 9),
                warnCount = g.Count(e => e.EvaluationScore < 5),
            })
            .ToList();

        var recentSqlLogs = sqlEvals
            .Take(20)
            .Select(e => new
            {
                score = e.EvaluationScore,
                agentRole = e.AgentRole,
                feedback = e.EvaluatorFeedback.Length > 200
                    ? e.EvaluatorFeedback[..200] + "..."
                    : e.EvaluatorFeedback,
                recordedAt = e.CreatedAt.ToString("HH:mm:ss"),
                quality = e.EvaluationScore >= 9 ? "gold"
                    : e.EvaluationScore >= 7 ? "good"
                    : e.EvaluationScore >= 5 ? "ok"
                    : "warn"
            })
            .ToList();

        var avgEvaluatorScore = sqlEvals.Count > 0
            ? Math.Round(sqlEvals.Average(e => (double)e.EvaluationScore), 2)
            : 0.0;

        // Redis çağrıları DbContext'ten bağımsız — paralelleştir.
        var agentMetricsTask = _redis.GetSystemMetricsAsync();
        var providerUsageTask = _redis.GetProviderUsageAsync();
        var redisHealthTask = _redis.GetRedisHealthAsync();
        var cacheMetricsTask = _redis.GetCacheMetricsAsync();

        await Task.WhenAll(agentMetricsTask, providerUsageTask, redisHealthTask, cacheMetricsTask);

        var agentMetrics = agentMetricsTask.Result.ToList();
        var providerUsage = providerUsageTask.Result.ToList();
        var redisHealth = redisHealthTask.Result;
        var cacheMetrics = cacheMetricsTask.Result.ToList();

        // DbContext bağımlı çağrılar — sequential (DbContext thread-safe değil).
        var learningOps = await BuildLearningOpsAsync();
        var endpointHealth = await BuildEndpointHealthAsync(redisHealth);

        var agentQualityMap = agentQuality.ToDictionary(a => a.agentRole, a => a);
        var enrichedAgents = agentMetrics.Select(a =>
        {
            agentQualityMap.TryGetValue(a.AgentRole, out var quality);
            return new
            {
                a.AgentRole,
                a.AvgLatencyMs,
                a.TotalCalls,
                a.ErrorCount,
                a.ErrorRatePct,
                a.LastProvider,
                avgQualityScore = quality?.avgQuality ?? 0.0,
                totalEvals = quality?.totalEvals ?? 0,
                goldCount = quality?.goldCount ?? 0,
                warnCount = quality?.warnCount ?? 0,
                status = a.TotalCalls == 0 ? "idle"
                    : a.ErrorRatePct > 50 ? "critical"
                    : a.ErrorRatePct > 20 ? "degraded"
                    : "online",
            };
        }).ToList();

        var notebookTools = cacheMetrics
            .Where(c => c.Area.StartsWith("notebook-", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(c.Area, "notebook-invalidation", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var notebookInvalidations = cacheMetrics
            .Where(c => string.Equals(c.Area, "notebook-invalidation", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var totalCacheEvents = cacheMetrics.Sum(c => c.HitCount + c.MissCount);
        var totalCacheHits = cacheMetrics.Sum(c => c.HitCount);
        var notebookEvents = notebookTools.Sum(c => c.HitCount + c.MissCount);
        var notebookHits = notebookTools.Sum(c => c.HitCount);

        return Ok(new
        {
            tokens = new
            {
                total = totalTokens,
                costUSD = Math.Round((double)totalCostUSD, 4),
            },
            pedagogy = new
            {
                score = pedagogyScore,
                masteredTopics = masteryCount,
            },
            sessions = new
            {
                total = sessionCount,
                lastDate = lastSessionDate?.ToString("O"),
            },
            llmops = new
            {
                avgEvaluatorScore,
                totalEvaluations = sqlEvals.Count,
                recentLogs = recentSqlLogs,
            },
            agents = enrichedAgents,
            modelMix = providerUsage,
            redis = redisHealth,
            cache = new
            {
                metrics = cacheMetrics,
                totalHits = totalCacheHits,
                totalMisses = cacheMetrics.Sum(c => c.MissCount),
                hitRatePct = totalCacheEvents == 0 ? 0 : Math.Round(totalCacheHits * 100.0 / totalCacheEvents, 1)
            },
            notebookCache = new
            {
                tools = notebookTools,
                invalidations = notebookInvalidations,
                hitRatePct = notebookEvents == 0 ? 0 : Math.Round(notebookHits * 100.0 / notebookEvents, 1)
            },
            learningOps,
            endpointHealth
        });
    }

    private async Task<object> BuildEndpointHealthAsync(object redisHealth)
    {
        var database = await CheckDatabaseAsync();
        var authEndpoints = new[]
        {
            Endpoint("POST", "/api/auth/register"),
            Endpoint("POST", "/api/auth/login"),
            Endpoint("POST", "/api/auth/refresh"),
            Endpoint("POST", "/api/auth/logout"),
            Endpoint("GET", "/api/user/me")
        };
        var coreEndpoints = new[]
        {
            Endpoint("GET", "/health/live"),
            Endpoint("GET", "/health/ready"),
            Endpoint("GET", "/swagger/v1/swagger.json", _environment.IsDevelopment() ? "available" : "disabled"),
            Endpoint("GET", "/api/topics"),
            Endpoint("POST", "/api/chat/message"),
            Endpoint("POST", "/api/quiz/attempt"),
            Endpoint("POST", "/api/learning/signal"),
            Endpoint("GET", "/api/wiki/{topicId}"),
            Endpoint("GET", "/api/sources/topic/{topicId}"),
            Endpoint("POST", "/api/classroom/session"),
            Endpoint("POST", "/api/audio/overview"),
            Endpoint("POST", "/api/code/run")
        };

        return new
        {
            apiBaseUrl = $"{Request.Scheme}://{Request.Host}",
            swagger = new
            {
                path = "/swagger",
                json = "/swagger/v1/swagger.json",
                enabled = _environment.IsDevelopment(),
                status = _environment.IsDevelopment() ? "available" : "disabled-outside-development"
            },
            health = new
            {
                live = "/health/live",
                ready = "/health/ready",
                database,
                redis = redisHealth
            },
            auth = authEndpoints,
            core = coreEndpoints
        };
    }

    private async Task<object> CheckDatabaseAsync()
    {
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(HttpContext.RequestAborted);
            return new { canConnect, error = (string?)null };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Dashboard] Database readiness check failed Provider={Provider}",
                _dbContext.Database.ProviderName);
            return new { canConnect = false, error = "Database readiness check failed." };
        }
    }

    private static object Endpoint(string method, string path, string status = "contracted") => new
    {
        method,
        path,
        status
    };

    private async Task<object> BuildLearningOpsAsync()
    {
        var since = DateTime.UtcNow.AddDays(-7);

        var signalCounts = await _dbContext.LearningSignals
            .AsNoTracking()
            .Where(s => s.CreatedAt >= since)
            .GroupBy(s => s.SignalType)
            .Select(g => new { signalType = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToListAsync();

        var topWeakSkills = await _dbContext.LearningSignals
            .AsNoTracking()
            .Where(s => s.CreatedAt >= since && s.SignalType == LearningSignalTypes.WeaknessDetected)
            .GroupBy(s => s.SkillTag ?? s.TopicPath ?? "unknown skill")
            .Select(g => new { skillTag = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .Take(8)
            .ToListAsync();

        var attempts = await _dbContext.QuizAttempts
            .AsNoTracking()
            .Where(a => a.CreatedAt >= since)
            .Select(a => new
            {
                a.UserId,
                a.TopicId,
                a.SkillTag,
                a.QuestionHash,
                a.IsCorrect
            })
            .ToListAsync();

        var totalAttempts = attempts.Count;
        var unknownSkillCount = attempts.Count(a =>
            string.IsNullOrWhiteSpace(a.SkillTag) ||
            string.Equals(a.SkillTag, "unknown skill", StringComparison.OrdinalIgnoreCase));
        var repeatedAttempts = attempts
            .Where(a => !string.IsNullOrWhiteSpace(a.QuestionHash))
            .GroupBy(a => new { a.UserId, a.TopicId, a.QuestionHash })
            .Select(g => g.Count())
            .Where(count => count > 1)
            .Sum(count => count - 1);

        var weaknessCount = signalCounts.FirstOrDefault(x => x.signalType == LearningSignalTypes.WeaknessDetected)?.count ?? 0;
        var remediationCompleted = signalCounts.FirstOrDefault(x => x.signalType == LearningSignalTypes.RemediationCompleted)?.count ?? 0;
        var signalCountMap = signalCounts.ToDictionary(x => x.signalType, x => x.count, StringComparer.OrdinalIgnoreCase);
        var countSignal = (string signalType) => signalCountMap.TryGetValue(signalType, out var count) ? count : 0;
        var educatorCoreSignals =
            countSignal(LearningSignalTypes.YouTubeReferenceUsed) +
            countSignal(LearningSignalTypes.NotebookSourceUsed) +
            countSignal(LearningSignalTypes.MisconceptionDetected) +
            countSignal(LearningSignalTypes.TeachingMoveApplied);
        var citationMissing = countSignal(LearningSignalTypes.SourceCitationMissing);

        var bridgeHealth = new[]
        {
            new
            {
                key = "educator-core",
                label = "EducatorCore -> Tutor",
                status = educatorCoreSignals == 0
                    ? "idle"
                    : citationMissing > 0 ? "watch" : "healthy",
                detail = educatorCoreSignals == 0
                    ? "P6 egitimci cekirdegi henuz canli sinyal almadi."
                    : $"YouTube pedagojisi, Notebook kaynaklari, misconception ve teaching move sinyalleri: {educatorCoreSignals}. Citation eksigi: {citationMissing}.",
                signals = new[]
                {
                    LearningSignalTypes.YouTubeReferenceUsed,
                    LearningSignalTypes.NotebookSourceUsed,
                    LearningSignalTypes.MisconceptionDetected,
                    LearningSignalTypes.TeachingMoveApplied,
                    LearningSignalTypes.SourceCitationMissing
                }.Select(signalType => new { signalType, count = countSignal(signalType) }).ToList()
            },
            new
            {
                key = "quiz",
                label = "Quiz -> Learning -> Plan",
                status = totalAttempts == 0
                    ? "idle"
                    : unknownSkillCount > 0 || repeatedAttempts > 0 ? "watch" : "healthy",
                detail = totalAttempts == 0
                    ? "Henuz quiz cevabi yok."
                    : $"{totalAttempts} cevap, skill bilinmeyen %{(totalAttempts == 0 ? 0 : Math.Round(unknownSkillCount * 100.0 / totalAttempts, 1))}, tekrar %{(totalAttempts == 0 ? 0 : Math.Round(repeatedAttempts * 100.0 / totalAttempts, 1))}.",
                signals = new[] { LearningSignalTypes.QuizAnswered, LearningSignalTypes.WeaknessDetected }
                    .Select(signalType => new { signalType, count = countSignal(signalType) })
                    .ToList()
            },
            new
            {
                key = "wiki-notebook",
                label = "Wiki / NotebookLM -> Tutor",
                status = new[]
                {
                    LearningSignalTypes.SourceUploaded,
                    LearningSignalTypes.SourceOpened,
                    LearningSignalTypes.SourceAsked,
                    LearningSignalTypes.WikiActionClicked
                }.Any(signalType => countSignal(signalType) > 0) ? "healthy" : "idle",
                detail = "Kaynak yukleme, citation/source acma ve Wiki aksiyonlari agent hafizasina sinyal olarak doner.",
                signals = new[]
                {
                    LearningSignalTypes.SourceUploaded,
                    LearningSignalTypes.SourceOpened,
                    LearningSignalTypes.SourceAsked,
                    LearningSignalTypes.WikiActionClicked
                }.Select(signalType => new { signalType, count = countSignal(signalType) }).ToList()
            },
            new
            {
                key = "classroom",
                label = "Sesli Sinif -> ClassroomAgent",
                status = countSignal(LearningSignalTypes.ClassroomStarted) + countSignal(LearningSignalTypes.ClassroomQuestionAsked) > 0
                    ? "healthy"
                    : "idle",
                detail = "Aktif transcript segmentiyle soru soruldugunda sinyal ve classroom context korunur.",
                signals = new[] { LearningSignalTypes.ClassroomStarted, LearningSignalTypes.ClassroomQuestionAsked }
                    .Select(signalType => new { signalType, count = countSignal(signalType) })
                    .ToList()
            },
            new
            {
                key = "ide",
                label = "IDE -> Tutor",
                status = countSignal(LearningSignalTypes.IdeRunCompleted) + countSignal(LearningSignalTypes.IdeSentToTutor) > 0
                    ? "healthy"
                    : "idle",
                detail = "Kod calistirma ve hocaya gonderme akisi topic/session ogrenme hafizasina bagli.",
                signals = new[] { LearningSignalTypes.IdeRunCompleted, LearningSignalTypes.IdeSentToTutor }
                    .Select(signalType => new { signalType, count = countSignal(signalType) })
                    .ToList()
            },
            new
            {
                key = "remediation",
                label = "Zayiflik -> Telafi",
                status = weaknessCount == 0
                    ? "idle"
                    : remediationCompleted > 0 ? "healthy" : "watch",
                detail = weaknessCount == 0
                    ? "Zayiflik sinyali yok."
                    : $"{weaknessCount} zayiflik, {remediationCompleted} tamamlanan telafi.",
                signals = new[] { LearningSignalTypes.WeaknessDetected, LearningSignalTypes.RemediationStarted, LearningSignalTypes.RemediationCompleted }
                    .Select(signalType => new { signalType, count = countSignal(signalType) })
                    .ToList()
            }
        };

        return new
        {
            windowDays = 7,
            totalSignals = signalCounts.Sum(x => x.count),
            signalCounts,
            topWeakSkills,
            educatorCore = new
            {
                signals = educatorCoreSignals,
                citationMissing,
                youtubeReferenceUsed = countSignal(LearningSignalTypes.YouTubeReferenceUsed),
                notebookSourceUsed = countSignal(LearningSignalTypes.NotebookSourceUsed),
                misconceptionDetected = countSignal(LearningSignalTypes.MisconceptionDetected),
                teachingMoveApplied = countSignal(LearningSignalTypes.TeachingMoveApplied),
                status = educatorCoreSignals == 0 ? "idle" : citationMissing > 0 ? "watch" : "healthy"
            },
            quizAttempts = totalAttempts,
            quizAccuracyPct = totalAttempts == 0 ? 0 : Math.Round(attempts.Count(a => a.IsCorrect) * 100.0 / totalAttempts, 1),
            unknownSkillRatePct = totalAttempts == 0 ? 0 : Math.Round(unknownSkillCount * 100.0 / totalAttempts, 1),
            repeatedQuestionRatePct = totalAttempts == 0 ? 0 : Math.Round(repeatedAttempts * 100.0 / totalAttempts, 1),
            remediationCompletionRatePct = weaknessCount == 0 ? 0 : Math.Round(remediationCompleted * 100.0 / weaknessCount, 1),
            learningBridge = new
            {
                healthy = bridgeHealth.Count(b => b.status == "healthy"),
                watch = bridgeHealth.Count(b => b.status == "watch"),
                idle = bridgeHealth.Count(b => b.status == "idle"),
                bridges = bridgeHealth
            }
        };
    }
}
