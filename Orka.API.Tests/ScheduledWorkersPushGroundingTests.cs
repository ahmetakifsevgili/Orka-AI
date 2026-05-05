using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.API.Tests;

public sealed class ScheduledWorkersPushGroundingTests
{
    [Fact]
    public async Task PushDelivery_DisabledProviderReturnsSafeResultAndRecordsTelemetry()
    {
        await using var db = CreateDb();
        var telemetry = new RuntimeTelemetryService(db, NullLogger<RuntimeTelemetryService>.Instance);
        var push = new PushDeliveryService(EmptyConfig(), telemetry, NullLogger<PushDeliveryService>.Instance);

        var result = await push.SendAsync(
            Guid.NewGuid(),
            null,
            new Notification { Id = Guid.NewGuid(), Type = "daily-challenge" });

        Assert.False(result.Success);
        Assert.Equal("disabled", result.Status);
        Assert.Equal("provider_disabled", result.ErrorCode);
        Assert.DoesNotContain("token", result.SafeMessage, StringComparison.OrdinalIgnoreCase);

        var evt = await db.ToolTelemetryEvents.SingleAsync();
        Assert.Equal("push_delivery", evt.ToolId);
        Assert.Equal("disabled", evt.CapabilityStatus);
    }

    [Fact]
    public async Task SrsReminderWorker_DisabledConfigNoOpsWithTelemetry()
    {
        await using var db = CreateDb();
        var service = CreateSrsWorker(db, EmptyConfig());

        var count = await service.RunOnceAsync();

        Assert.Equal(0, count);
        var evt = await db.ToolTelemetryEvents.SingleAsync();
        Assert.Equal("srs_reminder_worker", evt.ToolId);
        Assert.Equal("disabled", evt.CapabilityStatus);
    }

    [Fact]
    public async Task SrsReminderWorker_CreatesReminderAndPreventsDuplicate()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        db.Users.Add(new User { Id = userId, Email = "srs@example.com", CreatedAt = DateTime.UtcNow });
        db.Topics.Add(new Topic { Id = topicId, UserId = userId, Title = "SRS", CreatedAt = DateTime.UtcNow });
        db.ReviewItems.Add(new ReviewItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ReviewKey = "concept:test",
            ConceptTag = "bounded reminders",
            DueAt = DateTime.UtcNow.AddMinutes(-5),
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var config = Config(("Workers:SrsReminder:Enabled", "true"), ("Workers:SrsReminder:BatchSize", "10"));
        var service = CreateSrsWorker(db, config);

        Assert.Equal(1, await service.RunOnceAsync());
        Assert.Equal(0, await service.RunOnceAsync());

        var notifications = await db.Notifications.Where(n => n.Type == "srs-reminder").ToListAsync();
        Assert.Single(notifications);
        Assert.Equal("ReviewItem", notifications[0].RelatedEntityType);
        Assert.Contains(await db.ToolTelemetryEvents.Select(e => e.ToolId).ToListAsync(), x => x == "push_delivery");
    }

    [Fact]
    public async Task DailyChallengeWorker_CreatesReminderAndPreventsDuplicate()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        db.Users.Add(new User { Id = userId, Email = "daily@example.com", CreatedAt = DateTime.UtcNow });
        db.DailyChallenges.Add(new DailyChallenge
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Date = DateTime.UtcNow.Date,
            SourceType = "fallback",
            SourceSkillTag = "retrieval",
            QuestionsJson = "[]",
            Status = "active",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var config = Config(("Workers:DailyChallenge:Enabled", "true"), ("Workers:DailyChallenge:BatchSize", "10"));
        var service = CreateDailyWorker(db, config);

        Assert.Equal(1, await service.RunOnceAsync());
        Assert.Equal(0, await service.RunOnceAsync());

        var notifications = await db.Notifications.Where(n => n.Type == "daily-challenge").ToListAsync();
        Assert.Single(notifications);
        Assert.Equal("DailyChallenge", notifications[0].RelatedEntityType);
    }

    [Fact]
    public async Task EducatorCore_DocContextWithoutCitationPersistsSourceCitationMissing()
    {
        await using var db = CreateDb();
        var (userId, topicId, sessionId) = await SeedLearningScopeAsync(db);
        var educator = CreateEducatorCore(db);

        var context = await educator.BuildTeacherContextAsync(
            userId,
            topicId,
            sessionId,
            "kaynakli anlat",
            notebookContext: "[doc:11111111-1111-1111-1111-111111111111:p1] Unique fact",
            wikiContext: "",
            learningSignalContext: "",
            rawYouTubeContext: null);

        await educator.RecordAnswerQualitySignalsAsync(userId, topicId, sessionId, "Genel bir cevap, ama kaynak etiketi yok.", context);

        Assert.True(await db.LearningSignals.AnyAsync(s =>
            s.SignalType == LearningSignalTypes.SourceCitationMissing &&
            s.UserId == userId &&
            s.TopicId == topicId));
    }

