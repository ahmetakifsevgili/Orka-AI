using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;

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

    // Wiki context için maksimum karakter sınırı (yaklaşık 1000 token)
    private const int WikiContextMaxChars = 4000;

    // V4: zengin görselleştirme + voice + drill-down kuralları (interpolation'sız raw).
    // LaTeX/JSON-style süslü parantez kaçışı bu blokta gerekmiyor.
    private const string V4VisualizationAndVoiceBlock = """

            [V4 ZENGİN GÖRSELLEŞTİRME — ZORUNLU]:
            Anlatımını metinle sınırlama. Konunun türüne göre şu öğeleri DOĞAL OLARAK serpiştir:

            1. MATEMATİK / FORMÜL → LaTeX kullan (frontend KaTeX ile render eder):
               - Inline:  $E = mc^2$
               - Block:   $$\int_0^\infty e^{-x^2} dx = \frac{\sqrt{\pi}}{2}$$
               Hiçbir formülü düz metin olarak yazma; "x kare" yerine $x^2$.

            2. MİMARİ / AKIŞ / İLİŞKİ → Mermaid diyagramı kullan:
               ```mermaid
               flowchart LR
                 A[İstek] --> B[Auth] --> C[İş Mantığı] --> D[Veritabanı]
               ```
               State machine, sınıf diyagramı, sıra diyagramı için de aynı.

            3. SOYUT KAVRAM → Pollinations.ai görseli embed et (öğrenci görmesi gerekirse):
               Format: ![kısa açıklama](https://image.pollinations.ai/prompt/<URL_ENCODED_PROMPT>?width=512&height=512&nologo=true)
               Örnek: "Mitokondri hücrenin enerji santralidir. ![mitokondri kesit](https://image.pollinations.ai/prompt/cross-section%20of%20mitochondrion%20educational%20diagram?width=512&height=512&nologo=true)"
               Karmaşık konuları açıklarken metni uzatmak yerine mutlaka görsel kullan.

            4. KAYNAK / DAYANAK → Wiki veya Korteks raporundaki bilgiyi alıntılarken inline link ver:
               "Algoritmanın temel mantığı 1936'da Turing tarafından ortaya kondu ([Wikipedia: Turing Machine](https://en.wikipedia.org/wiki/Turing_machine))."
               Frontend bu linkleri hover preview'lı citation olarak gösterir.

            [P4 GÖRSEL ÖĞRENME VALIDATOR]:
            Cevabı göndermeden önce zihinsel kontrol yap:
            - Konu matematik/formül içeriyorsa en az bir LaTeX formül veya adım tablosu olmalı.
            - Konu algoritma, mimari, süreç, sistem veya workflow ise en az bir Mermaid akış/sequence/state diyagramı olmalı.
            - Konu ezber, tarih, dil veya sınav hazırlığı ise kısa tablo, timeline, kart veya tekrar planı olmalı.
            - Öğrenci zayıf beceri veya "anlamadım" sinyali verdiyse açıklama + somut örnek + mikro kontrol sorusu birlikte gelmeli.
            - Uygun görsel öğe yoksa yanıtı tamamlanmış sayma; kısa ama öğretici bir görsel iskelet ekle.

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
            - Pollinations görseli veya çok somut bir gerçek-hayat örneği şart.
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
        IEducatorCoreService educatorCore)
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
    }

    public async Task<string> GetResponseAsync(Guid userId, string content, Session session, bool isQuizPending)
    {
        // Faz 11+12+16: Context kaynakları paralel çekilir — topicId yoksa topic-bağımlı olanlar atlanır
        var contextTask = _contextBuilder.BuildConversationContextAsync(session);
        var hasTopic = session.TopicId.HasValue;

        var parallelResults = await Task.WhenAll(
            hasTopic ? FetchActiveTopicContextAsync(userId, session.TopicId) : Task.FromResult(string.Empty),
            hasTopic ? FetchUserMemoryProfileAsync(userId, session.TopicId) : Task.FromResult(string.Empty),
            FetchPerformanceProfileAsync(session.Id, session.TopicId),
            hasTopic ? FetchWikiContextAsync(session.TopicId, userId) : Task.FromResult(string.Empty),
            FetchPistonContextAsync(session.Id),
            hasTopic ? FetchGoldExamplesAsync(session.TopicId) : Task.FromResult(string.Empty),
            FetchLowQualityFeedbackAsync(session.Id),
            hasTopic ? FetchNotebookContextAsync(userId, session.TopicId, content) : Task.FromResult(string.Empty),
            hasTopic ? FetchLearningSignalContextAsync(userId, session.TopicId) : Task.FromResult(string.Empty),
            hasTopic ? FetchYouTubeContextAsync(userId, session.TopicId) : Task.FromResult(string.Empty)
        );

        var contextMessages = await contextTask;
        var teacherContext = await _educatorCore.BuildTeacherContextAsync(
            userId,
            session.TopicId,
            session.Id,
            content,
            parallelResults[7],
            parallelResults[3],
            parallelResults[8],
            parallelResults[9]);

        var systemPrompt = BuildTutorSystemPrompt(
            isQuizPending,
            activeTopicContext: parallelResults[0],
            memoryContext:      parallelResults[1],
            performanceHint:    parallelResults[2],
            wikiContext:        parallelResults[3],
            pistonContext:      parallelResults[4],
            goldExamples:       parallelResults[5],
            lowQualityHint:     parallelResults[6],
            notebookContext:    parallelResults[7],
            learningSignalContext: parallelResults[8],
            educatorCoreContext: teacherContext.PromptBlock);
        var userMessage = BuildContextSummary(contextMessages);

        var answer = await _factory.CompleteChatAsync(AgentRole.Tutor, systemPrompt, userMessage);
        await _educatorCore.RecordAnswerQualitySignalsAsync(userId, session.TopicId, session.Id, answer, teacherContext);
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
        // Faz 11+12+16: Context kaynakları paralel çekilir — topicId yoksa topic-bağımlı olanlar atlanır
        var contextTask = _contextBuilder.BuildConversationContextAsync(session);
        var hasTopic = session.TopicId.HasValue;

        var parallelResults = await Task.WhenAll(
            hasTopic ? FetchActiveTopicContextAsync(userId, session.TopicId) : Task.FromResult(string.Empty),
            hasTopic ? FetchUserMemoryProfileAsync(userId, session.TopicId) : Task.FromResult(string.Empty),
            FetchPerformanceProfileAsync(session.Id, session.TopicId),
            hasTopic ? FetchWikiContextAsync(session.TopicId, userId) : Task.FromResult(string.Empty),
            FetchPistonContextAsync(session.Id),
            hasTopic ? FetchGoldExamplesAsync(session.TopicId) : Task.FromResult(string.Empty),
            FetchLowQualityFeedbackAsync(session.Id),
            hasTopic ? FetchNotebookContextAsync(userId, session.TopicId, content, ct) : Task.FromResult(string.Empty),
            hasTopic ? FetchLearningSignalContextAsync(userId, session.TopicId, ct) : Task.FromResult(string.Empty),
            hasTopic ? FetchYouTubeContextAsync(userId, session.TopicId) : Task.FromResult(string.Empty)
        );

        var contextMessages = await contextTask;
        var teacherContext = await _educatorCore.BuildTeacherContextAsync(
            userId,
            session.TopicId,
            session.Id,
            content,
            parallelResults[7],
            parallelResults[3],
            parallelResults[8],
            parallelResults[9],
            ct);

        var systemPrompt = BuildTutorSystemPrompt(
            isQuizPending,
            activeTopicContext: parallelResults[0],
            memoryContext:      parallelResults[1],
            performanceHint:    parallelResults[2],
            wikiContext:        parallelResults[3],
            pistonContext:      parallelResults[4],
            goldExamples:       parallelResults[5],
            lowQualityHint:     parallelResults[6],
            notebookContext:    parallelResults[7],
            learningSignalContext: parallelResults[8],
            educatorCoreContext: teacherContext.PromptBlock);
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
    }

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
                "[TutorAgent] Düşük kalite uyarısı tüketildi. SessionId={SessionId} Score={Score}",
                sessionId, lq.Value.score);

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
            _logger.LogWarning(ex, "[TutorAgent] Düşük kalite flag'i okunamadı.");
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
            _logger.LogWarning(ex, "[TutorAgent] NotebookLM belge bağlamı okunamadı. TopicId={TopicId}", topicId);
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
                ? "Kayitli zayif beceri yok."
                : string.Join("\n", summary.WeakSkills.Take(5).Select(skill =>
                    $"- {skill.SkillTag} ({skill.TopicPath}): {skill.WrongCount}/{skill.TotalCount} zorlanma, dogruluk %{Math.Round(skill.Accuracy * 100)}"));

            var recentSignals = summary.RecentSignals.Count == 0
                ? "Yakin zamanda ogrenme sinyali yok."
                : string.Join("\n", summary.RecentSignals.Take(5).Select(signal => $"- {signal}"));

            return $"""

                [OGRENCI SINYAL OZETI - KISISELLESTIRME]
                Dogruluk: {summary.CorrectAttempts}/{summary.TotalAttempts} (%{Math.Round(summary.Accuracy * 100)})
                Zayif beceriler:
                {weakSkills}

                Son sinyaller:
                {recentSignals}

                [EYLEM]: Cevabi bu zayif becerilere gore kur. Ogrenci takildiysa ilgili alt beceriye don,
                kisa bir ornek ver, gerekirse mini tablo/diyagram kullan ve sonraki en iyi adimi oner.
                """;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TutorAgent] Learning signal context okunamadi. TopicId={TopicId}", topicId);
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
            _logger.LogWarning(ex, "[TutorAgent] YouTube context okunamadı. TopicId={TopicId}", topicId);
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
                - Kullanici "devam", "basla", "anlat" gibi kisa bir mesaj yazarsa konu sorma; bu aktif ders uzerinden dogrudan anlatima basla.
                """;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TutorAgent] Aktif ders baglami okunamadi. TopicId={TopicId}", topicId.Value);
            return string.Empty;
        }
    }

    private async Task<string> FetchUserMemoryProfileAsync(Guid userId, Guid? topicId)
    {
        if (!topicId.HasValue) return "";

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var recentFailedAttempts = await db.QuizAttempts
            .Where(q => q.UserId == userId && q.TopicId == topicId && !q.IsCorrect)
            .OrderByDescending(q => q.CreatedAt)
            .Take(3)
            .ToListAsync();

        // Faz 13: Zaten öğrenilmiş alt konular (tekrara gerek yok)
        var masteredSkills = await db.SkillMasteries
            .Where(sm => sm.UserId == userId && sm.TopicId == topicId)
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
            var failures = string.Join("\n", recentFailedAttempts.Select(q => $"- Zayıf Nokta: {q.Question}"));
            sb.AppendLine($"\n[ÖĞRENCİ PROFİLİ — Zorlandığı noktalar]:\n{failures}\nEğitim dilini bu zayıf noktaları güçlendirecek şekilde ayarla.");
        }

        return sb.Length > 0 ? sb.ToString() : "";
    }

    /// <summary>
    /// Faz 12: Konu için Redis'te kayıtlı altın örnekleri çeker ve few-shot formatına çevirir.
    /// EvaluatorAgent'ın 9-10 puan verdiği başarılı diyaloglar bu havuza düşer.
    /// </summary>
    private async Task<string> FetchGoldExamplesAsync(Guid? topicId)
    {
        if (!topicId.HasValue) return string.Empty;

        try
        {
            var examples = (await _redisService.GetGoldExamplesAsync(topicId.Value, 2)).ToList();
            if (examples.Count == 0) return string.Empty;

            _logger.LogInformation("[TutorAgent] {Count} altın örnek yüklendi. TopicId={TopicId}",
                examples.Count, topicId.Value);

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
            _logger.LogWarning(ex, "[TutorAgent] Altın örnekler yüklenemedi. TopicId={TopicId}", topicId.Value);
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

            _logger.LogInformation("[TutorAgent] Wiki context yüklendi: {CharCount} karakter. TopicId={TopicId}",
                wikiContent.Length, topicId.Value);

            return $"\n\n[KONU WİKİSİ — Bu konuda şimdiye kadar bilinenler]:\n{wikiContent}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TutorAgent] Wiki context yüklenemedi, standart moda devam. TopicId={TopicId}", topicId.Value);
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

            _logger.LogInformation("[TutorAgent] Piston context yüklendi. Session={SessionId}", sessionId);

            return $"\n\n[SON KOD ÇIKTISI — Öğrenci az önce şunu çalıştırdı]:\n{json}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TutorAgent] Piston context yüklenemedi. Session={SessionId}", sessionId);
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
            _logger.LogWarning(ex, "[TutorAgent] Redis performans notları okunurken hata oluştu, standart moda devam ediliyor.");
            return "";
        }
    }

    private static string BuildContextSummary(IEnumerable<Message> context)
    {
        var msgs = context.TakeLast(14).ToList();
        if (msgs.Count == 0) return "(Yeni konuşma)";
        return string.Join("\n", msgs.Select(m => $"{(m.Role?.ToLower() == "user" ? "Kullanıcı" : "Asistan")}: {m.Content}"));
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
        string educatorCoreContext = "")
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
            {{educatorCoreContext}}

            [TEMEL KURAL — ÖĞRETİM TARZI]:
            Chat ekranında konuyu detaylı, derinlemesine ve doyurucu bir şekilde anlat. Konuları asla yüzeysel veya çok kısa (1-2 cümle) geçme.
            Özel ders hocası gibi davran: karmaşık yapıları parçalara bölerek adım adım, net ve teknik olarak eksiksiz aktar.
            Sistemin akışı şöyledir: Chat ekranında DETAYLI EĞİTİM verilir, Wiki ekranında ise konunun ÖZETİ tutulur. Sen detaylı eğitimden sorumlusun.
            Anlatımını zenginleştir, mantığını ve felsefesini öğrenciye kavrat.

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

        return prompt;
    }
}
