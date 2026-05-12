using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Exceptions;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.API.Tests;

public sealed class AiReliabilityTests
{
    [Fact]
    public async Task CompleteChat_AuthenticationFailure_DoesNotFallback()
    {
        var providers = new FakeProviders();
        providers.GitHubHandler = () => throw new AiProviderCallException(
            "GitHubModels",
            "primary-model",
            "Tutor",
            AiProviderFailureKind.Authentication,
            "auth failed",
            HttpStatusCode.Unauthorized,
            isFallbackable: false,
            redactedDiagnostic: "apiKey=[REDACTED]");

        var telemetry = new RecordingAiTelemetry();
        var factory = CreateFactory(providers, telemetry: telemetry);

        var exception = await Assert.ThrowsAsync<AiProviderCallException>(() =>
            factory.CompleteChatAsync(AgentRole.Tutor, "system", "hello"));

        Assert.Equal(AiProviderFailureKind.Authentication, exception.FailureKind);
        Assert.Equal(1, providers.GitHubCalls);
        Assert.Equal(0, providers.GroqCalls);
        Assert.DoesNotContain("secret", exception.RedactedDiagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(telemetry.Events, e => e.FailureKind == AiProviderFailureKind.Authentication && e.AttemptIndex == 1);
    }

    [Fact]
    public async Task CompleteChat_RateLimit_FallsBackWithinMaxAttempts()
    {
        var providers = new FakeProviders();
        providers.GitHubHandler = () => throw new AiProviderCallException(
            "GitHubModels",
            "primary-model",
            "Tutor",
            AiProviderFailureKind.RateLimited,
            "rate limited",
            HttpStatusCode.TooManyRequests,
            isRetryable: true,
            isFallbackable: true);
        providers.GroqHandler = () => Task.FromResult("groq-ok");

        var telemetry = new RecordingAiTelemetry();
        var factory = CreateFactory(providers, telemetry: telemetry);

        var result = await factory.CompleteChatAsync(AgentRole.Tutor, "system", "hello");

        Assert.Equal("groq-ok", result);
        Assert.Equal(1, providers.GitHubCalls);
        Assert.Equal(1, providers.GroqCalls);
        Assert.Equal(0, providers.MistralCalls);
        Assert.Contains(telemetry.Events, e => e.Provider == "Groq" && e.Success && e.FallbackUsed);
    }

    [Fact]
    public async Task CompleteChat_MaxAttempts_PreventsLongFallbackChain()
    {
        var providers = new FakeProviders();
        providers.GitHubHandler = () => throw ServerError("GitHubModels");
        providers.GroqHandler = () => throw ServerError("Groq");
        providers.MistralHandler = () => Task.FromResult("should-not-run");

        var factory = CreateFactory(providers);

        var exception = await Assert.ThrowsAsync<AiProviderCallException>(() =>
            factory.CompleteChatAsync(AgentRole.Tutor, "system", "hello"));

        Assert.Equal(AiProviderFailureKind.ServerError, exception.FailureKind);
        Assert.Equal(1, providers.GitHubCalls);
        Assert.Equal(1, providers.GroqCalls);
        Assert.Equal(0, providers.MistralCalls);
    }

    [Fact]
    public async Task CompleteChat_QuotaDenied_DoesNotCallProvider()
    {
        var providers = new FakeProviders();
        var budget = new StaticBudget(Allowed: false);
        var telemetry = new RecordingAiTelemetry();
        var factory = CreateFactory(providers, budget, telemetry);

        await Assert.ThrowsAsync<DailyLimitExceededException>(() =>
            factory.CompleteChatAsync(AgentRole.Tutor, "system", "hello"));

        Assert.Equal(0, providers.GitHubCalls);
        Assert.Contains(telemetry.Events, e => e.QuotaHit && e.FailureKind == AiProviderFailureKind.QuotaExceeded);
    }

    [Fact]
    public async Task CompleteChat_WithAiRequestContext_PassesUserAndSessionToBudgetAndCost()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var context = new AsyncLocalAiRequestContextAccessor();
        var budget = new StaticBudget(Allowed: true);
        var runtime = new RecordingRuntimeTelemetry();
        var providers = new FakeProviders();
        var factory = CreateFactory(providers, budget: budget, runtime: runtime, context: context);

        using (context.Push(new AiRequestContext(UserId: userId, SessionId: sessionId, TopicId: topicId, Source: "test")))
        {
            var result = await factory.CompleteChatAsync(AgentRole.Tutor, "system", "hello");
            Assert.Equal("github-ok", result);
        }

        Assert.Equal(userId, budget.LastRequest?.UserId);
        Assert.Contains(runtime.Costs, c => c.UserId == userId && c.SessionId == sessionId && c.TopicId == topicId);
    }

