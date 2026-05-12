using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.Entities;
using Orka.Infrastructure.Data;

namespace Orka.API.Tests;

internal static class CoordinationTestHelpers
{
    public static async Task<CoordinationTestUser> RegisterAuthenticatedClientAsync(
        ApiSmokeFactory factory,
        string prefix = "coord")
    {
        var client = factory.CreateClient();
        var email = $"{prefix}-{Guid.NewGuid():N}@orka.local";
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Coord",
            lastName = "User",
            email,
            password = "CoordPass123!"
        });
        response.EnsureSuccessStatusCode();

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var token = body.RootElement.GetProperty("token").GetString()
                    ?? throw new InvalidOperationException("Register token missing.");
        var userId = Guid.Parse(body.RootElement.GetProperty("user").GetProperty("id").GetString()!);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new CoordinationTestUser(client, userId);
    }

    public static async Task<CoordinationTopicTree> SeedTopicTreeAsync(
        ApiSmokeFactory factory,
        Guid userId,
        string prefix = "Coord")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;

        var root = NewTopic(userId, $"{prefix} Root", null, 0, now.AddMinutes(-3));
        var module = NewTopic(userId, $"{prefix} Module", root.Id, 0, now.AddMinutes(-2));
        var lesson = NewTopic(userId, $"{prefix} Lesson", module.Id, 0, now.AddMinutes(-1));

        db.Topics.AddRange(root, module, lesson);
        await db.SaveChangesAsync();
        return new CoordinationTopicTree(root.Id, module.Id, lesson.Id);
    }

    public static async Task<Guid> SeedTopicAsync(
        ApiSmokeFactory factory,
        Guid userId,
        string title,
        Guid? parentTopicId = null,
        int order = 0)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var topic = NewTopic(userId, title, parentTopicId, order, DateTime.UtcNow);
        db.Topics.Add(topic);
        await db.SaveChangesAsync();
        return topic.Id;
    }

    public static async Task<Guid> SeedSourceAsync(
        ApiSmokeFactory factory,
        Guid userId,
        Guid topicId,
        string title,
        string text)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var sourceId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        db.LearningSources.Add(new LearningSource
        {
            Id = sourceId,
            UserId = userId,
            TopicId = topicId,
            SourceType = "document",
            Title = title,
            FileName = $"{title}.txt",
            ContentType = "text/plain",
            FileSizeBytes = text.Length,
            PageCount = 1,
            ChunkCount = 1,
            Status = "ready",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.SourceChunks.Add(new SourceChunk
        {
            Id = Guid.NewGuid(),
            LearningSourceId = sourceId,
            PageNumber = 1,
            ChunkIndex = 0,
            Text = text,
            HighlightHint = text,
            CreatedAt = now
        });

        await db.SaveChangesAsync();
        return sourceId;
    }

    public static async Task<Guid> SeedWikiPageAsync(
        ApiSmokeFactory factory,
        Guid userId,
        Guid topicId,
        string title,
        string blockContent,
        int orderIndex = 1)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var pageId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        db.WikiPages.Add(new WikiPage
        {
            Id = pageId,
            UserId = userId,
            TopicId = topicId,
            Title = title,
            Status = "ready",
            OrderIndex = orderIndex,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.WikiBlocks.Add(new WikiBlock
        {
            Id = Guid.NewGuid(),
            WikiPageId = pageId,
            Title = $"{title} Block",
            Content = blockContent,
            OrderIndex = 1,
            CreatedAt = now,
            UpdatedAt = now
        });

        await db.SaveChangesAsync();
        return pageId;
    }

    public static async Task SeedDashboardEvidenceAsync(
        ApiSmokeFactory factory,
        Guid userId,
        Guid topicId,
        string conceptLabel = "Lesson Weak")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;

        var quizAttemptId = Guid.NewGuid();
        db.QuizAttempts.Add(new QuizAttempt
        {
            Id = quizAttemptId,
            UserId = userId,
            TopicId = topicId,
            Question = "Q",
            UserAnswer = "A",
            IsCorrect = false,
            Explanation = "Needs review",
            SkillTag = conceptLabel,
            CreatedAt = now
        });
        db.LearningSignals.Add(new LearningSignal
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            QuizAttemptId = quizAttemptId,
            SignalType = "coordination-test",
            SkillTag = conceptLabel,
            IsPositive = false,
            CreatedAt = now
        });
        db.KnowledgeTracingStates.Add(new KnowledgeTracingState
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ConceptKey = conceptLabel.Replace(' ', '-').ToLowerInvariant(),
            Label = conceptLabel,
            MasteryProbability = 0.20m,
            Confidence = 0.80m,
            EvidenceCount = 1,
            IncorrectCount = 1,
            CreatedAt = now,
            UpdatedAt = now
        });

        await db.SaveChangesAsync();
    }

    public static async Task<Guid> SeedSessionAsync(
        ApiSmokeFactory factory,
        Guid userId,
        Guid topicId,
        DateTime createdAt)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var session = new Session
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionNumber = 1,
            CreatedAt = createdAt
        };

        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    public static async Task<int> CountTopicSourcesAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        return await db.LearningSources.CountAsync(s => s.UserId == userId && s.TopicId == topicId && !s.IsDeleted);
    }

    private static Topic NewTopic(Guid userId, string title, Guid? parentTopicId, int order, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        ParentTopicId = parentTopicId,
        Title = title,
        Category = "Coordination",
        Order = order,
        TotalSections = 1,
        CompletedSections = 0,
        ProgressPercentage = 0,
        CreatedAt = now,
        LastAccessedAt = now
    };
}

internal sealed record CoordinationTestUser(HttpClient Client, Guid UserId);

internal sealed record CoordinationTopicTree(Guid RootId, Guid ModuleId, Guid LessonId);