    [Fact]
    public async Task EducatorCore_DocCitationPreventsMissingSignal()
    {
        await using var db = CreateDb();
        var (userId, topicId, sessionId) = await SeedLearningScopeAsync(db);
        var educator = CreateEducatorCore(db);

        var context = await educator.BuildTeacherContextAsync(
            userId,
            topicId,
            sessionId,
            "kaynakli anlat",
            notebookContext: "[doc:11111111-1111-1111-1111-111111111111:p1] Unique fact",
            wikiContext: "",
            learningSignalContext: "",
            rawYouTubeContext: null);

        await educator.RecordAnswerQualitySignalsAsync(
            userId,
            topicId,
            sessionId,
            "Bu bilgi kaynakta geciyor. [doc:11111111-1111-1111-1111-111111111111:p1]",
            context);

        Assert.False(await db.LearningSignals.AnyAsync(s => s.SignalType == LearningSignalTypes.SourceCitationMissing));
    }

    [Fact]
    public async Task EducatorCore_YouTubeReferencePersistsTeachingMoveApplied()
    {
        await using var db = CreateDb();
        var (userId, topicId, sessionId) = await SeedLearningScopeAsync(db);
        var educator = CreateEducatorCore(db);

        var context = await educator.BuildTeacherContextAsync(
            userId,
            topicId,
            sessionId,
            "video akisi",
            notebookContext: "",
            wikiContext: "",
            learningSignalContext: "",
            rawYouTubeContext: "[youtube:abc123xyz] YouTube transcript: Once sezgi kurulur. Mesela async akisi ornekle anlatilir. Bu hataya dikkat edilir.");

        await educator.RecordAnswerQualitySignalsAsync(userId, topicId, sessionId, "Once sezgi, sonra ornek.", context);

        Assert.True(await db.LearningSignals.AnyAsync(s =>
            s.SignalType == LearningSignalTypes.TeachingMoveApplied &&
            s.UserId == userId &&
            s.TopicId == topicId));
    }

    private static SrsReminderWorkerService CreateSrsWorker(OrkaDbContext db, IConfiguration config)
    {
        var telemetry = new RuntimeTelemetryService(db, NullLogger<RuntimeTelemetryService>.Instance);
        var notifications = new NotificationService(db);
        var push = new PushDeliveryService(EmptyConfig(), telemetry, NullLogger<PushDeliveryService>.Instance);
        return new SrsReminderWorkerService(db, notifications, push, telemetry, config, NullLogger<SrsReminderWorkerService>.Instance);
    }

    private static DailyChallengeWorkerService CreateDailyWorker(OrkaDbContext db, IConfiguration config)
    {
        var telemetry = new RuntimeTelemetryService(db, NullLogger<RuntimeTelemetryService>.Instance);
        var notifications = new NotificationService(db);
        var push = new PushDeliveryService(EmptyConfig(), telemetry, NullLogger<PushDeliveryService>.Instance);
        return new DailyChallengeWorkerService(db, notifications, push, telemetry, config, NullLogger<DailyChallengeWorkerService>.Instance);
    }

    private static EducatorCoreService CreateEducatorCore(OrkaDbContext db)
    {
        var redis = new TestRedisMemoryService();
        var signals = new LearningSignalService(db, redis, NullLogger<LearningSignalService>.Instance);
        return new EducatorCoreService(redis, signals, NullLogger<EducatorCoreService>.Instance);
    }

    private static async Task<(Guid userId, Guid topicId, Guid sessionId)> SeedLearningScopeAsync(OrkaDbContext db)
    {
        var userId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        db.Users.Add(new User { Id = userId, Email = $"{userId:N}@example.com", CreatedAt = DateTime.UtcNow });
        db.Topics.Add(new Topic { Id = topicId, UserId = userId, Title = "Grounding", CreatedAt = DateTime.UtcNow });
        db.Sessions.Add(new Session
        {
            Id = sessionId,
            UserId = userId,
            TopicId = topicId,
            CreatedAt = DateTime.UtcNow,
            SessionNumber = 1
        });
        await db.SaveChangesAsync();
        return (userId, topicId, sessionId);
    }

