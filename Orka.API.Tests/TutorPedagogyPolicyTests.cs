using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class TutorPedagogyPolicyTests : IClassFixture<ApiSmokeFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ApiSmokeFactory _factory;

    public TutorPedagogyPolicyTests(ApiSmokeFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PolicyEndpoint_UsesTurnPlanAssessmentSourceAndToolContextSafely()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "tutor-policy-owner");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Tutor Policy Topic");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(_factory, user.UserId, topicId, DateTime.UtcNow);
        await SeedTutorPolicyContextAsync(user.UserId, topicId, sessionId);

        var response = await user.Client.GetAsync($"/api/tutor/policy/topic/{topicId}?sessionId={sessionId}");

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;

        Assert.Equal("misconception_repair", root.GetProperty("teachingMove").GetString());
        Assert.Equal("source_grounded", root.GetProperty("sourceReadiness").GetString());
        Assert.Equal("cite_sources", root.GetProperty("groundingPolicy").GetString());
        Assert.Equal("micro_quiz", root.GetProperty("latestAssessmentMode").GetString());
        Assert.Equal("run_tool_if_allowed", root.GetProperty("toolPolicy").GetString());
        Assert.Contains(root.GetProperty("contextUse").EnumerateArray(), c =>
            c.GetProperty("contextType").GetString() == "plan_step" &&
            c.GetProperty("status").GetString() == "available");
        Assert.Contains(root.GetProperty("nextActions").EnumerateArray(), a =>
            a.GetProperty("actionType").GetString() == "start_micro_quiz");

        var publicJson = root.GetRawText();
        Assert.DoesNotContain(user.UserId.ToString(), publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("providerPayload", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hiddenPrompt", publicJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Evaluate_BlocksAnswerLeakSourceOverclaimUnsafeCopyAndPassiveWeakAdvice()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "tutor-policy-eval");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, user.UserId, "Tutor Policy Eval");

        var response = await user.Client.PostAsJsonAsync("/api/tutor/policy/evaluate", new
        {
            topicId,
            activeQuizUnsubmitted = true,
            assistantAnswer = "Dogru cevap A. Kaynaklara gore kesin. Kesin basarirsin. Notlari oku.",
            policy = new
            {
                groundingPolicy = "evidence_insufficient",
                remediationPolicy = "guided_repair",
                contextUse = new[]
                {
                    new { contextType = "active_lesson", status = "available", userSafeSummary = "active" },
                    new { contextType = "plan_step", status = "available", userSafeSummary = "Plan step" }
                },
                warnings = Array.Empty<object>()
            }
        });

        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = body.RootElement;
        var blockingCodes = root.GetProperty("blockingIssues").EnumerateArray()
            .Select(i => i.GetProperty("code").GetString())
            .ToArray();
        var warningCodes = root.GetProperty("warningIssues").EnumerateArray()
            .Select(i => i.GetProperty("code").GetString())
            .ToArray();

        Assert.Equal("needs_revision", root.GetProperty("qualityStatus").GetString());
        Assert.Contains("answer_key_leak", blockingCodes);
        Assert.Contains("source_overclaim", blockingCodes);
        Assert.Contains("success_guarantee", blockingCodes);
        Assert.Contains("passive_only_weak_learner", warningCodes);
    }

    [Fact]
    public async Task MissingContext_DegradesSafelyWithoutRawPayloads()
    {
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "tutor-policy-fallback");

        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITutorResponsePolicyService>();
        var policy = await service.BuildPolicyAsync(user.UserId, new TutorResponsePolicyRequestDto());

        Assert.Equal("explain", policy.TeachingMove);
        Assert.Equal("evidence_insufficient", policy.SourceReadiness);
        Assert.Equal("evidence_insufficient", policy.GroundingPolicy);
        Assert.Contains(policy.ContextUse, c => c.ContextType == "turn_state" && c.Status == "not_available");
        Assert.Contains(policy.Warnings, w => w.Code == "missing_turn_state");

        var json = JsonSerializer.Serialize(policy, JsonOptions);
        Assert.DoesNotContain(user.UserId.ToString(), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prompt", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PolicyEndpoints_AreCallerScoped()
    {
        var owner = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "tutor-policy-private");
        var other = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(_factory, "tutor-policy-cross");
        var topicId = await CoordinationTestHelpers.SeedTopicAsync(_factory, owner.UserId, "Private Tutor Policy");
        var sessionId = await CoordinationTestHelpers.SeedSessionAsync(_factory, owner.UserId, topicId, DateTime.UtcNow);
        await SeedTutorPolicyContextAsync(owner.UserId, topicId, sessionId);

        var crossTopic = await other.Client.GetAsync($"/api/tutor/policy/topic/{topicId}?sessionId={sessionId}");
        var crossSession = await other.Client.GetAsync($"/api/tutor/policy/session/{sessionId}");

        Assert.Equal(HttpStatusCode.NotFound, crossTopic.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossSession.StatusCode);
    }

    private async Task SeedTutorPolicyContextAsync(Guid userId, Guid topicId, Guid sessionId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var turnId = Guid.NewGuid();
        var traceId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var turn = new TutorTurnStateDto
        {
            Id = turnId,
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            ActiveLessonSnapshotId = Guid.NewGuid(),
            StudentContextSnapshotId = Guid.NewGuid(),
            PlanQualitySnapshotId = Guid.NewGuid(),
            ActiveConceptKey = "linear-equation",
            ActiveConceptLabel = "Linear equation",
            LearnerState = "needs_remediation",
            LessonSnapshotStatus = "active",
            StudentContextConfidenceStatus = "usable",
            MasteryProbability = 0.35m,
            Confidence = 0.42m,
            RemediationNeed = "high",
            GroundingStatus = "source_grounded",
            SourceEvidenceCount = 2,
            CurrentPlanStepId = "step-1",
            CurrentPlanStepTitle = "Repair linear equation setup",
            CurrentPlanTutorMove = "misconception_repair",
            CurrentPlanQuizHook = "micro_quiz",
            PlanSourceReadiness = "source_grounded",
            SourceReadiness = "source_grounded",
            LatestAssessmentMode = "micro_quiz",
            LatestMisconceptionConfidence = "usable",
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.TutorTurnStates.Add(new TutorTurnState
        {
            Id = turnId,
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            ActiveConceptKey = turn.ActiveConceptKey,
            TeachingMode = "remediate",
            GroundingStatus = "source_grounded",
            StateJson = JsonSerializer.Serialize(turn, JsonOptions),
            CreatedAt = now
        });
        db.TutorActionTraces.Add(new TutorActionTrace
        {
            Id = traceId,
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            TutorTurnStateId = turnId,
            TeachingMode = "remediate",
            ActiveConceptKey = turn.ActiveConceptKey,
            GroundingPolicy = "cite_sources",
            ToolPlanJson = "[]",
            ArtifactPlanJson = "[]",
            NextCheckPrompt = "Short check",
            CreatedAt = now.AddSeconds(1)
        });
        db.SourceEvidenceBundles.Add(new SourceEvidenceBundle
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            BundleHash = Guid.NewGuid().ToString("N"),
            EvidenceStatus = "source_grounded",
            SourceCount = 1,
            ReadySourceCount = 1,
            ChunkCount = 2,
            CitationCoverage = 0.9m,
            EvidenceJson = "{\"items\":[{\"label\":\"safe\"}]}",
            CreatedAt = now,
            UpdatedAt = now.AddSeconds(2)
        });
        db.QuizAttempts.Add(new QuizAttempt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            Question = "Seed question",
            UserAnswer = "B",
            IsCorrect = false,
            Explanation = "Needs setup repair.",
            SourceRefsJson = "{\"assessmentMode\":\"micro_quiz\",\"sourceReadiness\":\"source_grounded\"}",
            CreatedAt = now.AddSeconds(3)
        });
        db.TutorToolCalls.Add(new TutorToolCall
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            TutorActionTraceId = traceId,
            ToolId = "source_search",
            Status = "completed",
            Success = true,
            Evidence = "bounded safe evidence",
            SafeMessage = "Source evidence ready.",
            CreatedAt = now.AddSeconds(4)
        });
        await db.SaveChangesAsync();
    }
}
