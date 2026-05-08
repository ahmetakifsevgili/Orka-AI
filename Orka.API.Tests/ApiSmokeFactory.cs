using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.API.Tests;

public sealed class ApiSmokeFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"OrkaSmoke_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT:Issuer"] = "orka-api",
                ["JWT:Audience"] = "orka-client",
                ["Database:AutoMigrateOnStartup"] = "false",
                ["ConnectionStrings:Redis"] = "127.0.0.1:6399,abortConnect=false"
            });
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

            services.AddSingleton<IAIAgentFactory, SmokeAgentFactory>();

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

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            db.Database.EnsureCreated();
        });
    }

    private sealed class SmokeAgentFactory : IAIAgentFactory
    {
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
        public Task SaveGoldExampleAsync(Guid topicId, string userMessage, string agentResponse, int score) => Task.CompletedTask;
        public Task<IEnumerable<GoldExample>> GetGoldExamplesAsync(Guid topicId, int count = 2) => Task.FromResult<IEnumerable<GoldExample>>([]);
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
    }
}
