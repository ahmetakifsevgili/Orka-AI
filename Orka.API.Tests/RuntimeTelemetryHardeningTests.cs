using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Orka.Core.DTOs;
using Orka.Core.Entities;
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

        var topicId = Guid.NewGuid();
        await service.RecordCostAsync(new CostRecordRequest(
            UserId: Guid.NewGuid(),
            SessionId: Guid.NewGuid(),
            TopicId: topicId,
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
        Assert.Equal(topicId, cost.TopicId);
        Assert.Equal(0, cost.EstimatedTokens);
        Assert.Equal(0m, cost.EstimatedCostUsd);
    }

    [Theory]
    [InlineData("TutorAgent", "Tutor")]
    [InlineData("DeepPlanAgent", "DeepPlan")]
    [InlineData("AIAgentFactory", "AIAgentFactory")]
    public async Task RuntimeTelemetry_NormalizesKnownCostAgentRoles(string inputRole, string expectedRole)
    {
        await using var db = CreateDb();
        var service = new RuntimeTelemetryService(db, NullLogger<RuntimeTelemetryService>.Instance);

        await service.RecordCostAsync(new CostRecordRequest(
            UserId: Guid.NewGuid(),
            SessionId: null,
            TopicId: null,
            MessageId: null,
            AgentRole: inputRole,
            Provider: "test-provider",
            Model: "test-model",
            EstimatedTokens: 1,
            EstimatedCostUsd: 0.01m,
            Success: true,
            ErrorCode: null,
            MetadataJson: null));

        var cost = await db.CostRecords.SingleAsync();
        Assert.Equal(expectedRole, cost.AgentRole);
    }

    [Fact]
    public void AgentEvaluation_UniqueGuard_IsModeledAndMigrationDedupes()
    {
        using var db = CreateDb();
        var entity = db.Model.FindEntityType(typeof(AgentEvaluation))
            ?? throw new InvalidOperationException("AgentEvaluation model missing.");

        Assert.Equal(128, entity.FindProperty(nameof(AgentEvaluation.AgentRole))?.GetMaxLength());
        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(p => p.Name).SequenceEqual([
                nameof(AgentEvaluation.MessageId),
                nameof(AgentEvaluation.AgentRole)
            ]));

        var migration = ReadRepoFile("Orka.Infrastructure/Migrations/20260512110000_AddAgentEvaluationUniqueGuard.cs");
        Assert.Contains("ROW_NUMBER()", migration);
        Assert.Contains("PARTITION BY [MessageId], [AgentRole]", migration);
        Assert.Contains("IX_AgentEvaluations_MessageId_AgentRole", migration);
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
        var queue = new BackgroundTaskQueue(
            new AsyncLocalAiRequestContextAccessor(),
            NullLogger<BackgroundTaskQueue>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await queue.QueueAsync(new BackgroundTaskItem(
                "",
                null,
                null,
                _ => Task.CompletedTask)));
    }

    [Fact]
    public async Task BackgroundQueue_PushesAiRequestContextForJob()
    {
        var accessor = new AsyncLocalAiRequestContextAccessor();
        var queue = new BackgroundTaskQueue(accessor, NullLogger<BackgroundTaskQueue>.Instance);
        var userId = Guid.NewGuid();
        var observed = new TaskCompletionSource<AiRequestContext>(TaskCreationOptions.RunContinuationsAsynchronously);

        await queue.StartAsync(CancellationToken.None);
        try
        {
            await queue.QueueAsync(new BackgroundTaskItem(
                "ai-context-test",
                userId,
                "corr-test",
                _ =>
                {
                    observed.SetResult(accessor.Current);
                    return Task.CompletedTask;
                }));

            var completed = await Task.WhenAny(observed.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(observed.Task, completed);

            var context = await observed.Task;
            Assert.Equal(userId, context.UserId);
            Assert.Equal("corr-test", context.CorrelationId);
            Assert.Equal("ai-context-test", context.Source);
            Assert.True(context.IsBackground);
        }
        finally
        {
            await queue.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BackgroundQueue_RespectsConfiguredMaxConcurrency()
    {
        var accessor = new AsyncLocalAiRequestContextAccessor();
        var queue = new BackgroundTaskQueue(null, accessor, NullLogger<BackgroundTaskQueue>.Instance, maxConcurrency: 2);
        var running = 0;
        var maxObserved = 0;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remaining = 6;

        await queue.StartAsync(CancellationToken.None);
        try
        {
            for (var i = 0; i < 6; i++)
            {
                await queue.QueueAsync(new BackgroundTaskItem(
                    "concurrency-test",
                    null,
                    null,
                    async _ =>
                    {
                        var current = Interlocked.Increment(ref running);
                        var snapshot = maxObserved;
                        while (current > snapshot)
                        {
                            Interlocked.CompareExchange(ref maxObserved, current, snapshot);
                            snapshot = maxObserved;
                        }

                        await Task.Delay(75);
                        Interlocked.Decrement(ref running);
                        if (Interlocked.Decrement(ref remaining) == 0)
                        {
                            completed.SetResult();
                        }
                    }));
            }

            var finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(completed.Task, finished);
            Assert.Equal(2, maxObserved);
        }
        finally
        {
            await queue.StopAsync(CancellationToken.None);
        }
    }

    private static OrkaDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<OrkaDbContext>()
            .UseInMemoryDatabase($"telemetry-{Guid.NewGuid():N}")
            .Options;

        return new OrkaDbContext(options);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Orka.sln")))
            {
                return File.ReadAllText(Path.Combine(
                    directory.FullName,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)));
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root could not be located.");
    }
}
