using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class QuizAttemptSafetyTests : IClassFixture<ApiSmokeFactory>
{
    private readonly ApiSmokeFactory _factory;
    private readonly HttpClient _client;

    public QuizAttemptSafetyTests(ApiSmokeFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task QuizAttempt_InvalidQuizRunId_DoesNotReturnServerError()
    {
        var token = await RegisterAndGetTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/quiz/attempt");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            messageId = $"smoke-{Guid.NewGuid():N}",
            quizRunId = Guid.NewGuid(),
            questionId = "q1",
            question = "C# async akista await ne ise yarar?",
            selectedOptionId = "A) Async isi bloklamadan bekletir.",
            isCorrect = true,
            explanation = "Await, Task sonucunu akisi bloklamadan beklemek icin kullanilir.",
            skillTag = "async-await",
            topicPath = "CSharp/Async",
            difficulty = "kolay",
            cognitiveType = "conceptual",
            questionHash = $"invalid-run-{Guid.NewGuid():N}"
        });

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.True(json.RootElement.TryGetProperty("quizRunId", out var quizRunId));
        Assert.Equal(JsonValueKind.Null, quizRunId.ValueKind);
        Assert.Equal("unverified", json.RootElement.GetProperty("learningImpact").GetProperty("result").GetString());
    }

    [Fact]
    public async Task QuizAttempt_UsesDurableAssessmentItemAndIgnoresConflictingClientCorrectness()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "quiz-authoritative");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Authoritative Quiz");
        var (quizRunId, assessmentItemId) = await SeedAssessmentItemAsync(user.UserId, topicId);

        var response = await user.Client.PostAsJsonAsync("/api/quiz/attempt", new
        {
            quizRunId,
            topicId,
            assessmentItemId,
            questionId = "q-auth",
            question = "Server hangi secenegi dogrular?",
            selectedOptionId = "B) Server dogrular",
            isCorrect = false,
            explanation = "client-provided explanation must be ignored",
            skillTag = "server-authority",
            conceptKey = "server-authority",
            questionHash = $"authoritative-{Guid.NewGuid():N}"
        });

        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var impact = json.RootElement.GetProperty("learningImpact");
        Assert.Equal("correct", impact.GetProperty("result").GetString());
        Assert.Contains("server_verified_correctness", impact.GetProperty("evidenceBasis").EnumerateArray().Select(e => e.GetString()));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var attempt = await db.QuizAttempts.AsNoTracking().SingleAsync(a => a.UserId == user.UserId && a.AssessmentItemId == assessmentItemId);
        Assert.True(attempt.IsCorrect);
        Assert.Equal("Server-safe explanation.", attempt.Explanation);
        Assert.DoesNotContain("client-provided", attempt.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task QuizAttempt_LegacyNoDurableKeyIgnoresClientCorrectnessAndAvoidsStrongLearningUpdates()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "quiz-unverified");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Legacy Quiz");

        var response = await user.Client.PostAsJsonAsync("/api/quiz/attempt", new
        {
            topicId,
            questionId = "legacy-q",
            question = "Legacy generated question without durable answer key?",
            selectedOptionId = "A) Looks correct",
            isCorrect = true,
            explanation = "client answer key should not become remediation",
            skillTag = "legacy-client",
            conceptKey = "legacy-client",
            questionHash = $"legacy-{Guid.NewGuid():N}"
        });

        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var impact = json.RootElement.GetProperty("learningImpact");
        Assert.Equal("unverified", impact.GetProperty("result").GetString());
        Assert.Equal("observed_only", impact.GetProperty("misconceptionConfidence").GetString());
        Assert.Equal("keep_as_practice_observation", impact.GetProperty("nextPlanAction").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var attempt = await db.QuizAttempts.AsNoTracking().SingleAsync(a => a.UserId == user.UserId && a.QuestionId == "legacy-q");
        Assert.False(attempt.IsCorrect);
        Assert.Empty(attempt.Explanation);
        Assert.Contains("clientCorrectnessIgnored", attempt.SourceRefsJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.False(await db.SkillMasteries.AnyAsync(m => m.UserId == user.UserId && m.TopicId == topicId));
        Assert.False(await db.LearningSignals.AnyAsync(s => s.UserId == user.UserId && s.QuizAttemptId == attempt.Id));
    }

    [Fact]
    public async Task QuizAttempt_AppendsSafeLearningImpactToMatchingWikiPage()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "quiz-wiki-capture");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Wiki Capture Quiz");
        var (_, assessmentItemId) = await SeedAssessmentItemAsync(user.UserId, topicId);
        var wikiPageId = await SeedWikiConceptPageAsync(user.UserId, topicId, "server-authority");

        var response = await user.Client.PostAsJsonAsync("/api/quiz/attempt", new
        {
            topicId,
            assessmentItemId,
            questionId = "q-auth",
            question = "Server hangi secenegi dogrular?",
            selectedOptionId = "B) Server dogrular",
            isCorrect = false,
            explanation = "client-provided explanation must be ignored",
            skillTag = "server-authority",
            conceptKey = "server-authority",
            assessmentMode = "micro_quiz",
            questionHash = $"wiki-capture-{Guid.NewGuid():N}"
        });

        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var block = await db.WikiBlocks
            .AsNoTracking()
            .SingleAsync(b => b.WikiPageId == wikiPageId && b.BlockType == WikiBlockType.QuizReview);
        Assert.Equal("server-authority", block.ConceptKey);
        Assert.Equal("model_assisted", block.SourceBasis);
        Assert.Contains("micro_quiz", block.Content);
        Assert.Contains("correct", block.Content);

        var attempt = await db.QuizAttempts.AsNoTracking().SingleAsync(a => a.UserId == user.UserId && a.AssessmentItemId == assessmentItemId);
        Assert.Contains("wikiBlockId", attempt.SourceRefsJson ?? string.Empty);
        Assert.DoesNotContain("client-provided", block.Content, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> RegisterAndGetTokenAsync()
    {
        var email = $"quiz-{Guid.NewGuid():N}@orka.local";
        var register = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Quiz",
            lastName = "Smoke",
            email,
            password = "SmokePass123!"
        });
        register.EnsureSuccessStatusCode();

        using var body = await JsonDocument.ParseAsync(await register.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("token").GetString()!;
    }

    private async Task<(Guid QuizRunId, Guid AssessmentItemId)> SeedAssessmentItemAsync(Guid userId, Guid topicId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var snapshotId = Guid.NewGuid();
        var quizRunId = Guid.NewGuid();
        var assessmentItemId = Guid.NewGuid();

        db.ConceptGraphSnapshots.Add(new ConceptGraphSnapshot
        {
            Id = snapshotId,
            UserId = userId,
            TopicId = topicId,
            IntentHash = Guid.NewGuid().ToString("N"),
            ApprovedResearchIntent = "quiz authoritative test",
            TopicTitle = "quiz authoritative test",
            Domain = "test",
            SourceConfidence = "low",
            GraphJson = "{}",
            CreatedAt = DateTime.UtcNow
        });
        db.QuizRuns.Add(new QuizRun
        {
            Id = quizRunId,
            UserId = userId,
            TopicId = topicId,
            QuizType = "micro_quiz",
            Status = "active",
            TotalQuestions = 1,
            CreatedAt = DateTime.UtcNow
        });
        db.AssessmentItems.Add(new AssessmentItem
        {
            Id = assessmentItemId,
            UserId = userId,
            TopicId = topicId,
            QuizRunId = quizRunId,
            ConceptGraphSnapshotId = snapshotId,
            AssessmentItemKey = "auth:item",
            ConceptKey = "server-authority",
            ConceptLabel = "Server authority",
            QuestionType = "micro_quiz",
            CognitiveSkill = "conceptual",
            Difficulty = "kolay",
            EvidenceExpected = "server evaluates selected option",
            GeneratedQuestionJson = """
            {
              "questionId": "q-auth",
              "question": "Server hangi secenegi dogrular?",
              "options": [
                { "id": "A", "text": "Client dogrular", "isCorrect": false },
                { "id": "B", "text": "Server dogrular", "isCorrect": true }
              ],
              "correctAnswer": "B",
              "explanation": "Server-safe explanation."
            }
            """,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return (quizRunId, assessmentItemId);
    }

    private async Task<Guid> SeedWikiConceptPageAsync(Guid userId, Guid topicId, string conceptKey)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var pageId = Guid.NewGuid();
        db.WikiPages.Add(new WikiPage
        {
            Id = pageId,
            UserId = userId,
            TopicId = topicId,
            Title = "Server authority",
            PageKey = $"concept:{conceptKey}",
            PageType = "concept",
            ConceptKey = conceptKey,
            SourceReadiness = "evidence_insufficient",
            EvidenceStatus = "evidence_insufficient",
            Status = "ready",
            OrderIndex = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return pageId;
    }
}
