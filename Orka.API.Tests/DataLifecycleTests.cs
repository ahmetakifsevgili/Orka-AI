using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.API.Tests;

public sealed class DataLifecycleTests
{
    [Fact]
    public async Task DeleteTopic_RemovesNestedTopicScopedDataAndAnonymizesOperationalRecords()
    {
        using var factory = new ApiSmokeFactory();
        var userA = await RegisterAuthenticatedClientAsync(factory);
        var userB = await RegisterAuthenticatedClientAsync(factory);
        var ids = await SeedLifecycleGraphAsync(factory, userA.UserId, userB.UserId);

        var response = await userA.Client.DeleteAsync($"/api/topics/{ids.RootTopicId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        Assert.False(await db.Topics.AnyAsync(t => ids.DeletedTopicIds.Contains(t.Id)));
        Assert.True(await db.Topics.AnyAsync(t => t.Id == ids.PreservedUserTopicId));
        Assert.True(await db.Topics.AnyAsync(t => t.Id == ids.OtherUserTopicId));
        Assert.False(await db.Sessions.AnyAsync(s => ids.DeletedSessionIds.Contains(s.Id)));
        Assert.False(await db.Messages.AnyAsync(m => ids.DeletedMessageIds.Contains(m.Id)));
        Assert.False(await db.WikiPages.AnyAsync(w => ids.DeletedWikiPageIds.Contains(w.Id)));
        Assert.False(await db.WikiBlocks.AnyAsync(b => ids.DeletedWikiPageIds.Contains(b.WikiPageId)));
        Assert.False(await db.Sources.AnyAsync(s => ids.DeletedWikiPageIds.Contains(s.WikiPageId)));
        Assert.False(await db.LearningSources.AnyAsync(s => ids.DeletedLearningSourceIds.Contains(s.Id)));
        Assert.False(await db.SourceChunks.AnyAsync(c => ids.DeletedLearningSourceIds.Contains(c.LearningSourceId)));
        Assert.False(await db.QuizRuns.AnyAsync(q => ids.DeletedQuizRunIds.Contains(q.Id)));
        Assert.False(await db.QuizAttempts.AnyAsync(q => ids.DeletedQuizAttemptIds.Contains(q.Id)));
        Assert.False(await db.LearningSignals.AnyAsync(s => ids.DeletedLearningSignalIds.Contains(s.Id)));
        Assert.False(await db.ReviewItems.AnyAsync(r => ids.DeletedReviewItemIds.Contains(r.Id)));
        Assert.False(await db.Flashcards.AnyAsync(f => ids.DeletedFlashcardIds.Contains(f.Id)));
        Assert.False(await db.Bookmarks.AnyAsync(b => b.TopicId == ids.RootTopicId || ids.DeletedMessageIds.Contains(b.MessageId ?? Guid.Empty)));
        Assert.False(await db.Notifications.AnyAsync(n => n.UserId == userA.UserId && n.RelatedEntityId == ids.RootTopicId));
        Assert.False(await db.AgentEvaluations.AnyAsync(e => ids.DeletedMessageIds.Contains(e.MessageId)));

        var telemetry = await db.ToolTelemetryEvents.SingleAsync(t => t.Id == ids.ToolTelemetryId);
        Assert.Null(telemetry.UserId);
        Assert.Null(telemetry.TopicId);
        Assert.Null(telemetry.SessionId);
        Assert.Null(telemetry.MetadataJson);

        var cost = await db.CostRecords.SingleAsync(c => c.Id == ids.CostRecordId);
        Assert.Null(cost.UserId);
        Assert.Null(cost.SessionId);
        Assert.Null(cost.TopicId);
        Assert.Null(cost.MessageId);
        Assert.Null(cost.MetadataJson);
    }

    [Fact]
    public async Task DeleteTopic_WithCrossUserTopic_ReturnsNotFoundAndPreservesData()
    {
        using var factory = new ApiSmokeFactory();
        var userA = await RegisterAuthenticatedClientAsync(factory);
        var userB = await RegisterAuthenticatedClientAsync(factory);
        var ids = await SeedLifecycleGraphAsync(factory, userA.UserId, userB.UserId);

        var response = await userA.Client.DeleteAsync($"/api/topics/{ids.OtherUserTopicId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        Assert.True(await db.Topics.AnyAsync(t => t.Id == ids.OtherUserTopicId));
    }

    [Fact]
    public async Task DeleteAccount_RemovesUserOwnedPiiAndAnonymizesOperationalRecords()
    {
        using var factory = new ApiSmokeFactory();
        var userA = await RegisterAuthenticatedClientAsync(factory);
        var userB = await RegisterAuthenticatedClientAsync(factory);
        var ids = await SeedLifecycleGraphAsync(factory, userA.UserId, userB.UserId);

        var response = await userA.Client.DeleteAsync("/api/user/account");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        Assert.False(await db.Users.AnyAsync(u => u.Id == userA.UserId));
        Assert.True(await db.Users.AnyAsync(u => u.Id == userB.UserId));
        Assert.False(await db.RefreshTokens.AnyAsync(t => t.UserId == userA.UserId));
        Assert.False(await db.Topics.AnyAsync(t => t.UserId == userA.UserId));
        Assert.False(await db.Sessions.AnyAsync(s => s.UserId == userA.UserId));
        Assert.False(await db.Messages.AnyAsync(m => m.UserId == userA.UserId));
        Assert.False(await db.WikiPages.AnyAsync(w => w.UserId == userA.UserId));
        Assert.False(await db.LearningSources.AnyAsync(s => s.UserId == userA.UserId));
        Assert.False(await db.QuizAttempts.AnyAsync(q => q.UserId == userA.UserId));
        Assert.False(await db.LearningSignals.AnyAsync(s => s.UserId == userA.UserId));
        Assert.False(await db.ReviewItems.AnyAsync(r => r.UserId == userA.UserId));
        Assert.False(await db.Flashcards.AnyAsync(f => f.UserId == userA.UserId));
        Assert.False(await db.Notifications.AnyAsync(n => n.UserId == userA.UserId));
        Assert.False(await db.PushSubscriptions.AnyAsync(p => p.UserId == userA.UserId));
        Assert.False(await db.AgentEvaluations.AnyAsync(e => e.UserId == userA.UserId));
        Assert.False(await db.AudioOverviewJobs.AnyAsync(e => e.UserId == userA.UserId));
        Assert.False(await db.ClassroomSessions.AnyAsync(e => e.UserId == userA.UserId));

        var telemetry = await db.ToolTelemetryEvents.SingleAsync(t => t.Id == ids.ToolTelemetryId);
        Assert.Null(telemetry.UserId);
        Assert.Null(telemetry.TopicId);
        Assert.Null(telemetry.SessionId);
        Assert.Null(telemetry.MetadataJson);

        var cost = await db.CostRecords.SingleAsync(c => c.Id == ids.CostRecordId);
        Assert.Null(cost.UserId);
        Assert.Null(cost.SessionId);
        Assert.Null(cost.TopicId);
        Assert.Null(cost.MessageId);
        Assert.Null(cost.MetadataJson);

        Assert.True(await db.Topics.AnyAsync(t => t.Id == ids.OtherUserTopicId));
    }

    [Fact]
    public async Task DeleteTopic_WithRelationalDatabase_DoesNotViolateForeignKeysAndRemovesHighRiskFamilies()
    {
        await using var harness = await RelationalLifecycleHarness.CreateAsync();
        var userA = NewUser("topic-relational-a");
        var userB = NewUser("topic-relational-b");
        harness.Db.Users.AddRange(userA, userB);
        await harness.Db.SaveChangesAsync();
        var ids = await SeedLifecycleGraphCoreAsync(harness.Db, userA.Id, userB.Id);

        var deleted = await harness.Service.DeleteTopicTreeAsync(userA.Id, ids.RootTopicId);

        Assert.True(deleted);
        await AssertHighRiskFamiliesRemovedAsync(harness.Db, userA.Id);
        Assert.False(await harness.Db.Topics.AnyAsync(t => ids.DeletedTopicIds.Contains(t.Id)));
        Assert.True(await harness.Db.Topics.AnyAsync(t => t.Id == ids.PreservedUserTopicId));
        Assert.True(await harness.Db.Topics.AnyAsync(t => t.Id == ids.OtherUserTopicId));
        Assert.Contains(harness.Redis.Purges, p =>
            p.UserId == userA.Id &&
            p.Reason == "topic-deleted" &&
            ids.DeletedTopicIds.All(p.TopicIds.Contains));
    }

    [Fact]
    public async Task DeleteAccount_WithRelationalDatabase_DoesNotViolateForeignKeysAndAnonymizesOperationalRecords()
    {
        await using var harness = await RelationalLifecycleHarness.CreateAsync();
        var userA = NewUser("account-relational-a");
        var userB = NewUser("account-relational-b");
        harness.Db.Users.AddRange(userA, userB);
        await harness.Db.SaveChangesAsync();
        var ids = await SeedLifecycleGraphCoreAsync(harness.Db, userA.Id, userB.Id);

        var deleted = await harness.Service.DeleteAccountAsync(userA.Id);

        Assert.True(deleted);
        Assert.False(await harness.Db.Users.AnyAsync(u => u.Id == userA.Id));
        await AssertHighRiskFamiliesRemovedAsync(harness.Db, userA.Id);

        var telemetry = await harness.Db.ToolTelemetryEvents.SingleAsync(t => t.Id == ids.ToolTelemetryId);
        Assert.Null(telemetry.UserId);
        Assert.Null(telemetry.TopicId);
        Assert.Null(telemetry.SessionId);
        Assert.Null(telemetry.MetadataJson);

        var cost = await harness.Db.CostRecords.SingleAsync(c => c.Id == ids.CostRecordId);
        Assert.Null(cost.UserId);
        Assert.Null(cost.SessionId);
        Assert.Null(cost.TopicId);
        Assert.Null(cost.MessageId);
        Assert.Null(cost.MetadataJson);
        Assert.Contains(harness.Redis.Purges, p =>
            p.UserId == userA.Id &&
            p.Reason == "account-deleted" &&
            ids.DeletedTopicIds.All(p.TopicIds.Contains));
    }

    [Fact]
    public async Task DeleteTopic_DoesNotOverDeleteOtherUserRecordsWithScopedReferenceIds()
    {
        using var factory = new ApiSmokeFactory();
        var userA = await RegisterAuthenticatedClientAsync(factory);
        var userB = await RegisterAuthenticatedClientAsync(factory);
        var ids = await SeedLifecycleGraphAsync(factory, userA.UserId, userB.UserId);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = userB.UserId,
                Type = "shared-reference",
                Title = "Preserve",
                Body = "Other user reference",
                RelatedEntityType = "Topic",
                RelatedEntityId = ids.RootTopicId,
                CreatedAt = DateTime.UtcNow
            });
            db.CostRecords.Add(new CostRecord
            {
                Id = Guid.NewGuid(),
                UserId = userB.UserId,
                TopicId = ids.RootTopicId,
                AgentRole = "Tutor",
                Provider = "test",
                Model = "test",
                EstimatedTokens = 1,
                EstimatedCostUsd = 0.01m,
                MetadataJson = JsonSerializer.Serialize(new { referencedTopicId = ids.RootTopicId }),
                OccurredAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await userA.Client.DeleteAsync($"/api/topics/{ids.RootTopicId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        Assert.True(await verifyDb.Notifications.AnyAsync(n => n.UserId == userB.UserId && n.RelatedEntityId == ids.RootTopicId));
        Assert.True(await verifyDb.CostRecords.AnyAsync(c => c.UserId == userB.UserId && c.TopicId == ids.RootTopicId && c.MetadataJson != null));
    }

    [Fact]
    public async Task DeleteTopic_RedisPurgeFailure_DoesNotBreakDelete()
    {
        using var factory = new ApiSmokeFactory(
            "Development",
            configureServices: services =>
            {
                var descriptors = services
                    .Where(d => d.ServiceType == typeof(IRedisMemoryService))
                    .ToList();
                foreach (var descriptor in descriptors)
                    services.Remove(descriptor);

                services.AddSingleton<IRedisMemoryService, FailingPurgeRedisMemoryService>();
            });
        var userA = await RegisterAuthenticatedClientAsync(factory);
        var userB = await RegisterAuthenticatedClientAsync(factory);
        var ids = await SeedLifecycleGraphAsync(factory, userA.UserId, userB.UserId);

        var response = await userA.Client.DeleteAsync($"/api/topics/{ids.RootTopicId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        Assert.False(await db.Topics.AnyAsync(t => ids.DeletedTopicIds.Contains(t.Id)));
    }

    private static async Task<TestUser> RegisterAuthenticatedClientAsync(ApiSmokeFactory factory)
    {
        var client = factory.CreateClient();
        var email = $"lifecycle-{Guid.NewGuid():N}@orka.local";
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Lifecycle",
            lastName = "User",
            email,
            password = "LifecyclePass123!"
        });
        response.EnsureSuccessStatusCode();

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var token = body.RootElement.GetProperty("token").GetString()
                    ?? throw new InvalidOperationException("Register token missing.");
        var userId = Guid.Parse(body.RootElement.GetProperty("user").GetProperty("id").GetString()
                                ?? throw new InvalidOperationException("Register user id missing."));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new TestUser(client, userId);
    }

    private static async Task<LifecycleSeedIds> SeedLifecycleGraphAsync(ApiSmokeFactory factory, Guid userId, Guid otherUserId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;

        var root = NewTopic(userId, "Root");
        var child = NewTopic(userId, "Child", root.Id);
        var grandChild = NewTopic(userId, "GrandChild", child.Id);
        var preservedTopic = NewTopic(userId, "Preserved");
        var otherUserTopic = NewTopic(otherUserId, "Other User");

        var session = new Session { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, SessionNumber = 1, CreatedAt = now };
        var message = new Message { Id = Guid.NewGuid(), UserId = userId, SessionId = session.Id, Role = "user", Content = "PII prompt", CreatedAt = now };
        var wikiPage = new WikiPage { Id = Guid.NewGuid(), UserId = userId, TopicId = grandChild.Id, Title = "Wiki", Status = "ready", CreatedAt = now, UpdatedAt = now };
        var wikiBlock = new WikiBlock { Id = Guid.NewGuid(), WikiPageId = wikiPage.Id, Title = "Block", Content = "PII wiki", CreatedAt = now, UpdatedAt = now };
        var wikiSource = new Source { Id = Guid.NewGuid(), WikiPageId = wikiPage.Id, Type = "web", Title = "Source", Url = "https://example.test", CreatedAt = now };
        var learningSource = new LearningSource
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = root.Id,
            SessionId = session.Id,
            SourceType = "document",
            Title = "Source",
            FileName = "notes.txt",
            ContentType = "text/plain",
            FileSizeBytes = 64,
            PageCount = 1,
            ChunkCount = 1,
            Status = "ready",
            CreatedAt = now,
            UpdatedAt = now
        };
        var chunk = new SourceChunk { Id = Guid.NewGuid(), LearningSourceId = learningSource.Id, PageNumber = 1, ChunkIndex = 0, Text = "PII chunk", CreatedAt = now };
        var quizRun = new QuizRun { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, SessionId = session.Id, QuizType = "lesson", Status = "active", CreatedAt = now };
        var quizAttempt = new QuizAttempt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = child.Id,
            SessionId = session.Id,
            QuizRunId = quizRun.Id,
            Question = "Q",
            UserAnswer = "A",
            Explanation = "E",
            CreatedAt = now
        };
        var signal = new LearningSignal { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, SessionId = session.Id, QuizAttemptId = quizAttempt.Id, SignalType = "lifecycle", PayloadJson = "{\"pii\":true}", CreatedAt = now };
        var flashcard = new Flashcard { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, LearningSourceId = learningSource.Id, WikiPageId = wikiPage.Id, QuizAttemptId = quizAttempt.Id, Front = "front", Back = "back", CreatedAt = now, UpdatedAt = now };
        var review = new ReviewItem { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, ReviewKey = $"review-{Guid.NewGuid():N}", DueAt = now, CreatedAt = now, UpdatedAt = now, QuizAttemptId = quizAttempt.Id, LearningSignalId = signal.Id, FlashcardId = flashcard.Id };
        var daily = new DailyChallenge { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, Date = now.Date, ReviewItemId = review.Id, QuestionsJson = "[]", CreatedAt = now };
        var xp = new XpEvent { Id = Guid.NewGuid(), UserId = userId, EventKey = $"xp-{Guid.NewGuid():N}", EventType = "daily", XpDelta = 10, RelatedEntityType = "DailyChallenge", RelatedEntityId = daily.Id, CreatedAt = now };
        var submission = new DailyChallengeSubmission { Id = Guid.NewGuid(), UserId = userId, DailyChallengeId = daily.Id, XpEventId = xp.Id, Answer = "answer", Quality = 4, XpAwarded = 10, CreatedAt = now };
        var bookmark = new Bookmark { Id = Guid.NewGuid(), UserId = userId, TopicId = root.Id, SessionId = session.Id, MessageId = message.Id, LearningSourceId = learningSource.Id, WikiPageId = wikiPage.Id, ReviewItemId = review.Id, FlashcardId = flashcard.Id, Title = "Bookmark", CreatedAt = now, UpdatedAt = now };
        var notification = new Notification { Id = Guid.NewGuid(), UserId = userId, Type = "topic", Title = "Topic", Body = "PII", RelatedEntityType = "Topic", RelatedEntityId = root.Id, CreatedAt = now };
        var push = new PushSubscription { Id = Guid.NewGuid(), UserId = userId, Endpoint = $"https://push.example/{Guid.NewGuid():N}", Status = "active", CreatedAt = now, UpdatedAt = now };
        var refresh = new RefreshToken { Id = Guid.NewGuid(), UserId = userId, TokenHash = new string('a', 64), TokenFamilyId = Guid.NewGuid(), ExpiresAt = now.AddDays(7), CreatedAt = now };
        var telemetry = new ToolTelemetryEvent { Id = Guid.NewGuid(), UserId = userId, TopicId = root.Id, SessionId = session.Id, ToolId = "lifecycle", Success = true, MetadataJson = "{\"pii\":true}", OccurredAt = now };
        var cost = new CostRecord { Id = Guid.NewGuid(), UserId = userId, SessionId = session.Id, TopicId = root.Id, MessageId = message.Id, AgentRole = "Tutor", Provider = "test", Model = "test", EstimatedTokens = 10, EstimatedCostUsd = 0.01m, MetadataJson = "{\"pii\":true}", OccurredAt = now };
        var evaluation = new AgentEvaluation { Id = Guid.NewGuid(), UserId = userId, SessionId = session.Id, MessageId = message.Id, AgentRole = "Tutor", UserInput = "PII input", AgentResponse = "PII response", EvaluationScore = 8, EvaluatorFeedback = "ok", CreatedAt = now };
        var audio = new AudioOverviewJob { Id = Guid.NewGuid(), UserId = userId, TopicId = root.Id, SessionId = session.Id, Status = "ready", Script = "PII audio", CreatedAt = now, UpdatedAt = now };
        var classroom = new ClassroomSession { Id = Guid.NewGuid(), UserId = userId, TopicId = root.Id, SessionId = session.Id, AudioOverviewJobId = audio.Id, Transcript = "PII transcript", LastSegment = "PII", CreatedAt = now, UpdatedAt = now };
        var interaction = new ClassroomInteraction { Id = Guid.NewGuid(), ClassroomSessionId = classroom.Id, Question = "PII question", AnswerScript = "PII answer", CreatedAt = now };

        db.AddRange(
            root, child, grandChild, preservedTopic, otherUserTopic,
            session, message,
            wikiPage, wikiBlock, wikiSource,
            learningSource, chunk,
            quizRun, quizAttempt, signal, flashcard, review, daily, xp, submission,
            bookmark, notification, push, refresh, telemetry, cost, evaluation, audio, classroom, interaction);

        await db.SaveChangesAsync();

        return new LifecycleSeedIds(
            root.Id,
            preservedTopic.Id,
            otherUserTopic.Id,
            [root.Id, child.Id, grandChild.Id],
            [session.Id],
            [message.Id],
            [wikiPage.Id],
            [learningSource.Id],
            [quizRun.Id],
            [quizAttempt.Id],
            [signal.Id],
            [review.Id],
            [flashcard.Id],
            telemetry.Id,
            cost.Id);
    }

