using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class AiProviderTelemetryService : IAiProviderTelemetryService
{
    private const string RedisKey = "orka:ai:provider-telemetry:v1";
    private const int MaxRecords = 500;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IRedisMemoryService _redis;
    private readonly ILogger<AiProviderTelemetryService> _logger;

    public AiProviderTelemetryService(IRedisMemoryService redis, ILogger<AiProviderTelemetryService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task RecordAsync(AiProviderTelemetryEvent telemetryEvent, CancellationToken ct = default)
    {
        try
        {
            var existing = await _redis.GetJsonAsync(RedisKey);
            var records = string.IsNullOrWhiteSpace(existing)
                ? []
                : JsonSerializer.Deserialize<List<AiProviderTelemetryEvent>>(existing, JsonOptions) ?? [];

            records.Insert(0, telemetryEvent);
            if (records.Count > MaxRecords)
                records.RemoveRange(MaxRecords, records.Count - MaxRecords);

            await _redis.SetJsonAsync(RedisKey, JsonSerializer.Serialize(records, JsonOptions), TimeSpan.FromHours(24));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[AiProviderTelemetry] Metric write failed. Provider={Provider}", telemetryEvent.Provider);
        }
    }

    public async Task<AiProviderTelemetrySummary> GetSummaryAsync(CancellationToken ct = default)
    {
        try
        {
            var existing = await _redis.GetJsonAsync(RedisKey);
            var since = DateTime.UtcNow.AddHours(-24);
            var records = string.IsNullOrWhiteSpace(existing)
                ? []
                : JsonSerializer.Deserialize<List<AiProviderTelemetryEvent>>(existing, JsonOptions) ?? [];

            var recent = records.Where(r => r.OccurredAt >= since).ToList();
            return new AiProviderTelemetrySummary(
                FallbackCount24h: recent.Count(r => r.FallbackUsed),
                QuotaHitCount24h: recent.Count(r => r.QuotaHit),
                FailureKinds24h: recent
                    .Where(r => r.FailureKind.HasValue)
                    .GroupBy(r => r.FailureKind!.Value.ToString())
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase),
                CircuitStates: recent
                    .Where(r => !string.IsNullOrWhiteSpace(r.CircuitState))
                    .GroupBy(r => $"{r.Provider}:{r.CircuitState}")
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[AiProviderTelemetry] Summary read failed.");
            return new AiProviderTelemetrySummary(0, 0, new Dictionary<string, int>(), new Dictionary<string, int>());
        }
    }
}

public sealed class AiProviderCircuitBreaker : IAiProviderCircuitBreaker
{
    private readonly ConcurrentDictionary<string, DateTime> _openUntil = new(StringComparer.OrdinalIgnoreCase);

    public bool IsOpen(string provider) =>
        _openUntil.TryGetValue(provider, out var until) && until > DateTime.UtcNow;

    public string GetState(string provider)
    {
        if (!_openUntil.TryGetValue(provider, out var until))
            return "closed";

        return until > DateTime.UtcNow ? "open" : "half_open";
    }

    public void RecordSuccess(string provider)
    {
        _openUntil.TryRemove(provider, out _);
    }

    public void RecordFailure(string provider, TimeSpan cooldown)
    {
        if (cooldown <= TimeSpan.Zero)
            return;

        _openUntil[provider] = DateTime.UtcNow.Add(cooldown);
    }
}

public sealed class AsyncLocalAiRequestContextAccessor : IAiRequestContextAccessor
{
    private static readonly AsyncLocal<AiRequestContext?> CurrentContext = new();

    public AiRequestContext Current => CurrentContext.Value ?? AiRequestContext.Empty;

    public IDisposable Push(AiRequestContext context)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = Merge(previous, context);
        return new RestoreScope(previous);
    }

    private static AiRequestContext Merge(AiRequestContext? previous, AiRequestContext next)
    {
        if (previous == null)
            return next;

        return previous with
        {
            UserId = next.UserId ?? previous.UserId,
            SessionId = next.SessionId ?? previous.SessionId,
            TopicId = next.TopicId ?? previous.TopicId,
            CorrelationId = next.CorrelationId ?? previous.CorrelationId,
            Source = next.Source ?? previous.Source,
            IsBackground = next.IsBackground || previous.IsBackground
        };
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly AiRequestContext? _previous;
        private bool _disposed;

        public RestoreScope(AiRequestContext? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            CurrentContext.Value = _previous;
            _disposed = true;
        }
    }
}

