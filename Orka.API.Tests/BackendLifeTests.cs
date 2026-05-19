using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.DTOs.Chat;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class BackendLifeTests
{
    [Fact]
    public async Task AuthSessionAndTopicLifeTest_StartsAtRegisterAndKeepsPublicPayloadsSafe()
    {
        using var factory = new ApiSmokeFactory();
        var lifeUser = await RegisterLifeUserAsync(factory, "life-auth");

        var meResponse = await lifeUser.Client.GetAsync("/api/user/me");
        meResponse.EnsureSuccessStatusCode();
        var meJson = await meResponse.Content.ReadAsStringAsync();
        Assert.Contains(lifeUser.Email, meJson, StringComparison.OrdinalIgnoreCase);
        AssertNoPublicLeak(meJson, allowUserId: true);

        var loginResponse = await lifeUser.Client.PostAsJsonAsync("/api/auth/login", new
        {
            email = lifeUser.Email,
            password = LifePassword
        });
        loginResponse.EnsureSuccessStatusCode();
        using var loginJson = await JsonDocument.ParseAsync(await loginResponse.Content.ReadAsStreamAsync());
        var refreshToken = loginJson.RootElement.GetProperty("refreshToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(refreshToken));

        var refreshResponse = await lifeUser.Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
        refreshResponse.EnsureSuccessStatusCode();
        var refreshJson = await refreshResponse.Content.ReadAsStringAsync();
        Assert.Contains("token", refreshJson, StringComparison.OrdinalIgnoreCase);
        AssertNoPublicLeak(refreshJson, allowUserId: true);

        var topicResponse = await lifeUser.Client.PostAsJsonAsync("/api/topics", new
        {
            title = "Backend Life Test Algebra",
            emoji = "L",
            category = "student-learning",
            planIntent = "lesson"
        });
        topicResponse.EnsureSuccessStatusCode();
        using var topicJson = await JsonDocument.ParseAsync(await topicResponse.Content.ReadAsStreamAsync());
        var topicId = topicJson.RootElement.GetProperty("id").GetGuid();

        var topicsResponse = await lifeUser.Client.GetAsync("/api/topics");
        topicsResponse.EnsureSuccessStatusCode();
        Assert.Contains(topicId.ToString(), await topicsResponse.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var topicDetailResponse = await lifeUser.Client.GetAsync($"/api/topics/{topicId}");
        topicDetailResponse.EnsureSuccessStatusCode();
        AssertNoPublicLeak(await topicDetailResponse.Content.ReadAsStringAsync(), allowUserId: true);

        var logoutResponse = await lifeUser.Client.PostAsJsonAsync("/api/auth/logout", new { refreshToken });
        logoutResponse.EnsureSuccessStatusCode();

        using var anonymousFactory = new ApiSmokeFactory();
        var anonymous = anonymousFactory.CreateClient();
        var unauthorized = await anonymous.GetAsync("/api/topics");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    [Fact]
    public async Task LearningLoopLifeTest_GoalPlanTutorQuizSnapshotWikiAndDashboardStayConnected()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "life-learning");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Life Learning");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(factory, user.UserId, tree.RootId, DateTime.UtcNow);
        var graphId = await SeedConceptGraphAsync(factory, user.UserId, tree.RootId);
        var pageId = await SeedWikiConceptPageAsync(factory, user.UserId, tree.RootId, graphId, "slope");
        await SeedWeakLearnerStateAsync(factory, user.UserId, tree.RootId, graphId, "slope");

        var planResponse = await user.Client.PostAsJsonAsync("/api/plan-quality/evaluate", new
        {
            topicId = tree.RootId,
            sessionId,
            planTitle = "Life test adaptive algebra plan"
        });
        planResponse.EnsureSuccessStatusCode();
        var planJson = await planResponse.Content.ReadAsStringAsync();
        Assert.Contains("needs_repair", planJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("slope", planJson, StringComparison.OrdinalIgnoreCase);
        AssertNoPublicLeak(planJson);

        var chatResponse = await user.Client.PostAsJsonAsync("/api/chat/message", new
        {
            content = "I do not understand slope. Can we repair it with a short example?",
            topicId = tree.RootId,
            sessionId,
            isPlanMode = false
        });
        chatResponse.EnsureSuccessStatusCode();
        var chatJson = await chatResponse.Content.ReadAsStringAsync();
        Assert.Contains("tutor", chatJson, StringComparison.OrdinalIgnoreCase);
        AssertNoPublicLeak(chatJson);

        var policyResponse = await user.Client.GetAsync($"/api/tutor/policy/topic/{tree.RootId}?sessionId={sessionId}");
        policyResponse.EnsureSuccessStatusCode();
        var policyJson = await policyResponse.Content.ReadAsStringAsync();
        Assert.Contains("remediation", policyJson, StringComparison.OrdinalIgnoreCase);
        AssertNoPublicLeak(policyJson);

        var (quizRunId, assessmentItemId) = await CoordinationTestHelpers.SeedDurableAssessmentItemAsync(
            factory,
            user.UserId,
            tree.RootId,
            questionId: "life-slope",
            question: "What does slope describe?",
            conceptKey: "slope",
            correctOptionId: "A",
            correctOptionText: "Rate of change",
            wrongOptionId: "B",
            wrongOptionText: "Only the y-intercept",
            explanation: "Slope describes rate of change.");

        var blankResponse = await user.Client.PostAsJsonAsync("/api/quiz/attempt", new
        {
            quizRunId,
            topicId = tree.RootId,
            sessionId,
            assessmentItemId,
            questionId = "life-slope",
            question = "What does slope describe?",
            selectedOptionId = "skip",
            wasSkipped = true,
            conceptKey = "slope",
            skillTag = "slope",
            assessmentMode = "checkpoint",
            questionHash = $"life-slope-blank-{Guid.NewGuid():N}"
        });
        blankResponse.EnsureSuccessStatusCode();
        var blankJson = await blankResponse.Content.ReadAsStringAsync();
        Assert.Contains("prerequisite_repair", blankJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("blank_answer_not_misconception", blankJson, StringComparison.OrdinalIgnoreCase);
        AssertNoPublicLeak(blankJson);

        var snapshotResponse = await user.Client.PostAsJsonAsync("/api/learning-snapshots/active-lesson/refresh", new
        {
            topicId = tree.RootId,
            sessionId,
            conceptGraphSnapshotId = graphId,
            approvedIntent = "lesson",
            approvedMainTopic = "Linear functions",
            approvedFocusArea = "slope",
            approvedStudyGoal = "Repair slope safely",
            groundingMode = "model_assisted"
        });
        snapshotResponse.EnsureSuccessStatusCode();
        var snapshotJson = await snapshotResponse.Content.ReadAsStringAsync();
        Assert.Contains("slope", snapshotJson, StringComparison.OrdinalIgnoreCase);
        AssertNoPublicLeak(snapshotJson);

        var copilotResponse = await user.Client.GetAsync($"/api/wiki/page/{pageId}/copilot");
        copilotResponse.EnsureSuccessStatusCode();
        var copilotJson = await copilotResponse.Content.ReadAsStringAsync();
        Assert.Contains("start_repair", copilotJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("generate_checkpoint", copilotJson, StringComparison.OrdinalIgnoreCase);
        AssertNoPublicLeak(copilotJson);

        var dashboardResponse = await user.Client.GetAsync("/api/dashboard/today");
        dashboardResponse.EnsureSuccessStatusCode();
        AssertNoPublicLeak(await dashboardResponse.Content.ReadAsStringAsync(), allowUserId: true);
    }

    [Fact]
    public async Task SourceWikiNotebookStudioLifeTest_UploadsSourceAndUsesSafeStudySurfaces()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "life-source");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Life Source");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(factory, user.UserId, tree.RootId, DateTime.UtcNow);
        var graphId = await SeedConceptGraphAsync(factory, user.UserId, tree.RootId);
        var wikiPageId = await SeedWikiConceptPageAsync(factory, user.UserId, tree.RootId, graphId, "slope");

        var sourceId = await UploadSourceAsync(
            user,
            tree.RootId,
            sessionId,
            "life-linear-source.txt",
            "Slope is the constant rate of change. Linear functions can be written as y = mx + b.");

        var topicSources = await user.Client.GetAsync($"/api/sources/topic/{tree.RootId}");
        topicSources.EnsureSuccessStatusCode();
        Assert.Contains(sourceId.ToString(), await topicSources.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var evidenceResponse = await user.Client.PostAsJsonAsync($"/api/sources/topic/{tree.RootId}/evidence-bundle/refresh", new
        {
            sessionId,
            question = "What is slope?"
        });
        evidenceResponse.EnsureSuccessStatusCode();
        var evidenceJson = await evidenceResponse.Content.ReadAsStringAsync();
        Assert.Contains("sourceCount", evidenceJson, StringComparison.OrdinalIgnoreCase);
        AssertNoPublicLeak(evidenceJson);

        var askResponse = await user.Client.PostAsJsonAsync($"/api/sources/{sourceId}/ask", new
        {
            question = "What does the source say about slope?",
            topicId = tree.RootId,
            wikiPageId,
            mode = "selected_source",
            includeLearnerContext = true,
            writeWikiTrace = true
        });
        askResponse.EnsureSuccessStatusCode();
        var askJson = await askResponse.Content.ReadAsStringAsync();
        Assert.Contains("source", askJson, StringComparison.OrdinalIgnoreCase);
        AssertNoPublicLeak(askJson);

        var notebookResponse = await user.Client.GetAsync($"/api/sources/topic/{tree.RootId}/notebook");
        notebookResponse.EnsureSuccessStatusCode();
        var notebookJson = await notebookResponse.Content.ReadAsStringAsync();
        Assert.Contains(sourceId.ToString(), notebookJson, StringComparison.OrdinalIgnoreCase);
        AssertNoPublicLeak(notebookJson);

        var packResponse = await user.Client.PostAsJsonAsync($"/api/notebook-studio/sources/{sourceId}/pack", new
        {
            sessionId,
            wikiPageId,
            packType = "source_digest",
            focusConceptKey = "slope",
            userGoal = "Turn the source into a safe review pack.",
            includeArtifacts = false
        });
        packResponse.EnsureSuccessStatusCode();
        using var packDoc = await JsonDocument.ParseAsync(await packResponse.Content.ReadAsStreamAsync());
        var packId = packDoc.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(sourceId, packDoc.RootElement.GetProperty("sourceId").GetGuid());
        AssertNoPublicLeak(packDoc.RootElement.GetRawText());

        var previewResponse = await user.Client.GetAsync($"/api/notebook-studio/packs/{packId}/export/preview");
        previewResponse.EnsureSuccessStatusCode();
        var previewJson = await previewResponse.Content.ReadAsStringAsync();
        Assert.Contains(packId.ToString(), previewJson, StringComparison.OrdinalIgnoreCase);
        AssertNoPublicLeak(previewJson);
    }

    [Fact]
    public async Task PrivacyLifeTest_BlocksOtherUserAcrossTopicSourceWikiAndNotebookSurfaces()
    {
        using var factory = new ApiSmokeFactory();
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "life-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "life-other");
        var ownerTree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, owner.UserId, "Owner Life");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(factory, owner.UserId, ownerTree.RootId, DateTime.UtcNow);
        var graphId = await SeedConceptGraphAsync(factory, owner.UserId, ownerTree.RootId);
        var pageId = await SeedWikiConceptPageAsync(factory, owner.UserId, ownerTree.RootId, graphId, "slope");
        var sourceId = await CoordinationTestHelpers.SeedSourceAsync(
            factory,
            owner.UserId,
            ownerTree.RootId,
            "Owner Source",
            "Owner-only source content about slope.");

        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/topics/{ownerTree.RootId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/sources/{sourceId}/notebook")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync($"/api/wiki/page/{pageId}/copilot")).StatusCode);

        var foreignPack = await other.Client.PostAsJsonAsync($"/api/notebook-studio/sources/{sourceId}/pack", new
        {
            sessionId,
            packType = "source_digest",
            includeArtifacts = false
        });
        Assert.Equal(HttpStatusCode.NotFound, foreignPack.StatusCode);

        var ownerCopilot = await owner.Client.GetAsync($"/api/wiki/page/{pageId}/copilot");
        ownerCopilot.EnsureSuccessStatusCode();
        AssertNoPublicLeak(await ownerCopilot.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DegradedStateLifeTest_StaleDeletedAndInvalidInputsFailSafely()
    {
        using var factory = new ApiSmokeFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "life-degraded");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Degraded Source");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(factory, user.UserId, tree.RootId, DateTime.UtcNow);
        var sourceId = await UploadSourceAsync(
            user,
            tree.RootId,
            sessionId,
            "degraded-source.txt",
            "A source can become stale and should degrade instead of pretending evidence is fresh.");

        var invalidQuiz = await user.Client.PostAsJsonAsync("/api/quiz/attempt", new
        {
            quizRunId = Guid.NewGuid(),
            topicId = tree.RootId,
            questionId = "invalid-life",
            question = "Invalid durable key?",
            selectedOptionId = "A",
            isCorrect = true,
            conceptKey = "degraded",
            questionHash = $"invalid-life-{Guid.NewGuid():N}"
        });
        invalidQuiz.EnsureSuccessStatusCode();
        var invalidQuizJson = await invalidQuiz.Content.ReadAsStringAsync();
        Assert.Contains("unverified", invalidQuizJson, StringComparison.OrdinalIgnoreCase);
        AssertNoPublicLeak(invalidQuizJson);

        var staleResponse = await user.Client.PostAsJsonAsync($"/api/sources/{sourceId}/invalidate-evidence", new
        {
            reason = "life_test_stale_source"
        });
        staleResponse.EnsureSuccessStatusCode();
        var staleJson = await staleResponse.Content.ReadAsStringAsync();
        Assert.Contains("evidence_invalidated", staleJson, StringComparison.OrdinalIgnoreCase);
        AssertNoPublicLeak(staleJson);

        var lifecycleResponse = await user.Client.GetAsync($"/api/sources/topic/{tree.RootId}/lifecycle-summary");
        lifecycleResponse.EnsureSuccessStatusCode();
        var lifecycleJson = await lifecycleResponse.Content.ReadAsStringAsync();
        Assert.Contains("source", lifecycleJson, StringComparison.OrdinalIgnoreCase);
        AssertNoPublicLeak(lifecycleJson);

        var deleteResponse = await user.Client.DeleteAsync($"/api/sources/{sourceId}");
        deleteResponse.EnsureSuccessStatusCode();

        var deletedNotebook = await user.Client.GetAsync($"/api/sources/{sourceId}/notebook");
        Assert.Equal(HttpStatusCode.NotFound, deletedNotebook.StatusCode);

        var missingSnapshot = await user.Client.PostAsJsonAsync("/api/learning-snapshots/active-lesson/refresh", new
        {
            topicId = Guid.NewGuid(),
            sessionId,
            approvedIntent = "lesson",
            approvedMainTopic = "Missing",
            approvedFocusArea = "Missing"
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingSnapshot.StatusCode);
        AssertNoPublicLeak(await missingSnapshot.Content.ReadAsStringAsync());
    }

    private const string LifePassword = "LifePass123!";

    private static async Task<LifeTestUser> RegisterLifeUserAsync(ApiSmokeFactory factory, string prefix)
    {
        var client = factory.CreateClient();
        var email = $"{prefix}-{Guid.NewGuid():N}@orka.local";
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Life",
            lastName = "Tester",
            email,
            password = LifePassword
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var token = body.RootElement.GetProperty("token").GetString()!;
        var refreshToken = body.RootElement.GetProperty("refreshToken").GetString()!;
        var userId = Guid.Parse(body.RootElement.GetProperty("user").GetProperty("id").GetString()!);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new LifeTestUser(client, userId, email, refreshToken);
    }

    private static async Task<Guid> UploadSourceAsync(
        CoordinationTestUser user,
        Guid topicId,
        Guid sessionId,
        string fileName,
        string text)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(topicId.ToString()), "TopicId");
        content.Add(new StringContent(sessionId.ToString()), "SessionId");
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "File", fileName);

        var response = await user.Client.PostAsync("/api/sources/upload", content);
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync();
        AssertNoPublicLeak(raw);
        using var json = JsonDocument.Parse(raw);
        return ReadGuidProperty(json.RootElement, "id", "sourceId");
    }

    private static Guid ReadGuidProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return Guid.Parse(value.GetString()!);
        }

        throw new InvalidOperationException($"Missing GUID property: {string.Join(", ", names)}");
    }

    private static async Task<Guid> SeedConceptGraphAsync(ApiSmokeFactory factory, Guid userId, Guid topicId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;
        var graphId = Guid.NewGuid();
        db.ConceptGraphSnapshots.Add(new ConceptGraphSnapshot
        {
            Id = graphId,
            UserId = userId,
            TopicId = topicId,
            IntentHash = $"life-{Guid.NewGuid():N}",
            ApprovedResearchIntent = "Life test learning path",
            TopicTitle = "Life Test Algebra",
            Domain = "math",
            SourceConfidence = "medium",
            GraphJson = "{}",
            CreatedAt = now
        });
        db.LearningConcepts.AddRange(
            Concept(graphId, "coordinate-plane", "Coordinate plane", 0, "foundation"),
            Concept(graphId, "slope", "Slope", 1, "core", """["coordinate-plane"]""", """["slope-as-point"]"""),
            Concept(graphId, "linear-equation", "Linear equation", 2, "core", """["slope"]"""));
        db.ConceptRelations.AddRange(
            Relation(graphId, "coordinate-plane", "slope"),
            Relation(graphId, "slope", "linear-equation"));
        await db.SaveChangesAsync();
        return graphId;
    }

    private static async Task<Guid> SeedWikiConceptPageAsync(
        ApiSmokeFactory factory,
        Guid userId,
        Guid topicId,
        Guid graphId,
        string conceptKey)
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
            ConceptGraphSnapshotId = graphId,
            Title = "Slope",
            PageKey = $"concept:{conceptKey}",
            PageType = "concept",
            ConceptKey = conceptKey,
            SourceReadiness = "evidence_insufficient",
            EvidenceStatus = "evidence_insufficient",
            SafeSummary = "Life test page keeps clean repair and source context.",
            Status = "learning",
            OrderIndex = 1,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.WikiBlocks.AddRange(
            new WikiBlock
            {
                Id = Guid.NewGuid(),
                WikiPageId = pageId,
                BlockType = WikiBlockType.TutorExplanation,
                Title = "Slope explanation",
                Content = "Slope describes rate of change in a linear relation.",
                Source = "tutor",
                SourceBasis = "tutor_generated",
                ConceptKey = conceptKey,
                OrderIndex = 1,
                CreatedAt = now,
                UpdatedAt = now
            },
            new WikiBlock
            {
                Id = Guid.NewGuid(),
                WikiPageId = pageId,
                BlockType = WikiBlockType.RepairNote,
                Title = "Slope repair",
                Content = "Learner should repair prerequisite understanding before continuing.",
                Source = "quiz_attempt",
                SourceBasis = "assessment_verified",
                ConceptKey = conceptKey,
                Visibility = "highlighted",
                OrderIndex = 2,
                CreatedAt = now,
                UpdatedAt = now
            });
        await db.SaveChangesAsync();
        return pageId;
    }

    private static async Task SeedWeakLearnerStateAsync(
        ApiSmokeFactory factory,
        Guid userId,
        Guid topicId,
        Guid graphId,
        string conceptKey)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var now = DateTime.UtcNow;
        db.KnowledgeTracingStates.Add(new KnowledgeTracingState
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ConceptGraphSnapshotId = graphId,
            ConceptKey = conceptKey,
            Label = "Slope",
            EvidenceCount = 4,
            CorrectCount = 1,
            IncorrectCount = 3,
            MasteryProbability = 0.22m,
            Confidence = 0.75m,
            RemediationNeed = "high",
            PracticeReadiness = "guided",
            LastEvidenceAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.ConceptMasteries.Add(new ConceptMastery
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            ConceptGraphSnapshotId = graphId,
            ConceptKey = conceptKey,
            Label = "Slope",
            MasteryScore = 0.25m,
            Confidence = 0.70m,
            RemediationNeed = "high",
            PracticeReadiness = "guided",
            Attempts = 4,
            Correct = 1,
            LastEvidenceAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }

    private static LearningConcept Concept(
        Guid graphId,
        string key,
        string label,
        int order,
        string difficulty,
        string prerequisites = "[]",
        string misconceptions = "[]") => new()
        {
            Id = Guid.NewGuid(),
            ConceptGraphSnapshotId = graphId,
            StableKey = key,
            Label = label,
            DifficultyBand = difficulty,
            Order = order,
            PrerequisitesJson = prerequisites,
            MisconceptionsJson = misconceptions,
            LearningOutcomeKeysJson = $"""["outcome:{key}"]""",
            CreatedAt = DateTime.UtcNow
        };

    private static ConceptRelation Relation(Guid graphId, string source, string target) => new()
    {
        Id = Guid.NewGuid(),
        ConceptGraphSnapshotId = graphId,
        SourceConceptKey = source,
        TargetConceptKey = target,
        RelationType = "prerequisite",
        Weight = 1,
        CreatedAt = DateTime.UtcNow
    };

    private static void AssertNoPublicLeak(string payload, bool allowUserId = false)
    {
        Assert.DoesNotContain("rawPrompt", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hiddenPrompt", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("systemPrompt", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("developerPrompt", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawProviderPayload", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawSourceChunk", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawToolPayload", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("debugTrace", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localPath", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correctAnswer", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", payload, StringComparison.OrdinalIgnoreCase);
        if (!allowUserId)
        {
            Assert.DoesNotContain("ownerId", payload, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed record LifeTestUser(HttpClient Client, Guid UserId, string Email, string RefreshToken);
}
