using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orka.Core.DTOs;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class BackendCoordinationSmokeTests
{
    [Fact]
    public async Task ChatQuizEvidence_RemainsVisibleFromRootDashboardScope()
    {
        await using var factory = new ApiSmokeFactory("Development", configureServices: services =>
        {
            services.RemoveAll<IChatTurnPostProcessor>();
            services.AddSingleton<IChatTurnPostProcessor, NoopPostProcessor>();
            services.RemoveAll<IBackgroundTaskQueue>();
            services.AddSingleton<IBackgroundTaskQueue, NoopBackgroundTaskQueue>();
        });
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "coord-smoke");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Smoke");
        await CoordinationTestHelpers.SeedSourceAsync(factory, user.UserId, tree.LessonId, "lesson-source", "root dashboard evidence");
        var sessionId = await SeedQuizSessionAsync(factory, user.UserId, tree.RootId);

        var quiz = await user.Client.PostAsJsonAsync("/api/chat/message", new
        {
            sessionId,
            topicId = tree.RootId,
            content = "**Quiz Cevabim:** 1/1 Dogru"
        });
        quiz.EnsureSuccessStatusCode();

        var dashboard = await user.Client.GetFromJsonAsync<DashboardTodayDto>("/api/dashboard/today");
        Assert.NotNull(dashboard?.CoordinationScope);
        Assert.Equal(tree.RootId, dashboard!.CoordinationScope!.RootTopicId);
        Assert.True(dashboard.CoordinationScope.TreeTopicCount >= 3);
        Assert.True(dashboard.CoordinationScope.QuizAttemptCount >= 1);
        Assert.True(dashboard.CoordinationScope.LearningSignalCount >= 1);
        Assert.True(dashboard.CoordinationScope.SourceCount >= 1);
        Assert.NotNull(dashboard.CoordinationHealth);
        Assert.Contains(dashboard.CoordinationHealth!.Metrics, m =>
            m.Key == "quizCoverage" && m.Count >= 1 && m.Status == "healthy");
        Assert.Contains(dashboard.CoordinationHealth.Metrics, m =>
            m.Key == "sourceCoverage" && m.Count >= 1 && m.Status == "healthy");
        Assert.Contains(dashboard.CoordinationHealth.Metrics, m =>
            m.Key == "learningProfileCoverage" && m.Count >= 1);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        Assert.False(await db.QuizAttempts.AnyAsync(a => a.UserId != user.UserId));
    }

    private static async Task<Guid> SeedQuizSessionAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var session = new Orka.Core.Entities.Session
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionNumber = 1,
            CurrentState = SessionState.QuizMode,
            PendingQuiz = """[{"question":"q","answer":"a"}]""",
            CreatedAt = DateTime.UtcNow
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    private sealed class NoopPostProcessor : IChatTurnPostProcessor
    {
        public ValueTask ScheduleAsync(ChatTurnPostProcessRequest request, CancellationToken ct = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class NoopBackgroundTaskQueue : IBackgroundTaskQueue
    {
        public ValueTask QueueAsync(BackgroundTaskItem item, CancellationToken ct = default) =>
            ValueTask.CompletedTask;
    }
}