public sealed class AiUsageBudgetService : IAiUsageBudgetService
{
    private readonly OrkaDbContext _db;
    private readonly ITokenCostEstimator _estimator;
    private readonly IConfiguration _configuration;

    public AiUsageBudgetService(
        OrkaDbContext db,
        ITokenCostEstimator estimator,
        IConfiguration configuration)
    {
        _db = db;
        _estimator = estimator;
        _configuration = configuration;
    }

    public async Task<AiUsageBudgetDecision> CheckAsync(AiUsageBudgetRequest request, CancellationToken ct = default)
    {
        if (!_configuration.GetValue("AI:Cost:Enabled", true))
            return Allow(request, 0, 0, 0m);

        var maxOutputTokens = Math.Max(0, request.MaxOutputTokens);
        var inputTokens = _estimator.EstimateTokens(request.InputText);
        var estimatedOutputText = new string('x', maxOutputTokens * 4);
        var (_, estimatedCost) = _estimator.Estimate(request.Model ?? string.Empty, request.InputText, estimatedOutputText);
        var totalTokens = inputTokens + maxOutputTokens;

        var today = DateTime.UtcNow.Date;
        var costs = _db.CostRecords.AsNoTracking().Where(c => c.OccurredAt >= today);
        var globalCost = await costs.SumAsync(c => c.EstimatedCostUsd, ct);
        var globalTokens = await costs.SumAsync(c => c.EstimatedTokens, ct);

        var globalCostLimit = _configuration.GetValue<decimal?>("AI:Cost:GlobalDailyUsdLimit");
        if (globalCostLimit.HasValue && globalCost + estimatedCost > globalCostLimit.Value)
            return Deny("global_daily_cost", inputTokens, maxOutputTokens, totalTokens, estimatedCost);

        var globalTokenLimit = _configuration.GetValue<int?>("AI:Cost:GlobalDailyTokenLimit");
        if (globalTokenLimit.HasValue && globalTokens + totalTokens > globalTokenLimit.Value)
            return Deny("global_daily_tokens", inputTokens, maxOutputTokens, totalTokens, estimatedCost);

        if (request.UserId.HasValue)
        {
            var userCosts = costs.Where(c => c.UserId == request.UserId.Value);
            var userCost = await userCosts.SumAsync(c => c.EstimatedCostUsd, ct);
            var userTokens = await userCosts.SumAsync(c => c.EstimatedTokens, ct);

            var userCostLimit = _configuration.GetValue<decimal?>("AI:Cost:UserDailyUsdLimit");
            if (userCostLimit.HasValue && userCost + estimatedCost > userCostLimit.Value)
                return Deny("user_daily_cost", inputTokens, maxOutputTokens, totalTokens, estimatedCost);

            var userTokenLimit = _configuration.GetValue<int?>("AI:Cost:UserDailyTokenLimit");
            if (userTokenLimit.HasValue && userTokens + totalTokens > userTokenLimit.Value)
                return Deny("user_daily_tokens", inputTokens, maxOutputTokens, totalTokens, estimatedCost);
        }

        return Allow(request, inputTokens, totalTokens, estimatedCost);
    }

    private static AiUsageBudgetDecision Allow(AiUsageBudgetRequest request, int inputTokens, int totalTokens, decimal cost) =>
        new(true, "allowed", inputTokens, Math.Max(0, request.MaxOutputTokens), totalTokens, cost);

    private static AiUsageBudgetDecision Deny(string reason, int inputTokens, int outputTokens, int totalTokens, decimal cost) =>
        new(false, reason, inputTokens, outputTokens, totalTokens, cost);
}
