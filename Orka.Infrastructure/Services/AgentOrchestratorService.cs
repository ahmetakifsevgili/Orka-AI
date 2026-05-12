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
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Core.Services;
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
    private readonly IAiRequestContextAccessor _aiRequestContext;
    private readonly ISkillMasteryService _skillMastery;
    private readonly ITokenCostEstimator _tokenEstimator;
    private readonly IAIAgentFactory _agentFactory;
    private readonly IBackgroundTaskQueue _backgroundQueue;
    private readonly IChatMetadataService _chatMetadata;
    private readonly IRuntimeTelemetryService _runtimeTelemetry;
    private readonly IChatTurnPostProcessor _chatTurnPostProcessor;
    private readonly IQuizAttemptRecorder _quizAttemptRecorder;
    private readonly ITopicProgressPropagator _topicProgress;
    private readonly ITopicScopeResolver _topicScopeResolver;
    private readonly ITutorPolicyTraceService? _policyTrace;
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
        IAiRequestContextAccessor aiRequestContext,
        ISkillMasteryService skillMastery,
        ITokenCostEstimator tokenEstimator,
        IAIAgentFactory agentFactory,
        IBackgroundTaskQueue backgroundQueue,
        IChatMetadataService chatMetadata,
        IRuntimeTelemetryService runtimeTelemetry,
        IChatTurnPostProcessor chatTurnPostProcessor,
        IQuizAttemptRecorder quizAttemptRecorder,
        ITopicProgressPropagator topicProgress,
        ITopicScopeResolver topicScopeResolver,
        ILogger<AgentOrchestratorService> logger,
        ITutorPolicyTraceService? policyTrace = null)
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
        _aiRequestContext = aiRequestContext;
        _skillMastery = skillMastery;
        _tokenEstimator = tokenEstimator;
        _agentFactory = agentFactory;
        _backgroundQueue = backgroundQueue;
        _chatMetadata = chatMetadata;
        _runtimeTelemetry = runtimeTelemetry;
        _chatTurnPostProcessor = chatTurnPostProcessor;
        _quizAttemptRecorder = quizAttemptRecorder;
        _topicProgress = topicProgress;
        _topicScopeResolver = topicScopeResolver;
        _policyTrace = policyTrace;
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

    public async IAsyncEnumerable<string> ProcessMessageStreamAsync(Guid userId, string content, Guid? topicId, Guid? sessionId, bool isPlanMode = false, Guid? focusTopicId = null, string? focusTopicPath = null, string? focusSourceRef = null)
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

        using var aiContext = _aiRequestContext.Push(new AiRequestContext(
            UserId: userId,
            SessionId: session.Id,
            TopicId: session.TopicId,
            CorrelationId: _correlationContext.CorrelationId,
            Source: nameof(ProcessMessageStreamAsync)));

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
            if (session.CurrentState == SessionState.Learning &&
                !string.IsNullOrWhiteSpace(session.PendingQuiz))
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
                thinkingHint = "[THINKING: Quiz cevabı değerlendiriliyor ve kişisel müfredat oluşturuluyor...]";
            else if (session.CurrentState == SessionState.QuizMode)
                thinkingHint = "[THINKING: Cevabın analiz ediliyor...]";
            else if (isPlanMode || content.Contains("/plan", StringComparison.OrdinalIgnoreCase))
                thinkingHint = "[THINKING: Konu araştırılıyor ve seviye tespiti başlatılıyor...]";
            else
                thinkingHint = "[THINKING: Yanıt hazırlanıyor...]";

            yield return thinkingHint;

            if (isBaselineMode)
                yield return "[THINKING: Kişisel öğrenme planı derleniyor...]";

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
                    syncResponse = await HandleLearningStateAsync(userId, content, session);
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
                await ScheduleTurnPostProcessingAsync(
                    session,
                    userId,
                    content,
                    syncResponse,
                    syncMsgId,
                    syncAgentRole,
                    entryState,
                    isStream: true);
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
            await ScheduleTurnPostProcessingAsync(
                session,
                userId,
                content,
                fullResponse,
                msgId,
                "TutorAgent",
                session.CurrentState,
                isStream: true);
            var metadata = await BuildTutorMetadataAsync(userId, session, fullResponse);
            yield return System.Text.Json.JsonSerializer.Serialize(new
            {
                type = "metadata",
                data = new { metadata }
            });
        }
    }

    private async Task<string> BuildFocusedTutorContentAsync(
        string content,
        Guid? focusTopicId,
        string? focusTopicPath,
        string? focusSourceRef)
    {
        if (!focusTopicId.HasValue &&
            string.IsNullOrWhiteSpace(focusTopicPath) &&
            string.IsNullOrWhiteSpace(focusSourceRef))
        {
            return content;
        }

        string? focusTopicTitle = null;
        if (focusTopicId.HasValue)
        {
            focusTopicTitle = await _db.Topics
                .Where(t => t.Id == focusTopicId.Value)
                .Select(t => t.Title)
                .FirstOrDefaultAsync();
        }

        _logger.LogInformation(
            "[Orchestrator] ORKA_CONTEXT focus injected. FocusTopicId={FocusTopicId} FocusTopicPath={FocusTopicPath} FocusSourceRef={FocusSourceRef}",
            focusTopicId,
            focusTopicPath,
            focusSourceRef);

        var lines = new List<string>
        {
            "[ORKA_CONTEXT]",
            "Tutor bu istekte kullanıcının aktif odak bağlamını korumalıdır."
        };

        if (focusTopicId.HasValue)
            lines.Add($"FocusTopicId: {focusTopicId.Value}");
        if (!string.IsNullOrWhiteSpace(focusTopicTitle))
            lines.Add($"FocusTopicTitle: {focusTopicTitle}");
        if (!string.IsNullOrWhiteSpace(focusTopicPath))
            lines.Add($"FocusTopicPath: {focusTopicPath}");
        if (!string.IsNullOrWhiteSpace(focusSourceRef))
            lines.Add($"FocusSourceRef: {focusSourceRef}");

        lines.Add("[USER_MESSAGE]");
        lines.Add(content);
        return string.Join(Environment.NewLine, lines);
    }

    private static bool LooksLikeResearchRequest(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;

        var lower = content.ToLowerInvariant();
        return lower.Contains("araştır") ||
               lower.Contains("arastir") ||
               lower.Contains("kaynaklı") ||
               lower.Contains("kaynakli") ||
               lower.Contains("güncel") ||
               lower.Contains("guncel") ||
               lower.Contains("internetten") ||
               lower.Contains("webden") ||
               lower.Contains("web'den") ||
               lower.Contains("google");
    }

    // -- Yardımcı: AI mesajını DB'ye kaydet ----------------------------------
    private async Task<Guid> SaveAiMessage(Session session, Guid userId, string content)
    {
        var message = await SaveAiMessageEntityAsync(session, userId, content);
        return message.Id;
    }

    private async Task<Message> SaveAiMessageEntityAsync(Session session, Guid userId, string content)
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
                           (content.Contains("[{") && content.Contains("\"question\"")))
                          ? MessageType.Quiz : MessageType.General
        };
        _db.Messages.Add(aiMsg);
        session.Messages ??= new List<Message>();
        session.Messages.Add(aiMsg);

        // Session toplamlarını güncelle (Dashboard maliyet widget'ı için)
        session.TotalTokensUsed += tokens;
        session.TotalCostUSD    += cost;

        await _db.SaveChangesAsync();
        await RecordCostSafeAsync(userId, session.Id, session.TopicId, aiMsg.Id, "Tutor", "AIAgentFactory", model, tokens, cost);
        return aiMsg;
    }

    private async Task RecordCostSafeAsync(
        Guid userId,
        Guid sessionId,
        Guid? topicId,
        Guid messageId,
        string agentRole,
        string provider,
        string? model,
        int tokens,
        decimal cost)
    {
        var alreadyRecorded = await _db.CostRecords.AnyAsync(c =>
            c.MessageId == messageId &&
            c.AgentRole == agentRole &&
            c.UserId == userId);
        if (alreadyRecorded)
            return;

        await _runtimeTelemetry.RecordCostAsync(new CostRecordRequest(
            UserId: userId,
            SessionId: sessionId,
            TopicId: topicId,
            MessageId: messageId,
            AgentRole: agentRole,
            Provider: provider,
            Model: model,
            EstimatedTokens: tokens,
            EstimatedCostUsd: cost,
            Success: true,
            ErrorCode: null,
            MetadataJson: null));
    }

    private async ValueTask ScheduleTurnPostProcessingAsync(
        Session session,
        Guid userId,
        string userContent,
        string assistantContent,
        Guid assistantMessageId,
        string agentRole,
        SessionState entryState,
        bool isStream)
    {
        try
        {
            await _chatTurnPostProcessor.ScheduleAsync(new ChatTurnPostProcessRequest(
                UserId: userId,
                SessionId: session.Id,
                TopicId: session.TopicId,
                AssistantMessageId: assistantMessageId,
                UserContent: userContent,
                AssistantContent: assistantContent,
                AgentRole: agentRole,
                CorrelationId: _correlationContext.CorrelationId,
                EntryState: entryState,
                IsStream: isStream));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[ChatPostProcess] Schedule skipped. UserId={UserId} SessionId={SessionId} MessageId={MessageId} AgentRole={AgentRole} IsStream={IsStream}",
                userId,
                session.Id,
                assistantMessageId,
                agentRole,
                isStream);
        }
    }

    public async Task<ChatMessageResponse> ProcessMessageAsync(Guid userId, string content, Guid? topicId, Guid? sessionId, bool isPlanMode = false, Guid? focusTopicId = null, string? focusTopicPath = null, string? focusSourceRef = null)
    {
        Session? session = await GetOrCreateSessionAsync(userId, topicId, sessionId, content);
        if (session == null) throw new Exception("Oturum oluşturulamadı veya SmallTalk.");

        using var aiContext = _aiRequestContext.Push(new AiRequestContext(
            UserId: userId,
            SessionId: session.Id,
            TopicId: session.TopicId,
            CorrelationId: _correlationContext.CorrelationId,
            Source: nameof(ProcessMessageAsync)));

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
        var entryState = session.CurrentState;
        string aiResponse;
        bool wikiUpdated = false;
        bool skipAutoWiki = false;
        bool planCreated = false;
        Guid? activeWikiPageId = null;

        if (isPlanMode)
        {
            var result = await TriggerBaselineQuizForPlanAsync(userId, content, session);
            aiResponse = result.Response;
            skipAutoWiki = true;
        }
        else if (isNewTopic && session.CurrentState == SessionState.Learning)
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
            var actionRoute = LooksLikeResearchRequest(content)
                ? "RESEARCH"
                : await _supervisor.DetermineActionRouteAsync(content, session.Messages);

            _logger.LogInformation("[Orchestrator] Supervisor Route Kararı: {Route}", actionRoute);

            if (actionRoute == "RESEARCH")
            {
                using var researchScope = _scopeFactory.CreateScope();
                var korteks = researchScope.ServiceProvider.GetRequiredService<IKorteksAgent>();

                string researchContext;
                try
                {
                    var researchResult = await korteks.RunResearchWithEvidenceAsync(content, userId, session.TopicId);
                    researchContext = KorteksResearchContextFormatter.FormatForTutor(researchResult);

                    _logger.LogInformation(
                        "[Orchestrator] Research route completed. GroundingMode={GroundingMode}, IsFallback={IsFallback}, SourceCount={SourceCount}, ToolCallCount={ToolCallCount}",
                        researchResult.GroundingMode,
                        researchResult.IsFallback,
                        researchResult.SourceCount,
                        researchResult.ProviderCalls.Count(c => c.Invoked));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Orchestrator] Research route failed; Tutor will answer with explicit source limits.");
                    researchContext = "[KORTEKS SOURCE-GROUNDING]\nStatus: failed\nFallback: true\nGroundingWarning: Source-backed research could not be completed. Tell the user that external source grounding is unavailable for this answer.";
                }

                var enrichedContent = $"[KORTEKS ARAŞTIRMA SONUÇLARI]:{Environment.NewLine}{researchContext}{Environment.NewLine}{Environment.NewLine}[KULLANICI MESAJI]:{Environment.NewLine}{tutorContent}";
                aiResponse = await _tutorAgent.GetResponseAsync(userId, enrichedContent, session, false)
                    ?? "Araştırma tamamlandı ancak Tutor yanıtı üretilemedi.";
            }
            else if (actionRoute == "QUIZ" && session.CurrentState == SessionState.Learning)
            {
                session.CurrentState = SessionState.QuizPending;
                aiResponse = await HandleLearningStateAsync(userId, tutorContent, session);
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
        }

        // 3. SAVE AI MESSAGE
        var aiMsg = await SaveAiMessageEntityAsync(session, userId, aiResponse);
        bool isQuizMessage = aiMsg.MessageType == MessageType.Quiz;

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

        var responseAgentRole = entryState switch
        {
            SessionState.QuizMode => "GraderAgent",
            SessionState.BaselineQuizMode => "DeepPlanAgent",
            SessionState.AwaitingChoice => "TutorAgent",
            _ => isPlanMode ? "DeepPlanAgent" : "TutorAgent"
        };
        await ScheduleTurnPostProcessingAsync(
            session,
            userId,
            content,
            aiResponse,
            aiMsg.Id,
            responseAgentRole,
            entryState,
            isStream: false);

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
            TopicTitle  = isNewTopic ? (await _db.Topics.FindAsync(session.TopicId))?.Title : null,
            Metadata    = await BuildTutorMetadataAsync(userId, session, aiResponse)
        };
    }

    private async Task<ChatResponseMetadata> BuildTutorMetadataAsync(
        Guid userId,
        Session session,
        string aiResponse)
    {
        var metadata = _chatMetadata.Build(aiResponse);
        if (_policyTrace != null)
        {
            var traces = await _policyTrace.GetRecentAsync(userId, session.TopicId, session.Id, 1);
            var trace = traces.FirstOrDefault();
            if (trace != null)
            {
                metadata.TutorPolicyTraceId = trace.Id;
                metadata.ActiveConceptKey = trace.ActiveConceptKey;
                metadata.NextPedagogicalMove = trace.SelectedPedagogicalMove;
                metadata.GroundingStatus = trace.GroundingStatus;
            }
        }

        var actionTrace = await _db.TutorActionTraces
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.SessionId == session.Id)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        if (actionTrace != null)
        {
            metadata.TutorActionTraceId = actionTrace.Id;
            metadata.TeachingMode = actionTrace.TeachingMode;
            metadata.StyleMode = actionTrace.StyleMode;
            metadata.ActiveConceptKey = string.IsNullOrWhiteSpace(metadata.ActiveConceptKey)
                ? actionTrace.ActiveConceptKey
                : metadata.ActiveConceptKey;
            metadata.NextCheckPrompt = actionTrace.NextCheckPrompt;
        }

        TutorTurnStateDto? turnStateDto = null;
        var turnState = await _db.TutorTurnStates
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.SessionId == session.Id)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        if (turnState != null)
        {
            metadata.TutorTurnStateId = turnState.Id;
            metadata.TutorWorkingMemorySnapshotId = turnState.WorkingMemorySnapshotId;
            metadata.StyleMode ??= turnState.StyleMode;
            metadata.AffectiveState = turnState.AffectiveState;
            metadata.CognitiveLoad = turnState.CognitiveLoad;
            metadata.GroundingStatus ??= turnState.GroundingStatus;

            try
            {
                turnStateDto = System.Text.Json.JsonSerializer.Deserialize<TutorTurnStateDto>(turnState.StateJson);
                metadata.MasteryProbability = turnStateDto?.MasteryProbability;
                metadata.Confidence = turnStateDto?.Confidence;
            }
            catch
            {
                // Metadata enrichment must never block chat response.
            }
        }

        if (actionTrace != null)
        {
            var toolCalls = await _db.TutorToolCalls
                .AsNoTracking()
                .Where(t => t.TutorActionTraceId == actionTrace.Id)
                .OrderBy(t => t.CreatedAt)
                .ToListAsync();
            metadata.ToolCallIds = toolCalls.Select(t => t.Id).ToArray();
            metadata.ToolStatuses = toolCalls
                .Select(t => new Orka.Core.DTOs.Chat.ToolStatusDto(
                    t.Id,
                    t.ToolId,
                    t.Status,
                    t.Success,
                    t.Provider,
                    t.SafeMessage,
                    t.ErrorCode,
                    t.Confidence,
                    t.SourceCount))
                .ToArray();
            metadata.UsedTools = toolCalls
                .Where(t => t.Success && string.Equals(t.Status, "ready", StringComparison.OrdinalIgnoreCase))
                .Select(t => new UsedToolDto(
                    Name: t.ToolId,
                    Status: t.Status,
                    Evidence: t.Evidence,
                    FallbackReason: t.FallbackReason,
                    ToolId: t.ToolId,
                    Success: t.Success,
                    FallbackUsed: !string.IsNullOrWhiteSpace(t.FallbackReason),
                    Provider: t.Provider,
                    LatencyMs: t.LatencyMs,
                    SourceConfidence: t.Confidence,
                    ErrorCode: t.ErrorCode,
                    SafeMessage: t.SafeMessage,
                    Timestamp: t.CreatedAt))
                .ToArray();

            var artifacts = await _db.TeachingArtifacts
                .AsNoTracking()
                .Where(a => a.TutorActionTraceId == actionTrace.Id)
                .OrderBy(a => a.CreatedAt)
                .ToListAsync();
            metadata.ArtifactIds = artifacts.Select(a => a.Id).ToList();
            metadata.ArtifactSummaries = artifacts
                .Select(a => new Orka.Core.DTOs.Chat.ArtifactSummaryDto(a.Id, a.ArtifactType, a.Title, a.Status, a.RenderFormat, a.Provider, a.ExternalUrl))
                .ToArray();
            metadata.EvidenceSummary = new Orka.Core.DTOs.Chat.EvidenceSummaryDto(
                ReadyToolCount: toolCalls.Count(t => t.Success && t.Status == "ready"),
                SourceCount: toolCalls.Where(t => t.Success).Sum(t => t.SourceCount ?? 0),
                GroundingStatus: metadata.GroundingStatus ?? metadata.GroundingMode,
                LearnerEvidenceStatus: metadata.Confidence.HasValue && metadata.Confidence.Value >= 0.60m ? "sufficient" : "evidence_insufficient");

            metadata.PolicyViolationCount = await _db.TutorPolicyViolationsV2
                .AsNoTracking()
                .CountAsync(v => v.TutorActionTraceId == actionTrace.Id);

            var pedagogy = await _db.TutorPedagogyEvaluationRuns
                .AsNoTracking()
                .Where(e => e.TutorActionTraceId == actionTrace.Id)
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefaultAsync();
            if (pedagogy != null)
            {
                metadata.TutorPedagogyEvaluationRunId = pedagogy.Id;
                metadata.TutorPedagogyStatus = pedagogy.Status;
                metadata.TutorPedagogyScore = pedagogy.OverallScore;
                metadata.PedagogyWarnings = await _db.TutorPedagogyRubricScores
                    .AsNoTracking()
                    .Where(s => s.EvaluationRunId == pedagogy.Id && (s.IsCritical || s.Score < 0.60m))
                    .OrderBy(s => s.RubricKey)
                    .Select(s => s.Recommendation)
                    .ToArrayAsync();
            }
        }

        await EnrichEvidenceQualityAsync(metadata, userId, session.TopicId);
        EnrichTutorResponsePolicy(metadata, turnStateDto, actionTrace);
        return metadata;
    }

    private static void EnrichTutorResponsePolicy(ChatResponseMetadata metadata, TutorTurnStateDto? turnState, TutorActionTrace? actionTrace)
    {
        if (turnState != null)
        {
            turnState.EvidenceQuality = metadata.EvidenceQuality ?? turnState.EvidenceQuality;
            var decision = TutorResponsePolicy.Decide(turnState);
            metadata.TutorResponseMode ??= decision.TutorResponseMode;
            metadata.EvidencePolicy ??= decision.EvidencePolicy;
            metadata.PersonalizationMode ??= decision.PersonalizationMode;
            metadata.MasteryBasis ??= decision.MasteryBasis;
            metadata.WeakConceptHints = metadata.WeakConceptHints.Count > 0 ? metadata.WeakConceptHints : decision.WeakConceptHints;
        }
        else
        {
            metadata.EvidencePolicy ??= TutorResponsePolicy.EvidencePolicyFor(
                metadata.EvidenceQuality,
                metadata.GroundingStatus ?? metadata.GroundingMode,
                metadata.EvidenceSummary?.SourceCount ?? 0);
            metadata.PersonalizationMode ??= TutorResponsePolicy.PersonalizationModeFor(metadata.MasteryProbability, metadata.Confidence);
            metadata.MasteryBasis ??= metadata.Confidence < 0.45m ? "low_confidence" :
                metadata.MasteryProbability.HasValue ? "mastery_probability" : "default";
            metadata.TutorResponseMode ??= TutorResponsePolicy.ResponseModeFor(
                string.Empty,
                metadata.EvidenceQuality,
                metadata.MasteryProbability,
                metadata.Confidence,
                metadata.EvidenceSummary?.LearnerEvidenceStatus,
                directAnswerRisk: false);
        }

        var evidenceStatus = metadata.EvidenceQuality?.Status;
        if (TutorResponsePolicy.ShouldSuppressNextCheck(metadata.ProviderWarnings, metadata.FallbackReason, metadata.TutorResponseMode, evidenceStatus))
        {
            metadata.NextCheckPrompt = null;
        }
        else if (actionTrace != null && string.IsNullOrWhiteSpace(metadata.NextCheckPrompt))
        {
            metadata.NextCheckPrompt = actionTrace.NextCheckPrompt;
        }
    }

    private async Task EnrichEvidenceQualityAsync(ChatResponseMetadata metadata, Guid userId, Guid? topicId)
    {
        if (!topicId.HasValue)
        {
            metadata.EvidenceQuality ??= EvidenceQualityEvaluator.Unknown();
            return;
        }

        try
        {
            var topicScope = await _topicScopeResolver.ResolveAsync(userId, topicId.Value);
            var scopedTopicIds = topicScope.IsValid
                ? new[] { topicScope.CurrentTopicId }
                    .Concat(topicScope.AncestorTopicIds)
                    .Concat(topicScope.DescendantTopicIds)
                    .Distinct()
                    .ToArray()
                : new[] { topicId.Value };

            var sourceStats = await _db.LearningSources
                .AsNoTracking()
                .Where(s => s.UserId == userId && !s.IsDeleted && s.TopicId.HasValue && scopedTopicIds.Contains(s.TopicId.Value))
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    SourceCount = g.Count(),
                    ReadySourceCount = g.Count(s => s.Status == "ready")
                })
                .FirstOrDefaultAsync();
            var sourceCount = sourceStats?.SourceCount ?? 0;
            var readySourceCount = sourceStats?.ReadySourceCount ?? 0;

            var recentRuns = await _db.SourceRetrievalRuns
                .AsNoTracking()
                .Where(r => r.UserId == userId && r.TopicId.HasValue && scopedTopicIds.Contains(r.TopicId.Value))
                .OrderByDescending(r => r.CreatedAt)
                .Take(30)
                .Select(r => new { r.RetrievedCount, r.IsEmpty, r.QualityStatus })
                .ToListAsync();
            var recentChecks = await _db.SourceCitationChecks
                .AsNoTracking()
                .Where(c => c.UserId == userId && c.TopicId.HasValue && scopedTopicIds.Contains(c.TopicId.Value))
                .OrderByDescending(c => c.CreatedAt)
                .Take(50)
                .Select(c => c.CheckStatus)
                .ToListAsync();

            var runCount = recentRuns.Count;
            var emptyCount = recentRuns.Count(r => r.IsEmpty || r.QualityStatus is "source_retrieval_empty" or "empty");
            var unsupported = recentChecks.Count(s => s == "citation_unsupported");
            var missing = recentChecks.Count(s => s == "citation_missing");
            var supported = recentChecks.Count(s => s == "supported");
            var checkCount = recentChecks.Count;
            var citationCoverage = checkCount == 0 ? 0m : Math.Round(supported / (decimal)checkCount, 4);
            var retrievalHealth = runCount == 0 ? "unverified" :
                emptyCount == runCount ? "source_retrieval_empty" :
                recentRuns.Any(r => r.QualityStatus == "low_confidence") ? "low_confidence" :
                recentRuns.Any(r => r.QualityStatus == "degraded") ? "degraded" :
                "healthy";
            var citationStatus = checkCount == 0 ? "unverified" :
                missing > 0 ? "citation_missing" :
                unsupported > 0 ? "citation_unsupported" :
                "healthy";

            metadata.EvidenceQuality = EvidenceQualityEvaluator.Build(
                sourceCount,
                readySourceCount,
                recentRuns.Sum(r => r.RetrievedCount),
                citationCoverage,
                unsupported,
                missing,
                retrievalHealth,
                citationStatus);
            metadata.RagQualityStatus ??= metadata.EvidenceQuality.Status is "strong" ? "healthy" :
                metadata.EvidenceQuality.Status is "weak" or "missing" ? "degraded" :
                metadata.EvidenceQuality.Status;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[ChatMetadata] Evidence quality enrichment skipped. UserId={UserId} TopicId={TopicId}", userId, topicId);
            metadata.EvidenceQuality ??= EvidenceQualityEvaluator.Unknown();
        }
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
                topic.PlanIntent ??= "Core";
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
                var wrongAnswerTopic = session.TopicId.HasValue
                    ? await _db.Topics.FirstOrDefaultAsync(t => t.Id == session.TopicId.Value && t.UserId == userId)
                    : null;
                var wrongAnswerActiveSubTopic = await ResolveActiveQuizTopicAsync(userId, session, wrongAnswerTopic);
                if (wrongAnswerActiveSubTopic != null)
                {
                    await RecordChatQuizAttemptIfNeededAsync(
                        userId,
                        session,
                        wrongAnswerActiveSubTopic,
                        content,
                        score: 0,
                        total: 1,
                        isSkipped: false,
                        isIdeSubmission: false);
                }

                return ($"Hmm, tam olarak değil. Tekrar deneyelim:\n\n**{session.PendingQuiz}**", false);
            }
            score = 1;
            total = 1;
        }

        // -- PROCESS RESULTS & TRANSITION ----------------------------------
        var currentTopic = session.TopicId.HasValue
            ? await _db.Topics.FirstOrDefaultAsync(t => t.Id == session.TopicId.Value && t.UserId == userId)
            : null;
        var activeSubTopic = await ResolveActiveQuizTopicAsync(userId, session, currentTopic);

        if (activeSubTopic != null)
        {
            var attemptResult = await RecordChatQuizAttemptIfNeededAsync(
                userId,
                session,
                activeSubTopic,
                content,
                score,
                total,
                isSkipped,
                isIDESubmission);
            if (attemptResult.WasDuplicate)
            {
                session.CurrentState = SessionState.Learning;
                session.PendingQuiz = null;
                await _db.SaveChangesAsync();
                return ("Bu quiz cevabı zaten kaydedilmiş. İlerleme tekrar güncellenmedi.", false);
            }

            var quizPercentageForAdvance = total > 0 ? (double)score / total : 0;
            var currentCalculatedScore = total > 0 ? (int)Math.Round(quizPercentageForAdvance * 100) : 0;

            if (!isSkipped && quizPercentageForAdvance < 0.6)
            {
                session.RemedialAttemptCount++;
                activeSubTopic.ProgressPercentage = Math.Max(activeSubTopic.ProgressPercentage, quizPercentageForAdvance * 100);
                activeSubTopic.IsMastered = false;

                var remedialLesson = await GenerateRemedialLessonAsync(userId, session, activeSubTopic, content, score, total);
                session.CurrentState = SessionState.Learning;
                session.PendingQuiz = null;
                await _db.SaveChangesAsync();

                return ($"Skorun **{score}/{total}**. Bu konuyu henüz tamamlandı saymıyorum; aşağıdaki kısa telafi dersi zayıf becerilerine odaklanıyor.\n\n{remedialLesson}", false);
            }

            if (!isSkipped)
            {
                activeSubTopic.SuccessScore = Math.Max(activeSubTopic.SuccessScore, currentCalculatedScore);
                session.RemedialAttemptCount = 0;
                var user = await _db.Users.FindAsync(userId);
                if (user != null) user.LastActiveDate = DateTime.UtcNow;
            }

            await _topicProgress.PropagateLessonCompletionAsync(
                userId,
                activeSubTopic.Id,
                isSkipped ? null : currentCalculatedScore,
                !isSkipped && quizPercentageForAdvance >= 0.6);

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

    private async Task<Topic?> ResolveActiveQuizTopicAsync(Guid userId, Session session, Topic? currentTopic)
    {
        if (currentTopic == null)
            return null;

        var scope = await _topicScopeResolver.ResolveAsync(userId, currentTopic.Id);
        var activeTopicId = scope.IsValid
            ? scope.ActiveLessonTopicId ?? currentTopic.Id
            : currentTopic.Id;

        return await _db.Topics
            .FirstOrDefaultAsync(t => t.Id == activeTopicId && t.UserId == userId);
    }

    private sealed record ChatQuizAttemptRecordResult(bool WasDuplicate);

    private async Task<ChatQuizAttemptRecordResult> RecordChatQuizAttemptIfNeededAsync(
        Guid userId,
        Session session,
        Topic quizTopic,
        string content,
        int score,
        int total,
        bool isSkipped,
        bool isIdeSubmission)
    {
        var pendingQuiz = session.PendingQuiz ?? quizTopic.Title;
        var normalizedAnswer = isSkipped ? "skip" : content.Trim();
        var questionHash = BuildChatQuizQuestionHash(session.Id, quizTopic.Id, pendingQuiz, normalizedAnswer);

        var sameQuestionAnswerExists = await _db.QuizAttempts.AnyAsync(a =>
            a.UserId == userId &&
            a.SessionId == session.Id &&
            a.Question == pendingQuiz &&
            a.UserAnswer == normalizedAnswer);
        if (sameQuestionAnswerExists)
            return new ChatQuizAttemptRecordResult(true);

        var exists = await _db.QuizAttempts.AnyAsync(a =>
            a.UserId == userId &&
            a.TopicId == quizTopic.Id &&
            a.SessionId == session.Id &&
            a.QuestionHash == questionHash);
        if (exists)
            return new ChatQuizAttemptRecordResult(true);

        var scorePercent = total > 0 ? (int)Math.Round((double)score / total * 100) : 0;
        var isCorrect = !isSkipped && total > 0 && (double)score / total >= 0.6;
        var topicPath = await BuildTopicPathAsync(userId, quizTopic.Id);
        var sourceRefsJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            origin = "chat-quiz",
            sessionId = session.Id,
            topicId = quizTopic.Id,
            score,
            total,
            scorePercent,
            wasSkipped = isSkipped,
            isIdeSubmission
        });

        await _quizAttemptRecorder.RecordAsync(userId, new RecordQuizAttemptRequest
        {
            TopicId = quizTopic.Id,
            SessionId = session.Id,
            MessageId = session.Id.ToString("D"),
            QuestionId = $"chat-{quizTopic.Id:N}",
            Question = pendingQuiz,
            SelectedOptionId = normalizedAnswer,
            IsCorrect = isCorrect,
            Explanation = isSkipped
                ? "Chat quiz skipped."
                : $"Chat quiz score: {score}/{total}.",
            SkillTag = quizTopic.Title,
            ConceptTag = quizTopic.Title,
            LearningObjective = quizTopic.Title,
            TopicPath = topicPath,
            Difficulty = "chat",
            CognitiveType = isIdeSubmission ? "code" : "mixed",
            QuestionHash = questionHash,
            SourceRefsJson = sourceRefsJson,
            WasSkipped = isSkipped
        });

        return new ChatQuizAttemptRecordResult(false);
    }

    private async Task<string> BuildTopicPathAsync(Guid userId, Guid topicId)
    {
        var topics = await _db.Topics
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => new { t.Id, t.ParentTopicId, t.Title })
            .ToListAsync();
        var byId = topics.ToDictionary(t => t.Id);
        var titles = new List<string>();
        var seen = new HashSet<Guid>();
        var cursor = topicId;

        while (byId.TryGetValue(cursor, out var topic) && seen.Add(cursor))
        {
            titles.Add(topic.Title);
            if (!topic.ParentTopicId.HasValue)
                break;
            cursor = topic.ParentTopicId.Value;
        }

        titles.Reverse();
        return titles.Count == 0 ? "Chat Quiz" : string.Join(" / ", titles);
    }

    private static string BuildChatQuizQuestionHash(
        Guid sessionId,
        Guid topicId,
        string question,
        string answer)
    {
        var raw = $"chat:{sessionId:N}:{topicId:N}:{NormalizeHashPart(question)}:{NormalizeHashPart(answer)}";
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return "chat:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string NormalizeHashPart(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(' ', value.Trim().ToLowerInvariant().Split(
                Array.Empty<char>(),
                StringSplitOptions.RemoveEmptyEntries));

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

        // Modül -> Ders hiyerarşisini veritabanından çek
        var modules = await _db.Topics
            .Where(t => t.ParentTopicId == topic.Id)
            .OrderBy(t => t.Order)
            .ToListAsync();

        // Her modülün altındaki dersleri çek
        var moduleIds = modules.Select(m => m.Id).ToList();
        var lessonsByModule = await _db.Topics
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
    private static string ExtractQuizQuestionText(string response)
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
        catch { return response; }
    }

    /// <summary>
    /// 5 soruluk JSON array'den N'inci soruyu (0-tabanlı) tek JSON nesnesi olarak döndürür.
    /// Frontend'in quiz kartı olarak render edebilmesi için ```quiz ``` bloğu ile sarılır.
    /// </summary>
    private static string ExtractNthQuizFromArray(string allQuizJson, int index)
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
        }
        catch { /* fallback */ }

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

        int completedCount = siblings.Count(t => t.ProgressPercentage >= 100 || t.IsMastered);
        int completedIndex = Math.Clamp(completedCount - 1, 0, siblings.Count - 1);

        if (completedCount >= siblings.Count)
        {
            // Tüm alt konular bitti
            currentTopic.IsMastered = true;
            _logger.LogInformation("[TRANSITION] Tüm alt konular tamamlandı. TopicId={Id}", currentTopic.Id);
            session.CurrentState = SessionState.Learning;
            session.PendingQuiz = null;
            await _db.SaveChangesAsync();

            var allDoneSubtopicId = siblings[completedIndex].Id;

            return ($" **Harika!** Tüm alt konuları başarıyla tamamladın! Artık ana konunun uzmanısın.[TOPIC_COMPLETE:{allDoneSubtopicId}]", true);
        }

        // Sıradaki konuya geç
        var completedSubtopic = siblings[completedIndex];
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
        var nextTopic = siblings[completedCount];
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
            Metadata    = _chatMetadata.Build(quizResponse.Response)
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
        topic.PlanIntent ??= "Core";
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
        await _topicProgress.HandleCompletionAnalysisProgressionAsync(userId, sessionId, topicId);
    }
}
