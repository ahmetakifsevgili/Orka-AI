using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;
using Orka.Core.Services;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Services;
using System.Security.Claims;

namespace Orka.API.Controllers;

[Authorize]
[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly OrkaDbContext _dbContext;
    private readonly IRedisMemoryService _redis;
    private readonly ITopicScopeResolver _topicScopeResolver;
    private readonly IAiProviderTelemetryService _aiProviderTelemetry;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public DashboardController(
        OrkaDbContext dbContext,
        IRedisMemoryService redis,
        ITopicScopeResolver topicScopeResolver,
        IAiProviderTelemetryService aiProviderTelemetry,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _redis = redis;
        _topicScopeResolver = topicScopeResolver;
        _aiProviderTelemetry = aiProviderTelemetry;
        _configuration = configuration;
        _environment = environment;
    }

    private Guid GetUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static DashboardSourceHealthDto BuildSourceHealth(Orka.Core.Entities.SourceQualityReport? report, int sourceCount = 0, int readySourceCount = 0)
    {
        if (report is null)
        {
            return new DashboardSourceHealthDto
            {
                Status = "unknown",
                UserSafeLabel = "Kaynak yok",
                UserSafeDetail = "Kaynak eklenirse Wiki ve Tutor cevaplari daha guvenli olur.",
                EvidenceQuality = sourceCount > 0
                    ? EvidenceQualityEvaluator.Build(sourceCount, readySourceCount, 0, 0m, 0, 0, "unverified", "unverified")
                    : EvidenceQualityEvaluator.Build(0, 0, 0, 0m, 0, 0, "no_source", "unknown")
            };
        }

        var status = report.QualityStatus;
        var label = status switch
        {
            "healthy" => "Kaynaklar destekli",
            "degraded" => "Kaynaklar zayif",
            "source_retrieval_empty" => "Kaynakta cevap yok",
            "citation_missing" => "Citation eksik",
            "citation_unsupported" => "Citation desteklenmiyor",
            _ when report.UnsupportedCitationCount > 0 => "Citation desteklenmiyor",
            _ when report.CitationMissingCount > 0 => "Citation eksik",
            _ => "Kaynak durumu izleniyor"
        };

        var detail = status switch
        {
            "healthy" => "Cevaplar kaynak parcalariyla eslesiyor.",
            "degraded" => "Bazi cevaplar icin kaynak kaniti zayif; dikkatli ilerle.",
            "source_retrieval_empty" => "Soru icin kaynaklarda net parca bulunamadi.",
            "citation_missing" => "Bazi kaynakli iddialarda citation yok.",
            "citation_unsupported" => "Bazi citation etiketleri bulunan kaynak parcasiyla eslesmiyor.",
            _ when report.UnsupportedCitationCount > 0 => "Bazi citation etiketleri bulunan kaynak parcasiyla eslesmiyor.",
            _ when report.CitationMissingCount > 0 => "Bazi kaynakli iddialarda citation yok.",
            _ => "Kaynak kalitesi olculuyor."
        };

        return new DashboardSourceHealthDto
        {
            Status = status,
            UserSafeLabel = label,
            UserSafeDetail = detail,
            CitationCoverage = report.CitationCoverage,
            UnsupportedCitationCount = report.UnsupportedCitationCount,
            EvidenceQuality = EvidenceQualityEvaluator.Build(
                sourceCount,
                readySourceCount,
                report.RetrievalRunCount,
                report.CitationCoverage,
                report.UnsupportedCitationCount,
                report.CitationMissingCount,
                report.RetrievalHealthStatus,
                report.CitationCoverageStatus)
        };
    }

    [HttpGet("today")]
    public async Task<ActionResult<DashboardTodayDto>> GetToday(CancellationToken ct)
    {
        var userId = GetUserId();
        var now = DateTime.UtcNow;

        var activeTopic = await _dbContext.Topics
            .AsNoTracking()
            .Where(t => t.UserId == userId && !t.IsArchived && t.ParentTopicId == null)
            .OrderByDescending(t => t.LastAccessedAt)
            .ThenByDescending(t => t.CreatedAt)
            .Select(t => new DashboardActivePlanDto
            {
                TopicId = t.Id,
                Title = t.Title,
                ProgressPercentage = t.ProgressPercentage
            })
            .FirstOrDefaultAsync(ct);

        var activeTopicId = activeTopic?.TopicId;
        var activeScopeTopicIds = Array.Empty<Guid>();
        TopicScope? activeScope = null;
        if (activeTopicId.HasValue)
        {
            activeScope = await _topicScopeResolver.ResolveAsync(userId, activeTopicId.Value, ct);
            activeScopeTopicIds = activeScope.IsValid
                ? activeScope.TreeTopicIds.ToArray()
                : [activeTopicId.Value];
        }

        var weakConceptStates = await _dbContext.KnowledgeTracingStates
            .AsNoTracking()
            .Where(k => k.UserId == userId && (activeScopeTopicIds.Length == 0 || (k.TopicId.HasValue && activeScopeTopicIds.Contains(k.TopicId.Value))))
            .OrderBy(k => k.MasteryProbability)
            .ThenBy(k => k.Confidence)
            .Take(5)
            .Select(k => new
            {
                k.ConceptKey,
                k.Label,
                k.MasteryProbability,
                k.Confidence,
                k.TopicId,
                k.IncorrectCount,
                k.RemediationNeed
            })
            .ToListAsync(ct);
        var weakConcepts = weakConceptStates
            .Select(k =>
            {
                var label = string.IsNullOrWhiteSpace(k.Label) ? k.ConceptKey : k.Label;
                var intelligence = MisconceptionIntelligenceEvaluator.FromWeakConcept(
                    k.TopicId,
                    k.ConceptKey,
                    label,
                    k.MasteryProbability,
                    k.Confidence,
                    k.IncorrectCount,
                    k.RemediationNeed);
                return new DashboardWeakConceptDto
                {
                    ConceptKey = k.ConceptKey,
                    Label = label,
                    MasteryProbability = k.MasteryProbability,
                    Confidence = k.Confidence,
                    TopicId = k.TopicId,
                    UserSafeStatus = k.Confidence < 0.60m ? "Kanıt düşük" : k.MasteryProbability < 0.55m ? "Tekrar iyi olur" : "İzleniyor",
                    MisconceptionSignal = intelligence.MisconceptionSignal,
                    LearningSignalConfidence = intelligence.LearningSignalConfidence,
                    RemediationSeed = intelligence.RemediationSeed
                };
            })
            .ToList();

        var dueReviews = await _dbContext.ReviewItems
            .AsNoTracking()
            .CountAsync(r => r.UserId == userId && r.Status == "active" && r.DueAt <= now, ct);

        var latestSourceQuality = await _dbContext.SourceQualityReports
            .AsNoTracking()
            .Where(r => r.UserId == userId && (activeScopeTopicIds.Length == 0 || (r.TopicId.HasValue && activeScopeTopicIds.Contains(r.TopicId.Value))))
            .OrderByDescending(r => r.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        var scopedSourceCount = await _dbContext.LearningSources
            .AsNoTracking()
            .CountAsync(s => s.UserId == userId && !s.IsDeleted && (activeScopeTopicIds.Length == 0 || (s.TopicId.HasValue && activeScopeTopicIds.Contains(s.TopicId.Value))), ct);
        var scopedReadySourceCount = await _dbContext.LearningSources
            .AsNoTracking()
            .CountAsync(s => s.UserId == userId && !s.IsDeleted && s.Status == "ready" && (activeScopeTopicIds.Length == 0 || (s.TopicId.HasValue && activeScopeTopicIds.Contains(s.TopicId.Value))), ct);
        var scopedQuizAttemptCount = await _dbContext.QuizAttempts
            .AsNoTracking()
            .CountAsync(a => a.UserId == userId && (activeScopeTopicIds.Length == 0 || (a.TopicId.HasValue && activeScopeTopicIds.Contains(a.TopicId.Value))), ct);
        var scopedLearningSignalCount = await _dbContext.LearningSignals
            .AsNoTracking()
            .CountAsync(s => s.UserId == userId && (activeScopeTopicIds.Length == 0 || (s.TopicId.HasValue && activeScopeTopicIds.Contains(s.TopicId.Value))), ct);
        var coordinationHealth = await BuildCoordinationHealthAsync(userId, activeTopicId, activeScope, activeScopeTopicIds, ct);

        var sourceHealth = latestSourceQuality is not null
            ? BuildSourceHealth(latestSourceQuality, scopedSourceCount, scopedReadySourceCount)
            : scopedSourceCount > 0
                ? new DashboardSourceHealthDto
                {
                    Status = "unverified",
                    UserSafeLabel = "Kaynaklar hazir",
                    UserSafeDetail = "Kaynaklar var; citation kalitesi ilk kullanimda olculecek.",
                    EvidenceQuality = EvidenceQualityEvaluator.Build(scopedSourceCount, scopedReadySourceCount, 0, 0m, 0, 0, "unverified", "unverified")
                }
                : BuildSourceHealth(null);
        var hasRealData = activeTopic is not null ||
            weakConcepts.Count > 0 ||
            dueReviews > 0 ||
            latestSourceQuality is not null ||
            scopedSourceCount > 0 ||
            scopedQuizAttemptCount > 0 ||
            scopedLearningSignalCount > 0;
        var entry = dueReviews > 0
            ? new DashboardEntryPointDto { View = "learning", Label = "Tekrar", Reason = "Bugun zamani gelen tekrarlarin var." }
            : weakConcepts.Count > 0
                ? new DashboardEntryPointDto { View = "chat", Label = "Ogren", Reason = "Zayif kavrami Tutor ile toparla." }
                : sourceHealth.Status is "source_retrieval_empty" or "unknown"
                    ? new DashboardEntryPointDto { View = "sources", Label = "Kaynaklar", Reason = "Kaynak eklemek cevap kalitesini artirir." }
                    : new DashboardEntryPointDto { View = "chat", Label = "Ogren", Reason = "Tutor ile siradaki adima gec." };

        var focusTitle = weakConcepts.FirstOrDefault()?.Label
            ?? activeTopic?.Title
            ?? "Bugun";

        var focusReason = weakConcepts.Count > 0
            ? "Bu kavramda ogrenme kaniti dusuk; kisa bir aciklama ve mikro pratik iyi olur."
            : dueReviews > 0
                ? "Tekrar zamani gelen kartlar var; once onlari kapatmak hafizayi guclendirir."
                : activeTopic is not null
                    ? $"{activeTopic.Title} uzerinde kaldigin yerden devam edebilirsin."
                    : "Henuz yeterli ogrenme izi yok; bir konu acip Tutor ile baslayabilirsin.";

        return Ok(new DashboardTodayDto
        {
            DailyFocusTitle = focusTitle,
            DailyFocusReason = focusReason,
            DueReviewCount = dueReviews,
            WeakConcepts = weakConcepts,
            SourceHealth = sourceHealth,
            ActivePlan = activeTopic,
            CoordinationScope = activeTopicId.HasValue
                ? new DashboardCoordinationScopeDto
                {
                    RootTopicId = activeScope?.IsValid == true ? activeScope.RootTopicId : activeTopicId,
                    CurrentTopicId = activeTopicId,
                    ActiveLessonTopicId = activeScope?.ActiveLessonTopicId,
                    TreeTopicCount = activeScopeTopicIds.Length,
                    SourceCount = scopedSourceCount,
                    QuizAttemptCount = scopedQuizAttemptCount,
                    LearningSignalCount = scopedLearningSignalCount
                }
                : null,
            CoordinationHealth = coordinationHealth,
            RecommendedEntryPoint = entry,
            HasRealLearningData = hasRealData,
            NextAction = new DashboardNextActionDto
            {
                Label = entry.Label,
                Reason = entry.Reason,
                View = entry.View,
                TopicId = activeTopic?.TopicId,
                UserSafeStatus = hasRealData ? "Hazir" : "Veri bekleniyor"
            },
            GeneratedAt = DateTimeOffset.UtcNow
        });
    }

    private async Task<DashboardCoordinationHealthDto> BuildCoordinationHealthAsync(
        Guid userId,
        Guid? activeTopicId,
        TopicScope? activeScope,
        IReadOnlyCollection<Guid> scopedTopicIds,
        CancellationToken ct)
    {
        const int windowDays = 7;
        var generatedAt = DateTimeOffset.UtcNow;
        if (!activeTopicId.HasValue || scopedTopicIds.Count == 0)
        {
            return new DashboardCoordinationHealthDto
            {
                OverallStatus = "no_plan",
                UserSafeSummary = "Aktif plan yok; koordinasyon sagligi ilk planla birlikte olusur.",
                WindowDays = windowDays,
                Metrics =
                [
                    Metric("topicTreeCompleteness", "no_plan", 0, 0, "Plan yok", "Aktif root plan bulununca topic tree koordinasyonu izlenir.")
                ],
                GeneratedAt = generatedAt
            };
        }

        var topicIds = scopedTopicIds.Distinct().ToArray();
        var since = DateTime.UtcNow.AddDays(-windowDays);
        var today = DateTime.UtcNow.Date;

        var treeTopicCount = topicIds.Length;
        var wikiPageCount = await _dbContext.WikiPages
            .AsNoTracking()
            .CountAsync(p => p.UserId == userId && topicIds.Contains(p.TopicId), ct);
        var wikiBlockCount = await _dbContext.WikiBlocks
            .AsNoTracking()
            .CountAsync(b => b.WikiPage.UserId == userId && topicIds.Contains(b.WikiPage.TopicId), ct);

        var sourceStats = await _dbContext.LearningSources
            .AsNoTracking()
            .Where(s => s.UserId == userId && !s.IsDeleted && s.TopicId.HasValue && topicIds.Contains(s.TopicId.Value))
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Ready = g.Count(s => s.Status == "ready")
            })
            .FirstOrDefaultAsync(ct);

        var quizAttemptCount = await _dbContext.QuizAttempts
            .AsNoTracking()
            .CountAsync(a => a.UserId == userId && a.TopicId.HasValue && topicIds.Contains(a.TopicId.Value), ct);
        var learningSignalCount = await _dbContext.LearningSignals
            .AsNoTracking()
            .CountAsync(s => s.UserId == userId && s.TopicId.HasValue && topicIds.Contains(s.TopicId.Value), ct);
        var knowledgeStateCount = await _dbContext.KnowledgeTracingStates
            .AsNoTracking()
            .CountAsync(k => k.UserId == userId && k.TopicId.HasValue && topicIds.Contains(k.TopicId.Value), ct);
        var skillMasteryCount = await _dbContext.SkillMasteries
            .AsNoTracking()
            .CountAsync(s => s.UserId == userId && topicIds.Contains(s.TopicId), ct);

        var recentAssistantMessages = await _dbContext.Messages
            .AsNoTracking()
            .CountAsync(m =>
                m.UserId == userId &&
                m.Role == "assistant" &&
                m.CreatedAt >= since &&
                m.Session.TopicId.HasValue &&
                topicIds.Contains(m.Session.TopicId.Value), ct);
        var recentEvaluations = await _dbContext.AgentEvaluations
            .AsNoTracking()
            .CountAsync(e =>
                e.UserId == userId &&
                e.CreatedAt >= since &&
                e.Session.TopicId.HasValue &&
                topicIds.Contains(e.Session.TopicId.Value), ct);

        var retrievalRuns = await _dbContext.SourceRetrievalRuns
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.CreatedAt >= since && r.TopicId.HasValue && topicIds.Contains(r.TopicId.Value))
            .Select(r => new { r.IsEmpty, r.QualityStatus })
            .ToListAsync(ct);
        var healthyRetrievalRuns = retrievalRuns.Count(r =>
            !r.IsEmpty &&
            !string.Equals(r.QualityStatus, "source_retrieval_empty", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(r.QualityStatus, "low_confidence", StringComparison.OrdinalIgnoreCase));

        var sourceQualityReports = await _dbContext.SourceQualityReports
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.TopicId.HasValue && topicIds.Contains(r.TopicId.Value))
            .OrderByDescending(r => r.GeneratedAt)
            .Take(30)
            .Select(r => new { r.QualityStatus, r.CitationCoverage, r.UnsupportedCitationCount, r.CitationMissingCount })
            .ToListAsync(ct);
        var healthySourceQualityReports = sourceQualityReports.Count(r =>
            string.Equals(r.QualityStatus, "healthy", StringComparison.OrdinalIgnoreCase) &&
            r.UnsupportedCitationCount == 0 &&
            r.CitationMissingCount == 0);

        var userCostMetric = await BuildUserCostQuotaMetricAsync(userId, today, ct);
        var sourceTotal = sourceStats?.Total ?? 0;
        var readySources = sourceStats?.Ready ?? 0;
        var learningProfileCount = learningSignalCount + knowledgeStateCount + skillMasteryCount;

        var metrics = new List<DashboardCoordinationHealthMetricDto>
        {
            Metric(
                "topicTreeCompleteness",
                activeScope?.IsValid == true ? activeScope.ActiveLessonTopicId.HasValue ? "healthy" : "watch" : "watch",
                treeTopicCount,
                Math.Max(1, treeTopicCount),
                "Plan agaci",
                activeScope?.IsValid == true
                    ? $"Root plan {treeTopicCount} topic ile izleniyor."
                    : "Aktif plan agaci dogrulanamadi; dashboard root kapsam fallback kullanir."),
            Metric(
                "wikiReadiness",
                wikiPageCount > 0 ? "healthy" : "idle",
                wikiPageCount,
                Math.Max(1, treeTopicCount),
                "Wiki hazirligi",
                wikiPageCount > 0
                    ? $"{wikiPageCount} wiki sayfasi ve {wikiBlockCount} blok root plan altinda gorunuyor."
                    : "Root plan altinda henuz wiki kaniti yok."),
            Metric(
                "sourceCoverage",
                sourceTotal == 0 ? "idle" : readySources == sourceTotal ? "healthy" : "watch",
                readySources,
                sourceTotal,
                "Kaynak kapsami",
                sourceTotal == 0
                    ? "Root plan altinda kaynak yok."
                    : $"{readySources}/{sourceTotal} kaynak hazir durumda."),
            Metric(
                "quizCoverage",
                quizAttemptCount > 0 ? "healthy" : "idle",
                quizAttemptCount,
                Math.Max(1, treeTopicCount),
                "Quiz kaniti",
                quizAttemptCount > 0
                    ? $"{quizAttemptCount} quiz denemesi root plan altinda gorunuyor."
                    : "Root plan altinda quiz denemesi yok."),
            Metric(
                "learningProfileCoverage",
                learningProfileCount > 0 ? "healthy" : "idle",
                learningProfileCount,
                Math.Max(1, treeTopicCount),
                "Ogrenme profili",
                $"{learningSignalCount} sinyal, {knowledgeStateCount} bilgi izi, {skillMasteryCount} mastery kaydi root plan altinda gorunuyor."),
            Metric(
                "chatPostProcessingHealth",
                recentAssistantMessages == 0 ? "idle" : recentEvaluations >= recentAssistantMessages ? "healthy" : recentEvaluations > 0 ? "watch" : "critical",
                recentEvaluations,
                recentAssistantMessages,
                "Chat postprocess",
                recentAssistantMessages == 0
                    ? "Son 7 gunde postprocess bekleyen assistant mesaji yok."
                    : $"{recentEvaluations}/{recentAssistantMessages} assistant mesaji evaluator/postprocess kaniti tasiyor."),
            Metric(
                "ragScopeCoverage",
                retrievalRuns.Count == 0 ? "idle" : healthyRetrievalRuns == retrievalRuns.Count ? "healthy" : healthyRetrievalRuns > 0 ? "watch" : "critical",
                healthyRetrievalRuns,
                retrievalRuns.Count,
                "RAG scope",
                retrievalRuns.Count == 0
                    ? "Son 7 gunde RAG retrieval kosmadi."
                    : $"{healthyRetrievalRuns}/{retrievalRuns.Count} retrieval saglikli tamamlandi."),
            Metric(
                "sourceQuality",
                sourceQualityReports.Count == 0 ? "idle" : healthySourceQualityReports == sourceQualityReports.Count ? "healthy" : healthySourceQualityReports > 0 ? "watch" : "critical",
                healthySourceQualityReports,
                sourceQualityReports.Count,
                "Kaynak kalite raporu",
                sourceQualityReports.Count == 0
                    ? "Root plan altinda henuz kaynak kalite raporu yok."
                    : $"{healthySourceQualityReports}/{sourceQualityReports.Count} son kaynak kalite raporu saglikli."),
            userCostMetric
        };

        var overall = ResolveOverallStatus(metrics);
        return new DashboardCoordinationHealthDto
        {
            OverallStatus = overall,
            UserSafeSummary = overall switch
            {
                "healthy" => "Root plan koordinasyon kanitlari saglikli gorunuyor.",
                "watch" => "Root plan koordinasyonu calisiyor; bazi kanitlar izlenmeli.",
                "critical" => "Root plan koordinasyonunda dikkat isteyen eksikler var.",
                _ => "Root plan koordinasyon kanitlari toplanmaya basladi."
            },
            WindowDays = windowDays,
            RootTopicId = activeScope?.IsValid == true ? activeScope.RootTopicId : activeTopicId,
            CurrentTopicId = activeTopicId,
            ActiveLessonTopicId = activeScope?.ActiveLessonTopicId,
            Metrics = metrics,
            GeneratedAt = generatedAt
        };
    }

    private async Task<DashboardCoordinationHealthMetricDto> BuildUserCostQuotaMetricAsync(
        Guid userId,
        DateTime today,
        CancellationToken ct)
    {
        var userCosts = await _dbContext.CostRecords
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.OccurredAt >= today)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Tokens = g.Sum(c => c.EstimatedTokens),
                Cost = g.Sum(c => c.EstimatedCostUsd)
            })
            .FirstOrDefaultAsync(ct);

        var tokens = userCosts?.Tokens ?? 0;
        var cost = userCosts?.Cost ?? 0m;
        var tokenLimit = _configuration.GetValue<int?>("AI:Cost:UserDailyTokenLimit");
        var costLimit = _configuration.GetValue<decimal?>("AI:Cost:UserDailyUsdLimit");
        var tokenRatio = tokenLimit.HasValue && tokenLimit.Value > 0
            ? Math.Clamp(tokens / (decimal)tokenLimit.Value, 0m, 1m)
            : 0m;
        var costRatio = costLimit.HasValue && costLimit.Value > 0m
            ? Math.Clamp(cost / costLimit.Value, 0m, 1m)
            : 0m;
        var ratio = Math.Max(tokenRatio, costRatio);
        var hasLimit = tokenLimit.HasValue || costLimit.HasValue;
        var status = !hasLimit ? "healthy" :
            ratio >= 0.95m ? "critical" :
            ratio >= 0.80m ? "watch" :
            "healthy";

        return Metric(
            "costQuotaState",
            status,
            tokens,
            tokenLimit ?? 0,
            "Maliyet kotasi",
            hasLimit
                ? $"Bugun {tokens} token ve {Math.Round(cost, 4)} USD tahmini kullanim var."
                : $"Bugun {tokens} token ve {Math.Round(cost, 4)} USD tahmini kullanim var; kullanici kotasi tanimli degil.",
            ratio);
    }

    private static DashboardCoordinationHealthMetricDto Metric(
        string key,
        string status,
        int count,
        int total,
        string label,
        string detail,
        decimal? ratioOverride = null)
    {
        var ratio = ratioOverride ?? (total <= 0 ? 0m : Math.Clamp(count / (decimal)total, 0m, 1m));
        return new DashboardCoordinationHealthMetricDto
        {
            Key = key,
            Status = status,
            Count = Math.Max(0, count),
            Total = Math.Max(0, total),
            Ratio = Math.Round(ratio, 4),
            UserSafeLabel = label,
            UserSafeDetail = detail
        };
    }

    private static string ResolveOverallStatus(IReadOnlyCollection<DashboardCoordinationHealthMetricDto> metrics)
    {
        if (metrics.Any(m => m.Status == "critical"))
            return "critical";
        if (metrics.Any(m => m.Status == "watch"))
            return "watch";
        if (metrics.Any(m => m.Status == "healthy"))
            return "healthy";
        return "idle";
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var userId = GetUserId();

        var user = await _dbContext.Users.FindAsync(userId);
        var totalXP = user?.TotalXP ?? 0;
        var currentStreak = user?.CurrentStreak ?? 0;

        var allTopics = await _dbContext.Topics
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.ParentTopicId == null)
            .Select(t => new { t.ProgressPercentage, t.IsArchived })
            .ToListAsync();

        var completedTopics = allTopics.Count(t => t.ProgressPercentage >= 100);
        var activeLearning = allTopics.Count(t => t.ProgressPercentage > 0 && t.ProgressPercentage < 100);
        var totalTopics = allTopics.Count;

        var sectionData = await _dbContext.Topics
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => new { t.TotalSections, t.CompletedSections })
            .ToListAsync();

        var totalSections = sectionData.Sum(t => t.TotalSections);
        var completedSections = sectionData.Sum(t => t.CompletedSections);
        var progressPercentage = totalSections > 0
            ? Math.Round((double)completedSections / totalSections * 100, 1)
            : 0.0;

        var weekAgo = DateTime.UtcNow.Date.AddDays(-7);
        var recentMessages = await _dbContext.Messages
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.CreatedAt >= weekAgo)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        var activity = recentMessages
            .GroupBy(m => m.CreatedAt.Date)
            .Select(g => new { date = g.Key, count = g.Count() })
            .ToList();

        var wikisCount = await _dbContext.WikiPages.CountAsync(w => w.UserId == userId);
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
            .ToListAsync();

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
            .ToListAsync();

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
                    ? $"{weakSkills[0].skillTag} becerisi oncelikli tekrar istiyor."
                    : "Henuz belirgin zayif beceri sinyali yok."
            }
        });
    }

    [HttpGet("recent-activity")]
    public async Task<IActionResult> GetRecentActivity()
    {
        var userId = GetUserId();

        var recentTopics = await _dbContext.Topics
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.LastAccessedAt)
            .Take(5)
            .Select(t => new { t.Id, t.Title, t.Emoji, t.LastAccessedAt })
            .ToListAsync();

        return Ok(recentTopics);
    }

    [HttpGet("system-health")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetSystemHealth()
    {
        // Admin HUD is system-wide. Per-user learning dashboard remains /stats.
        var totalTokens = await _dbContext.Sessions
            .AsNoTracking()
            .SumAsync(s => (int?)s.TotalTokensUsed) ?? 0;

        var totalCostUSD = await _dbContext.Sessions
            .AsNoTracking()
            .SumAsync(s => (decimal?)s.TotalCostUSD) ?? 0m;

        var masteryCount = await _dbContext.SkillMasteries
            .AsNoTracking()
            .CountAsync();

        var averageMastery = await _dbContext.SkillMasteries
            .AsNoTracking()
            .Select(sm => (double?)sm.QuizScore)
            .AverageAsync() ?? 0.0;

        var pedagogyScore = masteryCount > 0
            ? Math.Round(averageMastery, 1)
            : 0.0;

        var sessionCount = await _dbContext.Sessions.CountAsync();
        var lastSessionDate = await _dbContext.Sessions
            .AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => (DateTime?)s.CreatedAt)
            .FirstOrDefaultAsync();

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
            .ToListAsync();

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

        var agentMetrics = (await _redis.GetSystemMetricsAsync()).ToList();
        var providerUsage = (await _redis.GetProviderUsageAsync()).ToList();
        var redisHealth = await _redis.GetRedisHealthAsync();
        var cacheMetrics = (await _redis.GetCacheMetricsAsync()).ToList();
        var learningOps = await BuildLearningOpsAsync();
        var endpointHealth = await BuildEndpointHealthAsync(redisHealth);
        var coordinationHealth = await BuildSystemCoordinationHealthAsync();

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
            endpointHealth,
            coordinationHealth
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
        catch (Exception)
        {
            return new { canConnect = false, error = "Database readiness check failed." };
        }
    }

    private static object Endpoint(string method, string path, string status = "contracted") => new
    {
        method,
        path,
        status
    };

    private async Task<object> BuildSystemCoordinationHealthAsync()
    {
        var providerSummary = await _aiProviderTelemetry.GetSummaryAsync(HttpContext.RequestAborted);
        var today = DateTime.UtcNow.Date;
        var globalCosts = await _dbContext.CostRecords
            .AsNoTracking()
            .Where(c => c.OccurredAt >= today)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Tokens = g.Sum(c => c.EstimatedTokens),
                Cost = g.Sum(c => c.EstimatedCostUsd)
            })
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        var tokens = globalCosts?.Tokens ?? 0;
        var cost = globalCosts?.Cost ?? 0m;
        var tokenLimit = _configuration.GetValue<int?>("AI:Cost:GlobalDailyTokenLimit");
        var costLimit = _configuration.GetValue<decimal?>("AI:Cost:GlobalDailyUsdLimit");
        var tokenRatio = tokenLimit.HasValue && tokenLimit.Value > 0
            ? Math.Clamp(tokens / (decimal)tokenLimit.Value, 0m, 1m)
            : 0m;
        var costRatio = costLimit.HasValue && costLimit.Value > 0m
            ? Math.Clamp(cost / costLimit.Value, 0m, 1m)
            : 0m;
        var quotaRatio = Math.Max(tokenRatio, costRatio);
        var quotaStatus = !(tokenLimit.HasValue || costLimit.HasValue) ? "healthy" :
            quotaRatio >= 0.95m ? "critical" :
            quotaRatio >= 0.80m ? "watch" :
            "healthy";
        var providerStatus = providerSummary.QuotaHitCount24h > 0 ? "watch" :
            providerSummary.FailureKinds24h.Values.Sum() > 0 || providerSummary.FallbackCount24h > 0 ? "watch" :
            "healthy";

        return new
        {
            korteksContractHealth = new
            {
                status = "regression_gated",
                source = "quick-coordination",
                mandatoryTests = new[]
                {
                    "KorteksContractTests",
                    "RagScopeIntegrationTests",
                    "TopicTreeScopeContractTests"
                }
            },
            aiProviderHealth = new
            {
                status = providerStatus,
                providerSummary.FallbackCount24h,
                providerSummary.QuotaHitCount24h,
                providerSummary.FailureKinds24h,
                providerSummary.CircuitStates
            },
            costQuotaState = new
            {
                status = quotaStatus,
                estimatedTokensToday = tokens,
                estimatedCostUsdToday = Math.Round(cost, 4),
                globalDailyTokenLimit = tokenLimit,
                globalDailyUsdLimit = costLimit,
                ratio = Math.Round(quotaRatio, 4)
            }
        };
    }

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
