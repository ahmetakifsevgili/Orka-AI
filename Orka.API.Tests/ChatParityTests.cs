using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orka.Core.DTOs.Chat;
using Orka.Core.DTOs.Korteks;
using Orka.Core.Entities;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class ChatParityTests
{
    [Fact]
    public async Task NonStreamChat_SchedulesCanonicalPostProcessing()
    {
        var capture = new CapturingPostProcessor();
        await using var factory = CreateFactory(capture);
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "chat-parity");

        var response = await user.Client.PostAsJsonAsync("/api/chat/message", new
        {
            content = "Merhaba, async await anlat."
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatMessageResponse>();

        var request = Assert.Single(capture.Requests);
        Assert.False(request.IsStream);
        Assert.Equal(user.UserId, request.UserId);
        Assert.Equal(body!.MessageId, request.AssistantMessageId);
        Assert.Equal("TutorAgent", request.AgentRole);
        Assert.False(string.IsNullOrWhiteSpace(request.AssistantContent));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        Assert.True(await db.CostRecords.AnyAsync(c => c.MessageId == body.MessageId && c.UserId == user.UserId));
    }

    [Fact]
    public async Task NonStreamPlanMode_PersistsAssistantCostAndSchedulesPostProcessing()
    {
        var capture = new CapturingPostProcessor();
        await using var factory = CreateFactory(capture, services =>
        {
            services.RemoveAll<IDeepPlanAgent>();
            services.AddSingleton<IDeepPlanAgent, FakeDeepPlanAgent>();
        });
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "chat-plan");

        var response = await user.Client.PostAsJsonAsync("/api/chat/message", new
        {
            content = "C# async icin plan hazirla.",
            isPlanMode = true
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatMessageResponse>();

        Assert.NotNull(body);
        Assert.Equal("quiz", body!.MessageType);

        var request = Assert.Single(capture.Requests);
        Assert.False(request.IsStream);
        Assert.Equal(user.UserId, request.UserId);
        Assert.Equal(body.MessageId, request.AssistantMessageId);
        Assert.Equal("DeepPlanAgent", request.AgentRole);
        Assert.Contains("Seviye Testi", request.AssistantContent);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        Assert.True(await db.Messages.AnyAsync(m =>
            m.Id == body.MessageId &&
            m.UserId == user.UserId &&
            m.Role == "assistant"));
        Assert.True(await db.CostRecords.AnyAsync(c =>
            c.MessageId == body.MessageId &&
            c.UserId == user.UserId));
    }

    [Fact]
    public async Task StreamChat_SchedulesSameCanonicalPostProcessingContract()
    {
        var capture = new CapturingPostProcessor();
        await using var factory = CreateFactory(capture);
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "chat-stream");

        using var response = await user.Client.PostAsJsonAsync("/api/chat/stream", new
        {
            content = "Iterator nedir?"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var streamText = await response.Content.ReadAsStringAsync();
        Assert.Contains("data:", streamText);

        var request = Assert.Single(capture.Requests);
        Assert.True(request.IsStream);
        Assert.Equal(user.UserId, request.UserId);
        Assert.Equal("TutorAgent", request.AgentRole);
        Assert.False(string.IsNullOrWhiteSpace(request.AssistantContent));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        Assert.True(await db.CostRecords.AnyAsync(c => c.MessageId == request.AssistantMessageId && c.UserId == user.UserId));
    }

    [Fact]
    public async Task PostProcessor_IsBestEffortAndIdempotentForEvaluator()
    {
        var evaluator = new ConfigurableEvaluator();
        await using var factory = new ApiSmokeFactory("Development", configureServices: services =>
        {
            services.RemoveAll<IBackgroundTaskQueue>();
            services.AddSingleton<IBackgroundTaskQueue, InlineBackgroundTaskQueue>();
            services.RemoveAll<IEvaluatorAgent>();
            services.AddSingleton<IEvaluatorAgent>(evaluator);
            services.RemoveAll<IAnalyzerAgent>();
            services.AddSingleton<IAnalyzerAgent, IncompleteAnalyzer>();
        });

        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "chat-post");
        var (sessionId, messageId) = await SeedAssistantTurnAsync(factory, user.UserId);

        using var scope = factory.Services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IChatTurnPostProcessor>();
        var request = new ChatTurnPostProcessRequest(
            user.UserId,
            sessionId,
            TopicId: null,
            messageId,
            "question",
            "answer",
            "TutorAgent",
            "test-correlation",
            Orka.Core.Enums.SessionState.Learning,
            IsStream: false);

        await processor.ScheduleAsync(request);
        await processor.ScheduleAsync(request);

        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        Assert.Equal(1, await db.AgentEvaluations.CountAsync(e => e.MessageId == messageId && e.AgentRole == "TutorAgent"));
        Assert.Equal(1, evaluator.CallCount);

        evaluator.Throw = true;
        var secondMessageId = await SeedAssistantMessageAsync(factory, user.UserId, sessionId);
        await processor.ScheduleAsync(request with { AssistantMessageId = secondMessageId, AssistantContent = "second answer" });
        Assert.Equal(0, await db.AgentEvaluations.CountAsync(e => e.MessageId == secondMessageId));
    }

    [Fact]
    public async Task PostProcessorScheduleFailure_DoesNotBreakChatResponse()
    {
        await using var factory = new ApiSmokeFactory("Development", configureServices: services =>
        {
            services.RemoveAll<IChatTurnPostProcessor>();
            services.AddSingleton<IChatTurnPostProcessor, ThrowingPostProcessor>();
        });
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "chat-schedule-fail");

        var response = await user.Client.PostAsJsonAsync("/api/chat/message", new
        {
            content = "Iterator nedir?"
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ChatMessageResponse>();
        Assert.NotNull(body);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        Assert.True(await db.Messages.AnyAsync(m =>
            m.Id == body!.MessageId &&
            m.UserId == user.UserId &&
            m.Role == "assistant"));
        Assert.True(await db.CostRecords.AnyAsync(c =>
            c.MessageId == body!.MessageId &&
            c.UserId == user.UserId));
    }

    private static ApiSmokeFactory CreateFactory(
        CapturingPostProcessor capture,
        Action<IServiceCollection>? configureServices = null) =>
        new("Development", configureServices: services =>
        {
            services.RemoveAll<IChatTurnPostProcessor>();
            services.AddSingleton<IChatTurnPostProcessor>(capture);
            configureServices?.Invoke(services);
        });

    private static async Task<(Guid SessionId, Guid MessageId)> SeedAssistantTurnAsync(ApiSmokeFactory factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var session = new Session
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SessionNumber = 1,
            CreatedAt = DateTime.UtcNow
        };
        db.Sessions.Add(session);
        var messageId = await SeedAssistantMessageAsync(db, userId, session.Id);
        await db.SaveChangesAsync();
        return (session.Id, messageId);
    }

    private static async Task<Guid> SeedAssistantMessageAsync(ApiSmokeFactory factory, Guid userId, Guid sessionId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var id = await SeedAssistantMessageAsync(db, userId, sessionId);
        await db.SaveChangesAsync();
        return id;
    }

    private static Task<Guid> SeedAssistantMessageAsync(OrkaDbContext db, Guid userId, Guid sessionId)
    {
        var messageId = Guid.NewGuid();
        db.Messages.Add(new Message
        {
            Id = messageId,
            UserId = userId,
            SessionId = sessionId,
            Role = "assistant",
            Content = "answer",
            CreatedAt = DateTime.UtcNow
        });
        return Task.FromResult(messageId);
    }

    private sealed class CapturingPostProcessor : IChatTurnPostProcessor
    {
        public ConcurrentQueue<ChatTurnPostProcessRequest> Requests { get; } = new();

        public ValueTask ScheduleAsync(ChatTurnPostProcessRequest request, CancellationToken ct = default)
        {
            Requests.Enqueue(request);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingPostProcessor : IChatTurnPostProcessor
    {
        public ValueTask ScheduleAsync(ChatTurnPostProcessRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("schedule failed");
    }

    private sealed class FakeDeepPlanAgent : IDeepPlanAgent
    {
        public Task<List<Topic>> GenerateAndSaveDeepPlanAsync(
            Guid parentTopicId,
            string topicTitle,
            Guid userId,
            string userLevel = "Bilinmiyor",
            string? researchContext = null,
            string? failedTopics = null) =>
            Task.FromResult(new List<Topic>());

        public Task<DeepPlanGenerationWithGroundingResultDto> GenerateAndSaveDeepPlanWithGroundingAsync(
            Guid parentTopicId,
            string topicTitle,
            Guid userId,
            string userLevel = "Bilinmiyor",
            string? researchContext = null,
            string? failedTopics = null) =>
            Task.FromResult(new DeepPlanGenerationWithGroundingResultDto());

        public Task<DeepPlanGenerationWithGroundingResultDto> GenerateAndSaveDeepPlanFromDiagnosticAsync(
            Guid parentTopicId,
            string topicTitle,
            Guid userId,
            string compressedResearchPromptBlock,
            string diagnosticQuizSummary,
            string userLevel = "Bilinmiyor") =>
            Task.FromResult(new DeepPlanGenerationWithGroundingResultDto());

        public Task<string> GenerateBaselineQuizAsync(string topicTitle) =>
            Task.FromResult("""[{"question":"async ne yapar?","options":[{"text":"bekler","isCorrect":true}],"correctAnswer":"bekler","explanation":"ok"}]""");
    }

    private sealed class InlineBackgroundTaskQueue : IBackgroundTaskQueue
    {
        public async ValueTask QueueAsync(BackgroundTaskItem item, CancellationToken ct = default) =>
            await item.Work(ct);
    }

    private sealed class ConfigurableEvaluator : IEvaluatorAgent
    {
        public int CallCount { get; private set; }
        public bool Throw { get; set; }

        public Task<(int score, string feedback)> EvaluateInteractionAsync(
            Guid sessionId,
            string userMessage,
            string agentResponse,
            string agentRole,
            Guid? topicId = null,
            CancellationToken ct = default)
        {
            CallCount++;
            if (Throw) throw new InvalidOperationException("evaluator failed");
            return Task.FromResult((8, "ok"));
        }
    }

    private sealed class IncompleteAnalyzer : IAnalyzerAgent
    {
        public Task<AnalyzerResult> AnalyzeCompletionAsync(IEnumerable<Message> messages) =>
            Task.FromResult(new AnalyzerResult(
                false,
                "not complete",
                new IntentResult("CONTINUE", 0.9, "continue", 5, "")));
    }
}
