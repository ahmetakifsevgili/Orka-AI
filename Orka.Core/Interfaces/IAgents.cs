using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orka.Core.Entities;
using Orka.Core.DTOs.Chat;

namespace Orka.Core.Interfaces;

public interface IAgentOrchestrator
{
    Task<Session?> GetOrCreateSessionAsync(Guid userId, Guid? topicId, Guid? sessionId, string content);
    Task<ChatMessageResponse> ProcessMessageAsync(Guid userId, string content, Guid? topicId, Guid? sessionId, bool isPlanMode = false, Guid? focusTopicId = null, string? focusTopicPath = null, string? focusSourceRef = null);
    IAsyncEnumerable<string> ProcessMessageStreamAsync(Guid userId, string content, Guid? topicId, Guid? sessionId, bool isPlanMode = false, Guid? focusTopicId = null, string? focusTopicPath = null, string? focusSourceRef = null);
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
    /// Konu başlığına uygun, 5 soruluk bir sınav json formatında üretir.
    /// Albert araştırmasından gelen taze bilgiler (context) varsa sınav daha nitelikli olur.
    /// </summary>
    Task<string> GenerateTopicQuizAsync(string topicTitle, string? researchContext = null);

    /// <summary>
    /// Öğrencinin cevabını değerlendirir.
    /// content içinde '[PLAYWRIGHT_PASS_QUIZ]' varsa AI çağrısı yapılmadan DOĞRU döner.
    /// </summary>
    Task<bool> EvaluateQuizAnswerAsync(string question, string answer);
}

public record AnalyzerResult(bool IsComplete, string Reasoning, IntentResult IntentData);

public interface IAnalyzerAgent
{
    Task<AnalyzerResult> AnalyzeCompletionAsync(IEnumerable<Message> messages);
}

public interface ISummarizerAgent
{
    Task SummarizeAndSaveWikiAsync(Guid sessionId, Guid topicId, Guid userId);

    /// <summary>
    /// NotebookLM-tarzı "Briefing Document" — wiki içeriğinden 5-7 maddelik
    /// kısa özet + ana fikirler + öneri çıkarır. Cache'lenir (1 saat).
    /// Kullanılan kaynak: tüm wiki blockları + Korteks raporu (varsa).
    /// </summary>
    Task<BriefingDocument> GenerateBriefingAsync(Guid topicId, Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<Orka.Core.DTOs.GlossaryItemDto>> GenerateGlossaryAsync(Guid topicId, Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<Orka.Core.DTOs.TimelineItemDto>> GenerateTimelineAsync(Guid topicId, Guid userId, CancellationToken ct = default);

    Task<Orka.Core.DTOs.MindMapDto> GenerateMindMapAsync(Guid topicId, Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<Orka.Core.DTOs.StudyCardDto>> GenerateStudyCardsAsync(Guid topicId, Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<Orka.Core.DTOs.StudyRecommendationDto>> GenerateRecommendationsAsync(Guid topicId, Guid userId, CancellationToken ct = default);

    void InvalidateNotebookTools(Guid topicId);
}

/// <summary>
/// NotebookLM-tarzı kısa "okumadan önce göz at" kartı.
/// </summary>
public record BriefingDocument(
    string TopicTitle,
    string TLDR,
    IReadOnlyList<string> KeyTakeaways,
    IReadOnlyList<string> SuggestedQuestions,
    DateTime GeneratedAt);

public interface IQuizAgent
{
    Task GeneratePendingQuizAsync(Guid sessionId, Guid topicId, Guid userId);
}

/// <summary>
/// Kullanıcının son N mesajını okuyarak niyet kategorisi ve güvenilirlik skoru üretir.
/// Bu çıktı hem AnalyzerAgent (IsComplete kararı) hem SupervisorAgent (Route kararı) tarafından kullanılır.
/// Tek LLM çağrısıyla iki ayrı ajan için karar üretilmesi maliyeti düşürür.
/// </summary>
public record IntentResult(
    string   Intent,     // UNDERSTOOD | CONFUSED | CHANGE_TOPIC | QUIZ_REQUEST | CONTINUE
    double   Confidence, // 0.0 - 1.0
    string   Reasoning,  // Karar gerekçesi
    int      UnderstandingScore, // 1-10
    string   Weaknesses  // Zayıf yönler
);

public interface IIntentClassifierAgent
{
    /// <summary>
    /// Son 6 mesajı analiz edip kullanıcının niyetini sınıflandırır.
    /// Confidence 0.65 altındaysa sistem kararı belirsiz kabul eder.
    /// </summary>
    Task<IntentResult> ClassifyAsync(IEnumerable<Message> recentMessages, CancellationToken ct = default);
}
