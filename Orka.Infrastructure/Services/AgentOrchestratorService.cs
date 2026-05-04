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
using Orka.Core.Constants;
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
    private readonly ISupervisorAgent _supervisor;
    private readonly ICorrelationContext _correlationContext;
    private readonly ISkillMasteryService _skillMastery;
    private readonly ITokenCostEstimator _tokenEstimator;
    private readonly IAIAgentFactory _agentFactory;
    private readonly IBackgroundTaskQueue _backgroundQueue;
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
        ISupervisorAgent supervisor,
        ICorrelationContext correlationContext,
        ISkillMasteryService skillMastery,
        ITokenCostEstimator tokenEstimator,
        IAIAgentFactory agentFactory,
        IBackgroundTaskQueue backgroundQueue,
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
        _supervisor = supervisor;
        _correlationContext = correlationContext;
        _skillMastery = skillMastery;
        _tokenEstimator = tokenEstimator;
        _agentFactory = agentFactory;
        _backgroundQueue = backgroundQueue;
        _logger = logger;
    }

    public async Task EndSessionAsync(Guid sessionId, Guid userId)
    {
        var session = await _db.Sessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId);
        if (session != null)
        {
            session.EndedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            // Session bitişinde otomatik Wiki üretimi - öğrenci erken ayrılsa bile not defteri oluşsun
            if (session.TopicId.HasValue && session.Messages?.Count >= 4)
            {
                var capturedTopicId = session.TopicId.Value;
                var capturedSessionId = session.Id;
                var capturedCorrelationId = _correlationContext.CorrelationId;
                _ = _backgroundQueue.QueueAsync(new BackgroundTaskItem(
                    "session-end-wiki-summary",
                    userId,
                    capturedCorrelationId,
                    async ct =>
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var summarizer = scope.ServiceProvider.GetRequiredService<ISummarizerAgent>();
                        await summarizer.SummarizeAndSaveWikiAsync(capturedSessionId, capturedTopicId, userId);
                        _logger.LogInformation("[Orchestrator] EndSession Wiki uretimi tamamlandi. SessionId={SessionId}", capturedSessionId);
                    },
                    MaxAttempts: 2,
                    Timeout: TimeSpan.FromSeconds(90)));
            }
        }
    }

    public async IAsyncEnumerable<string> ProcessMessageStreamAsync(
        Guid userId,
        string content,
        Guid? topicId,
        Guid? sessionId,
        bool isPlanMode = false,
        Guid? focusTopicId = null,
        string? focusTopicPath = null,
        string? focusSourceRef = null)
    {
        // SESSION - Controller already called GetOrCreateSessionAsync and passed the real sessionId.
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

        var tutorContent = await BuildFocusedTutorContentAsync(content, focusTopicId, focusTopicPath, focusSourceRef);

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
            session.Messages ??= new List<Message>();
            session.Messages.Add(userMsg);
            await _db.SaveChangesAsync();
        }

        string fullResponse = "";

        // UI'daki "Plan Modu" açık kalsa bile kullanıcı Quiz cevaplıyorsa Plan modunu ezgeç.
        var trimmedContent = content.Trim();
        bool isAnsweringQuiz =
            trimmedContent.StartsWith("**Quiz Cevabım:**", StringComparison.OrdinalIgnoreCase) ||
            trimmedContent.StartsWith("**Quiz Cevabim:**", StringComparison.OrdinalIgnoreCase);
        if (isAnsweringQuiz)
        {
            isPlanMode = false;
            // Eğer sistem (AI streaming) quizi sorduktan sonra DataBase state'i Learning'de unutmuşsa,
            // bunu organik bir Lesson Quiz (ders sonu testi) olarak kabul edip durumu QuizMode'a zorla.
            if (session.CurrentState == SessionState.Learning)
            {
                session.CurrentState = SessionState.QuizMode;
                _logger.LogInformation("[Orchestrator] Quiz cevabı saptandı, state öğrenmeden QuizMode'a geçirildi.");
            }
        }

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

            // State handler içinde değişir - Evaluator etiketlemesi için entry state'i yakala
            var entryState = session.CurrentState;

            string syncResponse;
            Guid syncMsgId = Guid.Empty;
            try
            {
                if (session.CurrentState == SessionState.BaselineQuizMode)
                {
                    var result = await HandleBaselineQuizModeAsync(userId, content, session);
                    syncResponse = result.Response;
                    syncMsgId = await SaveAiMessage(session, userId, syncResponse);
                }
                else if (session.CurrentState == SessionState.QuizMode)
                {
                    var result = await HandleQuizModeAsync(userId, content, session);
                    syncResponse = result.Response;
                    syncMsgId = await SaveAiMessage(session, userId, syncResponse);
                }
                else if (session.CurrentState == SessionState.AwaitingChoice)
                {
                    var result = await HandleAwaitingChoiceStateAsync(userId, content, session);
                    syncResponse = result.Response;
                    syncMsgId = await SaveAiMessage(session, userId, syncResponse);
                }
                // 2. isPlanMode veya /plan komutu -> DeepPlan
                else if (isPlanMode || content.Contains("/plan", StringComparison.OrdinalIgnoreCase))
                {
                    var result = await TriggerBaselineQuizForPlanAsync(userId, content, session);
                    syncResponse = result.Response;
                    syncMsgId = await SaveAiMessage(session, userId, syncResponse);
                }
                else // QuizPending
                {
                    syncResponse = await HandleLearningStateAsync(userId, tutorContent, session);
                    syncMsgId = await SaveAiMessage(session, userId, syncResponse);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[STREAM] Senkron routing hatası");
                syncResponse = "Bir hata oluştu. Lütfen tekrar deneyin.";
            }

            if (!string.IsNullOrEmpty(syncResponse) && syncMsgId != Guid.Empty)
            {
                // Sync route path'ten hangi ajan cevap verdiyse doğru rolü etiketle (entry state baz alınır)
                string syncAgentRole = entryState switch
                {
                    SessionState.QuizMode         => "GraderAgent",
                    SessionState.BaselineQuizMode => "DeepPlanAgent",
                    SessionState.AwaitingChoice   => "TutorAgent",
                    _                             => isPlanMode || content.Contains("/plan", StringComparison.OrdinalIgnoreCase)
                                                     ? "DeepPlanAgent" : "TutorAgent"
                };
                TriggerBackgroundTasks(session, userId, content, syncResponse, syncMsgId, syncAgentRole);
            }

            yield return syncResponse;
            yield break;
        }

        // 2B. Dinamik Yönlendirme (Supervisor Intent Check)
        var actionRoute = await _supervisor.DetermineActionRouteAsync(content, session.Messages);
        _logger.LogInformation("[Orchestrator] Supervisor Route Kararı: {Route}", actionRoute);

        if (actionRoute == "QUIZ" && session.CurrentState == SessionState.Learning)
        {
            _logger.LogInformation("[Orchestrator] Kullanıcı organik olarak quiz talep etti. Durum QuizPending'e çekiliyor.");
            session.CurrentState = SessionState.QuizPending;
            // Veri kaydedilir ancak akış Tutor'un pekiştirme veya soru hazırlama mesajına devredilir (Aşağıda isQuizPending = true olarak algılar)
        }

        // Varsayılan: Normal ders anlatımı - gerçek zamanlı STREAM
        bool isQuizPending = session.CurrentState == SessionState.QuizPending;
        await foreach (var chunk in _tutorAgent.GetResponseStreamAsync(userId, tutorContent, session, isQuizPending))
        {
            fullResponse += chunk;
            yield return chunk;
        }

        // 3. POST-STREAM: SAVE TO DB & BACKGROUND TASKS
        if (!string.IsNullOrEmpty(fullResponse))
        {
            var msgId = await SaveAiMessage(session, userId, fullResponse);
            TriggerBackgroundTasks(session, userId, content, fullResponse, msgId);
        }
    }

    private async Task<string> BuildFocusedTutorContentAsync(
        string content,
        Guid? focusTopicId,
        string? focusTopicPath,
        string? focusSourceRef)
    {
        if (!focusTopicId.HasValue && string.IsNullOrWhiteSpace(focusTopicPath) && string.IsNullOrWhiteSpace(focusSourceRef))
            return content;

        string? focusTitle = null;
        if (focusTopicId.HasValue)
        {
            focusTitle = await _db.Topics
                .AsNoTracking()
                .Where(t => t.Id == focusTopicId.Value)
                .Select(t => t.Title)
                .FirstOrDefaultAsync();
        }

        var focusLine = !string.IsNullOrWhiteSpace(focusTopicPath)
            ? focusTopicPath!.Trim()
            : focusTitle;

        var context = new List<string>
        {
            "[ORKA_CONTEXT]",
            "Kullanıcı ana sohbet içinde belirli bir ders/alt dal bağlamından geldi.",
        };

        if (!string.IsNullOrWhiteSpace(focusLine))
            context.Add($"Aktif odak: {focusLine}");

        if (focusTopicId.HasValue)
            context.Add($"FocusTopicId: {focusTopicId.Value}");

        if (!string.IsNullOrWhiteSpace(focusSourceRef))
            context.Add($"Kaynak/citation odağı: {focusSourceRef}");

        context.Add("Yanıtı bu odağa göre ver; genel konuya dağılma. Kaynak kullanıyorsan citation etiketlerini koru.");
        context.Add("[/ORKA_CONTEXT]");

        return string.Join(Environment.NewLine, context) + Environment.NewLine + Environment.NewLine + content;
    }

    private void TriggerBackgroundTasks(Session session, Guid userId, string content, string aiResponse, Guid aiMessageId, string agentRole = "TutorAgent")
    {
        var capturedTopicId      = session.TopicId;
        var capturedSessionId    = session.Id;
        var capturedAgentRole    = agentRole;
        // ICorrelationContext request-scoped oldugu icin background queue'ya girmeden once capture edilir.
        var capturedCorrelationId = _correlationContext.CorrelationId;

        _ = _backgroundQueue.QueueAsync(new BackgroundTaskItem(
            "agent-feedback-loop",
            userId,
            capturedCorrelationId,
            async ct =>
            {
            using var scope = _scopeFactory.CreateScope();
            var summarizer = scope.ServiceProvider.GetRequiredService<ISummarizerAgent>();
            var analyzer   = scope.ServiceProvider.GetRequiredService<IAnalyzerAgent>();
            var evaluator  = scope.ServiceProvider.GetRequiredService<IEvaluatorAgent>();
            var db         = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

            _logger.LogInformation(
                "[Background] EvaluatorAgent başladı. Session={SessionId} Correlation={CorrelationId}",
                capturedSessionId, capturedCorrelationId);

            try
            {
                // 1. LLMOps Evaluator - ilgili ajan yanıtını değerlendir
                //    topicId geçilir: puan >= 9 ise Altın Örnek kaydedilir (Faz 12, yalnızca TutorAgent)
                var (score, feedback) = await evaluator.EvaluateInteractionAsync(
                    capturedSessionId, content, aiResponse, capturedAgentRole,
                    topicId: capturedTopicId);

                var eval = new AgentEvaluation
                {
                    SessionId         = capturedSessionId,
                    UserId            = userId,
                    MessageId         = aiMessageId,
                    AgentRole         = capturedAgentRole,
                    UserInput         = content,
                    AgentResponse     = aiResponse,
                    EvaluationScore   = score,
                    EvaluatorFeedback = feedback,
                    CreatedAt         = DateTime.UtcNow
                };
                db.AgentEvaluations.Add(eval);
                await db.SaveChangesAsync();

                _logger.LogInformation(
                    "[Background] EvaluatorAgent tamamlandı. Puan={Score}/10 Correlation={CorrelationId}",
                    score, capturedCorrelationId);

                // -- Faz 16: Anlık Müdahale - düşük kalite ise TutorAgent için flag bırak --
                // TutorAgent bir sonraki yanıtında bu uyarıyı tüketip stilini düzeltsin.
                if (score < 7 && capturedAgentRole == "TutorAgent")
                {
                    var redisService = scope.ServiceProvider.GetRequiredService<IRedisMemoryService>();
                    await redisService.SetLowQualityFeedbackAsync(capturedSessionId, score, feedback);
                    _logger.LogInformation(
                        "[Background] Düşük kalite flag set. SessionId={SessionId} Score={Score}",
                        capturedSessionId, score);
                }

                // 2. Konu anlatımı tamamlandıysa wiki üret
                _logger.LogInformation(
                    "[Background] AnalyzerAgent başladı. Session={SessionId} Correlation={CorrelationId}",
                    capturedSessionId, capturedCorrelationId);

                var msgs           = await db.Messages.Where(m => m.SessionId == capturedSessionId).OrderBy(m => m.CreatedAt).ToListAsync();
                var analyzerResult = await analyzer.AnalyzeCompletionAsync(msgs);

                _logger.LogInformation(
                    "[Background] AnalyzerAgent tamamlandı. IsComplete={IsComplete} MsgCount={Count} Correlation={CorrelationId}",
                    analyzerResult.IsComplete, msgs.Count, capturedCorrelationId);

                // Faz 15: Yaşayan Organizasyon - Öğrenci profilini kaydet
                if (capturedTopicId.HasValue && analyzerResult.IntentData != null)
                {
                    var redisService = scope.ServiceProvider.GetRequiredService<IRedisMemoryService>();
                    await redisService.RecordStudentProfileAsync(
                        capturedTopicId.Value,
                        analyzerResult.IntentData.UnderstandingScore,
                        analyzerResult.IntentData.Weaknesses);
                }

                // Wiki özeti üret: konu tamamlandıysa VEYA yeterli yeni mesaj biriktiğinde (en az 6 mesaj ve son wiki'den bu yana 6+ yeni mesaj)
                var aiMsgCount = msgs.Count(m => m.Role == "assistant");
                var shouldSummarize = analyzerResult.IsComplete || (aiMsgCount >= 3 && msgs.Count >= 6 && msgs.Count % 6 == 0);

                if (shouldSummarize && capturedTopicId.HasValue)
                {
                    _logger.LogInformation(
                        "[Background] SummarizerAgent başladı. TopicId={TopicId} Trigger={Trigger} Correlation={CorrelationId}",
                        capturedTopicId, analyzerResult.IsComplete ? "Complete" : "MessageCount", capturedCorrelationId);

                    await summarizer.SummarizeAndSaveWikiAsync(capturedSessionId, capturedTopicId.Value, userId);

                    _logger.LogInformation(
                        "[Background] SummarizerAgent tamamlandı. TopicId={TopicId} Correlation={CorrelationId}",
                        capturedTopicId, capturedCorrelationId);

                    // Faz 13 Adım 13.4: Otomatik Ders Geçişi
                    var topicService = scope.ServiceProvider.GetRequiredService<ITopicService>();
                    var tutorAgentScoped = scope.ServiceProvider.GetRequiredService<ITutorAgent>();
                    var mediatorScoped = scope.ServiceProvider.GetRequiredService<IMediator>();

                    await HandleTopicProgressionAsync(
                        capturedSessionId, capturedTopicId.Value, userId, db, topicService, tutorAgentScoped, mediatorScoped);

                    // Faz 12: SummarizerAgent çıktısını da değerlendir (wiki kalitesi)
                    // Wiki içeriğini geri okuyarak EvaluatorAgent'a gönder - DB'ye kaydeder, gold example değil
                    try
                    {
                        var wikiService   = scope.ServiceProvider.GetRequiredService<IWikiService>();
                        var wikiContent   = await wikiService.GetWikiFullContentAsync(capturedTopicId.Value, userId);
                        var topicEntity   = await db.Topics.FindAsync(capturedTopicId.Value);
                        var topicTitle    = topicEntity?.Title ?? "Konu";

                        if (!string.IsNullOrWhiteSpace(wikiContent))
                        {
                            var (wikiScore, wikiFeedback) = await evaluator.EvaluateInteractionAsync(
                                capturedSessionId, topicTitle, wikiContent, "SummarizerAgent");

                            db.AgentEvaluations.Add(new AgentEvaluation
                            {
                                SessionId         = capturedSessionId,
                                UserId            = userId,
                                MessageId         = aiMessageId,
                                AgentRole         = "SummarizerAgent",
                                UserInput         = topicTitle,
                                AgentResponse     = wikiContent.Length > 500 ? wikiContent[..500] + "..." : wikiContent,
                                EvaluationScore   = wikiScore,
                                EvaluatorFeedback = wikiFeedback,
                                CreatedAt         = DateTime.UtcNow
                            });
                            await db.SaveChangesAsync();

                            _logger.LogInformation(
                                "[Background] SummarizerAgent kalite puanı: {Score}/10 Correlation={CorrelationId}",
                                wikiScore, capturedCorrelationId);
                        }
                    }
                    catch (Exception wikiEvalEx)
                    {
                        _logger.LogWarning(wikiEvalEx,
                            "[Background] SummarizerAgent değerlendirmesi başarısız - ana akış etkilenmedi. Correlation={CorrelationId}",
                            capturedCorrelationId);
                    }
                }
                else if (analyzerResult.IsComplete && !capturedTopicId.HasValue)
                {
                    _logger.LogWarning(
                        "[Background] IsComplete=true ama TopicId null - SummarizerAgent atlandı. Correlation={CorrelationId}",
                        capturedCorrelationId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[Background] HATA. Session={SessionId} TopicId={TopicId} Correlation={CorrelationId}",
                    capturedSessionId, capturedTopicId, capturedCorrelationId);

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
                    _logger.LogError(dbEx,
                        "[Background] WikiPage 'failed' işaretlenemedi. Correlation={CorrelationId}",
                        capturedCorrelationId);
                }
            }
            },
            MaxAttempts: 1,
            Timeout: TimeSpan.FromSeconds(120)));
    }

    // -- Yardımcı: AI mesajını DB'ye kaydet ----------------------------------
    private async Task<Guid> SaveAiMessage(Session session, Guid userId, string content)
    {
        // Token/cost tahmini: input = son kullanıcı mesajı, output = AI yanıtı
        var lastUserInput = session.Messages?
            .Where(m => m.Role == "user")
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefault()?.Content ?? string.Empty;
        var model = _agentFactory.GetModel(AgentRole.Tutor);
        var (tokens, cost) = _tokenEstimator.Estimate(model, lastUserInput, content);

        var aiMsg = new Message
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            UserId = userId,
            Role = "assistant",
            Content = content,
            ModelUsed = model,
            TokensUsed = tokens,
            CostUSD = cost,
            CreatedAt = DateTime.UtcNow,
            MessageType = (content.Contains("```json") || content.Contains("```quiz") ||
                           (content.TrimStart().StartsWith("[{") && content.Contains("\"question\"")))
                          ? MessageType.Quiz : MessageType.General
        };
        _db.Messages.Add(aiMsg);
        session.Messages ??= new List<Message>();
        session.Messages.Add(aiMsg);

        // Session toplamlarını güncelle (Dashboard maliyet widget'ı için)
        session.TotalTokensUsed += tokens;
        session.TotalCostUSD    += cost;

        await _db.SaveChangesAsync();
        return aiMsg.Id;
    }

    public async Task<ChatMessageResponse> ProcessMessageAsync(
        Guid userId,
        string content,
        Guid? topicId,
        Guid? sessionId,
        bool isPlanMode = false,
        Guid? focusTopicId = null,
        string? focusTopicPath = null,
        string? focusSourceRef = null)
    {
        // -- DEEP PLAN MODE - bypass normal agent routing ----
        if (isPlanMode)
        {
            return await HandleDeepPlanModeAsync(userId, content, topicId, sessionId);
        }

        Session? session = await GetOrCreateSessionAsync(userId, topicId, sessionId, content);
        if (session == null) throw new BadRequestException("Oturum oluşturulamadı veya SmallTalk.");

        bool isNewTopic = !session.Messages.Any();
        var tutorContent = await BuildFocusedTutorContentAsync(content, focusTopicId, focusTopicPath, focusSourceRef);

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
            var responseTask = _tutorAgent.GetResponseAsync(userId, tutorContent, session, false);
            aiResponse = responseTask != null ? await responseTask : "Yanıt üretilemedi.";
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
                    userId, tutorContent, session),
                _ => await _tutorAgent.GetResponseAsync(userId, tutorContent, session, false)
            };
        }

        // 3. SAVE AI MESSAGE
        bool isQuizMessage = aiResponse.Contains("```json") || aiResponse.Contains("```quiz") ||
                             (aiResponse.TrimStart().StartsWith("[{") && aiResponse.Contains("\"question\""));

        var tutorModel = _agentFactory.GetModel(AgentRole.Tutor);
        var (aiTokens, aiCost) = _tokenEstimator.Estimate(tutorModel, content, aiResponse);

        var aiMsg = new Message
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            UserId = userId,
            Role = "assistant",
            Content = aiResponse,
            ModelUsed = tutorModel,
            TokensUsed = aiTokens,
            CostUSD = aiCost,
            CreatedAt = DateTime.UtcNow,
            MessageType = isQuizMessage ? MessageType.Quiz : MessageType.General
        };
        _db.Messages.Add(aiMsg);
        session.Messages.Add(aiMsg);

        session.TotalTokensUsed += aiTokens;
        session.TotalCostUSD    += aiCost;

        await _db.SaveChangesAsync();

        // AUTO-WIKI: Mesaj başına wiki üretimi YAPILMAZ (CLAUDE.md kuralı).
        // Wiki yalnızca (a) alt konu quiz'i geçildiğinde veya (b) AnalyzerAgent konu tamamlandı
        // dediğinde SummarizerAgent tarafından üretilir. Stream path ile tutarlı.
        if (!skipAutoWiki && session.TopicId.HasValue)
        {
            var tId = session.TopicId.Value;
            var hasActivePage = await _db.WikiPages
                .AnyAsync(p => p.TopicId == tId && (p.Status == "pending" || p.Status == "learning"));
            if (hasActivePage) wikiUpdated = true;
        }

        // BACKGROUND ANALYSIS (Topic Completed?)
        if (session.CurrentState == SessionState.Learning)
        {
            var sId = session.Id;
            var tId = session.TopicId;

            _ = _backgroundQueue.QueueAsync(new BackgroundTaskItem(
                "learning-completion-analysis",
                userId,
                _correlationContext.CorrelationId,
                async ct =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
                    var analyzer = scope.ServiceProvider.GetRequiredService<IAnalyzerAgent>();
                    var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

                    var msgs = await db.Messages.Where(m => m.SessionId == sId).OrderBy(m => m.CreatedAt).ToListAsync(ct);
                    var analyzerRes = await analyzer.AnalyzeCompletionAsync(msgs);

                    if (analyzerRes.IsComplete && tId.HasValue)
                    {
                        var topicService = scope.ServiceProvider.GetRequiredService<ITopicService>();
                        var tutorAgentScoped = scope.ServiceProvider.GetRequiredService<ITutorAgent>();

                        await HandleTopicProgressionAsync(
                            sId, tId.Value, userId, db, topicService, tutorAgentScoped, mediator);
                    }
                },
                MaxAttempts: 1,
                Timeout: TimeSpan.FromSeconds(90)));
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
                _logger.LogInformation("[DeepPlan] Müfredat için seviye tespiti başlatılıyor: {Topic}", topic.Title);
                topic.Category = "Plan";
                await _db.SaveChangesAsync();

                var allQuizJson = await _deepPlanAgent.GenerateBaselineQuizAsync(topic.Title);
                var firstQuizJson = ExtractNthQuizFromArray(allQuizJson, 0);

                session.BaselineQuizData = allQuizJson;
                session.BaselineQuizIndex = 0;
                session.BaselineCorrectCount = 0;
                session.PendingQuiz = ExtractQuizQuestionText(firstQuizJson);
                session.CurrentState = SessionState.BaselineQuizMode;
                await _db.SaveChangesAsync();

                var responseText = $"Senin için en uygun öğrenme yolunu çizebilmem için **20 soruluk** kapsamlı bir seviye testi yapacağım. \n\n" +
                                   $"{allQuizJson}";

                return (responseText, false);
            }
        }
        else if (wantsChat)
        {
            session.CurrentState = SessionState.Learning;
            await _db.SaveChangesAsync();
            return ("Anlaştık! Sohbet üzerinden devam edelim. Ne merak ediyorsun, sormak istediğin bir şey var mı?", false);
        }

        // Belirsiz yanıt - Learning'e dön, doğal sohbete devam et
        session.CurrentState = SessionState.Learning;
        await _db.SaveChangesAsync();
        return (await _tutorAgent.GetResponseAsync(userId, content, session, false), false);
    }

    private async Task<string> HandleLearningStateAsync(Guid userId, string content, Session session)
    {
        // -- /plan komutu: müfredat planı teklifi --------------------------------
        bool wantsPlan = content.Contains("/plan", StringComparison.OrdinalIgnoreCase) ||
                         content.Contains("plan yap", StringComparison.OrdinalIgnoreCase)   ||
                         content.Contains("müfredat", StringComparison.OrdinalIgnoreCase);

        if (wantsPlan)
        {
            var result = await TriggerBaselineQuizForPlanAsync(userId, content, session);
            return result.Response;
        }

        // -- "Anladım" / "Konuyu Geç" tespiti -----------------------------------
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

                var quizJson = await _tutorAgent.GenerateTopicQuizAsync(currentTopic.Title);
                session.PendingQuiz  = quizJson;
                session.CurrentState = SessionState.QuizMode;
                await _db.SaveChangesAsync();

                // Kullanıcıya tam yanıt gönderilir (JSON quiz dizisi)
                return $"Harika! Seni tebrik etmeden önce bu konuyu tam pekiştirdiğimizden emin olmak için **5 soruluk** küçük bir testimiz var \n\n{quizJson}";
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

    // -- Quiz cevabı değerlendirme + konu geçişi ------------------------------
    private async Task<(string Response, bool WikiUpdated)> HandleQuizModeAsync(
        Guid userId, string content, Session session)
    {
        bool isSkipped = content.Contains("[SKIP_QUIZ]");
        bool isFinished =
            content.Contains("Testi Tamamlandı") ||
            content.Contains("Testi Tamamlandi") ||
            content.Contains("Quiz Cevabım") ||
            content.Contains("Quiz Cevabim");
        // IDE'den "Hocaya Gönder" ile gelen kodlama cevabı: Quiz Sorusu başlığı + kod bloğu içerir.
        bool isIDESubmission = !isFinished && content.Contains("**Quiz Sorusu:**") && content.Contains("```");

        int score = 0;
        int total = 1;

        if (isSkipped)
        {
            _logger.LogInformation("[QUIZ] Kullanıcı testi atladı.");
        }
        else if (isFinished)
        {
            // "3/5 Doğru" özetinden skoru çıkar. Slash'ten sonraki boşluk çeşitli olabilir.
            var match = System.Text.RegularExpressions.Regex.Match(content, @"(\d+)\s*/\s*(\d+)\s*Do(?:ğ|g)ru", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                score = int.Parse(match.Groups[1].Value);
                total = int.Parse(match.Groups[2].Value);
            }
            else
            {
                // Tek soru vakası
                score = content.Contains("Doğru", StringComparison.OrdinalIgnoreCase) || content.Contains("Dogru", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            }
        }
        else if (isIDESubmission)
        {
            // Kodlama cevabını TutorAgent ile değerlendir, tek soru akışı gibi işle.
            _logger.LogInformation("[QUIZ] IDE kod cevabı değerlendiriliyor. SessionId={SessionId}", session.Id);
            bool isCorrect = await _tutorAgent.EvaluateQuizAnswerAsync(session.PendingQuiz ?? "Unknown", content);
            score = isCorrect ? 1 : 0;
            total = 1;
        }
        else
        {
            // Legacy tek soru step-by-step
            bool isCorrect = await _tutorAgent.EvaluateQuizAnswerAsync(session.PendingQuiz ?? "Unknown", content);
            if (!isCorrect)
            {
                return ($"Hmm, tam olarak değil. Tekrar deneyelim:\n\n**{session.PendingQuiz}**", false);
            }
            score = 1;
            total = 1;
        }

        // -- PROCESS RESULTS & TRANSITION ----------------------------------
        var user = await _db.Users.FindAsync(userId);
        var currentTopic = session.TopicId.HasValue ? await _db.Topics.FindAsync(session.TopicId.Value) : null;

        if (currentTopic != null)
        {
            var orderedLessons = await _topicService.GetOrderedLessonsAsync(currentTopic.Id, userId);
            var activeSubTopic = orderedLessons.Skip(currentTopic.CompletedSections).FirstOrDefault();

            if (activeSubTopic != null)
            {
                var quizPercentageForAdvance = total > 0 ? (double)score / total : 0;
                if (!isSkipped && quizPercentageForAdvance < 0.6)
                {
                    session.RemedialAttemptCount++;
                    activeSubTopic.ProgressPercentage = Math.Max(activeSubTopic.ProgressPercentage, quizPercentageForAdvance * 100);
                    activeSubTopic.IsMastered = false;

                    var remedialLesson = await GenerateRemedialLessonAsync(userId, session, activeSubTopic, content, score, total);
                    session.CurrentState = SessionState.Learning;
                    session.PendingQuiz = null;
                    await _db.SaveChangesAsync();

                    return ($"Skorun **{score}/{total}**. Bu konuyu henuz tamamlandi saymiyorum; asagidaki kisa telafi dersi zayif becerilerine odaklaniyor.\n\n{remedialLesson}", false);
                }
                activeSubTopic.ProgressPercentage = 100; // Alt konu her halükarda tamamlandı

                if (!isSkipped)
                {
                    double percentage = (double)score / total;
                    int currentCalculatedScore = (int)(percentage * 100);
                    int previousScore = activeSubTopic.SuccessScore;

                    // High Score Mantığı ve Rekor Kırma (Sonsuz XP Engeli)
                    if (currentCalculatedScore > previousScore)
                    {
                        // Sadece önceki rekoru geçtiği puan farkı kadar XP! (Max 20 XP)
                        int scoreDifference = currentCalculatedScore - previousScore;
                        int xpReward = (int)((scoreDifference / 100.0) * 20);

                        if (xpReward > 0 && user != null)
                        {
                            user.TotalXP += xpReward;
                            user.LastActiveDate = DateTime.UtcNow;
                            _logger.LogInformation("[QUIZ] Score: {Score}/{Total}. Yeni rekor (+{Diff})! XP Reward: {XP}", score, total, scoreDifference, xpReward);
                        }

                        // Eski skor rekorla güncellenir
                        activeSubTopic.SuccessScore = currentCalculatedScore;

                        if (percentage >= 0.6)
                        {
                            activeSubTopic.IsMastered = true;
                            await _skillMastery.RecordMasteryAsync(userId, activeSubTopic.Id, activeSubTopic.Title, activeSubTopic.SuccessScore);
                            // Başarılıysa remedial sayacı sıfırla
                            session.RemedialAttemptCount = 0;
                        }
                        else
                        {
                            // Quiz geçildi ama düşük -> telafi sayacı +1
                            session.RemedialAttemptCount++;
                        }
                    }
                    else
                    {
                        if (user != null) user.LastActiveDate = DateTime.UtcNow;
                        _logger.LogInformation("[QUIZ] Score: {Score}/{Total}. Eski rekor ({Prev}) başarıyla korundu, ilerleniyor.", score, total, previousScore);

                        // Önceki skoru geçemediyse de (rekor kırılmadı) telafi denemesi sayılır
                        if (percentage < 0.6) session.RemedialAttemptCount++;
                    }

                    // Faz 16: 2+ telafi -> önkoşul dersi sinyali (sonsuz döngü engeli)
                    // Redis'e bir bayrak bırak, TutorAgent bir sonraki yanıtta önkoşul dersine yönlendirsin.
                    if (session.RemedialAttemptCount >= 2)
                    {
                        _logger.LogWarning(
                            "[REMEDIAL] {Count} telafi denemesi sonrasında önkoşul yönlendirmesi. SessionId={SessionId} Topic={Topic}",
                            session.RemedialAttemptCount, session.Id, activeSubTopic.Title);

                        var capturedTopicTitle = activeSubTopic.Title;
                        var capturedSessionForFlag = session.Id;
                        var capturedRemedialAttemptCount = session.RemedialAttemptCount;
                        _ = _backgroundQueue.QueueAsync(new BackgroundTaskItem(
                            "remedial-low-quality-flag",
                            userId,
                            _correlationContext.CorrelationId,
                            async ct =>
                            {
                                using var s = _scopeFactory.CreateScope();
                                var redis = s.ServiceProvider.GetRequiredService<IRedisMemoryService>();
                                await redis.SetLowQualityFeedbackAsync(
                                    capturedSessionForFlag, 4,
                                    $"Ogrenci '{capturedTopicTitle}' konusunda {capturedRemedialAttemptCount} kez basarisiz. " +
                                    "Bir onceki/temel konuyu kisa orneklerle hatirlatarak basla, sonra ders devam etsin.");
                            },
                            MaxAttempts: 2,
                            Timeout: TimeSpan.FromSeconds(20)));

                        session.RemedialAttemptCount = 0;
                    }
                }

                // Alt konu tamamlandı - wiki üretimini arka planda başlat
                var completedSubtopicId = activeSubTopic.Id;
                var capturedSessionId = session.Id;
                var capturedUserId = userId;
                _ = _backgroundQueue.QueueAsync(new BackgroundTaskItem(
                    "quiz-completed-subtopic-wiki",
                    capturedUserId,
                    _correlationContext.CorrelationId,
                    async ct =>
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var summarizer = scope.ServiceProvider.GetRequiredService<ISummarizerAgent>();
                        await summarizer.SummarizeAndSaveWikiAsync(capturedSessionId, completedSubtopicId, capturedUserId);
                    },
                    MaxAttempts: 2,
                    Timeout: TimeSpan.FromSeconds(90)));
            }

            // Parent Topic (Ana Konu) İlerlemesini Güncelle
            if (currentTopic.TotalSections > 0)
            {
                int nextCompleted = currentTopic.CompletedSections + 1;
                currentTopic.ProgressPercentage = (double)nextCompleted / currentTopic.TotalSections * 100;
                if (nextCompleted >= currentTopic.TotalSections) currentTopic.IsMastered = true;
            }
        }

        // Move to next topic session state
        session.CurrentState = SessionState.Learning;
        session.PendingQuiz = null;
        await _db.SaveChangesAsync();

        // -- TRANSITION TO NEXT TOPIC --------------------------------------
        var (nextResponse, _) = await TransitionToNextTopicAsync(userId, session);

        string prefix = isSkipped
            ? "Peki, testi atlıyoruz. Sıradaki konuya geçiyoruz... "
            : $"Test için teşekkürler! Skorun: **{score}/{total}**. Bakalım sırada ne var... ";

        return ($"{prefix}\n\n{nextResponse}", false);
    }

    private async Task<string> GenerateRemedialLessonAsync(
        Guid userId,
        Session session,
        Topic activeSubTopic,
        string quizContent,
        int score,
        int total)
    {
        var failedSkills = ExtractLabeledValue(quizContent, @"Hata\s+Yap\S*lan\s+Beceriler");
        var failedTopics = ExtractLabeledValue(quizContent, @"Hata\s+Yap\S*lan\s+Konular");
        var focus = !string.IsNullOrWhiteSpace(failedSkills)
            ? failedSkills
            : !string.IsNullOrWhiteSpace(failedTopics)
                ? failedTopics
                : activeSubTopic.Title;

        var systemPrompt = """
            Sen Orka AI'nin Telafi (Remedial) ajanisin.
            Ogrenci quizde dusuk skor aldi; konu tamamlanmis sayilmayacak.
            Gorev: Sadece hatali becerilere odaklanan kisa, net, moral bozmayacak bir telafi dersi uret.

            Zorunlu cikti:
            - "Nerede takildin?" basligi altinda 2-3 cumlelik tani.
            - "Tekrar anlatalim" basligi altinda basit analoji veya adim adim cozum.
            - Konu uygunsa Mermaid diyagrami, tablo veya formul adimi kullan.
            - "Mikro quiz" basligi altinda 2 soru ver; her soru skillTag tasimayi ima etsin.
            - Ogrenciyi ayni beceriyi tekrar denemeye davet et; siradaki konuya gecirme.
            """;

        var userPrompt = $"""
            Konu: {activeSubTopic.Title}
            Skor: {score}/{total}
            Hedef beceri/konular: {focus}

            Quiz cevap ozeti:
            {quizContent}
            """;

        var lesson = await _agentFactory.CompleteChatAsync(AgentRole.Remedial, systemPrompt, userPrompt);

        _db.RemediationPlans.Add(new RemediationPlan
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = activeSubTopic.Id,
            SessionId = session.Id,
            SkillTag = focus.Length > 250 ? focus[..250] : focus,
            Status = "active",
            LessonMarkdown = lesson,
            CreatedAt = DateTime.UtcNow
        });

        _db.LearningSignals.Add(new LearningSignal
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = activeSubTopic.Id,
            SessionId = session.Id,
            SignalType = LearningSignalTypes.WeaknessDetected,
            SkillTag = focus,
            TopicPath = activeSubTopic.Title,
            Score = total > 0 ? (int)Math.Round(score * 100.0 / total) : 0,
            IsPositive = false,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { score, total, failedSkills, failedTopics }),
            CreatedAt = DateTime.UtcNow
        });

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var summarizer = scope.ServiceProvider.GetRequiredService<ISummarizerAgent>();
            summarizer.InvalidateNotebookTools(activeSubTopic.Id);
            if (activeSubTopic.ParentTopicId.HasValue)
                summarizer.InvalidateNotebookTools(activeSubTopic.ParentTopicId.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[REMEDIAL] Notebook cache invalidation atlandi.");
        }

        return lesson;
    }

    private static string ExtractLabeledValue(string content, string labelPattern)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            content,
            $@"{labelPattern}\s*:\s*(.+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!match.Success) return string.Empty;
        return match.Groups[1].Value.Split('\n')[0].Trim();
    }

    // -- Baseline Quiz değerlendirme (Multi-Round: 5 Soru) ------------------------------
    private async Task<(string Response, bool WikiUpdated, bool PlanCreated)> HandleBaselineQuizModeAsync(
        Guid userId, string content, Session session)
    {
        int totalQuestions = 20; // Default
        string correctEmoji = "Tamam.";
        string feedbackLine = "Tebrikler, seviye testi tamamlandı!";
        string? failedTopics = null;

        // Check if this is an aggregated baseline completion message from frontend
        if (content.Contains("**Seviye Testi Tamamlandı:**") || content.Contains("**Seviye Testi Tamamlandi:**"))
        {
            var correctCount = 0;

            // Try to extract the score like "15/20 Doğru"
            var match = System.Text.RegularExpressions.Regex.Match(content, @"(\d+)/(\d+)\sDo(?:ğ|g)ru", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                correctCount = int.Parse(match.Groups[1].Value);
                totalQuestions = int.Parse(match.Groups[2].Value);
            }

            // Structured skill diagnosis: konu listesinden once alt beceri listesini tercih et.
            var failedSkills = ExtractLabeledValue(content, @"Hata\s+Yap\S*lan\s+Beceriler");
            var failedTopicList = ExtractLabeledValue(content, @"Hata\s+Yap\S*lan\s+Konular");
            if (!string.IsNullOrWhiteSpace(failedSkills) || !string.IsNullOrWhiteSpace(failedTopicList))
            {
                failedTopics = !string.IsNullOrWhiteSpace(failedSkills)
                    ? $"Beceriler: {failedSkills}; Konular: {failedTopicList}"
                    : failedTopicList;
                _logger.LogInformation("[BASELINE QUIZ] Basarisiz beceri/konular algilandi: {FailedTopics}", failedTopics);
            }

            session.BaselineQuizIndex = totalQuestions;
            session.BaselineCorrectCount = correctCount;
        }
        else
        {
            // Fallback for legacy step-by-step
            totalQuestions = session.BaselineQuizData != null ? System.Text.Json.JsonDocument.Parse(session.BaselineQuizData).RootElement.GetArrayLength() : 5;
            bool isCorrect = await _tutorAgent.EvaluateQuizAnswerAsync(session.PendingQuiz ?? "", content);

            if (isCorrect) session.BaselineCorrectCount++;
            session.BaselineQuizIndex++;

            var currentIndex = session.BaselineQuizIndex;
            if (currentIndex < totalQuestions)
            {
                var nextQuizJson = ExtractNthQuizFromArray(session.BaselineQuizData ?? "[]", currentIndex);
                session.PendingQuiz = ExtractQuizQuestionText(nextQuizJson);
                await _db.SaveChangesAsync();

                return ($"{(isCorrect ? "Doğru!" : "Yanlış.")}\n\n" +
                        $"**Soru {currentIndex + 1}/{totalQuestions}:**\n\n{nextQuizJson}", false, false);
            }
        }

        // -- Seviye hesapla (Dinamik Yüzde Bazlı) ----------
        var finalCorrectCount = session.BaselineCorrectCount;
        double ratio = (double)finalCorrectCount / totalQuestions;

        string userLevel;
        string levelEmoji;
        if (ratio < 0.35) // 0-7 / 20
        {
            userLevel = "Başlangıç (Sıfırdan)";
            levelEmoji = "";
        }
        else if (ratio < 0.75) // 8-15 / 20
        {
            userLevel = "Orta (Temelleri biliyor)";
            levelEmoji = "";
        }
        else
        {
            userLevel = "İleri (Konuya hakim)";
            levelEmoji = "";
        }

        _logger.LogInformation("[BASELINE QUIZ] Sonuç: {Correct}/{Total} ({Ratio:P}) -> {Level}",
            finalCorrectCount, totalQuestions, ratio, userLevel);

        // State temizle
        session.CurrentState = SessionState.Learning;
        session.PendingQuiz = null;
        session.BaselineQuizData = null;
        session.BaselineQuizIndex = 0;
        session.BaselineCorrectCount = 0;
        await _db.SaveChangesAsync();

        var topic = await _db.Topics.FindAsync(session.TopicId);
        if (topic == null) return ("Konu bulunamadı.", false, false);

        // -- Müfredat oluştur (Hiyerarşik Modüler) -----------------------
        var allLessons = await _deepPlanAgent.GenerateAndSaveDeepPlanAsync(
            topic.Id, topic.Title, userId, userLevel, topic.PhaseMetadata, failedTopics);
        await _db.Entry(topic).ReloadAsync();

        // Modül -> Ders hiyerarşisini veritabanından çek (read-only render → AsNoTracking).
        var modules = await _db.Topics
            .AsNoTracking()
            .Where(t => t.ParentTopicId == topic.Id)
            .OrderBy(t => t.Order)
            .ToListAsync();

        // Her modülün altındaki dersleri çek (read-only render → AsNoTracking).
        var moduleIds = modules.Select(m => m.Id).ToList();
        var lessonsByModule = await _db.Topics
            .AsNoTracking()
            .Where(t => t.ParentTopicId != null && moduleIds.Contains(t.ParentTopicId.Value))
            .OrderBy(t => t.Order)
            .ToListAsync();

        // Müfredat render - modül/ders hiyerarşisi
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{correctEmoji} {feedbackLine}\n");
        sb.AppendLine($"###  Seviye Testi Sonucu: **{finalCorrectCount}/{totalQuestions}** - {levelEmoji} {userLevel}\n");

        if (ratio < 0.35)
            sb.AppendLine("Bu konuyu sıfırdan, adım adım ve sana özel bir müfredatla öğreteceğim.\n");
        else if (ratio < 0.75)
            sb.AppendLine("Temelleri biliyorsun! Gereksiz tekrarları atlayıp pratik ve uygulamaya ağırlık veren bir müfredat hazırladım.\n");
        else
            sb.AppendLine("Harika bir skor! İleri düzey konulara odaklanan yoğun bir müfredat hazırladım.\n");

        sb.AppendLine($"---\n\n###  Müfredat ({allLessons.Count} Ders, {modules.Count} Modül)\n");

        foreach (var mod in modules)
        {
            var lessons = lessonsByModule.Where(l => l.ParentTopicId == mod.Id).ToList();
            sb.AppendLine($"\n**{mod.Emoji} {mod.Title}** ({lessons.Count} ders)");
            foreach (var lesson in lessons)
            {
                sb.AppendLine($"  - {lesson.Title}");
            }
        }

        // İlk ders içeriğini üret
        var firstLesson = allLessons.FirstOrDefault();
        if (firstLesson != null)
        {
            sb.AppendLine($"\n---\n\n **Hadi Başlayalım:** Sol menüdeki **\"{firstLesson.Title}\"** dersine tıklayarak interaktif eğitime geçiş yapabilir veya doğrudan 'Başla' yazabilirsin!");
        }

        return (sb.ToString() + " [PLAN_READY]", true, true);
    }

    /// <summary>
    /// Mesajın selamlama / small talk olup olmadığını tespit eder.
    /// Yaygın yazım hatalarını (merha, slm, nbr vb.) Contains ile yakalar.
    /// Yeni Topic oluşturulmasını engellemek için kullanılır.
    /// </summary>
    private static bool IsSmallTalk(string content)
    {
        var t = content.Trim().ToLowerInvariant();

        // 1. Ü¡ok kısa mesaj (15 karakter altı) -> muhtemelen small talk
        if (t.Length < 15) return true;

        // 2. Bilinen selamlama kalıpları (kısmen yazım hatalı versiyonlar dahil)
        // StartsWith: mesajın selamlama ile başlaması
        // Contains: selamlama başka kelimelerle birleşmiş olabilir ("merha nasılsın")
        var prefixes = new[]
        {
            "merhaba", "merha",   // "merha nasılsın" -> typo guard
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
    private string ExtractQuizQuestionText(string response)
    {
        try
        {
            // Eğer direkt JSON object ise (markdown bloğu yok)
            var trimmed = response.Trim();
            if (trimmed.StartsWith("{"))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(trimmed);
                return doc.RootElement.GetProperty("question").GetString() ?? response;
            }

            const string markerJson = "```json";
            const string markerQuiz = "```quiz";
            int idx = response.IndexOf(markerQuiz, StringComparison.Ordinal);
            if (idx < 0) idx = response.IndexOf(markerJson, StringComparison.Ordinal);
            if (idx < 0) return response;

            var nIdx = response.IndexOf('\n', idx);
            if (nIdx < 0) nIdx = idx + 7;

            var after  = response[nIdx..];
            var endIdx = after.IndexOf("```", StringComparison.Ordinal);
            if (endIdx < 0) return response;

            var jsonStr = after[..endIdx].Trim();
            using var doc2 = System.Text.Json.JsonDocument.Parse(jsonStr);
            return doc2.RootElement.GetProperty("question").GetString() ?? response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Orchestrator] Quiz question extraction failed; returning raw response. Snippet={Snippet}",
                response.Length > 200 ? response[..200] : response);
            return response;
        }
    }

    /// <summary>
    /// 5 soruluk JSON array'den N'inci soruyu (0-tabanlı) tek JSON nesnesi olarak döndürür.
    /// Frontend'in quiz kartı olarak render edebilmesi için ```quiz ``` bloğu ile sarılır.
    /// </summary>
    private string ExtractNthQuizFromArray(string allQuizJson, int index)
    {
        try
        {
            var cleaned = allQuizJson.Trim();
            if (cleaned.StartsWith("```"))
                cleaned = cleaned.Replace("```json", "").Replace("```", "").Trim();

            var s = cleaned.IndexOf('[');
            var e = cleaned.LastIndexOf(']');
            if (s >= 0 && e > s) cleaned = cleaned[s..(e + 1)];

            using var doc = System.Text.Json.JsonDocument.Parse(cleaned);
            var arr = doc.RootElement.EnumerateArray().ToList();
            if (index < arr.Count)
            {
                return arr[index].GetRawText();
            }

            _logger.LogWarning(
                "[Orchestrator] Quiz array index out of range. Index={Index} Count={Count}",
                index, arr.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[Orchestrator] Quiz array parse failed; using fallback question. Index={Index} Snippet={Snippet}",
                index,
                allQuizJson.Length > 200 ? allQuizJson[..200] : allQuizJson);
        }

        // Fallback: boş soru
        return """{"question": "Bu soru yüklenemedi.", "options": [{"text": "Devam et", "isCorrect": true}], "explanation": "Teknik bir sorun oluştu."}""";
    }

    // -- Bir sonraki alt başlığa geç ------------------------------------------
    private async Task<(string Response, bool WikiUpdated)> TransitionToNextTopicAsync(
        Guid userId, Session session)
    {
        if (!session.TopicId.HasValue)
        {
            session.CurrentState = SessionState.Learning;
            session.PendingQuiz = null;
            await _db.SaveChangesAsync();
            return (" Tebrikler! Tüm konuları başarıyla tamamladın!", true);
        }

        var currentTopic = await _db.Topics.FindAsync(session.TopicId.Value);
        if (currentTopic == null)
        {
            _logger.LogWarning("[TRANSITION] TopicId geçerli ama Topic kaydı bulunamadı. SessionId={SessionId}", session.Id);
            session.CurrentState = SessionState.Learning;
            session.PendingQuiz = null;
            await _db.SaveChangesAsync();
            return (" Tebrikler! Bu konuyu tamamladın.", true);
        }

        // Sistemin dallanma listesini getir
        var siblings = await _topicService.GetOrderedLessonsAsync(currentTopic.Id, userId);

        if (siblings.Count == 0)
        {
            session.CurrentState = SessionState.Learning;
            session.PendingQuiz = null;
            await _db.SaveChangesAsync();
            return (" Tebrikler! Bu konuyu tamamladın.", true);
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

            return ($" **Harika!** Tüm alt konuları başarıyla tamamladın! Artık ana konunun uzmanısın.[TOPIC_COMPLETE:{allDoneSubtopicId}]", true);
        }

        // Sıradaki konuya geç
        var completedSubtopic = siblings[currentIndex];
        _db.LearningSignals.Add(new LearningSignal
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TopicId = completedSubtopic.Id,
            SessionId = session.Id,
            SignalType = "LessonCompleted",
            SkillTag = completedSubtopic.Title,
            TopicPath = currentTopic.Title,
            IsPositive = true,
            CreatedAt = DateTime.UtcNow
        });
        currentTopic.CompletedSections++;
        var nextTopic = siblings[currentIndex + 1];
        // Session.TopicId değişmiyor. Ana konuda (Parent) kalıyor.
        session.CurrentState = SessionState.Learning;
        session.PendingQuiz = null;
        await _db.SaveChangesAsync();

        _logger.LogInformation("[TRANSITION] {From} -> {To}", completedSubtopic.Title, nextTopic.Title);

        var curriculumTitles = siblings.Select(t => t.Title).ToList();

        var congratsMsg = $"Tamam **Mükemmel cevap!** Sıradaki konuya geçiyoruz.\n\n---";
        var nextLesson = await _tutorAgent.GetFirstLessonAsync(
            currentTopic.Title, nextTopic.Title, curriculumTitles);

        return ($"{congratsMsg}\n\n**Sıradaki Konumuz: {nextTopic.Title}**\n\n{nextLesson}[TOPIC_COMPLETE:{completedSubtopic.Id}]", true);
    }

    // ------------------------------------------------------------------------
    // DEEP PLAN MODE - Unified Plan Pipeline (DeepPlanAgent)
    // ------------------------------------------------------------------------

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

        // Gerçek öğrenme niyetini (Topic) AI ile ayrıştır
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IAIAgentFactory>();

            string intentPrompt = "Sen bir konu belirleyicisin. Kullanıcının mesajından ÖĞRENMEK İSTEDİĞİ asıl konuyu 2-4 kelime ile çıkar. Eğer ortada bir öğrenme isteği yoksa sadece 'NULL' dön. Örnek: 'C# Algoritmalar', 'Felsefe Tarihi'. Sadece konuyu döndür.";
            var extractedTopic = await factory.CompleteChatAsync(AgentRole.Analyzer, intentPrompt, content);

            if (!string.IsNullOrWhiteSpace(extractedTopic) && extractedTopic.Length > 2 && !extractedTopic.Contains("NULL"))
            {
                var cleanTitle = extractedTopic.Trim().Trim('"', '\'');
                if (cleanTitle.Length > 60) cleanTitle = cleanTitle[..60];
                topic.Title = cleanTitle;
                _logger.LogInformation("[DeepPlan] Chat bağlamı plan moduna çevrildi. Yeni Başlık: {Title}", cleanTitle);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DeepPlan] Niyet ayrıştırma başarısız oldu.");
        }

        topic.Category = "Plan";
        await _db.SaveChangesAsync();

        _logger.LogInformation("[DeepPlan] Müfredat planı seviye tespiti başlatılıyor: {Topic}", topic.Title);

        var allQuizJson = await _deepPlanAgent.GenerateBaselineQuizAsync(topic.Title);

        session.BaselineQuizData = allQuizJson;
        session.BaselineQuizIndex = 0;
        session.BaselineCorrectCount = 0;
        session.PendingQuiz = null;
        session.CurrentState = SessionState.BaselineQuizMode;
        await _db.SaveChangesAsync();

        var responseText = $"Harika! **{topic.Title}** konusu için detaylı bir akademik planlama süreci başlatıyorum. \n\n" +
                            "Öncelikle senin için en uygun öğrenme müfredatını çizebilmem için **Seviye Testi** yapacağım.\n\n" +
                            $"Lütfen aşağıdaki soruları dikkatlice yanıtla:\n\n{allQuizJson}";

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
            var treeTopicIds = await GetTopicTreeIdsAsync(topicId.Value, userId);

            session = await _db.Sessions
                .Where(s => s.TopicId.HasValue && treeTopicIds.Contains(s.TopicId.Value) && s.UserId == userId)
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
            // Kilidi aldıktan sonra tekrar kontrol et
            if (topicId.HasValue)
            {
                var treeTopicIds = await GetTopicTreeIdsAsync(topicId.Value, userId);
                var existing = await _db.Sessions
                    .Where(s => s.TopicId.HasValue && treeTopicIds.Contains(s.TopicId.Value) && s.UserId == userId)
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

                // Eğer topic varsa ama session yoksa (alt derslere ilk kez girildiğinde yaşanır)
                // Yeni bir 'Discovery Topic' üretmek yerine mevcuda bir Session bağlamalıyız!
                var targetTopic = await _db.Topics.FindAsync(topicId.Value);
                if (targetTopic != null && targetTopic.UserId == userId)
                {
                    var newLinkedSession = new Session
                    {
                        Id = Guid.NewGuid(),
                        TopicId = targetTopic.Id,
                        UserId = userId,
                        SessionNumber = 1,
                        CurrentState = SessionState.Learning,
                        TotalTokensUsed = 0,
                        TotalCostUSD = 0m,
                        CreatedAt = DateTime.UtcNow
                    };
                    _db.Sessions.Add(newLinkedSession);
                    await _db.SaveChangesAsync();

                    newLinkedSession.Messages = new List<Message>();
                    return newLinkedSession;
                }
            }
        }
        finally
        {
            semaphore.Release();
        }

        string titlePreview = content.Length > 40 ? content[..40] : content;

        string resolvedCategory = "Genel";
        using (var scope = _scopeFactory.CreateScope())
        {
            var groq = scope.ServiceProvider.GetRequiredService<IGroqService>();
            var route = await groq.SemanticRouteAsync(content);
            // Sadece açıkça "Plan" olarak tanımlanan kategorileri Plan yap, geri kalanı Genel sohbet
            resolvedCategory = string.Equals(route.Category, "Plan", StringComparison.OrdinalIgnoreCase) ? "Plan" : "Genel";
        }

        var (topic, newSession) = await _topicService.CreateDiscoveryTopicAsync(userId, titlePreview, resolvedCategory);

        // ChatGPT-Style Auto Naming
        _ = _backgroundQueue.QueueAsync(new BackgroundTaskItem(
            "topic-auto-naming",
            userId,
            _correlationContext.CorrelationId,
            async ct =>
            {
                using var scope = _scopeFactory.CreateScope();
                var factory = scope.ServiceProvider.GetRequiredService<IAIAgentFactory>();
                var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

                var generatedTitle = await factory.CompleteChatAsync(
                    AgentRole.Analyzer,
                    "Sen bir konu başlığı üreticisin. Verilen kısa mesajdan 3-5 kelimelik kısa bir konu başlığı üret. Sadece başlığı yaz. Örnek: 'Python Temelleri', 'React Eğitimi'",
                    content
                );
                if (!string.IsNullOrWhiteSpace(generatedTitle)) {
                    generatedTitle = generatedTitle.Trim().Trim('"').Trim();
                    if (generatedTitle.Length > 60) generatedTitle = generatedTitle[..60];
                    var t = await db.Topics.FindAsync(topic.Id);
                    if (t != null) {
                        t.Title = generatedTitle;
                        await db.SaveChangesAsync(ct);
                        _logger.LogInformation("[AUTO-NAMING] Başlık güncellendi: {Title}", generatedTitle);
                    }
                }
            },
            MaxAttempts: 1,
            Timeout: TimeSpan.FromSeconds(45)));

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

    private async Task<List<Guid>> GetTopicTreeIdsAsync(Guid topicId, Guid userId)
    {
        var topic = await _db.Topics
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == topicId && t.UserId == userId);
        if (topic == null) return new List<Guid>();

        while (topic.ParentTopicId.HasValue)
        {
            var parent = await _db.Topics
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == topic.ParentTopicId.Value && t.UserId == userId);
            if (parent == null) break;
            topic = parent;
        }

        var ids = new List<Guid> { topic.Id };
        var frontier = new List<Guid> { topic.Id };

        while (frontier.Count > 0)
        {
            var children = await _db.Topics
                .AsNoTracking()
                .Where(t => t.ParentTopicId.HasValue && frontier.Contains(t.ParentTopicId.Value) && t.UserId == userId)
                .Select(t => t.Id)
                .ToListAsync();

            frontier = children.Except(ids).ToList();
            ids.AddRange(frontier);
        }

        return ids;
    }

    private async Task HandleTopicProgressionAsync(
        Guid sessionId,
        Guid topicId,
        Guid userId,
        OrkaDbContext db,
        ITopicService topicService,
        ITutorAgent tutorAgent,
        IMediator mediator)
    {
        var topic = await db.Topics.FindAsync(topicId);
        if (topic == null) return;

        // Guard: Quiz akışı (TransitionToNextTopicAsync) zaten CompletedSections'ı artırmış olabilir.
        // Son AI mesajında [TOPIC_COMPLETE:] marker'ı varsa progression zaten tetiklendi demektir.
        var lastAiMsg = await db.Messages
            .Where(m => m.SessionId == sessionId && m.Role == "assistant")
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync();
        if (lastAiMsg?.Content?.Contains("[TOPIC_COMPLETE:") == true)
        {
            _logger.LogInformation("[Auto-Progression] Quiz akışı zaten progression tetikledi. Atlanıyor. TopicId={Id}", topicId);
            return;
        }

        topic.CompletedSections += 1;
        await db.SaveChangesAsync();

        // Hiyerarşi farkında: modül->ders yapısında leaf-level'a iner, düz planda children döner
        var subTopics = await topicService.GetOrderedLessonsAsync(topicId, userId);
        if (topic.CompletedSections < subTopics.Count)
        {
            var nextTopic = subTopics[topic.CompletedSections];
            _logger.LogInformation("[Auto-Progression] Otomatik ders geçişi. Yeni konu: {Topic}", nextTopic.Title);

            var curriculumTitles = subTopics.Select(t => t.Title).ToList();
            var autoLesson = await tutorAgent.GetFirstLessonAsync(topic.Title, nextTopic.Title, curriculumTitles);

            var session = await db.Sessions.FindAsync(sessionId);
            if (session != null)
            {
                var autoModel = _agentFactory.GetModel(AgentRole.Tutor);
                var (autoTokens, autoCost) = _tokenEstimator.Estimate(autoModel, string.Empty, autoLesson);

                var autoMsg = new Message
                {
                    Id = Guid.NewGuid(),
                    SessionId = sessionId,
                    UserId = userId,
                    Role = "assistant",
                    Content = autoLesson,
                    ModelUsed = autoModel,
                    TokensUsed = autoTokens,
                    CostUSD = autoCost,
                    CreatedAt = DateTime.UtcNow,
                    MessageType = MessageType.General
                };
                db.Messages.Add(autoMsg);

                session.TotalTokensUsed += autoTokens;
                session.TotalCostUSD    += autoCost;

                await db.SaveChangesAsync();
                _logger.LogInformation("[Auto-Progression] Otomatik AI dersi veritabanına eklendi.");
            }
        }
        else
        {
            _logger.LogInformation("[Auto-Progression] Tüm alt konular bitti, TopicCompletedEvent fırlatılıyor.");
            await mediator.Publish(new Orka.Core.Events.TopicCompletedEvent
            {
                SessionId = sessionId,
                TopicId = topicId,
                UserId = userId
            });
        }
    }
}
