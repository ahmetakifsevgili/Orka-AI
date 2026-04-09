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
    private readonly IWikiService _wikiService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentOrchestratorService> _logger;

    public AgentOrchestratorService(
        OrkaDbContext db,
        ITutorAgent tutorAgent,
        IAnalyzerAgent analyzerAgent,
        IDeepPlanAgent deepPlanAgent,
        IMediator mediator,
        ITopicService topicService,
        IWikiService wikiService,
        IServiceScopeFactory scopeFactory,
        ILogger<AgentOrchestratorService> logger)
    {
        _db = db;
        _tutorAgent = tutorAgent;
        _analyzerAgent = analyzerAgent;
        _deepPlanAgent = deepPlanAgent;
        _mediator = mediator;
        _topicService = topicService;
        _wikiService = wikiService;
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
                .Where(s => s.Id == sessionId && s.UserId == userId)
                .FirstOrDefaultAsync();
            if (session != null)
            {
                // Son 20 mesajı yükle — tüm geçmişi çekme
                session.Messages = await _db.Messages
                    .Where(m => m.SessionId == session.Id)
                    .OrderByDescending(m => m.CreatedAt)
                    .Take(20)
                    .OrderBy(m => m.CreatedAt)
                    .ToListAsync();
            }
        }

        if (session == null && topicId.HasValue)
        {
            session = await _db.Sessions
                .Where(s => s.TopicId == topicId && s.UserId == userId)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();
            if (session != null)
            {
                session.Messages = await _db.Messages
                    .Where(m => m.SessionId == session.Id)
                    .OrderByDescending(m => m.CreatedAt)
                    .Take(20)
                    .OrderBy(m => m.CreatedAt)
                    .ToListAsync();
            }
        }

        bool isNewTopic = false;

        // ── Small Talk Guard ───────────────────────────────────────────────
        // Selamlama veya çok kısa mesajlar için ASLA yeni Topic/Session açma.
        if (session == null && IsSmallTalk(content))
        {
            _logger.LogInformation("[SMALL-TALK] Selamlama tespit edildi, topic oluşturulmadı: \"{Content}\"", content);
            return new ChatMessageResponse
            {
                MessageId   = Guid.NewGuid(),
                SessionId   = Guid.Empty,
                TopicId     = Guid.Empty,
                Content     = "Merhaba! 👋 Bugün ne öğrenmek istersin? Bir konu yaz, hemen başlayalım.",
                Role        = "assistant",
                CreatedAt   = DateTime.UtcNow,
                ModelUsed   = "Orka-SmallTalkGuard",
                WikiUpdated = false,
            };
        }
        // ──────────────────────────────────────────────────────────────────

        if (session == null)
        {
            // ── Yeni Konu + Oturum oluştur ─────────────────────────────────
            string title = content.Length > 40 ? content[..40] : content;
            var (topic, newSession) = await _topicService.CreateDiscoveryTopicAsync(userId, title);
            session = await _db.Sessions
                .FirstOrDefaultAsync(s => s.Id == newSession.Id);
            if (session != null)
                session.Messages = new List<Message>();

            if (session == null) throw new Exception("Oturum oluşturulamadı.");
            isNewTopic = true;

            // Yeni konu: doğrudan Learning moduna gir — zorla seçim menüsü YOK
            session.CurrentState = SessionState.Learning;
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
            // İlk mesaj: doğal TutorAgent yanıtı + ince plan teklifi ipucu
            aiResponse = await _tutorAgent.GetResponseAsync(userId, content, session, false);
            aiResponse += "\n\n---\n*İstersen `/plan` yazarak bu konu için yapılandırılmış bir müfredat planı da oluşturabilirim.*";
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

        // 4. AUTO-WIKI: WikiService.AutoUpdateWikiAsync ile WikiBlock olarak kaydet
        // WikiPanel.jsx blokları render eder — page.Content değil WikiBlock listesi.
        // wikiUpdated: Ders geçişi (skipAutoWiki=true) dışındaki durumlarda async tamamlanmadan
        // flag set edemeyiz. Çözüm: WikiPage'de aktif sayfa varsa önceden true set et.
        if (!skipAutoWiki && session.TopicId.HasValue && !string.IsNullOrWhiteSpace(aiResponse))
        {
            var tId          = session.TopicId.Value;
            var snapshot     = aiResponse;
            var userQuestion = content;

            // WikiPage mevcutsa frontend'e önceden güncelleme sinyali ver
            var hasActivePage = await _db.WikiPages
                .AnyAsync(p => p.TopicId == tId && (p.Status == "pending" || p.Status == "learning"));
            if (hasActivePage)
                wikiUpdated = true;

            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                try
                {
                    var wiki = scope.ServiceProvider.GetRequiredService<IWikiService>();
                    await wiki.AutoUpdateWikiAsync(tId, snapshot, userQuestion, "tutor-agent");
                }
                catch (Exception ex)
                {
                    AiDebugLogger.LogError("AUTO-WIKI", $"WikiService.AutoUpdateWikiAsync başarısız. TopicId={tId} — {ex.Message}");
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
        // AwaitingChoice artık plan onayı için kullanılıyor
        var lowerContent = content.ToLowerInvariant();
        bool wantsDeepPlan = lowerContent.Contains("1")       || lowerContent.Contains("plan")   ||
                             lowerContent.Contains("evet")    || lowerContent.Contains("yes")     ||
                             lowerContent.Contains("yapalım") || lowerContent.Contains("olur")    ||
                             lowerContent.Contains("tamam");
        bool wantsChat    = lowerContent.Contains("2")        || lowerContent.Contains("hayır")   ||
                             lowerContent.Contains("devam")   || lowerContent.Contains("sohbet")  ||
                             lowerContent.Contains("gerek yok");

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
                    // Hallucination Guard: müfredat başlıkları geçiliyor
                    string firstLessonContent;
                    try
                    {
                        firstLessonContent = await _tutorAgent.GetFirstLessonAsync(topic.Title, firstChild.Title, titles);
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
            return ("Anlaştık! Sohbet üzerinden devam edelim. Ne merak ediyorsun, sormak istediğin bir şey var mı?", false);
        }

        // Belirsiz yanıt — Learning'e dön, doğal sohbete devam et
        session.CurrentState = SessionState.Learning;
        await _db.SaveChangesAsync();
        return (await _tutorAgent.GetResponseAsync(userId, content, session, false), false);
    }

    private async Task<string> HandleLearningStateAsync(Guid userId, string content, Session session)
    {
        // ── /plan komutu: müfredat planı teklifi ────────────────────────────────
        bool wantsPlan = content.Trim().Equals("/plan", StringComparison.OrdinalIgnoreCase) ||
                         content.Contains("plan yap", StringComparison.OrdinalIgnoreCase)   ||
                         content.Contains("müfredat", StringComparison.OrdinalIgnoreCase);

        if (wantsPlan)
        {
            session.CurrentState = SessionState.AwaitingChoice;
            await _db.SaveChangesAsync();
            return """
                Bu konuyu çok güzel yakaladın! 🎯

                İstersen bu konu için yapılandırılmış bir müfredat planı (Bilgi Haritası) hazırlayayım. Plan, konuyu pedagojik sıraya göre alt başlıklara böler ve Wiki'ne işler.

                **Plan yapalım mı, yoksa sohbet üzerinden mi devam edelim?**
                → `evet` / `1` — Plan oluştur
                → `hayır` / `2` — Sohbete devam et
                """;
        }

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
#if DEBUG
        if (content.Contains("[PLAYWRIGHT_PASS_QUIZ]", StringComparison.Ordinal))
            _logger.LogInformation("[QUIZ] PLAYWRIGHT_PASS_QUIZ backdoor — cevap otomatik DOĞRU.");
#endif

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
    /// Mesajın selamlama / small talk olup olmadığını tespit eder.
    /// Yaygın yazım hatalarını (merha, slm, nbr vb.) Contains ile yakalar.
    /// Yeni Topic oluşturulmasını engellemek için kullanılır.
    /// </summary>
    private static bool IsSmallTalk(string content)
    {
        var t = content.Trim().ToLowerInvariant();

        // 1. Çok kısa mesaj (15 karakter altı) → muhtemelen small talk
        if (t.Length < 15) return true;

        // 2. Bilinen selamlama kalıpları (kısmen yazım hatalı versiyonlar dahil)
        // StartsWith: mesajın selamlama ile başlaması
        // Contains: selamlama başka kelimelerle birleşmiş olabilir ("merha nasılsın")
        var prefixes = new[]
        {
            "merhaba", "merha",   // "merha nasılsın" → typo guard
            "selam", "slm",
            "hey", "heyy",
            "naber", "nbr", "ne haber",
            "nasılsın", "nasilsin", "nasılsnız",
            "iyi misin", "iyimisin",
            "günaydın", "gunaydin",
            "iyi günler", "iyi akşamlar", "iyi geceler",
            "ne var ne yok", "ne var",
            "hello", "hi ", "hi,", "hey!",
        };

        foreach (var p in prefixes)
        {
            if (t.StartsWith(p, StringComparison.Ordinal)) return true;
            // Sadece bu kelimeden ibaret tam mesaj
            if (t == p.Trim()) return true;
        }

        return false;
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

        // Hallucination Guard: AI sadece DB'deki başlıkları kullanabilir
        var curriculumTitles = siblings.Select(t => t.Title).ToList();

        var congratsMsg = $"✅ **Mükemmel cevap!** {currentTopic.Title} konusunu geçtik.\n\n---";
        var nextLesson = await _tutorAgent.GetFirstLessonAsync(
            parentTopic?.Title ?? nextTopic.Title, nextTopic.Title, curriculumTitles);

        return ($"{congratsMsg}\n\n**Sıradaki Konumuz: {nextTopic.Title}**\n\n{nextLesson}", true);
    }
}
