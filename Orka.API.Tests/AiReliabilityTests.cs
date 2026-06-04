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
        Assert.Contains(telemetry.Events, e => e.Provider == "OpenRouter" && e.Success && e.FallbackUsed);
    }

    [Fact]
    public async Task CompleteChat_QuizProviderInfraFailure_FallsBackToGitHubModels()
    {
        var providers = new FakeProviders();
        providers.GeminiHandler = () => throw new AiProviderCallException(
            "Gemini",
            "gemini-3.1-pro-preview",
            "Quiz",
            AiProviderFailureKind.Timeout,
            "timeout",
            isRetryable: true,
            isFallbackable: true);
        providers.GitHubHandler = () => Task.FromResult("""[{"assessmentItemId":"00000000-0000-0000-0000-000000000001"}]""");

        var telemetry = new RecordingAiTelemetry();
        var factory = CreateFactory(
            providers,
            telemetry: telemetry,
            overrides: new Dictionary<string, string?>
            {
                ["AI:AgentRouting:Quiz:Provider"] = "Gemini",
                ["AI:AgentRouting:Quiz:Model"] = "gemini-3.1-pro-preview",
                ["AI:GitHubModels:Agents:Quiz:Model"] = "openai/gpt-4o",
                ["AI:Cost:RoleBudgets:Quiz:MaxOutputTokens"] = "32768"
            });

        var result = await factory.CompleteChatAsync(AgentRole.Quiz, "quiz contract", "make quiz");

        Assert.Contains("assessmentItemId", result);
        Assert.Equal(1, providers.GeminiCalls);
        Assert.Equal(1, providers.GitHubCalls);
        Assert.Contains(telemetry.Events, e => e.Provider == "GitHubModels" && e.Model == "openai/gpt-4o" && e.Success && e.FallbackUsed);
    }

    [Fact]
    public async Task CompleteChat_DeepPlanProviderInfraFailure_FallsBackToGitHubModels()
    {
        var providers = new FakeProviders();
        providers.GeminiHandler = () => throw new AiProviderCallException(
            "Gemini",
            "gemini-3.1-pro-preview",
            "DeepPlan",
            AiProviderFailureKind.TransientNetwork,
            "tls failure",
            isRetryable: true,
            isFallbackable: true);
        providers.GitHubHandler = () => Task.FromResult("""{"topics":[{"title":"Professional scope","lessons":[{"title":"Measurable concept step"}]}]}""");

        var telemetry = new RecordingAiTelemetry();
        var factory = CreateFactory(
            providers,
            telemetry: telemetry,
            overrides: new Dictionary<string, string?>
            {
                ["AI:AgentRouting:DeepPlan:Provider"] = "Gemini",
                ["AI:AgentRouting:DeepPlan:Model"] = "gemini-3.1-pro-preview",
                ["AI:Cost:RoleBudgets:DeepPlan:MaxOutputTokens"] = "16384"
            });

        var result = await factory.CompleteChatAsync(AgentRole.DeepPlan, "deep plan contract", "build curriculum");

        Assert.Contains("Professional scope", result);
        Assert.Equal(1, providers.GeminiCalls);
        Assert.Equal(1, providers.GitHubCalls);
        Assert.Equal("openai/gpt-4o", providers.LastGitHubModel);
        Assert.Contains(telemetry.Events, e =>
            e.Provider == "Gemini" &&
            e.Role == nameof(AgentRole.DeepPlan) &&
            e.FailureKind == AiProviderFailureKind.TransientNetwork &&
            e.CircuitState == "closed");
        Assert.Contains(telemetry.Events, e => e.Provider == "GitHubModels" && e.Model == "openai/gpt-4o" && e.Success && e.FallbackUsed);
    }

    [Fact]
    public async Task CompleteChat_QuizProviderFailures_DoNotUseInMemoryFallback()
    {
        var providers = new FakeProviders();
        providers.GeminiHandler = () => throw ServerError("Gemini", "Quiz");
        providers.GitHubHandler = () => throw ServerError("GitHubModels", "Quiz");

        var factory = CreateFactory(
            providers,
            overrides: new Dictionary<string, string?>
            {
                ["AI:AgentRouting:Quiz:Provider"] = "Gemini",
                ["AI:AgentRouting:Quiz:Model"] = "gemini-3.1-pro-preview",
                ["AI:GitHubModels:Agents:Quiz:Model"] = "openai/gpt-4o",
                ["AI:Cost:RoleBudgets:Quiz:MaxOutputTokens"] = "32768"
            });

        var exception = await Assert.ThrowsAsync<AiProviderCallException>(() =>
            factory.CompleteChatAsync(AgentRole.Quiz, "quiz contract", "make quiz"));

        Assert.Equal(AiProviderFailureKind.ServerError, exception.FailureKind);
        Assert.Equal(1, providers.GeminiCalls);
        Assert.Equal(1, providers.GitHubCalls);
    }

    [Fact]
    public void CircuitBreaker_ThresholdAndRoleKey_IsolatesFailures()
    {
        var breaker = new AiProviderCircuitBreaker();
        var quizKey = "Gemini:gemini-3.1-pro-preview:Quiz";
        var tutorKey = "Gemini:gemini-3.1-pro-preview:Tutor";

        breaker.RecordFailure(quizKey, TimeSpan.FromMinutes(1), failureThreshold: 2);

        Assert.False(breaker.IsOpen(quizKey));
        Assert.False(breaker.IsOpen(tutorKey));

        breaker.RecordFailure(quizKey, TimeSpan.FromMinutes(1), failureThreshold: 2);

        Assert.True(breaker.IsOpen(quizKey));
        Assert.False(breaker.IsOpen(tutorKey));

        breaker.RecordSuccess(quizKey);

        Assert.False(breaker.IsOpen(quizKey));
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
            Content = new StringContent("""
                {
                  "apiKey":"sk-secret-value-that-should-not-appear",
                  "email":"demo@example.com",
                  "rawPrompt":"hidden system prompt",
                  "rawProviderPayload":"provider body",
                  "rawSourceChunk":"source private paragraph: confidential lesson material",
                  "debugTrace":"stackTrace C:\\secret\\file.txt",
                  "answerKey":"A",
                  "correctAnswer":"A",
                  "studentPrivateNote":"student private note: my family phone is 555-111-2222"
                }
                """)
        };

        var rawBody = await response.Content.ReadAsStringAsync();
        var exception = AiProviderFailureMapperForTests.FromResponse("Test", "model", response, rawBody);

        Assert.Equal(AiProviderFailureKind.Authentication, exception.FailureKind);
        Assert.Contains("provider=Test", exception.RedactedDiagnostic);
        Assert.Contains("status=401", exception.RedactedDiagnostic);
        Assert.Contains("category=authentication", exception.RedactedDiagnostic);
        Assert.Contains("bodyLength=", exception.RedactedDiagnostic);
        Assert.Contains("bodyHash=sha256:", exception.RedactedDiagnostic);
        Assert.DoesNotContain("sk-secret-value", exception.RedactedDiagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("demo@example.com", exception.RedactedDiagnostic, StringComparison.OrdinalIgnoreCase);
        AssertNoUnsafeProviderDiagnosticContent(exception.RedactedDiagnostic);
    }

    [Fact]
    public void ProviderFailureMapper_ExceptionDiagnostic_DoesNotRetainExceptionMessage()
    {
        var exception = AiProviderFailureMapperForTests.FromException(
            "OpenRouter",
            "model",
            new InvalidOperationException("rawProviderPayload rawPrompt source private paragraph: confidential lesson material C:\\secret\\file.txt stackTrace"));

        Assert.Equal(AiProviderFailureKind.InvalidResponse, exception.FailureKind);
        Assert.Contains("provider=OpenRouter", exception.RedactedDiagnostic);
        Assert.Contains("exceptionType=InvalidOperationException", exception.RedactedDiagnostic);
        Assert.DoesNotContain("InvalidOperationException: rawProviderPayload", exception.RedactedDiagnostic, StringComparison.OrdinalIgnoreCase);
        AssertNoUnsafeProviderDiagnosticContent(exception.RedactedDiagnostic);
    }




    [Fact]
    public async Task CohereEmbeddingService_FailureDiagnostic_DoesNotRetainProviderBody()
    {
        var service = new CohereEmbeddingService(
            new StaticHttpClientFactory(HttpStatusCode.Unauthorized, UnsafeProviderBody()),
            ProviderConfig("AI:Cohere:ApiKey", "test-key", "AI:Cohere:EmbedModel", "embed-test"),
            NullLogger<CohereEmbeddingService>.Instance);

        var exception = await Assert.ThrowsAsync<AiProviderCallException>(() =>
            service.EmbedAsync("harmless synthetic text"));

        Assert.Equal(AiProviderFailureKind.Authentication, exception.FailureKind);
        Assert.Contains("provider=CohereEmbed", exception.RedactedDiagnostic);
        Assert.Contains("status=401", exception.RedactedDiagnostic);
        AssertNoUnsafeProviderDiagnosticContent(exception.RedactedDiagnostic);
    }

    private static AiProviderCallException ServerError(string provider, string role = "Tutor") =>
        new(provider, "model", role, AiProviderFailureKind.ServerError, "server error", HttpStatusCode.InternalServerError, isRetryable: true, isFallbackable: true);

    private static IConfiguration ProviderConfig(params string?[] values)
    {
        var pairs = new Dictionary<string, string?>();
        for (var i = 0; i + 1 < values.Length; i += 2)
            pairs[values[i]!] = values[i + 1];

        return new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();
    }

    private static string UnsafeProviderBody() => """
        {
          "rawProviderPayload":"provider body",
          "rawPrompt":"hidden system prompt",
          "rawSourceChunk":"source private paragraph: confidential lesson material",
          "rawToolPayload":"tool output",
          "debugTrace":"stackTrace C:\\secret\\file.txt",
          "apiKey":"sk-secret-value",
          "answerKey":"A",
          "correctAnswer":"A",
          "ownerId":"owner-123",
          "userId":"user-123",
          "studentPrivateNote":"student private note: my family phone is 555-111-2222"
        }
        """;

    private static void AssertNoUnsafeProviderDiagnosticContent(string diagnostic)
    {
        var unsafeFragments = new[]
        {
            "rawPrompt",
            "hiddenPrompt",
            "systemPrompt",
            "developerPrompt",
            "rawProviderPayload",
            "rawSourceChunk",
            "rawToolPayload",
            "debugTrace",
            "localPath",
            "apiKey",
            "secret",
            "token",
            "answerKey",
            "correctAnswer",
            "stackTrace",
            "ownerId",
            "userId",
            "C:\\",
            "D:\\",
            "student private note",
            "my family phone",
            "source private paragraph",
            "confidential lesson material",
            "provider body",
            "hidden system prompt"
        };

        foreach (var fragment in unsafeFragments)
            Assert.DoesNotContain(fragment, diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    private static AIAgentFactory CreateFactory(
        FakeProviders providers,
        IAiUsageBudgetService? budget = null,
        IAiProviderTelemetryService? telemetry = null,
        IRuntimeTelemetryService? runtime = null,
        IAiRequestContextAccessor? context = null,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
            {
                ["AI:AgentRouting:Tutor:Provider"] = "GitHubModels",
                ["AI:AgentRouting:Tutor:Model"] = "primary-model",
                ["AI:AgentRouting:Quiz:Provider"] = "Gemini",
                ["AI:AgentRouting:Quiz:Model"] = "gemini-3.1-pro-preview",
                ["AI:GitHubModels:Token"] = "test-github-token",
                ["AI:GitHubModels:Agents:Quiz:Model"] = "openai/gpt-4o",
                ["AI:Groq:ApiKey"] = "test-groq-key",
                ["AI:Mistral:ApiKey"] = "test-mistral-key",
                ["AI:Gemini:ApiKey"] = "test-gemini-key",
                ["AI:OpenRouter:ApiKey"] = "test-openrouter-key",
                ["AI:Cerebras:ApiKey"] = "test-cerebras-key",
                ["AI:SambaNova:ApiKey"] = "test-sambanova-key",
                ["AI:Reliability:MaxAttemptsPerRequest"] = "2",
                ["AI:Reliability:FallbackEnabled"] = "true",
                ["AI:Reliability:ProviderCooldownSeconds"] = "60",
                ["AI:Reliability:StrictRoleCircuitFailureThreshold"] = "2",
                ["AI:Cost:RoleBudgets:Tutor:MaxOutputTokens"] = "256",
                ["AI:Cost:RoleBudgets:Quiz:MaxOutputTokens"] = "32768"
            };
        if (overrides != null)
        {
            foreach (var pair in overrides)
            {
                values[pair.Key] = pair.Value;
            }
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var activeRuntime = runtime ?? new RecordingRuntimeTelemetry();
        var activeRedis = new NullRedisMemoryService();
        var serviceProvider = new TestServiceProvider(activeRedis, activeRuntime);
        var syncQueue = new SyncBackgroundTaskQueue(serviceProvider);

        return new AIAgentFactory(
            providers,
            providers,
            providers,
            providers,
            providers,
            providers,
            providers,
            activeRedis,
            syncQueue,
            activeRuntime,
            telemetry ?? new RecordingAiTelemetry(),
            budget ?? new StaticBudget(Allowed: true),
            new AiProviderCircuitBreaker(),
            context ?? new AsyncLocalAiRequestContextAccessor(),
            configuration,
            NullLogger<AIAgentFactory>.Instance);
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        private readonly IRedisMemoryService _redis;
        private readonly IRuntimeTelemetryService _runtime;

        public TestServiceProvider(IRedisMemoryService redis, IRuntimeTelemetryService runtime)
        {
            _redis = redis;
            _runtime = runtime;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IRedisMemoryService))
                return _redis;
            if (serviceType == typeof(IRuntimeTelemetryService))
                return _runtime;
            return null;
        }
    }

    private sealed class SyncBackgroundTaskQueue : IBackgroundTaskQueue
    {
        private readonly IServiceProvider _serviceProvider;

        public SyncBackgroundTaskQueue(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async ValueTask QueueAsync(BackgroundTaskItem item, CancellationToken ct = default)
        {
            if (item.ScopedWork != null)
            {
                await item.ScopedWork(_serviceProvider, ct);
            }
            else if (item.Work != null)
            {
                await item.Work(ct);
            }
        }
    }


    private sealed class FakeProviders : IGitHubModelsService, IGroqService, IGeminiService, IOpenRouterService, ICerebrasService, IMistralService, ISambaNovaService
    {
        public int GitHubCalls;
        public int GroqCalls;
        public int GeminiCalls;
        public int MistralCalls;
        public string? LastGitHubModel;
        public int? LastGitHubMaxOutputTokens;
        public Func<Task<string>> GitHubHandler { get; set; } = () => Task.FromResult("github-ok");
        public Func<Task<string>> GroqHandler { get; set; } = () => Task.FromResult("groq-ok");
        public Func<Task<string>> GeminiHandler { get; set; } = () => Task.FromResult("gemini-ok");
        public Func<Task<string>> MistralHandler { get; set; } = () => Task.FromResult("mistral-ok");

        public Task<string> ChatAsync(string systemPrompt, string userMessage, string model, CancellationToken ct = default, int? maxOutputTokens = null)
        {
            GitHubCalls++;
            LastGitHubModel = model;
            LastGitHubMaxOutputTokens = maxOutputTokens;
            return GitHubHandler();
        }

        public async IAsyncEnumerable<string> ChatStreamAsync(string systemPrompt, string userMessage, string model, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default, int? maxOutputTokens = null)
        {
            yield return await ChatAsync(systemPrompt, userMessage, model, ct, maxOutputTokens);
        }

        public Task<string> GenerateResponseAsync(string systemPrompt, string userMessage, CancellationToken ct = default, int? maxOutputTokens = null)
        {
            GroqCalls++;
            return GroqHandler();
        }

        public async IAsyncEnumerable<string> GenerateResponseStreamAsync(string systemPrompt, string userMessage, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default, int? maxOutputTokens = null)
        {
            yield return await GenerateResponseAsync(systemPrompt, userMessage, ct, maxOutputTokens);
        }

        public Task<string> GenerateSmartAsync(string systemPrompt, string userMessage, CancellationToken ct = default) => GenerateWithModelAsync("gemini-test", systemPrompt, userMessage, ct);
        public Task<string> GenerateWithModelAsync(string model, string systemPrompt, string userMessage, CancellationToken ct = default, int? maxOutputTokens = null)
        {
            GeminiCalls++;
            return GeminiHandler();
        }
        public IAsyncEnumerable<string> StreamSmartAsync(string systemPrompt, string userMessage, CancellationToken ct = default) => GenerateResponseStreamAsync(systemPrompt, userMessage, ct);
        public IAsyncEnumerable<string> StreamWithModelAsync(string model, string systemPrompt, string userMessage, CancellationToken ct = default, int? maxOutputTokens = null) => GenerateResponseStreamAsync(systemPrompt, userMessage, ct, maxOutputTokens);
        public Task<string> ChatCompletionAsync(string systemPrompt, string userMessage, string? model = null, CancellationToken ct = default, int? maxOutputTokens = null) => GenerateResponseAsync(systemPrompt, userMessage, ct, maxOutputTokens);
        public Task<string> ChatCompletionWithKeyAsync(string systemPrompt, string userMessage, string? model = null, string? apiKey = null, CancellationToken ct = default, int? maxOutputTokens = null) => GenerateResponseAsync(systemPrompt, userMessage, ct, maxOutputTokens);
        public IAsyncEnumerable<string> GenerateResponseStreamWithKeyAsync(string systemPrompt, string userMessage, string? model = null, string? apiKey = null, CancellationToken ct = default, int? maxOutputTokens = null) => GenerateResponseStreamAsync(systemPrompt, userMessage, ct, maxOutputTokens);
        public Task<string> GetResponseAsync(IEnumerable<Message> context, string systemPrompt, CancellationToken ct = default) => GenerateResponseAsync(systemPrompt, string.Empty, ct);
        public IAsyncEnumerable<string> GetResponseStreamAsync(IEnumerable<Message> context, string systemPrompt, CancellationToken ct = default) => GenerateResponseStreamAsync(systemPrompt, string.Empty, ct);
        public Task<string> SummarizeSessionAsync(IEnumerable<Message> messages) => GenerateResponseAsync(string.Empty, string.Empty);
        public Task<RoutingResult> SemanticRouteAsync(string message, string? currentPhase = "Discovery") => Task.FromResult(new RoutingResult { Intent = "general", Method = "fake" });
        public Task<string> ResearchAsync(string topic, string depth = "normal") => GenerateResponseAsync(string.Empty, topic);
        public Task<string> GeneratePlanAsync(string topicTitle, string intent = "genel", string level = "orta") => GenerateResponseAsync(string.Empty, topicTitle);
        public Task<string> ExtractWikiBlocksAsync(string conversation, string topicTitle) => GenerateResponseAsync(string.Empty, conversation);
        public Task<string> GenerateReinforcementQuestionsAsync(string content) { MistralCalls++; return GenerateResponseAsync(string.Empty, content); }
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StaticHttpClientFactory(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public HttpClient CreateClient(string name) => new(new StaticHttpHandler(_status, _body));
    }

    private sealed class StaticHttpHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StaticHttpHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body)
            });
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
        public Task SaveGoldExampleAsync(Guid userId, Guid topicId, string userMessage, string agentResponse, int score) => Task.CompletedTask;
        public Task<IEnumerable<GoldExample>> GetGoldExamplesAsync(Guid userId, Guid topicId, int count = 2) => Task.FromResult<IEnumerable<GoldExample>>(Array.Empty<GoldExample>());
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
        public Task<bool> AcquireLockAsync(string key, string value, TimeSpan expiry) => Task.FromResult(true);
        public Task<bool> RenewLockAsync(string key, string value, TimeSpan expiry) => Task.FromResult(true);
        public Task ReleaseLockAsync(string key, string value) => Task.CompletedTask;
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

    public static AiProviderCallException FromException(string provider, string? model, Exception exception)
    {
        var mapperType = typeof(AIAgentFactory).Assembly.GetType("Orka.Infrastructure.Services.AiProviderFailureMapper", throwOnError: true)!;
        var method = mapperType.GetMethod(
            "FromException",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        return (AiProviderCallException)method!.Invoke(null, new object?[] { provider, model, exception, AiProviderFailureKind.TransientNetwork })!;
    }
}
