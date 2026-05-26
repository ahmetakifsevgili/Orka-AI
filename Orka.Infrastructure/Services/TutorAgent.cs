using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orka.Core.DTOs;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;

namespace Orka.Infrastructure.Services;

public class TutorAgent : ITutorAgent
{
    private readonly IContextBuilder _contextBuilder;
    private readonly IAIAgentFactory _factory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGraderAgent _grader;
    private readonly IWikiService _wikiService;
    private readonly ILogger<TutorAgent> _logger;
    private readonly IRedisMemoryService _redisService;
    private readonly ILearningSourceService _learningSourceService;
    private readonly ILearningSignalService _learningSignals;
    private readonly IEducatorCoreService _educatorCore;
    private readonly ITutorPolicyEngine _tutorPolicy;
    private readonly ITutorTurnStateAssembler _turnStateAssembler;
    private readonly ITutorActionPlanner _actionPlanner;
    private readonly ITutorToolOrchestrator _toolOrchestrator;
    private readonly ITeachingArtifactService _teachingArtifacts;
    private readonly ITutorReflectionService _tutorReflection;
    private readonly ITutorPedagogyEvaluationService _tutorPedagogyEvaluation;
    private readonly ITutorPedagogyQualityGate _tutorPedagogyQualityGate;

    // Wiki context için maksimum karakter sınırı (yaklaşık 1000 token)
    private const int WikiContextMaxChars = 4000;

    // V4: zengin görselleştirme + voice + drill-down kuralları (interpolation'sız raw).
    // LaTeX/JSON-style süslü parantez kaçışı bu blokta gerekmiyor.
    private const string V4VisualizationAndVoiceBlock = """

            [V4 ZENGİN GÖRSELLEŞTİRME — ACTION PLAN'A BAĞLI]:
            Anlatımını metinle sınırlama; ancak görsel, tablo, Mermaid veya resim kararını öncelikle [TUTOR ACTION PLAN v3] ve [TEACHING ARTIFACT v3] belirler.

            1. MATEMATİK / FORMÜL → LaTeX kullan (frontend KaTeX ile render eder):
               - Inline:  $E = mc^2$
               - Block:   $$\int_0^\infty e^{-x^2} dx = \frac{\sqrt{\pi}}{2}$$
               Hiçbir formülü düz metin olarak yazma; "x kare" yerine $x^2$.

            2. MİMARİ / AKIŞ / İLİŞKİ → Mermaid diyagramı kullan:
               ```mermaid
               flowchart LR
                 A["İstek"] --> B["Auth"] --> C["İş Mantığı"] --> D["Veritabanı"]
               ```
               State machine, sınıf diyagramı, sıra diyagramı için de aynı. Node etiketlerinde parantez, nokta veya iki nokta kullanırsan mutlaka çift tırnakla yaz; emin değilsen tablo kullan.

            3. SOYUT KAVRAM → Eğer action plan visual_generation/image_prompt isterse görsel prompt veya güvenli fallback diyagramı ver:
               Format: ![kısa açıklama](https://image.pollinations.ai/prompt/<URL_ENCODED_PROMPT>?width=512&height=512&nologo=true)
               Örnek: "Mitokondri hücrenin enerji santralidir. ![mitokondri kesit](https://image.pollinations.ai/prompt/cross-section%20of%20mitochondrion%20educational%20diagram?width=512&height=512&nologo=true)"
               Karmaşık konularda görseli zorla uydurma; planlanmış artifact yoksa tablo, Mermaid veya kısa şema kullan.

            4. KAYNAK / DAYANAK → Wiki veya Korteks raporundaki bilgiyi alıntılarken inline link ver:
               "Algoritmanın temel mantığı 1936'da Turing tarafından ortaya kondu ([Wikipedia: Turing Machine](https://en.wikipedia.org/wiki/Turing_machine))."
               Frontend bu linkleri hover preview'lı citation olarak gösterir.

            [P4 GÖRSEL ÖĞRENME VALIDATOR - ACTION PLAN ÖNCELİKLİ]:
            Cevabı göndermeden önce zihinsel kontrol yap:
            - Konu matematik/formül içeriyorsa en az bir LaTeX formül veya adım tablosu olmalı.
            - Konu algoritma, mimari, süreç, sistem veya workflow ise en az bir Mermaid akış/sequence/state diyagramı olmalı.
            - Konu ezber, tarih, dil veya sınav hazırlığı ise kısa tablo, timeline, kart veya tekrar planı olmalı.
            - Öğrenci zayıf beceri veya "anlamadım" sinyali verdiyse açıklama + somut örnek + mikro kontrol sorusu birlikte gelmeli.
            - Uygun ve planlanmış görsel öğe yoksa yanıtı kısa bir tablo, metinsel şema veya mikro kontrol ile tamamla.

            [SES MODU — VOICE/PODCAST KIP]:
            Eğer system bağlamında "[VOICE_MODE: PODCAST]" işareti varsa, çıktın iki veya üç sesli bir podcast diyaloğu olmalı:
            - [HOCA]: derin, açıklayıcı, soru yönelten ses
            - [ASISTAN]: meraklı, öğrencinin yerine soran ses
            - [KONUK]: opsiyonel 3. kişi; uzman, öğrenci veya karşı görüş sesi
            Format:
              [HOCA]: Bugün for döngüsünü öğreneceğiz, hazır mısın?
              [ASISTAN]: Hocam, neden döngü kullanırız ki, tek tek de yazılır?
              [KONUK]: Ben de bunu gerçek hayatta nerede kullanacağımızı merak ediyorum.
              [HOCA]: Harika soru — eğer 1000 satır yazman gerekirse...
            Voice modu dışında bu etiketleri ASLA kullanma.

            [DRILL-DOWN / TELAFİ MODU]:
            Eğer lowQualityHint içinde "Öğrenci ... kez başarısız" geçiyorsa:
            - Ana konuyu tekrar anlatma. Sadece eksik kavramı 2-3 cümlede yeniden, daha basit bir analojiyle ver.
            - "Şimdi tekrar deneyelim mi?" diyerek kullanıcıyı tekrar quiz'e davet et.
            - Action plan görsel istiyorsa görsel/diagram; istemiyorsa çok somut bir gerçek-hayat örneği yeterlidir.
            """;

    public TutorAgent(
        IContextBuilder contextBuilder,
        IAIAgentFactory factory,
        IServiceScopeFactory scopeFactory,
        IGraderAgent grader,
        IWikiService wikiService,
        ILogger<TutorAgent> logger,
        IRedisMemoryService redisService,
        ILearningSourceService learningSourceService,
        ILearningSignalService learningSignals,
        IEducatorCoreService educatorCore,
        ITutorPolicyEngine tutorPolicy,
        ITutorTurnStateAssembler turnStateAssembler,
        ITutorActionPlanner actionPlanner,
        ITutorToolOrchestrator toolOrchestrator,
        ITeachingArtifactService teachingArtifacts,
        ITutorReflectionService tutorReflection,
        ITutorPedagogyEvaluationService tutorPedagogyEvaluation,
        ITutorPedagogyQualityGate tutorPedagogyQualityGate)
    {
        _contextBuilder = contextBuilder;
        _factory = factory;
        _scopeFactory = scopeFactory;
        _grader = grader;
        _wikiService = wikiService;
        _logger = logger;
        _redisService = redisService;
        _learningSourceService = learningSourceService;
        _learningSignals = learningSignals;
        _educatorCore = educatorCore;
        _tutorPolicy = tutorPolicy;
        _turnStateAssembler = turnStateAssembler;
        _actionPlanner = actionPlanner;
        _toolOrchestrator = toolOrchestrator;
        _teachingArtifacts = teachingArtifacts;
        _tutorReflection = tutorReflection;
        _tutorPedagogyEvaluation = tutorPedagogyEvaluation;
        _tutorPedagogyQualityGate = tutorPedagogyQualityGate;
    }

