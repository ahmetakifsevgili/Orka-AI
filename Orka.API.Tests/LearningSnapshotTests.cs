using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.Entities;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class LearningSnapshotTests : IClassFixture<ApiSmokeFactory>
{
    private readonly ApiSmokeFactory _factory;

    public LearningSnapshotTests(ApiSmokeFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ActiveLessonSnapshot_CombinesPlanSourceWikiAttemptAndWeakConceptContext()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "snapshot-active");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Snapshot Algebra");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(_factory, user.UserId, topicId, DateTime.UtcNow);
        await CoordinationTestHelpers.SeedSourceAsync(_factory, user.UserId, topicId, "Snapshot source", "Evidence text");
        await CoordinationTestHelpers.SeedWikiPageAsync(_factory, user.UserId, topicId, "Snapshot wiki", "Wiki evidence");
        await SeedWeakConceptAsync(user.UserId, topicId, "fractions", "Fractions");
        await SeedTutorToolCallAsync(user.UserId, topicId, sessionId);
        await SeedQuizAttemptAsync(user.UserId, topicId, sessionId);

        var response = await user.Client.PostAsJsonAsync("/api/learning-snapshots/active-lesson/refresh", new
        {
            topicId,
            sessionId,
            planRequestId = Guid.NewGuid(),
            approvedIntent = "learn",
            approvedMainTopic = "Algebra",
            approvedFocusArea = "Fractions",
            approvedStudyGoal = "practice",
            groundingMode = "source_grounded"
        });

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;

        Assert.Equal("active", root.GetProperty("status").GetString());
        Assert.Equal("fractions", root.GetProperty("activeConceptKey").GetString());
        Assert.Equal("remediation", root.GetProperty("learnerState").GetString());
        Assert.Equal("usable", root.GetProperty("evidenceSummary").GetProperty("evidenceStatus").GetString());
        Assert.True(root.GetProperty("evidenceSummary").GetProperty("sourceEvidenceCount").GetInt32() >= 1);
        Assert.True(root.GetProperty("evidenceSummary").GetProperty("wikiEvidenceCount").GetInt32() >= 1);
        Assert.True(root.GetProperty("evidenceSummary").GetProperty("toolEvidenceCount").GetInt32() >= 1);
        Assert.True(root.GetProperty("evidenceSummary").GetProperty("recentAttemptCount").GetInt32() >= 1);

        var publicJson = root.GetRawText();
        Assert.DoesNotContain(user.UserId.ToString(), publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("snapshotJson", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw", publicJson, StringComparison.OrdinalIgnoreCase);

        var get = await user.Client.GetAsync($"/api/learning-snapshots/active-lesson?topicId={topicId}&sessionId={sessionId}");
        get.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task StudentContextSnapshot_ExposesBoundedMemoryAndReviewPressureOnlyForCaller()
    {
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "snapshot-student");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "snapshot-other");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, owner.UserId, "Snapshot Memory");
        await SeedWeakConceptAsync(owner.UserId, topicId, "derivatives", "Derivatives");
        await SeedReviewPressureAsync(owner.UserId, topicId, "derivatives");

        var response = await owner.Client.PostAsJsonAsync("/api/learning-snapshots/student-context/refresh", new
        {
            topicId
        });

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;

        Assert.Equal("usable", root.GetProperty("confidenceStatus").GetString());
        Assert.NotEmpty(root.GetProperty("weakConcepts").EnumerateArray());
        Assert.Contains("derivatives", root.GetProperty("reviewPressure").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("needs_review", root.GetProperty("learningMemoryHygiene").GetProperty("memoryStatus").GetString());
        Assert.True(root.GetProperty("learningMemoryHygiene").GetProperty("retainedSignalCount").GetInt32() > 0);
        Assert.DoesNotContain(owner.UserId.ToString(), root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payloadJson", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawPrompt", root.GetRawText(), StringComparison.OrdinalIgnoreCase);

        var crossUserRefresh = await other.Client.PostAsJsonAsync("/api/learning-snapshots/student-context/refresh", new
        {
            topicId
        });
        Assert.Equal(HttpStatusCode.BadRequest, crossUserRefresh.StatusCode);

        var crossUserGet = await other.Client.GetAsync($"/api/learning-snapshots/student-context?topicId={topicId}");
        Assert.Equal(HttpStatusCode.NotFound, crossUserGet.StatusCode);
    }

    [Fact]
    public async Task QuizAttempt_RefreshesStudentContextAndStalesActiveLessonSnapshot()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "snapshot-quiz");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Snapshot Quiz");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(_factory, user.UserId, topicId, DateTime.UtcNow);

        var active = await user.Client.PostAsJsonAsync("/api/learning-snapshots/active-lesson/refresh", new
        {
            topicId,
            sessionId,
            approvedIntent = "learn",
            approvedMainTopic = "Quiz",
            approvedFocusArea = "Basics"
        });
        active.EnsureSuccessStatusCode();
        var (quizRunId, assessmentItemId) = await CoordinationTestHelpers.SeedDurableAssessmentItemAsync(
            _factory,
            user.UserId,
            topicId,
            questionId: "snapshot-quiz-q",
            question: "What is the key idea?",
            conceptKey: "base-concept",
            correctOptionId: "A",
            correctOptionText: "Use the base concept",
            wrongOptionId: "B",
            wrongOptionText: "Skip the base concept",
            explanation: "Review the base concept.");

        var attempt = await user.Client.PostAsJsonAsync("/api/quiz/attempt", new
        {
            topicId,
            sessionId,
            quizRunId,
            assessmentItemId,
            questionId = "snapshot-quiz-q",
            question = "What is the key idea?",
            selectedOptionId = "B) Skip the base concept",
            isCorrect = false,
            explanation = "Review the base concept.",
            skillTag = "base-concept",
            conceptKey = "base-concept",
            conceptTag = "base-concept",
            learningObjective = "base-concept",
            topicPath = "Snapshot Quiz > Basics",
            questionHash = Guid.NewGuid().ToString("N")
        });
        attempt.EnsureSuccessStatusCode();

        var staleGet = await user.Client.GetAsync($"/api/learning-snapshots/active-lesson?topicId={topicId}&sessionId={sessionId}");
        Assert.Equal(HttpStatusCode.NotFound, staleGet.StatusCode);

        var studentGet = await user.Client.GetAsync($"/api/learning-snapshots/student-context?topicId={topicId}&sessionId={sessionId}");
        studentGet.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await studentGet.Content.ReadAsStreamAsync());
        Assert.NotEmpty(body.RootElement.GetProperty("weakConcepts").EnumerateArray());
    }

    [Fact]
    public async Task SnapshotReads_IgnoreExpiredRowsAndRejectTopicSessionMismatch()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "snapshot-scope");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Snapshot Scope A");
        var otherTopicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Snapshot Scope B");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(_factory, user.UserId, topicId, DateTime.UtcNow);

        var active = await user.Client.PostAsJsonAsync("/api/learning-snapshots/active-lesson/refresh", new
        {
            topicId,
            sessionId,
            approvedIntent = "learn",
            approvedMainTopic = "Scope",
            approvedFocusArea = "TTL"
        });
        active.EnsureSuccessStatusCode();

        var student = await user.Client.PostAsJsonAsync("/api/learning-snapshots/student-context/refresh", new
        {
            topicId,
            sessionId
        });
        student.EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var expiredAt = DateTime.UtcNow.AddMinutes(-1);
            var activeRows = await db.ActiveLessonSnapshots
                .Where(s => s.UserId == user.UserId && s.TopicId == topicId && s.SessionId == sessionId)
                .ToListAsync();
            var studentRows = await db.StudentContextSnapshots
                .Where(s => s.UserId == user.UserId && s.TopicId == topicId && s.SessionId == sessionId)
                .ToListAsync();

            foreach (var snapshot in activeRows)
            {
                snapshot.ExpiresAt = expiredAt;
            }

            foreach (var snapshot in studentRows)
            {
                snapshot.ExpiresAt = expiredAt;
            }

            await db.SaveChangesAsync();
        }

        var expiredActive = await user.Client.GetAsync($"/api/learning-snapshots/active-lesson?topicId={topicId}&sessionId={sessionId}");
        Assert.Equal(HttpStatusCode.NotFound, expiredActive.StatusCode);

        var expiredStudent = await user.Client.GetAsync($"/api/learning-snapshots/student-context?topicId={topicId}&sessionId={sessionId}");
        Assert.Equal(HttpStatusCode.NotFound, expiredStudent.StatusCode);

        var mismatch = await user.Client.PostAsJsonAsync("/api/learning-snapshots/active-lesson/refresh", new
        {
            topicId = otherTopicId,
            sessionId
        });
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
    }

    [Fact]
    public async Task LearningContextPack_ProvidesBoundedCrossSurfaceContext()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "context-pack");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Context Pack");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(_factory, user.UserId, topicId, DateTime.UtcNow);

        await user.Client.PostAsJsonAsync("/api/learning-snapshots/active-lesson/refresh", new
        {
            topicId,
            sessionId,
            approvedIntent = "learn",
            approvedMainTopic = "Context",
            approvedFocusArea = "Pack"
        });
        await user.Client.PostAsJsonAsync("/api/learning-snapshots/student-context/refresh", new
        {
            topicId,
            sessionId
        });

        var response = await user.Client.GetAsync($"/api/learning/context-pack?topicId={topicId}&sessionId={sessionId}");

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;

        Assert.Equal(topicId.ToString(), root.GetProperty("topicId").GetString(), ignoreCase: true);
        Assert.Equal(sessionId.ToString(), root.GetProperty("sessionId").GetString(), ignoreCase: true);
        Assert.Equal("orka.learning-context-pack.v1.1", root.GetProperty("schemaVersion").GetString());
        var watermark = root.GetProperty("contextWatermark").GetString();
        Assert.StartsWith("ctx_", watermark);
        Assert.True(root.GetProperty("estimatedTokenCount").GetInt32() > 0);
        Assert.True(root.GetProperty("estimatedTokenCount").GetInt32() <= 2_000);
        var blocks = root.GetProperty("blocks").EnumerateArray().ToArray();
        var blockTypes = blocks
            .Select(b => b.GetProperty("blockType").GetString())
            .ToArray();
        Assert.Contains("orka_state", blockTypes);
        Assert.Contains("active_lesson_snapshot", blockTypes);
        Assert.Contains("student_context_snapshot", blockTypes);
        Assert.Contains(blocks, b =>
            b.GetProperty("blockType").GetString() == "active_lesson_snapshot" &&
            b.TryGetProperty("snapshotRef", out var snapshotRef) &&
            snapshotRef.ValueKind == JsonValueKind.Object &&
            snapshotRef.GetProperty("kind").GetString() == "active_lesson_snapshot" &&
            !string.IsNullOrWhiteSpace(snapshotRef.GetProperty("version").GetString()));
        Assert.Contains(blocks, b =>
            b.GetProperty("blockType").GetString() == "student_context_snapshot" &&
            b.TryGetProperty("snapshotRef", out var snapshotRef) &&
            snapshotRef.ValueKind == JsonValueKind.Object &&
            snapshotRef.GetProperty("kind").GetString() == "student_context_snapshot");
        var trace = root.GetProperty("trace");
        Assert.Equal("orka.learning-context-pack.trace.v1", trace.GetProperty("schemaVersion").GetString());
        Assert.Equal(2_000, trace.GetProperty("tokenBudget").GetInt32());
        Assert.True(trace.GetProperty("initialEstimatedTokenCount").GetInt32() >= trace.GetProperty("estimatedTokenCount").GetInt32());
        Assert.Equal(root.GetProperty("estimatedTokenCount").GetInt32(), trace.GetProperty("estimatedTokenCount").GetInt32());
        Assert.Contains(trace.GetProperty("selectedBlocks").EnumerateArray(), b => b.GetProperty("blockType").GetString() == "orka_state");
        Assert.True(trace.GetProperty("droppedBlocks").ValueKind == JsonValueKind.Array);
        Assert.True(trace.GetProperty("droppedWarnings").ValueKind == JsonValueKind.Array);
        Assert.DoesNotContain(user.UserId.ToString(), root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("orkaState", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("activeLessonSnapshot", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("studentContextSnapshot", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sourceEvidenceBundle", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payloadJson", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawPrompt", root.GetRawText(), StringComparison.OrdinalIgnoreCase);

        var secondResponse = await user.Client.GetAsync($"/api/learning/context-pack?topicId={topicId}&sessionId={sessionId}");
        secondResponse.EnsureSuccessStatusCode();
        using var secondBody = await JsonDocument.ParseAsync(await secondResponse.Content.ReadAsStreamAsync());
        Assert.Equal(watermark, secondBody.RootElement.GetProperty("contextWatermark").GetString());
    }

    [Fact]
    public async Task LearningContextPack_BlocksForeignTopicAndTopicSessionMismatch()
    {
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "context-pack-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "context-pack-foreign");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, owner.UserId, "Context Scope A");
        var otherTopicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, owner.UserId, "Context Scope B");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(_factory, owner.UserId, topicId, DateTime.UtcNow);

        var mismatch = await owner.Client.GetAsync($"/api/learning/context-pack?topicId={otherTopicId}&sessionId={sessionId}");
        Assert.Equal(HttpStatusCode.NotFound, mismatch.StatusCode);

        var foreign = await other.Client.GetAsync($"/api/learning/context-pack?topicId={topicId}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task RecordSignal_RejectsCustomPayloadWithoutSchemaVersion()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "signal-schema");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Signal Schema");

        var rejected = await user.Client.PostAsJsonAsync("/api/learning/signal", new
        {
            topicId,
            signalType = "CustomExternalSignal",
            payloadJson = """{"value":1}"""
        });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var accepted = await user.Client.PostAsJsonAsync("/api/learning/signal", new
        {
            topicId,
            signalType = "CustomExternalSignal",
            payloadJson = """{"schemaVersion":"orka.custom-signal.v1","value":1}"""
        });
        accepted.EnsureSuccessStatusCode();
    }

    private async Task SeedWeakConceptAsync(Guid userId, Guid topicId, string conceptKey, string label)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;
        db.KnowledgeTracingStates.Add(new KnowledgeTracingState
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ConceptKey = conceptKey,
            Label = label,
            MasteryProbability = 0.25m,
            Confidence = 0.80m,
            EvidenceCount = 3,
            IncorrectCount = 2,
            RemediationNeed = "high",
            PracticeReadiness = "guided",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedReviewPressureAsync(Guid userId, Guid topicId, string conceptKey)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;
        db.ReviewItems.Add(new ReviewItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ReviewKey = conceptKey,
            SkillTag = conceptKey,
            ConceptTag = conceptKey,
            SourceType = "test",
            DueAt = now,
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedTutorToolCallAsync(Guid userId, Guid topicId, Guid sessionId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        db.TutorToolCalls.Add(new TutorToolCall
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            ToolId = "source_search",
            Status = "completed",
            Success = true,
            Evidence = "safe evidence",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedQuizAttemptAsync(Guid userId, Guid topicId, Guid sessionId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        db.QuizAttempts.Add(new QuizAttempt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            Question = "Seed question",
            UserAnswer = "A",
            IsCorrect = false,
            Explanation = "Seed explanation",
            SkillTag = "fractions",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
