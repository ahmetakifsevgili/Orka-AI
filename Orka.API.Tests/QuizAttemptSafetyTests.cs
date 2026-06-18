using System.Net.Http.Headers;
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
    public async Task QuizAttempt_DropsUntrustedSourceEvidenceBundleBeforePersistence()
    {
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "quiz-source-owner");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "quiz-source-other");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, owner.UserId, "Source Poison Quiz");
        var (_, assessmentItemId) = await SeedAssessmentItemAsync(owner.UserId, topicId);
        var maliciousBundleId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            db.SourceEvidenceBundles.Add(new SourceEvidenceBundle
            {
                Id = maliciousBundleId,
                UserId = other.UserId,
                TopicId = topicId,
                BundleHash = "cross-user",
                EvidenceStatus = "source_grounded",
                SourceCount = 1,
                ReadySourceCount = 1,
                ChunkCount = 1,
                CitationCoverage = 1m,
                EvidenceJson = "{}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await owner.Client.PostAsJsonAsync("/api/quiz/attempt", new
        {
            topicId,
            assessmentItemId,
            questionId = "q-auth",
            question = "Server hangi secenegi dogrular?",
            selectedOptionId = "B) Server dogrular",
            sourceEvidenceBundleId = maliciousBundleId,
            sourceRefsJson = """{ "sourceReadiness": "source_grounded", "rawSourceRefs": ["poison"] }""",
            skillTag = "server-authority",
            conceptKey = "server-authority",
            questionHash = $"source-poison-{Guid.NewGuid():N}"
        });

        response.EnsureSuccessStatusCode();

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var attempt = await verifyDb.QuizAttempts.AsNoTracking().SingleAsync(a => a.UserId == owner.UserId && a.AssessmentItemId == assessmentItemId);
        Assert.Contains("client_rejected", attempt.SourceRefsJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("evidence_insufficient", attempt.SourceRefsJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(maliciousBundleId.ToString("D"), attempt.SourceRefsJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawSourceRefs", attempt.SourceRefsJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task QuizAttempt_RejectsClientSuppliedSourceEvidenceBundleWithoutOwnershipAndReadiness()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "quiz-source-evidence");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Source Evidence Quiz");
        var (_, assessmentItemId) = await SeedAssessmentItemAsync(user.UserId, topicId);
        var foreignBundleId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            db.SourceEvidenceBundles.Add(new SourceEvidenceBundle
            {
                Id = foreignBundleId,
                UserId = Guid.NewGuid(),
                TopicId = topicId,
                BundleHash = Guid.NewGuid().ToString("N"),
                EvidenceStatus = "source_grounded",
                ReadySourceCount = 1,
                ChunkCount = 1,
                CitationCoverage = 1m,
                EvidenceJson = "{}",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await user.Client.PostAsJsonAsync("/api/quiz/attempt", new
        {
            topicId,
            assessmentItemId,
            sourceEvidenceBundleId = foreignBundleId,
            sourceRefsJson = "{\"sourceReadiness\":\"source_grounded\",\"rawSourceRefs\":[\"poison\"]}",
            questionId = "q-auth",
            question = "Server hangi secenegi dogrular?",
            selectedOptionId = "B) Server dogrular",
            skillTag = "server-authority",
            conceptKey = "server-authority",
            questionHash = $"source-bundle-{Guid.NewGuid():N}"
        });

        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var evidenceBasis = json.RootElement.GetProperty("learningImpact").GetProperty("evidenceBasis")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();
        Assert.DoesNotContain("source_evidence_bundle", evidenceBasis);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var attempt = await verifyDb.QuizAttempts.AsNoTracking().SingleAsync(a => a.UserId == user.UserId && a.AssessmentItemId == assessmentItemId);
        Assert.DoesNotContain(foreignBundleId.ToString("D"), attempt.SourceRefsJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("client_rejected", attempt.SourceRefsJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("evidence_insufficient", attempt.SourceRefsJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawSourceRefs", attempt.SourceRefsJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task QuizAttempt_BlankAnswerCreatesPrerequisiteRepairWithoutMisconceptionCertainty()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "quiz-blank-repair");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Blank Repair Quiz");
        var (_, assessmentItemId) = await SeedAssessmentItemAsync(user.UserId, topicId);
        var wikiPageId = await SeedWikiConceptPageAsync(user.UserId, topicId, "server-authority");

        var response = await user.Client.PostAsJsonAsync("/api/quiz/attempt", new
        {
            topicId,
            assessmentItemId,
            questionId = "q-auth",
            question = "Server hangi secenegi dogrular?",
            selectedOptionId = "skip",
            wasSkipped = true,
            skillTag = "server-authority",
            conceptKey = "server-authority",
            assessmentMode = "micro_quiz",
            questionHash = $"blank-repair-{Guid.NewGuid():N}"
        });

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var impact = doc.RootElement.GetProperty("learningImpact");

        Assert.Equal("blank", impact.GetProperty("result").GetString());
        Assert.Equal("medium", impact.GetProperty("remediationNeed").GetString());
        Assert.Equal("prerequisite_scaffold", impact.GetProperty("nextTutorMove").GetString());
        Assert.Equal("insert_prerequisite_review", impact.GetProperty("nextPlanAction").GetString());
        Assert.Equal("observed_only", impact.GetProperty("misconceptionConfidence").GetString());
        var remediationLesson = impact.GetProperty("remediationLesson");
        Assert.Equal("prerequisite_repair", remediationLesson.GetProperty("repairType").GetString());
        Assert.Equal("skipped_answer", remediationLesson.GetProperty("trigger").GetProperty("triggerType").GetString());
        Assert.True(remediationLesson.GetProperty("checkpoint").GetProperty("avoidsPreSubmitReveal").GetBoolean());
        Assert.Equal("do_not_overstate_mastery", remediationLesson.GetProperty("outcome").GetProperty("masteryPolicy").GetString());
        Assert.Contains("blank_answer_not_misconception", remediationLesson.GetProperty("warnings").EnumerateArray().Select(e => e.GetString()));
        Assert.True(!impact.TryGetProperty("misconceptionSignal", out var signal) || signal.ValueKind == JsonValueKind.Null);
        Assert.DoesNotContain("correctAnswer", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("answerKey", json, StringComparison.OrdinalIgnoreCase);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var repair = await db.WikiBlocks
            .AsNoTracking()
            .SingleAsync(b => b.WikiPageId == wikiPageId && b.BlockType == WikiBlockType.RepairNote);
        Assert.Equal("server-authority", repair.ConceptKey);
        Assert.Equal("assessment_verified", repair.SourceBasis);
        Assert.Contains("Sonuc: blank", repair.Content);
        Assert.Contains("Remediation ihtiyaci: medium", repair.Content);
        Assert.Contains("Telafi tipi: prerequisite_repair", repair.Content);
        Assert.Contains("Tutor sonraki hamlesi: prerequisite_scaffold", repair.Content);
    }

    [Fact]
    public async Task QuizAttempt_CorrectAnswerAfterRemediationStarted_RecordsCompletedOnce()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "quiz-remediation-complete");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Remediation Completion Quiz");
        var (quizRunId, assessmentItemId) = await SeedAssessmentItemAsync(user.UserId, topicId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            db.LearningSignals.Add(new LearningSignal
            {
                Id = Guid.NewGuid(),
                UserId = user.UserId,
                TopicId = topicId,
                SignalType = LearningSignalTypes.RemediationStarted,
                SkillTag = "server-authority",
                TopicPath = "server-authority",
                Score = 0,
                IsPositive = false,
                PayloadJson = """
                {
                  "schemaVersion": "orka.remediation-lifecycle.v1",
                  "selectedAction": "start_remediation",
                  "recordedAt": "2026-06-18T00:00:00Z"
                }
                """,
                CreatedAt = DateTime.UtcNow.AddMinutes(-2)
            });
            await db.SaveChangesAsync();
        }

        var requestPayload = new
        {
            quizRunId,
            topicId,
            assessmentItemId,
            questionId = "q-auth",
            question = "Server hangi secenegi dogrular?",
            selectedOptionId = "B",
            isCorrect = false,
            explanation = "client-provided explanation must be ignored",
            skillTag = "server-authority",
            conceptKey = "server-authority",
            questionHash = $"remediation-complete-{Guid.NewGuid():N}"
        };

        var response = await user.Client.PostAsJsonAsync("/api/quiz/attempt", requestPayload);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("correct", json.RootElement.GetProperty("learningImpact").GetProperty("result").GetString());
        var attemptId = json.RootElement.GetProperty("id").GetGuid();

        var replay = await user.Client.PostAsJsonAsync("/api/quiz/attempt", requestPayload);
        replay.EnsureSuccessStatusCode();

        using var scopeAfter = _factory.Services.CreateScope();
        var dbAfter = scopeAfter.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var completions = await dbAfter.LearningSignals
            .AsNoTracking()
            .Where(s =>
                s.UserId == user.UserId &&
                s.TopicId == topicId &&
                s.SignalType == LearningSignalTypes.RemediationCompleted)
            .ToListAsync();

        var completion = Assert.Single(completions);
        Assert.Equal("server-authority", completion.SkillTag);
        Assert.Equal(100, completion.Score);
        Assert.True(completion.IsPositive.GetValueOrDefault());
        Assert.False(string.IsNullOrWhiteSpace(completion.PayloadJson));
        using var payload = JsonDocument.Parse(completion.PayloadJson!);
        Assert.Equal("orka.remediation-lifecycle.v1", payload.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("verified_correct_attempt", payload.RootElement.GetProperty("completedBy").GetString());
        Assert.Equal(attemptId, payload.RootElement.GetProperty("quizAttemptId").GetGuid());
    }

    [Fact]
    public async Task QuizAttempt_DoubleSubmit_ReturnsGracefullyAndPreventsDuplicate()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "quiz-double-submit");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Double Submit Quiz");
        var (_, assessmentItemId) = await SeedAssessmentItemAsync(user.UserId, topicId);

        var requestPayload = new
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
            questionHash = $"double-submit-{Guid.NewGuid():N}"
        };

        // Send first submit
        var response1 = await user.Client.PostAsJsonAsync("/api/quiz/attempt", requestPayload);
        response1.EnsureSuccessStatusCode();
        using var json1 = await JsonDocument.ParseAsync(await response1.Content.ReadAsStreamAsync());
        var attemptId1 = json1.RootElement.GetProperty("id").GetGuid();

        // Send second submit (double submit)
        var response2 = await user.Client.PostAsJsonAsync("/api/quiz/attempt", requestPayload);
        response2.EnsureSuccessStatusCode(); // Must be 200 OK, not 500!
        using var json2 = await JsonDocument.ParseAsync(await response2.Content.ReadAsStreamAsync());
        var attemptId2 = json2.RootElement.GetProperty("id").GetGuid();

        // They must return the exact same attempt ID!
        Assert.Equal(attemptId1, attemptId2);

        // Verify only 1 attempt exists in the database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var count = await db.QuizAttempts.CountAsync(a => a.UserId == user.UserId && a.AssessmentItemId == assessmentItemId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task QuizAttempt_DoubleSubmitWithNullTopicId_ReturnsGracefullyAndPreventsDuplicate()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "quiz-double-submit-null-topic");

        var requestPayload = new
        {
            questionId = "q-null-topic",
            question = "Soru text?",
            selectedOptionId = "A) Cevap",
            skillTag = "null-topic-skill",
            conceptKey = "null-topic-concept",
            questionHash = $"double-submit-null-topic-{Guid.NewGuid():N}"
        };

        // Send first submit
        var response1 = await user.Client.PostAsJsonAsync("/api/quiz/attempt", requestPayload);
        response1.EnsureSuccessStatusCode();
        using var json1 = await JsonDocument.ParseAsync(await response1.Content.ReadAsStreamAsync());
        var attemptId1 = json1.RootElement.GetProperty("id").GetGuid();

        // Send second submit (double submit)
        var response2 = await user.Client.PostAsJsonAsync("/api/quiz/attempt", requestPayload);
        response2.EnsureSuccessStatusCode(); // Must be 200 OK
        using var json2 = await JsonDocument.ParseAsync(await response2.Content.ReadAsStreamAsync());
        var attemptId2 = json2.RootElement.GetProperty("id").GetGuid();

        // They must return the exact same attempt ID!
        Assert.Equal(attemptId1, attemptId2);
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
