using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MediatR;
using Orka.Core.DTOs.Chat;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;
using Orka.Infrastructure.SemanticKernel;
using Orka.Infrastructure.SemanticKernel.Audio;

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
    private readonly IClassroomVoicePusher _voicePusher;
    private readonly ITtsStreamService _ttsStreamService;
    private readonly Orka.Infrastructure.SemanticKernel.Audio.ClassroomSessionManager _sessionManager;
    private readonly ILogger<AgentOrchestratorService> _logger;
    private readonly InteractiveClassSession _classSession;
    private readonly ITopicDetectorService _topicDetector;

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
        IClassroomVoicePusher voicePusher,
        ITtsStreamService ttsStreamService,
        Orka.Infrastructure.SemanticKernel.Audio.ClassroomSessionManager sessionManager,
        InteractiveClassSession classSession,
        ITopicDetectorService topicDetector,
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
        _voicePusher = voicePusher;
        _ttsStreamService = ttsStreamService;
        _sessionManager = sessionManager;
        _classSession = classSession;
        _topicDetector = topicDetector;
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

    public async IAsyncEnumerable<string> ProcessMessageStreamAsync(Guid userId, string content, Guid? topicId, Guid? sessionId, bool isPlanMode = false, bool isVoiceMode = false)
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

        // --- PHASE 3: Interruption Recovery Injection ---
        var interruptedMs = _sessionManager.PopInterruptedState(session.Id);
        if (interruptedMs.HasValue)
        {
            _logger.LogInformation("Kesinti geçmişi tespit edildi ({Elapsed}ms). Interruption Context basılıyor. Session: {SessionId}", interruptedMs, session.Id);
            
            string sysPrompt = isVoiceMode 
                ? "[SİSTEM DİREKTİFİ - PODCAST KESİNTİSİ]: Sen ve Asistan (Emel) radyo programı (Sesli Sınıf) konseptinde dersi anlatırken tam o anda sözünüz kesildi ve öğrenci araya girip bir soru sordu. GÖREVİN: Robotik tepkiler kesinlikle verme. Bir podcast kesintisi gibi, Asistan veya Hoca hemen devreye girip öğrencinin sözünü onaylasın. Örneğin [ASISTAN]: 'Araya girdin harika oldu' gibi doğal bir geçişle sorusunu yanıtlayın ve muhabbeti kaldığı yerden sürdürün. Daima [HOCA]: ve [ASISTAN]: etiketlerini kullanın."
                : "[SİSTEM DİREKTİFİ]: Sen Orka Tutor ajansın. Yukarıda dersi anlatırken tam cümlenin ortasında sözün kesildi ve öğrenci araya girip bir soru sordu/yorum yaptı. GÖREVİN: Robotik tepkiler verme! Tamamen insansı bir şekilde, kaba olmadan doğal bir geçişle sorusunu yanıtla ve SEZİSİZCE bir önceki anlattığın plana geri dönüp dersi sürdür.";

            var systemMsg = new Message
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                UserId = userId,
                Role = "system",
                Content = sysPrompt,
                CreatedAt = DateTime.UtcNow,
                MessageType = MessageType.General
            };
            _db.Messages.Add(systemMsg);
            session.Messages.Add(systemMsg);
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
            session.Messages ??= new System.Collections.Generic.List<Message>();
            _db.Messages.Add(userMsg);
            session.Messages.Add(userMsg);
            await _db.SaveChangesAsync();
        }

        string fullResponse = "";

        // UI'daki "Plan Modu" açık kalsa bile kullanıcı Quiz cevaplıyorsa Plan modunu ezgeç.
        bool isAnsweringQuiz = content.Trim().StartsWith("==Quiz Cevabım:**", StringComparison.OrdinalIgnoreCase);
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
        bool isQuizAcceptance = session.CurrentState == SessionState.QuizPending &&
                               (content.Contains("evet", StringComparison.OrdinalIgnoreCase) ||
                                content.Contains("başla", StringComparison.OrdinalIgnoreCase));

        bool needsSyncRoute =
            session.CurrentState == SessionState.BaselineQuizMode ||
            session.CurrentState == SessionState.QuizMode ||
            session.CurrentState == SessionState.AwaitingChoice ||
            session.CurrentState == SessionState.RemedialOfferPending ||
            session.CurrentState == SessionState.PlanDiagnosticMode ||
            isQuizAcceptance ||
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
            else if (session.CurrentState == SessionState.PlanDiagnosticMode)
                thinkingHint = "[THINKING: Hedefin ve konunun kapsami analiz ediliyor...]";
            else if (isPlanMode || content.Contains("/plan", StringComparison.OrdinalIgnoreCase))
                thinkingHint = "[THINKING: Konu arastiriliyor ve seviye tespiti baslatiliyor...]";
            else
                thinkingHint = "[THINKING: Yanit hazirlaniyor...]";

            yield return thinkingHint;

            if (isBaselineMode)
                yield return "[THINKING: Kisisel ogrenme plani derleniyor...]";

            // State handler içinde değişir — Evaluator etiketlemesi için entry state'i yakala
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
                else if (session.CurrentState == SessionState.PlanDiagnosticMode)
                {
                    var result = await HandlePlanDiagnosticModeAsync(userId, content, session);
                    syncResponse = result.Response;
                    syncMsgId = await SaveAiMessage(session, userId, syncResponse);
                }
                else if (session.CurrentState == SessionState.AwaitingChoice)
                {
                    var result = await HandleAwaitingChoiceStateAsync(userId, content, session);
                    syncResponse = result.Response;
                    syncMsgId = await SaveAiMessage(session, userId, syncResponse);
                }
                // 2. isPlanMode veya /plan komutu → DeepPlan
                else if (isPlanMode || content.Contains("/plan", StringComparison.OrdinalIgnoreCase))
                {
                    var result = await TriggerBaselineQuizForPlanAsync(userId, content, session);
                    syncResponse = result.Response;
                    syncMsgId = await SaveAiMessage(session, userId, syncResponse);
                }
                else if (session.CurrentState == SessionState.RemedialOfferPending)
                {
                    var result = await HandleRemedialOfferPendingAsync(userId, content, session);
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
                    SessionState.QuizMode => "GraderAgent",
                    SessionState.BaselineQuizMode => "DeepPlanAgent",
                    SessionState.PlanDiagnosticMode => "SupervisorAgent",
                    SessionState.AwaitingChoice => "TutorAgent",
                    _ => isPlanMode || content.Contains("/plan", StringComparison.OrdinalIgnoreCase)
                                                     ? "DeepPlanAgent" : "TutorAgent"
                };
                TriggerBackgroundTasks(session, userId, content, syncResponse, syncMsgId, syncAgentRole);

                // Ses Akışını Başlat — SADECE Voice Mode aktifse
                if (isVoiceMode)
                {
                    var ttsVoice = syncAgentRole == "TutorAgent" ? TtsVoice.Hoca : TtsVoice.Asistan;
                    var syncTtsCt = _sessionManager.StartSession(session.Id);
                    
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await foreach (var audioBytes in _ttsStreamService.GetAudioStreamAsync(CreateSingleItemStream(syncResponse), syncTtsCt, ttsVoice))
                            {
                                await _voicePusher.PushAudioChunkAsync(session.Id, Convert.ToBase64String(audioBytes), ttsVoice.ToString(), syncTtsCt);
                            }
                        }
                        catch (OperationCanceledException) { /* İptal edildi */ }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Sync TTS Error in Orchestrator");
                        }
                        finally { _sessionManager.EndSession(session.Id); }
                    }, syncTtsCt);
                }
            }

            yield return syncResponse;
            yield break;
        }

        // 2B. Dinamik Yönlendirme (Supervisor Intent Check)
        var actionRoute = await _supervisor.DetermineActionRouteAsync(content, session.Messages);
        _logger.LogInformation("[Orchestrator] Supervisor Route Kararı: {Route}", actionRoute);

        // Faz 16: Supervisor sinyalini TutorAgent'a ilet (Closed-Loop)
        string? supervisorHint = actionRoute switch
        {
            "QUIZ" => null, // Quiz akışı ayrı handle ediliyor
            _ => actionRoute // TUTOR, CONFUSED, CHANGE_TOPIC vs.
        };

        if (actionRoute == "QUIZ" && session.CurrentState == SessionState.Learning)
        {
            _logger.LogInformation("[Orchestrator] Kullanıcı organik olarak quiz talep etti. Durum QuizPending'e çekiliyor.");
            session.CurrentState = SessionState.QuizPending;
            // Veri kaydedilir ancak akış Tutor'un pekiştirme veya soru hazırlama mesajına devredilir (Aşağıda isQuizPending = true olarak algılar)
        }

        // Varsayılan: Normal ders anlatımı — gerçek zamanlı STREAM
        bool isQuizPending = session.CurrentState == SessionState.QuizPending;
        
        CancellationToken ttsCt = default;
        var ttsChannel = System.Threading.Channels.Channel.CreateUnbounded<ParsedPodcastChunk>();

        if (isVoiceMode)
        {
            ttsCt = _sessionManager.StartSession(session.Id);

            // Ses Akışını Arka Planda Başlat (Sıralı İşleme)
            _ = Task.Run(async () =>
            {
                try
                {
                    var sentenceBuffer = new System.Text.StringBuilder();
                    var currentVoice = TtsVoice.Hoca;
                    
                    await foreach (var chunk in ttsChannel.Reader.ReadAllAsync(ttsCt))
                    {
                        if (string.IsNullOrWhiteSpace(chunk.Text)) continue;

                        if (chunk.Voice != currentVoice)
                        {
                            // Flush previous voice buffer
                            var leftOver = sentenceBuffer.ToString().Trim();
                            if (!string.IsNullOrWhiteSpace(leftOver))
                            {
                                await foreach (var audioBytes in _ttsStreamService.GetAudioStreamAsync(leftOver, ttsCt, currentVoice))
                                    await _voicePusher.PushAudioChunkAsync(session.Id, Convert.ToBase64String(audioBytes), currentVoice == TtsVoice.Hoca ? "Hoca" : "Asistan", ttsCt);
                            }
                            sentenceBuffer.Clear();
                            currentVoice = chunk.Voice;
                        }

                        sentenceBuffer.Append(chunk.Text);
                        
                        if (chunk.Text.Contains(".") || chunk.Text.Contains("?") || chunk.Text.Contains("!") || chunk.Text.Contains("\n"))
                        {
                            var sentence = sentenceBuffer.ToString().Trim();
                            sentenceBuffer.Clear();

                            if (!string.IsNullOrWhiteSpace(sentence))
                            {
                                await foreach (var audioBytes in _ttsStreamService.GetAudioStreamAsync(sentence, ttsCt, currentVoice))
                                    await _voicePusher.PushAudioChunkAsync(session.Id, Convert.ToBase64String(audioBytes), currentVoice == TtsVoice.Hoca ? "Hoca" : "Asistan", ttsCt);
                            }
                        }
                    }

                    if (sentenceBuffer.Length > 0)
                    {
                        var finalLeftOver = sentenceBuffer.ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(finalLeftOver))
                        {
                            await foreach (var audioBytes in _ttsStreamService.GetAudioStreamAsync(finalLeftOver, ttsCt, currentVoice))
                                await _voicePusher.PushAudioChunkAsync(session.Id, Convert.ToBase64String(audioBytes), currentVoice == TtsVoice.Hoca ? "Hoca" : "Asistan", ttsCt);
                        }
                    }
                }
                catch (OperationCanceledException) { /* Öğrenci sözü kesti */ }
                catch (Exception ex) { _logger.LogError(ex, "Sıralı TTS Streaming Arka Plan Hatası"); }
            }, ttsCt);
        }

        // LLM Metin Akışı (her zaman çalışır)
        // FAZ 1: Metin modu için de CancellationToken bağlanıyor (Barge-in desteği)
        CancellationToken textModeCt = default;
        if (!isVoiceMode)
        {
            textModeCt = _sessionManager.StartTextSession(session.Id);
        }
        var streamCt = isVoiceMode ? ttsCt : textModeCt;
        var rawLlmStream = _tutorAgent.GetResponseStreamAsync(userId, content, session, isQuizPending, isVoiceMode, goalContext: session.Topic?.PhaseMetadata, ct: streamCt);
        
        if (isVoiceMode)
        {
            var parsedStream = VoicePodcastParser.ParseStreamAsync(rawLlmStream, streamCt);
            var enumerator = parsedStream.GetAsyncEnumerator(streamCt);
            bool hasNext = true;
            
            while (hasNext)
            {
                ParsedPodcastChunk? parsedChunk = null;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                    if (hasNext) parsedChunk = enumerator.Current;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Tutor LLM akışı kesildi - Öğrenci araya girdi. SessionId: {SessionId}", session.Id);
                    break;
                }

                if (!hasNext || parsedChunk is null) break;

                fullResponse += parsedChunk.RawChunk;
                yield return parsedChunk.RawChunk; // Metne ham halini akıt (UI script edit görsün)

                if (!string.IsNullOrEmpty(parsedChunk.Text))
                {
                    ttsChannel.Writer.TryWrite(parsedChunk);
                }
            }
            ttsChannel.Writer.Complete();
            await enumerator.DisposeAsync();
        }
        else
        {
            var enumerator = rawLlmStream.GetAsyncEnumerator(streamCt);
            bool hasNext = true;

            while (hasNext)
            {
                string? chunk = null;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                    if (hasNext) chunk = enumerator.Current;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Tutor LLM akışı kesildi - Öğrenci araya girdi. SessionId: {SessionId}", session.Id);
                    break;
                }

                if (!hasNext) break;

                if (chunk != null)
                {
                    fullResponse += chunk;
                    yield return chunk;
                }
            }
            await enumerator.DisposeAsync();
        }
        if (isVoiceMode)
        {
            // Stream bitti, kalan parçaların TTS'e iletilmesi için kanalı kapat
            ttsChannel.Writer.TryComplete();
        }

        // 3. POST-STREAM: SAVE TO DB & BACKGROUND TASKS
        // FAZ 1 Rollback: Barge-in ile kesilen yarım cevabı DB'ye kaydetme
        bool wasInterrupted = !isVoiceMode && streamCt != default &&
                               streamCt.IsCancellationRequested;

        if (!string.IsNullOrEmpty(fullResponse) && !wasInterrupted)
        {
            var msgId = await SaveAiMessage(session, userId, fullResponse);
            TriggerBackgroundTasks(session, userId, content, fullResponse, msgId);
        }
        else if (wasInterrupted)
        {
            _logger.LogInformation(
                "[Orchestrator] Barge-in: Yarım cevap DB'ye kaydedilmedi. SessionId={SessionId}",
                session.Id);
        }
        
        // Sesli sınıf oturumunu temizle (Memory Leak önlemi)
        if (isVoiceMode) _sessionManager.EndSession(session.Id);
        // Metin oturumunu temizle
        if (!isVoiceMode) _sessionManager.EndSession(session.Id);
    }

    private async IAsyncEnumerable<string> CreateSingleItemStream(string item)
    {
        yield return item;
        await Task.CompletedTask;
    }

    private void TriggerBackgroundTasks(Session session, Guid userId, string content, string aiResponse, Guid aiMessageId, string agentRole = "TutorAgent")
    {
        var capturedTopicId = session.TopicId;
        var capturedSessionId = session.Id;
        var capturedAgentRole = agentRole;
        // ICorrelationContext Scoped'dır — Task.Run dışında capture edilmeli (scope güvenliği)
        var capturedCorrelationId = _correlationContext.CorrelationId;

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var summarizer = scope.ServiceProvider.GetRequiredService<ISummarizerAgent>();
            var analyzer = scope.ServiceProvider.GetRequiredService<IAnalyzerAgent>();
            var evaluator = scope.ServiceProvider.GetRequiredService<IEvaluatorAgent>();
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

            _logger.LogInformation(
                "[Background] EvaluatorAgent başladı. Session={SessionId} Correlation={CorrelationId}",
                capturedSessionId, capturedCorrelationId);

            try
            {
                // 1. LLMOps Evaluator — ilgili ajan yanıtını değerlendir
                //    topicId geçilir: puan >= 9 ise Altın Örnek kaydedilir (Faz 12, yalnızca TutorAgent)
                var topicEntityForEval = capturedTopicId.HasValue ? await db.Topics.FindAsync(capturedTopicId.Value) : null;
                var (score, feedback) = await evaluator.EvaluateInteractionAsync(
                    capturedSessionId, content, aiResponse, capturedAgentRole,
                    topicId: capturedTopicId,
                    goalContext: topicEntityForEval?.PhaseMetadata);

                var eval = new AgentEvaluation
                {
                    SessionId = capturedSessionId,
                    UserId = userId,
                    MessageId = aiMessageId,
                    AgentRole = capturedAgentRole,
                    UserInput = content,
                    AgentResponse = aiResponse,
                    EvaluationScore = score,
                    EvaluatorFeedback = feedback,
                    CreatedAt = DateTime.UtcNow
                };
                db.AgentEvaluations.Add(eval);
                await db.SaveChangesAsync();

                _logger.LogInformation(
                    "[Background] EvaluatorAgent tamamlandı. Puan={Score}/10 Correlation={CorrelationId}",
                    score, capturedCorrelationId);

                // 2. Konu anlatımı tamamlandıysa wiki üret
                _logger.LogInformation(
                    "[Background] AnalyzerAgent başladı. Session={SessionId} Correlation={CorrelationId}",
                    capturedSessionId, capturedCorrelationId);

                var msgs = await db.Messages.Where(m => m.SessionId == capturedSessionId).OrderBy(m => m.CreatedAt).ToListAsync();
                var analyzerResult = await analyzer.AnalyzeCompletionAsync(msgs);

                _logger.LogInformation(
                    "[Background] AnalyzerAgent tamamlandı. IsComplete={IsComplete} MsgCount={Count} Correlation={CorrelationId}",
                    analyzerResult.IsComplete, msgs.Count, capturedCorrelationId);

                // Faz 15: Yaşayan Organizasyon — Öğrenci profilini kaydet
                if (capturedTopicId.HasValue && analyzerResult.IntentData != null)
                {
                    var redisService = scope.ServiceProvider.GetRequiredService<IRedisMemoryService>();
                    await redisService.RecordStudentProfileAsync(
                        capturedTopicId.Value,
                        analyzerResult.IntentData.UnderstandingScore,
                        analyzerResult.IntentData.Weaknesses);
                }

                // Wiki özeti üret: konu tamamlandıysa VEYA yeterli mesaj biriktiğinde (her 10 mesajda)
                var shouldSummarize = analyzerResult.IsComplete || (msgs.Count > 0 && msgs.Count % 10 == 0);

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
                    // Wiki içeriğini geri okuyarak EvaluatorAgent'a gönder — DB'ye kaydeder, gold example değil
                    try
                    {
                        var wikiService = scope.ServiceProvider.GetRequiredService<IWikiService>();
                        var wikiContent = await wikiService.GetWikiFullContentAsync(capturedTopicId.Value, userId);
                        var topicEntity = await db.Topics.FindAsync(capturedTopicId.Value);
                        var topicTitle = topicEntity?.Title ?? "Konu";

                        if (!string.IsNullOrWhiteSpace(wikiContent))
                        {
                            var (wikiScore, wikiFeedback) = await evaluator.EvaluateInteractionAsync(
                                capturedSessionId, topicTitle, wikiContent, "SummarizerAgent", topicId: capturedTopicId, goalContext: topicEntity?.PhaseMetadata);

                            db.AgentEvaluations.Add(new AgentEvaluation
                            {
                                SessionId = capturedSessionId,
                                UserId = userId,
                                MessageId = aiMessageId,
                                AgentRole = "SummarizerAgent",
                                UserInput = topicTitle,
                                AgentResponse = wikiContent.Length > 500 ? wikiContent[..500] + "..." : wikiContent,
                                EvaluationScore = wikiScore,
                                EvaluatorFeedback = wikiFeedback,
                                CreatedAt = DateTime.UtcNow
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
                            "[Background] SummarizerAgent değerlendirmesi başarısız — ana akış etkilenmedi. Correlation={CorrelationId}",
                            capturedCorrelationId);
                    }
                }
                else if (analyzerResult.IsComplete && !capturedTopicId.HasValue)
                {
                    _logger.LogWarning(
                        "[Background] IsComplete=true ama TopicId null — SummarizerAgent atlandı. Correlation={CorrelationId}",
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
        });
    }

    // ── Yardımcı: AI mesajını DB'ye kaydet ──────────────────────────────────
    private async Task<Guid> SaveAiMessage(Session session, Guid userId, string content)
    {
        // Whitelist dışı görsel URL'lerini ayıkla (prompt kuralı + ikinci savunma).
        content = ContentSanitizer.SanitizeImages(content);

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
            MessageType = (!string.IsNullOrEmpty(content) && (content.Contains("```json") || content.Contains("```quiz") ||
                           (content.TrimStart().StartsWith("[{") && content.Contains("\"question\""))))
                          ? MessageType.Quiz : MessageType.General
        };
        _db.Messages.Add(aiMsg);
        session.Messages ??= new List<Message>();
        session.Messages.Add(aiMsg);

        // Session toplamlarını güncelle (Dashboard maliyet widget'ı için)
        session.TotalTokensUsed += tokens;
        session.TotalCostUSD += cost;

        await _db.SaveChangesAsync();
        return aiMsg.Id;
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
        else if (session.CurrentState == SessionState.PlanDiagnosticMode)
        {
            var result = await HandlePlanDiagnosticModeAsync(userId, content, session);
            aiResponse = result.Response;
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
        session.TotalCostUSD += aiCost;

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

            _ = Task.Run(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
                var analyzer = scope.ServiceProvider.GetRequiredService<IAnalyzerAgent>();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

                try
                {
                    var msgs = await db.Messages.Where(m => m.SessionId == sId).OrderBy(m => m.CreatedAt).ToListAsync();
                    var analyzerRes = await analyzer.AnalyzeCompletionAsync(msgs);

                    if (analyzerRes.IsComplete && tId.HasValue)
                    {
                        var topicService = scope.ServiceProvider.GetRequiredService<ITopicService>();
                        var tutorAgentScoped = scope.ServiceProvider.GetRequiredService<ITutorAgent>();

                        await HandleTopicProgressionAsync(
                            sId, tId.Value, userId, db, topicService, tutorAgentScoped, mediator);
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
            MessageId = aiMsg.Id,
            SessionId = session.Id,
            TopicId = session.TopicId ?? Guid.Empty,
            Content = aiResponse,
            Role = "assistant",
            CreatedAt = aiMsg.CreatedAt,
            ModelUsed = "Tutor-Agent",
            MessageType = (isQuizMessage ? MessageType.Quiz : MessageType.General).ToString().ToLowerInvariant(),
            WikiUpdated = wikiUpdated,
            PlanCreated = planCreated,
            WikiPageId = activeWikiPageId,
            IsNewTopic = isNewTopic,
            TopicTitle = isNewTopic ? (await _db.Topics.FindAsync(session.TopicId))?.Title : null
        };
    }

    private async Task<(string Response, bool WikiUpdated)> HandleAwaitingChoiceStateAsync(Guid userId, string content, Session session)
    {
        // AwaitingChoice artık plan onayı için kullanılıyor
        var lowerContent = content.ToLowerInvariant();
        bool wantsDeepPlan = lowerContent.Contains("1") || lowerContent.Contains("plan") ||
                             lowerContent.Contains("evet") || lowerContent.Contains("yes") ||
                             lowerContent.Contains("yapalım") || lowerContent.Contains("olur") ||
                             lowerContent.Contains("tamam");
        bool wantsChat = lowerContent.Contains("2") || lowerContent.Contains("hayır") ||
                             lowerContent.Contains("devam") || lowerContent.Contains("sohbet") ||
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

                var responseText = $"Senin için en uygun öğrenme yolunu çizebilmem için **20 soruluk** kapsamlı bir seviye testi yapacağım. 🎯\n\n" +
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

        // Belirsiz yanıt — Learning'e dön, doğal sohbete devam et
        session.CurrentState = SessionState.Learning;
        await _db.SaveChangesAsync();
        return (await _tutorAgent.GetResponseAsync(userId, content, session, false), false);
    }

    private async Task<string> HandleLearningStateAsync(Guid userId, string content, Session session)
    {
        // ── /plan komutu: müfredat planı teklifi ────────────────────────────────
        bool wantsPlan = content.Contains("/plan", StringComparison.OrdinalIgnoreCase) ||
                         content.Contains("plan yap", StringComparison.OrdinalIgnoreCase) ||
                         content.Contains("müfredat", StringComparison.OrdinalIgnoreCase);

        if (wantsPlan)
        {
            return await TriggerBaselineQuizForPlanAsync(userId, content, session).ContinueWith(t => t.Result.Response);
        }

        // ── "Anladım" / "Konuyu Geç" tespiti ───────────────────────────────────
        // Faz 16: String-match + Supervisor AI intent birleşik karar — konu geçiş tespiti
        bool wantsToAdvance =
            content.Contains("anladım", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("konuyu geç", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("sıradaki konu", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("geçelim", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("öğrendim", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("kavradım", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("tamam anladım", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("bitti", StringComparison.OrdinalIgnoreCase);

        // Supervisor AI kararını da dahil et — "Bu konuyu kavradım artık" gibi serbest formu yakalar
        if (!wantsToAdvance && session.Messages?.Any() == true)
        {
            try
            {
                var supervisorRoute = await _supervisor.DetermineActionRouteAsync(content, session.Messages);
                if (supervisorRoute == "UNDERSTOOD" || supervisorRoute == "CHANGE_TOPIC")
                {
                    wantsToAdvance = true;
                    _logger.LogInformation("[Orchestrator] Supervisor AI konu geçiş sinyali verdi: {Route}", supervisorRoute);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Orchestrator] Supervisor intent check başarısız — string-match fallback'e devam.");
            }
        }

        if (wantsToAdvance && session.CurrentState == SessionState.Learning && session.TopicId.HasValue)
        {
            var currentTopic = await _db.Topics.FindAsync(session.TopicId);
            // Sadece alt başlıklarda quiz tetikle (parent topic'te değil)
            if (currentTopic?.ParentTopicId != null)
            {
                _logger.LogInformation("[QUIZ] Konu geçiş talebi. TopicId={Id}, Title={Title}",
                    currentTopic.Id, currentTopic.Title);

                // Profil bazlı soru sayısı: ExamPrep/Academic → 10 soru, hobi → 3 soru, standart → 5 soru
                var quizUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
                int questionCount = quizUser?.LearningGoal switch
                {
                    LearningGoal.ExamPrep or LearningGoal.Academic or LearningGoal.Certification => 10,
                    LearningGoal.Hobby => 3,
                    _ => 5
                };

                // Faz 16: Öğrencinin zayıf yönlerini Redis'ten çek → Quiz'i bu eksiklere odakla (Adaptive Quiz)
                string? weaknessContext = null;
                string? researchContext = null;
                try
                {
                    using var quizScope = _scopeFactory.CreateScope();
                    var redis = quizScope.ServiceProvider.GetRequiredService<IRedisMemoryService>();
                    
                    // Adaptive Quiz: Zayıf yön odağı
                    var studentProfile = await redis.GetStudentProfileAsync(currentTopic.Id);
                    if (studentProfile.HasValue && !string.IsNullOrWhiteSpace(studentProfile.Value.weaknesses))
                    {
                        weaknessContext = studentProfile.Value.weaknesses;
                        _logger.LogInformation("[QUIZ] Adaptive: Öğrenci zayıf yönleri tespit edildi → quiz odaklanıyor. Weaknesses={W}", weaknessContext);
                    }
                    
                    // Korteks raporu varsa quiz'e güncel bilgi kaynağı olarak ekle
                    var korteksReport = await redis.GetKorteksResearchReportAsync(currentTopic.Id);
                    if (!string.IsNullOrWhiteSpace(korteksReport))
                    {
                        // Raporu çok uzunsa sadece ilk 2000 karakteri al (prompt limit)
                        researchContext = korteksReport.Length > 2000 
                            ? korteksReport[..2000] + "\n...[rapor kısaltıldı]"
                            : korteksReport;
                        _logger.LogInformation("[QUIZ] Korteks araştırma raporu quiz'e dahil ediliyor. TopicId={TopicId}", currentTopic.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[QUIZ] Redis weakness/korteks fetch başarısız — standart quiz üretilecek.");
                }

                var quizJson = await _tutorAgent.GenerateTopicQuizAsync(currentTopic.Title, userId, currentTopic.Id, goalContext: currentTopic.PhaseMetadata, researchContext: researchContext, questionCount: questionCount, weaknessContext: weaknessContext);
                session.PendingQuiz = quizJson;
                session.CurrentState = SessionState.QuizMode;
                await _db.SaveChangesAsync();

                var countLabel = questionCount == 3 ? "3" : questionCount == 10 ? "10" : "5";
                return $"Harika! Seni tebrik etmeden önce bu konuyu tam pekiştirdiğimizden emin olmak için **{countLabel} soruluk** küçük bir testimiz var 🎯\n\n```quiz\n{quizJson}\n```";
            }
        }

        // Mevcut QuizPending kabulü
        if (session.CurrentState == SessionState.QuizPending &&
            (content.Contains("evet", StringComparison.OrdinalIgnoreCase) ||
             content.Contains("başla", StringComparison.OrdinalIgnoreCase)))
        {
            session.CurrentState = SessionState.QuizMode;
            await _db.SaveChangesAsync();
            return $"Harika! İşte senin için hazırladığım test:\n\n```quiz\n{session.PendingQuiz}\n```";
        }

        return await _tutorAgent.GetResponseAsync(
            userId, content, session,
            session.CurrentState == SessionState.QuizPending);
    }

    // ── Quiz cevabı değerlendirme + konu geçişi ──────────────────────────────
    private async Task<(string Response, bool WikiUpdated)> HandleQuizModeAsync(
        Guid userId, string content, Session session)
    {
        bool isSkipped = content.Contains("[SKIP_QUIZ]");
        bool isFinished = content.Contains("Testi Tamamlandı") || content.Contains("Quiz Cevabım");
        // IDE'den "Hocaya Gönder" ile gelen kodlama cevabı: Quiz Sorusu başlığı + kod bloğu içerir.
        bool isIDESubmission = !isFinished && content.Contains("**Quiz Sorusu:**") && content.Contains("```");

        double score = 0;
        int total = 1;
        string aiFeedback = string.Empty;

        if (isSkipped)
        {
            _logger.LogInformation("[QUIZ] Kullanıcı testi atladı.");
        }
        else if (isFinished)
        {
            // "3/5 Doğru" özetinden skoru çıkar. Slash'ten sonraki boşluk çeşitli olabilir.
            var match = System.Text.RegularExpressions.Regex.Match(content, @"(\d+)\s*/\s*(\d+)\s*Doğru");
            if (match.Success)
            {
                score = int.Parse(match.Groups[1].Value);
                total = int.Parse(match.Groups[2].Value);
            }
            else
            {
                // Tek soru vakası
                score = content.Contains("Doğru") ? 1 : 0;
            }
        }
        else if (isIDESubmission)
        {
            // Kodlama cevabını TutorAgent ile değerlendir, tek soru akışı gibi işle.
            _logger.LogInformation("[QUIZ] IDE kod cevabı değerlendiriliyor. SessionId={SessionId}", session.Id);
            
            // Faz 16: Piston çalıştırma sonucunu Redis'ten çek → değerlendirmeye dahil et
            string enrichedContent = content;
            try
            {
                using var ideScope = _scopeFactory.CreateScope();
                var redis = ideScope.ServiceProvider.GetRequiredService<IRedisMemoryService>();
                var pistonResult = await redis.GetLastPistonResultAsync(session.Id);
                if (!string.IsNullOrEmpty(pistonResult))
                {
                    enrichedContent += $"\n\n[KOD ÇALIŞTIRMA SONUCU (Piston Sandbox)]:\n{pistonResult}";
                    _logger.LogInformation("[QUIZ] Piston sonucu değerlendirmeye dahil edildi.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[QUIZ] Piston sonucu Redis'ten okunamadı — salt kod üzerinden değerlendirme yapılacak.");
            }
            
            var result = await _tutorAgent.EvaluateQuizAnswerAsync(session.PendingQuiz ?? "Unknown", enrichedContent, goalContext: session.Topic?.PhaseMetadata);
            score = result.score;
            aiFeedback = result.feedback;
            total = 1;
        }
        else
        {
            // Legacy tek soru step-by-step
            var result = await _tutorAgent.EvaluateQuizAnswerAsync(session.PendingQuiz ?? "Unknown", content, goalContext: session.Topic?.PhaseMetadata);
            score = result.score;
            aiFeedback = result.feedback;
            
            if (score <= 0.3) // Eskiden !isCorrect idi. %30 ve altı zayıf kabul ediliyor.
            {
                return ($"{aiFeedback}\n\nTekrar deneyelim:\n**{session.PendingQuiz}**", false);
            }
            total = 1;
        }


        // ── PROCESS RESULTS & TRANSITION ──────────────────────────────────
        var user = await _db.Users.FindAsync(userId);
        var currentTopic = session.TopicId.HasValue ? await _db.Topics.FindAsync(session.TopicId.Value) : null;

        if (currentTopic != null)
        {
            var orderedLessons = await _topicService.GetOrderedLessonsAsync(currentTopic.Id, userId);
            var activeSubTopic = orderedLessons.Skip(currentTopic.CompletedSections).FirstOrDefault();

            if (activeSubTopic != null)
            {
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
                        }
                    }
                    else
                    {
                        if (user != null) user.LastActiveDate = DateTime.UtcNow;
                        _logger.LogInformation("[QUIZ] Score: {Score}/{Total}. Eski rekor ({Prev}) başarıyla korundu, ilerleniyor.", score, total, previousScore);
                    }
                }

                // Alt konu tamamlandı — wiki üretimini + öğrenci profilini arka planda başlat
                var completedSubtopicId = activeSubTopic.Id;
                var capturedSessionId = session.Id;
                var capturedUserId = userId;
                var capturedScore = (int)((double)score / total * 10); // 0-10 anlayış skoru
                _ = Task.Run(async () =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    var summarizer = scope.ServiceProvider.GetRequiredService<ISummarizerAgent>();
                    var redis = scope.ServiceProvider.GetRequiredService<IRedisMemoryService>();
                    try
                    {
                        await summarizer.SummarizeAndSaveWikiAsync(capturedSessionId, completedSubtopicId, capturedUserId);

                        // Yaşayan Organizasyon: öğrenci anlayış skorunu kaydet (🔑 Daha önce çağrılmıyordu)
                        var weaknesses = score < total
                            ? $"{total - score}/{total} soru yanlış — Konu: {activeSubTopic.Title}"
                            : string.Empty;
                        await redis.RecordStudentProfileAsync(completedSubtopicId, capturedScore, weaknesses);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[QuizMode] Alt konu wiki/profil arka plan görevi başarısız. SubtopicId={SubtopicId}", completedSubtopicId);
                    }
                });
            }

            bool passedQuiz = isSkipped || total == 0 || ((double)score / total) >= 0.6;

            if (passedQuiz)
            {
                // Parent Topic (Ana Konu) İlerlemesini Güncelle
                if (currentTopic.TotalSections > 0)
                {
                    int nextCompleted = currentTopic.CompletedSections + 1;
                    currentTopic.ProgressPercentage = (double)nextCompleted / currentTopic.TotalSections * 100;
                    if (nextCompleted >= currentTopic.TotalSections) 
                    {
                        currentTopic.IsMastered = true;
                        
                        // Modül Bitti: Toplu modül wiki özetini başlat
                        var parentTopicIdToSummarize = currentTopic.Id;
                        var finalUserId = userId;
                        _ = Task.Run(async () =>
                        {
                            using var scope = _scopeFactory.CreateScope();
                            var summarizer = scope.ServiceProvider.GetRequiredService<ISummarizerAgent>();
                            await summarizer.SummarizeModuleAsync(parentTopicIdToSummarize, finalUserId);
                        });
                    }
                }
            }

            if (passedQuiz)
            {
                session.CurrentState = SessionState.Learning;
                session.PendingQuiz = null;
                await _db.SaveChangesAsync();

                // ── TRANSITION TO NEXT TOPIC ──────────────────────────────────────
                var (nextResponse, _) = await TransitionToNextTopicAsync(userId, session);

                string scoreText = total == 1 ? $"{(int)(score * 10)}/10" : $"{score}/{total}";
                string feedbackText = !string.IsNullOrEmpty(aiFeedback) && !isFinished ? $"\n\n*Hocanın Notu:* {aiFeedback}\n" : "";

                string prefix = isSkipped
                    ? "Peki, testi atlıyoruz. Sıradaki konuya geçiyoruz... 🚀"
                    : $"Test için teşekkürler! Skorun: **{scoreText}**.{feedbackText}\nBakalım sırada ne var... 🎓";

                return ($"{prefix}\n\n{nextResponse}", false);
            }
            else
            {
                // ── OFFER REMEDIAL LESSON ────────────────────────────────
                session.CurrentState = SessionState.RemedialOfferPending;
                // PendingQuiz içine aktif alt konunun ID'sini veya adını kaydederek durumu koruyalım:
                session.PendingQuiz = activeSubTopic?.Title ?? "Bilinmeyen Konu"; 
                await _db.SaveChangesAsync();
                
                string scoreText = total == 1 ? $"{(int)(score * 10)}/10" : $"{score}/{total}";
                string feedbackText = !string.IsNullOrEmpty(aiFeedback) && !isFinished ? $"\n\n*Hocanın Notu:* {aiFeedback}" : "";

                string failurePrefix = $"Test bitti! Skorun: **{scoreText}**.{feedbackText}\n\nGörünüşe göre bu konuda bazı eksiklerimiz var.\n" +
                                       $"Sıradaki konuya geçmeden önce bu eksiklerini kapatman için sana **özel bir telafi dersi** oluşturmamı ister misin?[REMEDIAL_OFFER]";

                return (failurePrefix, false);
            }
        }

        // Fallback
        session.CurrentState = SessionState.Learning;
        session.PendingQuiz = null;
        await _db.SaveChangesAsync();
        return ($"Durum kaydedildi (Tema yansımadı).", false);
    }

    private async Task<(string Response, bool WikiUpdated)> HandleRemedialOfferPendingAsync(Guid userId, string content, Session session)
    {
        bool accepted = content.Contains("[REMEDIAL_ACCEPT]") ||
                        content.Contains("evet", StringComparison.OrdinalIgnoreCase) || 
                        content.Contains("olur", StringComparison.OrdinalIgnoreCase) ||
                        content.Contains("isterim", StringComparison.OrdinalIgnoreCase) ||
                        content.Contains("başla", StringComparison.OrdinalIgnoreCase);

        var subTopicTitle = session.PendingQuiz ?? "Bilinmeyen Konu";
        
        session.CurrentState = SessionState.Learning;
        session.PendingQuiz = null;
        await _db.SaveChangesAsync();

        if (accepted)
        {
            // Kullanıcı telafi dersi istedi
            using var scope = _scopeFactory.CreateScope();
            var redis = scope.ServiceProvider.GetRequiredService<IRedisMemoryService>();
            
            var profile = await redis.GetStudentProfileAsync(session.TopicId ?? Guid.Empty);
            string weaknesses = profile?.weaknesses ?? "Genel konu eksikleri";
            
            string remedialLesson = await _tutorAgent.GetRemedialLessonAsync(subTopicTitle, weaknesses);
            return ($"Harika, hemen eksiklerimizi kapatalım! 🔍\n\n{remedialLesson}", false);
        }
        else
        {
            // İstemedi, sonraki konuya geçelim
            var currentTopic = await _db.Topics.FindAsync(session.TopicId);
            if (currentTopic != null && currentTopic.TotalSections > 0)
            {
                int nextCompleted = currentTopic.CompletedSections + 1;
                currentTopic.ProgressPercentage = (double)nextCompleted / currentTopic.TotalSections * 100;
                if (nextCompleted >= currentTopic.TotalSections) 
                {
                    currentTopic.IsMastered = true;
                    var parentTopicIdToSummarize = currentTopic.Id;
                    var finalUserId = userId;
                    _ = Task.Run(async () =>
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var summarizer = scope.ServiceProvider.GetRequiredService<ISummarizerAgent>();
                        await summarizer.SummarizeModuleAsync(parentTopicIdToSummarize, finalUserId);
                    });
                }
                await _db.SaveChangesAsync();
            }

            var (nextResponse, _) = await TransitionToNextTopicAsync(userId, session);
            return ($"Peki, telafiyi geçiyoruz. Sıradaki konumuz gelsin... 🚀\n\n{nextResponse}", false);
        }
    }

    // ── Baseline Quiz değerlendirme (Multi-Round: 5 Soru) ──────────────────────────────
    private async Task<(string Response, bool WikiUpdated, bool PlanCreated)> HandleBaselineQuizModeAsync(
        Guid userId, string content, Session session)
    {
        int totalQuestions = 20; // Default
        string correctEmoji = "✅";
        string feedbackLine = "Tebrikler, seviye testi tamamlandı!";
        string? failedTopics = null;

        // Check if user requested to skip the initial diagnostic
        if (content.Trim().Equals("[SKIP_QUIZ_BASELINE]", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("[BASELINE QUIZ] Kullanıcı testi atladı. Temel seviyeden başlatılıyor.");
            session.BaselineQuizIndex = totalQuestions;
            session.BaselineCorrectCount = 0; // ratio will be 0 -> Başlangıç seviyesi
        }
        else if (content.Contains("**Seviye Testi Tamamlandı:**"))
        {
            var correctCount = 0;

            // Try to extract the score like "15/20 Doğru"
            var match = System.Text.RegularExpressions.Regex.Match(content, @"(\d+)/(\d+)\sDoğru");
            if (match.Success)
            {
                correctCount = int.Parse(match.Groups[1].Value);
                totalQuestions = int.Parse(match.Groups[2].Value);
            }

            // Hata yapılan konuları çek
            var failMatch = System.Text.RegularExpressions.Regex.Match(content, @"Hata Yapılan Konular:\s*(.+)");
            if (failMatch.Success)
            {
                failedTopics = failMatch.Groups[1].Value.Trim();
                _logger.LogInformation("[BASELINE QUIZ] Başarısız Konular Algılandı: {FailedTopics}", failedTopics);
            }

            session.BaselineQuizIndex = totalQuestions;
            session.BaselineCorrectCount = correctCount;
        }
        else
        {
            // Fallback for legacy step-by-step
            totalQuestions = session.BaselineQuizData != null ? System.Text.Json.JsonDocument.Parse(session.BaselineQuizData).RootElement.GetArrayLength() : 5;
            var result = await _tutorAgent.EvaluateQuizAnswerAsync(session.PendingQuiz ?? "", content, goalContext: session.Topic?.PhaseMetadata);
            bool isCorrect = result.score >= 0.5;

            if (isCorrect) session.BaselineCorrectCount++;
            session.BaselineQuizIndex++;

            var currentIndex = session.BaselineQuizIndex;
            if (currentIndex < totalQuestions)
            {
                var nextQuizJson = ExtractNthQuizFromArray(session.BaselineQuizData ?? "[]", currentIndex);
                session.PendingQuiz = ExtractQuizQuestionText(nextQuizJson);
                await _db.SaveChangesAsync();

                return ($"{(isCorrect ? "✅" : "❌")} {(isCorrect ? "Doğru!" : "Yanlış.")}\n\n" +
                        $"**Soru {currentIndex + 1}/{totalQuestions}:**\n\n{nextQuizJson}", false, false);
            }
        }

        // ── Seviye hesapla (Dinamik Yüzde Bazlı) ──────────
        var finalCorrectCount = session.BaselineCorrectCount;
        double ratio = (double)finalCorrectCount / totalQuestions;

        string userLevel;
        string levelEmoji;
        if (ratio < 0.35) // 0-7 / 20
        {
            userLevel = "Başlangıç (Sıfırdan)";
            levelEmoji = "🌱";
        }
        else if (ratio < 0.75) // 8-15 / 20
        {
            userLevel = "Orta (Temelleri biliyor)";
            levelEmoji = "⚡";
        }
        else
        {
            userLevel = "İleri (Konuya hakim)";
            levelEmoji = "🚀";
        }

        _logger.LogInformation("[BASELINE QUIZ] Sonuç: {Correct}/{Total} ({Ratio:P}) → {Level}",
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

        // ── Müfredat oluştur (Hiyerarşik Modüler) ───────────────────────
        var allLessons = await _deepPlanAgent.GenerateAndSaveDeepPlanAsync(
            topic.Id, topic.Title, userId, userLevel, topic.PhaseMetadata, failedTopics);
        await _db.Entry(topic).ReloadAsync();

        // Modül → Ders hiyerarşisini veritabanından çek
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

        // Müfredat render — modül/ders hiyerarşisi
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{correctEmoji} {feedbackLine}\n");
        sb.AppendLine($"### 📊 Seviye Testi Sonucu: **{finalCorrectCount}/{totalQuestions}** — {levelEmoji} {userLevel}\n");

        if (ratio < 0.35)
            sb.AppendLine("Bu konuyu sıfırdan, adım adım ve sana özel bir müfredatla öğreteceğim.\n");
        else if (ratio < 0.75)
            sb.AppendLine("Temelleri biliyorsun! Gereksiz tekrarları atlayıp pratik ve uygulamaya ağırlık veren bir müfredat hazırladım.\n");
        else
            sb.AppendLine("Harika bir skor! İleri düzey konulara odaklanan yoğun bir müfredat hazırladım.\n");

        sb.AppendLine($"---\n\n### 📚 Müfredat ({allLessons.Count} Ders, {modules.Count} Modül)\n");

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
            sb.AppendLine($"\n---\n\n🎒 **Hadi Başlayalım:** Sol menüdeki **\"{firstLesson.Title}\"** dersine tıklayarak interaktif eğitime geçiş yapabilir veya doğrudan 'Başla' yazabilirsin!");
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

            var after = response[nIdx..];
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
        if (currentTopic == null)
        {
            _logger.LogWarning("[TRANSITION] TopicId geçerli ama Topic kaydı bulunamadı. SessionId={SessionId}", session.Id);
            session.CurrentState = SessionState.Learning;
            session.PendingQuiz = null;
            await _db.SaveChangesAsync();
            return ("🎉 Tebrikler! Bu konuyu tamamladın.", true);
        }

        // Sistemin dallanma listesini getir
        var siblings = await _topicService.GetOrderedLessonsAsync(currentTopic.Id, userId);

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
            MessageId = Guid.NewGuid(),
            SessionId = session.Id,
            TopicId = topic.Id,
            Content = quizResponse.Response,
            Role = "assistant",
            CreatedAt = DateTime.UtcNow,
            ModelUsed = "DeepPlan_Quiz",
            MessageType = "quiz",
            WikiUpdated = false,
            WikiPageId = null,
            IsNewTopic = topicId == null,
            TopicTitle = topic.Title,
        };
    }

    private async Task<(string Response, bool WikiUpdated)> TriggerBaselineQuizForPlanAsync(Guid userId, string content, Session session)
    {
        var topic = await _db.Topics.FindAsync(session.TopicId);
        if (topic == null) return ("Hata: Konu bulunamadı.", false);

        try
        {
            string intentPrompt = "Sen bir konu belirleyicisin. Kullanıcının mesajından ÖĞRENMEK İSTEDİĞİ asıl konuyu 2-4 kelime ile çıkar. Eğer ortada bir öğrenme isteği yoksa sadece 'NULL' dön. Örnek: 'C# Algoritmalar', 'Felsefe Tarihi'. Sadece konuyu döndür.";
            var extractedTopic = await _agentFactory.CompleteChatAsync(AgentRole.Supervisor, intentPrompt, content);

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

        _logger.LogInformation("[DeepPlan] Teşhis ve Hedef Belirleme başlatılıyor: {Topic}", topic.Title);

        session.CurrentState = SessionState.PlanDiagnosticMode;
        await _db.SaveChangesAsync();

        var responseText = $"Harika! **{topic.Title}** konusu için detaylı bir öğrenme planı hazırlayacağım. 🚀\n\n" +
                           $"Ancak sana en uygun müfredatı çizebilmem için hedefini tam olarak anlamam gerekiyor.\n" +
                           $"**Bu konuyu ne için çalışmak istiyorsun?** (Örn: KPSS, YKS, Okul sınavı, mülakat hazırlığı veya sadece hobi/genel kültür amaçlı mı?)";

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
            var targetTopic = await _db.Topics.FindAsync(topicId.Value);
            if (targetTopic != null)
            {
                var rootId = targetTopic.ParentTopicId ?? targetTopic.Id;

                var treeTopicIds = await _db.Topics
                    .Where(t => t.Id == rootId || t.ParentTopicId == rootId)
                    .Select(t => t.Id)
                    .ToListAsync();

                session = await _db.Sessions
                    .Where(s => s.TopicId.HasValue && treeTopicIds.Contains(s.TopicId.Value) && s.UserId == userId)
                    .OrderByDescending(s => s.CreatedAt)
                    .FirstOrDefaultAsync();
            }
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

                // EĞER TOPIC VARSA AMA SESSION YOKSA (Alt Derslere ilk kez girildiğinde yaşanır)
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

        var extractionResult = await _topicDetector.ExtractTopicNameAsync(content);
        string titlePreview = extractionResult.Topic.Length > 40 ? extractionResult.Topic[..40] : extractionResult.Topic;
        string resolvedCategory = string.Equals(extractionResult.Category, "Plan", StringComparison.OrdinalIgnoreCase) ? "Plan" : "Genel";

        var (topic, newSession) = await _topicService.CreateDiscoveryTopicAsync(userId, titlePreview, resolvedCategory);

        // ChatGPT-Style Auto Naming (AIServiceChain ile başlık üret)
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

                var generatedTitle = await _agentFactory.CompleteChatAsync(
                    AgentRole.Supervisor,
                    "Sen bir konu başlığı üreticisin. Verilen kısa mesajdan 3-5 kelimelik kısa bir konu başlığı üret. Sadece başlığı yaz. Örnek: 'Python Temelleri', 'React Eğitimi'",
                    content
                );
                if (!string.IsNullOrWhiteSpace(generatedTitle))
                {
                    generatedTitle = generatedTitle.Trim().Trim('"').Trim();
                    if (generatedTitle.Length > 60) generatedTitle = generatedTitle[..60];
                    var t = await db.Topics.FindAsync(topic.Id);
                    if (t != null)
                    {
                        t.Title = generatedTitle;
                        await db.SaveChangesAsync();
                        _logger.LogInformation("[AUTO-NAMING] Başlık güncellendi: {Title}", generatedTitle);
                    }
                }
            }
            catch (Exception ex)
            {
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

        // Hiyerarşi farkında: modül→ders yapısında leaf-level'a iner, düz planda children döner
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
                session.TotalCostUSD += autoCost;

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

    private async Task<(string Response, bool WikiUpdated)> HandlePlanDiagnosticModeAsync(Guid userId, string content, Session session)
    {
        var topic = await _db.Topics.FindAsync(session.TopicId);
        if (topic == null) return ("Hata: Konu bulunamadı.", false);

        string goalContext = "Bilinmeyen Hedef";
        string specificTopic = topic.Title;

        try
        {
            string prompt = $$"""
                Kullanıcı '{{topic.Title}}' konusunda bir eğitim planı istiyor. Kendisine 'Hedefin nedir?' diye sorduk.
                Aşağıdaki cevabına bakarak:
                1. Tam olarak hedefini (Örn: KPSS Lise, YKS Sayısal, İş Görüşmesi, Hobi vb.)
                2. Eğer konu genel bir şeyse ve kullanıcı alt bir başlık verdiyse spesifik alt konuyu (Örn: Kimya denmiş ama kullanıcı 'Organik Kimya' diyorsa 'Organik Kimya')
                çıkar. 
                SADECE şu JSON formatında dön:
                {"goal": "Hedef", "specificTopic": "Spesifik Konu Adı"}
                Eğer çıkaramıyorsan: {"goal": "Genel Kültür", "specificTopic": "{{topic.Title}}"}
                """;

            var analysisJson = await _agentFactory.CompleteChatAsync(AgentRole.Supervisor, prompt, content);
            
            // Clean markdown
            if (analysisJson.Contains("```json"))
            {
                analysisJson = analysisJson.Replace("```json", "").Replace("```", "").Trim();
            }

            using var doc = System.Text.Json.JsonDocument.Parse(analysisJson);
            goalContext = doc.RootElement.TryGetProperty("goal", out var g) ? g.GetString() ?? "Bilinmiyor" : "Bilinmiyor";
            specificTopic = doc.RootElement.TryGetProperty("specificTopic", out var s) ? s.GetString() ?? topic.Title : topic.Title;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DeepPlan] Diagnostik analiz hatası, varsayılan hedef atandı.");
        }

        // Güncelle
        topic.Title = specificTopic;
        topic.PhaseMetadata = goalContext; // PhaseMetadata alanını hedef bağlamı olarak kullanalım.
        await _db.SaveChangesAsync();

        _logger.LogInformation("[DeepPlan] Hedef tespit edildi. Konu: {Topic}, Hedef: {Goal}", topic.Title, goalContext);

        // Şimdi amaca uygun Baseline Quiz oluştur!
        var allQuizJson = await _deepPlanAgent.GenerateBaselineQuizAsync(topic.Title, goalContext);

        session.BaselineQuizData = allQuizJson;
        session.BaselineQuizIndex = 0;
        session.BaselineCorrectCount = 0;
        session.PendingQuiz = null;
        session.CurrentState = SessionState.BaselineQuizMode;
        await _db.SaveChangesAsync();

        var responseText = $"Аnladım! Hedefinin **{goalContext}** olması harika. Planı tam olarak buna göre şekillendireceğim. 🎯\n\n" +
                           $"Seni bu hedefte doğru yönlendirebilmem için **Seviye Testi** yapacağım. Merak etme, sorular {goalContext} formatında olacak.\n\n" +
                           $"İşte testimiz:\n\n```quiz\n{allQuizJson}\n```";

        return (responseText, false);
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // FAZ 1: Barge-In — Metin Modu Kesintisi
    // ──────────────────────────────────────────────────────────────────────────────

    public Task<bool> InterruptStreamAsync(Guid sessionId, string userMessage)
    {
        var interrupted = _sessionManager.InterruptTextSession(sessionId, userMessage);
        if (interrupted)
        {
            _logger.LogWarning(
                "[Orchestrator] Metin akışı kesildi (Barge-in). SessionId={SessionId}",
                sessionId);
        }
        return Task.FromResult(interrupted);
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // FAZ 2: AgentGroupChat — Otonom Sınıf Simülasıyonu
    // ──────────────────────────────────────────────────────────────────────────────

    public async IAsyncEnumerable<string> StartClassroomSessionAsync(
        Guid userId,
        Guid sessionId,
        string topic,
        bool isVoiceMode,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var session = await _db.Sessions
            .Include(s => s.Messages)
            .Include(s => s.Topic)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);

        if (session == null)
        {
            yield return "Oturum bulunamadı.";
            yield break;
        }

        _logger.LogInformation(
            "[Orchestrator] Sınıf simülasıyonu başlatılıyor. Topic={Topic} SessionId={SessionId}",
            topic, sessionId);

        await foreach (var chunk in _classSession.StartAsync(session, topic, ct))
        {
            yield return chunk;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // FAZ 3: Çok Modlu (Multimodal) Mesaj İşleme
    // ──────────────────────────────────────────────────────────────────────────────

    public async IAsyncEnumerable<string> ProcessMultimodalMessageStreamAsync(
        Guid userId,
        List<ContentItemDto> contentItems,
        Guid? topicId,
        Guid? sessionId,
        bool isPlanMode = false,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!sessionId.HasValue)
        {
            yield return "Oturum bulunamadı.";
            yield break;
        }

        var session = await _db.Sessions
            .Include(s => s.Messages)
            .Include(s => s.Topic)
            .FirstOrDefaultAsync(s => s.Id == sessionId.Value && s.UserId == userId, ct);

        if (session == null)
        {
            yield return "Oturum bulunamadı.";
            yield break;
        }

        // ContentItemDto listesini metin + görsel URL'ı olarak ayıkla
        var textParts = contentItems
            .Where(x => x.Type == ContentType.Text && !string.IsNullOrEmpty(x.Text))
            .Select(x => x.Text!)
            .ToList();

        var imageUrls = contentItems
            .Where(x => x.Type == ContentType.ImageUrl && !string.IsNullOrEmpty(x.ImageUrl))
            .Select(x => x.ImageUrl!)
            .ToList();

        // Görsel URL'lerini prompt'a ekle (Semantic Kernel ImageContent alternatiŁi)
        // LLM'in görebileceği format: Metin + Görsel URL birleşimi
        var combinedContent = string.Join(" ", textParts);
        if (imageUrls.Count > 0)
        {
            var imageSection = string.Join("\n", imageUrls.Select((url, i) =>
                $"[Görsel {i + 1}]: {url}"));
            combinedContent = $"{combinedContent}\n\n{imageSection}";
        }

        // Kullanıcı mesajını DB'ye kaydet
        var userMsg = new Message
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            UserId = userId,
            Role = "user",
            Content = combinedContent,
            CreatedAt = DateTime.UtcNow,
            MessageType = MessageType.General
        };
        _db.Messages.Add(userMsg);
        session.Messages ??= new List<Message>();
        session.Messages.Add(userMsg);
        await _db.SaveChangesAsync(ct);

        // Metin modu CancellationToken (Barge-in desteği)
        var streamCt = _sessionManager.StartTextSession(session.Id);
        var fullResponse = new StringBuilder();

        var rawStream = _tutorAgent.GetResponseStreamAsync(
            userId, combinedContent, session,
            isQuizPending: false, isVoiceMode: false,
            goalContext: session.Topic?.PhaseMetadata,
            ct: streamCt);

        var enumerator = rawStream.GetAsyncEnumerator(streamCt);
        bool hasNext = true;

        while (hasNext)
        {
            string? chunk = null;
            try
            {
                hasNext = await enumerator.MoveNextAsync();
                if (hasNext) chunk = enumerator.Current;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("[Multimodal] Akış kesildi - Barge-in. SessionId={SessionId}", session.Id);
                break;
            }

            if (!hasNext) break;
            if (chunk != null)
            {
                fullResponse.Append(chunk);
                yield return chunk;
            }
        }

        await enumerator.DisposeAsync();

        // Barge-in olmadan tamamlandıysa kaydet
        if (fullResponse.Length > 0 && !streamCt.IsCancellationRequested)
        {
            var msgId = await SaveAiMessage(session, userId, fullResponse.ToString());
            TriggerBackgroundTasks(session, userId, combinedContent, fullResponse.ToString(), msgId);
        }

        _sessionManager.EndSession(session.Id);
    }
}
