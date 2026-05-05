using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Orka.Core.DTOs;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.API.Tests;

public sealed class RuntimeTelemetryHardeningTests
{
    [Fact]
    public async Task RuntimeTelemetry_RecordsToolEventAndCostRecord()
    {
        await using var db = CreateDb();
        var service = new RuntimeTelemetryService(db, NullLogger<RuntimeTelemetryService>.Instance);

        await service.RecordToolEventAsync(new ToolTelemetryEventRequest(
            UserId: Guid.NewGuid(),
            SessionId: Guid.NewGuid(),
            TopicId: Guid.NewGuid(),
            ToolId: "wolfram_alpha",
            CapabilityStatus: "Disabled",
            Provider: "wolfram",
            Model: null,
            LatencyMs: -50,
            Success: false,
            ErrorCode: "provider_missing",
            FallbackUsed: true,
            CorrelationId: "corr-test",
            MetadataJson: new string('x', 5000)));

        await service.RecordCostAsync(new CostRecordRequest(
            UserId: Guid.NewGuid(),
            SessionId: Guid.NewGuid(),
            MessageId: Guid.NewGuid(),
            AgentRole: "Tutor",
            Provider: "AIAgentFactory",
            Model: "unknown-model",
            EstimatedTokens: -20,
            EstimatedCostUsd: -1m,
            Success: true,
            ErrorCode: null,
            MetadataJson: null));

        var tool = await db.ToolTelemetryEvents.SingleAsync();
        Assert.Equal("wolfram_alpha", tool.ToolId);
        Assert.Equal(0, tool.LatencyMs);
        Assert.True(tool.FallbackUsed);
        Assert.True(tool.MetadataJson!.Length <= 4000);

        var cost = await db.CostRecords.SingleAsync();
        Assert.Equal("Tutor", cost.AgentRole);
        Assert.Equal(0, cost.EstimatedTokens);
        Assert.Equal(0m, cost.EstimatedCostUsd);
    }

    [Fact]
    public void TokenCostEstimator_UnknownModelUsesSafeFallback()
    {
        ITokenCostEstimator estimator = new TokenCostEstimator();

        var (tokens, cost) = estimator.Estimate("not-a-real-model", "hello", "world");

        Assert.True(tokens > 0);
        Assert.True(cost >= 0);
    }

    [Fact]
    public async Task BackgroundQueue_RejectsMissingJobType()
    {
        var queue = new BackgroundTaskQueue(NullLogger<BackgroundTaskQueue>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await queue.QueueAsync(new BackgroundTaskItem(
                "",
                null,
                null,
                _ => Task.CompletedTask)));
    }

    private static OrkaDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<OrkaDbContext>()
            .UseInMemoryDatabase($"telemetry-{Guid.NewGuid():N}")
            .Options;

        return new OrkaDbContext(options);
    }
}