    private static IConfiguration EmptyConfig() => Config();

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(x => x.Key, x => (string?)x.Value))
            .Build();

    private static OrkaDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<OrkaDbContext>()
            .UseInMemoryDatabase($"workers-{Guid.NewGuid():N}")
            .Options;
        return new OrkaDbContext(options);
    }

    private sealed class TestRedisMemoryService : IRedisMemoryService
    {
        private readonly Dictionary<string, string> _json = new(StringComparer.OrdinalIgnoreCase);

        public Task RecordEvaluationAsync(Guid sessionId, int score, string feedback) => Task.CompletedTask;
        public Task<IEnumerable<string>> GetRecentFeedbackAsync(Guid sessionId, int count = 5) => Task.FromResult<IEnumerable<string>>([]);
        public Task<bool> CheckRateLimitAsync(string clientIp, int maxRequests, TimeSpan window) => Task.FromResult(true);
        public Task SetGlobalPolicyAsync(string policyText) => Task.CompletedTask;
        public Task<string> GetGlobalPolicyAsync() => Task.FromResult(string.Empty);
        public Task SetLastPistonResultAsync(Guid sessionId, string code, string stdout, string stderr, string language) => Task.CompletedTask;
        public Task<string> GetLastPistonResultAsync(Guid sessionId) => Task.FromResult(string.Empty);
        public Task SetWikiReadyAsync(Guid topicId) => Task.CompletedTask;
        public Task SaveGoldExampleAsync(Guid topicId, string userMessage, string agentResponse, int score) => Task.CompletedTask;
        public Task<IEnumerable<GoldExample>> GetGoldExamplesAsync(Guid topicId, int count = 2) => Task.FromResult<IEnumerable<GoldExample>>([]);
        public Task RecordAgentMetricAsync(string agentRole, long latencyMs, bool isSuccess, string? provider = null) => Task.CompletedTask;
        public Task<IEnumerable<AgentMetricSummary>> GetSystemMetricsAsync() => Task.FromResult<IEnumerable<AgentMetricSummary>>([]);
        public Task<IEnumerable<EvaluatorLogEntry>> GetRecentEvaluatorLogsAsync(int count = 20) => Task.FromResult<IEnumerable<EvaluatorLogEntry>>([]);
        public Task<IEnumerable<ProviderUsageStat>> GetProviderUsageAsync() => Task.FromResult<IEnumerable<ProviderUsageStat>>([]);
        public Task<string?> GetJsonAsync(string key) => Task.FromResult(_json.TryGetValue(key, out var value) ? value : null);
        public Task SetJsonAsync(string key, string payload, TimeSpan ttl) { _json[key] = payload; return Task.CompletedTask; }
        public Task DeleteKeyAsync(string key) { _json.Remove(key); return Task.CompletedTask; }
        public Task<long> GetTopicVersionAsync(Guid topicId) => Task.FromResult(1L);
        public Task<long> BumpTopicVersionAsync(Guid topicId, string reason) => Task.FromResult(2L);
        public Task InvalidateLearningCachesAsync(Guid userId, Guid topicId, string reason) => Task.CompletedTask;
        public Task RecordCacheMetricAsync(string area, bool hit, string? tool = null, double? latencyMs = null) => Task.CompletedTask;
        public Task<IEnumerable<CacheMetricSummary>> GetCacheMetricsAsync() => Task.FromResult<IEnumerable<CacheMetricSummary>>([]);
        public Task<RedisHealthDto> GetRedisHealthAsync() => Task.FromResult(new RedisHealthDto(true, 0, 0, "ok", null, DateTime.UtcNow));
        public Task<IReadOnlyList<string>> GetRecentQuestionHashesAsync(Guid userId, Guid topicId, int count = 80) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task RememberQuestionHashesAsync(Guid userId, Guid topicId, IEnumerable<string> hashes) => Task.CompletedTask;
        public Task RecordTopicScoreAsync(Guid topicId, int score, string feedback) => Task.CompletedTask;
        public Task<(double avgScore, int totalEvals)> GetTopicScoreAsync(Guid topicId) => Task.FromResult((0d, 0));
        public Task RecordStudentProfileAsync(Guid topicId, int understandingScore, string weaknesses) => Task.CompletedTask;
        public Task<(int score, string weaknesses)?> GetStudentProfileAsync(Guid topicId) => Task.FromResult<(int score, string weaknesses)?>(null);
        public Task SetLowQualityFeedbackAsync(Guid sessionId, int score, string feedback) => Task.CompletedTask;
        public Task<(int score, string feedback)?> GetAndClearLowQualityFeedbackAsync(Guid sessionId) => Task.FromResult<(int score, string feedback)?>(null);
        public Task SaveKorteksResearchReportAsync(Guid topicId, string report) => Task.CompletedTask;
        public Task<string?> GetKorteksResearchReportAsync(Guid topicId) => Task.FromResult<string?>(null);
        public Task SaveYouTubeContextAsync(Guid topicId, string payload) => Task.CompletedTask;
        public Task<string?> GetYouTubeContextAsync(Guid topicId) => Task.FromResult<string?>(null);
    }
}
