using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Orka.Core.Entities;
using Orka.Core.DTOs.Chat;

namespace Orka.Core.Interfaces;

public interface IAgentOrchestrator
{
    Task<Session?> GetOrCreateSessionAsync(Guid userId, Guid? topicId, Guid? sessionId, string content);
    Task<ChatMessageResponse> ProcessMessageAsync(Guid userId, string content, Guid? topicId, Guid? sessionId, bool isPlanMode = false);
    IAsyncEnumerable<string> ProcessMessageStreamAsync(Guid userId, string content, Guid? topicId, Guid? sessionId, bool isPlanMode = false, bool isVoiceMode = false);
    Task EndSessionAsync(Guid sessionId, Guid userId);

    // ─── FAZ 1: Barge-In (Metin Modu Kesintisi) ──────────────────────────────
    /// <summary>
    /// Metin SSE akışını anında keser. userMessage bağlama eklenir (Context Reconstruction).
    /// Race Condition koruması: Yarım kalan LLM cevabı DB'ye kaydedilmez.
    /// </summary>
    Task<bool> InterruptStreamAsync(Guid sessionId, string userMessage);

    // ─── FAZ 2: AgentGroupChat (Otonom Sınıf Simülasıyonu) ─────────────────────
    /// <summary>
    /// Tutor ve Peer ajanlarının otonom podcast/sınıf simülasıyonunu başlatır.
    /// Öğrenci dinleyici olabilir veya araya girebilir (Barge-in).
    /// </summary>
    IAsyncEnumerable<string> StartClassroomSessionAsync(
        Guid userId, Guid sessionId, string topic, bool isVoiceMode, CancellationToken ct = default);

    // ─── FAZ 3: Multimodal ────────────────────────────────────────────────
    /// <summary>
    /// Metin + Görsel (URL) içeren mesajları işler.
    /// ContentItemDto listesi SK'nın TextContent/ImageContent objelerine dönüştürülür.
    /// </summary>
    IAsyncEnumerable<string> ProcessMultimodalMessageStreamAsync(
        Guid userId, List<ContentItemDto> contentItems, Guid? topicId, Guid? sessionId,
        bool isPlanMode = false, CancellationToken ct = default);
}

public interface ITutorAgent
{
    Task<string> GetResponseAsync(Guid userId, string content, Session session, bool isQuizPending, string? goalContext = null);
    IAsyncEnumerable<string> GetResponseStreamAsync(Guid userId, string content, Session session, bool isQuizPending, bool isVoiceMode = false, string? goalContext = null, CancellationToken ct = default);

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
    /// Öğrenci quiz'de başarısız olduğunda zayıf olduğu noktalara odaklanan telafi (remedial) dersi üretir.
    /// </summary>
    Task<string> GetRemedialLessonAsync(string lessonTitle, string weaknesses);

    /// <summary>
    /// Konu başlığına uygun sınav json formatında üretir.
    /// questionCount: ExamPrep için 10-20, hobi için 3-5. Varsayılan 5.
    /// </summary>
    Task<string> GenerateTopicQuizAsync(string topicTitle, Guid userId, Guid topicId, string? goalContext = null, string? researchContext = null, int questionCount = 5, string? weaknessContext = null);

    /// <summary>
    /// Öğrencinin cevabını değerlendirir.
    /// content içinde '[PLAYWRIGHT_PASS_QUIZ]' varsa AI çağrısı yapılmadan DOĞRU döner.
    /// </summary>
    Task<(double score, string feedback)> EvaluateQuizAnswerAsync(string question, string answer, string? goalContext = null);
}

public record AnalyzerResult(bool IsComplete, string Reasoning, IntentResult IntentData);

public interface IAnalyzerAgent
{
    Task<AnalyzerResult> AnalyzeCompletionAsync(IEnumerable<Message> messages);
}

public interface ISummarizerAgent
{
    Task SummarizeAndSaveWikiAsync(Guid sessionId, Guid topicId, Guid userId);
    Task SummarizeModuleAsync(Guid parentTopicId, Guid userId);
}

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

// ─── FAZ 2: Peer Agent (Sınıf Simülasıyonu) ────────────────────────────────

/// <summary>
/// Öğrenci rolünde soru soran Akran Ajanı.
/// TutorAgent'in anlattığı konudan doğal, meraklı sorular üretir.
/// AgentGroupChat içinde TutorAgent ile otonom diyalog kurar.
/// </summary>
public interface IPeerAgent
{
    IAsyncEnumerable<string> GetResponseStreamAsync(
        string tutorMessage, Session session, CancellationToken ct = default);
    Task<string> GetResponseAsync(
        string tutorMessage, Session session, CancellationToken ct = default);
}
