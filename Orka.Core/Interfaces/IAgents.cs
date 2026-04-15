using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orka.Core.Entities;
using Orka.Core.DTOs.Chat;

namespace Orka.Core.Interfaces;

public interface IAgentOrchestrator
{
    Task<Session?> GetOrCreateSessionAsync(Guid userId, Guid? topicId, Guid? sessionId, string content);
    Task<ChatMessageResponse> ProcessMessageAsync(Guid userId, string content, Guid? topicId, Guid? sessionId, bool isPlanMode = false);
    IAsyncEnumerable<string> ProcessMessageStreamAsync(Guid userId, string content, Guid? topicId, Guid? sessionId, bool isPlanMode = false);
    Task EndSessionAsync(Guid sessionId, Guid userId);
}

public interface ITutorAgent
{
    Task<string> GetResponseAsync(Guid userId, string content, Session session, bool isQuizPending);
    IAsyncEnumerable<string> GetResponseStreamAsync(Guid userId, string content, Session session, bool isQuizPending, CancellationToken ct = default);

    /// <summary>
    /// Yeni konu açıldığında Deep Plan başlıklarını kullanıcıya bildiren ilk yanıtı üretir.
    /// </summary>
    Task<string> GetDeepPlanWelcomeAsync(Guid userId, string content, Session session, IReadOnlyList<string> planTitles);
    Task<string> GetOptionsWelcomeAsync(Guid userId, string content, Session session);

    /// <summary>
    /// Deep Plan'ın ilk alt konusunun anlatımını üretir.
    /// Session geçmişine bağlı değildir; doğrudan konu başlığından ders üretir.
    /// curriculumTitles geçilirse AI müfredat dışı başlık üretmez (hallucination guard).
    /// </summary>
    Task<string> GetFirstLessonAsync(string parentTopicTitle, string lessonTitle, IReadOnlyList<string>? curriculumTitles = null);

    /// <summary>
    /// Konu başlığına uygun, kısa ve net bir sınav sorusu üretir.
    /// Albert araştırmasından gelen taze bilgiler (context) varsa sınav daha nitelikli olur.
    /// </summary>
    Task<string> GenerateQuizQuestionAsync(string topicTitle, string? researchContext = null);

    /// <summary>
    /// Öğrencinin cevabını değerlendirir.
    /// content içinde '[PLAYWRIGHT_PASS_QUIZ]' varsa AI çağrısı yapılmadan DOĞRU döner.
    /// </summary>
    Task<bool> EvaluateQuizAnswerAsync(string question, string answer);
}

public interface IAnalyzerAgent
{
    Task<bool> AnalyzeCompletionAsync(IEnumerable<Message> messages);
}

public interface ISummarizerAgent
{
    Task SummarizeAndSaveWikiAsync(Guid sessionId, Guid topicId, Guid userId);
}

public interface IQuizAgent
{
    Task GeneratePendingQuizAsync(Guid sessionId, Guid topicId, Guid userId);
}