    public async Task<string> GetResponseAsync(Guid userId, string content, Session session, bool isQuizPending)
    {
        var contextTask = _contextBuilder.BuildConversationContextAsync(session);
        var hasTopic = session.TopicId.HasValue;
        var isStrictGrounding = (content ?? "").Contains("FocusSourceRef:");

        var parallelResults = await Task.WhenAll(
            (!isStrictGrounding && hasTopic) ? FetchActiveTopicContextAsync(userId, session.TopicId) : Task.FromResult(string.Empty),
            (!isStrictGrounding && hasTopic) ? FetchUserMemoryProfileAsync(userId, session.TopicId) : Task.FromResult(string.Empty),
            !isStrictGrounding ? FetchPerformanceProfileAsync(session.Id, session.TopicId) : Task.FromResult(string.Empty),
            (!isStrictGrounding && hasTopic) ? FetchWikiContextAsync(session.TopicId, userId) : Task.FromResult(string.Empty),
            !isStrictGrounding ? FetchPistonContextAsync(session.Id) : Task.FromResult(string.Empty),
            (!isStrictGrounding && hasTopic) ? FetchGoldExamplesAsync(session.UserId, session.TopicId) : Task.FromResult(string.Empty),
            !isStrictGrounding ? FetchLowQualityFeedbackAsync(session.Id) : Task.FromResult(string.Empty),
            hasTopic ? FetchNotebookContextAsync(userId, session.TopicId, content ?? "") : Task.FromResult(string.Empty),
            (!isStrictGrounding && hasTopic) ? FetchLearningSignalContextAsync(userId, session.TopicId) : Task.FromResult(string.Empty),
            (!isStrictGrounding && hasTopic) ? FetchYouTubeContextAsync(userId, session.TopicId) : Task.FromResult(string.Empty),
            (!isStrictGrounding && hasTopic) ? FetchReviewPressureContextAsync(userId, session.TopicId) : Task.FromResult(string.Empty)
        );

        var activeTopicContext = isStrictGrounding ? string.Empty : parallelResults[0];
        var memoryContext = isStrictGrounding ? string.Empty : parallelResults[1];
        var performanceHint = isStrictGrounding ? string.Empty : parallelResults[2];
        var wikiContext = isStrictGrounding ? string.Empty : parallelResults[3];
        var pistonContext = isStrictGrounding ? string.Empty : parallelResults[4];
        var goldExamples = isStrictGrounding ? string.Empty : parallelResults[5];
        var lowQualityHint = isStrictGrounding ? string.Empty : parallelResults[6];
        var notebookContext = parallelResults[7];
        var learningSignalContext = isStrictGrounding ? string.Empty : parallelResults[8];
        var youtubeContext = isStrictGrounding ? string.Empty : parallelResults[9];
        var reviewPressureContext = isStrictGrounding ? string.Empty : parallelResults[10];

        var contextMessages = await contextTask;
        var learnerEvidenceContext = isStrictGrounding ? string.Empty : BuildTutorEvidenceContext(parallelResults);
        var teacherContext = await _educatorCore.BuildTeacherContextAsync(
            userId,
            session.TopicId,
            session.Id,
            content,
            notebookContext,
            wikiContext,
            learnerEvidenceContext,
            youtubeContext);
        var tutorPolicy = await _tutorPolicy.BuildAsync(
            userId,
            session.TopicId,
            session.Id,
            content,
            notebookContext,
            wikiContext,
            learnerEvidenceContext);
        var orchestration = await BuildTutorOrchestrationAsync(
            userId,
            content,
            session,
            contextMessages,
            notebookContext,
            wikiContext,
            learnerEvidenceContext,
            pistonContext,
            tutorPolicy,
            CancellationToken.None);

        var systemPrompt = BuildTutorSystemPrompt(
            isQuizPending,
            content: content,
            educatorCoreContext: teacherContext.PromptBlock,
            tutorPolicyContext: tutorPolicy.PromptBlock + orchestration.PromptBlock);
        var userMessage = BuildContextSummary(contextMessages);

        var answer = await _factory.CompleteChatAsync(AgentRole.Tutor, systemPrompt, userMessage);
        await _educatorCore.RecordAnswerQualitySignalsAsync(userId, session.TopicId, session.Id, answer, teacherContext);
        var reflection = await _tutorReflection.ReflectAsync(
            orchestration.TurnState,
            orchestration.ActionPlan,
            answer,
            orchestration.Artifacts,
            CancellationToken.None);

        var pedagogyEval = await _tutorPedagogyEvaluation.EvaluateAsync(new TutorPedagogyEvaluationRequestDto
        {
            TurnState = orchestration.TurnState,
            ActionPlan = orchestration.ActionPlan,
            Reflection = reflection,
            ToolCalls = orchestration.ToolCalls,
            Artifacts = orchestration.Artifacts,
            AssistantAnswer = answer,
            AllowLlmJudge = false
        }, CancellationToken.None);

        if (_tutorPedagogyQualityGate.RequiresRepair(pedagogyEval))
        {
            var repairPrompt = _tutorPedagogyQualityGate.BuildRepairPrompt(
                pedagogyEval,
                orchestration.TurnState,
                orchestration.ActionPlan,
                answer);
            var repaired = await _factory.CompleteChatAsync(AgentRole.Tutor, systemPrompt + "\n\n" + repairPrompt, userMessage);
            if (!string.IsNullOrWhiteSpace(repaired))
            {
                answer = repaired;
                reflection = await _tutorReflection.ReflectAsync(
                    orchestration.TurnState,
                    orchestration.ActionPlan,
                    answer,
                    orchestration.Artifacts,
                    CancellationToken.None);
                await _tutorPedagogyEvaluation.EvaluateAsync(new TutorPedagogyEvaluationRequestDto
                {
                    TurnState = orchestration.TurnState,
                    ActionPlan = orchestration.ActionPlan,
                    Reflection = reflection,
                    ToolCalls = orchestration.ToolCalls,
                    Artifacts = orchestration.Artifacts,
                    AssistantAnswer = answer,
                    AllowLlmJudge = false
                }, CancellationToken.None);
            }
        }
        return answer;
    }

    public async Task<string> GetDeepPlanWelcomeAsync(
        Guid userId, string content, Session session, IReadOnlyList<string> planTitles)
    {
        var numberedPlan = string.Join("\n", planTitles.Select((t, i) => $"{i + 1}. {t}"));

        var systemPrompt = $"""
            Sen Orka AI'nın profesyonel eğitmenisin.
            Kullanıcı yeni bir öğrenme yolculuğu başlatıyor.
            Onun için aşağıdaki plan hazırlandı ve Wiki'ye işlendi:

            {numberedPlan}

            Kullanıcıyı sıcak ve motive edici bir şekilde karşıla. Planı kapsamlıca tanıt:
            - Her modülün ne öğreteceğini kısa bir cümleyle özetle.
            - Öğrenciye bu yolculuğun sonunda neler yapabileceğini heyecan verici bir şekilde anlat.
            - İlk adımla başlamaya davet et.
            Kısa ve yüzeysel karşılama yapma; öğrencinin planın değerini hissetmesini sağla.
            [KESİN KURAL]: Yukarıdaki planda olmayan hiçbir konu başlığını asla ekleme veya önermeme.
            """;

        var contextMessages = await _contextBuilder.BuildConversationContextAsync(session);
        var userMessage = BuildContextSummary(contextMessages);

        // V4 görselleştirme: karşılama mesajında müfredatın Mermaid haritası gösterilebilsin
        systemPrompt += V4VisualizationAndVoiceBlock;

        return await _factory.CompleteChatAsync(AgentRole.Tutor, systemPrompt, userMessage);
    }

