using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.Entities;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class DashboardCoordinationHealthTests
{
    [Fact]
    public async Task TodayDashboard_CoordinationHealthAggregatesRootTreeEvidence()
    {
        using var factory = new ApiSmokeFactory(
            "Development",
            configurationOverrides: new Dictionary<string, string?>
            {
                ["AI:Cost:UserDailyTokenLimit"] = "1000",
                ["AI:Cost:UserDailyUsdLimit"] = "2.0"
            });
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "dash-health-a");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "dash-health-b");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Health");
        var foreignChild = await CoordinationTestHelpers.SeedTopicAsync(factory, other.UserId, "Foreign Health", tree.RootId);

        await CoordinationTestHelpers.SeedWikiPageAsync(factory, user.UserId, tree.LessonId, "Lesson Wiki", "lesson block");
        await CoordinationTestHelpers.SeedSourceAsync(factory, user.UserId, tree.LessonId, "Lesson Source", "lesson source");
        await CoordinationTestHelpers.SeedDashboardEvidenceAsync(factory, user.UserId, tree.LessonId, "Health Weak");
        await CoordinationTestHelpers.SeedDashboardEvidenceAsync(factory, other.UserId, foreignChild, "Foreign Weak");
        await SeedHealthRuntimeEvidenceAsync(factory, user.UserId, tree.LessonId, true);
        await SeedHealthRuntimeEvidenceAsync(factory, other.UserId, foreignChild, false);

        var response = await user.Client.GetAsync("/api/dashboard/today");
        response.EnsureSuccessStatusCode();

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;
        var health = root.GetProperty("coordinationHealth");
        Assert.NotEqual("no_plan", health.GetProperty("overallStatus").GetString());
        Assert.Equal(tree.RootId, health.GetProperty("rootTopicId").GetGuid());

        var metrics = health.GetProperty("metrics").EnumerateArray().ToDictionary(
            m => m.GetProperty("key").GetString()!,
            m => m);

        Assert.Equal("healthy", metrics["wikiReadiness"].GetProperty("status").GetString());
        Assert.Equal("healthy", metrics["sourceCoverage"].GetProperty("status").GetString());
        Assert.Equal("healthy", metrics["quizCoverage"].GetProperty("status").GetString());
        Assert.Equal("healthy", metrics["learningProfileCoverage"].GetProperty("status").GetString());
        Assert.Equal("healthy", metrics["chatPostProcessingHealth"].GetProperty("status").GetString());
        Assert.Equal("healthy", metrics["ragScopeCoverage"].GetProperty("status").GetString());
        Assert.Equal("healthy", metrics["sourceQuality"].GetProperty("status").GetString());
        Assert.Equal("healthy", metrics["costQuotaState"].GetProperty("status").GetString());

        Assert.Equal(1, metrics["chatPostProcessingHealth"].GetProperty("count").GetInt32());
        Assert.Equal(1, metrics["chatPostProcessingHealth"].GetProperty("total").GetInt32());
        Assert.Equal(1, metrics["sourceQuality"].GetProperty("count").GetInt32());
        Assert.Equal(1, metrics["sourceQuality"].GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task TodayDashboard_CoordinationHealthReturnsSafeNoPlanForEmptyUser()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "dash-empty");

        var response = await user.Client.GetAsync("/api/dashboard/today");
        response.EnsureSuccessStatusCode();

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var health = body.RootElement.GetProperty("coordinationHealth");
        Assert.Equal("no_plan", health.GetProperty("overallStatus").GetString());
        Assert.Contains("Aktif plan yok", health.GetProperty("userSafeSummary").GetString());
        Assert.Equal("no_plan", health.GetProperty("metrics")[0].GetProperty("status").GetString());
    }

    private static async Task SeedHealthRuntimeEvidenceAsync(
        ApiSmokeFactory factory,
        Guid userId,
        Guid topicId,
        bool includeHealthyQuality)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;
        var sessionId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        db.Sessions.Add(new Session
        {
            Id = sessionId,
            UserId = userId,
            TopicId = topicId,
            SessionNumber = 1,
            CreatedAt = now
        });
        db.Messages.Add(new Message
        {
            Id = messageId,
            SessionId = sessionId,
            UserId = userId,
            Role = "assistant",
            Content = "assistant response",
            CreatedAt = now
        });
        db.AgentEvaluations.Add(new AgentEvaluation
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            UserId = userId,
            MessageId = messageId,
            AgentRole = "TutorAgent",
            UserInput = "question",
            AgentResponse = "assistant response",
            EvaluationScore = 8,
            EvaluatorFeedback = "ok",
            CreatedAt = now
        });
        db.CostRecords.Add(new CostRecord
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SessionId = sessionId,
            TopicId = topicId,
            MessageId = messageId,
            AgentRole = "TutorAgent",
            Provider = "test",
            Model = "test",
            EstimatedTokens = 100,
            EstimatedCostUsd = 0.05m,
            OccurredAt = now
        });
        db.SourceRetrievalRuns.Add(new SourceRetrievalRun
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            Query = "lesson source",
            RetrievalScope = "wiki_topic_tree",
            RequestedTopK = 5,
            RetrievedCount = 1,
            IsEmpty = false,
            MaxScore = 0.9m,
            AverageScore = 0.9m,
            QualityStatus = "healthy",
            CreatedAt = now,
            CompletedAt = now
        });
        db.SkillMasteries.Add(new SkillMastery
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SubTopicTitle = "Health Skill",
            QuizScore = 90,
            MasteredAt = now
        });

        if (includeHealthyQuality)
        {
            db.SourceQualityReports.Add(new SourceQualityReport
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                TopicId = topicId,
                QualityStatus = "healthy",
                RetrievalHealthStatus = "healthy",
                CitationCoverageStatus = "healthy",
                CitationSupportStatus = "supported",
                RetrievalRunCount = 1,
                CitationCheckCount = 1,
                CitationCoverage = 1m,
                ReportJson = "{}",
                GeneratedAt = now,
                CreatedAt = now
            });
        }

        await db.SaveChangesAsync();
    }
}
