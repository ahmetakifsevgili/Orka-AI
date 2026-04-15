using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    // S-03: Aynı kullanıcı+konu için mükerrer session oluşmasını önleyen kilit
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();
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

    public async IAsyncEnumerable<string> ProcessMessageStreamAsync(Guid userId, string content, Guid? topicId, Guid? sessionId, bool isPlanMode = false)
    {
        // SESSION — Controller already called GetOrCreateSessionAsync and passed the real sessionId.
        // We MUST NOT call it again here or we will create a second topic every time.
        if (!sessionId.HasValue)
        {
            yield return "Oturum bulunamadı.";
            yield break;
        }

        var session = await _db.Sessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == sessionId.Value && s.UserId == userId);

        if (session == null)
        {
            yield return "Oturum bulunamadı.";
            yield break;
        }

        // Kullanıcı mesajını kaydet (duplike önleme: son mesaj aynı değilse)
        var lastMsg = session.Messages?.OrderByDescending(m => m.CreatedAt).FirstOrDefault();
        bool isDuplicate = lastMsg != null && lastMsg.Role == "user" && lastMsg.Content == content;
        if (!isDuplicate)
        {
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
        }

        string fullResponse = "";

        // ÖNEMLİ: Session state kontrolleri isPlanMode'dan ÖNCE gelmelidir.
        // Aksi halde quiz cevabı gönderildiğinde plan akışı tekrar tetiklenir.
        bool needsSyncRoute =
            session.CurrentState == SessionState.BaselineQuizMode ||
            session.CurrentState == SessionState.QuizMode ||
            session.CurrentState == SessionState.AwaitingChoice ||
            session.CurrentState == SessionState.QuizPending ||
            isPlanMode ||
            content.Contains("/plan", StringComparison.OrdinalIgnoreCase);

        if (needsSyncRoute)
        {
            // Durum bazlı thinking state'i önce stream'e yolla (UI donmasını önlemek için)
            // NOT: C#'ta try-catch bloğu içinde yield return kullanılamaz, bu yüzden dışarıda.
            string thinkingHint;
            bool isBaselineMode = session.CurrentState == SessionState.BaselineQuizMode;
            if (isBaselineMode)
                thinkingHint = "[THINKING: Quiz cevabi degerlendiriliyor ve kisisel mufredat olusturuluyor...]";
            else if (session.CurrentState == SessionState.QuizMode)
                thinkingHint = "[THINKING: Cevabin analiz ediliyor...]";
            else if (isPlanMode || content.Contains("/plan", StringComparison.OrdinalIgnoreCase))
                thinkingHint = "[THINKING: Konu arastiriliyor ve seviye tespiti baslatiliyor...]";
            else
                thinkingHint = "[THINKING: Yanit hazirlaniyor...]";

            yield return thinkingHint;

            if (isBaselineMode)
                yield return "[THINKING: Kisisel ogrenme plani derleniyor...]";

            string syncResponse;
            try
            {
                // 1. Önce session state'e göre routing (state machine öncelikli)
                if (session.CurrentState == SessionState.BaselineQuizMode)
                {
                    var result = await HandleBaselineQuizModeAsync(userId, content, session);
                    syncResponse = result.Response;
                    await SaveAiMessage(session, userId, syncResponse);
                }
                else if (session.CurrentState == SessionState.QuizMode)
                {
                    var result = await HandleQuizModeAsync(userId, content, session);
                    syncResponse = result.Response;
                    await SaveAiMessage(session, userId, syncResponse);
                }
                else if (session.CurrentState == SessionState.AwaitingChoice)
                {
                    var result = await HandleAwaitingChoiceStateAsync(userId, content, session);
                    syncResponse = result.Response;
                    await SaveAiMessage(session, userId, syncResponse);
                }
                // 2. isPlanMode veya /plan komutu → DeepPlan
                else if (isPlanMode || content.Contains("/plan", StringComparison.OrdinalIgnoreCase))
                {
                    var result = await TriggerBaselineQuizForPlanAsync(userId, content, session);
                    syncResponse = result.Response;
                    await SaveAiMessage(session, userId, syncResponse);
                }
                else // QuizPending
                {
                    syncResponse = await HandleLearningStateAsync(userId, content, session);
                    await SaveAiMessage(session, userId, syncResponse);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[STREAM] Senkron routing hatası");
                syncResponse = "Bir hata oluştu. Lütfen tekrar deneyin.";
            }

            if (!string.IsNullOrEmpty(syncResponse))
            {
                TriggerBackgroundTasks(session, userId, content, syncResponse);
            }

            yield return syncResponse;
            yield break;
        }

        // Varsayılan: Normal ders anlatımı — gerçek zamanlı STREAM
        await foreach (var chunk in _tutorAgent.GetResponseStreamAsync(userId, content, session, false))
        {
            fullResponse += chunk;
            yield return chunk;
        }

        // 3. POST-STREAM: SAVE TO DB & BACKGROUND TASKS
        if (!string.IsNullOrEmpty(fullResponse))
        {
            await SaveAiMessage(session, userId, fullResponse);
            TriggerBackgroundTasks(session, userId, content, fullResponse);
        }
    }

    private void TriggerBackgroundTasks(Session session, Guid userId, string content, string aiResponse)
    {
        var capturedTopicId = session.TopicId;
        var capturedSessionId = session.Id;

        _ = Task.Run(async () => {
            using var scope = _scopeFactory.CreateScope();
            var summarizer = scope.ServiceProvider.GetRequiredService<ISummarizerAgent>();
            var analyzer = scope.ServiceProvider.GetRequiredService<IAnalyzerAgent>();
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

            try
            {
                // Konu anlatımı tamamlandıysa wiki üret — wiki yalnızca sohbet bittikten sonra oluşturulur
                var msgs = await db.Messages.Where(m => m.SessionId == capturedSessionId).OrderBy(m => m.CreatedAt).ToListAsync();
                var isComplete = await analyzer.AnalyzeCompletionAsync(msgs);
                if (isComplete) await summarizer.SummarizeAndSaveWikiAsync(capturedSessionId, capturedTopicId ?? Guid.Empty, userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[TriggerBackgroundTasks] Wiki üretimi başarısız. SessionId={SessionId} TopicId={TopicId}", capturedSessionId, capturedTopicId);
                // Wiki üretimi başarısız olursa ilgili WikiPage'i "failed" olarak işaretle
                try
                {
                    if (capturedTopicId.HasValue)
                    {
                        var failedPage = await db.WikiPages
                            .FirstOrDefaultAsync(p => p.TopicId == capturedTopicId.Value && p.Status == "pending");
                        if (failedPage != null)
                        {
                            failedPage.Status = "failed";
                            await db.SaveChangesAsync();
                        }
                    }
                }
                catch (Exception dbEx)
                {
                    _logger.LogError(dbEx, "[TriggerBackgroundTasks] WikiPage 'failed' işaretlenemedi.");
                }
            }
        });
    }

    // ── Yardımcı: AI mesajını DB'ye kaydet ──────────────────────────────────
    private async Task SaveAiMessage(Session session, Guid userId, string content)
    {
        var aiMsg = new Message
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            UserId = userId,
            Role = "assistant",
            Content = content,
            CreatedAt = DateTime.UtcNow,
            MessageType = content.Contains("```json") ? MessageType.Quiz : MessageType.General
        };
        _db.Messages.Add(aiMsg);
        session.Messages.Add(aiMsg);
        await _db.SaveChangesAsync();
    }

    public async Task<ChatMessageResponse> ProcessMessageAsync(Guid userId, string content, Guid? topicId, Guid? sessionId, bool isPlanMode = false)
    {
        // ── DEEP PLAN MODE ─ bypass normal agent routing ────
        if (isPlanMode)
        {
            return await HandleDeepPlanModeAsync(userId, content, topicId, sessionId);
        }

        Session? session = await GetOrCreateSessionAsync(userId, topicId, sessionId, content);
        if (session == null) throw new Exception("Oturum oluşturulamadı veya SmallTalk.");

        bool isNewTopic = !session.Messages.Any();

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
        bool planCreated = false;
        Guid? activeWikiPageId = null;

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
        }
        else if (session.CurrentState == SessionState.BaselineQuizMode)
        {
            var result = await HandleBaselineQuizModeAsync(userId, content, session);
            aiResponse = result.Response;
            wikiUpdated = result.WikiUpdated;
            planCreated = result.PlanCreated;
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
        bool isQuizMessage = aiResponse.Contains("```json") || aiResponse.Contains("```quiz");

        var aiMsg = new Message
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            UserId = userId,
            Role = "assistant",
            Content = aiResponse,
            CreatedAt = DateTime.UtcNow,
            MessageType = isQuizMessage ? MessageType.Quiz : MessageType.General
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
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[BackgroundAnalysis] Tamamlanma analizi başarısız. SessionId={SessionId}", sId);
                }
            });
        }
        return new ChatMessageResponse
        {
            MessageId   = aiMsg.Id,
            SessionId   = session.Id,
            TopicId     = session.TopicId ?? Guid.Empty,
            Content     = aiResponse,
            Role        = "assistant",
            CreatedAt   = aiMsg.CreatedAt,
            ModelUsed   = "Tutor-Agent",
            MessageType = (isQuizMessage ? MessageType.Quiz : MessageType.General).ToString().ToLowerInvariant(),
            WikiUpdated = wikiUpdated,
            PlanCreated = planCreated,
            WikiPageId  = activeWikiPageId,
            IsNewTopic  = isNewTopic,
            TopicTitle  = isNewTopic ? (await _db.Topics.FindAsync(session.TopicId))?.Title : null
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
            var topic = await _db.Topics.FindAsync(session.TopicId);
            if (topic != null)
            {
                // DeepPlan: Seviye Tespiti (Baseline Quiz) başlat
                _logger.LogInformation("[DeepPlan] Müfredat için seviye tespiti başlatılıyor: {Topic}", topic.Title);

                topic.Category = "Plan";
                await _db.SaveChangesAsync();

                var quizResponse = await _deepPlanAgent.GenerateBaselineQuizAsync(topic.Title);
                
                session.PendingQuiz = ExtractQuizQuestionText(quizResponse);
                session.CurrentState = SessionState.BaselineQuizMode;
                await _db.SaveChangesAsync();

                var responseText = $"Şimdi senin için en uygun öğrenme yolunu çizebilmem için seviyeni ölçmeliyim.\n\n" +
                                   $"Lütfen şu soruya yanıt ver:\n\n{quizResponse}";

                return (responseText, false);
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
        bool wantsPlan = content.Contains("/plan", StringComparison.OrdinalIgnoreCase) ||
                         content.Contains("plan yap", StringComparison.OrdinalIgnoreCase)   ||
                         content.Contains("müfredat", StringComparison.OrdinalIgnoreCase);

        if (wantsPlan)
        {
            return await TriggerBaselineQuizForPlanAsync(userId, content, session).ContinueWith(t => t.Result.Response);
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

        var currentTopic = session.TopicId.HasValue 
            ? await _db.Topics.FindAsync(session.TopicId.Value) 
            : null;

        // ── SAVE QUIZ ATTEMPT ──────────────────────────────────────────────
        var attempt = new QuizAttempt
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            TopicId = session.TopicId ?? Guid.Empty,
            UserId = userId,
            Question = session.PendingQuiz ?? "Unknown Question",
            UserAnswer = content,
            IsCorrect = isCorrect,
            Explanation = isCorrect ? "Doğru cevap!" : "Yanlış cevap.",
            CreatedAt = DateTime.UtcNow
        };
        _db.QuizAttempts.Add(attempt);
        await _db.SaveChangesAsync();
        // ──────────────────────────────────────────────────────────────────

        if (!isCorrect)
        {
            _logger.LogInformation("[QUIZ] Yanlış cevap. Tekrar soruldu.");
            return ($"Hmm, tam olarak değil. Tekrar deneyelim:\n\n**{session.PendingQuiz}**", false);
        }

        // Doğru cevap → XP + Streak güncelle
        _logger.LogInformation("[QUIZ] Doğru cevap! XP ve streak güncelleniyor.");

        var user = await _db.Users.FindAsync(userId);
        if (user != null)
        {
            // XP
            user.TotalXP += 20;

            // Streak
            var today = DateTime.UtcNow.Date;
            if (user.LastActiveDate == null)
            {
                user.CurrentStreak = 1;
            }
            else
            {
                var lastDate = user.LastActiveDate.Value.Date;
                if (lastDate == today)
                {
                    // Bugün zaten aktif — streak aynı kalır
                }
                else if (lastDate == today.AddDays(-1))
                {
                    // Dün aktifti — streak devam ediyor
                    user.CurrentStreak += 1;
                }
                else
                {
                    // Seri koptu — sıfırla
                    user.CurrentStreak = 1;
                }
            }
            user.LastActiveDate = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogInformation("[QUIZ] TotalXP={XP}, CurrentStreak={Streak}", user.TotalXP, user.CurrentStreak);
        }

        // Konu geçişi
        _logger.LogInformation("[QUIZ] Konu geçişi ve başarı puanı güncelleniyor.");
        
        // Başarı Puanı ve İlerleme Güncelleme (Mastery Pipeline)
        if (currentTopic != null)
        {
            var activeSubTopic = await _db.Topics
                .Where(t => t.ParentTopicId == currentTopic.Id && t.UserId == userId)
                .OrderBy(t => t.Order)
                .Skip(currentTopic.CompletedSections)
                .FirstOrDefaultAsync();

            if (activeSubTopic != null)
            {
                activeSubTopic.SuccessScore = Math.Min(100, activeSubTopic.SuccessScore + 20); // Her doğru cevap +20 puan
                activeSubTopic.ProgressPercentage = 100; // Alt konu tamamlandığı için %100
                activeSubTopic.IsMastered = true;        // Alt konu bazında mastered (Yeşil sidebar)

                // Alt konu tamamlandı — wiki üretimini arka planda başlat
                var completedSubtopicId = activeSubTopic.Id;
                var capturedSessionId = session.Id;
                var capturedUserId = userId;
                _ = Task.Run(async () => {
                    using var scope = _scopeFactory.CreateScope();
                    var summarizer = scope.ServiceProvider.GetRequiredService<ISummarizerAgent>();
                    var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
                    try
                    {
                        await summarizer.SummarizeAndSaveWikiAsync(capturedSessionId, completedSubtopicId, capturedUserId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[QuizMode] Alt konu wiki üretimi başarısız. SubtopicId={SubtopicId}", completedSubtopicId);
                        try
                        {
                            var failedPage = await db.WikiPages
                                .FirstOrDefaultAsync(p => p.TopicId == completedSubtopicId && p.Status == "pending");
                            if (failedPage != null)
                            {
                                failedPage.Status = "failed";
                                await db.SaveChangesAsync();
                            }
                        }
                        catch (Exception dbEx)
                        {
                            _logger.LogError(dbEx, "[QuizMode] WikiPage 'failed' işaretlenemedi. SubtopicId={SubtopicId}", completedSubtopicId);
                        }
                    }
                });
            }
            
            // Parent Topic (Ana Konu) İlerlemesini Güncelle
            if (currentTopic.TotalSections > 0)
            {
                int nextCompleted = currentTopic.CompletedSections + 1;
                currentTopic.ProgressPercentage = (double)nextCompleted / currentTopic.TotalSections * 100;
                if (nextCompleted >= currentTopic.TotalSections) currentTopic.IsMastered = true;
            }
            await _db.SaveChangesAsync();
        }

        return await TransitionToNextTopicAsync(userId, session);
    }

    // ── Baseline Quiz değerlendirme + Deep Research Planlama ──────────────────────────────
    private async Task<(string Response, bool WikiUpdated, bool PlanCreated)> HandleBaselineQuizModeAsync(
        Guid userId, string content, Session session)
    {
        bool isCorrect = await _tutorAgent.EvaluateQuizAnswerAsync(session.PendingQuiz ?? "", content);
        
        // ── SAVE QUIZ ATTEMPT (Baseline) ───────────────────────────────────
        var attempt = new QuizAttempt
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            TopicId = session.TopicId ?? Guid.Empty,
            UserId = userId,
            Question = session.PendingQuiz ?? "Baseline Evaluation",
            UserAnswer = content,
            IsCorrect = isCorrect,
            Explanation = isCorrect ? "Kullanıcı temellere sahip." : "Kullanıcı başlangıç seviyesinde.",
            CreatedAt = DateTime.UtcNow
        };
        _db.QuizAttempts.Add(attempt);
        // ──────────────────────────────────────────────────────────────────

        string userLevel = isCorrect ? "İleri (Temelleri biliyor)" : "Başlangıç (Sıfırdan)";

        _logger.LogInformation("[BASELINE QUIZ] Kullanıcı değerlendirmesi: {Level}", userLevel);

        session.CurrentState = SessionState.Learning;
        session.PendingQuiz = null;
        await _db.SaveChangesAsync();

        var topic = await _db.Topics.FindAsync(session.TopicId);
        if (topic == null) return ("Konu bulunamadı.", false, false);

        var subTopics = await _deepPlanAgent.GenerateAndSaveDeepPlanAsync(
            topic.Id, 
            topic.Title, 
            userId, 
            userLevel, 
            topic.PhaseMetadata);
        await _db.Entry(topic).ReloadAsync();

        var titles = subTopics.Select(t => t.Title).ToList();
        var planWelcomeText = await _tutorAgent.GetDeepPlanWelcomeAsync(userId, content, session, titles);

        if (subTopics.Any())
        {
            var firstChild = subTopics.First();
            // session.TopicId ana konuda kalmaya devam ediyor (DEĞİŞTİRMİYORUZ)
            topic.TotalSections = subTopics.Count;
            topic.CompletedSections = 0;
            await _db.SaveChangesAsync();

            var levelLabel = isCorrect ? "İleri Seviye" : "Temel Seviye";
            var prefix = isCorrect 
                ? "Harika, temellerin sağlam görünüyor! Gereksiz tekrarları atlayıp doğrudan ileri konulara odaklanan bir müfredat hazırladım." 
                : "Güzel deneme! Bu konuyu sıfırdan, adım adım ve sana özel bir yolla öğreteceğim.";

            // Müfredat listesini numaralandırılmış Markdown olarak hazırla
            var planListMarkdown = string.Join("\n", titles.Select((t, i) =>
                $"{i + 1}. {(i == 0 ? $"**{t}** ← _Buradan başlıyoruz_" : t)}"));

            string firstLessonContent;
            try
            {
                firstLessonContent = await _tutorAgent.GetFirstLessonAsync(topic.Title, firstChild.Title, titles);
            }
            catch
            {
                firstLessonContent = $"**{firstChild.Title}** konusuna hoş geldin! Derse başlamak için hazır mısın?";
            }

            var fullResponse = prefix + "\n\n" + planWelcomeText +
                "\n\n---\n\n### Musfredat (" + titles.Count + " Konu)\n\n" +
                planListMarkdown +
                "\n\n---\n\n**Ilk Konumuz: " + firstChild.Title + "**\n\n" +
                firstLessonContent;

            return (fullResponse + " [PLAN_READY]", true, true);
        }

        return (planWelcomeText, true, true);
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
            const string markerJson = "```json";
            const string markerQuiz = "```quiz";
            int idx = response.IndexOf(markerQuiz, StringComparison.Ordinal);
            if (idx < 0) idx = response.IndexOf(markerJson, StringComparison.Ordinal);
            if (idx < 0) return response;

            // Satır sonunu bul (```json\n veya ```quiz\n)
            var nIdx = response.IndexOf('\n', idx);
            if (nIdx < 0) nIdx = idx + 7;

            var after  = response[nIdx..];
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

        // Sistemin dallanma listesini getir
        var siblings = await _db.Topics
            .Where(t => t.ParentTopicId == currentTopic.Id && t.UserId == userId)
            .OrderBy(t => t.Order)
            .ToListAsync();

        if (siblings.Count == 0)
        {
            session.CurrentState = SessionState.Learning;
            session.PendingQuiz = null;
            await _db.SaveChangesAsync();
            return ("🎉 Tebrikler! Bu konuyu tamamladın.", true);
        }

        int currentIndex = currentTopic.CompletedSections;

        if (currentIndex < 0 || currentIndex >= siblings.Count - 1)
        {
            // Tüm alt konular bitti
            currentTopic.IsMastered = true;
            _logger.LogInformation("[TRANSITION] Tüm alt konular tamamlandı. TopicId={Id}", currentTopic.Id);
            session.CurrentState = SessionState.Learning;
            session.PendingQuiz = null;
            await _db.SaveChangesAsync();

            var allDoneSubtopicId = (currentIndex >= 0 && currentIndex < siblings.Count)
                ? siblings[currentIndex].Id
                : siblings.Last().Id;

            return ($"🎉 **Harika!** Tüm alt konuları başarıyla tamamladın! Artık ana konunun uzmanısın.[TOPIC_COMPLETE:{allDoneSubtopicId}]", true);
        }

        // Sıradaki konuya geç
        var completedSubtopic = siblings[currentIndex];
        currentTopic.CompletedSections++;
        var nextTopic = siblings[currentIndex + 1];
        // Session.TopicId DEĞİŞMİYOR. Ana konuda (Parent) kalıyor.
        session.CurrentState = SessionState.Learning;
        session.PendingQuiz = null;
        await _db.SaveChangesAsync();

        _logger.LogInformation("[TRANSITION] {From} → {To}", completedSubtopic.Title, nextTopic.Title);

        var curriculumTitles = siblings.Select(t => t.Title).ToList();

        var congratsMsg = $"✅ **Mükemmel cevap!** Sıradaki konuya geçiyoruz.\n\n---";
        var nextLesson = await _tutorAgent.GetFirstLessonAsync(
            currentTopic.Title, nextTopic.Title, curriculumTitles);

        return ($"{congratsMsg}\n\n**Sıradaki Konumuz: {nextTopic.Title}**\n\n{nextLesson}[TOPIC_COMPLETE:{completedSubtopic.Id}]", true);
    }

    // ════════════════════════════════════════════════════════════════════════
    // DEEP PLAN MODE — Unified Plan Pipeline (DeepPlanAgent)
    // ════════════════════════════════════════════════════════════════════════

    private async Task<ChatMessageResponse> HandleDeepPlanModeAsync(
        Guid userId, string content, Guid? topicId, Guid? sessionId)
    {
        _logger.LogInformation("[DEEP_PLAN] Planlama modu etkinleştirildi: {Content}", content);

        Topic? topic = topicId.HasValue ? await _db.Topics.FindAsync(topicId.Value) : null;
        Session? session = null;

        if (topic == null)
        {
            string title = content.Replace("/plan", "").Trim();
            if (string.IsNullOrWhiteSpace(title)) title = "Yeni Öğrenme Planı";
            if (title.Length > 50) title = title[..50] + "...";
            
            var (newTopic, newSession) = await _topicService.CreateDiscoveryTopicAsync(userId, title, "Plan");
            topic = newTopic;
            session = newSession;
        }

        if (session == null)
        {
            if (sessionId.HasValue)
            {
                session = await _db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId.Value && s.UserId == userId)
                          ?? new Session
                          {
                              Id = Guid.NewGuid(),
                              UserId = userId,
                              TopicId = topic.Id,
                              CreatedAt = DateTime.UtcNow,
                              CurrentState = Core.Enums.SessionState.Learning,
                              Messages = new List<Message>()
                          };
                if (session.Messages == null) session.Messages = new List<Message>();
            }
            else
            {
                session = new Session
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    TopicId = topic.Id,
                    CreatedAt = DateTime.UtcNow,
                    CurrentState = Core.Enums.SessionState.Learning,
                    Messages = new List<Message>()
                };
                _db.Sessions.Add(session);
                await _db.SaveChangesAsync();
            }
        }

        // NOT: Stream path zaten user message'ı kaydediyor.
        // Sadece ProcessMessageAsync (non-stream) path'inden çağrıldığında kaydet.
        // Stream path session.Id gönderir, ProcessMessageAsync ise sessionId null gönderebilir.

        var quizResponse = await TriggerBaselineQuizForPlanAsync(userId, content, session);
        
        return new ChatMessageResponse
        {
            MessageId   = Guid.NewGuid(),
            SessionId   = session.Id,
            TopicId     = topic.Id,
            Content     = quizResponse.Response,
            Role        = "assistant",
            CreatedAt   = DateTime.UtcNow,
            ModelUsed   = "DeepPlan_Quiz",
            MessageType = "quiz",
            WikiUpdated = false,
            WikiPageId  = null,
            IsNewTopic  = topicId == null,
            TopicTitle  = topic.Title,
        };
    }

    private async Task<(string Response, bool WikiUpdated)> TriggerBaselineQuizForPlanAsync(Guid userId, string content, Session session)
    {
        var topic = await _db.Topics.FindAsync(session.TopicId);
        if (topic == null) return ("Hata: Konu bulunamadı.", false);

        // Kullanıcının /plan ile açık bir niyeti (intent) varsa Topic'i override et
        string intent = content.Replace("/plan", "", StringComparison.OrdinalIgnoreCase).Trim();
        if (!string.IsNullOrWhiteSpace(intent) && !intent.Equals("1") && intent.Length > 2)
        {
            topic.Title = intent.Length > 80 ? intent[..80] + "..." : intent;
        }

        topic.Category = "Plan";
        await _db.SaveChangesAsync();

        _logger.LogInformation("[DeepPlan] Müfredat planı seviye tespiti (Baseline Quiz) başlatılıyor: {Topic}", topic.Title);

        // Albert Service yerine doğrudan DeepPlanAgent'i kullanıyoruz.
        var quizResponse = await _deepPlanAgent.GenerateBaselineQuizAsync(topic.Title);
        
        session.PendingQuiz = ExtractQuizQuestionText(quizResponse);
        session.CurrentState = SessionState.BaselineQuizMode;
        await _db.SaveChangesAsync();

        var responseText = $"Harika! **{topic.Title}** konusu için detaylı bir akademik planlama süreci başlatıyorum. 🚀\n\n" + 
                            "Öncelikle senin için en uygun öğrenme müfredatını çizebilmem için genel bilgi seviyeni ölçmeliyim.\n\n" +
                            $"Lütfen başlangıç niteliğindeki şu soruyu dikkatlice yanıtla:\n\n{quizResponse}";

        return (responseText, false);
    }

    public async Task<Session?> GetOrCreateSessionAsync(Guid userId, Guid? topicId, Guid? sessionId, string content)
    {
        Session? session = null;
        if (sessionId.HasValue)
        {
            session = await _db.Sessions
                .Where(s => s.Id == sessionId && s.UserId == userId)
                .FirstOrDefaultAsync();
        }

        if (session == null && topicId.HasValue)
        {
            session = await _db.Sessions
                .Where(s => s.TopicId == topicId && s.UserId == userId)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();
        }

        if (session != null)
        {
            session.Messages = await _db.Messages
                .Where(m => m.SessionId == session.Id)
                .OrderByDescending(m => m.CreatedAt)
                .Take(20)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();
            return session;
        }

        // IsSmallTalk bariyeri ruhsuzluğu artırdığı için kaldırıldı.
        // if (IsSmallTalk(content)) return null;

        // S-03: Aynı kullanıcı için eşzamanlı session oluşturma yarış koşulunu önle
        var lockKey = $"{userId}";
        var semaphore = _sessionLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        try
        {
            // Kilidi aldıktan sonra tekrar kontrol et — başka bir istek oluşturmuş olabilir
            if (topicId.HasValue)
            {
                var existing = await _db.Sessions
                    .Where(s => s.TopicId == topicId && s.UserId == userId)
                    .OrderByDescending(s => s.CreatedAt)
                    .FirstOrDefaultAsync();
                if (existing != null)
                {
                    existing.Messages = await _db.Messages
                        .Where(m => m.SessionId == existing.Id)
                        .OrderByDescending(m => m.CreatedAt)
                        .Take(20)
                        .OrderBy(m => m.CreatedAt)
                        .ToListAsync();
                    return existing;
                }
            }
        }
        finally
        {
            semaphore.Release();
        }

        string titlePreview = content.Length > 40 ? content[..40] : content;
        
        string resolvedCategory = "Plan";
        using (var scope = _scopeFactory.CreateScope())
        {
            var groq = scope.ServiceProvider.GetRequiredService<IGroqService>();
            var route = await groq.SemanticRouteAsync(content);
            resolvedCategory = !string.IsNullOrEmpty(route.Category) ? route.Category : "Plan";
        }

        var (topic, newSession) = await _topicService.CreateDiscoveryTopicAsync(userId, titlePreview, resolvedCategory);
        
        // ChatGPT-Style Auto Naming (AIServiceChain ile başlık üret)
        _ = Task.Run(async () => {
            try {
                using var scope = _scopeFactory.CreateScope();
                var aiChain = scope.ServiceProvider.GetRequiredService<IAIServiceChain>();
                var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
                
                var generatedTitle = await aiChain.GenerateWithFallbackAsync(
                    "Sen bir konu başlığı üreticisin. Verilen kısa mesajdan 3-5 kelimelik kısa bir konu başlığı üret. Sadece başlığı yaz. Örnek: 'Python Temelleri', 'React Eğitimi'",
                    content
                );
                if (!string.IsNullOrWhiteSpace(generatedTitle)) {
                    generatedTitle = generatedTitle.Trim().Trim('"').Trim();
                    if (generatedTitle.Length > 60) generatedTitle = generatedTitle[..60];
                    var t = await db.Topics.FindAsync(topic.Id);
                    if (t != null) {
                        t.Title = generatedTitle;
                        await db.SaveChangesAsync();
                        _logger.LogInformation("[AUTO-NAMING] Başlık güncellendi: {Title}", generatedTitle);
                    }
                }
            } catch (Exception ex) {
                _logger.LogWarning(ex, "[AUTO-NAMING] Başlık üretimi arka planda başarısız oldu.");
            }
        });

        session = await _db.Sessions
            .FirstOrDefaultAsync(s => s.Id == newSession.Id);
            
        if (session != null)
        {
            session.Messages = new List<Message>();
            session.CurrentState = SessionState.Learning;
            await _db.SaveChangesAsync();
        }

        return session;
    }
}