    public Task<string> GetOptionsWelcomeAsync(Guid userId, string content, Session session)
    {
        var response = $"""
Harika bir konu! Bu konuyu nasıl öğrenmek istersin? Sana iki farklı yol sunabilirim:

* 🎯 **Seçenek 1: Derinlemesine Plan (Deep Plan):** Senin için saniyeler içinde 4 adımlı bir müfredat hazırlarım ve her adımı Wiki'ye işlerim. Yapılandırılmış ve düzenli öğreniriz.
* 💬 **Seçenek 2: Hızlı Sohbet:** Herhangi bir plan yapmadan, doğrudan merak ettiğin yerlerden konuşarak organik şekilde başlarız.

Lütfen "1" veya "2" yazarak tercihini belirt, hemen başlayalım!
""";
        return Task.FromResult(response);
    }

    public async Task<string> GetFirstLessonAsync(
        string parentTopicTitle,
        string lessonTitle,
        IReadOnlyList<string>? curriculumTitles = null)
    {
        // Hallucination Guard: müfredat listesi geçilmişse AI sadece bu başlıklara bağlı kalır
        var curriculumNote = curriculumTitles?.Count > 0
            ? $"\n\n[MÜFREDAT KISITI — KESİN KURAL]: Bu derste yalnızca aşağıdaki müfredat başlıklarından bahsedebilirsin. " +
              $"Bu listede olmayan hiçbir konu adını önermemeli, bahsetmemelisin:\n" +
              string.Join("\n", curriculumTitles.Select((t, i) => $"  {i + 1}. {t}"))
            : "";

        var systemPrompt = $"""
            Sen Orka AI'nın profesyonel, bilge ve kişisel eğitmenisin.

            Kullanıcı "{parentTopicTitle}" konusunu yapılandırılmış Plan Modu ile öğrenmeye başladı.
            Planını başarıyla oluşturduk ve şimdi "{lessonTitle}" alt konusunu anlatıyorsun.

            [GÖREV KAPSAMI]: Bu dersi kullanıcının öğrenme potansiyelini zirveye taşıyacak şekilde detaylı ve derinlemesine anlat.
            - Adım 1: "Neden bu konu önemli?" (Kapsamlı ve ilgi çekici bir giriş).
            - Adım 2: Teknik terimleri basite indirgeyerek ve benzetmeler (analoji) kullanarak açıkla, ancak teknik derinlikten ödün verme.
            - Adım 3: Gerçek dünyadan somut bir senaryo veya kod örneği ver.
            - Dil: İçten, Türkçe ve akademik standartlardan şaşmayan bir dil kullan. Konuyu yüzeysel geçme, öğrencinin tam anlamıyla doyuma ulaşmasını sağla.
            {curriculumNote}
            """;

        // V4 görselleştirme kuralları Plan Modu dersleri için de geçerli
        systemPrompt += V4VisualizationAndVoiceBlock;

        return await _factory.CompleteChatAsync(AgentRole.Tutor, systemPrompt, $"Konu: {lessonTitle}");
    }

    public async Task<string> GenerateTopicQuizAsync(string topicTitle, string? researchContext = null)
    {
        var contextInfo = "";
        if (!string.IsNullOrWhiteSpace(researchContext))
        {
            _logger.LogInformation("[TutorAgent] Sınav soruları üretilmeden önce araştırma konteksti Grader denetiminden geçiyor...");
            var isRelevant = await _grader.IsContextRelevantAsync(topicTitle, researchContext);
            if (isRelevant)
            {
                contextInfo = $"\n\n[ARAŞTIRMA VERİLERİ (GÜNCEL BİLGİ KAYNAĞI)]:\n{researchContext}\n\nLütfen yukarıdaki araştırma verilerini kullanarak konuyu daha güncel ve teknik bir seviyede sorgula.";
            }
        }

        var systemPrompt = $$"""
            Sen akademik düzeyde bir 'Eğitim Değerlendiricisi (Educational Assessor)' botusun.
            Görevin: Kullanıcının '{{topicTitle}}' konusundaki başarısını ölçmek için 5 soruluk bir mini-test hazırlamak.

            {{contextInfo}}

            SORU KALİTESİ KURALI:
            - Basit tanımlama sorularından KAÇIN. Uygulama, analiz ve problem çözme odaklı ol.
            - Her soru bağımsız olmalı.
            - Seçenekler mantıklı çeldiriciler içermeli.

            GÖRSEL SORU KURALI (ÖNEMLİ):
            - Coğrafya, biyoloji, fizik, kimya, astronomi, geometri gibi görsel konularda soru metnine Pollinations görseli EKLE.
            - Format: ![kısa açıklama](https://image.pollinations.ai/prompt/<URL_ENCODED_PROMPT>?width=512&height=512&nologo=true)
            - Örnek: "Aşağıdaki haritada işaretlenen bölge hangi iklim kuşağındadır?\n![dünya iklim kuşakları haritası](https://image.pollinations.ai/prompt/world%20climate%20zones%20map%20educational%20labeled?width=512&height=512&nologo=true)"
            - Görsel gerektirmeyen salt bilgi sorularında görsel ekleme.
            - explanation alanında da konuyu açıklarken uygun görsel veya diyagram kullanabilirsin.

            ÇIKTI KURALI:
            - SADECE aşağıdaki JSON dizisini döndür. Markdown tırnağı EKLEME.
            - "type" özelliği çok önemlidir. Eğer soru SADECE sözel bilgi veya kavram ölçüyorsa "multiple_choice" yap ve 4 şık ekle. Eğer soru YAZILIM/PROGRAMLAMA veya ALGORİTMA kodu yazmayı gerektiriyorsa "type": "coding" yap ve şıkları BOŞ bırak ("options": []).
            - KODLAMA SORUSU KISITI: Dizideki SON soru bir coding sorusu OLABİLİR; ancak dizinin ortasına asla coding sorusu koyma. Coding sorusu en fazla 1 tanedir ve sadece son indexte yer alır. Konu kod yazmayı hiç gerektirmiyorsa coding sorusu ekleme.

            [
              {
                "type": "multiple_choice", // veya "coding"
                "quizRunId": "uuid",
                "questionId": "uuid",
                "question": "Soru metni",
                "options": [
                  { "text": "...", "isCorrect": false },
                  { "text": "...", "isCorrect": true },
                  { "text": "...", "isCorrect": false },
                  { "text": "...", "isCorrect": false }
                ],
                "explanation": "Detaylı açıklama",
                "topic": "{{topicTitle}}",
                "skillTag": "olculen-alt-beceri",
                "topicPath": "{{topicTitle}} > alt beceri",
                "difficulty": "kolay|orta|zor",
                "cognitiveType": "hatirlama|uygulama|analiz|problem_cozme",
                "sourceHint": "wiki|korteks|ders",
                "questionHash": "soru-ve-beceri-ozeti"
              },
              ... (TOPLAM 5 SORU)
            ]

            DİL: Türkçe.
            """;

        return await _factory.CompleteChatAsync(AgentRole.Quiz, systemPrompt, $"Konu: \"{topicTitle}\"");
    }

    public async Task<bool> EvaluateQuizAnswerAsync(string question, string answer)
    {
#if DEBUG
        // ── PLAYWRIGHT BACKDOOR (yalnızca DEBUG build'de aktif) ──────────────
        // E2E testlerde AI değerlendirmesini bypass etmek için kullanılır.
        // Release build'de bu blok derlenmez → production'da sıfır risk.
        if (answer.Contains("[PLAYWRIGHT_PASS_QUIZ]", StringComparison.Ordinal))
            return true;
        // ────────────────────────────────────────────────────────────────────
#endif

        // Eski "_chain.GenerateWithFallbackAsync" kullanimi kaldirildi.
        // Ayni isi yapan ve cok daha optimize calisan GraderAgent devrede.
        var isCorrect = await _grader.EvaluateAnswerAsync(question, answer, CancellationToken.None);
        return isCorrect;
    }

