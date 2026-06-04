using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
    private readonly IWikiLearningTraceWriter? _wikiTraceWriter;
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
        ITutorPolicyTraceService? policyTrace = null,
        IWikiLearningTraceWriter? wikiTraceWriter = null)
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
        _wikiTraceWriter = wikiTraceWriter;
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
                        _logger.LogInformation("[Orchestrator] EndSession Wiki uretimi tamamlandi. SessionRef={SessionRef}",
                            LogPrivacyGuard.SafeId(capturedSessionId, "session"));
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
        string? focusSourceRef = null,
        [EnumeratorCancellation] CancellationToken ct = default)
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
            .FirstOrDefaultAsync(s => s.Id == sessionId.Value && s.UserId == userId, ct);

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
            await _db.SaveChangesAsync(ct);
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
                _logger.LogError("[STREAM] Senkron routing hatasi. ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeExceptionType(ex));
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

                var metadata = await BuildTutorMetadataAsync(userId, session, syncResponse);
                var isQuizResponse = syncResponse.Contains("```json") || syncResponse.Contains("```quiz") ||
                                     (syncResponse.Contains("[{") && syncResponse.Contains("\"question\""));
                var msgType = isQuizResponse ? MessageType.Quiz : MessageType.General;

                await AppendTutorTurnToWikiAsync(userId, session, content, syncResponse, syncMsgId, metadata, syncAgentRole, msgType);

                yield return syncResponse;
                yield return System.Text.Json.JsonSerializer.Serialize(new
                {
                    type = "metadata",
                    data = new { metadata }
                });
                yield break;
            }

            yield return syncResponse;
            yield break;
        }

        // 2B. Dinamik Yönlendirme (Supervisor Intent Check)
        var actionRoute = LooksLikeResearchRequest(content)
            ? "RESEARCH"
            : await _supervisor.DetermineActionRouteAsync(content, session.Messages);
        _logger.LogInformation("[Orchestrator] Supervisor Route Kararı: {Route}", actionRoute);

        if (actionRoute == "QUIZ" && session.CurrentState == SessionState.Learning)
        {
            _logger.LogInformation("[Orchestrator] Kullanıcı organik olarak quiz talep etti. Durum QuizPending'e çekiliyor.");
            session.CurrentState = SessionState.QuizPending;
        }

        string streamingContent = tutorContent;
        if (actionRoute == "RESEARCH")
        {
            using var researchScope = _scopeFactory.CreateScope();
            var korteks = researchScope.ServiceProvider.GetRequiredService<IKorteksAgent>();
            var synthesisService = researchScope.ServiceProvider.GetService<IKorteksSynthesisService>();

            string researchContext;
            try
            {
                var researchResult = await korteks.RunResearchWithEvidenceAsync(content, userId, session.TopicId);
                var synthesis = synthesisService == null
                    ? null
                    : await synthesisService.BuildAndSaveAsync(
                        userId,
                        researchResult,
                        new Orka.Core.DTOs.Korteks.KorteksResearchSynthesisContextDto
                        {
                            TopicId = session.TopicId,
                            SessionId = session.Id,
                            Purpose = "tutor_research_route"
                        });
                researchContext = synthesis?.ConsumerContexts.Tutor.PromptBlock
                                  ?? KorteksResearchContextFormatter.FormatForTutor(researchResult);

                _logger.LogInformation(
                    "[Orchestrator] Research route completed. GroundingMode={GroundingMode}, IsFallback={IsFallback}, SourceCount={SourceCount}, ToolCallCount={ToolCallCount}, SynthesisWorkflowRef={SynthesisWorkflowRef}",
                    researchResult.GroundingMode,
                    researchResult.IsFallback,
                    researchResult.SourceCount,
                    researchResult.ProviderCalls.Count(c => c.Invoked),
                    LogPrivacyGuard.SafeId(synthesis?.Id, "workflow"));
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Orchestrator] Research route failed; Tutor will answer with explicit source limits. ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeExceptionType(ex));
                researchContext = "[KORTEKS SOURCE-GROUNDING]\nStatus: failed\nFallback: true\nGroundingWarning: Source-backed research could not be completed. Tell the user that external source grounding is unavailable for this answer.";
            }

            streamingContent = $"[KORTEKS ARAŞTIRMA SONUÇLARI]:{Environment.NewLine}{researchContext}{Environment.NewLine}{Environment.NewLine}[KULLANICI MESAJI]:{Environment.NewLine}{tutorContent}";
        }

        // Varsayılan: Normal ders anlatımı - gerçek zamanlı STREAM
        bool isQuizPending = session.CurrentState == SessionState.QuizPending;
        await foreach (var chunk in _tutorAgent.GetResponseStreamAsync(userId, streamingContent, session, isQuizPending, ct))
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
            await AppendTutorTurnToWikiAsync(userId, session, content, fullResponse, msgId, metadata, "TutorAgent", MessageType.General);
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
            "[Orchestrator] ORKA_CONTEXT focus injected. FocusTopicRef={FocusTopicRef} FocusTopicPathRef={FocusTopicPathRef} FocusSourceRef={FocusSourceRef}",
            LogPrivacyGuard.SafeId(focusTopicId, "topic"),
            LogPrivacyGuard.SafeTextRef(focusTopicPath, "path"),
            LogPrivacyGuard.SafeTextRef(focusSourceRef, "source"));

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

    private ValueTask ScheduleTurnPostProcessingAsync(
        Session session,
        Guid userId,
        string userContent,
        string assistantContent,
        Guid assistantMessageId,
        string agentRole,
        SessionState entryState,
        bool isStream)
    {
        var postProcessor = _chatTurnPostProcessor;
        var correlationId = _correlationContext.CorrelationId;
        var sessionId = session.Id;
        var topicId = session.TopicId;

        _ = _backgroundQueue.QueueAsync(new BackgroundTaskItem(
            JobType: "chat_turn_post_process",
            UserId: userId,
            CorrelationId: correlationId,
            Work: async (token) =>
            {
                await postProcessor.ProcessSynchronouslyAsync(new ChatTurnPostProcessRequest(
                    UserId: userId,
                    SessionId: sessionId,
                    TopicId: topicId,
                    AssistantMessageId: assistantMessageId,
                    UserContent: userContent,
                    AssistantContent: assistantContent,
                    AgentRole: agentRole,
                    CorrelationId: correlationId,
                    EntryState: entryState,
                    IsStream: isStream));
            }
        ));

        return ValueTask.CompletedTask;
    }

    public async Task<ChatMessageResponse> ProcessMessageAsync(Guid userId, string content, Guid? topicId, Guid? sessionId, bool isPlanMode = false, Guid? focusTopicId = null, string? focusTopicPath = null, string? focusSourceRef = null, CancellationToken ct = default)
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
                var synthesisService = researchScope.ServiceProvider.GetService<IKorteksSynthesisService>();

                string researchContext;
                try
                {
                    var researchResult = await korteks.RunResearchWithEvidenceAsync(content, userId, session.TopicId);
                    var synthesis = synthesisService == null
                        ? null
                        : await synthesisService.BuildAndSaveAsync(
                            userId,
                            researchResult,
                            new Orka.Core.DTOs.Korteks.KorteksResearchSynthesisContextDto
                            {
                                TopicId = session.TopicId,
                                SessionId = session.Id,
                                Purpose = "tutor_research_route"
                            });
                    researchContext = synthesis?.ConsumerContexts.Tutor.PromptBlock
                                      ?? KorteksResearchContextFormatter.FormatForTutor(researchResult);

                    _logger.LogInformation(
                        "[Orchestrator] Research route completed. GroundingMode={GroundingMode}, IsFallback={IsFallback}, SourceCount={SourceCount}, ToolCallCount={ToolCallCount}, SynthesisWorkflowRef={SynthesisWorkflowRef}",
                        researchResult.GroundingMode,
                        researchResult.IsFallback,
                        researchResult.SourceCount,
                        researchResult.ProviderCalls.Count(c => c.Invoked),
                        LogPrivacyGuard.SafeId(synthesis?.Id, "workflow"));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("[Orchestrator] Research route failed; Tutor will answer with explicit source limits. ErrorType={ErrorType}",
                        LogPrivacyGuard.SafeExceptionType(ex));
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

        var metadata = await BuildTutorMetadataAsync(userId, session, aiResponse);
        var tutorWikiPageId = await AppendTutorTurnToWikiAsync(userId, session, content, aiResponse, aiMsg.Id, metadata, responseAgentRole, aiMsg.MessageType);
        if (tutorWikiPageId.HasValue)
        {
            wikiUpdated = true;
            activeWikiPageId ??= tutorWikiPageId;
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
            TopicTitle  = isNewTopic ? (await _db.Topics.FindAsync(session.TopicId))?.Title : null,
            Metadata    = metadata
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
                metadata.ActiveLessonSnapshotId = turnStateDto?.ActiveLessonSnapshotId;
                metadata.StudentContextSnapshotId = turnStateDto?.StudentContextSnapshotId;
                metadata.PlanQualitySnapshotId = turnStateDto?.PlanQualitySnapshotId;
                metadata.LessonSnapshotStatus = turnStateDto?.LessonSnapshotStatus;
                metadata.StudentContextConfidenceStatus = turnStateDto?.StudentContextConfidenceStatus;
                metadata.CurrentPlanStepId = turnStateDto?.CurrentPlanStepId;
                metadata.CurrentPlanStepTitle = turnStateDto?.CurrentPlanStepTitle;
                metadata.CurrentPlanTutorMove = turnStateDto?.CurrentPlanTutorMove;
                metadata.CurrentPlanQuizHook = turnStateDto?.CurrentPlanQuizHook;
                metadata.PlanSourceReadiness = turnStateDto?.PlanSourceReadiness;
                metadata.AdaptiveDiagnostic = turnStateDto?.AdaptiveDiagnostic;
                metadata.CoursePlanQuality = turnStateDto?.CoursePlanQuality;
                metadata.MisconceptionSignal ??= turnStateDto?.MisconceptionSignal;
                metadata.LearningSignalConfidence ??= turnStateDto?.LearningSignalConfidence;
                metadata.RemediationSeed ??= turnStateDto?.RemediationSeed;
                metadata.RemediationLesson ??= turnStateDto?.RemediationLesson;
            }
            catch
            {
                // Metadata enrichment must never block chat response.
            }
        }

        if (turnStateDto?.StudentContextSnapshotId.HasValue == true)
        {
            try
            {
                var memoryJson = await _db.StudentContextSnapshots
                    .AsNoTracking()
                    .Where(s => s.Id == turnStateDto.StudentContextSnapshotId.Value && s.UserId == userId && !s.IsDeleted)
                    .Select(s => s.LearningMemoryJson)
                    .FirstOrDefaultAsync();
                metadata.LearningMemoryHygiene = TryReadLearningMemoryHygiene(memoryJson);
            }
            catch
            {
                // Safe memory hygiene metadata must never block chat response.
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

    private async Task<Guid?> AppendTutorTurnToWikiAsync(
        Guid userId,
        Session session,
        string userContent,
        string assistantContent,
        Guid assistantMessageId,
        ChatResponseMetadata metadata,
        string agentRole,
        MessageType messageType)
    {
        if (!session.TopicId.HasValue ||
            !string.Equals(agentRole, "TutorAgent", StringComparison.OrdinalIgnoreCase) ||
            messageType != MessageType.General ||
            string.IsNullOrWhiteSpace(assistantContent))
        {
            return null;
        }

        userContent = ScrubGdprPatterns(userContent);
        assistantContent = ScrubGdprPatterns(assistantContent);

        try
        {
            if (LooksLikeQuizOrRawPayload(assistantContent))
            {
                return null;
            }

            if (metadata.TutorTurnStateId.HasValue)
            {
                var alreadyCaptured = await _db.WikiBlocks.AsNoTracking().AnyAsync(b =>
                    b.TutorTurnStateId == metadata.TutorTurnStateId.Value &&
                    !b.IsDeleted &&
                    !b.WikiPage.IsDeleted &&
                    b.WikiPage.UserId == userId);
                if (alreadyCaptured) return null;
            }

            var topicScope = await _topicScopeResolver.ResolveAsync(userId, session.TopicId.Value);
            if (!topicScope.IsValid) return null;

            var readTopicIds = topicScope.TreeTopicIds.Count > 0
                ? topicScope.TreeTopicIds
                : new[] { session.TopicId.Value };
            var pages = await _db.WikiPages
                .AsNoTracking()
                .Where(p => p.UserId == userId && readTopicIds.Contains(p.TopicId) && !p.IsDeleted)
                .OrderBy(p => p.TopicId == session.TopicId.Value ? 0 : 1)
                .ThenBy(p => p.OrderIndex)
                .ThenBy(p => p.CreatedAt)
                .Take(200)
                .ToListAsync();
            if (pages.Count == 0 && _wikiTraceWriter == null) return null;

            var conceptKey = FirstNonEmpty(
                metadata.ActiveConceptKey,
                metadata.RemediationSeed?.ConceptKey,
                metadata.MisconceptionSignal?.ConceptKey);
            var page = !string.IsNullOrWhiteSpace(conceptKey)
                ? pages.FirstOrDefault(p => string.Equals(p.ConceptKey, conceptKey, StringComparison.OrdinalIgnoreCase))
                : null;
            page ??= pages.FirstOrDefault(p => string.Equals(p.PageType, "topic_root", StringComparison.OrdinalIgnoreCase));
            page ??= pages.FirstOrDefault();
            if (page == null && _wikiTraceWriter == null) return null;
            var activeWikiPageId = page?.Id;

            var blockType = TutorWikiBlockType(metadata);
            var artifactId = metadata.ArtifactIds.FirstOrDefault();
            Guid? capturedPageId = null;

            if (!string.IsNullOrWhiteSpace(userContent) && !LooksLikeQuizOrRawPayload(userContent))
            {
                var questionBlock = _wikiTraceWriter != null
                    ? await _wikiTraceWriter.RecordStudentQuestionAsync(new WikiLearningTraceRequestDto
                    {
                        UserId = userId,
                        TopicId = session.TopicId,
                        SessionId = session.Id,
                        ActiveWikiPageId = activeWikiPageId,
                        ConceptKey = conceptKey,
                        MisconceptionKey = FirstNonEmpty(metadata.MisconceptionSignal?.Category, metadata.RemediationSeed?.MisconceptionCategory),
                        TutorTurnStateId = metadata.TutorTurnStateId,
                        TraceType = "student_question",
                        Title = "Ogrenci sorusu",
                        SafeContent = $"Ogrenci sorusu: {Limit(userContent, 1200)}",
                        SourceBasis = "student_manual",
                        CreatedBy = "student",
                        Visibility = "normal"
                    })
                    : await _wikiService.AddWikiBlockAsync(page!.Id, userId, new CreateWikiBlockRequestDto
                {
                    BlockType = "student_question",
                    Title = "Ogrenci sorusu",
                    Content = $"Ogrenci sorusu: {Limit(userContent, 1200)}",
                    Source = "student",
                    SourceBasis = "model_assisted",
                    ConceptKey = conceptKey,
                    MisconceptionKey = FirstNonEmpty(metadata.MisconceptionSignal?.Category, metadata.RemediationSeed?.MisconceptionCategory),
                    TutorTurnStateId = metadata.TutorTurnStateId,
                    Visibility = "normal"
                });
                capturedPageId = questionBlock?.WikiPageId;
            }

            var block = _wikiTraceWriter != null
                ? await _wikiTraceWriter.RecordTutorExplanationAsync(new WikiLearningTraceRequestDto
                {
                    UserId = userId,
                    TopicId = session.TopicId,
                    SessionId = session.Id,
                    ActiveWikiPageId = activeWikiPageId,
                    ConceptKey = conceptKey,
                    MisconceptionKey = FirstNonEmpty(metadata.MisconceptionSignal?.Category, metadata.RemediationSeed?.MisconceptionCategory),
                    TutorTurnStateId = metadata.TutorTurnStateId,
                    LearningArtifactId = artifactId == Guid.Empty ? null : artifactId,
                    TraceType = blockType,
                    Title = TutorWikiBlockTitle(metadata),
                    SafeContent = BuildTutorWikiBlockContent(userContent, assistantContent, metadata),
                    SourceBasis = TutorWikiSourceBasis(metadata),
                    CreatedBy = "tutor",
                    Visibility = blockType == "repair_note" ? "highlighted" : "normal"
                })
                : await _wikiService.AddWikiBlockAsync(page!.Id, userId, new CreateWikiBlockRequestDto
            {
                BlockType = blockType,
                Title = TutorWikiBlockTitle(metadata),
                Content = BuildTutorWikiBlockContent(userContent, assistantContent, metadata),
                Source = "tutor",
                SourceBasis = TutorWikiSourceBasis(metadata),
                ConceptKey = conceptKey,
                MisconceptionKey = FirstNonEmpty(metadata.MisconceptionSignal?.Category, metadata.RemediationSeed?.MisconceptionCategory),
                TutorTurnStateId = metadata.TutorTurnStateId,
                LearningArtifactId = artifactId == Guid.Empty ? null : artifactId,
                Visibility = blockType == "repair_note" ? "highlighted" : "normal"
            });

            return block?.WikiPageId ?? capturedPageId;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[WikiCapture] Tutor turn could not be appended to Wiki. UserRef={UserRef} SessionRef={SessionRef} MessageRef={MessageRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeId(session.Id, "session"),
                LogPrivacyGuard.SafeId(assistantMessageId, "msg"),
                LogPrivacyGuard.SafeExceptionType(ex));
            return null;
        }
    }

    private static string TutorWikiBlockType(ChatResponseMetadata metadata)
    {
        var move = FirstNonEmpty(metadata.TutorLessonDelivery?.DeliveryMode, metadata.TutorTeachingMove, metadata.TeachingMode, metadata.TutorRemediationPolicy, metadata.NextPedagogicalMove) ?? string.Empty;
        var normalized = move.ToLowerInvariant();
        if (normalized.Contains("repair") || normalized.Contains("remediation") || normalized.Contains("prerequisite"))
            return "repair_note";
        if (normalized.Contains("source"))
            return "source_note";
        if (normalized.Contains("check") || normalized.Contains("retrieval"))
            return "checkpoint";
        if (normalized.Contains("example"))
            return "worked_example";
        return "tutor_explanation";
    }

    private static string TutorWikiBlockTitle(ChatResponseMetadata metadata)
    {
        var move = FirstNonEmpty(metadata.TutorLessonDelivery?.DeliveryMode, metadata.TutorTeachingMove, metadata.TeachingMode, metadata.NextPedagogicalMove);
        return string.IsNullOrWhiteSpace(move)
            ? "Tutor aciklamasi"
            : $"Tutor: {Limit(move, 80)}";
    }

    private static string TutorWikiSourceBasis(ChatResponseMetadata metadata)
    {
        var status = FirstNonEmpty(metadata.SourceReadiness, metadata.GroundingStatus, metadata.GroundingMode, metadata.TutorGroundingPolicy) ?? string.Empty;
        var normalized = status.ToLowerInvariant();
        if (normalized.Contains("wiki")) return "wiki_backed";
        if (normalized.Contains("insufficient") || normalized.Contains("degraded") || normalized.Contains("stale")) return "evidence_insufficient";
        return "model_assisted";
    }

    private static string BuildTutorWikiBlockContent(string userContent, string assistantContent, ChatResponseMetadata metadata)
    {
        var lines = new List<string>
        {
            $"Ogrenci sorusu: {Limit(userContent, 500)}",
            $"Tutor notu: {Limit(assistantContent, 1600)}"
        };
        if (!string.IsNullOrWhiteSpace(metadata.ActiveConceptKey)) lines.Add($"Kavram: {metadata.ActiveConceptKey}");
        if (!string.IsNullOrWhiteSpace(metadata.TutorLessonDelivery?.DeliveryMode)) lines.Add($"Ders modu: {metadata.TutorLessonDelivery.DeliveryMode}");
        if (!string.IsNullOrWhiteSpace(metadata.TutorLessonDelivery?.StudentVisibleSummary)) lines.Add($"Ders ozeti: {metadata.TutorLessonDelivery.StudentVisibleSummary}");
        if (!string.IsNullOrWhiteSpace(metadata.RemediationLesson?.RepairType)) lines.Add($"Telafi tipi: {metadata.RemediationLesson.RepairType}");
        if (!string.IsNullOrWhiteSpace(metadata.RemediationLesson?.StudentVisibleSummary)) lines.Add($"Telafi ozeti: {metadata.RemediationLesson.StudentVisibleSummary}");
        if (!string.IsNullOrWhiteSpace(metadata.RemediationLesson?.Checkpoint.UserSafePrompt)) lines.Add($"Telafi checkpoint: {metadata.RemediationLesson.Checkpoint.UserSafePrompt}");
        if (!string.IsNullOrWhiteSpace(metadata.TutorTeachingMove)) lines.Add($"Ogretim hamlesi: {metadata.TutorTeachingMove}");
        if (!string.IsNullOrWhiteSpace(metadata.TutorRemediationPolicy)) lines.Add($"Remediation politikasi: {metadata.TutorRemediationPolicy}");
        if (metadata.MisconceptionSignal != null)
        {
            lines.Add($"Takilma sinyali: {FirstNonEmpty(metadata.MisconceptionSignal.UserSafeLabel, metadata.MisconceptionSignal.SafeHint, metadata.MisconceptionSignal.Category) ?? "dusuk guvenli sinyal"}");
            lines.Add($"Takilma guveni: {FirstNonEmpty(metadata.LatestMisconceptionConfidence, metadata.MisconceptionSignal.ConfidenceStatus) ?? "observed_only"}");
        }
        if (!string.IsNullOrWhiteSpace(metadata.SourceReadiness)) lines.Add($"Kaynak hazirligi: {metadata.SourceReadiness}");
        if (metadata.TutorNextLearningActions.Count > 0) lines.Add($"Sonraki aksiyon: {Limit(string.Join(", ", metadata.TutorNextLearningActions.Take(3)), 240)}");
        return string.Join("\n", lines);
    }

    private static bool LooksLikeQuizOrRawPayload(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        var normalized = content.Trim().ToLowerInvariant();
        return normalized.Contains("```json") ||
               normalized.Contains("```quiz") ||
               (normalized.Contains("\"question\"") && normalized.Contains("\"options\"")) ||
               normalized.Contains("rawproviderpayload") ||
               normalized.Contains("rawtoolpayload") ||
               normalized.Contains("hiddenprompt");
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string Limit(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var cleaned = value.Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength];
    }

    private static void EnrichTutorResponsePolicy(ChatResponseMetadata metadata, TutorTurnStateDto? turnState, TutorActionTrace? actionTrace)
    {
        if (turnState != null)
        {
            turnState.EvidenceQuality = metadata.EvidenceQuality ?? turnState.EvidenceQuality;
            var decision = TutorResponsePolicy.Decide(turnState);
            var professionalPolicy = TutorResponsePolicyService.BuildPolicy(
                turnState,
                actionTrace,
                latestAttempt: null,
                latestBundle: null,
                toolCalls: Array.Empty<TutorToolCall>());
            metadata.TutorResponseMode ??= decision.TutorResponseMode;
            metadata.EvidencePolicy ??= decision.EvidencePolicy;
            metadata.PersonalizationMode ??= decision.PersonalizationMode;
            metadata.MasteryBasis ??= decision.MasteryBasis;
            metadata.WeakConceptHints = metadata.WeakConceptHints.Count > 0 ? metadata.WeakConceptHints : decision.WeakConceptHints;
            metadata.MisconceptionSignal ??= turnState.MisconceptionSignal;
            metadata.LearningSignalConfidence ??= turnState.LearningSignalConfidence;
            metadata.RemediationSeed ??= turnState.RemediationSeed;
            metadata.RemediationLesson ??= turnState.RemediationLesson;
            metadata.TutorTeachingMove ??= professionalPolicy.TeachingMove;
            metadata.TutorResponseDepth ??= professionalPolicy.ResponseDepth;
            metadata.TutorGroundingPolicy ??= professionalPolicy.GroundingPolicy;
            metadata.TutorRemediationPolicy ??= professionalPolicy.RemediationPolicy;
            metadata.TutorToolPolicy ??= professionalPolicy.ToolPolicy;
            metadata.TutorToolDecision ??= BuildTutorToolDecisionMetadata(metadata, turnState, actionTrace, professionalPolicy);
            metadata.TutorLessonDelivery ??= BuildTutorLessonDeliveryMetadata(metadata, turnState, actionTrace, professionalPolicy, metadata.TutorToolDecision);
            metadata.RemediationLesson ??= BuildMetadataRemediationLesson(metadata, turnState, metadata.TutorToolDecision, metadata.TutorLessonDelivery);
            metadata.TutorNextLearningActions = metadata.TutorNextLearningActions.Count > 0
                ? metadata.TutorNextLearningActions
                : professionalPolicy.NextActions.Select(a => a.ActionType).ToArray();
            metadata.TutorContextUse = metadata.TutorContextUse.Count > 0
                ? metadata.TutorContextUse
                : professionalPolicy.ContextUse.Select(c => $"{c.ContextType}:{c.Status}").ToArray();
            metadata.TutorResponseQualityStatus ??= professionalPolicy.QualityStatus;
            metadata.TutorResponseQualityWarnings = metadata.TutorResponseQualityWarnings.Count > 0
                ? metadata.TutorResponseQualityWarnings
                : professionalPolicy.Warnings.Select(w => w.UserSafeMessage).ToArray();
            metadata.ActivePlanStepId ??= turnState.CurrentPlanStepId;
            metadata.LatestAssessmentMode ??= professionalPolicy.LatestAssessmentMode;
            metadata.LatestMisconceptionConfidence ??= professionalPolicy.LatestMisconceptionConfidence;
            metadata.SourceReadiness ??= professionalPolicy.SourceReadiness;
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
            metadata.TutorLessonDelivery ??= BuildTutorLessonDeliveryMetadata(metadata, null, actionTrace, null, metadata.TutorToolDecision);
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

    private static TutorToolDecisionDto BuildTutorToolDecisionMetadata(
        ChatResponseMetadata metadata,
        TutorTurnStateDto turnState,
        TutorActionTrace? actionTrace,
        TutorResponsePolicyDto policy)
    {
        var allowedTools = metadata.ToolStatuses
            .Select(t => t.ToolId)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var blockedTools = new List<string>();
        var reasons = new List<string>();
        var learnerSignals = new List<string>();
        var safetyWarnings = new List<string>();
        var sourceReady = FirstNonEmpty(
            metadata.SourceReadiness,
            metadata.PlanSourceReadiness,
            metadata.EvidenceQuality?.Status,
            turnState.SourceReadiness,
            turnState.PlanSourceReadiness,
            turnState.GroundingStatus) ?? "unknown";
        var evidenceStatus = FirstNonEmpty(metadata.EvidencePolicy, policy.GroundingPolicy, turnState.EvidencePolicy) ?? "unknown";

        if (!string.IsNullOrWhiteSpace(turnState.ActiveConceptKey)) learnerSignals.Add("active_concept");
        if (turnState.MasteryProbability.HasValue || turnState.Confidence.HasValue) learnerSignals.Add("mastery_confidence");
        if (turnState.RemediationNeed is "high" or "medium") learnerSignals.Add("remediation_need");
        if (turnState.MisconceptionSignal != null) learnerSignals.Add("misconception_signal");
        if (turnState.RemediationSeed != null) learnerSignals.Add("remediation_seed");
        if (!string.IsNullOrWhiteSpace(turnState.AdaptiveDiagnostic?.Intent)) learnerSignals.Add("adaptive_diagnostic");
        if (!string.IsNullOrWhiteSpace(turnState.CoursePlanQuality?.ReadinessStatus)) learnerSignals.Add("plan_readiness");
        if (turnState.HasWikiContext || metadata.TutorContextUse.Any(c => c.Contains("wiki", StringComparison.OrdinalIgnoreCase))) learnerSignals.Add("wiki_context");
        if (turnState.HasNotebookContext || turnState.SourceEvidenceCount > 0 || metadata.EvidenceSummary?.SourceCount > 0) learnerSignals.Add("source_context");
        if (turnState.HasIdeContext || HasTool(metadata, "ide")) learnerSignals.Add("ide_context");

        string selectedAction;
        if (turnState.CoursePlanQuality?.ReadinessStatus is "needs_diagnostic" or "thin_plan")
        {
            selectedAction = "run_diagnostic";
            reasons.Add("plan_needs_diagnostic");
        }
        else if (turnState.CoursePlanQuality?.ReadinessStatus == "needs_prerequisite_check")
        {
            selectedAction = "run_diagnostic";
            reasons.Add("plan_needs_prerequisite_check");
        }
        else if (policy.RemediationPolicy is "guided_repair" or "prerequisite_review" ||
            actionTrace?.TeachingMode == "remediate" ||
            turnState.LearnerState.Contains("remediation", StringComparison.OrdinalIgnoreCase) ||
            turnState.CoursePlanQuality?.ReadinessStatus == "needs_repair")
        {
            selectedAction = "start_remediation";
            reasons.Add("learner_needs_repair");
        }
        else if (actionTrace?.TeachingMode == "source_grounded_answer" && HasReadySourceMetadata(metadata, turnState))
        {
            selectedAction = "ask_source";
            reasons.Add("source_evidence_ready");
        }
        else if (policy.GroundingPolicy is "evidence_insufficient" or "mention_source_limits" &&
                 (sourceReady is "evidence_insufficient" or "missing" or "weak" or "unknown"))
        {
            selectedAction = "ask_clarifying_question";
            blockedTools.Add("ask_source");
            blockedTools.Add("source_grounded_answer");
            reasons.Add("source_evidence_insufficient");
            safetyWarnings.Add("source_grounded_route_blocked");
        }
        else if (actionTrace?.TeachingMode == "code_lab" || HasTool(metadata, "ide"))
        {
            selectedAction = "use_ide_context";
            reasons.Add("code_context_detected");
        }
        else if (actionTrace?.TeachingMode == "review")
        {
            selectedAction = "review_quiz_result";
            reasons.Add("review_context_available");
        }
        else if (HasTool(metadata, "research_context"))
        {
            selectedAction = "use_korteks_research";
            reasons.Add("research_context_available");
        }
        else if (HasTool(metadata, "wiki"))
        {
            selectedAction = "read_wiki_context";
            reasons.Add("wiki_context_available");
        }
        else
        {
            selectedAction = "explain";
            reasons.Add("default_tutor_explanation");
        }

        if (metadata.TutorGroundingPolicy is "evidence_insufficient" or "mention_source_limits" ||
            metadata.TutorResponseMode == "evidence_limited")
        {
            safetyWarnings.Add("evidence_limited");
        }

        return new TutorToolDecisionDto
        {
            SelectedAction = selectedAction,
            AllowedTools = allowedTools.Take(10).ToArray(),
            BlockedTools = blockedTools.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray(),
            ReasonCodes = reasons.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray(),
            Confidence = selectedAction == "ask_clarifying_question" ? 0.72m : 0.76m,
            LearnerSignalsUsed = learnerSignals.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray(),
            EvidenceStatus = evidenceStatus,
            SourceReadiness = sourceReady,
            SafetyWarnings = safetyWarnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray(),
            NextTutorMove = turnState.CoursePlanQuality?.RecommendedNextAction ?? policy.TeachingMove,
            StudentVisibleSummary = BuildToolDecisionSummary(selectedAction, safetyWarnings)
        };
    }

    private static TutorLessonDeliveryDto BuildTutorLessonDeliveryMetadata(
        ChatResponseMetadata metadata,
        TutorTurnStateDto? turnState,
        TutorActionTrace? actionTrace,
        TutorResponsePolicyDto? policy,
        TutorToolDecisionDto? toolDecision)
    {
        var learnerLevel = DetermineMetadataLearnerLevel(metadata, turnState);
        var deliveryMode = DetermineMetadataDeliveryMode(metadata, turnState, actionTrace, policy, toolDecision);
        var warnings = new List<string>();
        if (metadata.TutorResponseMode == "evidence_limited" ||
            metadata.TutorGroundingPolicy is "evidence_insufficient" or "mention_source_limits" ||
            toolDecision?.SafetyWarnings.Any(w => w.Contains("source", StringComparison.OrdinalIgnoreCase)) == true)
        {
            warnings.Add("source_evidence_limited");
        }

        if (metadata.TutorResponseQualityWarnings.Any(w => w.Contains("answer", StringComparison.OrdinalIgnoreCase) || w.Contains("key", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add("answer_key_guard_active");
        }

        if (metadata.Confidence < 0.45m || metadata.MasteryBasis == "low_confidence")
        {
            warnings.Add("low_confidence_learner_state");
        }

        var includesRepair = deliveryMode is "misconception_repair" or "prerequisite_repair";
        var includesCheckpoint = deliveryMode is "checkpoint_question" or "guided_example" or "concept_explanation" or "misconception_repair" or "prerequisite_repair";
        var sourceBasis = deliveryMode == "source_grounded_explanation" ? "source_grounded" :
            deliveryMode == "model_assisted_explanation" ? "model_assisted" :
            "tutor_generated";

        return new TutorLessonDeliveryDto
        {
            DeliveryMode = deliveryMode,
            LearnerLevel = learnerLevel,
            Structure = new TutorLessonStructureDto
            {
                Goal = BuildMetadataLessonGoal(deliveryMode, metadata, turnState),
                ShortExplanation = BuildMetadataExplanationGuidance(deliveryMode, learnerLevel),
                Example = deliveryMode is "guided_example" or "misconception_repair" or "prerequisite_repair"
                    ? "Bir cozumlu ornek veya mini senaryo kullan."
                    : "Gerekirse tek somut ornek ekle.",
                Checkpoint = deliveryMode == "ask_clarifying_question" ? "Once hedefi netlestir." : metadata.NextCheckPrompt ?? actionTrace?.NextCheckPrompt ?? "Kisa kontrol sorusu sor.",
                NextAction = policy?.NextActions.FirstOrDefault()?.ActionType ?? metadata.TutorNextLearningActions.FirstOrDefault() ?? "continue_lesson"
            },
            RubricSignals = new TutorLessonRubricDto
            {
                UsesLearnerState = turnState != null || !string.IsNullOrWhiteSpace(metadata.LessonSnapshotStatus),
                UsesMasterySignal = metadata.MasteryProbability.HasValue || metadata.Confidence.HasValue || turnState?.MasteryProbability.HasValue == true,
                UsesQuizSignal = !string.IsNullOrWhiteSpace(metadata.LatestAssessmentMode) || metadata.MisconceptionSignal != null || metadata.RemediationSeed != null,
                UsesSourceEvidence = deliveryMode == "source_grounded_explanation",
                AvoidsPreSubmitReveal = true,
                IncludesCheckpoint = includesCheckpoint,
                IncludesRepairStep = includesRepair,
                BoundedLength = true
            },
            Steps = BuildMetadataLessonSteps(deliveryMode, sourceBasis),
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray(),
            StudentVisibleSummary = BuildMetadataLessonSummary(deliveryMode, warnings)
        };
    }

    private static RemediationLessonDto? BuildMetadataRemediationLesson(
        ChatResponseMetadata metadata,
        TutorTurnStateDto? turnState,
        TutorToolDecisionDto? toolDecision,
        TutorLessonDeliveryDto? lessonDelivery)
    {
        if (turnState?.RemediationLesson != null)
        {
            return turnState.RemediationLesson;
        }

        if (lessonDelivery == null ||
            toolDecision?.SelectedAction != "start_remediation" &&
            lessonDelivery.DeliveryMode is not ("misconception_repair" or "prerequisite_repair"))
        {
            return null;
        }

        var sourceGap = lessonDelivery.Warnings.Contains("source_evidence_limited", StringComparer.OrdinalIgnoreCase) ||
                        toolDecision?.SafetyWarnings.Any(w => w.Contains("source", StringComparison.OrdinalIgnoreCase)) == true;
        var repairType = lessonDelivery.DeliveryMode == "misconception_repair" ? "misconception_repair" :
            lessonDelivery.DeliveryMode == "prerequisite_repair" ? "prerequisite_repair" :
            sourceGap ? "source_evidence_review" :
            metadata.MasteryProbability < 0.45m ? "weak_concept_repair" :
            "guided_reteach";
        var triggerType = metadata.LatestAssessmentMode is "skipped" ? "skipped_answer" :
            metadata.LatestAssessmentMode is "blank" ? "blank_answer" :
            metadata.MisconceptionSignal != null ? "misconception_signal" :
            metadata.AffectiveState is "confused" or "frustrated" ? "student_confused" :
            sourceGap ? "source_evidence_gap" :
            "weak_concept";
        var concept = FirstNonEmpty(metadata.ActiveConceptKey, turnState?.ActiveConceptLabel, turnState?.ActiveConceptKey, metadata.CurrentPlanStepTitle) ?? "aktif kavram";
        var gap = repairType switch
        {
            "misconception_repair" => FirstNonEmpty(metadata.MisconceptionSignal?.UserSafeLabel, metadata.MisconceptionSignal?.SafeHint, "Takilma sinyali kesin tani degil.")!,
            "prerequisite_repair" => FirstNonEmpty(metadata.RemediationSeed?.Reason, "Eksik onkosul kisa telafi istiyor.")!,
            "source_evidence_review" => "Kaynak kaniti sinirli; kaynakli iddia kurulmadan once kanit kontrolu gerekir.",
            "weak_concept_repair" => "Mastery sinyali zayif kavrami mikro dersle toparlamayi oneriyor.",
            _ => "Bu tur kisa guided reteach istiyor."
        };

        return new RemediationLessonDto
        {
            TopicId = turnState?.TopicId,
            ConceptKey = string.IsNullOrWhiteSpace(turnState?.ActiveConceptKey) ? metadata.ActiveConceptKey : turnState!.ActiveConceptKey,
            Trigger = new RemediationTriggerDto
            {
                TriggerType = triggerType,
                UserSafeLabel = triggerType switch
                {
                    "blank_answer" => "Bos cevap telafisi",
                    "skipped_answer" => "Atlanan cevap telafisi",
                    "student_confused" => "Anlamadim sinyali",
                    "source_evidence_gap" => "Kaynak kaniti siniri",
                    "misconception_signal" => "Takilma sinyali",
                    _ => "Zayif kavram sinyali"
                },
                EvidenceStatus = metadata.LearningSignalConfidence?.Status ?? metadata.LatestMisconceptionConfidence ?? "observed_only"
            },
            RepairType = repairType,
            Confidence = metadata.LearningSignalConfidence?.Status == "usable" ? "medium" : "low",
            Basis = (toolDecision?.ReasonCodes ?? Array.Empty<string>())
                .Concat(toolDecision?.LearnerSignalsUsed ?? Array.Empty<string>())
                .Append("chat_metadata")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToArray(),
            LessonShape = new RemediationRepairLoopDto
            {
                Goal = lessonDelivery.Structure.Goal,
                MisconceptionOrGap = gap,
                ShortReteach = lessonDelivery.Structure.ShortExplanation,
                WorkedExample = lessonDelivery.Structure.Example,
                GuidedPractice = "Ogrenciye tek kucuk adimi kendisi yaptir.",
                Checkpoint = lessonDelivery.Structure.Checkpoint,
                NextAction = lessonDelivery.Structure.NextAction,
                Steps = lessonDelivery.Steps
                    .Where(s => s.StepType is "goal" or "short_explanation" or "worked_example" or "repair_step" or "checkpoint")
                    .Select(s => new RemediationStepDto
                    {
                        StepType = s.StepType == "short_explanation" ? "short_reteach" : s.StepType,
                        UserSafeLabel = s.UserSafeLabel,
                        Required = s.Required,
                        SourceBasis = s.SourceBasis
                    })
                    .Take(5)
                    .ToArray()
            },
            Checkpoint = new RemediationCheckpointDto
            {
                CheckpointType = sourceGap ? "evidence_check" : "micro_check",
                UserSafePrompt = lessonDelivery.Structure.Checkpoint,
                AvoidsPreSubmitReveal = true,
                Required = true
            },
            Outcome = new RemediationOutcomeDto
            {
                ExpectedSignal = repairType == "source_evidence_review" ? "source_review_needed" : "needs_review",
                MasteryPolicy = "do_not_overstate_mastery",
                NextTutorAction = lessonDelivery.Structure.NextAction,
                NotebookAction = "repair_pack_available"
            },
            Warnings = lessonDelivery.Warnings
                .Append(metadata.MisconceptionSignal == null && repairType == "guided_reteach" ? "misconception_not_confirmed" : null)
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Select(w => w!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToArray(),
            SourceBasis = sourceGap ? "evidence_insufficient" : "tutor_generated",
            StudentVisibleSummary = repairType switch
            {
                "misconception_repair" => "Tutor takilma sinyalini kesin tani gibi sunmadan onaracak.",
                "prerequisite_repair" => "Tutor once eksik onkosulu kisa telafiyle toparlayacak.",
                "source_evidence_review" => "Tutor kaynak kaniti sinirini acik tutarak ilerleyecek.",
                "weak_concept_repair" => "Tutor zayif kavram icin mikro ders ve kontrol uygulayacak.",
                _ => "Tutor bu turda kisa guided telafi dersi uygulayacak."
            }
        };
    }

    private static string DetermineMetadataLearnerLevel(ChatResponseMetadata metadata, TutorTurnStateDto? turnState)
    {
        if (metadata.AdaptiveDiagnostic?.LearnerLevel is "beginner" or "developing" or "exam_ready" or "advanced")
            return metadata.AdaptiveDiagnostic.LearnerLevel;
        if (turnState?.AdaptiveDiagnostic?.LearnerLevel is "beginner" or "developing" or "exam_ready" or "advanced")
            return turnState.AdaptiveDiagnostic.LearnerLevel;
        if (turnState?.PracticeReadiness == "exam_ready" && (metadata.MasteryProbability ?? turnState.MasteryProbability) >= 0.70m && (metadata.Confidence ?? turnState.Confidence) >= 0.60m)
            return "exam_ready";
        if (string.Equals(metadata.PersonalizationMode, "advanced", StringComparison.OrdinalIgnoreCase))
            return "advanced";
        var mastery = metadata.MasteryProbability ?? turnState?.MasteryProbability;
        var confidence = metadata.Confidence ?? turnState?.Confidence;
        if (mastery >= 0.78m && confidence >= 0.65m) return "advanced";
        if (mastery >= 0.55m && confidence >= 0.45m) return "developing";
        if (mastery.HasValue || confidence.HasValue) return "beginner";
        return "unknown";
    }

    private static string DetermineMetadataDeliveryMode(
        ChatResponseMetadata metadata,
        TutorTurnStateDto? turnState,
        TutorActionTrace? actionTrace,
        TutorResponsePolicyDto? policy,
        TutorToolDecisionDto? toolDecision)
    {
        if (toolDecision?.SelectedAction == "ask_clarifying_question") return "ask_clarifying_question";
        if (toolDecision?.SelectedAction == "run_diagnostic") return "checkpoint_question";
        if (toolDecision?.SelectedAction == "generate_quiz") return "checkpoint_question";
        if (toolDecision?.SelectedAction == "ask_source")
            return turnState != null && HasReadySourceMetadata(metadata, turnState) ? "source_grounded_explanation" : "model_assisted_explanation";
        if (toolDecision?.SelectedAction == "start_remediation")
        {
            if (metadata.RemediationSeed?.FirstAction == "prerequisite_review" ||
                metadata.TutorRemediationPolicy == "prerequisite_review" ||
                metadata.LatestAssessmentMode is "skipped" or "blank")
            {
                return "prerequisite_repair";
            }

            return metadata.MisconceptionSignal != null || metadata.LatestMisconceptionConfidence is "observed" or "strong"
                ? "misconception_repair"
                : "prerequisite_repair";
        }

        if (metadata.TutorGroundingPolicy == "cite_sources" && turnState != null && HasReadySourceMetadata(metadata, turnState))
            return "source_grounded_explanation";
        if (metadata.TutorGroundingPolicy is "evidence_insufficient" or "mention_source_limits" &&
            toolDecision?.SafetyWarnings.Any(w => w.Contains("source", StringComparison.OrdinalIgnoreCase)) == true)
            return "model_assisted_explanation";
        if (actionTrace?.TeachingMode == "review" || policy?.TeachingMove == "retrieval_prompt")
            return "quiz_review";
        if (actionTrace?.TeachingMode == "challenge" || metadata.MasteryProbability >= 0.75m && metadata.Confidence >= 0.60m)
            return "checkpoint_question";
        if (actionTrace?.TeachingMode == "guided_practice" || metadata.MasteryProbability < 0.55m || metadata.Confidence < 0.45m)
            return "guided_example";
        return "concept_explanation";
    }

    private static IReadOnlyList<TutorLessonStepDto> BuildMetadataLessonSteps(string deliveryMode, string sourceBasis)
    {
        var steps = new List<TutorLessonStepDto>
        {
            new() { StepType = "goal", UserSafeLabel = "Hedefi kisa kur", Required = true, SourceBasis = sourceBasis },
            new() { StepType = "short_explanation", UserSafeLabel = "Kisa aciklama", Required = true, SourceBasis = sourceBasis }
        };
        if (deliveryMode is "guided_example" or "misconception_repair" or "prerequisite_repair")
        {
            steps.Add(new TutorLessonStepDto { StepType = "worked_example", UserSafeLabel = "Somut ornekle ilerle", Required = true, SourceBasis = sourceBasis });
        }
        if (deliveryMode is "misconception_repair" or "prerequisite_repair")
        {
            steps.Add(new TutorLessonStepDto { StepType = "repair_step", UserSafeLabel = "Telafi adimini ac", Required = true, SourceBasis = "tutor_generated" });
        }
        if (deliveryMode is "checkpoint_question" or "guided_example" or "concept_explanation" or "misconception_repair" or "prerequisite_repair")
        {
            steps.Add(new TutorLessonStepDto { StepType = "checkpoint", UserSafeLabel = "Kisa kontrol sorusu sor", Required = true, SourceBasis = "tutor_generated" });
        }
        steps.Add(new TutorLessonStepDto { StepType = "next_action", UserSafeLabel = "Sonraki adimi soyle", Required = true, SourceBasis = "tutor_generated" });
        return steps.Take(6).ToArray();
    }

    private static string BuildMetadataLessonGoal(string deliveryMode, ChatResponseMetadata metadata, TutorTurnStateDto? turnState)
    {
        var concept = FirstNonEmpty(metadata.ActiveConceptKey, turnState?.ActiveConceptLabel, turnState?.ActiveConceptKey) ?? "aktif kavram";
        return deliveryMode switch
        {
            "misconception_repair" => $"{concept} icin takilma sinyalini kesin tani gibi sunmadan onar.",
            "prerequisite_repair" => $"{concept} icin eksik onkosulu kisa telafiyle tamamla.",
            "guided_example" => $"{concept} icin kavrami somut ornekle kur.",
            "checkpoint_question" => $"{concept} icin kisa yoklama yap.",
            "source_grounded_explanation" => $"{concept} aciklamasini hazir kaynak kanitindan ayirarak kur.",
            "model_assisted_explanation" => $"{concept} icin kaynak sinirini belirterek model destekli acikla.",
            "ask_clarifying_question" => "Ogrenme hedefini ve baglami netlestir.",
            _ => $"{concept} icin kisa, hedefli kavram aciklamasi yap."
        };
    }

    private static string BuildMetadataExplanationGuidance(string deliveryMode, string learnerLevel) =>
        deliveryMode switch
        {
            "ask_clarifying_question" => "Cevap vermeden once tek netlestirme sorusu sor.",
            "checkpoint_question" => "Aciklamayi kisa tut ve cevabi sakli tek kontrol sorusu sor.",
            "misconception_repair" => "Hatayi kesin tani gibi sunmadan dogru ayrimi iki kisa adimda kur.",
            "prerequisite_repair" => "Eksik onkosulu yavas ve kisa parcalarla toparla.",
            "source_grounded_explanation" => "Kaynakta desteklenen kisim ile Tutor yorumunu ayri tut.",
            "model_assisted_explanation" => "Kaynak kaniti sinirliyken kesin kaynak iddiasi kurma.",
            _ => learnerLevel == "advanced" ? "Kisa gerekce ve sinir kosulu ver." : "Tek kavramsal parca ile basla."
        };

    private static string BuildMetadataLessonSummary(string deliveryMode, IReadOnlyCollection<string> warnings)
    {
        if (warnings.Contains("source_evidence_limited", StringComparer.OrdinalIgnoreCase))
            return "Tutor kaynak sinirini belirterek model destekli ve kontrollu anlatim yapiyor.";

        return deliveryMode switch
        {
            "guided_example" => "Tutor once kisa hedefi kurup somut ornekle ilerliyor.",
            "checkpoint_question" => "Tutor bu turda kisa kontrol sorusuyla ogrenmeyi yokluyor.",
            "misconception_repair" => "Tutor takilma sinyalini kesin tani gibi sunmadan ornekle onariyor.",
            "prerequisite_repair" => "Tutor eksik onkosulu kisa telafiyle toparliyor.",
            "source_grounded_explanation" => "Tutor hazir kaynak kanitini Tutor yorumundan ayri tutarak acikliyor.",
            "model_assisted_explanation" => "Tutor kaynak kaniti sinirliyken model destekli aciklama yapiyor.",
            "ask_clarifying_question" => "Tutor daha iyi ders kurmak icin once hedefi netlestiriyor.",
            "quiz_review" => "Tutor once tekrar ve quiz sonucunu kisa bir ogrenme hamlesine bagliyor.",
            _ => "Tutor bu turda kisa, hedefli kavram anlatimiyla ilerliyor."
        };
    }

    private static bool HasTool(ChatResponseMetadata metadata, string toolNeedle) =>
        metadata.ToolStatuses.Any(t => t.ToolId.Contains(toolNeedle, StringComparison.OrdinalIgnoreCase)) ||
        metadata.UsedTools.Any(t => (t.ToolId ?? t.Name).Contains(toolNeedle, StringComparison.OrdinalIgnoreCase));

    private static bool HasReadySourceMetadata(ChatResponseMetadata metadata, TutorTurnStateDto turnState) =>
        turnState.SourceEvidenceCount > 0 ||
        metadata.EvidenceSummary is { SourceCount: > 0 } ||
        metadata.EvidenceQuality is { ReadySourceCount: > 0 } ||
        FirstNonEmpty(metadata.SourceReadiness, turnState.SourceReadiness, turnState.GroundingStatus, metadata.EvidenceQuality?.Status) is
            "source_grounded" or "mixed" or "ready" or "strong";

    private static string BuildToolDecisionSummary(string selectedAction, IReadOnlyCollection<string> safetyWarnings)
    {
        if (safetyWarnings.Contains("source_grounded_route_blocked", StringComparer.OrdinalIgnoreCase))
            return "Kaynak kaniti yeterli olmadigi icin Tutor kaynakli iddia kurmadan ilerliyor.";

        return selectedAction switch
        {
            "start_remediation" => "Tutor bu turda eksigi kapatmak icin telafi adimina geciyor.",
            "ask_source" => "Tutor hazir kaynak kanitini kullanarak ilerleyecek.",
            "ask_clarifying_question" => "Tutor once baglami netlestirecek.",
            "use_ide_context" => "Tutor kod/IDE ciktisini guvenli baglam olarak kullanacak.",
            "review_quiz_result" => "Tutor once tekrar ve quiz sonucunu dikkate alacak.",
            "use_korteks_research" => "Tutor arastirma baglamini destekleyici kanit olarak kullanacak.",
            "read_wiki_context" => "Tutor once Wiki ogrenme hafizasini kontrol edecek.",
            _ => "Tutor bu turda genel anlatimla ilerliyor."
        };
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
            _logger.LogDebug("[ChatMetadata] Evidence quality enrichment skipped. UserRef={UserRef} TopicRef={TopicRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(userId, "usr"),
                LogPrivacyGuard.SafeId(topicId, "topic"),
                LogPrivacyGuard.SafeExceptionType(ex));
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
                _logger.LogInformation("[DeepPlan] Mufredat icin seviye tespiti baslatiliyor. TopicRef={TopicRef}",
                    LogPrivacyGuard.SafeTextRef(topic.Title, "topic"));
                topic.Category = "Plan";
                topic.PlanIntent ??= "Core";
                await _db.SaveChangesAsync();

                var user = await _db.Users.FindAsync(topic.UserId);
                var language = user?.Language ?? "Turkish";
                if (language == "English" && !string.IsNullOrWhiteSpace(content))
                {
                    var lower = content.ToLowerInvariant();
                    if (lower.Contains("icin") || lower.Contains("için") || lower.Contains("hazirla") || lower.Contains("hazırla") || lower.Contains("plan") || lower.Contains("nedir") || lower.Contains("nasil") || lower.Contains("nasıl") || lower.Contains("anlat"))
                    {
                        language = "Turkish";
                    }
                }
                int questionCount = 20;
                if (!string.IsNullOrEmpty(topic.MetadataJson))
                {
                    try
                    {
                        using var metaDoc = System.Text.Json.JsonDocument.Parse(topic.MetadataJson);
                        if (metaDoc.RootElement.TryGetProperty("quizQuestionCount", out var qcProp) && qcProp.TryGetInt32(out var parsedCount))
                        {
                            questionCount = parsedCount;
                        }
                    }
                    catch { }
                }

                var allQuizJson = await _deepPlanAgent.GenerateBaselineQuizAsync(topic.Title, topic.Id, language, questionCount);
                var firstQuizJson = ExtractNthQuizFromArray(allQuizJson, 0);

                session.BaselineQuizData = allQuizJson;
                session.BaselineQuizIndex = 0;
                session.BaselineCorrectCount = 0;
                session.PendingQuiz = ExtractQuizQuestionText(firstQuizJson);
                session.CurrentState = SessionState.BaselineQuizMode;
                await _db.SaveChangesAsync();

                var isTurkish = language.StartsWith("tr", System.StringComparison.OrdinalIgnoreCase) ||
                                 language.StartsWith("tü", System.StringComparison.OrdinalIgnoreCase) ||
                                 language.StartsWith("tu", System.StringComparison.OrdinalIgnoreCase);
                var responseText = isTurkish
                    ? $"Senin için en uygun öğrenme yolunu çizebilmem için **{questionCount} soruluk** kapsamlı bir seviye testi yapacağım. \n\n{allQuizJson}"
                    : $"I will conduct a comprehensive **{questionCount}-question** assessment to customize your learning path. \n\n{allQuizJson}";

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
                _logger.LogInformation("[QUIZ] Konu gecis talebi. TopicRef={TopicRef} TitleRef={TitleRef}",
                    LogPrivacyGuard.SafeId(currentTopic.Id, "topic"),
                    LogPrivacyGuard.SafeTextRef(currentTopic.Title, "title"));

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
            if (string.IsNullOrWhiteSpace(session.PendingQuiz))
            {
                string topicTitle = "Genel Değerlendirme";
                if (session.TopicId.HasValue)
                {
                    var topic = await _db.Topics.FindAsync(session.TopicId.Value);
                    if (topic != null && !string.IsNullOrWhiteSpace(topic.Title))
                    {
                        topicTitle = topic.Title;
                    }
                }
                session.PendingQuiz = await _tutorAgent.GenerateTopicQuizAsync(topicTitle);
            }
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
            _logger.LogInformation("[QUIZ] IDE kod cevabi degerlendiriliyor. SessionRef={SessionRef}",
                LogPrivacyGuard.SafeId(session.Id, "session"));
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
            _logger.LogWarning("[REMEDIAL] Notebook cache invalidation atlandi. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
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
            _logger.LogWarning("[TRANSITION] Topic kaydi bulunamadi. SessionRef={SessionRef}",
                LogPrivacyGuard.SafeId(session.Id, "session"));
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
            _logger.LogInformation("[TRANSITION] Tum alt konular tamamlandi. TopicRef={TopicRef}",
                LogPrivacyGuard.SafeId(currentTopic.Id, "topic"));
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
        _logger.LogInformation("[DEEP_PLAN] Planlama modu etkinlestirildi. ContentLength={ContentLength} ContentRef={ContentRef}",
            content?.Length ?? 0,
            LogPrivacyGuard.SafeTextRef(content, "content"));

        Topic? topic = topicId.HasValue ? await _db.Topics.FindAsync(topicId.Value) : null;
        Session? session = null;

        if (topic == null)
        {
            string title = (content ?? "").Replace("/plan", "").Trim();
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

        var quizResponse = await TriggerBaselineQuizForPlanAsync(userId, content ?? "", session);

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
                _logger.LogInformation("[DeepPlan] Chat baglami plan moduna cevrildi. TitleRef={TitleRef}",
                    LogPrivacyGuard.SafeTextRef(cleanTitle, "title"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[DeepPlan] Niyet ayrıştırma başarısız oldu. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
        }

        topic.Category = "Plan";
        topic.PlanIntent ??= "Core";
        await _db.SaveChangesAsync();

        _logger.LogInformation("[DeepPlan] Mufredat plani seviye tespiti baslatiliyor. TopicRef={TopicRef}",
            LogPrivacyGuard.SafeTextRef(topic.Title, "topic"));

        var user = await _db.Users.FindAsync(topic.UserId);
        var language = user?.Language ?? "Turkish";
        if (language == "English" && !string.IsNullOrWhiteSpace(content))
        {
            var lower = content.ToLowerInvariant();
            if (lower.Contains("icin") || lower.Contains("için") || lower.Contains("hazirla") || lower.Contains("hazırla") || lower.Contains("plan") || lower.Contains("nedir") || lower.Contains("nasil") || lower.Contains("nasıl") || lower.Contains("anlat"))
            {
                language = "Turkish";
            }
        }
        int questionCount = 20;
        if (!string.IsNullOrEmpty(topic.MetadataJson))
        {
            try
            {
                using var metaDoc = System.Text.Json.JsonDocument.Parse(topic.MetadataJson);
                if (metaDoc.RootElement.TryGetProperty("quizQuestionCount", out var qcProp) && qcProp.TryGetInt32(out var parsedCount))
                {
                    questionCount = parsedCount;
                }
            }
            catch { }
        }

        var allQuizJson = await _deepPlanAgent.GenerateBaselineQuizAsync(topic.Title, topic.Id, language, questionCount);

        session.BaselineQuizData = allQuizJson;
        session.BaselineQuizIndex = 0;
        session.BaselineCorrectCount = 0;
        session.PendingQuiz = null;
        session.CurrentState = SessionState.BaselineQuizMode;
        await _db.SaveChangesAsync();

        var isTurkish = language.StartsWith("tr", System.StringComparison.OrdinalIgnoreCase) ||
                         language.StartsWith("tü", System.StringComparison.OrdinalIgnoreCase) ||
                         language.StartsWith("tu", System.StringComparison.OrdinalIgnoreCase);
        var responseText = isTurkish
            ? $"Harika! **{topic.Title}** konusu için detaylı bir akademik planlama süreci başlatıyorum. \n\n" +
              $"Öncelikle senin için en uygun öğrenme müfredatını çizebilmem için **Seviye Testi** ({questionCount} soru) yapacağım.\n\n" +
              $"Lütfen aşağıdaki soruları dikkatlice yanıtla:\n\n{allQuizJson}"
            : $"Excellent! I am starting a detailed academic planning process for **{topic.Title}**. \n\n" +
              $"First, I will conduct a **Placement Quiz** ({questionCount} questions) to customize your learning curriculum.\n\n" +
              $"Please answer the following questions carefully:\n\n{allQuizJson}";

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
                        _logger.LogInformation("[AUTO-NAMING] Baslik guncellendi. TitleRef={TitleRef}",
                            LogPrivacyGuard.SafeTextRef(generatedTitle, "title"));
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

    private static LearningMemoryHygieneDto? TryReadLearningMemoryHygiene(string? memoryJson)
    {
        if (string.IsNullOrWhiteSpace(memoryJson)) return null;
        try
        {
            var projection = System.Text.Json.JsonSerializer.Deserialize<LearningMemoryHygieneProjection>(
                memoryJson,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
                {
                    PropertyNameCaseInsensitive = true
                });
            return projection?.Hygiene;
        }
        catch
        {
            return null;
        }
    }

    private sealed class LearningMemoryHygieneProjection
    {
        public LearningMemoryHygieneDto? Hygiene { get; set; }
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

    private static string ScrubGdprPatterns(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var emailRegex = new System.Text.RegularExpressions.Regex(
            @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", 
            System.Text.RegularExpressions.RegexOptions.Compiled);
        
        var result = emailRegex.Replace(input, "[MASKED_EMAIL]");

        var phoneRegex = new System.Text.RegularExpressions.Regex(
            @"\+?\d{10,12}", 
            System.Text.RegularExpressions.RegexOptions.Compiled);
        result = phoneRegex.Replace(result, "[PHONE_MASKED]");

        return result;
    }
}