    private static async Task<LifecycleSeedIds> SeedLifecycleGraphCoreAsync(OrkaDbContext db, Guid userId, Guid otherUserId)
    {
        var now = DateTime.UtcNow;

        var root = NewTopic(userId, "Root");
        var child = NewTopic(userId, "Child", root.Id);
        var grandChild = NewTopic(userId, "GrandChild", child.Id);
        var preservedTopic = NewTopic(userId, "Preserved");
        var otherUserTopic = NewTopic(otherUserId, "Other User");

        var session = new Session { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, SessionNumber = 1, CreatedAt = now };
        var message = new Message { Id = Guid.NewGuid(), UserId = userId, SessionId = session.Id, Role = "user", Content = "PII prompt", CreatedAt = now };
        var wikiPage = new WikiPage { Id = Guid.NewGuid(), UserId = userId, TopicId = grandChild.Id, Title = "Wiki", Status = "ready", CreatedAt = now, UpdatedAt = now };
        var wikiBlock = new WikiBlock { Id = Guid.NewGuid(), WikiPageId = wikiPage.Id, Title = "Block", Content = "PII wiki", CreatedAt = now, UpdatedAt = now };
        var wikiSource = new Source { Id = Guid.NewGuid(), WikiPageId = wikiPage.Id, Type = "web", Title = "Source", Url = "https://example.test", CreatedAt = now };
        var learningSource = new LearningSource { Id = Guid.NewGuid(), UserId = userId, TopicId = root.Id, SessionId = session.Id, SourceType = "document", Title = "Source", FileName = "notes.txt", ContentType = "text/plain", FileSizeBytes = 64, PageCount = 1, ChunkCount = 1, Status = "ready", CreatedAt = now, UpdatedAt = now };
        var chunk = new SourceChunk { Id = Guid.NewGuid(), LearningSourceId = learningSource.Id, PageNumber = 1, ChunkIndex = 0, Text = "PII chunk", CreatedAt = now };
        var quizRun = new QuizRun { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, SessionId = session.Id, QuizType = "lesson", Status = "active", CreatedAt = now };
        var quizAttempt = new QuizAttempt { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, SessionId = session.Id, QuizRunId = quizRun.Id, Question = "Q", UserAnswer = "A", Explanation = "E", CreatedAt = now };
        var signal = new LearningSignal { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, SessionId = session.Id, QuizAttemptId = quizAttempt.Id, SignalType = "lifecycle", PayloadJson = "{\"pii\":true}", CreatedAt = now };
        var flashcard = new Flashcard { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, LearningSourceId = learningSource.Id, WikiPageId = wikiPage.Id, QuizAttemptId = quizAttempt.Id, Front = "front", Back = "back", CreatedAt = now, UpdatedAt = now };
        var review = new ReviewItem { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, ReviewKey = $"review-{Guid.NewGuid():N}", DueAt = now, CreatedAt = now, UpdatedAt = now, QuizAttemptId = quizAttempt.Id, LearningSignalId = signal.Id, FlashcardId = flashcard.Id };
        var daily = new DailyChallenge { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, Date = now.Date, ReviewItemId = review.Id, QuestionsJson = "[]", CreatedAt = now };
        var xp = new XpEvent { Id = Guid.NewGuid(), UserId = userId, EventKey = $"xp-{Guid.NewGuid():N}", EventType = "daily", XpDelta = 10, RelatedEntityType = "DailyChallenge", RelatedEntityId = daily.Id, CreatedAt = now };
        var submission = new DailyChallengeSubmission { Id = Guid.NewGuid(), UserId = userId, DailyChallengeId = daily.Id, XpEventId = xp.Id, Answer = "answer", Quality = 4, XpAwarded = 10, CreatedAt = now };
        var bookmark = new Bookmark { Id = Guid.NewGuid(), UserId = userId, TopicId = root.Id, SessionId = session.Id, MessageId = message.Id, LearningSourceId = learningSource.Id, WikiPageId = wikiPage.Id, ReviewItemId = review.Id, FlashcardId = flashcard.Id, Title = "Bookmark", CreatedAt = now, UpdatedAt = now };
        var notification = new Notification { Id = Guid.NewGuid(), UserId = userId, Type = "topic", Title = "Topic", Body = "PII", RelatedEntityType = "Topic", RelatedEntityId = root.Id, CreatedAt = now };
        var push = new PushSubscription { Id = Guid.NewGuid(), UserId = userId, Endpoint = $"https://push.example/{Guid.NewGuid():N}", Status = "active", CreatedAt = now, UpdatedAt = now };
        var refresh = new RefreshToken { Id = Guid.NewGuid(), UserId = userId, TokenHash = new string('b', 64), TokenFamilyId = Guid.NewGuid(), ExpiresAt = now.AddDays(7), CreatedAt = now };
        var telemetry = new ToolTelemetryEvent { Id = Guid.NewGuid(), UserId = userId, TopicId = root.Id, SessionId = session.Id, ToolId = "lifecycle", Success = true, MetadataJson = "{\"pii\":true}", OccurredAt = now };
        var metadataOnlyTelemetry = new ToolTelemetryEvent { Id = Guid.NewGuid(), UserId = userId, ToolId = "metadata", Success = false, MetadataJson = JsonSerializer.Serialize(new { topicId = root.Id, prompt = "PII prompt" }), OccurredAt = now };
        var cost = new CostRecord { Id = Guid.NewGuid(), UserId = userId, SessionId = session.Id, TopicId = root.Id, MessageId = message.Id, AgentRole = "Tutor", Provider = "test", Model = "test", EstimatedTokens = 10, EstimatedCostUsd = 0.01m, MetadataJson = "{\"pii\":true}", OccurredAt = now };
        var metadataOnlyCost = new CostRecord { Id = Guid.NewGuid(), UserId = userId, TopicId = root.Id, AgentRole = "Tutor", Provider = "test", Model = "test", EstimatedTokens = 10, EstimatedCostUsd = 0.01m, MetadataJson = JsonSerializer.Serialize(new { topicId = root.Id, prompt = "PII prompt" }), OccurredAt = now };
        var evaluation = new AgentEvaluation { Id = Guid.NewGuid(), UserId = userId, SessionId = session.Id, MessageId = message.Id, AgentRole = "Tutor", UserInput = "PII input", AgentResponse = "PII response", EvaluationScore = 8, EvaluatorFeedback = "ok", CreatedAt = now };
        var audio = new AudioOverviewJob { Id = Guid.NewGuid(), UserId = userId, TopicId = root.Id, SessionId = session.Id, Status = "ready", Script = "PII audio", CreatedAt = now, UpdatedAt = now };
        var classroom = new ClassroomSession { Id = Guid.NewGuid(), UserId = userId, TopicId = root.Id, SessionId = session.Id, AudioOverviewJobId = audio.Id, Transcript = "PII transcript", LastSegment = "PII", CreatedAt = now, UpdatedAt = now };
        var interaction = new ClassroomInteraction { Id = Guid.NewGuid(), ClassroomSessionId = classroom.Id, Question = "PII question", AnswerScript = "PII answer", CreatedAt = now };

        var conceptGraph = new ConceptGraphSnapshot { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, IntentHash = "intent", ApprovedResearchIntent = "learn", TopicTitle = "Lifecycle", SourceBundleHash = "bundle", GraphJson = "{\"pii\":true}", CreatedAt = now };
        var concept = new LearningConcept { Id = Guid.NewGuid(), ConceptGraphSnapshotId = conceptGraph.Id, StableKey = "concept", Label = "Concept", Description = "PII concept", CreatedAt = now };
        var relation = new ConceptRelation { Id = Guid.NewGuid(), ConceptGraphSnapshotId = conceptGraph.Id, SourceConceptKey = "concept", TargetConceptKey = "next", CreatedAt = now };
        var outcome = new LearningOutcome { Id = Guid.NewGuid(), ConceptGraphSnapshotId = conceptGraph.Id, StableKey = "outcome", Label = "Outcome", Description = "PII outcome", CreatedAt = now };
        var outcomeAlignment = new OutcomeAlignment { Id = Guid.NewGuid(), LearningOutcomeId = outcome.Id, EntityType = "quiz_attempt", EntityId = quizAttempt.Id, MetadataJson = "{\"pii\":true}", CreatedAt = now };
        var assessmentItem = new AssessmentItem { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, QuizRunId = quizRun.Id, ConceptGraphSnapshotId = conceptGraph.Id, LearningConceptId = concept.Id, AssessmentItemKey = "item", ConceptKey = "concept", ConceptLabel = "Concept", CreatedAt = now };
        var skillMastery = new SkillMastery { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, SubTopicTitle = "PII skill", MasteredAt = now, QuizScore = 90 };
        var diagnostic = new DiagnosticProfile { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, QuizRunId = quizRun.Id, ConceptGraphSnapshotId = conceptGraph.Id, AnsweredCount = 1, CorrectCount = 1, AccuracyPercent = 100, ProfileJson = "{\"pii\":true}", CreatedAt = now, UpdatedAt = now };
        var conceptMastery = new ConceptMastery { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, ConceptGraphSnapshotId = conceptGraph.Id, ConceptKey = "concept", Label = "Concept", MasteryScore = 0.5m, Confidence = 0.5m, CreatedAt = now, UpdatedAt = now };
        var learningEvent = new LearningEvent { Id = Guid.NewGuid(), UserId = userId, TopicId = null, SessionId = session.Id, QuizAttemptId = quizAttempt.Id, AssessmentItemId = assessmentItem.Id, EventType = "answer", Verb = "answered", ObjectType = "assessment", PayloadJson = "{\"pii\":true}", CreatedAt = now, OccurredAt = now };
        var violation = new LearningEventSchemaViolation { Id = Guid.NewGuid(), LearningEventId = learningEvent.Id, UserId = userId, TopicId = null, EventType = "answer", ViolationCode = "bad", ViolationDetail = "PII violation", PayloadJson = "{\"pii\":true}", CreatedAt = now };
        var graphQuality = new ConceptGraphQualityRun { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, ConceptGraphSnapshotId = conceptGraph.Id, CreatedAt = now };
        var assessmentQuality = new AssessmentQualityRun { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, QuizRunId = quizRun.Id, ConceptGraphSnapshotId = conceptGraph.Id, CreatedAt = now };
        var itemStat = new AssessmentItemStat { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, AssessmentItemId = assessmentItem.Id, ConceptGraphSnapshotId = conceptGraph.Id, ConceptKey = "concept", CreatedAt = now, UpdatedAt = now };
        var knowledge = new KnowledgeTracingState { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, ConceptGraphSnapshotId = conceptGraph.Id, ConceptKey = "concept", Label = "Concept", CreatedAt = now, UpdatedAt = now };
        var resourceAlignment = new ResourceConceptAlignment { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, ConceptGraphSnapshotId = conceptGraph.Id, SourceId = learningSource.Id.ToString("D"), SourceTitle = "PII source", ConceptKey = "concept", CreatedAt = now };
        var learningQuality = new LearningQualityReport { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, ConceptGraphSnapshotId = conceptGraph.Id, ReportJson = "{\"pii\":true}", CreatedAt = now, GeneratedAt = now };
        var policyTrace = new TutorPolicyTrace { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, SessionId = session.Id, ConceptGraphSnapshotId = conceptGraph.Id, InputHash = "hash", CreatedAt = now };

        var workingMemory = new TutorWorkingMemorySnapshot { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, SessionId = session.Id, SnapshotJson = "{\"pii\":true}", CreatedAt = now };
        var turnState = new TutorTurnState { Id = Guid.NewGuid(), UserId = userId, TopicId = null, SessionId = session.Id, WorkingMemorySnapshotId = workingMemory.Id, ConceptGraphSnapshotId = conceptGraph.Id, StateJson = "{\"pii\":true}", CreatedAt = now };
        var memoryPatch = new TutorMemoryPatch { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, SessionId = session.Id, PatchJson = "{\"pii\":true}", CreatedAt = now };
        var learnerProfile = new LearnerProfile { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, ProfileJson = "{\"pii\":true}", CreatedAt = now, UpdatedAt = now };
        var styleSignal = new LearningStyleSignal { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, SessionId = session.Id, PayloadJson = "{\"pii\":true}", CreatedAt = now };
        var affectiveSignal = new AffectiveSignal { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, SessionId = session.Id, PayloadJson = "{\"pii\":true}", CreatedAt = now };
        var cognitiveSignal = new CognitiveLoadSignal { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, SessionId = session.Id, PayloadJson = "{\"pii\":true}", CreatedAt = now };
        var actionTrace = new TutorActionTrace { Id = Guid.NewGuid(), UserId = userId, TopicId = null, SessionId = session.Id, TutorTurnStateId = turnState.Id, ToolPlanJson = "[]", ArtifactPlanJson = "[]", CreatedAt = now };
        var toolCall = new TutorToolCall { Id = Guid.NewGuid(), UserId = userId, TopicId = null, SessionId = session.Id, TutorActionTraceId = actionTrace.Id, ToolId = "evidence", ResultJson = "{\"pii\":true}", CreatedAt = now };
        var artifact = new TeachingArtifact { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, SessionId = session.Id, TutorActionTraceId = actionTrace.Id, ArtifactType = "markdown", Title = "PII artifact", Content = "PII", MetadataJson = "{\"pii\":true}", CreatedAt = now };
        var memoryFragment = new TutorMemoryFragment { Id = Guid.NewGuid(), UserId = userId, TopicId = null, SessionId = session.Id, TutorTurnStateId = turnState.Id, TutorActionTraceId = actionTrace.Id, Content = "PII memory", CreatedAt = now };
        var reflection = new TutorReflectionUpdate { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, SessionId = session.Id, TutorActionTraceId = actionTrace.Id, TutorTurnStateId = turnState.Id, ReflectionJson = "{\"pii\":true}", CreatedAt = now };
        var policyViolation = new TutorPolicyViolationV2 { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, SessionId = session.Id, TutorActionTraceId = actionTrace.Id, Evidence = "PII", CreatedAt = now };
        var pedagogyRun = new TutorPedagogyEvaluationRun { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, SessionId = session.Id, TutorTurnStateId = turnState.Id, TutorActionTraceId = actionTrace.Id, TutorReflectionUpdateId = reflection.Id, RunJson = "{\"pii\":true}", CreatedAt = now };
        var pedagogyItem = new TutorPedagogyEvaluationItem { Id = Guid.NewGuid(), EvaluationRunId = pedagogyRun.Id, UserId = userId, TopicId = child.Id, SessionId = session.Id, TutorTurnStateId = turnState.Id, TutorActionTraceId = actionTrace.Id, UserMessage = "PII", AssistantAnswer = "PII", CreatedAt = now };
        var rubric = new TutorPedagogyRubricScore { Id = Guid.NewGuid(), EvaluationRunId = pedagogyRun.Id, UserId = userId, TopicId = child.Id, TutorActionTraceId = actionTrace.Id, RubricKey = "safe", Evidence = "PII", CreatedAt = now };
        var feedbackPatch = new TutorPedagogyFeedbackPatch { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, SessionId = session.Id, TutorPedagogyEvaluationRunId = pedagogyRun.Id, Feedback = "PII", PatchJson = "{\"pii\":true}", CreatedAt = now };
        var traceProjection = new TutorTraceProjection { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, SessionId = session.Id, StreamKey = "trace", StreamId = "1-0", EventType = "state", PayloadJson = "{\"pii\":true}", CreatedAt = now, OccurredAt = now };

        var ragRun = new RagEvaluationRun { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, ConceptGraphSnapshotId = conceptGraph.Id, ReportJson = "{\"pii\":true}", CreatedAt = now };
        var ragItem = new RagEvaluationItem { Id = Guid.NewGuid(), RagEvaluationRunId = ragRun.Id, UserId = userId, TopicId = null, Query = "PII query", Answer = "PII answer", ContextJson = "[]", CreatedAt = now };
        var teachingEvidence = new TeachingEvidenceItem { Id = Guid.NewGuid(), UserId = userId, TopicId = null, SessionId = session.Id, TutorTurnStateId = turnState.Id, TutorActionTraceId = actionTrace.Id, TutorToolCallId = toolCall.Id, EvidenceType = "knowledge_entity", Provider = "test", Query = "PII query", Title = "PII title", RawPayloadJson = "{\"pii\":true}", CreatedAt = now };
        var retrievalRun = new SourceRetrievalRun { Id = Guid.NewGuid(), UserId = userId, TopicId = null, SessionId = session.Id, SourceId = learningSource.Id, Query = "PII query", MetadataJson = "{\"pii\":true}", CreatedAt = now };
        var retrievalItem = new SourceRetrievalItem { Id = Guid.NewGuid(), SourceRetrievalRunId = retrievalRun.Id, UserId = userId, TopicId = null, SourceId = learningSource.Id, SourceChunkId = chunk.Id, Snippet = "PII snippet", CreatedAt = now };
        var citationCheck = new SourceCitationCheck { Id = Guid.NewGuid(), UserId = userId, TopicId = null, SessionId = session.Id, SourceRetrievalRunId = retrievalRun.Id, SourceId = learningSource.Id, SourceChunkId = chunk.Id, CitationId = "c1", Answer = "PII answer", ClaimText = "PII claim", CreatedAt = now };
        var sourceQuality = new SourceQualityReport { Id = Guid.NewGuid(), UserId = userId, TopicId = null, SourceId = learningSource.Id, ReportJson = "{\"pii\":true}", CreatedAt = now, GeneratedAt = now };

        var calibrationRun = new AssessmentCalibrationRun { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, ConceptGraphSnapshotId = conceptGraph.Id, ReportJson = "{\"pii\":true}", CreatedAt = now };
        var calibrationItem = new AssessmentCalibrationItem { Id = Guid.NewGuid(), AssessmentCalibrationRunId = calibrationRun.Id, UserId = userId, TopicId = null, AssessmentItemId = assessmentItem.Id, ConceptKey = "concept", CreatedAt = now };
        var adaptiveSession = new AdaptiveAssessmentSession { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, SessionId = session.Id, QuizRunId = quizRun.Id, ConceptGraphSnapshotId = conceptGraph.Id, CreatedAt = now, UpdatedAt = now };
        var adaptiveDecision = new AdaptiveAssessmentDecision { Id = Guid.NewGuid(), AdaptiveAssessmentSessionId = adaptiveSession.Id, UserId = userId, TopicId = null, AssessmentItemId = assessmentItem.Id, QuizAttemptId = quizAttempt.Id, ConceptKey = "concept", SelectedQuestionJson = "{\"pii\":true}", CreatedAt = now };
        var standardsExportRun = new StandardsExportRun { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, ConceptGraphSnapshotId = conceptGraph.Id, PayloadJson = "{\"pii\":true}", CreatedAt = now };
        var standardsExportItem = new StandardsExportItem { Id = Guid.NewGuid(), StandardsExportRunId = standardsExportRun.Id, UserId = userId, TopicId = null, EntityType = "learning_outcome", EntityId = outcome.Id, PayloadJson = "{\"pii\":true}", CreatedAt = now };
        var standardsValidationRun = new StandardsValidationRun { Id = Guid.NewGuid(), UserId = userId, TopicId = child.Id, ConceptGraphSnapshotId = conceptGraph.Id, SummaryJson = "{\"pii\":true}", CreatedAt = now };
        var standardsValidationItem = new StandardsValidationItem { Id = Guid.NewGuid(), StandardsValidationRunId = standardsValidationRun.Id, UserId = userId, TopicId = null, EntityType = "assessment_item", EntityId = assessmentItem.Id, DetailJson = "{\"pii\":true}", CreatedAt = now };

        db.AddRange(
            root, child, grandChild, preservedTopic, otherUserTopic,
            session, message,
            wikiPage, wikiBlock, wikiSource,
            learningSource, chunk,
            quizRun, quizAttempt, signal, flashcard, review, daily, xp, submission,
            bookmark, notification, push, refresh, telemetry, metadataOnlyTelemetry, cost, metadataOnlyCost,
            evaluation, audio, classroom, interaction,
            conceptGraph, concept, relation, outcome, outcomeAlignment, assessmentItem, skillMastery,
            diagnostic, conceptMastery, learningEvent, violation, graphQuality, assessmentQuality,
            itemStat, knowledge, resourceAlignment, learningQuality, policyTrace,
            workingMemory, turnState, memoryPatch, learnerProfile, styleSignal, affectiveSignal,
            cognitiveSignal, actionTrace, toolCall, artifact, memoryFragment, reflection,
            policyViolation, pedagogyRun, pedagogyItem, rubric, feedbackPatch, traceProjection,
            ragRun, ragItem, teachingEvidence, retrievalRun, retrievalItem, citationCheck, sourceQuality,
            calibrationRun, calibrationItem, adaptiveSession, adaptiveDecision,
            standardsExportRun, standardsExportItem, standardsValidationRun, standardsValidationItem);

        await db.SaveChangesAsync();

        return new LifecycleSeedIds(
            root.Id,
            preservedTopic.Id,
            otherUserTopic.Id,
            [root.Id, child.Id, grandChild.Id],
            [session.Id],
            [message.Id],
            [wikiPage.Id],
            [learningSource.Id],
            [quizRun.Id],
            [quizAttempt.Id],
            [signal.Id],
            [review.Id],
            [flashcard.Id],
            telemetry.Id,
            cost.Id);
    }

