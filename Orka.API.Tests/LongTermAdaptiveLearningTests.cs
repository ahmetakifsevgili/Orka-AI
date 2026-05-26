using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.Constants;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class LongTermAdaptiveLearningTests
{
    [Fact]
    public async Task AdaptiveProfile_BuildsLongTermReviewRepairSourceAndWeeklyRhythm()
    {
        await using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "long-term");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Long Term Algebra");
        await SeedLongTermEvidenceAsync(factory, user.UserId, topicId);

        var response = await user.Client.GetAsync($"/api/learning/topic/{topicId}/adaptive-profile");

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;
        var concepts = root.GetProperty("concepts").EnumerateArray().ToArray();

        Assert.True(root.GetProperty("hasEnoughEvidence").GetBoolean());
        Assert.Contains("source_evidence_limited", root.GetProperty("reasonCodes").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains(root.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("Kaynak", StringComparison.OrdinalIgnoreCase));

        var oneCorrect = FindConcept(concepts, "single-correct");
        Assert.NotEqual("stable", oneCorrect.GetProperty("state").GetString());
        Assert.Equal("take_quiz", oneCorrect.GetProperty("recommendedAction").GetString());

        var stable = FindConcept(concepts, "stable-skill");
        Assert.Equal("stable", stable.GetProperty("state").GetString());
        Assert.Equal("none", stable.GetProperty("reviewPriority").GetString());

        var repeatedWrong = FindConcept(concepts, "prereq-gap");
        Assert.Equal("repair", repeatedWrong.GetProperty("recommendedAction").GetString());
        Assert.Contains("prerequisite_gap", repeatedWrong.GetProperty("reasonCodes").EnumerateArray().Select(x => x.GetString()));

        var blank = FindConcept(concepts, "blank-gap");
        Assert.Equal("repair", blank.GetProperty("recommendedAction").GetString());
        Assert.Contains("repeated_blank", blank.GetProperty("reasonCodes").EnumerateArray().Select(x => x.GetString()));
        Assert.DoesNotContain("misconception", blank.GetRawText(), StringComparison.OrdinalIgnoreCase);

        var due = FindConcept(concepts, "due-srs");
        Assert.Equal("review", due.GetProperty("recommendedAction").GetString());
        Assert.Contains("due_srs", due.GetProperty("reasonCodes").EnumerateArray().Select(x => x.GetString()));

        var wikiRepair = FindConcept(concepts, "wiki-gap");
        Assert.Equal("repair", wikiRepair.GetProperty("recommendedAction").GetString());
        Assert.Contains("repair_pending", wikiRepair.GetProperty("reasonCodes").EnumerateArray().Select(x => x.GetString()));

        var reviewPressure = root.GetProperty("reviewPressure").EnumerateArray().ToArray();
        Assert.Contains(reviewPressure, r => r.GetProperty("conceptKey").GetString() == "due-srs");
        Assert.Contains(root.GetProperty("nextActions").EnumerateArray(), a => a.GetProperty("actionType").GetString() == "source_review");
        Assert.NotEqual("Kısa seviye tespiti", root.GetProperty("weeklyRhythm").GetProperty("todayFocus").GetString());

        var publicJson = root.GetRawText();
        Assert.DoesNotContain(user.UserId.ToString(), publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawPrompt", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawProviderPayload", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawSourceChunk", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correctAnswer", publicJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TutorNextActions_ConsumeLongTermStateSafely()
    {
        await using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "long-term-tutor");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, user.UserId, "Long Term Tutor");
        await SeedRepeatedWrongAsync(factory, user.UserId, topicId, "tutor-gap", "Tutor Gap");

        var response = await user.Client.GetAsync($"/api/tutor/next-actions?topicId={topicId}");

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var actions = body.RootElement.EnumerateArray().ToArray();

        Assert.Contains(actions, a =>
            a.GetProperty("actionType").GetString() == "start_micro_quiz" &&
            a.GetProperty("targetConceptKey").GetString() == "tutor-gap" &&
            a.GetProperty("priority").GetString() == "high");

        var publicJson = body.RootElement.GetRawText();
        Assert.DoesNotContain(user.UserId.ToString(), publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawPrompt", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", publicJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdaptiveProfile_BlocksOtherUserTopic()
    {
        await using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "long-term-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "long-term-other");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(factory, owner.UserId, "Private Long Term Topic");

        var response = await other.Client.GetAsync($"/api/learning/topic/{topicId}/adaptive-profile");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static JsonElement FindConcept(IEnumerable<JsonElement> concepts, string conceptKey) =>
        concepts.Single(c => c.GetProperty("conceptKey").GetString() == conceptKey);

    private static async Task SeedLongTermEvidenceAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        await SeedCorrectAttemptAsync(factory, userId, topicId, "single-correct", DateTime.UtcNow.AddMinutes(-10));
        await SeedStableKnowledgeAsync(factory, userId, topicId, "stable-skill", "Stable Skill");
        await SeedRepeatedWrongAsync(factory, userId, topicId, "prereq-gap", "Prerequisite Gap");
        await SeedRepeatedBlankAsync(factory, userId, topicId, "blank-gap", "Blank Gap");
        await SeedDueReviewAsync(factory, userId, topicId, "due-srs", "Due SRS");
        await SeedWikiRepairAsync(factory, userId, topicId, "wiki-gap", "Wiki Gap");
        await SeedStaleSourceBundleAsync(factory, userId, topicId);
    }

    private static async Task SeedCorrectAttemptAsync(ApiSmokeFactory factory, Guid userId, Guid topicId, string conceptKey, DateTime createdAt)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        db.QuizAttempts.Add(new QuizAttempt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            Question = "Safe question",
            UserAnswer = "A",
            IsCorrect = true,
            Explanation = "Safe explanation",
            SkillTag = conceptKey,
            CreatedAt = createdAt
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedStableKnowledgeAsync(ApiSmokeFactory factory, Guid userId, Guid topicId, string conceptKey, string label)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;
        db.KnowledgeTracingStates.Add(new KnowledgeTracingState
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ConceptKey = conceptKey,
            Label = label,
            EvidenceCount = 4,
            CorrectCount = 4,
            IncorrectCount = 0,
            MasteryProbability = 0.86m,
            Confidence = 0.82m,
            RemediationNeed = "none",
            PracticeReadiness = "independent",
            LastEvidenceAt = now.AddDays(-1),
            CreatedAt = now.AddDays(-4),
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedRepeatedWrongAsync(ApiSmokeFactory factory, Guid userId, Guid topicId, string conceptKey, string label)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;
        db.KnowledgeTracingStates.Add(new KnowledgeTracingState
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ConceptKey = conceptKey,
            Label = label,
            EvidenceCount = 2,
            CorrectCount = 0,
            IncorrectCount = 2,
            MasteryProbability = 0.28m,
            Confidence = 0.72m,
            RemediationNeed = "high",
            PracticeReadiness = "guided",
            LastEvidenceAt = now.AddMinutes(-5),
            CreatedAt = now.AddDays(-1),
            UpdatedAt = now
        });
        db.QuizAttempts.AddRange(
            WrongAttempt(userId, topicId, conceptKey, now.AddMinutes(-8)),
            WrongAttempt(userId, topicId, conceptKey, now.AddMinutes(-4)));
        await db.SaveChangesAsync();
    }

    private static async Task SeedRepeatedBlankAsync(ApiSmokeFactory factory, Guid userId, Guid topicId, string conceptKey, string label)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;
        db.QuizAttempts.AddRange(
            BlankAttempt(userId, topicId, conceptKey, now.AddMinutes(-7)),
            BlankAttempt(userId, topicId, conceptKey, now.AddMinutes(-3)));
        db.KnowledgeTracingStates.Add(new KnowledgeTracingState
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ConceptKey = conceptKey,
            Label = label,
            EvidenceCount = 2,
            CorrectCount = 0,
            IncorrectCount = 0,
            MasteryProbability = 0.40m,
            Confidence = 0.45m,
            RemediationNeed = "medium",
            PracticeReadiness = "guided",
            LastEvidenceAt = now.AddMinutes(-3),
            CreatedAt = now.AddDays(-1),
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedDueReviewAsync(ApiSmokeFactory factory, Guid userId, Guid topicId, string conceptKey, string label)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;
        db.ReviewItems.Add(new ReviewItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ReviewKey = conceptKey,
            SkillTag = label,
            ConceptTag = conceptKey,
            LearningObjective = label,
            DueAt = now.AddDays(-1),
            Status = "active",
            CreatedAt = now.AddDays(-4),
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedWikiRepairAsync(ApiSmokeFactory factory, Guid userId, Guid topicId, string conceptKey, string label)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;
        var pageId = Guid.NewGuid();
        db.WikiPages.Add(new WikiPage
        {
            Id = pageId,
            UserId = userId,
            TopicId = topicId,
            PageKey = conceptKey,
            ConceptKey = conceptKey,
            Title = label,
            Status = "ready",
            SourceReadiness = "evidence_insufficient",
            EvidenceStatus = "evidence_insufficient",
            CreatedAt = now,
            UpdatedAt = now
        });
        db.WikiBlocks.Add(new WikiBlock
        {
            Id = Guid.NewGuid(),
            WikiPageId = pageId,
            BlockType = WikiBlockType.RepairNote,
            Title = label,
            Content = "Safe repair summary.",
            ConceptKey = conceptKey,
            SourceBasis = "assessment_verified",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedStaleSourceBundleAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;
        db.SourceEvidenceBundles.Add(new SourceEvidenceBundle
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            BundleHash = Guid.NewGuid().ToString("N"),
            EvidenceStatus = "stale",
            SourceCount = 1,
            ReadySourceCount = 0,
            ChunkCount = 1,
            StaleEvidenceCount = 1,
            EvidenceJson = "{}",
            CreatedAt = now.AddDays(-8),
            UpdatedAt = now.AddDays(-8)
        });
        await db.SaveChangesAsync();
    }

    private static QuizAttempt WrongAttempt(Guid userId, Guid topicId, string conceptKey, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TopicId = topicId,
        Question = "Safe question",
        UserAnswer = "B",
        IsCorrect = false,
        Explanation = "Safe explanation",
        SkillTag = conceptKey,
        CreatedAt = createdAt
    };

    private static QuizAttempt BlankAttempt(Guid userId, Guid topicId, string conceptKey, DateTime createdAt) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        TopicId = topicId,
        Question = "Safe question",
        UserAnswer = "",
        WasSkipped = true,
        IsCorrect = false,
        Explanation = "Safe explanation",
        SkillTag = conceptKey,
        CreatedAt = createdAt
    };
}
