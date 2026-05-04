using System.Text.Json;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs.PlanDiagnostic;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public sealed class RedisPlanDiagnosticStateStore : IPlanDiagnosticStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan MinimumTtl = TimeSpan.FromMinutes(1);

    private readonly IRedisMemoryService _redis;
    private readonly ILogger<RedisPlanDiagnosticStateStore> _logger;

    public RedisPlanDiagnosticStateStore(
        IRedisMemoryService redis,
        ILogger<RedisPlanDiagnosticStateStore> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<PlanDiagnosticStateDto?> GetAsync(Guid planRequestId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var payload = await _redis.GetJsonAsync(Key(planRequestId));
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<PlanDiagnosticStateDto>(payload, JsonOptions);
            if (state == null)
            {
                return null;
            }

            if (state.ExpiresAt <= DateTime.UtcNow)
            {
                await DeleteAsync(planRequestId, ct);
                return null;
            }

            return state;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[PlanDiagnosticStateStore] Invalid state payload. PlanRequestId={PlanRequestId}", planRequestId);
            await DeleteAsync(planRequestId, ct);
            return null;
        }
    }

    public async Task SaveAsync(PlanDiagnosticStateDto state, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var ttl = state.ExpiresAt - DateTime.UtcNow;
        if (ttl < MinimumTtl)
        {
            ttl = MinimumTtl;
        }

        var payload = JsonSerializer.Serialize(state, JsonOptions);
        await _redis.SetJsonAsync(Key(state.PlanRequestId), payload, ttl);
    }

    public async Task DeleteAsync(Guid planRequestId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _redis.DeleteKeyAsync(Key(planRequestId));
    }

    private static string Key(Guid planRequestId) =>
        $"orka:v1:plan-diagnostic:state:{planRequestId:N}";
}