    public async IAsyncEnumerable<string> GetResponseStreamAsync(Guid userId, string content, Session session, bool isQuizPending, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return BuildStreamEvent("thinking", new { message = "Tutor state hazırlanıyor" });

        var contextTask = _contextBuilder.BuildConversationContextAsync(session);
        var hasTopic = session.TopicId.HasValue;
        var isStrictGrounding = (content ?? "").Contains("FocusSourceRef:");

        var parallelResults = await Task.WhenAll(
            (!isStrictGrounding && hasTopic) ? FetchActiveTopicContextAsync(userId, session.TopicId) : Task.FromResult(string.Empty),
            (!isStrictGrounding && hasTopic) ? FetchUserMemoryProfileAsync(userId, session.TopicId) : Task.FromResult(string.Empty),
            !isStrictGrounding ? FetchPerformanceProfileAsync(session.Id, session.TopicId) : Task.FromResult(string.Empty),
            (!isStrictGrounding && hasTopic) ? FetchWikiContextAsync(session.TopicId, userId) : Task.FromResult(string.Empty),
            !isStrictGrounding ? FetchPistonContextAsync(session.Id) : Task.FromResult(string.Empty),
            (!isStrictGrounding && hasTopic) ? FetchGoldExamplesAsync(session.UserId, session.TopicId) : Task.FromResult(string.Empty),
            !isStrictGrounding ? FetchLowQualityFeedbackAsync(session.Id) : Task.FromResult(string.Empty),
            hasTopic ? FetchNotebookContextAsync(userId, session.TopicId, content ?? "", ct) : Task.FromResult(string.Empty),
            (!isStrictGrounding && hasTopic) ? FetchLearningSignalContextAsync(userId, session.TopicId, ct) : Task.FromResult(string.Empty),
            (!isStrictGrounding && hasTopic) ? FetchYouTubeContextAsync(userId, session.TopicId) : Task.FromResult(string.Empty),
            (!isStrictGrounding && hasTopic) ? FetchReviewPressureContextAsync(userId, session.TopicId, ct) : Task.FromResult(string.Empty)
        );

        var activeTopicContext = isStrictGrounding ? string.Empty : parallelResults[0];
        var memoryContext = isStrictGrounding ? string.Empty : parallelResults[1];
        var performanceHint = isStrictGrounding ? string.Empty : parallelResults[2];
        var wikiContext = isStrictGrounding ? string.Empty : parallelResults[3];
        var pistonContext = isStrictGrounding ? string.Empty : parallelResults[4];
        var goldExamples = isStrictGrounding ? string.Empty : parallelResults[5];
        var lowQualityHint = isStrictGrounding ? string.Empty : parallelResults[6];
        var notebookContext = parallelResults[7];
        var learningSignalContext = isStrictGrounding ? string.Empty : parallelResults[8];
        var youtubeContext = isStrictGrounding ? string.Empty : parallelResults[9];
        var reviewPressureContext = isStrictGrounding ? string.Empty : parallelResults[10];

        var contextMessages = await contextTask;
        var learnerEvidenceContext = isStrictGrounding ? string.Empty : BuildTutorEvidenceContext(parallelResults);
        var teacherContext = await _educatorCore.BuildTeacherContextAsync(
            userId,
            session.TopicId,
            session.Id,
            content,
            notebookContext,
            wikiContext,
            learnerEvidenceContext,
            youtubeContext,
            ct);
        var tutorPolicy = await _tutorPolicy.BuildAsync(
            userId,
            session.TopicId,
            session.Id,
            content,
            notebookContext,
            wikiContext,
            learnerEvidenceContext,
            ct);
        var orchestrationState = await BuildTutorStateAndActionPlanAsync(
            userId,
            content,
            session,
            contextMessages,
            notebookContext,
            wikiContext,
            learnerEvidenceContext,
            pistonContext,
            tutorPolicy,
            ct);
        var startedToolIds = orchestrationState.ActionPlan.ToolPlans.Select(p => p.ToolId).ToArray();
        foreach (var toolId in startedToolIds)
        {
            yield return BuildStreamEvent("tool_started", new
            {
                toolId,
                tutorActionTraceId = orchestrationState.ActionPlan.Id
            });
        }

        var toolCalls = await _toolOrchestrator.RunAsync(orchestrationState.ActionPlan, orchestrationState.TurnState, ct);
        var artifacts = await _teachingArtifacts.BuildArtifactsAsync(orchestrationState.ActionPlan, orchestrationState.TurnState, ct);
        var orchestration = (
            orchestrationState.TurnState,
            orchestrationState.ActionPlan,
            ToolCalls: toolCalls,
            Artifacts: artifacts,
            PromptBlock: BuildOrchestrationPromptBlock(orchestrationState.TurnState, orchestrationState.ActionPlan, toolCalls, artifacts));

        foreach (var tool in orchestration.ToolCalls)
        {
            yield return BuildStreamEvent("tool_finished", new
            {
                toolCallId = tool.Id,
                toolId = tool.ToolId,
                status = tool.Status,
                riskLevel = tool.RiskLevel,
                success = tool.Success,
                provider = tool.Provider,
                safeMessage = tool.SafeMessage
            });
        }

        foreach (var artifact in orchestration.Artifacts)
        {
            yield return BuildStreamEvent("artifact_ready", new
            {
                artifactId = artifact.Id,
                artifactType = artifact.ArtifactType,
                title = artifact.Title
            });
        }

        var systemPrompt = BuildTutorSystemPrompt(
            isQuizPending,
            content: content,
            educatorCoreContext: teacherContext.PromptBlock,
            tutorPolicyContext: tutorPolicy.PromptBlock + orchestration.PromptBlock);
        var userMessage = BuildContextSummary(contextMessages);

        // AIAgentFactory: Primary → Gemini → Mistral (stream failover zinciri)
        var answerBuffer = new StringBuilder();
        await foreach (var chunk in _factory.StreamChatAsync(AgentRole.Tutor, systemPrompt, userMessage, ct))
        {
            answerBuffer.Append(chunk);
            yield return chunk;
        }

        await _educatorCore.RecordAnswerQualitySignalsAsync(
            userId,
            session.TopicId,
            session.Id,
            answerBuffer.ToString(),
            teacherContext,
            CancellationToken.None);
        var reflection = await _tutorReflection.ReflectAsync(
            orchestration.TurnState,
            orchestration.ActionPlan,
            answerBuffer.ToString(),
            orchestration.Artifacts,
            CancellationToken.None);
        var pedagogyEval = await _tutorPedagogyEvaluation.EvaluateAsync(new TutorPedagogyEvaluationRequestDto
        {
            TurnState = orchestration.TurnState,
            ActionPlan = orchestration.ActionPlan,
            Reflection = reflection,
            ToolCalls = orchestration.ToolCalls,
            Artifacts = orchestration.Artifacts,
            AssistantAnswer = answerBuffer.ToString(),
            AllowLlmJudge = false
        }, CancellationToken.None);
        yield return BuildStreamEvent("final", new
        {
            tutorTurnStateId = orchestration.TurnState.Id,
            tutorActionTraceId = orchestration.ActionPlan.Id,
            artifactIds = orchestration.Artifacts.Select(a => a.Id).ToArray(),
            tutorPedagogyEvaluationRunId = pedagogyEval.Id,
            tutorPedagogyStatus = pedagogyEval.Status,
            tutorPedagogyScore = pedagogyEval.OverallScore
        });
    }

    private async Task<(
        TutorTurnStateDto TurnState,
        TutorActionPlanDto ActionPlan,
        IReadOnlyList<TutorToolCallDto> ToolCalls,
        IReadOnlyList<TeachingArtifactDto> Artifacts,
        string PromptBlock)> BuildTutorOrchestrationAsync(
        Guid userId,
        string content,
        Session session,
        IEnumerable<Message> contextMessages,
        string notebookContext,
        string wikiContext,
        string learningSignalContext,
        string ideContext,
        TutorPolicyContextDto tutorPolicy,
        CancellationToken ct)
    {
        var (turnState, actionPlan) = await BuildTutorStateAndActionPlanAsync(
            userId,
            content,
            session,
            contextMessages,
            notebookContext,
            wikiContext,
            learningSignalContext,
            ideContext,
            tutorPolicy,
            ct);

        var toolCalls = await _toolOrchestrator.RunAsync(actionPlan, turnState, ct);
        var artifacts = await _teachingArtifacts.BuildArtifactsAsync(actionPlan, turnState, ct);
        return (turnState, actionPlan, toolCalls, artifacts, BuildOrchestrationPromptBlock(turnState, actionPlan, toolCalls, artifacts));
    }

