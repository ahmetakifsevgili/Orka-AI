using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Services;
using Xunit;

namespace Orka.Infrastructure.UnitTests;

public sealed class TutorAgentStreamSafetyTests
{
    [Fact]
    public async Task GetResponseStreamAsync_WhenPedagogyPostProcessingFails_ReturnsBufferedAnswerAndDegradedFinalEvent()
    {
        var factory = new FakeAgentFactory();
        var agent = CreateAgent(factory, evaluation: new ThrowingTutorPedagogyEvaluationService());
        var session = NewSession();

        var chunks = new List<string>();
        await foreach (var chunk in agent.GetResponseStreamAsync(session.UserId, "Explain safely.", session, false))
        {
            chunks.Add(chunk);
        }

        Assert.Contains("buffered answer", chunks);
        Assert.Contains(chunks, chunk =>
            chunk.Contains("\"type\":\"final\"", StringComparison.Ordinal) &&
            chunk.Contains("\"tutorPedagogyStatus\":\"degraded\"", StringComparison.Ordinal) &&
            chunk.Contains("\"pedagogyPostProcessingDegraded\":true", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetResponseStreamAsync_WhenRepairRuns_PassesRequestCancellationTokenToProvider()
    {
        var factory = new FakeAgentFactory();
        var agent = CreateAgent(
            factory,
            evaluation: new FakeTutorPedagogyEvaluationService(status: "degraded"),
            gate: new FakeTutorPedagogyQualityGate(requiresRepair: true));
        var session = NewSession();
        using var cts = new CancellationTokenSource();

        var chunks = new List<string>();
        await foreach (var chunk in agent.GetResponseStreamAsync(session.UserId, "Repair this.", session, false, cts.Token))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(cts.Token, factory.LastRepairCancellationToken);
        Assert.Contains("repaired answer", chunks);
        Assert.Contains(chunks, chunk => chunk.Contains("\"type\":\"pedagogy_repaired\"", StringComparison.Ordinal));
    }

    private static TutorAgent CreateAgent(
        FakeAgentFactory factory,
        ITutorPedagogyEvaluationService? evaluation = null,
        ITutorPedagogyQualityGate? gate = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<OrkaDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        var provider = services.BuildServiceProvider();
        return new TutorAgent(
            new FakeContextBuilder(),
            factory,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FakeGraderAgent(),
            new FakeWikiService(),
            NullLogger<TutorAgent>.Instance,
            new FakeRedisMemoryService(),
            new FakeLearningSourceService(),
            new FakeLearningSignalService(),
            new FakeEducatorCoreService(),
            new FakeTutorPolicyEngine(),
            new FakeTutorTurnStateAssembler(),
            new FakeTutorActionPlanner(),
            new FakeTutorToolOrchestrator(),
            new FakeTeachingArtifactService(),
            new FakeTutorReflectionService(),
            evaluation ?? new FakeTutorPedagogyEvaluationService(),
            gate ?? new FakeTutorPedagogyQualityGate(requiresRepair: false));
    }

    private static Session NewSession()
    {
        var userId = Guid.NewGuid();
        return new Session
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = null,
            CreatedAt = DateTime.UtcNow
        };
    }

    private sealed class FakeContextBuilder : IContextBuilder
    {
        public Task<IEnumerable<Message>> BuildContextAsync(Guid topicId, Guid sessionId) =>
            Task.FromResult<IEnumerable<Message>>([]);

        public Task<IEnumerable<Message>> BuildConversationContextAsync(Session session, int maxMessages = 0) =>
            Task.FromResult<IEnumerable<Message>>([]);
    }

    private sealed class FakeAgentFactory : IAIAgentFactory
    {
        public CancellationToken LastRepairCancellationToken { get; private set; }

        public string GetModel(AgentRole role) => "fake";
        public string GetProvider(AgentRole role) => "fake";

        public Task<string> CompleteChatAsync(AgentRole role, string systemPrompt, string userMessage, CancellationToken ct = default)
        {
            if (role == AgentRole.Tutor && systemPrompt.Contains("[repair]", StringComparison.OrdinalIgnoreCase))
            {
                LastRepairCancellationToken = ct;
                return Task.FromResult("repaired answer");
            }

            return Task.FromResult("complete answer");
        }

        public async IAsyncEnumerable<string> StreamChatAsync(
            AgentRole role,
            string systemPrompt,
            string userMessage,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return "buffered ";
            ct.ThrowIfCancellationRequested();
            yield return "answer";
        }

        public Task<string> CompleteChatWithHistoryAsync(
            AgentRole role,
            string systemPrompt,
            IEnumerable<(string Role, string Content)> messages,
            CancellationToken ct = default) =>
            CompleteChatAsync(role, systemPrompt, string.Join("\n", messages.Select(m => m.Content)), ct);
    }

    private sealed class FakeGraderAgent : IGraderAgent
    {
        public Task<bool> IsContextRelevantAsync(string topic, string retrievedContext, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<bool> EvaluateAnswerAsync(string question, string answer, CancellationToken ct = default) =>
            Task.FromResult(true);
    }

    private sealed class FakeWikiService : IWikiService
    {
        public Task<IEnumerable<WikiPage>> GetTopicWikiPagesAsync(Guid topicId, Guid userId) =>
            Task.FromResult<IEnumerable<WikiPage>>([]);
        public Task<WikiPage?> GetWikiPageAsync(Guid pageId, Guid userId) => Task.FromResult<WikiPage?>(null);
        public Task<WikiBlock> AddUserNoteAsync(Guid pageId, Guid userId, string content) => throw new NotSupportedException();
        public Task<WikiBlockDto?> AddWikiBlockAsync(Guid pageId, Guid userId, CreateWikiBlockRequestDto request) => throw new NotSupportedException();
        public Task UpdateWikiBlockAsync(Guid blockId, Guid userId, string? title, string? content) => Task.CompletedTask;
        public Task DeleteWikiBlockAsync(Guid blockId, Guid userId) => Task.CompletedTask;
        public Task AutoUpdateWikiAsync(Guid topicId, string aiContent, string userQuestion, string modelUsed) => Task.CompletedTask;
        public Task<string> GetWikiFullContentAsync(Guid topicId, Guid userId) => Task.FromResult(string.Empty);
        public Task<WikiGraphDto> GetWikiGraphAsync(Guid topicId, Guid userId) => Task.FromResult(new WikiGraphDto());
        public Task<WikiGraphDto?> GetLocalWikiGraphAsync(Guid pageId, Guid userId) => Task.FromResult<WikiGraphDto?>(null);
        public Task<WikiGraphLinkDto?> LinkWikiPagesAsync(Guid userId, CreateWikiLinkRequestDto request) => Task.FromResult<WikiGraphLinkDto?>(null);
        public Task<WikiGraphSyncResultDto> SyncWikiGraphAsync(Guid topicId, Guid userId, WikiGraphSyncRequestDto request) => throw new NotSupportedException();
    }

    private sealed class FakeLearningSourceService : ILearningSourceService
    {
        public Task<LearningSourceSummaryDto> UploadAsync(Guid userId, Guid? topicId, Guid? sessionId, string fileName, string contentType, Stream content, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LearningSourceSummaryDto>> GetTopicSourcesAsync(Guid userId, Guid topicId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<LearningSourceSummaryDto>>([]);
        public Task<SourceNotebookDto?> GetTopicSourceNotebookAsync(Guid userId, Guid topicId, CancellationToken ct = default) => Task.FromResult<SourceNotebookDto?>(null);
        public Task<SourceNotebookDto?> GetSourceNotebookAsync(Guid userId, Guid sourceId, CancellationToken ct = default) => Task.FromResult<SourceNotebookDto?>(null);
        public Task<SourcePageDto?> GetPageAsync(Guid userId, Guid sourceId, int pageNumber, CancellationToken ct = default) => Task.FromResult<SourcePageDto?>(null);
        public Task<SourceAskResultDto> AskAsync(Guid userId, Guid sourceId, string question, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TopicSourceEvidenceDto>> RetrieveTopicEvidenceAsync(Guid userId, Guid topicId, string question, int take = 8, Guid? sourceId = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TopicSourceEvidenceDto>>([]);
        public Task<SourceQualityReportDto> GetTopicQualityAsync(Guid userId, Guid topicId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<LearningSourceSummaryDto?> UpdateSourceAsync(Guid userId, Guid sourceId, string? title, CancellationToken ct = default) => Task.FromResult<LearningSourceSummaryDto?>(null);
        public Task<bool> DeleteSourceAsync(Guid userId, Guid sourceId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<string> BuildTopicGroundingContextAsync(Guid userId, Guid? topicId, string question, CancellationToken ct = default) => Task.FromResult(string.Empty);
    }

    private sealed class FakeLearningSignalService : ILearningSignalService
    {
        public Task RecordQuizAnsweredAsync(QuizAttempt attempt, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordSignalAsync(Guid userId, Guid? topicId, Guid? sessionId, string signalType, string? skillTag = null, string? topicPath = null, int? score = null, bool? isPositive = null, string? payloadJson = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<LearningTopicSummaryDto> GetTopicSummaryAsync(Guid userId, Guid topicId, CancellationToken ct = default) =>
            Task.FromResult(new LearningTopicSummaryDto(topicId, 0, 0, 0, [], []));
        public Task<IReadOnlyList<StudyRecommendationDto>> GetRecommendationsAsync(Guid userId, Guid topicId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<StudyRecommendationDto>>([]);
    }

    private sealed class FakeEducatorCoreService : IEducatorCoreService
    {
        public int RecordCalls { get; private set; }

        public Task<TeacherContext> BuildTeacherContextAsync(Guid userId, Guid? topicId, Guid? sessionId, string question, string notebookContext, string wikiContext, string learningSignalContext, string? rawYouTubeContext, CancellationToken ct = default) =>
            Task.FromResult(new TeacherContext([], [], [], new EducatorQualityScore(false, false, false, false, false, "ok"), "[TEACHER] ok"));

        public Task<TeachingReference?> NormalizeTeachingReferenceAsync(Guid topicId, string? rawYouTubeContext, CancellationToken ct = default) =>
            Task.FromResult<TeachingReference?>(null);

        public Task RecordAnswerQualitySignalsAsync(Guid userId, Guid? topicId, Guid? sessionId, string answer, TeacherContext context, CancellationToken ct = default)
        {
            RecordCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTutorPolicyEngine : ITutorPolicyEngine
    {
        public Task<TutorPolicyContextDto> BuildAsync(Guid userId, Guid? topicId, Guid? sessionId, string userMessage, string notebookContext, string wikiContext, string learningSignalContext, CancellationToken ct = default) =>
            Task.FromResult(new TutorPolicyContextDto
            {
                TopicId = topicId,
                PromptBlock = "[POLICY] ok"
            });
    }

    private sealed class FakeTutorTurnStateAssembler : ITutorTurnStateAssembler
    {
        public Task<TutorTurnStateDto> BuildAsync(Guid userId, Guid? topicId, Guid? sessionId, string userMessage, string conversationContext, string notebookContext, string wikiContext, string learningSignalContext, string ideContext, TutorPolicyContextDto policyContext, CancellationToken ct = default)
        {
            var id = Guid.NewGuid();
            return Task.FromResult(new TutorTurnStateDto
            {
                Id = id,
                UserId = userId,
                TopicId = topicId,
                SessionId = sessionId,
                UserMessage = userMessage,
                ActiveConceptKey = "concept",
                ActiveConceptLabel = "Concept",
                PromptBlock = "[TURN] ok"
            });
        }
    }

    private sealed class FakeTutorActionPlanner : ITutorActionPlanner
    {
        public Task<TutorActionPlanDto> PlanAsync(TutorTurnStateDto turnState, CancellationToken ct = default) =>
            Task.FromResult(new TutorActionPlanDto
            {
                Id = Guid.NewGuid(),
                TutorTurnStateId = turnState.Id,
                UserId = turnState.UserId,
                TopicId = turnState.TopicId,
                SessionId = turnState.SessionId,
                PromptBlock = "[PLAN] ok"
            });
    }

    private sealed class FakeTutorToolOrchestrator : ITutorToolOrchestrator
    {
        public Task<IReadOnlyList<TutorToolCallDto>> RunAsync(TutorActionPlanDto actionPlan, TutorTurnStateDto turnState, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TutorToolCallDto>>([]);
    }

    private sealed class FakeTeachingArtifactService : ITeachingArtifactService
    {
        public Task<IReadOnlyList<TeachingArtifactDto>> BuildArtifactsAsync(TutorActionPlanDto actionPlan, TutorTurnStateDto turnState, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TeachingArtifactDto>>([]);

        public Task MarkRenderedAsync(Guid artifactId, Guid userId, string? renderError = null, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeTutorReflectionService : ITutorReflectionService
    {
        public Task<TutorReflectionUpdateDto> ReflectAsync(TutorTurnStateDto turnState, TutorActionPlanDto actionPlan, string assistantAnswer, IReadOnlyList<TeachingArtifactDto> artifacts, CancellationToken ct = default) =>
            Task.FromResult(new TutorReflectionUpdateDto
            {
                Id = Guid.NewGuid(),
                TutorActionTraceId = actionPlan.Id,
                TutorTurnStateId = turnState.Id,
                PolicyApplied = true,
                MicroCheckAsked = assistantAnswer.Contains("?", StringComparison.Ordinal)
            });
    }

    private class FakeTutorPedagogyEvaluationService : ITutorPedagogyEvaluationService
    {
        private readonly string _status;

        public FakeTutorPedagogyEvaluationService(string status = "ok")
        {
            _status = status;
        }

        public virtual Task<TutorPedagogyEvaluationRunDto> EvaluateAsync(TutorPedagogyEvaluationRequestDto request, CancellationToken ct = default) =>
            Task.FromResult(new TutorPedagogyEvaluationRunDto
            {
                Id = Guid.NewGuid(),
                UserId = request.TurnState.UserId,
                TopicId = request.TurnState.TopicId,
                SessionId = request.TurnState.SessionId,
                TutorTurnStateId = request.TurnState.Id,
                TutorActionTraceId = request.ActionPlan.Id,
                TutorReflectionUpdateId = request.Reflection?.Id,
                Status = _status,
                OverallScore = _status == "ok" ? 0.9m : 0.4m
            });

        public Task<TutorPedagogyEvaluationRunDto?> GetRunAsync(Guid userId, Guid runId, CancellationToken ct = default) =>
            Task.FromResult<TutorPedagogyEvaluationRunDto?>(null);

        public Task<TutorPedagogyTopicSummaryDto> GetTopicSummaryAsync(Guid userId, Guid? topicId, CancellationToken ct = default) =>
            Task.FromResult(new TutorPedagogyTopicSummaryDto());

        public Task<TutorPedagogyEvaluationRunDto?> EvaluateRecentAsync(Guid userId, Guid? topicId, Guid? sessionId = null, CancellationToken ct = default) =>
            Task.FromResult<TutorPedagogyEvaluationRunDto?>(null);
    }

    private sealed class ThrowingTutorPedagogyEvaluationService : FakeTutorPedagogyEvaluationService
    {
        public override Task<TutorPedagogyEvaluationRunDto> EvaluateAsync(TutorPedagogyEvaluationRequestDto request, CancellationToken ct = default) =>
            throw new InvalidOperationException("pedagogy failed");
    }

    private sealed class FakeTutorPedagogyQualityGate : ITutorPedagogyQualityGate
    {
        private readonly bool _requiresRepair;

        public FakeTutorPedagogyQualityGate(bool requiresRepair)
        {
            _requiresRepair = requiresRepair;
        }

        public bool RequiresRepair(TutorPedagogyEvaluationRunDto evaluation) => _requiresRepair;

        public string BuildRepairPrompt(TutorPedagogyEvaluationRunDto evaluation, TutorTurnStateDto turnState, TutorActionPlanDto actionPlan, string assistantAnswer) =>
            "[repair]";
    }

    private sealed class FakeRedisMemoryService : IRedisMemoryService
    {
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
        public Task RecordCacheMetricAsync(string area, bool hit, string? tool = null, double? latencyMs = null) => Task.CompletedTask;
        public Task<IEnumerable<CacheMetricSummary>> GetCacheMetricsAsync() => Task.FromResult<IEnumerable<CacheMetricSummary>>([]);
        public Task<RedisHealthDto> GetRedisHealthAsync() => Task.FromResult(new RedisHealthDto(true, 0d, 1, "fake", null, DateTime.UtcNow));
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
