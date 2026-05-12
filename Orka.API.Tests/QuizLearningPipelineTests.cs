using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Xunit;

namespace Orka.API.Tests;

public sealed class QuizLearningPipelineTests
{
    [Fact]
    public async Task ChatQuizCompletion_RecordsDurableAttemptAndCanonicalLearningState()
    {
        await using var factory = CreateFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "quiz-pipe");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "QuizPipe");
        var secondLessonId = await CoordinationTestHelpers.SeedTopicAsync(
            factory,
            user.UserId,
            "QuizPipe Lesson 2",
            tree.ModuleId,
            order: 1);
        var sessionId = await SeedQuizSessionAsync(factory, user.UserId, tree.RootId, PendingQuiz);

        var response = await user.Client.PostAsJsonAsync("/api/chat/message", new
        {
            sessionId,
            topicId = tree.RootId,
            content = "**Quiz Cevabim:** 1/1 Dogru"
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<Orka.Core.DTOs.Chat.ChatMessageResponse>();
        Assert.Contains("Sıradaki Konumuz", body!.Content);
        Assert.Contains("QuizPipe Lesson 2", body.Content);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var attempt = await db.QuizAttempts.SingleAsync(a =>
            a.UserId == user.UserId &&
            a.SessionId == sessionId &&
            a.TopicId == tree.LessonId);
        Assert.True(attempt.IsCorrect);
        Assert.StartsWith("chat:", attempt.QuestionHash);

        Assert.True(await db.LearningSignals.AnyAsync(s =>
            s.UserId == user.UserId &&
            s.SessionId == sessionId &&
            s.QuizAttemptId == attempt.Id &&
            s.SignalType == "QuizAnswered"));
        Assert.True(await db.LearningEvents.AnyAsync(e =>
            e.UserId == user.UserId &&
            e.SessionId == sessionId &&
            e.QuizAttemptId == attempt.Id));
        Assert.True(await db.SkillMasteries.AnyAsync(m =>
            m.UserId == user.UserId &&
            m.TopicId == tree.LessonId &&
            m.SubTopicTitle == "QuizPipe Lesson"));

        var lesson1 = await db.Topics.SingleAsync(t => t.Id == tree.LessonId);
        var lesson2 = await db.Topics.SingleAsync(t => t.Id == secondLessonId);
        var module = await db.Topics.SingleAsync(t => t.Id == tree.ModuleId);
        var root = await db.Topics.SingleAsync(t => t.Id == tree.RootId);

        Assert.Equal(100, lesson1.ProgressPercentage);
        Assert.True(lesson1.IsMastered);
        Assert.Equal(0, lesson2.ProgressPercentage);
        Assert.Equal(2, module.TotalSections);
        Assert.Equal(1, module.CompletedSections);
        Assert.InRange(module.ProgressPercentage, 49.9, 50.1);
        Assert.Equal(1, root.TotalSections);
        Assert.Equal(0, root.CompletedSections);
    }

    [Fact]
    public async Task ChatQuizCompletion_IsIdempotentForSameSessionTopicQuestionAndAnswer()
    {
        await using var factory = CreateFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "quiz-dedupe");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Dedupe");
        var secondLessonId = await CoordinationTestHelpers.SeedTopicAsync(
            factory,
            user.UserId,
            "Dedupe Lesson 2",
            tree.ModuleId,
            order: 1);
        var sessionId = await SeedQuizSessionAsync(factory, user.UserId, tree.RootId, PendingQuiz);

        await SubmitQuizAnswerAsync(user, sessionId, tree.RootId);
        await ResetQuizStateAsync(factory, user.UserId, sessionId, PendingQuiz);
        var duplicateResponse = await SubmitQuizAnswerAsync(user, sessionId, tree.RootId);
        var duplicateBody = await duplicateResponse.Content.ReadFromJsonAsync<Orka.Core.DTOs.Chat.ChatMessageResponse>();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        Assert.Equal(1, await db.QuizAttempts.CountAsync(a =>
            a.UserId == user.UserId &&
            a.SessionId == sessionId &&
            a.TopicId == tree.LessonId));
        Assert.Contains("zaten kaydedilmiş", duplicateBody!.Content);

        var lesson1 = await db.Topics.SingleAsync(t => t.Id == tree.LessonId);
        var lesson2 = await db.Topics.SingleAsync(t => t.Id == secondLessonId);
        var module = await db.Topics.SingleAsync(t => t.Id == tree.ModuleId);
        Assert.Equal(100, lesson1.ProgressPercentage);
        Assert.Equal(0, lesson2.ProgressPercentage);
        Assert.Equal(1, module.CompletedSections);
    }

    [Fact]
    public async Task LegacyWrongChatQuizAnswer_RecordsDurableAttemptAndLearningSignal()
    {
        await using var factory = CreateFactory(services =>
        {
            services.RemoveAll<ITutorAgent>();
            services.AddSingleton<ITutorAgent, AlwaysWrongTutorAgent>();
        });
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "quiz-wrong");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Wrong");
        var sessionId = await SeedQuizSessionAsync(factory, user.UserId, tree.LessonId, PendingQuiz);

        var response = await user.Client.PostAsJsonAsync("/api/chat/message", new
        {
            sessionId,
            topicId = tree.LessonId,
            content = "Yanlis cevap"
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<Orka.Core.DTOs.Chat.ChatMessageResponse>();
        Assert.Contains("Tekrar deneyelim", body!.Content);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var attempt = await db.QuizAttempts.SingleAsync(a =>
            a.UserId == user.UserId &&
            a.SessionId == sessionId &&
            a.TopicId == tree.LessonId);
        Assert.False(attempt.IsCorrect);
        Assert.True(await db.LearningSignals.AnyAsync(s =>
            s.UserId == user.UserId &&
            s.SessionId == sessionId &&
            s.QuizAttemptId == attempt.Id &&
            s.SignalType == "QuizAnswered"));
        Assert.True(await db.LearningEvents.AnyAsync(e =>
            e.UserId == user.UserId &&
            e.SessionId == sessionId &&
            e.QuizAttemptId == attempt.Id));
    }

    private const string PendingQuiz = """
        [{"question":"await ne yapar?","options":["Bekler","Sil"],"answer":"Bekler"}]
        """;

    private static ApiSmokeFactory CreateFactory(Action<IServiceCollection>? configureServices = null) =>
        new("Development", configureServices: services =>
        {
            services.RemoveAll<IChatTurnPostProcessor>();
            services.AddSingleton<IChatTurnPostProcessor, NoopPostProcessor>();
            services.RemoveAll<IBackgroundTaskQueue>();
            services.AddSingleton<IBackgroundTaskQueue, NoopBackgroundTaskQueue>();
            configureServices?.Invoke(services);
        });

    private static async Task<Guid> SeedQuizSessionAsync(
        ApiSmokeFactory factory,
        Guid userId,
        Guid topicId,
        string pendingQuiz)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var session = new Session
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = topicId,
            SessionNumber = 1,
            CurrentState = SessionState.QuizMode,
            PendingQuiz = pendingQuiz,
            CreatedAt = DateTime.UtcNow
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    private static async Task<HttpResponseMessage> SubmitQuizAnswerAsync(CoordinationTestUser user, Guid sessionId, Guid topicId)
    {
        var response = await user.Client.PostAsJsonAsync("/api/chat/message", new
        {
            sessionId,
            topicId,
            content = "**Quiz Cevabim:** 1/1 Dogru"
        });
        response.EnsureSuccessStatusCode();
        return response;
    }

    private static async Task ResetQuizStateAsync(
        ApiSmokeFactory factory,
        Guid userId,
        Guid sessionId,
        string pendingQuiz)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var session = await db.Sessions.SingleAsync(s => s.Id == sessionId && s.UserId == userId);
        session.CurrentState = SessionState.QuizMode;
        session.PendingQuiz = pendingQuiz;
        await db.SaveChangesAsync();
    }

    private sealed class NoopPostProcessor : IChatTurnPostProcessor
    {
        public ValueTask ScheduleAsync(ChatTurnPostProcessRequest request, CancellationToken ct = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class NoopBackgroundTaskQueue : IBackgroundTaskQueue
    {
        public ValueTask QueueAsync(BackgroundTaskItem item, CancellationToken ct = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class AlwaysWrongTutorAgent : ITutorAgent
    {
        public Task<string> GetResponseAsync(Guid userId, string content, Session session, bool isQuizPending) =>
            Task.FromResult("Tutor response");

        public async IAsyncEnumerable<string> GetResponseStreamAsync(
            Guid userId,
            string content,
            Session session,
            bool isQuizPending,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return await GetResponseAsync(userId, content, session, isQuizPending);
        }

        public Task<string> GetDeepPlanWelcomeAsync(Guid userId, string content, Session session, IReadOnlyList<string> planTitles) =>
            Task.FromResult("Welcome");

        public Task<string> GetOptionsWelcomeAsync(Guid userId, string content, Session session) =>
            Task.FromResult("Options");

        public Task<string> GetFirstLessonAsync(string parentTopicTitle, string lessonTitle, IReadOnlyList<string>? curriculumTitles = null) =>
            Task.FromResult("First lesson");

        public Task<string> GenerateTopicQuizAsync(string topicTitle, string? researchContext = null) =>
            Task.FromResult(PendingQuiz);

        public Task<bool> EvaluateQuizAnswerAsync(string question, string answer) =>
            Task.FromResult(false);
    }
}
