using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Orka.API.Controllers;
using Orka.Core.Constants;
using Orka.Core.DTOs;
using Orka.Core.DTOs.Code;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Xunit;

namespace Orka.API.Tests;

public sealed class ToolActivationTutorConsumptionTests
{
    [Fact]
    public async Task CodeController_CompileErrorIsStoredForTutorAndCreatesSignal()
    {
        var sessionId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var redis = new CapturingRedisMemoryService();
        var signals = new CapturingLearningSignalService();
        var controller = CreateController(
            new FakePistonService(new PistonResult(
                "",
                "CS1002 ; expected",
                false,
                Phase: "compile",
                CompileError: "CS1002 ; expected",
                SafeTutorSummary: "Derleme hatasi var; syntax kavramina odaklan.")),
            redis,
            signals,
            userId);

        var result = await controller.RunCode(new CodeRunRequest("Console.WriteLine(1)", "csharp", SessionId: sessionId, TopicId: topicId));

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<CodeRunResponse>(ok.Value);
        Assert.False(body.Success);
        Assert.Equal("compile", body.Phase);
        Assert.Equal("CS1002 ; expected", body.CompileError);

        Assert.NotNull(redis.LastPayload);
        Assert.Contains("compile", redis.LastPayload!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CS1002", redis.LastPayload!, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(LearningSignalTypes.IdeCompileError, signals.LastSignalType);
        Assert.Equal(topicId, signals.LastTopicId);
        Assert.Equal(sessionId, signals.LastSessionId);
    }

    [Fact]
    public async Task CodeController_RuntimeErrorIsStoredForTutorAndCreatesSignal()
    {
        var redis = new CapturingRedisMemoryService();
        var signals = new CapturingLearningSignalService();
        var controller = CreateController(
            new FakePistonService(new PistonResult(
                "",
                "IndexError: list index out of range",
                false,
                Phase: "run",
                RuntimeError: "IndexError: list index out of range",
                SafeTutorSummary: "Runtime hatasi var; indeks sorununu acikla.")),
            redis,
            signals,
            Guid.NewGuid());

        var result = await controller.RunCode(new CodeRunRequest("print([1][3])", "python", SessionId: Guid.NewGuid(), TopicId: Guid.NewGuid()));

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<CodeRunResponse>(ok.Value);
        Assert.False(body.Success);
        Assert.Equal("run", body.Phase);
        Assert.Equal("IndexError: list index out of range", body.RuntimeError);
        Assert.Equal(LearningSignalTypes.IdeRuntimeError, signals.LastSignalType);
        Assert.Contains("IndexError", redis.LastPayload!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CodeController_SuccessStoresStdoutForTutorAndPositiveSignal()
    {
        var redis = new CapturingRedisMemoryService();
        var signals = new CapturingLearningSignalService();
        var controller = CreateController(
            new FakePistonService(new PistonResult(
                "42",
                "",
                true,
                Phase: "run",
                SafeTutorSummary: "Kod basariyla calisti.")),
            redis,
            signals,
            Guid.NewGuid());

        var result = await controller.RunCode(new CodeRunRequest("print(42)", "python", SessionId: Guid.NewGuid(), TopicId: Guid.NewGuid()));

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<CodeRunResponse>(ok.Value);
        Assert.True(body.Success);
        Assert.Equal("42", body.Stdout);
        Assert.Equal(LearningSignalTypes.IdeRunCompleted, signals.LastSignalType);
        Assert.True(signals.LastIsPositive);
        Assert.Contains("stdout_redacted", redis.LastPayload!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CodeController_BlocksOversizedStdinBeforeProvider()
    {
        var controller = CreateController(new FakePistonService(new PistonResult("", "", true)), new CapturingRedisMemoryService(), new CapturingLearningSignalService(), Guid.NewGuid());

        var result = await controller.RunCode(new CodeRunRequest("print(1)", "python", Stdin: new string('x', 10_001)));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void TutorAgent_SourceContainsPistonPedagogyGuard()
    {
        var root = FindRepoRoot();
        var tutor = File.ReadAllText(Path.Combine(root, "Orka.Infrastructure", "Services", "TutorAgent.cs"));

        Assert.Contains("[SON KOD ÇIKTISI", tutor);
        Assert.Contains("Compile/derleme hatası", tutor);
        Assert.Contains("Runtime hatası", tutor);
        Assert.Contains("Kod çıktısını uydurma", tutor);
    }

    private static CodeController CreateController(
        IPistonService piston,
        CapturingRedisMemoryService redis,
        CapturingLearningSignalService signals,
        Guid userId)
    {
        var controller = new CodeController(piston, redis, signals, NullLogger<CodeController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, userId.ToString())
                    ], "test"))
                }
            }
        };
        return controller;
    }

    private sealed class FakePistonService : IPistonService
    {
        private readonly PistonResult _result;
        public FakePistonService(PistonResult result) => _result = result;
        public Task<PistonResult> ExecuteAsync(string code, string language = "csharp", string? stdin = null) => Task.FromResult(_result);
        public Task<IReadOnlyList<PistonRuntime>> GetRuntimesAsync() => Task.FromResult<IReadOnlyList<PistonRuntime>>([]);
    }

