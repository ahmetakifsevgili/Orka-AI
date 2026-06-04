using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Orka.API.Services;
using Orka.Core.DTOs;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.API.Tests;

public sealed class ApiSmokeFactory : WebApplicationFactory<Program>
{
    private readonly object _chaosTrackingGate = new();
    private readonly List<string> _chaosTrackingProviders = [];

    private readonly string _databaseName = $"OrkaSmoke_{Guid.NewGuid():N}";
    private readonly string _environmentName;
    private readonly IReadOnlyDictionary<string, string?> _configurationOverrides;
    private readonly Action<IServiceCollection>? _configureServices;

    public ApiSmokeFactory()
        : this("Development")
    {
    }

    internal ApiSmokeFactory(
        string environmentName,
        IReadOnlyDictionary<string, string?>? configurationOverrides = null,
        Action<IServiceCollection>? configureServices = null)
    {
        _environmentName = environmentName;
        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Testing:DisableBackgroundWorkers"] = "true"
        };
        if (configurationOverrides != null)
        {
            foreach (var kvp in configurationOverrides)
            {
                overrides[kvp.Key] = kvp.Value;
            }
        }
        _configurationOverrides = overrides;
        _configureServices = configureServices;
    }

    public void ResetChaosTracking()
    {
        lock (_chaosTrackingGate)
        {
            _chaosTrackingProviders.Clear();
        }
    }

    public IReadOnlyList<string> GetChaosTrackingProviders()
    {
        lock (_chaosTrackingGate)
        {
            return _chaosTrackingProviders.ToArray();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environmentName);

        builder.ConfigureLogging(logging =>
        {
            logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
            logging.AddFilter("LuckyPennySoftware.MediatR.License", LogLevel.None);
            logging.AddFilter("Orka.Infrastructure.Services.BackgroundTaskQueue", LogLevel.Warning);
            logging.AddFilter("Orka.Infrastructure.Services.RetentionCleanupWorker", LogLevel.Warning);
            logging.AddFilter("Orka.Infrastructure.Services.RedisStreamMaintenanceWorker", LogLevel.Warning);
            logging.AddFilter("Orka.Infrastructure.Services.SrsReminderWorker", LogLevel.Warning);
            logging.AddFilter("Orka.Infrastructure.Services.DailyChallengeWorker", LogLevel.Warning);
        });

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var values = new Dictionary<string, string?>
            {
                ["AllowedHosts"] = IsProtectedEnvironment(_environmentName) ? "localhost;app.example.com" : "localhost",
                ["JWT:Issuer"] = "orka-api",
                ["JWT:Audience"] = "orka-client",
                ["JWT:Secret"] = "ORKA_TEST_JWT_SECRET_64_CHARS_2026_01",
                ["JWT:RefreshTokenHashSecret"] = "ORKA_TEST_REFRESH_HASH_SECRET_64_CHARS_2026_01",
                ["Database:Provider"] = "SqlServer",
                ["Database:AutoMigrateOnStartup"] = "false",
                ["ConnectionStrings:DefaultConnection"] = "Server=sql.example.invalid;Database=OrkaSmoke;User Id=orka;Password=SmokePass123!;TrustServerCertificate=True;",
                ["ConnectionStrings:Redis"] = IsProtectedEnvironment(_environmentName) ? "redis.example.invalid:6379,abortConnect=false" : "127.0.0.1:6399,abortConnect=false",
                ["Cors:AllowedOrigins:0"] = IsProtectedEnvironment(_environmentName) ? "https://app.example.com" : "http://localhost:3000",
                ["RateLimits:Auth:Backend"] = IsProtectedEnvironment(_environmentName) ? "Redis" : "InMemory",
                ["RateLimits:Auth:AllowInMemoryFallback"] = IsProtectedEnvironment(_environmentName) ? "false" : "true",
                ["RateLimits:Auth:Login:PermitLimit"] = "1000",
                ["RateLimits:Auth:Register:PermitLimit"] = "1000",
                ["RateLimits:Auth:Refresh:PermitLimit"] = "1000",
                ["RateLimits:Chat:ReplenishmentMinutes"] = "60",
                ["AI:Cost:Enabled"] = "true",
                ["AI:Cost:GlobalDailyUsdLimit"] = "100",
                ["AI:Cost:UserDailyUsdLimit"] = "10",
                ["AI:GitHubModels:Token"] = "test-github-token",
                ["AI:Groq:ApiKey"] = "test-groq-key",
                ["AI:Gemini:ApiKey"] = "test-gemini-key",
                ["AI:OpenRouter:ApiKey"] = "test-openrouter-key",
                ["AI:Cerebras:ApiKey"] = "test-cerebras-key",
                ["AI:Mistral:ApiKey"] = "test-mistral-key",
                ["AI:SambaNova:ApiKey"] = "test-sambanova-key"
            };

            foreach (var (key, value) in _configurationOverrides)
            {
                values[key] = value;
            }

            config.AddInMemoryCollection(values);
        });

        builder.ConfigureServices(services =>
        {
            var descriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<OrkaDbContext>))
                .ToList();

            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<OrkaDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));

            var agentFactoryDescriptors = services
                .Where(d => d.ServiceType == typeof(IAIAgentFactory))
                .ToList();

            foreach (var descriptor in agentFactoryDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddScoped<IAIAgentFactory, SmokeAgentFactory>();

            var groqDescriptors = services
                .Where(d => d.ServiceType == typeof(IGroqService))
                .ToList();

            foreach (var descriptor in groqDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddScoped<IGroqService, SmokeGroqService>();

            var embeddingDescriptors = services
                .Where(d => d.ServiceType == typeof(IEmbeddingService))
                .ToList();

            foreach (var descriptor in embeddingDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<IEmbeddingService, SmokeEmbeddingService>();

            var pistonDescriptors = services
                .Where(d => d.ServiceType == typeof(IPistonService))
                .ToList();

            foreach (var descriptor in pistonDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<IPistonService, SmokePistonService>();

            var ttsDescriptors = services
                .Where(d => d.ServiceType == typeof(IEdgeTtsService))
                .ToList();

            foreach (var descriptor in ttsDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<IEdgeTtsService, SmokeEdgeTtsService>();

            var redisDescriptors = services
                .Where(d => d.ServiceType == typeof(IRedisMemoryService))
                .ToList();

            foreach (var descriptor in redisDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<IRedisMemoryService, SmokeRedisMemoryService>();

            var realWorldEvidenceDescriptors = services
                .Where(d => d.ServiceType == typeof(IRealWorldEvidenceService))
                .ToList();

            foreach (var descriptor in realWorldEvidenceDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<IRealWorldEvidenceService, SmokeRealWorldEvidenceService>();

            var chaosDescriptors = services
                .Where(d => d.ServiceType == typeof(IChaosContext))
                .ToList();

            foreach (var descriptor in chaosDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddScoped<IChaosContext>(sp => new SmokeChaosContext(this));

            var authLimiterDescriptors = services
                .Where(d => d.ServiceType == typeof(IAuthAttemptLimiter))
                .ToList();

            foreach (var descriptor in authLimiterDescriptors)
            {
                services.Remove(descriptor);
            }

            services.AddSingleton<IAuthAttemptLimiter>(sp => sp.GetRequiredService<AuthAttemptRateLimiter>());

            services.Configure<HealthCheckServiceOptions>(options =>
            {
                var redisRegistrations = options.Registrations
                    .Where(registration => string.Equals(registration.Name, "redis", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var registration in redisRegistrations)
                {
                    options.Registrations.Remove(registration);
                }

                options.Registrations.Add(new HealthCheckRegistration(
                    "redis",
                    _ => new SmokeRedisHealthCheck(),
                    HealthStatus.Unhealthy,
                    new[] { "ready" }));
            });

            _configureServices?.Invoke(services);

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            db.Database.EnsureCreated();
        });
    }

    private static bool IsProtectedEnvironment(string environmentName) =>
        string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(environmentName, "Staging", StringComparison.OrdinalIgnoreCase);

    private sealed class SmokeRedisHealthCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthCheckResult.Healthy("Smoke Redis health is provided by ApiSmokeFactory."));
    }

    private sealed class SmokeAgentFactory : IAIAgentFactory
    {
        private readonly IChaosContext _chaos;

        public SmokeAgentFactory(IChaosContext chaos)
        {
            _chaos = chaos;
        }

        public string GetModel(AgentRole role) => "smoke-model";

        public string GetProvider(AgentRole role) => "smoke-provider";

        public Task<string> CompleteChatAsync(
            AgentRole role,
            string systemPrompt,
            string userMessage,
            CancellationToken ct = default) =>
            Task.FromResult("[HOCA]: Bu kısmı sade bir örnekle tekrar anlatalım.\n[ASISTAN]: Ben de öğrencinin takıldığı noktayı küçük bir soruyla netleştireyim.");

        public async IAsyncEnumerable<string> StreamChatAsync(
            AgentRole role,
            string systemPrompt,
            string userMessage,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return await CompleteChatAsync(role, systemPrompt, userMessage, ct);
        }

        public Task<string> CompleteChatWithHistoryAsync(
            AgentRole role,
            string systemPrompt,
            IEnumerable<(string Role, string Content)> messages,
            CancellationToken ct = default) =>
            CompleteChatAsync(role, systemPrompt, string.Join("\n", messages.Select(m => m.Content)), ct);
    }

    private sealed class SmokeChaosContext : IChaosContext
    {
        private readonly ApiSmokeFactory _factory;
        private string? _failingProvider;

        public SmokeChaosContext(ApiSmokeFactory factory)
        {
            _factory = factory;
        }

        public bool IsProviderFailing(string providerName) =>
            !string.IsNullOrEmpty(_failingProvider) &&
            string.Equals(_failingProvider, providerName, StringComparison.OrdinalIgnoreCase);

        public void SetFailingProvider(string providerName)
        {
            _failingProvider = providerName?.Trim();
            if (string.IsNullOrWhiteSpace(_failingProvider))
                return;

            lock (_factory._chaosTrackingGate)
            {
                _factory._chaosTrackingProviders.Add(_failingProvider);
            }
        }
    }

    private sealed class SmokePistonService : IPistonService
    {
        public Task<PistonResult> ExecuteAsync(string code, string language = "csharp", string? stdin = null)
        {
            if (code.Contains("throw", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new PistonResult("", "Smoke runtime error", false));
            }

            return Task.FromResult(new PistonResult("Smoke output", "", true));
        }

        public Task<IReadOnlyList<PistonRuntime>> GetRuntimesAsync() =>
            Task.FromResult<IReadOnlyList<PistonRuntime>>(
            [
                new("csharp", "smoke", []),
                new("python", "smoke", [])
            ]);
    }

    private sealed class SmokeGroqService : IGroqService
    {
        private readonly IChaosContext _chaos;

        public SmokeGroqService(IChaosContext chaos)
        {
            _chaos = chaos;
        }

        public Task<string> GenerateResponseAsync(string systemPrompt, string userMessage, CancellationToken ct = default, int? maxOutputTokens = null) =>
            GetResponseAsync([], systemPrompt, ct);

        public async IAsyncEnumerable<string> GenerateResponseStreamAsync(
            string systemPrompt,
            string userMessage,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default,
            int? maxOutputTokens = null)
        {
            yield return await GetResponseAsync([], systemPrompt, ct);
        }

        public Task<string> GetResponseAsync(IEnumerable<Core.Entities.Message> context, string systemPrompt, CancellationToken ct = default)
        {
            if (_chaos.IsProviderFailing("Groq"))
                throw new HttpRequestException("Smoke chaos Groq failure.");

            return Task.FromResult("OK");
        }

        public async IAsyncEnumerable<string> GetResponseStreamAsync(
            IEnumerable<Core.Entities.Message> context,
            string systemPrompt,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return await GetResponseAsync(context, systemPrompt, ct);
        }

        public Task<string> SummarizeSessionAsync(IEnumerable<Core.Entities.Message> messages) =>
            Task.FromResult("Smoke summary");

        public Task<RoutingResult> SemanticRouteAsync(string message, string? currentPhase = "Discovery") =>
            Task.FromResult(new RoutingResult { Intent = "general", Method = "smoke" });

        public Task<string> ResearchAsync(string topic, string depth = "normal") =>
            Task.FromResult("Smoke research");

        public Task<string> GeneratePlanAsync(string topicTitle, string intent = "genel öğrenme", string level = "orta") =>
            Task.FromResult("Smoke plan");
    }

    private sealed class SmokeEmbeddingService : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, string inputType = "search_query", CancellationToken ct = default) =>
            Task.FromResult(VectorFor(text));

        public Task<float[][]> EmbedBatchAsync(IEnumerable<string> texts, string inputType = "search_document", CancellationToken ct = default) =>
            Task.FromResult(texts.Select(VectorFor).ToArray());

        public float CosineSimilarity(float[] a, float[] b)
        {
            var length = Math.Min(a.Length, b.Length);
            if (length == 0) return 0;
            var dot = 0f;
            var normA = 0f;
            var normB = 0f;
            for (var i = 0; i < length; i++)
            {
                dot += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }

            return normA <= 0 || normB <= 0 ? 0 : dot / (float)(Math.Sqrt(normA) * Math.Sqrt(normB));
        }

        private static float[] VectorFor(string text)
        {
            var value = Math.Abs((text ?? string.Empty).GetHashCode());
            return
            [
                1f,
                (value % 17) / 17f,
                (value % 31) / 31f,
                (value % 47) / 47f
            ];
        }
    }

    private sealed class SmokeEdgeTtsService : IEdgeTtsService
    {
        public Task<byte[]> SynthesizeDialogueAsync(string script, CancellationToken ct = default) =>
            Task.FromResult(Array.Empty<byte>());

        public Task<byte[]> SynthesizeDialogueAsync(string script, string? ttsQuality, CancellationToken ct = default) =>
            Task.FromResult(Array.Empty<byte>());
    }

    private sealed class SmokeRealWorldEvidenceService : IRealWorldEvidenceService
    {
        public Task<TeachingEvidenceResultDto> GetEvidenceAsync(
            TeachingEvidenceRequestDto request,
            CancellationToken ct = default) =>
            Task.FromResult(new TeachingEvidenceResultDto(
                false,
                request.EvidenceType,
                "smoke",
                "skipped",
                [],
                "Real-world evidence is disabled in deterministic smoke tests.",
                "smoke_disabled",
                null,
                0,
                0,
                true));

        public Task<IReadOnlyList<TeachingEvidenceCardDto>> GetRecentCardsAsync(
            Guid userId,
            Guid? topicId,
            Guid? tutorActionTraceId = null,
            int take = 8,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TeachingEvidenceCardDto>>([]);
    }

    private sealed class SmokeRedisMemoryService : IRedisMemoryService
    {
        private readonly Dictionary<string, string> _json = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Guid, long> _versions = [];
        private readonly Dictionary<Guid, string> _lastPiston = [];

        public Task RecordEvaluationAsync(Guid sessionId, int score, string feedback) => Task.CompletedTask;
        public Task<IEnumerable<string>> GetRecentFeedbackAsync(Guid sessionId, int count = 5) => Task.FromResult<IEnumerable<string>>([]);
        public Task<bool> CheckRateLimitAsync(string clientIp, int maxRequests, TimeSpan window) => Task.FromResult(true);
        public Task SetGlobalPolicyAsync(string policyText) => Task.CompletedTask;
        public Task<string> GetGlobalPolicyAsync() => Task.FromResult(string.Empty);

        public Task SetLastPistonResultAsync(
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
            _lastPiston[sessionId] = System.Text.Json.JsonSerializer.Serialize(new { code, stdout, stderr, language, phase, compileError, runtimeError, success, safeTutorSummary });
            return Task.CompletedTask;
        }

        public Task<string> GetLastPistonResultAsync(Guid sessionId) =>
            Task.FromResult(_lastPiston.TryGetValue(sessionId, out var value) ? value : string.Empty);

        public Task SetWikiReadyAsync(Guid topicId) => Task.CompletedTask;
        public Task SaveGoldExampleAsync(Guid userId, Guid topicId, string userMessage, string agentResponse, int score) => Task.CompletedTask;
        public Task<IEnumerable<GoldExample>> GetGoldExamplesAsync(Guid userId, Guid topicId, int count = 2) => Task.FromResult<IEnumerable<GoldExample>>([]);
        public Task RecordAgentMetricAsync(string agentRole, long latencyMs, bool isSuccess, string? provider = null) => Task.CompletedTask;
        public Task<IEnumerable<AgentMetricSummary>> GetSystemMetricsAsync() => Task.FromResult<IEnumerable<AgentMetricSummary>>([]);
        public Task<IEnumerable<EvaluatorLogEntry>> GetRecentEvaluatorLogsAsync(int count = 20) => Task.FromResult<IEnumerable<EvaluatorLogEntry>>([]);
        public Task<IEnumerable<ProviderUsageStat>> GetProviderUsageAsync() => Task.FromResult<IEnumerable<ProviderUsageStat>>([]);

        public Task<string?> GetJsonAsync(string key) =>
            Task.FromResult(_json.TryGetValue(key, out var value) ? value : null);

        public Task SetJsonAsync(string key, string payload, TimeSpan ttl)
        {
            _json[key] = payload;
            return Task.CompletedTask;
        }

        public Task AddStreamEventAsync(string key, IReadOnlyDictionary<string, string> values, TimeSpan? ttl = null) => Task.CompletedTask;
        public Task<IReadOnlyList<RedisStreamEventDto>> ReadStreamEventsAsync(string key, string afterId = "0-0", int count = 50) => Task.FromResult<IReadOnlyList<RedisStreamEventDto>>([]);
        public Task<bool> EnsureConsumerGroupAsync(string key, string group, string startId = "0-0") => Task.FromResult(true);
        public Task<IReadOnlyList<RedisStreamEventDto>> ReadConsumerGroupAsync(string key, string group, string consumer, int count = 50, string streamId = ">") => Task.FromResult<IReadOnlyList<RedisStreamEventDto>>([]);
        public Task AckStreamEventsAsync(string key, string group, IEnumerable<string> eventIds) => Task.CompletedTask;
        public Task<bool> SupportsVectorSearchAsync() => Task.FromResult(false);

        public Task DeleteKeyAsync(string key)
        {
            _json.Remove(key);
            return Task.CompletedTask;
        }

        public Task<long> GetTopicVersionAsync(Guid topicId) =>
            Task.FromResult(_versions.TryGetValue(topicId, out var value) ? value : 0L);

        public Task<long> BumpTopicVersionAsync(Guid topicId, string reason)
        {
            _versions[topicId] = _versions.TryGetValue(topicId, out var value) ? value + 1 : 1;
            return Task.FromResult(_versions[topicId]);
        }

        public Task InvalidateLearningCachesAsync(Guid userId, Guid topicId, string reason)
        {
            _json.Clear();
            return Task.CompletedTask;
        }
        public Task RecordCacheMetricAsync(string area, bool hit, string? tool = null, double? latencyMs = null) => Task.CompletedTask;
        public Task<IEnumerable<CacheMetricSummary>> GetCacheMetricsAsync() => Task.FromResult<IEnumerable<CacheMetricSummary>>([]);
        public Task<RedisHealthDto> GetRedisHealthAsync() => Task.FromResult(new RedisHealthDto(true, 0, 0, "smoke", null, DateTime.UtcNow));
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
}