    [Fact]
    public async Task CompleteChat_WithoutAiRequestContext_UsesGlobalBudgetOnly()
    {
        var budget = new StaticBudget(Allowed: true);
        var factory = CreateFactory(new FakeProviders(), budget: budget);

        await factory.CompleteChatAsync(AgentRole.Tutor, "system", "hello");

        Assert.Null(budget.LastRequest?.UserId);
    }

    [Fact]
    public void AiRequestContextAccessor_NestedPushRestoresPreviousContext()
    {
        var accessor = new AsyncLocalAiRequestContextAccessor();
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        using (accessor.Push(new AiRequestContext(UserId: userId, Source: "outer")))
        {
            using (accessor.Push(new AiRequestContext(SessionId: sessionId, IsBackground: true)))
            {
                Assert.Equal(userId, accessor.Current.UserId);
                Assert.Equal(sessionId, accessor.Current.SessionId);
                Assert.True(accessor.Current.IsBackground);
            }

            Assert.Equal(userId, accessor.Current.UserId);
            Assert.Null(accessor.Current.SessionId);
            Assert.False(accessor.Current.IsBackground);
        }

        Assert.Null(accessor.Current.UserId);
    }

    [Fact]
    public async Task ProviderFailureMapper_RedactsSecretDiagnostics()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"apiKey":"sk-secret-value-that-should-not-appear","email":"demo@example.com"}""")
        };

        var exception = AiProviderFailureMapperForTests.FromResponse("Test", "model", response, await response.Content.ReadAsStringAsync());

        Assert.Equal(AiProviderFailureKind.Authentication, exception.FailureKind);
        Assert.DoesNotContain("sk-secret-value", exception.RedactedDiagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("demo@example.com", exception.RedactedDiagnostic, StringComparison.OrdinalIgnoreCase);
    }

    private static AiProviderCallException ServerError(string provider) =>
        new(provider, "model", "Tutor", AiProviderFailureKind.ServerError, "server error", HttpStatusCode.InternalServerError, isRetryable: true, isFallbackable: true);

    private static AIAgentFactory CreateFactory(
        FakeProviders providers,
        IAiUsageBudgetService? budget = null,
        IAiProviderTelemetryService? telemetry = null,
        IRuntimeTelemetryService? runtime = null,
        IAiRequestContextAccessor? context = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:AgentRouting:Tutor:Provider"] = "GitHubModels",
                ["AI:AgentRouting:Tutor:Model"] = "primary-model",
                ["AI:GitHubModels:Token"] = "test-github-token",
                ["AI:Groq:ApiKey"] = "test-groq-key",
                ["AI:Mistral:ApiKey"] = "test-mistral-key",
                ["AI:Gemini:ApiKey"] = "test-gemini-key",
                ["AI:OpenRouter:ApiKey"] = "test-openrouter-key",
                ["AI:Cerebras:ApiKey"] = "test-cerebras-key",
                ["AI:SambaNova:ApiKey"] = "test-sambanova-key",
                ["AI:Reliability:MaxAttemptsPerRequest"] = "2",
                ["AI:Reliability:FallbackEnabled"] = "true",
                ["AI:Reliability:ProviderCooldownSeconds"] = "60",
                ["AI:Cost:RoleBudgets:Tutor:MaxOutputTokens"] = "256"
            })
            .Build();

        return new AIAgentFactory(
            providers,
            providers,
            providers,
            providers,
            providers,
            providers,
            providers,
            new NullRedisMemoryService(),
            new NoopBackgroundTaskQueue(),
            runtime ?? new RecordingRuntimeTelemetry(),
            telemetry ?? new RecordingAiTelemetry(),
            budget ?? new StaticBudget(Allowed: true),
            new AiProviderCircuitBreaker(),
            context ?? new AsyncLocalAiRequestContextAccessor(),
            configuration,
            NullLogger<AIAgentFactory>.Instance);
    }

    private sealed class FakeProviders : IGitHubModelsService, IGroqService, IGeminiService, IOpenRouterService, ICerebrasService, IMistralService, ISambaNovaService
    {
        public int GitHubCalls;
        public int GroqCalls;
        public int MistralCalls;
        public Func<Task<string>> GitHubHandler { get; set; } = () => Task.FromResult("github-ok");
        public Func<Task<string>> GroqHandler { get; set; } = () => Task.FromResult("groq-ok");
        public Func<Task<string>> MistralHandler { get; set; } = () => Task.FromResult("mistral-ok");

        public Task<string> ChatAsync(string systemPrompt, string userMessage, string model, CancellationToken ct = default)
        {
            GitHubCalls++;
            return GitHubHandler();
        }

        public async IAsyncEnumerable<string> ChatStreamAsync(string systemPrompt, string userMessage, string model, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return await ChatAsync(systemPrompt, userMessage, model, ct);
        }

        public Task<string> GenerateResponseAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
        {
            GroqCalls++;
            return GroqHandler();
        }

        public async IAsyncEnumerable<string> GenerateResponseStreamAsync(string systemPrompt, string userMessage, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return await GenerateResponseAsync(systemPrompt, userMessage, ct);
        }

        public Task<string> GenerateSmartAsync(string systemPrompt, string userMessage, CancellationToken ct = default) => GenerateResponseAsync(systemPrompt, userMessage, ct);
        public Task<string> GenerateWithModelAsync(string model, string systemPrompt, string userMessage, CancellationToken ct = default) => GenerateResponseAsync(systemPrompt, userMessage, ct);
        public IAsyncEnumerable<string> StreamSmartAsync(string systemPrompt, string userMessage, CancellationToken ct = default) => GenerateResponseStreamAsync(systemPrompt, userMessage, ct);
        public Task<string> ChatCompletionAsync(string systemPrompt, string userMessage, string? model = null, CancellationToken ct = default) => GenerateResponseAsync(systemPrompt, userMessage, ct);
        public Task<string> ChatCompletionWithKeyAsync(string systemPrompt, string userMessage, string? model = null, string? apiKey = null, CancellationToken ct = default) => GenerateResponseAsync(systemPrompt, userMessage, ct);
        public IAsyncEnumerable<string> GenerateResponseStreamWithKeyAsync(string systemPrompt, string userMessage, string? model = null, string? apiKey = null, CancellationToken ct = default) => GenerateResponseStreamAsync(systemPrompt, userMessage, ct);
        public Task<string> GetResponseAsync(IEnumerable<Message> context, string systemPrompt, CancellationToken ct = default) => GenerateResponseAsync(systemPrompt, string.Empty, ct);
        public IAsyncEnumerable<string> GetResponseStreamAsync(IEnumerable<Message> context, string systemPrompt, CancellationToken ct = default) => GenerateResponseStreamAsync(systemPrompt, string.Empty, ct);
        public Task<string> SummarizeSessionAsync(IEnumerable<Message> messages) => GenerateResponseAsync(string.Empty, string.Empty);
        public Task<RoutingResult> SemanticRouteAsync(string message, string? currentPhase = "Discovery") => Task.FromResult(new RoutingResult { Intent = "general", Method = "fake" });
        public Task<string> ResearchAsync(string topic, string depth = "normal") => GenerateResponseAsync(string.Empty, topic);
        public Task<string> GeneratePlanAsync(string topicTitle, string intent = "genel", string level = "orta") => GenerateResponseAsync(string.Empty, topicTitle);
        public Task<string> ExtractWikiBlocksAsync(string conversation, string topicTitle) => GenerateResponseAsync(string.Empty, conversation);
        public Task<string> GenerateReinforcementQuestionsAsync(string content) { MistralCalls++; return GenerateResponseAsync(string.Empty, content); }
    }

    private sealed class StaticBudget(bool Allowed) : IAiUsageBudgetService
    {
        public AiUsageBudgetRequest? LastRequest { get; private set; }

        public Task<AiUsageBudgetDecision> CheckAsync(AiUsageBudgetRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(new AiUsageBudgetDecision(Allowed, Allowed ? "allowed" : "test_quota", 10, request.MaxOutputTokens, 10 + request.MaxOutputTokens, 0.001m));
        }
    }

    private sealed class RecordingAiTelemetry : IAiProviderTelemetryService
    {
        public ConcurrentQueue<AiProviderTelemetryEvent> Events { get; } = new();
        public Task RecordAsync(AiProviderTelemetryEvent telemetryEvent, CancellationToken ct = default)
        {
            Events.Enqueue(telemetryEvent);
            return Task.CompletedTask;
        }

        public Task<AiProviderTelemetrySummary> GetSummaryAsync(CancellationToken ct = default) =>
            Task.FromResult(new AiProviderTelemetrySummary(0, 0, new Dictionary<string, int>(), new Dictionary<string, int>()));
    }

    private sealed class RecordingRuntimeTelemetry : IRuntimeTelemetryService
    {
        public ConcurrentQueue<CostRecordRequest> Costs { get; } = new();

        public Task RecordToolEventAsync(ToolTelemetryEventRequest request, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordCostAsync(CostRecordRequest request, CancellationToken ct = default)
        {
            Costs.Enqueue(request);
            return Task.CompletedTask;
        }
    }

    private sealed class NoopBackgroundTaskQueue : IBackgroundTaskQueue
    {
        public ValueTask QueueAsync(BackgroundTaskItem item, CancellationToken ct = default) => ValueTask.CompletedTask;
    }

    private sealed class NullRedisMemoryService : IRedisMemoryService
    {
        private readonly ConcurrentDictionary<string, string> _json = new();
        public Task RecordEvaluationAsync(Guid sessionId, int score, string feedback) => Task.CompletedTask;
        public Task<IEnumerable<string>> GetRecentFeedbackAsync(Guid sessionId, int count = 5) => Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
        public Task<bool> CheckRateLimitAsync(string clientIp, int maxRequests, TimeSpan window) => Task.FromResult(true);
        public Task SetGlobalPolicyAsync(string policyText) => Task.CompletedTask;
        public Task<string> GetGlobalPolicyAsync() => Task.FromResult(string.Empty);
        public Task SetLastPistonResultAsync(Guid sessionId, string code, string stdout, string stderr, string language, string phase = "run", string? compileError = null, string? runtimeError = null, bool success = true, string? safeTutorSummary = null) => Task.CompletedTask;
        public Task<string> GetLastPistonResultAsync(Guid sessionId) => Task.FromResult(string.Empty);
        public Task SetWikiReadyAsync(Guid topicId) => Task.CompletedTask;
        public Task SaveGoldExampleAsync(Guid topicId, string userMessage, string agentResponse, int score) => Task.CompletedTask;
        public Task<IEnumerable<GoldExample>> GetGoldExamplesAsync(Guid topicId, int count = 2) => Task.FromResult<IEnumerable<GoldExample>>(Array.Empty<GoldExample>());
        public Task RecordAgentMetricAsync(string agentRole, long latencyMs, bool isSuccess, string? provider = null) => Task.CompletedTask;
        public Task<IEnumerable<AgentMetricSummary>> GetSystemMetricsAsync() => Task.FromResult<IEnumerable<AgentMetricSummary>>(Array.Empty<AgentMetricSummary>());
        public Task<IEnumerable<EvaluatorLogEntry>> GetRecentEvaluatorLogsAsync(int count = 20) => Task.FromResult<IEnumerable<EvaluatorLogEntry>>(Array.Empty<EvaluatorLogEntry>());
        public Task<IEnumerable<ProviderUsageStat>> GetProviderUsageAsync() => Task.FromResult<IEnumerable<ProviderUsageStat>>(Array.Empty<ProviderUsageStat>());
        public Task<string?> GetJsonAsync(string key) => Task.FromResult(_json.TryGetValue(key, out var value) ? value : null);
        public Task SetJsonAsync(string key, string payload, TimeSpan ttl) { _json[key] = payload; return Task.CompletedTask; }
        public Task AddStreamEventAsync(string key, IReadOnlyDictionary<string, string> values, TimeSpan? ttl = null) => Task.CompletedTask;
        public Task<IReadOnlyList<RedisStreamEventDto>> ReadStreamEventsAsync(string key, string afterId = "0-0", int count = 50) => Task.FromResult<IReadOnlyList<RedisStreamEventDto>>(Array.Empty<RedisStreamEventDto>());
        public Task<bool> EnsureConsumerGroupAsync(string key, string group, string startId = "0-0") => Task.FromResult(true);
        public Task<IReadOnlyList<RedisStreamEventDto>> ReadConsumerGroupAsync(string key, string group, string consumer, int count = 50, string streamId = ">") => Task.FromResult<IReadOnlyList<RedisStreamEventDto>>(Array.Empty<RedisStreamEventDto>());
        public Task AckStreamEventsAsync(string key, string group, IEnumerable<string> eventIds) => Task.CompletedTask;
        public Task<bool> SupportsVectorSearchAsync() => Task.FromResult(false);
        public Task DeleteKeyAsync(string key) { _json.TryRemove(key, out _); return Task.CompletedTask; }
        public Task<long> GetTopicVersionAsync(Guid topicId) => Task.FromResult(0L);
        public Task<long> BumpTopicVersionAsync(Guid topicId, string reason) => Task.FromResult(1L);
        public Task InvalidateLearningCachesAsync(Guid userId, Guid topicId, string reason) => Task.CompletedTask;
        public Task RecordCacheMetricAsync(string area, bool hit, string? tool = null, double? latencyMs = null) => Task.CompletedTask;
        public Task<IEnumerable<CacheMetricSummary>> GetCacheMetricsAsync() => Task.FromResult<IEnumerable<CacheMetricSummary>>(Array.Empty<CacheMetricSummary>());
        public Task<RedisHealthDto> GetRedisHealthAsync() => Task.FromResult(new RedisHealthDto(false, 0, 0, "test", null, DateTime.UtcNow));
        public Task<IReadOnlyList<string>> GetRecentQuestionHashesAsync(Guid userId, Guid topicId, int count = 80) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task RememberQuestionHashesAsync(Guid userId, Guid topicId, IEnumerable<string> hashes) => Task.CompletedTask;
        public Task RecordTopicScoreAsync(Guid topicId, int score, string feedback) => Task.CompletedTask;
        public Task<(double avgScore, int totalEvals)> GetTopicScoreAsync(Guid topicId) => Task.FromResult((0d, 0));
        public Task RecordStudentProfileAsync(Guid topicId, int understandingScore, string weaknesses) => Task.CompletedTask;
        public Task<(int score, string weaknesses)?> GetStudentProfileAsync(Guid topicId) => Task.FromResult<(int, string)?>(null);
        public Task SetLowQualityFeedbackAsync(Guid sessionId, int score, string feedback) => Task.CompletedTask;
        public Task<(int score, string feedback)?> GetAndClearLowQualityFeedbackAsync(Guid sessionId) => Task.FromResult<(int, string)?>(null);
        public Task SaveKorteksResearchReportAsync(Guid topicId, string report) => Task.CompletedTask;
        public Task<string?> GetKorteksResearchReportAsync(Guid topicId) => Task.FromResult<string?>(null);
        public Task SaveYouTubeContextAsync(Guid topicId, string payload) => Task.CompletedTask;
        public Task<string?> GetYouTubeContextAsync(Guid topicId) => Task.FromResult<string?>(null);
    }
}

internal static class AiProviderFailureMapperForTests
{
    public static AiProviderCallException FromResponse(string provider, string? model, HttpResponseMessage response, string? body)
    {
        var mapperType = typeof(AIAgentFactory).Assembly.GetType("Orka.Infrastructure.Services.AiProviderFailureMapper", throwOnError: true)!;
        var method = mapperType.GetMethod(
            "FromResponse",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        return (AiProviderCallException)method!.Invoke(null, new object?[] { provider, model, response, body })!;
    }
}
