using System.Text.Json;
using Xunit;

namespace Orka.API.Tests;

public sealed class DashboardAggregationTests
{
    [Fact]
    public async Task TodayDashboard_AggregatesRootTreeEvidenceWithoutCrossUserLeakage()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "dash-a");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "dash-b");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Dash");
        var foreignChild = await CoordinationTestHelpers.SeedTopicAsync(factory, other.UserId, "Foreign Dash Child", tree.RootId);

        await CoordinationTestHelpers.SeedDashboardEvidenceAsync(factory, user.UserId, tree.LessonId, "Lesson Weak");
        await CoordinationTestHelpers.SeedDashboardEvidenceAsync(factory, other.UserId, foreignChild, "Foreign Weak");
        await CoordinationTestHelpers.SeedSourceAsync(factory, user.UserId, tree.LessonId, "Lesson Source", "lesson dashboard source");
        await CoordinationTestHelpers.SeedSourceAsync(factory, other.UserId, foreignChild, "Foreign Source", "foreign dashboard source");

        var response = await user.Client.GetAsync("/api/dashboard/today");
        response.EnsureSuccessStatusCode();

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;
        Assert.True(root.GetProperty("hasRealLearningData").GetBoolean());

        var weakConcepts = root.GetProperty("weakConcepts").EnumerateArray().ToList();
        Assert.Contains(weakConcepts, c => c.GetProperty("label").GetString() == "Lesson Weak");
        Assert.DoesNotContain(weakConcepts, c => c.GetProperty("label").GetString() == "Foreign Weak");

        var sourceHealth = root.GetProperty("sourceHealth");
        Assert.Equal("unverified", sourceHealth.GetProperty("status").GetString());

        var coordinationScope = root.GetProperty("coordinationScope");
        Assert.Equal(tree.RootId, coordinationScope.GetProperty("rootTopicId").GetGuid());
        Assert.Equal(3, coordinationScope.GetProperty("treeTopicCount").GetInt32());
        Assert.Equal(1, coordinationScope.GetProperty("sourceCount").GetInt32());
        Assert.Equal(1, coordinationScope.GetProperty("quizAttemptCount").GetInt32());
        Assert.Equal(1, coordinationScope.GetProperty("learningSignalCount").GetInt32());

        var coordinationHealth = root.GetProperty("coordinationHealth");
        Assert.NotEqual("no_plan", coordinationHealth.GetProperty("overallStatus").GetString());
        var metricKeys = coordinationHealth.GetProperty("metrics").EnumerateArray()
            .Select(m => m.GetProperty("key").GetString())
            .ToList();
        Assert.Contains("topicTreeCompleteness", metricKeys);
        Assert.Contains("sourceCoverage", metricKeys);
        Assert.Contains("learningProfileCoverage", metricKeys);
    }
}
