using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orka.Core.Entities;
using Orka.Core.DTOs.Chat;

namespace Orka.Core.Interfaces;

public interface IAgentOrchestrator
{
    Task<ChatMessageResponse> ProcessMessageAsync(Guid userId, string content, Guid? topicId, Guid? sessionId);
    Task EndSessionAsync(Guid sessionId, Guid userId);
}

public interface ITutorAgent
{
    Task<string> GetResponseAsync(Guid userId, string content, Session session, bool isQuizPending);

    /// <summary>
    /// Yeni konu açıldığında Deep Plan başlıklarını kullanıcıya bildiren ilk yanıtı üretir.
    /// </summary>
    Task<string> GetDeepPlanWelcomeAsync(Guid userId, string content, Session session, IReadOnlyList<string> planTitles);
    Task<string> GetOptionsWelcomeAsync(Guid userId, string content, Session session);

    /// <summary>
    /// Deep Plan'ın ilk alt konusunun anlatımını üretir.
    /// Session geçmişine bağlı değildir; doğrudan konu başlığından ders üretir.
    /// </summary>
    Task<string> GetFirstLessonAsync(string parentTopicTitle, string lessonTitle);

    /// <summary>
    /// Konu başlığına uygun, kısa ve net bir sınav sorusu üretir.
    /// </summary>
    Task<string> GenerateQuizQuestionAsync(string topicTitle);

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