    private sealed class CapturingLearningSignalService : ILearningSignalService
    {
        public string? LastSignalType { get; private set; }
        public Guid? LastTopicId { get; private set; }
        public Guid? LastSessionId { get; private set; }
        public bool? LastIsPositive { get; private set; }

        public Task RecordQuizAnsweredAsync(QuizAttempt attempt, CancellationToken ct = default) => Task.CompletedTask;

        public Task RecordSignalAsync(Guid userId, Guid? topicId, Guid? sessionId, string signalType, string? skillTag = null, string? topicPath = null, int? score = null, bool? isPositive = null, string? payloadJson = null, CancellationToken ct = default)
        {
            LastSignalType = signalType;
            LastTopicId = topicId;
            LastSessionId = sessionId;
            LastIsPositive = isPositive;
            return Task.CompletedTask;
        }

        public Task<LearningTopicSummaryDto> GetTopicSummaryAsync(Guid userId, Guid topicId, CancellationToken ct = default) =>
            Task.FromResult(new LearningTopicSummaryDto(topicId, 0, 0, 0, [], []));

        public Task<IReadOnlyList<StudyRecommendationDto>> GetRecommendationsAsync(Guid userId, Guid topicId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<StudyRecommendationDto>>([]);
    }

    private sealed class CapturingRedisMemoryService : IRedisMemoryService
    {
        public string? LastPayload { get; private set; }

        public Task SetLastPistonResultAsync(Guid sessionId, string code, string stdout, string stderr, string language, string phase = "run", string? compileError = null, string? runtimeError = null, bool success = true, string? safeTutorSummary = null)
        {
            LastPayload = JsonSerializer.Serialize(new { sessionId, code, stdout, stderr, language, phase, compileError, runtimeError, success, safeTutorSummary });
            return Task.CompletedTask;
        }

        public Task<string> GetLastPistonResultAsync(Guid sessionId) => Task.FromResult(LastPayload ?? string.Empty);
        public Task RecordEvaluationAsync(Guid sessionId, int score, string feedback) => Task.CompletedTask;
        public Task<IEnumerable<string>> GetRecentFeedbackAsync(Guid sessionId, int count = 5) => Task.FromResult<IEnumerable<string>>([]);
        public Task<bool> CheckRateLimitAsync(string clientIp, int maxRequests, TimeSpan window) => Task.FromResult(true);
        public Task SetGlobalPolicyAsync(string policyText) => Task.CompletedTask;
        public Task<string> GetGlobalPolicyAsync() => Task.FromResult(string.Empty);
        public Task SetWikiReadyAsync(Guid topicId) => Task.CompletedTask;
        public Task SaveGoldExampleAsync(Guid userId, Guid topicId, string userMessage, string agentResponse, int score) => Task.CompletedTask;
        public Task<IEnumerable<GoldExample>> GetGoldExamplesAsync(Guid userId, Guid topicId, int count = 2) => Task.FromResult<IEnumerable<GoldExample>>([]);
        public Task RecordAgentMetricAsync(string agentRole, long latencyMs, bool isSuccess, string? provider = null) => Task.CompletedTask;
        public Task<IEnumerable<AgentMetricSummary>> GetSystemMetricsAsync() => Task.FromResult<IEnumerable<AgentMetricSummary>>([]);
        public Task<IEnumerable<EvaluatorLogEntry>> GetRecentEvaluatorLogsAsync(int count = 20) => Task.FromResult<IEnumerable<EvaluatorLogEntry>>([]);
        public Task<IEnumerable<ProviderUsageStat>> GetProviderUsageAsync() => Task.FromResult<IEnumerable<ProviderUsageStat>>([]);
        public Task<string?> GetJsonAsync(string key) => Task.FromResult<string?>(null);
        public Task SetJsonAsync(string key, string payload, TimeSpan ttl) => Task.CompletedTask;
        public Task AddStreamEventAsync(string key, IReadOnlyDictionary<string, string> values, TimeSpan? ttl = null) => Task.CompletedTask;
        public Task<IReadOnlyList<RedisStreamEventDto>> ReadStreamEventsAsync(string key, string afterId = "0-0", int count = 50) => Task.FromResult<IReadOnlyList<RedisStreamEventDto>>([]);
        public Task<bool> EnsureConsumerGroupAsync(string key, string group, string startId = "0-0") => Task.FromResult(true);
        public Task<IReadOnlyList<RedisStreamEventDto>> ReadConsumerGroupAsync(string key, string group, string consumer, int count = 50, string streamId = ">") => Task.FromResult<IReadOnlyList<RedisStreamEventDto>>([]);
        public Task AckStreamEventsAsync(string key, string group, IEnumerable<string> eventIds) => Task.CompletedTask;
        public Task<bool> SupportsVectorSearchAsync() => Task.FromResult(false);
        public Task DeleteKeyAsync(string key) => Task.CompletedTask;
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
        public Task<bool> AcquireLockAsync(string key, string value, TimeSpan expiry) => Task.FromResult(true);
        public Task<bool> RenewLockAsync(string key, string value, TimeSpan expiry) => Task.FromResult(true);
        public Task ReleaseLockAsync(string key, string value) => Task.CompletedTask;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Orka.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
