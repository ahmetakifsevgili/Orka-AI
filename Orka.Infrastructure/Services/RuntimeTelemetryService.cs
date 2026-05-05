using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

namespace Orka.Infrastructure.Services;

public sealed class RuntimeTelemetryService : IRuntimeTelemetryService
{
    private const int MetadataLimit = 4000;
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
            _db.CostRecords.Add(new CostRecord
            {
                Id = Guid.NewGuid(),
                OccurredAt = DateTime.UtcNow,
                UserId = request.UserId,
                SessionId = request.SessionId,
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