    private async Task<(TutorTurnStateDto TurnState, TutorActionPlanDto ActionPlan)> BuildTutorStateAndActionPlanAsync(
        Guid userId,
        string content,
        Session session,
        IEnumerable<Message> contextMessages,
        string notebookContext,
        string wikiContext,
        string learningSignalContext,
        string ideContext,
        TutorPolicyContextDto tutorPolicy,
        CancellationToken ct)
    {
        var turnState = await _turnStateAssembler.BuildAsync(
            userId,
            session.TopicId,
            session.Id,
            content,
            BuildContextSummary(contextMessages),
            notebookContext,
            wikiContext,
            learningSignalContext,
            ideContext,
            tutorPolicy,
            ct);

        var actionPlan = await _actionPlanner.PlanAsync(turnState, ct);
        return (turnState, actionPlan);
    }

    private static string BuildOrchestrationPromptBlock(
        TutorTurnStateDto turnState,
        TutorActionPlanDto actionPlan,
        IReadOnlyList<TutorToolCallDto> toolCalls,
        IReadOnlyList<TeachingArtifactDto> artifacts)
    {
        var toolPrompt = toolCalls.Count == 0
            ? string.Empty
            : $"""

                [TUTOR TOOL ORCHESTRATION v3]
                {string.Join("\n", toolCalls.Select(t => $"- {t.ToolId}: {t.Status}; success={t.Success}; provider={t.Provider}; evidence={t.Evidence ?? "none"}; warning={t.SafeMessage ?? t.FallbackReason ?? "none"}"))}
                [TOOL KURALI] External provider required ise success=false veya status ready degilse kesin bilgi iddiasi kurma; safeMessage'i kisa ve dürüst sekilde kullan.
                """;

        var artifactPrompt = artifacts.Count == 0
            ? string.Empty
            : string.Join("\n", artifacts.Select(a => a.PromptBlock));

        return turnState.PromptBlock + actionPlan.PromptBlock + toolPrompt + artifactPrompt;
    }

    private static string BuildStreamEvent(string type, object payload) =>
        JsonSerializer.Serialize(new
        {
            type,
            data = payload
        });

    /// <summary>
    /// Faz 16: Bir önceki yanıt için EvaluatorAgent düşük puan (≤ 6) verdiyse
    /// Redis'te bekleyen flag'i atomik olarak alır ve prompt'a uyarı bloğu döker.
    /// </summary>
    private async Task<string> FetchLowQualityFeedbackAsync(Guid sessionId)
    {
        try
        {
            var lq = await _redisService.GetAndClearLowQualityFeedbackAsync(sessionId);
            if (lq == null) return string.Empty;

            _logger.LogInformation(
                "[TutorAgent] Dusuk kalite uyarisi tuketildi. SessionRef={SessionRef} Score={Score}",
                LogPrivacyGuard.SafeId(sessionId, "session"), lq.Value.score);

            return $"""

                [⚠️ ANLIK MÜDAHALE — ÖNCEKİ YANIT KALİTE UYARISI]:
                Son cevabın EvaluatorAgent tarafından {lq.Value.score}/10 puanlandı.
                Geri bildirim: {lq.Value.feedback}

                [EYLEM]: Bir sonraki yanıtını şu şekilde yapısal olarak iyileştir:
                - Anlatım derinliğini koru ama daha net ve yapılandırılmış paragraflar kullan.
                - Karmaşık cümleleri parçala; madde işaretleri, başlıklar ve Mermaid diyagramlarıyla destekle.
                - Öğrencinin kavrayıp kavramadığını anlamak için yanıtın sonunda kısa bir kontrol sorusu sor.
                - Detaylı anlatımdan ödün verme; sadece sunumu iyileştir.
                """;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[TutorAgent] Dusuk kalite flag'i okunamadi. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
            return string.Empty;
        }
    }

