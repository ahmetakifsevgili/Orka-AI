using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class RuntimeTelemetryService : IRuntimeTelemetryService
{
    private const int MetadataLimit = 4000;
    private static readonly ActivitySource ActivitySource = new("Orka");
    private static readonly Meter Meter = new("Orka");
    private static readonly Counter<long> ToolCalls = Meter.CreateCounter<long>("orka_tool_calls");
    private static readonly Counter<long> ToolFailures = Meter.CreateCounter<long>("orka_tool_failures");
    private static readonly Counter<long> EstimatedTokens = Meter.CreateCounter<long>("orka_estimated_tokens");
    private static readonly Counter<double> EstimatedCostUsd = Meter.CreateCounter<double>("orka_estimated_cost_usd");
    private readonly OrkaDbContext _db;
    private readonly ILogger<RuntimeTelemetryService> _logger;

    public RuntimeTelemetryService(OrkaDbContext db, ILogger<RuntimeTelemetryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task RecordToolEventAsync(ToolTelemetryEventRequest request, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ToolId))
                return;

            using var activity = ActivitySource.StartActivity("orka.tool", ActivityKind.Internal);
            activity?.SetTag("orka.tool.id", request.ToolId);
            activity?.SetTag("orka.tool.provider", request.Provider);
            activity?.SetTag("orka.tool.success", request.Success);
            activity?.SetTag("orka.tool.error_code", request.ErrorCode);
            ToolCalls.Add(1, new KeyValuePair<string, object?>("tool.id", request.ToolId), new KeyValuePair<string, object?>("provider", request.Provider ?? "unknown"));
            if (!request.Success)
            {
                ToolFailures.Add(1, new KeyValuePair<string, object?>("tool.id", request.ToolId), new KeyValuePair<string, object?>("provider", request.Provider ?? "unknown"));
            }

            _db.ToolTelemetryEvents.Add(new ToolTelemetryEvent
            {
                Id = Guid.NewGuid(),
                OccurredAt = DateTime.UtcNow,
                UserId = request.UserId,
                SessionId = request.SessionId,
                TopicId = request.TopicId,
                ToolId = Truncate(request.ToolId, 128) ?? "unknown",
                CapabilityStatus = Truncate(request.CapabilityStatus, 64) ?? "unknown",
                Provider = Truncate(request.Provider, 128),
                Model = Truncate(request.Model, 256),
                LatencyMs = Math.Max(0, request.LatencyMs),
                Success = request.Success,
                ErrorCode = Truncate(request.ErrorCode, 128),
                FallbackUsed = request.FallbackUsed,
                CorrelationId = Truncate(request.CorrelationId, 128),
                MetadataJson = Truncate(request.MetadataJson, MetadataLimit)
            });

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RuntimeTelemetry] Tool event write failed. ToolId={ToolId}", request.ToolId);
        }
    }

    public async Task RecordCostAsync(CostRecordRequest request, CancellationToken ct = default)
    {
        try
        {
            using var activity = ActivitySource.StartActivity("orka.cost", ActivityKind.Internal);
            activity?.SetTag("orka.agent.role", request.AgentRole);
            activity?.SetTag("orka.provider", request.Provider);
            activity?.SetTag("orka.model", request.Model);
            activity?.SetTag("orka.estimated_tokens", request.EstimatedTokens);
            activity?.SetTag("orka.estimated_cost_usd", (double)request.EstimatedCostUsd);
            EstimatedTokens.Add(Math.Max(0, request.EstimatedTokens), new KeyValuePair<string, object?>("provider", request.Provider ?? "unknown"));
            EstimatedCostUsd.Add((double)Math.Max(0m, request.EstimatedCostUsd), new KeyValuePair<string, object?>("provider", request.Provider ?? "unknown"));

            _db.CostRecords.Add(new CostRecord
            {
                Id = Guid.NewGuid(),
                OccurredAt = DateTime.UtcNow,
                UserId = request.UserId,
                SessionId = request.SessionId,
                TopicId = request.TopicId,
                MessageId = request.MessageId,
                AgentRole = Truncate(request.AgentRole, 128) ?? "unknown",
                Provider = Truncate(request.Provider, 128),
                Model = Truncate(request.Model, 256),
                EstimatedTokens = Math.Max(0, request.EstimatedTokens),
                EstimatedCostUsd = Math.Max(0m, request.EstimatedCostUsd),
                Success = request.Success,
                ErrorCode = Truncate(request.ErrorCode, 128),
                MetadataJson = Truncate(request.MetadataJson, MetadataLimit)
            });

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RuntimeTelemetry] Cost record write failed. Agent={AgentRole} Model={Model}", request.AgentRole, request.Model);
        }
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