    private static Topic NewTopic(Guid userId, string title, Guid? parentTopicId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ParentTopicId = parentTopicId,
            Title = title,
            Category = "Lifecycle",
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow
        };

    private static User NewUser(string prefix) =>
        new()
        {
            Id = Guid.NewGuid(),
            FirstName = "Lifecycle",
            LastName = "Relational",
            Email = $"{prefix}-{Guid.NewGuid():N}@orka.local",
            PasswordHash = "hash",
            StorageLimitMB = 100,
            CreatedAt = DateTime.UtcNow,
            DailyMessageResetAt = DateTime.UtcNow.Date.AddDays(1)
        };

    private static async Task AssertHighRiskFamiliesRemovedAsync(OrkaDbContext db, Guid userId)
    {
        Assert.False(await db.SkillMasteries.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.ConceptGraphSnapshots.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.LearningConcepts.AnyAsync());
        Assert.False(await db.ConceptRelations.AnyAsync());
        Assert.False(await db.LearningOutcomes.AnyAsync());
        Assert.False(await db.OutcomeAlignments.AnyAsync());
        Assert.False(await db.AssessmentItems.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.DiagnosticProfiles.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.ConceptMasteries.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.LearningEvents.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.LearningEventSchemaViolations.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.ConceptGraphQualityRuns.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.AssessmentQualityRuns.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.AssessmentItemStats.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.KnowledgeTracingStates.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.ResourceConceptAlignments.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.LearningQualityReports.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.TutorPolicyTraces.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.TutorWorkingMemorySnapshots.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.TutorTurnStates.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.TutorMemoryPatches.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.LearnerProfiles.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.LearningStyleSignals.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.AffectiveSignals.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.CognitiveLoadSignals.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.TutorActionTraces.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.TutorToolCalls.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.TeachingArtifacts.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.TutorReflectionUpdates.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.TutorPolicyViolationsV2.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.TutorMemoryFragments.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.RagEvaluationRuns.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.RagEvaluationItems.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.TeachingEvidenceItems.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.SourceRetrievalRuns.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.SourceRetrievalItems.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.SourceCitationChecks.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.SourceQualityReports.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.TutorPedagogyEvaluationRuns.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.TutorPedagogyEvaluationItems.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.TutorPedagogyRubricScores.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.TutorPedagogyFeedbackPatches.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.AssessmentCalibrationRuns.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.AssessmentCalibrationItems.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.AdaptiveAssessmentSessions.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.AdaptiveAssessmentDecisions.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.TutorTraceProjections.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.StandardsExportRuns.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.StandardsExportItems.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.StandardsValidationRuns.AnyAsync(e => e.UserId == userId));
        Assert.False(await db.StandardsValidationItems.AnyAsync(e => e.UserId == userId));
    }

