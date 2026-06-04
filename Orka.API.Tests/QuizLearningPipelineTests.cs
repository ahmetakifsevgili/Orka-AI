using System.Net.Http.Json;
using System.Text.Json;
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
    public async Task ChatQuizCompletion_RecordsObservedAttemptAndCanonicalLessonProgress()
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
        // Chat quiz completion may advance the lesson, but durable correctness stays observed-only
        // until a server-authored assessment item verifies the answer key.
        Assert.False(attempt.IsCorrect);
        Assert.StartsWith("chat:", attempt.QuestionHash);

        using var metadata = JsonDocument.Parse(attempt.SourceRefsJson!);
        Assert.False(metadata.RootElement.GetProperty("correctnessVerified").GetBoolean());
        Assert.True(metadata.RootElement.GetProperty("clientCorrectnessIgnored").GetBoolean());
        Assert.False(await db.LearningSignals.AnyAsync(s =>
            s.UserId == user.UserId &&
            s.SessionId == sessionId &&
            s.QuizAttemptId == attempt.Id &&
            s.SignalType == "QuizAnswered"));
        Assert.True(await db.LearningEvents.AnyAsync(e =>
            e.UserId == user.UserId &&
            e.SessionId == sessionId &&
            e.QuizAttemptId == attempt.Id));
        Assert.True(await db.LearningSignals.AnyAsync(s =>
            s.UserId == user.UserId &&
            s.SessionId == sessionId &&
            s.TopicId == tree.LessonId &&
            s.SignalType == "LessonCompleted"));

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
    public async Task LegacyWrongChatQuizAnswer_RecordsObservedAttemptWithoutStrongLearningSignal()
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
        using var metadata = JsonDocument.Parse(attempt.SourceRefsJson!);
        Assert.False(metadata.RootElement.GetProperty("correctnessVerified").GetBoolean());
        Assert.True(metadata.RootElement.GetProperty("clientCorrectnessIgnored").GetBoolean());
        Assert.False(await db.LearningSignals.AnyAsync(s =>
            s.UserId == user.UserId &&
            s.SessionId == sessionId &&
            s.QuizAttemptId == attempt.Id &&
            s.SignalType == "QuizAnswered"));
        Assert.True(await db.LearningEvents.AnyAsync(e =>
            e.UserId == user.UserId &&
            e.SessionId == sessionId &&
            e.QuizAttemptId == attempt.Id));
    }

    [Fact]
    public async Task WrongQuizAttempt_ReturnsSafeMisconceptionAndRemediationSeed()
    {
        await using var factory = CreateFactory();
        var user = await CoordinationTestHelpers.RegisterAuthenticatedClientAsync(factory, "quiz-misconception");
        var tree = await CoordinationTestHelpers.SeedTopicTreeAsync(factory, user.UserId, "Misconception");
        var (quizRunId, assessmentItemId) = await CoordinationTestHelpers.SeedDurableAssessmentItemAsync(
            factory,
            user.UserId,
            tree.LessonId,
            questionId: "q-pack3",
            question: "Index neden kullanilir?",
            conceptKey: "indexes",
            correctOptionId: "A",
            correctOptionText: "Okuma hizini iyilestirmek icin",
            wrongOptionId: "B",
            wrongOptionText: "Tabloyu silmek icin",
            explanation: "Kavram mantigi karismis; index okuma hizini iyilestirir.");

        var response = await user.Client.PostAsJsonAsync("/api/quiz/attempt", new
        {
            quizRunId,
            topicId = tree.LessonId,
            assessmentItemId,
            questionId = "q-pack3",
            question = "Index neden kullanılır?",
            selectedOptionId = "B) Tabloyu silmek için",
            isCorrect = false,
            explanation = "Kavram mantığı karışmış; index okuma hızını iyileştirir.",
            skillTag = "indexes",
            conceptTag = "Indexes",
            sourceRefsJson = """{"conceptKey":"indexes","conceptTag":"Indexes","mistakeCategory":"Conceptual"}""",
            questionHash = "pack3-misconception"
        });

        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("concept_confusion", body.RootElement.GetProperty("misconceptionSignal").GetProperty("category").GetString());
        Assert.Equal("tutor_explain", body.RootElement.GetProperty("remediationSeed").GetProperty("firstAction").GetString());
        Assert.NotEqual("ignored", body.RootElement.GetProperty("learningSignalConfidence").GetProperty("status").GetString());
        var impact = body.RootElement.GetProperty("learningImpact");
        var remediationLesson = impact.GetProperty("remediationLesson");
        Assert.Equal("misconception_repair", remediationLesson.GetProperty("repairType").GetString());
        Assert.Equal("misconception_signal", remediationLesson.GetProperty("trigger").GetProperty("triggerType").GetString());
        Assert.True(remediationLesson.GetProperty("checkpoint").GetProperty("avoidsPreSubmitReveal").GetBoolean());
        Assert.Equal("do_not_overstate_mastery", remediationLesson.GetProperty("outcome").GetProperty("masteryPolicy").GetString());
        Assert.DoesNotContain("answerKey", remediationLesson.GetRawText(), StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
        var attempt = await db.QuizAttempts.SingleAsync(a => a.UserId == user.UserId && a.QuestionHash == "pack3-misconception");
        Assert.Contains("remediationSeed", attempt.SourceRefsJson);
        Assert.True(await db.LearningSignals.AnyAsync(s =>
            s.UserId == user.UserId &&
            s.QuizAttemptId == attempt.Id &&
            s.PayloadJson != null &&
            s.PayloadJson.Contains("learningSignalConfidence")));
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

        public Task ProcessSynchronouslyAsync(ChatTurnPostProcessRequest request, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopBackgroundTaskQueue : IBackgroundTaskQueue
    {
        public ValueTask QueueAsync(BackgroundTaskItem item, CancellationToken ct = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class AlwaysWrongTutorAgent : ITutorAgent
    {
        public Task<string> GetResponseAsync(Guid userId, string content, Session session, bool isQuizPending, CancellationToken ct = default) =>
            Task.FromResult("Tutor response");

        public async IAsyncEnumerable<string> GetResponseStreamAsync(
            Guid userId,
            string content,
            Session session,
            bool isQuizPending,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return await GetResponseAsync(userId, content, session, isQuizPending, ct);
        }

        public Task<string> GetDeepPlanWelcomeAsync(Guid userId, string content, Session session, IReadOnlyList<string> planTitles, CancellationToken ct = default) =>
            Task.FromResult("Welcome");

        public Task<string> GetOptionsWelcomeAsync(Guid userId, string content, Session session, CancellationToken ct = default) =>
            Task.FromResult("Options");

        public Task<string> GetFirstLessonAsync(string parentTopicTitle, string lessonTitle, IReadOnlyList<string>? curriculumTitles = null, CancellationToken ct = default) =>
            Task.FromResult("First lesson");

        public Task<string> GenerateTopicQuizAsync(string topicTitle, string? researchContext = null, CancellationToken ct = default) =>
            Task.FromResult(PendingQuiz);

        public Task<bool> EvaluateQuizAnswerAsync(string question, string answer, CancellationToken ct = default) =>
            Task.FromResult(false);
    }
}
