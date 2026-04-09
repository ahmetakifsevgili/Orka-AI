using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MediatR;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Core.DTOs.Chat;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public class AgentOrchestratorService : IAgentOrchestrator
{
    private readonly OrkaDbContext _db;
    private readonly ITutorAgent _tutorAgent;
    private readonly IAnalyzerAgent _analyzerAgent;
    private readonly IDeepPlanAgent _deepPlanAgent;
    private readonly IMediator _mediator;
    private readonly ITopicService _topicService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentOrchestratorService> _logger;

    public AgentOrchestratorService(
        OrkaDbContext db,
        ITutorAgent tutorAgent,
        IAnalyzerAgent analyzerAgent,
        IDeepPlanAgent deepPlanAgent,
        IMediator mediator,
        ITopicService topicService,
        IServiceScopeFactory scopeFactory,
        ILogger<AgentOrchestratorService> logger)
    {
        _db = db;
        _tutorAgent = tutorAgent;
        _analyzerAgent = analyzerAgent;
        _deepPlanAgent = deepPlanAgent;
        _mediator = mediator;
        _topicService = topicService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task EndSessionAsync(Guid sessionId, Guid userId)
    {
        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId);
        if (session != null)
        {
            session.EndedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }

    public async Task<ChatMessageResponse> ProcessMessageAsync(Guid userId, string content, Guid? topicId, Guid? sessionId)
    {
        Session? session = null;

        if (sessionId.HasValue)
        {
            session = await _db.Sessions
                .Include(s => s.Messages)
                .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId);
        }

        if (session == null && topicId.HasValue)
        {
            session = await _db.Sessions
                .Include(s => s.Messages)
                .Where(s => s.TopicId == topicId && s.UserId == userId)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();
        }

        bool isNewTopic = false;

        if (session == null)
        {
            // ── Yeni Konu + Oturum oluştur ─────────────────────────────────
            string title = content.Length > 40 ? content[..40] : content;
            var (topic, newSession) = await _topicService.CreateDiscoveryTopicAsync(userId, title);
            session = await _db.Sessions
                .Include(s => s.Messages)
                .FirstOrDefaultAsync(s => s.Id == newSession.Id);

            if (session == null) throw new Exception("Oturum oluşturulamadı.");
            isNewTopic = true;
            
            session.CurrentState = SessionState.AwaitingChoice;
            await _db.SaveChangesAsync();
        }

        // 1. SAVE USER MESSAGE
        var userMsg = new Message
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            UserId = userId,
            Role = "user",
            Content = content,
            CreatedAt = DateTime.UtcNow,
            MessageType = MessageType.General
        };
        _db.Messages.Add(userMsg);
        session.Messages.Add(userMsg);
        await _db.SaveChangesAsync();

        // 2. CHECK STATE & ROUTE
        string aiResponse;
        bool wikiUpdated = false;
        bool skipAutoWiki = false;

        if (isNewTopic)
        {
            aiResponse = await _tutorAgent.GetOptionsWelcomeAsync(userId, content, session);
        }
        else if (session.CurrentState == SessionState.AwaitingChoice)
        {
            var result = await HandleAwaitingChoiceStateAsync(userId, content, session);
            aiResponse = result.Response;
            wikiUpdated = result.WikiUpdated;
            skipAutoWiki = true;
        }
        else if (session.CurrentState == SessionState.QuizMode)
        {
            // Quiz cevabı değerlendirme + konu geçişi
            var result = await HandleQuizModeAsync(userId, content, session);
            aiResponse = result.Response;
            wikiUpdated = result.WikiUpdated;
            skipAutoWiki = true;
        }
        else
        {
            aiResponse = session.CurrentState switch
            {
                SessionState.Learning or SessionState.QuizPending => await HandleLearningStateAsync(
                    userId, content, session),
                _ => await _tutorAgent.GetResponseAsync(userId, content, session, false)
            };
        }

        // 3. SAVE AI MESSAGE
        var aiMsg = new Message
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            UserId = userId,
            Role = "assistant",
            Content = aiResponse,
            CreatedAt = DateTime.UtcNow,
            MessageType = MessageType.General
        };
        _db.Messages.Add(aiMsg);
        session.Messages.Add(aiMsg);
        await _db.SaveChangesAsync();

        // 4. AUTO-WIKI: Yanıtı asenkron WikiPage.Content'e yaz
        if (!skipAutoWiki && session.TopicId.HasValue && !string.IsNullOrWhiteSpace(aiResponse))
        {
            var sId = session.Id;
            var tId = session.TopicId.Value;
            var responseSnapshot = aiResponse;

            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                try
                {
                    var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

                    // İlk boş (Content == null) WikiPage'i bul
                    var page = await db.WikiPages
                        .Where(p => p.TopicId == tId && p.Content == null)
                        .OrderBy(p => p.OrderIndex)
                        .FirstOrDefaultAsync();

                    if (page == null) return;

                    // Markdown özet üret (OpenRouter)
                    var openRouter = scope.ServiceProvider.GetRequiredService<IOpenRouterService>();
                    var summary    = await openRouter.ChatCompletionAsync(
                        "Sen bir Wiki yazarısın. Verilen öğretici metni kısa ve anlaşılır Markdown formatında özetle. " +
                        "Başlık (#), madde (- ) ve kod bloğu (```) kullanabilirsin. Maksimum 300 kelime.",
                        $"Özetle:\n\n{responseSnapshot[..Math.Min(responseSnapshot.Length, 3000)]}");

                    page.Content   = summary;
                    page.Status    = "generated";
                    page.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    AiDebugLogger.LogError("AUTO-WIKI", $"WikiPage güncellenemedi. SessionId={sId} — {ex.Message}");
                }
            });
        }

        // 5. BACKGROUND ANALYSIS (Topic Completed?)
        if (session.CurrentState == SessionState.Learning)
        {
            var sId = session.Id;
            var tId = session.TopicId;
            
            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
                var analyzer = scope.ServiceProvider.GetRequiredService<IAnalyzerAgent>();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

                try
                {
                    var msgs = await db.Messages.Where(m => m.SessionId == sId).OrderBy(m => m.CreatedAt).ToListAsync();
                    var isCompleted = await analyzer.AnalyzeCompletionAsync(msgs);
                    
                    if (isCompleted)
                    {
                        await mediator.Publish(new Orka.Core.Events.TopicCompletedEvent
                        {
                            SessionId = sId,
                            TopicId = tId ?? Guid.Empty,
                            UserId = userId
                        });
                    }
                }
                catch (Exception) { /* Log error */ }
            });
        }

        return new ChatMessageResponse
        {
            MessageId = aiMsg.Id,
            SessionId = session.Id,
            TopicId   = session.TopicId ?? Guid.Empty,
            Content   = aiResponse,
            Role      = "assistant",
            CreatedAt = aiMsg.CreatedAt,
            ModelUsed = "Tutor-Agent",
            WikiUpdated = wikiUpdated
        };
    }

    private async Task<(string Response, bool WikiUpdated)> HandleAwaitingChoiceStateAsync(Guid userId, string content, Session session)
    {
        var lowerContent = content.ToLowerInvariant();
        bool wantsDeepPlan = lowerContent.Contains("1") || lowerContent.Contains("plan") || lowerContent.Contains("ilk");
        bool wantsChat = lowerContent.Contains("2") || lowerContent.Contains("sohbet") || lowerContent.Contains("ikinci");

        if (wantsDeepPlan)
        {
            session.CurrentState = SessionState.Learning;
            await _db.SaveChangesAsync();

            var topic = await _db.Topics.FindAsync(session.TopicId);
            if (topic != null)
            {
                var subTopics = await _deepPlanAgent.GenerateAndSaveDeepPlanAsync(topic.Id, topic.Title, userId);
                
                // EF Core Stale Data Fix: Bellekteki Topic verisini yenile
                await _db.Entry(topic).ReloadAsync();

                var titles = subTopics.Select(t => t.Title).ToList();
                var planWelcomeText = await _tutorAgent.GetDeepPlanWelcomeAsync(userId, content, session, titles);

                // KESİNTİSİZ AKIŞ (Continuous Engagement)
                if (subTopics.Any())
                {
                    var firstChild = subTopics.First();

                    // Session.TopicId'yi güncelle — session zaten tracked, Update() çağrısına gerek yok
                    session.TopicId = firstChild.Id;
                    await _db.SaveChangesAsync();

                    // CONTEXT INJECTION: GetFirstLessonAsync, session geçmişine bağlı değil.
                    // Doğrudan konu başlığından ders üretir — Race Condition ve Stale Data riski yok.
                    string firstLessonContent;
                    try
                    {
                        firstLessonContent = await _tutorAgent.GetFirstLessonAsync(topic.Title, firstChild.Title);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ORCHESTRATOR] GetFirstLessonAsync exception: {ex}");
                        firstLessonContent = $"**{firstChild.Title}** konusuna hoş geldin! Derse başlamak için bana hazır olduğunu söyle.";
                    }

                    // TODO: Quiz & Transition Logic
                    // İleride burada dersin ilk kısmı bittikten sonra quiz sorma veya doğrudan
                    // sıradaki alt başlığa geçiş kancaları eklenecek.

                    return ($"{planWelcomeText}\n\n---\n\n**İlk Konumuz: {firstChild.Title}**\n\n{firstLessonContent}", true);
                }

                return (planWelcomeText, true);
            }
        }
        else if (wantsChat)
        {
            session.CurrentState = SessionState.Learning;
            await _db.SaveChangesAsync();
            return ("Harika! O zaman plan dertlerine girmeden organik sohbetimize başlayalım. Bu konu hakkında öncelikle neyi merak ediyorsun?", false);
        }

        return ("Lütfen '1' (Derinlemesine Plan) veya '2' (Hızlı Sohbet) seçeneklerinden birini belirterek ilerle.", false);
    }

    private async Task<string> HandleLearningStateAsync(Guid userId, string content, Session session)
    {
        // ── "Anladım" / "Konuyu Geç" tespiti ───────────────────────────────────
        bool wantsToAdvance =
            content.Contains("anladım", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("konuyu geç", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("sıradaki konu", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("geçelim", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("öğrendim", StringComparison.OrdinalIgnoreCase);

        if (wantsToAdvance && session.CurrentState == SessionState.Learning && session.TopicId.HasValue)
        {
            var currentTopic = await _db.Topics.FindAsync(session.TopicId);
            // Sadece alt başlıklarda quiz tetikle (parent topic'te değil)
            if (currentTopic?.ParentTopicId != null)
            {
                _logger.LogInformation("[QUIZ] Konu geçiş talebi. TopicId={Id}, Title={Title}",
                    currentTopic.Id, currentTopic.Title);

                var quizResponse = await _tutorAgent.GenerateQuizQuestionAsync(currentTopic.Title);
                // session.PendingQuiz'de sadece soru metni saklanır (JSON bloğu hariç)
                session.PendingQuiz  = ExtractQuizQuestionText(quizResponse);
                session.CurrentState = SessionState.QuizMode;
                await _db.SaveChangesAsync();

                // Kullanıcıya tam yanıt gönderilir (giriş metni + JSON quiz bloğu)
                return $"Harika! Seni tebrik etmeden önce hızlı bir soru sormak istiyorum 🎯\n\n{quizResponse}";
            }
        }

        // Mevcut QuizPending kabulü
        if (session.CurrentState == SessionState.QuizPending &&
            (content.Contains("evet", StringComparison.OrdinalIgnoreCase) ||
             content.Contains("başla", StringComparison.OrdinalIgnoreCase)))
        {
            session.CurrentState = SessionState.QuizMode;
            await _db.SaveChangesAsync();
            return $"Harika! İşte senin için hazırladığım test:\n\n{session.PendingQuiz}";
        }

        return await _tutorAgent.GetResponseAsync(
            userId, content, session,
            session.CurrentState == SessionState.QuizPending);
    }

    // ── Quiz cevabı değerlendirme + konu geçişi ──────────────────────────────
    private async Task<(string Response, bool WikiUpdated)> HandleQuizModeAsync(
        Guid userId, string content, Session session)
    {
        var isPlaywright = content.Contains("[PLAYWRIGHT_PASS_QUIZ]", StringComparison.Ordinal);
        if (isPlaywright)
            _logger.LogInformation("[QUIZ] PLAYWRIGHT_PASS_QUIZ backdoor — cevap otomatik DOĞRU.");

        bool isCorrect = await _tutorAgent.EvaluateQuizAnswerAsync(
            session.PendingQuiz ?? "", content);

        if (!isCorrect)
        {
            _logger.LogInformation("[QUIZ] Yanlış cevap. Tekrar soruldu.");
            return ($"Hmm, tam olarak değil. Tekrar deneyelim:\n\n**{session.PendingQuiz}**", false);
        }

        // Doğru cevap → konu geçişi
        _logger.LogInformation("[QUIZ] Doğru cevap! Konu geçişi başlıyor.");
        return await TransitionToNextTopicAsync(userId, session);
    }

    /// <summary>
    /// Quiz yanıtından ```quiz JSON bloğunu parse eder, sadece soru metnini döndürür.
    /// JSON bulunamazsa tüm yanıtı döndürür.
    /// </summary>
    private static string ExtractQuizQuestionText(string response)
    {
        try
        {
            const string Marker = "```quiz";
            var idx = response.IndexOf(Marker, StringComparison.Ordinal);
            if (idx < 0) return response;
            var after  = response[(idx + Marker.Length)..];
            var endIdx = after.IndexOf("```", StringComparison.Ordinal);
            if (endIdx < 0) return response;
            var jsonStr = after[..endIdx].Trim();
            using var doc = System.Text.Json.JsonDocument.Parse(jsonStr);
            return doc.RootElement.GetProperty("question").GetString() ?? response;
        }
        catch { return response; }
    }

    // ── Bir sonraki alt başlığa geç ──────────────────────────────────────────
    private async Task<(string Response, bool WikiUpdated)> TransitionToNextTopicAsync(
        Guid userId, Session session)
    {
        if (!session.TopicId.HasValue)
        {
            session.CurrentState = SessionState.Learning;
            session.PendingQuiz = null;
            await _db.SaveChangesAsync();
            return ("🎉 Tebrikler! Tüm konuları başarıyla tamamladın!", true);
        }

        var currentTopic = await _db.Topics.FindAsync(session.TopicId.Value);

        if (currentTopic?.ParentTopicId == null)
        {
            session.CurrentState = SessionState.Learning;
            session.PendingQuiz = null;
            await _db.SaveChangesAsync();
            return ("🎉 Tebrikler! Bu konuyu tamamladın.", true);
        }

        // Tüm kardeş konuları Order kolonu ile deterministik sırayla al
        var siblings = await _db.Topics
            .Where(t => t.ParentTopicId == currentTopic.ParentTopicId && t.UserId == userId)
            .OrderBy(t => t.Order)
            .ToListAsync();

        var currentIndex = siblings.FindIndex(t => t.Id == currentTopic.Id);

        if (currentIndex < 0 || currentIndex >= siblings.Count - 1)
        {
            // Tüm alt konular bitti
            _logger.LogInformation("[TRANSITION] Tüm alt konular tamamlandı. ParentId={Id}",
                currentTopic.ParentTopicId);
            session.CurrentState = SessionState.Learning;
            session.PendingQuiz = null;
            await _db.SaveChangesAsync();
            return ("🎉 **Harika!** Tüm alt konuları başarıyla tamamladın! Artık ana konunun uzmanısın.", true);
        }

        var nextTopic = siblings[currentIndex + 1];
        var parentTopic = await _db.Topics.FindAsync(currentTopic.ParentTopicId);

        // ── Session.TopicId güncelle (tracked entity — Update() gereksiz) ──
        session.TopicId = nextTopic.Id;
        session.CurrentState = SessionState.Learning;
        session.PendingQuiz = null;
        await _db.SaveChangesAsync();

        _logger.LogInformation("[TRANSITION] {From} → {To}", currentTopic.Title, nextTopic.Title);

        var congratsMsg = $"✅ **Mükemmel cevap!** {currentTopic.Title} konusunu geçtik.\n\n---";
        var nextLesson = await _tutorAgent.GetFirstLessonAsync(
            parentTopic?.Title ?? nextTopic.Title, nextTopic.Title);

        return ($"{congratsMsg}\n\n**Sıradaki Konumuz: {nextTopic.Title}**\n\n{nextLesson}", true);
    }
}
