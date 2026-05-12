using Microsoft.Extensions.DependencyInjection;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Services;
using System.Text.Json;
using Xunit;

namespace Orka.API.Tests;

public sealed class TopicTreeScopeContractTests
{
    [Fact]
    public async Task ResolveAsync_ComputesRootAncestorsDescendantsAndRejectsForeignTopics()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "scope-a");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "scope-b");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Scope");
        var foreignChild = await CoordinationTestHelpers.SeedTopicAsync(factory, other.UserId, "Foreign Child", tree.RootId);

        using var scope = factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ITopicScopeResolver>();

        var rootScope = await resolver.ResolveAsync(user.UserId, tree.RootId);
        Assert.True(rootScope.IsValid);
        Assert.Equal(tree.RootId, rootScope.RootTopicId);
        Assert.Equal(tree.LessonId, rootScope.ActiveLessonTopicId);
        Assert.Equal([tree.ModuleId, tree.LessonId], rootScope.DescendantTopicIds);
        Assert.DoesNotContain(foreignChild, rootScope.TreeTopicIds);

        var lessonScope = await resolver.ResolveAsync(user.UserId, tree.LessonId);
        Assert.True(lessonScope.IsValid);
        Assert.Equal(tree.RootId, lessonScope.RootTopicId);
        Assert.Equal([tree.ModuleId, tree.RootId], lessonScope.AncestorTopicIds);
        Assert.Contains(tree.RootId, lessonScope.TreeTopicIds);
        Assert.Contains(tree.LessonId, lessonScope.TreeTopicIds);

        var moduleScope = await resolver.ResolveAsync(user.UserId, tree.ModuleId);
        Assert.True(moduleScope.IsValid);
        Assert.Equal(tree.RootId, moduleScope.RootTopicId);
        Assert.Equal([tree.RootId], moduleScope.AncestorTopicIds);
        Assert.Equal([tree.LessonId], moduleScope.DescendantTopicIds);
        Assert.DoesNotContain(foreignChild, moduleScope.TreeTopicIds);

        var inaccessible = await resolver.ResolveAsync(user.UserId, foreignChild);
        Assert.False(inaccessible.IsValid);
        Assert.Empty(inaccessible.TreeTopicIds);
    }

    [Fact]
    public async Task ResolveAsync_SelectsActiveLessonByCurriculumPathWithinCurrentScope()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "active-path");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Path");
        var moduleBId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Path Module B", tree.RootId, order: 1);
        var lessonBId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Path Lesson B", moduleBId, order: 0);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Orka.Infrastructure.Data.OrkaDbContext>();
        var now = DateTime.UtcNow;
        var lessonA = await db.Topics.FindAsync(tree.LessonId);
        var lessonB = await db.Topics.FindAsync(lessonBId);
        Assert.NotNull(lessonA);
        Assert.NotNull(lessonB);
        lessonA!.Order = 0;
        lessonA.LastAccessedAt = now.AddDays(-10);
        lessonB!.Order = 0;
        lessonB.LastAccessedAt = now;
        await db.SaveChangesAsync();

        var resolver = scope.ServiceProvider.GetRequiredService<ITopicScopeResolver>();

        var rootScope = await resolver.ResolveAsync(user.UserId, tree.RootId);
        Assert.Equal(tree.LessonId, rootScope.ActiveLessonTopicId);

        var moduleBScope = await resolver.ResolveAsync(user.UserId, moduleBId);
        Assert.Equal(lessonBId, moduleBScope.ActiveLessonTopicId);

        lessonA.IsMastered = true;
        lessonA.ProgressPercentage = 100;
        await db.SaveChangesAsync();

        var fallbackRootScope = await resolver.ResolveAsync(user.UserId, tree.RootId);
        Assert.Equal(lessonBId, fallbackRootScope.ActiveLessonTopicId);
    }

    [Fact]
    public async Task WikiRootReadsDescendantPagesWhileLessonRemainsExactAndUserSafe()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-a");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "wiki-b");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Wiki");
        var foreignChild = await CoordinationTestHelpers.SeedTopicAsync(factory, other.UserId, "Foreign Wiki Child", tree.RootId);

        await CoordinationTestHelpers.SeedWikiPageAsync(factory, user.UserId, tree.RootId, "Root Wiki", "root wiki content", 1);
        await CoordinationTestHelpers.SeedWikiPageAsync(factory, user.UserId, tree.LessonId, "Lesson Wiki", "lesson wiki content", 2);
        await CoordinationTestHelpers.SeedWikiPageAsync(factory, other.UserId, foreignChild, "Foreign Wiki", "foreign wiki content", 1);

        using var scope = factory.Services.CreateScope();
        var wiki = scope.ServiceProvider.GetRequiredService<IWikiService>();

        var rootPages = (await wiki.GetTopicWikiPagesAsync(tree.RootId, user.UserId)).ToList();
        Assert.Contains(rootPages, p => p.TopicId == tree.RootId);
        Assert.Contains(rootPages, p => p.TopicId == tree.LessonId);
        Assert.DoesNotContain(rootPages, p => p.TopicId == foreignChild);

        var rootFullContent = await wiki.GetWikiFullContentAsync(tree.RootId, user.UserId);
        Assert.Contains("root wiki content", rootFullContent);
        Assert.Contains("lesson wiki content", rootFullContent);
        Assert.DoesNotContain("foreign wiki content", rootFullContent);

        var lessonPages = (await wiki.GetTopicWikiPagesAsync(tree.LessonId, user.UserId)).ToList();
        Assert.Single(lessonPages);
        Assert.Equal(tree.LessonId, lessonPages[0].TopicId);

        var lessonFullContent = await wiki.GetWikiFullContentAsync(tree.LessonId, user.UserId);
        Assert.Contains("lesson wiki content", lessonFullContent);
        Assert.DoesNotContain("root wiki content", lessonFullContent);
    }

    [Fact]
    public async Task LatestSessionRestore_UsesRootTreeScopeWithoutForeignSessionLeakage()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "session-a");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "session-b");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Session");
        var siblingLessonId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Sibling Lesson", tree.ModuleId, order: 1);
        var fallbackLessonId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Fallback Lesson", tree.ModuleId, order: 2);
        var foreignChild = await CoordinationTestHelpers.SeedTopicAsync(factory, other.UserId, "Foreign Session Child", tree.RootId);

        var rootSessionId = await CoordinationTestHelpers.SeedSessionAsync(factory, user.UserId, tree.RootId, DateTime.UtcNow.AddMinutes(-40));
        var moduleSessionId = await CoordinationTestHelpers.SeedSessionAsync(factory, user.UserId, tree.ModuleId, DateTime.UtcNow.AddMinutes(-30));
        var lessonSessionId = await CoordinationTestHelpers.SeedSessionAsync(factory, user.UserId, tree.LessonId, DateTime.UtcNow.AddMinutes(-20));
        var siblingSessionId = await CoordinationTestHelpers.SeedSessionAsync(factory, user.UserId, siblingLessonId, DateTime.UtcNow.AddMinutes(-5));
        await CoordinationTestHelpers.SeedSessionAsync(factory, other.UserId, foreignChild, DateTime.UtcNow);

        using var scope = factory.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<SessionService>();

        var rootLatest = await sessions.GetLatestSessionAsync(tree.RootId, user.UserId);
        Assert.Equal(siblingSessionId, ReadSessionId(rootLatest));

        var lessonLatest = await sessions.GetLatestSessionAsync(tree.LessonId, user.UserId);
        Assert.Equal(lessonSessionId, ReadSessionId(lessonLatest));

        var fallbackLessonLatest = await sessions.GetLatestSessionAsync(fallbackLessonId, user.UserId);
        Assert.Equal(moduleSessionId, ReadSessionId(fallbackLessonLatest));
        Assert.NotEqual(siblingSessionId, ReadSessionId(fallbackLessonLatest));
        Assert.NotEqual(rootSessionId, ReadSessionId(fallbackLessonLatest));
    }

    private static Guid ReadSessionId(object? latest)
    {
        Assert.NotNull(latest);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(latest));
        return json.RootElement.GetProperty("sessionId").GetGuid();
    }
}