    private sealed class RelationalLifecycleHarness : IAsyncDisposable
    {
        private readonly string _connectionString;

        private RelationalLifecycleHarness(string connectionString, OrkaDbContext db)
        {
            _connectionString = connectionString;
            Db = db;
            Redis = new LifecycleRedisMemoryService();
            Service = new DataLifecycleService(db, Redis, NullLogger<DataLifecycleService>.Instance);
        }

        public OrkaDbContext Db { get; }
        public LifecycleRedisMemoryService Redis { get; }
        public DataLifecycleService Service { get; }

        public static async Task<RelationalLifecycleHarness> CreateAsync()
        {
            var databaseName = $"OrkaLifecycle_{Guid.NewGuid():N}";
            var connectionString = BuildConnectionString(databaseName);

            var options = new DbContextOptionsBuilder<OrkaDbContext>()
                .UseSqlServer(connectionString)
                .Options;

            var db = new OrkaDbContext(options);
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
            return new RelationalLifecycleHarness(connectionString, db);
        }

        private static string BuildConnectionString(string databaseName)
        {
            var ciBaseConnection = Environment.GetEnvironmentVariable("ORKA_LIFECYCLE_SQLSERVER_BASE_CONNECTION");
            if (!string.IsNullOrWhiteSpace(ciBaseConnection))
            {
                var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(ciBaseConnection)
                {
                    InitialCatalog = databaseName,
                    TrustServerCertificate = true
                };
                return builder.ConnectionString;
            }

            return $"Server=(localdb)\\OrkaLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;";
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Db.Database.EnsureDeletedAsync();
            }
            finally
            {
                await Db.DisposeAsync();
            }
        }
    }

    private class LifecycleRedisMemoryService : IRedisMemoryService
    {
        public List<RedisPurgeCall> Purges { get; } = [];

        public Task RecordEvaluationAsync(Guid sessionId, int score, string feedback) => Task.CompletedTask;
        public Task<IEnumerable<string>> GetRecentFeedbackAsync(Guid sessionId, int count = 5) => Task.FromResult<IEnumerable<string>>([]);
        public Task<bool> CheckRateLimitAsync(string clientIp, int maxRequests, TimeSpan window) => Task.FromResult(true);
        public Task SetGlobalPolicyAsync(string policyText) => Task.CompletedTask;
        public Task<string> GetGlobalPolicyAsync() => Task.FromResult(string.Empty);
        public Task SetLastPistonResultAsync(Guid sessionId, string code, string stdout, string stderr, string language, string phase = "run", string? compileError = null, string? runtimeError = null, bool success = true, string? safeTutorSummary = null) => Task.CompletedTask;
        public Task<string> GetLastPistonResultAsync(Guid sessionId) => Task.FromResult(string.Empty);
        public Task SetWikiReadyAsync(Guid topicId) => Task.CompletedTask;
        public Task SaveGoldExampleAsync(Guid topicId, string userMessage, string agentResponse, int score) => Task.CompletedTask;
        public Task<IEnumerable<GoldExample>> GetGoldExamplesAsync(Guid topicId, int count = 2) => Task.FromResult<IEnumerable<GoldExample>>([]);
        public Task RecordAgentMetricAsync(string agentRole, long latencyMs, bool isSuccess, string? provider = null) => Task.CompletedTask;
        public Task<IEnumerable<AgentMetricSummary>> GetSystemMetricsAsync() => Task.FromResult<IEnumerable<AgentMetricSummary>>([]);
        public Task<IEnumerable<EvaluatorLogEntry>> GetRecentEvaluatorLogsAsync(int count = 20) => Task.FromResult<IEnumerable<EvaluatorLogEntry>>([]);
        public Task<IEnumerable<ProviderUsageStat>> GetProviderUsageAsync() => Task.FromResult<IEnumerable<ProviderUsageStat>>([]);
        public Task<string?> GetJsonAsync(string key) => Task.FromResult<string?>(null);
        public Task SetJsonAsync(string key, string payload, TimeSpan ttl) => Task.CompletedTask;
        public Task AddStreamEventAsync(string key, IReadOnlyDictionary<string, string> values, TimeSpan? ttl = null) => Task.CompletedTask;
        public Task<IReadOnlyList<RedisStreamEventDto>> ReadStreamEventsAsync(string key, string afterId = "0-0", int count = 50) => Task.FromResult<IReadOnlyList<RedisStreamEventDto>>([]);
        public Task<bool> EnsureConsumerGroupAsync(string key, string group, string startId = "0-0") => Task.FromResult(true);
        public Task<IReadOnlyList<RedisStreamEventDto>> ReadConsumerGroupAsync(string key, string group, string consumer, int count = 50, string streamId = ">") => Task.FromResult<IReadOnlyList<RedisStreamEventDto>>([]);
        public Task AckStreamEventsAsync(string key, string group, IEnumerable<string> eventIds) => Task.CompletedTask;
        public Task<bool> SupportsVectorSearchAsync() => Task.FromResult(false);
        public Task DeleteKeyAsync(string key) => Task.CompletedTask;
        public Task<long> GetTopicVersionAsync(Guid topicId) => Task.FromResult(0L);
        public Task<long> BumpTopicVersionAsync(Guid topicId, string reason) => Task.FromResult(1L);
        public Task InvalidateLearningCachesAsync(Guid userId, Guid topicId, string reason) => Task.CompletedTask;
        public Task PurgeUserCachesAsync(Guid userId, IEnumerable<Guid> topicIds, string reason, int maxKeysPerPattern = 100)
        {
            Purges.Add(new RedisPurgeCall(userId, topicIds.ToArray(), reason));
            return Task.CompletedTask;
        }
        public Task RecordCacheMetricAsync(string area, bool hit, string? tool = null, double? latencyMs = null) => Task.CompletedTask;
        public Task<IEnumerable<CacheMetricSummary>> GetCacheMetricsAsync() => Task.FromResult<IEnumerable<CacheMetricSummary>>([]);
        public Task<RedisHealthDto> GetRedisHealthAsync() => Task.FromResult(new RedisHealthDto(true, 0, 0, "lifecycle", null, DateTime.UtcNow));
        public Task<IReadOnlyList<string>> GetRecentQuestionHashesAsync(Guid userId, Guid topicId, int count = 80) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task RememberQuestionHashesAsync(Guid userId, Guid topicId, IEnumerable<string> hashes) => Task.CompletedTask;
        public Task RecordTopicScoreAsync(Guid topicId, int score, string feedback) => Task.CompletedTask;
        public Task<(double avgScore, int totalEvals)> GetTopicScoreAsync(Guid topicId) => Task.FromResult((0d, 0));
        public Task RecordStudentProfileAsync(Guid topicId, int understandingScore, string weaknesses) => Task.CompletedTask;
        public Task<(int score, string weaknesses)?> GetStudentProfileAsync(Guid topicId) => Task.FromResult<(int score, string weaknesses)?>(null);
        public Task SetLowQualityFeedbackAsync(Guid sessionId, int score, string feedback) => Task.CompletedTask;
        public Task<(int score, string feedback)?> GetAndClearLowQualityFeedbackAsync(Guid sessionId) => Task.FromResult<(int score, string feedback)?>(null);
        public Task SaveKorteksResearchReportAsync(Guid topicId, string report) => Task.CompletedTask;
        public Task<string?> GetKorteksResearchReportAsync(Guid topicId) => Task.FromResult<string?>(null);
        public Task SaveYouTubeContextAsync(Guid topicId, string payload) => Task.CompletedTask;
        public Task<string?> GetYouTubeContextAsync(Guid topicId) => Task.FromResult<string?>(null);
    }

    private sealed class FailingPurgeRedisMemoryService : LifecycleRedisMemoryService, IRedisMemoryService
    {
        public new Task PurgeUserCachesAsync(Guid userId, IEnumerable<Guid> topicIds, string reason, int maxKeysPerPattern = 100) =>
            throw new InvalidOperationException("Simulated Redis purge failure.");
    }

    private sealed record RedisPurgeCall(Guid UserId, IReadOnlyCollection<Guid> TopicIds, string Reason);

    private sealed record TestUser(HttpClient Client, Guid UserId);

    private sealed record LifecycleSeedIds(
        Guid RootTopicId,
        Guid PreservedUserTopicId,
        Guid OtherUserTopicId,
        IReadOnlyCollection<Guid> DeletedTopicIds,
        IReadOnlyCollection<Guid> DeletedSessionIds,
        IReadOnlyCollection<Guid> DeletedMessageIds,
        IReadOnlyCollection<Guid> DeletedWikiPageIds,
        IReadOnlyCollection<Guid> DeletedLearningSourceIds,
        IReadOnlyCollection<Guid> DeletedQuizRunIds,
        IReadOnlyCollection<Guid> DeletedQuizAttemptIds,
        IReadOnlyCollection<Guid> DeletedLearningSignalIds,
        IReadOnlyCollection<Guid> DeletedReviewItemIds,
        IReadOnlyCollection<Guid> DeletedFlashcardIds,
        Guid ToolTelemetryId,
        Guid CostRecordId);
}