    private async Task<string> FetchNotebookContextAsync(Guid userId, Guid? topicId, string question, CancellationToken ct = default)
    {
        try
        {
            return await _learningSourceService.BuildTopicGroundingContextAsync(userId, topicId, question, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[TutorAgent] NotebookLM belge baglami okunamadi. TopicRef={TopicRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(topicId, "topic"), LogPrivacyGuard.SafeExceptionType(ex));
            return string.Empty;
        }
    }

    private async Task<string> FetchLearningSignalContextAsync(Guid userId, Guid? topicId, CancellationToken ct = default)
    {
        if (!topicId.HasValue) return string.Empty;

        try
        {
            var summary = await _learningSignals.GetTopicSummaryAsync(userId, topicId.Value, ct);
            if (summary.TotalAttempts == 0 && summary.WeakSkills.Count == 0 && summary.RecentSignals.Count == 0)
                return string.Empty;

            var weakSkills = summary.WeakSkills.Count == 0
                ? "Kayıtlı zayıf beceri yok."
                : string.Join("\n", summary.WeakSkills.Take(5).Select(skill =>
                    $"- {skill.SkillTag} ({skill.TopicPath}): {skill.WrongCount}/{skill.TotalCount} zorlanma, dogruluk %{Math.Round(skill.Accuracy * 100)}"));

            var recentSignals = summary.RecentSignals.Count == 0
                ? "Yakın zamanda öğrenme sinyali yok."
                : string.Join("\n", summary.RecentSignals.Take(5).Select(signal => $"- {signal}"));

            return $"""

                [OGRENCI SINYAL OZETI - KISISELLESTIRME]
                Dogruluk: {summary.CorrectAttempts}/{summary.TotalAttempts} (%{Math.Round(summary.Accuracy * 100)})
                Zayif beceriler:
                {weakSkills}

                Son sinyaller:
                {recentSignals}

                [EYLEM]: Cevabı bu zayıf becerilere göre kur. Öğrenci takıldıysa ilgili alt beceriye dön,
                kisa bir ornek ver, gerekirse mini tablo/diyagram kullan ve sonraki en iyi adimi oner.
                """;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[TutorAgent] Learning signal context okunamadi. TopicRef={TopicRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(topicId, "topic"), LogPrivacyGuard.SafeExceptionType(ex));
            return string.Empty;
        }
    }

    private async Task<string> FetchYouTubeContextAsync(Guid userId, Guid? topicId)
    {
        if (!topicId.HasValue) return string.Empty;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

            var cursor = await db.Topics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == topicId.Value && t.UserId == userId);
            Guid rootTopicId = topicId.Value;

            while (cursor != null && cursor.ParentTopicId.HasValue)
            {
                rootTopicId = cursor.ParentTopicId.Value;
                cursor = await db.Topics.AsNoTracking().FirstOrDefaultAsync(t => t.Id == cursor.ParentTopicId.Value && t.UserId == userId);
            }

            return await _redisService.GetYouTubeContextAsync(rootTopicId) ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[TutorAgent] YouTube context okunamadi. TopicRef={TopicRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(topicId, "topic"), LogPrivacyGuard.SafeExceptionType(ex));
            return string.Empty;
        }
    }

    private async Task<string> FetchActiveTopicContextAsync(Guid userId, Guid? topicId)
    {
        if (!topicId.HasValue) return string.Empty;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

            var topic = await db.Topics
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == topicId.Value && t.UserId == userId);
            if (topic == null) return string.Empty;

            var path = new List<Topic> { topic };
            var cursor = topic;
            while (cursor.ParentTopicId.HasValue)
            {
                var parent = await db.Topics
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == cursor.ParentTopicId.Value && t.UserId == userId);
                if (parent == null) break;
                path.Insert(0, parent);
                cursor = parent;
            }

            var root = path[0];
            var activeLessonTitle = topic.Title;
            var planIntentHint = TutorIntelligenceContextFormatter.BuildPlanIntentHint(topic.PlanIntent, topic.Category);

            var modules = await db.Topics
                .AsNoTracking()
                .Where(t => t.ParentTopicId == root.Id && t.UserId == userId)
                .OrderBy(t => t.Order)
                .ToListAsync();

            if (modules.Count > 0)
            {
                var moduleIds = modules.Select(m => m.Id).ToList();
                var leafLessons = await db.Topics
                    .AsNoTracking()
                    .Where(t => t.ParentTopicId.HasValue && moduleIds.Contains(t.ParentTopicId.Value) && t.UserId == userId)
                    .OrderBy(t => t.Order)
                    .ToListAsync();

                var orderedLessons = leafLessons.Count > 0
                    ? modules.SelectMany(m => leafLessons.Where(l => l.ParentTopicId == m.Id).OrderBy(l => l.Order)).ToList()
                    : modules;

                if (orderedLessons.Count > 0 && topic.Id == root.Id)
                {
                    var index = Math.Clamp(root.CompletedSections, 0, orderedLessons.Count - 1);
                    activeLessonTitle = orderedLessons[index].Title;
                }
                else if (leafLessons.Count > 0 && modules.Any(m => m.Id == topic.Id))
                {
                    activeLessonTitle = leafLessons
                        .Where(l => l.ParentTopicId == topic.Id)
                        .OrderBy(l => l.Order)
                        .Select(l => l.Title)
                        .FirstOrDefault() ?? topic.Title;
                }
            }

            return $"""

                [AKTIF_DERS_BAGLAMI]:
                - Secili konu yolu: {string.Join(" > ", path.Select(p => p.Title))}
                - Aktif anlatilacak ders: {activeLessonTitle}
                - Dil seviyesi: {topic.LanguageLevel ?? root.LanguageLevel ?? "not recorded"}
                - Son calisma ozeti: {topic.LastStudySnapshot ?? root.LastStudySnapshot ?? "not recorded"}
                - Kullanici "devam", "basla", "anlat" gibi kisa bir mesaj yazarsa konu sorma; bu aktif ders uzerinden dogrudan anlatima basla.
                {planIntentHint}
                """;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[TutorAgent] Aktif ders baglami okunamadi. TopicRef={TopicRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(topicId.Value, "topic"), LogPrivacyGuard.SafeExceptionType(ex));
            return string.Empty;
        }
    }

    private async Task<string> FetchUserMemoryProfileAsync(Guid userId, Guid? topicId)
    {
        if (!topicId.HasValue) return "";

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var scopeIds = await GetTopicScopeIdsAsync(db, userId, topicId.Value);

        var recentFailedAttempts = await db.QuizAttempts
            .AsNoTracking()
            .Where(q => q.UserId == userId && q.TopicId.HasValue && scopeIds.Contains(q.TopicId.Value) && !q.IsCorrect)
            .OrderByDescending(q => q.CreatedAt)
            .Take(5)
            .ToListAsync();

        // Faz 13: Zaten öğrenilmiş alt konular (tekrara gerek yok)
        var masteredSkills = await db.SkillMasteries
            .AsNoTracking()
            .Where(sm => sm.UserId == userId && scopeIds.Contains(sm.TopicId))
            .OrderByDescending(sm => sm.MasteredAt)
            .Take(10)
            .Select(sm => sm.SubTopicTitle)
            .ToListAsync();

        var sb = new System.Text.StringBuilder();

        if (masteredSkills.Count > 0)
        {
            var masteredList = string.Join(", ", masteredSkills);
            sb.AppendLine($"\n\n[ÖĞRENCİ ZATEN BİLİYOR — Bu alt konuları tekrar anlatma, üstüne inşa et]:\n{masteredList}");
        }

        if (recentFailedAttempts.Count > 0)
        {
            sb.AppendLine(TutorIntelligenceContextFormatter.BuildFailedAttemptSummary(recentFailedAttempts));
        }

        return sb.Length > 0 ? sb.ToString() : "";
    }

    private async Task<string> FetchReviewPressureContextAsync(Guid userId, Guid? topicId, CancellationToken ct = default)
    {
        if (!topicId.HasValue) return string.Empty;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var scopeIds = await GetTopicScopeIdsAsync(db, userId, topicId.Value, ct);

            var recommendations = await _learningSignals.GetRecommendationsAsync(userId, topicId.Value, ct);
            var remediationPlans = await db.RemediationPlans
                .AsNoTracking()
                .Where(r => r.UserId == userId && scopeIds.Contains(r.TopicId))
                .OrderByDescending(r => r.CreatedAt)
                .Take(5)
                .ToListAsync(ct);

            var context = TutorIntelligenceContextFormatter.BuildReviewPressureSummary(recommendations, remediationPlans);
            if (!string.IsNullOrWhiteSpace(context))
            {
                _logger.LogInformation(
                    "[TutorAgent] Review/remediation pressure context loaded. TopicRef={TopicRef} RecommendationCount={RecommendationCount} RemediationCount={RemediationCount}",
                    LogPrivacyGuard.SafeId(topicId.Value, "topic"),
                    recommendations.Count,
                    remediationPlans.Count);
            }

            return context;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[TutorAgent] Review/remediation pressure context okunamadi. TopicRef={TopicRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(topicId.Value, "topic"), LogPrivacyGuard.SafeExceptionType(ex));
            return string.Empty;
        }
    }

    /// <summary>
    /// Faz 12: Konu için Redis'te kayıtlı altın örnekleri çeker ve few-shot formatına çevirir.
    /// EvaluatorAgent'ın 9-10 puan verdiği başarılı diyaloglar bu havuza düşer.
    /// </summary>
    private async Task<string> FetchGoldExamplesAsync(Guid userId, Guid? topicId)
    {
        if (!topicId.HasValue) return string.Empty;

        try
        {
            var examples = (await _redisService.GetGoldExamplesAsync(userId, topicId.Value, 2)).ToList();
            if (examples.Count == 0) return string.Empty;

            _logger.LogInformation("[TutorAgent] {Count} altin ornek yuklendi. TopicRef={TopicRef}",
                examples.Count, LogPrivacyGuard.SafeId(topicId.Value, "topic"));

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("\n\n[ALTIN ÖRNEKLER — Bu konuda daha önce başarılı olan yanıtlar (sen yazdın, puan >= 9)]:");
            for (int i = 0; i < examples.Count; i++)
            {
                sb.AppendLine($"Örnek {i + 1}:");
                sb.AppendLine($"Öğrenci: \"{examples[i].UserMessage}\"");
                sb.AppendLine($"Sen: \"{examples[i].AgentResponse}\"");
                sb.AppendLine("---");
            }
            sb.AppendLine("[Bu örnekleri referans al, aynı tarz ve kaliteyi koru]");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[TutorAgent] Altin ornekler yuklenemedi. TopicRef={TopicRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(topicId.Value, "topic"), LogPrivacyGuard.SafeExceptionType(ex));
            return string.Empty;
        }
    }

    /// <summary>
    /// Faz 11: Aktif konunun wiki içeriğini WikiService'ten çeker.
    /// KorteksAgent araştırması ve SummarizerAgent özeti bu içeriği besler.
    /// Tutor bu sayede "Bu konuda ne bilindiğini" öğrenerek öğretim kalitesini artırır.
    /// </summary>
    private async Task<string> FetchWikiContextAsync(Guid? topicId, Guid userId)
    {
        if (!topicId.HasValue) return string.Empty;

        try
        {
            var wikiContent = await _wikiService.GetWikiFullContentAsync(topicId.Value, userId);

            if (string.IsNullOrWhiteSpace(wikiContent)) return string.Empty;

            // Token taşmasını önlemek için kırp
            if (wikiContent.Length > WikiContextMaxChars)
                wikiContent = wikiContent[..WikiContextMaxChars] + "\n[...devamı wiki panelinde]";

            _logger.LogInformation("[TutorAgent] Wiki context yuklendi: {CharCount} karakter. TopicRef={TopicRef}",
                wikiContent.Length, LogPrivacyGuard.SafeId(topicId.Value, "topic"));

            return $"\n\n[KONU WİKİSİ — Bu konuda şimdiye kadar bilinenler]:\n{wikiContent}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[TutorAgent] Wiki context yuklenemedi, standart moda devam. TopicRef={TopicRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(topicId.Value, "topic"), LogPrivacyGuard.SafeExceptionType(ex));
            return string.Empty;
        }
    }

    /// <summary>
    /// Faz 11: Öğrencinin bu session'da en son çalıştırdığı kod ve çıktısını Redis'ten okur.
    /// Tutor bu bağlamı kullanarak "Az önce çalıştırdığın kodda şunu fark ettim..." gibi
    /// bağlamsal geri bildirim verebilir.
    /// </summary>
    private async Task<string> FetchPistonContextAsync(Guid sessionId)
    {
        try
        {
            var json = await _redisService.GetLastPistonResultAsync(sessionId);
            if (string.IsNullOrEmpty(json)) return string.Empty;

            _logger.LogInformation("[TutorAgent] Piston context yuklendi. SessionRef={SessionRef}",
                LogPrivacyGuard.SafeId(sessionId, "session"));

            return $"\n\n[SON KOD ÇIKTISI — Öğrenci az önce şunu çalıştırdı]:\n{json}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[TutorAgent] Piston context yuklenemedi. SessionRef={SessionRef} ErrorType={ErrorType}",
                LogPrivacyGuard.SafeId(sessionId, "session"), LogPrivacyGuard.SafeExceptionType(ex));
            return string.Empty;
        }
    }

    /// <summary>
    /// Faz 10+14: Redis üzerinden session + topic level geri bildirim okur.
    /// Session değişse bile topic-level puanlar korunur.
    /// </summary>
    private async Task<string> FetchPerformanceProfileAsync(Guid sessionId, Guid? topicId = null)
    {
        try
        {
            var feedbacks = await _redisService.GetRecentFeedbackAsync(sessionId, 5);
            var feedbackList = feedbacks.ToList();

            // Session-level boşsa, topic-level'dan oku (Faz 14)
            string topicScoreInfo = "";
            if (topicId.HasValue)
            {
                var (avgScore, totalEvals) = await _redisService.GetTopicScoreAsync(topicId.Value);
                if (totalEvals > 0)
                {
                    topicScoreInfo = $"\n[KONU BAZLI PERFORMANS]: Bu konuda ortalama kalite puanı: {avgScore}/10 ({totalEvals} değerlendirme)";
                    if (avgScore < 5) topicScoreInfo += " ⚠️ DİKKAT: Kalite ortalaması düşük, daha sade ve kısa anlatıma geç.";
                    else if (avgScore >= 8) topicScoreInfo += " ✅ Kalite yüksek, bu tarzı koru.";
                }
            }

            // Faz 15: Yaşayan Organizasyon (Öğrenci Profili)
            string studentProfileInfo = "";
            if (topicId.HasValue)
            {
                var studentProfile = await _redisService.GetStudentProfileAsync(topicId.Value);
                if (studentProfile.HasValue)
                {
                    studentProfileInfo = $"\n[ÖĞRENCİ ANLAYIŞ PROFİLİ (Yaşayan Organizasyon)]:\n- Kavrama Seviyesi: {studentProfile.Value.score}/10\n";
                    if (!string.IsNullOrEmpty(studentProfile.Value.weaknesses))
                        studentProfileInfo += $"- Zayıf Noktaları / Hataları: {studentProfile.Value.weaknesses}\n";
                    studentProfileInfo += "-> DİKKAT: Konuyu bu seviyeye göre anlat. Zayıf noktaları varsa üzerine git ve destekleyici sorular sor.\n";
                }
            }

            if (!feedbackList.Any() && string.IsNullOrEmpty(topicScoreInfo) && string.IsNullOrEmpty(studentProfileInfo)) return "";

            var feedbackSummary = feedbackList.Any()
                ? string.Join("\n", feedbackList.Select(f => $"- {f}"))
                : "(Session-level geri bildirim henüz yok)";

            _logger.LogInformation("[TutorAgent][Faz10+14+15] Redis'ten {Count} adet performans notu ve öğrenci profili çekildi.",
                feedbackList.Count + (studentProfileInfo != "" ? 1 : 0));

            return $$"""

                [CRITICAL LLMOPS FEEDBACK - PERFORMANS DENETİM NOTLARI]:
                Aşağıdaki notlar senin son mesajlarının kalitesine dair 'EvaluatorAgent' tarafından bırakıldı:
                {{feedbackSummary}}
                {{topicScoreInfo}}

                [EYLEM TALİMATI]:
                Eğer yukarıdaki notlarda 'düşük puan', 'uzun anlatım' veya 'anlaşılmayan kısım' uyarısı varsa;
                bir sonraki yanıtını ACİLEN daha sade, daha kısa ve daha empatik bir tona çek.
                Öğrencinin kafasını karıştırmadan, bu uyarılara göre tarzını optimize et.
                {{studentProfileInfo}}
                """;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[TutorAgent] Redis performans notlari okunurken hata olustu, standart moda devam ediliyor. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
            return "";
        }
    }

    private static string BuildContextSummary(IEnumerable<Message> context)
    {
        var msgs = context.TakeLast(14).ToList();
        if (msgs.Count == 0) return "(Yeni konuşma)";
        return string.Join("\n", msgs.Select(m => $"{(m.Role?.ToLower() == "user" ? "Kullanıcı" : "Asistan")}: {m.Content}"));
    }

    private static string BuildTutorEvidenceContext(IReadOnlyList<string> contextBlocks)
    {
        string Pick(int index) => index >= 0 && index < contextBlocks.Count ? contextBlocks[index] : string.Empty;
        var sections = new[]
        {
            ("active_topic", Pick(0)),
            ("legacy_skill_memory", Pick(1)),
            ("legacy_redis_performance", Pick(2)),
            ("learning_signals", Pick(8)),
            ("gold_examples", Pick(5)),
            ("low_quality_feedback", Pick(6)),
            ("review_pressure", Pick(10))
        }
        .Where(s => !string.IsNullOrWhiteSpace(s.Item2))
        .Select(s => $"[{s.Item1}]\n{TrimForState(s.Item2, 1200)}")
        .ToList();

        return sections.Count == 0
            ? string.Empty
            : "[TUTOR STATE INPUTS - LEGACY SIGNALS AS LOW PRIORITY EVIDENCE]\n" + string.Join("\n\n", sections);
    }

    private static string TrimForState(string value, int maxChars)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxChars ? trimmed : trimmed[..maxChars] + "...";
    }

    private string BuildTutorSystemPrompt(
        bool isQuizPending,
        string activeTopicContext = "",
        string memoryContext   = "",
        string performanceHint = "",
        string wikiContext     = "",
        string pistonContext   = "",
        string goldExamples    = "",
        string lowQualityHint  = "",
        string notebookContext = "",
        string learningSignalContext = "",
        string educatorCoreContext = "",
        string tutorPolicyContext = "",
        string reviewPressureContext = "",
        string content = "")
    {
        var prompt = $$"""
            Sen Orka AI — Kullanıcının özel öğretmeni ve bilge bir mentorusun.
            {{activeTopicContext}}
            {{lowQualityHint}}
            {{memoryContext}}
            {{performanceHint}}
            {{wikiContext}}
            {{pistonContext}}
            {{goldExamples}}
            {{notebookContext}}
            {{learningSignalContext}}
            {{reviewPressureContext}}
            {{educatorCoreContext}}
            {{tutorPolicyContext}}

            [TEMEL KURAL — ÖĞRETİM TARZI]:
            Chat ekranında konuyu detaylı, derinlemesine ve doyurucu bir şekilde anlat. Konuları asla yüzeysel veya çok kısa (1-2 cümle) geçme.
            Özel ders hocası gibi davran: karmaşık yapıları parçalara bölerek adım adım, net ve teknik olarak eksiksiz aktar.
            Sistemin akışı şöyledir: Chat ekranında DETAYLI EĞİTİM verilir, Wiki ekranında ise konunun ÖZETİ tutulur. Sen detaylı eğitimden sorumlusun.
            Anlatımını zenginleştir, mantığını ve felsefesini öğrenciye kavrat.

            [YOUTUBE PEDAGOJI GUVENLIK KURALI]:
            YouTube öğretim referansı yalnızca system context içinde "[YOUTUBE TEACHING REFERENCE - PEDAGOGY ONLY]" bloğu varsa kullanılabilir.
            Bu blok yoksa belirli bir video, kanal veya hocayı izlediğini/duyduğunu ima etme; "bu hoca burada..." gibi ifadeler kullanma.
            YouTube varsa bile onu varsayılan olarak gerçek kanıt değil, anlatım akışı, örnek, analoji, sık hata ve pratik fikri için pedagojik referans kabul et.

            [KİMLİK VE TON]:
            1. Samimi, cesaretlendirici, sabırlı ve bilgi dolu bir mentor gibi konuş.
            2. Öğrencinin merakını kıvılcımla. Bir konsepti derinlemesine anlattıktan sonra "Peki sence neden böyle?" veya "Bunu bir deneyelim mi?" gibi sorularla sürükle.
            3. Emoji kullanımı: tutumlu ama etkili (📌 🔥 💡 gibi).

            [ANLATIM KURALLARI]:
            - Konuyu anlatırken detaya inmekten çekinme. Yüzeysel cevaplar verme.
            - Kod örneği vereceksen işlevsel ve anlaşılır uzunlukta bloklar kullanabilirsin.
            - "Bu konunun temelinde yatan mantık şudur..." gibi köprüler kurarak konunun kök nedenlerini açıkla.
            - DİKKAT: Diyagramlar (Mermaid) ve Görseller (Pollinations) anlatımını çok daha güçlü kılar. Karmaşık konuları metinle boğmak yerine görsel ve diyagram kullan!
            - Kullanıcı selamlama yaparsa: sıcak karşılık ver ve hemen güçlü bir eğitime başla.

            [SOHBET ÖRNEKLERİ]:
            - Kullanıcı: "nasılsın" → Sen: "Harikayım! Bugün ne öğrenmek istersin? 🚀"
            - Kullanıcı: "JavaScript'te promise nedir?" → Sen: "Promise, JavaScript'in 'söz verme' mekanizmasıdır. Asenkron işlemlerde callback cehennemini çözmek için geliştirilmiştir... (Burada detaylı bir anlatım ve örnek kod verilir). Peki sence neden arka planda bekleyen işleri ana akışı durdurmadan yapmalıyız? 🤔"
            - Kullanıcı: "anladım" → Sen: "Harika! O zaman bu mantığı pekiştirmek için şöyle bir kod yazsak nasıl olurdu? (Örnek verir)"

            [KODLAMA VE ALGORİTMA GÖREVLERİ (KRİTİK KURAL)]:
            ORKA IDE VARSAYILAN ORTAMDIR:
            - Kullanıcı "C#, Python, JavaScript, SQL, algoritma, kod yazalım" gibi bir hedef verdiğinde ilk anlatım ve ilk pratik Orka IDE/sandbox üzerinden kurgulanır.
            - Visual Studio, VS Code, Rider, PyCharm veya harici kurulumları ilk adım gibi anlatma; kullanıcı özellikle yerel kurulum sorarsa opsiyonel ek not olarak ver.
            - Başlangıç derslerinde "Orka IDE'de deneyelim, çıktıyı Tutor'a gönderelim, hata varsa bunu öğrenme sinyaline çevirelim" çizgisini koru.
            Eğer [SON KOD ÇIKTISI] bağlamı varsa, bunu gerçek Piston/Judge0 sandbox sonucu kabul et:
            - Compile/derleme hatası ise syntax, tip, import veya eksik sembol kavramını öğret.
            - Runtime hatası ise exception, null/index, veri yapısı veya akış nedenini açıkla.
            - Timeout ise sonsuz döngü veya algoritma karmaşıklığını anlat.
            - Başarılı stdout varsa sonucu yorumla ve bir sonraki küçük pratik adımı ver.
            - Kod çıktısını uydurma; sadece verilen stdout/stderr/compileError/runtimeError alanlarına dayan.
            - Kullanıcı C#, Python, JavaScript gibi kod öğrenmek istiyorsa ilk yol olarak Orka IDE/sandbox akışını öner. Visual Studio, VS Code veya harici kurulumları ilk varsayım yapma; onları sadece opsiyonel yerel geliştirme aracı olarak anlat.
            - Başlangıç dersinde "önce Orka IDE'de deneyelim, sonra istersen yerel IDE kurulumuna geçeriz" çizgisini koru.
            Eğer kullanıcı pratik bir kodlama, algoritma problemi veya hands-on bir görev adımındaysa:
            1. Yanıtının herhangi bir yerinde (tercihen sonunda veya görev başlığından hemen önce) tam olarak şu gizli etiketi kullan: `[IDE_OPEN]` (Bu, kullanıcının kod editörünü otomatik açacaktır).
            2. Görevi şu formatta ver:
               [IDE_OPEN]
               ## GÖREV
               (Görev açıklaması)
               ## BEKLENEN ÇIKTI
               (Beklenen sonuç)
               Ardından küçük bir başlangıç kodu (boilerplate) sağla.

            ÖRNEK: "Harika! Şimdi bir for döngüsü yazalım. [IDE_OPEN] ## GÖREV: 1'den 10'a kadar sayıları ekrana yazan bir döngü kur."
            """;

        // V4 bloğu — interpolation'sız ayrı string (LaTeX/JSON-style {} kaçışı için).
        prompt += V4VisualizationAndVoiceBlock;

        if (isQuizPending)
        {
            prompt += """

                [SİSTEM BİLDİRİMİ]: Konu özeti Wiki'ye kaydedildi.
                Kullanıcıya konuyu tamamladığını bildir ve kısa bir pekiştirme testi çözmek isteyip istemediğini sor.
                """;
        }

        if ((content ?? "").Contains("FocusSourceRef:"))
        {
            prompt += """

                [STRICT DOCUMENT GROUNDING ACTIVE]:
                Sen şu an belgeden cevaplama modundasın. SADECE enjekte edilen 'doc' (NotebookLM) kaynaklarındaki bilgileri kullanmalısın.
                Dış bilgileri, varsayımları veya diğer genel bilgileri karıştırma. Eğer cevap belgede yoksa, 'Bu bilgi belgede yer alamamaktadır.' veya 'Bu bilgi belgede yer almıyor.' şeklinde cevap ver.
                """;
        }

        return prompt;
    }

    private static async Task<HashSet<Guid>> GetTopicScopeIdsAsync(
        OrkaDbContext db,
        Guid userId,
        Guid topicId,
        CancellationToken ct = default)
    {
        var topics = await db.Topics
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => new { t.Id, t.ParentTopicId })
            .ToListAsync(ct);

        var result = new HashSet<Guid> { topicId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var topic in topics)
            {
                if (topic.ParentTopicId.HasValue &&
                    result.Contains(topic.ParentTopicId.Value) &&
                    result.Add(topic.Id))
                {
                    changed = true;
                }
            }
        }

        return result;
    }
}
