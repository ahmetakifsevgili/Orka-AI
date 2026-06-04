using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public sealed class TutorPedagogyEvaluationTests
{
    [Fact]
    public void Rubric_LowMasteryDirectAnswerWithoutScaffold_IsDegraded()
    {
        var service = new TutorPedagogyRubricService();
        var request = Request(
            mastery: 0.25m,
            confidence: 0.20m,
            directAnswerRisk: true,
            directAnswerPolicy: "hint_first_then_scaffold",
            answer: "Cevap B seçeneği. Çünkü böyledir.");

        var scores = service.EvaluateDeterministic(request);

        Assert.Contains(scores, s => s.RubricKey == "scaffolding_quality" && s.Score < 0.60m);
        Assert.Contains(scores, s => s.RubricKey == "micro_check" && s.Score < 0.60m);
    }

    [Fact]
    public void Rubric_SourceClaimWithoutEvidence_IsCritical()
    {
        var service = new TutorPedagogyRubricService();
        var request = Request(
            sourceEvidenceCount: 0,
            answer: "Kaynağa göre bu sonuç kesin olarak böyle açıklanır.");

        var scores = service.EvaluateDeterministic(request);

        Assert.Contains(scores, s => s.RubricKey == "grounding_discipline" && s.IsCritical);
    }

    [Fact]
    public void Rubric_LowEvidenceLearningStyleOverclaim_IsCritical()
    {
        var service = new TutorPedagogyRubricService();
        var request = Request(
            confidence: 0.30m,
            answer: "Sen görsel öğreniyorsun; artık öğrendin.");

        var scores = service.EvaluateDeterministic(request);

        Assert.Contains(scores, s => s.RubricKey == "safety_integrity" && s.IsCritical);
    }

    [Fact]
    public void Rubric_GenericQuestionMark_IsNotProfessionalMicroCheck()
    {
        var service = new TutorPedagogyRubricService();
        var request = Request(answer: "Anladin mi?");

        var scores = service.EvaluateDeterministic(request);

        Assert.Contains(scores, s => s.RubricKey == "micro_check" && s.Score < 0.60m);
    }

    [Fact]
    public void Rubric_ConceptualLearnerAction_IsProfessionalMicroCheck()
    {
        var service = new TutorPedagogyRubricService();
        var request = Request(answer: "For concept a, try one: why does this step work?");

        var scores = service.EvaluateDeterministic(request);

        Assert.Contains(scores, s => s.RubricKey == "micro_check" && s.Score >= 0.80m);
    }

    [Fact]
    public async Task EvaluationService_WritesRunScoresEventsAndFeedbackPatch()
    {
        await using var db = CreateDb();
        var (userId, topicId) = await SeedAsync(db);
        var redis = new FakeRedis();
        var workingMemory = new TestTutorWorkingMemoryService();
        var schema = new LearningEventSchemaService(db, NullLogger<LearningEventSchemaService>.Instance);
        var feedback = new TutorPedagogyFeedbackService(db, redis, workingMemory, schema);
        var service = new TutorPedagogyEvaluationService(
            db,
            new TutorPedagogyRubricService(),
            feedback,
            schema,
            workingMemory,
            new FakeAgentFactory(),
            new ConfigurationBuilder().Build(),
            NullLogger<TutorPedagogyEvaluationService>.Instance);

        var request = Request(
            userId,
            topicId,
            mastery: 0.20m,
            confidence: 0.20m,
            directAnswerRisk: true,
            directAnswerPolicy: "hint_first_then_scaffold",
            answer: "Kaynağa göre cevap B. Artık öğrendin.");
        request.TurnState.SourceEvidenceCount = 0;

        var result = await service.EvaluateAsync(request);

        Assert.Equal("degraded", result.Status);
        Assert.True(result.HasCriticalViolation);
        Assert.Equal(1, await db.TutorPedagogyEvaluationRuns.CountAsync());
        Assert.True(await db.TutorPedagogyRubricScores.AnyAsync(s => s.IsCritical));
        Assert.Equal(1, await db.TutorPedagogyFeedbackPatches.CountAsync());
        Assert.True(await db.LearningEvents.AnyAsync(e => e.EventType == "tutor.pedagogy.evaluated"));
        Assert.True(await db.LearningEvents.AnyAsync(e => e.EventType == "tutor.feedback.patch.created"));
        Assert.Contains(redis.Stored.Keys, k => k.StartsWith("orka:v3:tutor-pedagogy-feedback:", StringComparison.Ordinal));
        Assert.Contains(workingMemory.Events, e => e.EventType == "tutor.pedagogy_evaluation.ready");
    }

    [Fact]
    public async Task ActionPlanner_UnknownMastery_DoesNotForceRemediation()
    {
        await using var db = CreateDb();
        var (userId, topicId) = await SeedAsync(db);
        var sessionId = Guid.NewGuid();
        var state = new TutorTurnStateDto
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = sessionId,
            UserMessage = "Explain the next idea clearly.",
            ActiveConceptKey = "concept-a",
            ActiveConceptLabel = "Concept A",
            LearnerState = "unknown",
            RemediationNeed = "unknown",
            PracticeReadiness = "guided",
            GroundingStatus = "model_only",
            SourceEvidenceCount = 0
        };

        var plan = await new TutorActionPlanner(db, new TestTutorWorkingMemoryService()).PlanAsync(state);

        Assert.NotEqual("remediate", plan.TeachingMode);
        Assert.Null(plan.RemediationLesson);
    }

    [Fact]
    public async Task ActionPlanner_RemediationSignalWithoutMastery_UsesBeginnerDeliveryLevel()
    {
        await using var db = CreateDb();
        var (userId, topicId) = await SeedAsync(db);
        var state = new TutorTurnStateDto
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionId = Guid.NewGuid(),
            UserMessage = "I still do not understand this misconception.",
            ActiveConceptKey = "concept-a",
            ActiveConceptLabel = "Concept A",
            LearnerState = "needs_remediation",
            RemediationNeed = "medium",
            PracticeReadiness = "guided",
            LearningLoopStatus = "remediation_ready",
            LatestAssessmentMode = "misconception_probe",
            MisconceptionSignal = new MisconceptionSignalDto
            {
                Category = "concept_confusion",
                UserSafeLabel = "Concept confusion",
                ConfidenceStatus = "usable",
                ConceptKey = "concept-a",
                Label = "Concept A"
            },
            RemediationSeed = new RemediationSeedDto
            {
                ConceptKey = "concept-a",
                Label = "Concept A",
                ConfidenceStatus = "usable",
                FirstAction = "tutor_explain"
            }
        };

        var plan = await new TutorActionPlanner(db, new TestTutorWorkingMemoryService()).PlanAsync(state);

        Assert.Equal("beginner", plan.LessonDelivery?.LearnerLevel);
        Assert.Equal("misconception_repair", plan.LessonDelivery?.DeliveryMode);
        Assert.NotNull(plan.RemediationLesson);
    }

    [Fact]
    public void GoldenScenarioService_ContainsCanonicalTutorCases()
    {
        var scenarios = new TutorGoldenScenarioService().GetCanonicalScenarios();

        Assert.Contains(scenarios, s => s.ScenarioKey == "low_mastery_direct_answer");
        Assert.Contains(scenarios, s => s.ScenarioKey == "diagnostic_skip");
        Assert.Contains(scenarios, s => s.RequiredRubrics.Contains("grounding_discipline"));
    }

    private static TutorPedagogyEvaluationRequestDto Request(
        decimal? mastery = 0.35m,
        decimal? confidence = 0.35m,
        bool directAnswerRisk = false,
        string directAnswerPolicy = "scaffold",
        int sourceEvidenceCount = 1,
        string answer = "Önce küçük bir ipucu verelim. Bunu kendi cümlenle özetler misin?")
        => Request(Guid.NewGuid(), Guid.NewGuid(), mastery, confidence, directAnswerRisk, directAnswerPolicy, sourceEvidenceCount, answer);

    private static TutorPedagogyEvaluationRequestDto Request(
        Guid userId,
        Guid topicId,
        decimal? mastery = 0.35m,
        decimal? confidence = 0.35m,
        bool directAnswerRisk = false,
        string directAnswerPolicy = "scaffold",
        int sourceEvidenceCount = 1,
        string answer = "Önce küçük bir ipucu verelim. Bunu kendi cümlenle özetler misin?")
    {
        var turnStateId = Guid.NewGuid();
        var actionTraceId = Guid.NewGuid();
        return new TutorPedagogyEvaluationRequestDto
        {
            TurnState = new TutorTurnStateDto
            {
                Id = turnStateId,
                UserId = userId,
                TopicId = topicId,
                SessionId = Guid.NewGuid(),
                UserMessage = "Direkt cevabı ver, anlamadım.",
                ActiveConceptKey = "concept-a",
                ActiveConceptLabel = "Concept A",
                LearnerState = "needs_remediation",
                MasteryProbability = mastery,
                Confidence = confidence,
                RemediationNeed = "high",
                CognitiveLoad = "high",
                AffectiveState = "confused",
                DirectAnswerRisk = directAnswerRisk,
                SourceEvidenceCount = sourceEvidenceCount,
                RecentMistakes = ["confuses concept A"]
            },
            ActionPlan = new TutorActionPlanDto
            {
                Id = actionTraceId,
                TutorTurnStateId = turnStateId,
                UserId = userId,
                TopicId = topicId,
                SessionId = Guid.NewGuid(),
                TeachingMode = "remediate",
                ActiveConceptKey = "concept-a",
                DirectAnswerPolicy = directAnswerPolicy,
                GroundingPolicy = sourceEvidenceCount > 0 ? "cite_available_sources" : "model_ok_no_source_claim",
                ArtifactPlans = [new TeachingArtifactPlanDto("worked_example", "repair misconception", "markdown")]
            },
            Reflection = new TutorReflectionUpdateDto
            {
                Id = Guid.NewGuid(),
                TutorActionTraceId = actionTraceId,
                TutorTurnStateId = turnStateId,
                DirectAnswerRiskHandled = answer.Contains("ipucu", StringComparison.OrdinalIgnoreCase),
                MicroCheckAsked = answer.Contains('?'),
                SourceClaimWithoutSource = sourceEvidenceCount == 0 && answer.Contains("kaynağa göre", StringComparison.OrdinalIgnoreCase)
            },
            AssistantAnswer = answer
        };
    }

    private static OrkaDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<OrkaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task<(Guid UserId, Guid TopicId)> SeedAsync(OrkaDbContext db)
    {
        var userId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        db.Users.Add(new User { Id = userId, Email = $"{userId:N}@example.com", CreatedAt = DateTime.UtcNow });
        db.Topics.Add(new Topic { Id = topicId, UserId = userId, Title = "Tutor Pedagogy", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return (userId, topicId);
    }

    private sealed class FakeAgentFactory : IAIAgentFactory
    {
        public string GetModel(AgentRole role) => "fake";
        public string GetProvider(AgentRole role) => "fake";
        public Task<string> CompleteChatAsync(AgentRole role, string systemPrompt, string userMessage, CancellationToken ct = default) =>
            Task.FromResult("""{"score":0.8,"evidence":"ok","recommendation":"keep scaffolding"}""");
        public async IAsyncEnumerable<string> StreamChatAsync(AgentRole role, string systemPrompt, string userMessage, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return await CompleteChatAsync(role, systemPrompt, userMessage, ct);
        }
        public Task<string> CompleteChatWithHistoryAsync(AgentRole role, string systemPrompt, IEnumerable<(string Role, string Content)> messages, CancellationToken ct = default) =>
            CompleteChatAsync(role, systemPrompt, string.Join("\n", messages.Select(m => m.Content)), ct);
    }

    private sealed class TestTutorWorkingMemoryService : ITutorWorkingMemoryService
    {
        public List<(Guid SessionId, string EventType)> Events { get; } = [];
        public Task<TutorWorkingMemorySnapshot> SaveTurnSnapshotAsync(TutorTurnStateDto state, CancellationToken ct = default) =>
            Task.FromResult(new TutorWorkingMemorySnapshot { Id = Guid.NewGuid(), UserId = state.UserId, TopicId = state.TopicId, SessionId = state.SessionId });
        public Task<TutorMemoryPatchDto> ApplyPatchAsync(Guid userId, Guid? topicId, Guid? sessionId, string patchType, object patch, CancellationToken ct = default) =>
            Task.FromResult(new TutorMemoryPatchDto { Id = Guid.NewGuid(), UserId = userId, TopicId = topicId, SessionId = sessionId, PatchType = patchType });
        public Task RecordStreamEventAsync(Guid sessionId, string eventType, IReadOnlyDictionary<string, string> fields, CancellationToken ct = default)
        {
            Events.Add((sessionId, eventType));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRedis : IRedisMemoryService
    {
        public Dictionary<string, string> Stored { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Task<string?> GetJsonAsync(string key) => Task.FromResult(Stored.TryGetValue(key, out var value) ? value : null);
        public Task SetJsonAsync(string key, string payload, TimeSpan ttl) { Stored[key] = payload; return Task.CompletedTask; }
        public Task AddStreamEventAsync(string key, IReadOnlyDictionary<string, string> values, TimeSpan? ttl = null) => Task.CompletedTask;
        public Task<IReadOnlyList<RedisStreamEventDto>> ReadStreamEventsAsync(string key, string afterId = "0-0", int count = 50) => Task.FromResult<IReadOnlyList<RedisStreamEventDto>>([]);
        public Task<bool> EnsureConsumerGroupAsync(string key, string group, string startId = "0-0") => Task.FromResult(true);
        public Task<IReadOnlyList<RedisStreamEventDto>> ReadConsumerGroupAsync(string key, string group, string consumer, int count = 50, string streamId = ">") => Task.FromResult<IReadOnlyList<RedisStreamEventDto>>([]);
        public Task AckStreamEventsAsync(string key, string group, IEnumerable<string> eventIds) => Task.CompletedTask;
        public Task<bool> SupportsVectorSearchAsync() => Task.FromResult(false);
        public Task DeleteKeyAsync(string key) { Stored.Remove(key); return Task.CompletedTask; }
        public Task RecordEvaluationAsync(Guid sessionId, int score, string feedback) => Task.CompletedTask;
        public Task<IEnumerable<string>> GetRecentFeedbackAsync(Guid sessionId, int count = 5) => Task.FromResult<IEnumerable<string>>([]);
        public Task<bool> CheckRateLimitAsync(string clientIp, int maxRequests, TimeSpan window) => Task.FromResult(true);
        public Task SetGlobalPolicyAsync(string policyText) => Task.CompletedTask;
        public Task<string> GetGlobalPolicyAsync() => Task.FromResult(string.Empty);
        public Task SetLastPistonResultAsync(Guid sessionId, string code, string stdout, string stderr, string language, string phase = "run", string? compileError = null, string? runtimeError = null, bool success = true, string? safeTutorSummary = null) => Task.CompletedTask;
        public Task<string> GetLastPistonResultAsync(Guid sessionId) => Task.FromResult(string.Empty);
        public Task SetWikiReadyAsync(Guid topicId) => Task.CompletedTask;
        public Task SaveGoldExampleAsync(Guid userId, Guid topicId, string userMessage, string agentResponse, int score) => Task.CompletedTask;
        public Task<IEnumerable<GoldExample>> GetGoldExamplesAsync(Guid userId, Guid topicId, int count = 2) => Task.FromResult<IEnumerable<GoldExample>>([]);
        public Task RecordAgentMetricAsync(string agentRole, long latencyMs, bool isSuccess, string? provider = null) => Task.CompletedTask;
        public Task<IEnumerable<AgentMetricSummary>> GetSystemMetricsAsync() => Task.FromResult<IEnumerable<AgentMetricSummary>>([]);
        public Task<IEnumerable<EvaluatorLogEntry>> GetRecentEvaluatorLogsAsync(int count = 20) => Task.FromResult<IEnumerable<EvaluatorLogEntry>>([]);
        public Task<IEnumerable<ProviderUsageStat>> GetProviderUsageAsync() => Task.FromResult<IEnumerable<ProviderUsageStat>>([]);
        public Task<long> GetTopicVersionAsync(Guid topicId) => Task.FromResult(1L);
        public Task<long> BumpTopicVersionAsync(Guid topicId, string reason) => Task.FromResult(2L);
        public Task InvalidateLearningCachesAsync(Guid userId, Guid topicId, string reason) => Task.CompletedTask;
        public Task RecordCacheMetricAsync(string area, bool hit, string? tool = null, double? latencyMs = null) => Task.CompletedTask;
        public Task<IEnumerable<CacheMetricSummary>> GetCacheMetricsAsync() => Task.FromResult<IEnumerable<CacheMetricSummary>>([]);
        public Task<RedisHealthDto> GetRedisHealthAsync() => Task.FromResult(new RedisHealthDto(true, 0, 1, "ok", null, DateTime.UtcNow));
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
        public Task<bool> AcquireLockAsync(string key, string value, TimeSpan expiry) => Task.FromResult(true);
        public Task<bool> RenewLockAsync(string key, string value, TimeSpan expiry) => Task.FromResult(true);
        public Task ReleaseLockAsync(string key, string value) => Task.CompletedTask;
    }
}
