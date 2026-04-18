using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly IAIServiceChain _chain;
    private readonly IContextBuilder _contextBuilder;
    private readonly IAIAgentFactory _factory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGraderAgent _grader;
    private readonly IWikiService _wikiService;
    private readonly ILogger<TutorAgent> _logger;
    private readonly IRedisMemoryService _redisService;

    // Wiki context için maksimum karakter sınırı (yaklaşık 500 token)
    private const int WikiContextMaxChars = 2000;

    public TutorAgent(
        IAIServiceChain chain,
        IContextBuilder contextBuilder,
        IAIAgentFactory factory,
        IServiceScopeFactory scopeFactory,
        IGraderAgent grader,
        IWikiService wikiService,
        ILogger<TutorAgent> logger,
        IRedisMemoryService redisService)
    {
        _chain = chain;
        _contextBuilder = contextBuilder;
        _factory = factory;
        _scopeFactory = scopeFactory;
        _grader = grader;
        _wikiService = wikiService;
        _logger = logger;
        _redisService = redisService;
    }

    public async Task<string> GetResponseAsync(Guid userId, string content, Session session, bool isQuizPending)
    {
        // Faz 11+12: 5 context kaynağı paralel çekilir — herhangi biri başarısız olursa boş string döner
        var contextTask = _contextBuilder.BuildConversationContextAsync(session);
        var parallelResults = await Task.WhenAll(
            FetchUserMemoryProfileAsync(userId, session.TopicId),
            FetchPerformanceProfileAsync(session.Id, session.TopicId),
            FetchWikiContextAsync(session.TopicId, userId),
            FetchPistonContextAsync(session.Id),
            FetchGoldExamplesAsync(session.TopicId)
        );

        var contextMessages = await contextTask;
        var systemPrompt = BuildTutorSystemPrompt(
            isQuizPending,
            memoryContext:    parallelResults[0],
            performanceHint:  parallelResults[1],
            wikiContext:      parallelResults[2],
            pistonContext:    parallelResults[3],
            goldExamples:     parallelResults[4]);
        var userMessage = BuildContextSummary(contextMessages);

        return await _factory.CompleteChatAsync(AgentRole.Tutor, systemPrompt, userMessage);
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

            Kullanıcıyı sıcak karşıla, planı kısaca tanıt ve ilk adımla başlamaya davet et.
            Yanıtın samimi, motive edici ve 3-4 cümle olsun.
            [KESİN KURAL]: Yukarıdaki planda olmayan hiçbir konu başlığını asla ekleme veya önermeme.
            """;

        var contextMessages = await _contextBuilder.BuildConversationContextAsync(session);
        var userMessage = BuildContextSummary(contextMessages);
        
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

            [GÖREV KAPSAMI]: Bu dersi kullanıcının öğrenme potansiyelini zirveye taşıyacak şekilde anlat. 
            - Adım 1: "Neden bu konu önemli?" (Kısa ve ilgi çekici bir giriş).
            - Adım 2: Teknik terimleri basite indirgeyerek ve benzetmeler (analoji) kullanarak açıkla.
            - Adım 3: Gerçek dünyadan somut bir senaryo veya kod örneği ver.
            - Dil: İçten, Türkçe ve akademik standartlardan şaşmayan bir dil kullan. Konuyu boğucu şekilde uzatma (300-400 kelime civarı tut).
            {curriculumNote}
            """;

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

            ÇIKTI KURALI:
            - SADECE aşağıdaki JSON dizisini döndür. Markdown tırnağı EKLEME.
            - "type" özelliği çok önemlidir. Eğer soru SADECE sözel bilgi veya kavram ölçüyorsa "multiple_choice" yap ve 4 şık ekle. Eğer soru YAZILIM/PROGRAMLAMA veya ALGORİTMA kodu yazmayı gerektiriyorsa "type": "coding" yap ve şıkları BOŞ bırak ("options": []).
            - KODLAMA SORUSU KISITI: Dizideki SON soru bir coding sorusu OLABİLİR; ancak dizinin ortasına asla coding sorusu koyma. Coding sorusu en fazla 1 tanedir ve sadece son indexte yer alır. Konu kod yazmayı hiç gerektirmiyorsa coding sorusu ekleme.
            
            [
              {
                "type": "multiple_choice", // veya "coding"
                "question": "Soru metni",
                "options": [
                  { "text": "...", "isCorrect": false },
                  { "text": "...", "isCorrect": true },
                  { "text": "...", "isCorrect": false },
                  { "text": "...", "isCorrect": false }
                ],
                "explanation": "Detaylı açıklama",
                "topic": "{{topicTitle}}"
              },
              ... (TOPLAM 5 SORU)
            ]

            DİL: Türkçe.
            """;

        return await _chain.GenerateWithFallbackAsync(systemPrompt, $"Konu: \"{topicTitle}\"");
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

        var systemPrompt = """
            Sen bir öğretmensin. Öğrencinin cevabını değerlendir.
            Cevap kabul edilebilir doğrulukta ise SADECE 'DOĞRU' yaz.
            Yanlışsa SADECE 'YANLIŞ' yaz. Başka hiçbir şey yazma.
            """;

        var result = await _chain.GenerateWithFallbackAsync(
            systemPrompt,
            $"Soru: {question}\nÖğrenci Cevabı: {answer}");

        return result.Contains("DOĞRU", StringComparison.OrdinalIgnoreCase);
    }

    public async IAsyncEnumerable<string> GetResponseStreamAsync(Guid userId, string content, Session session, bool isQuizPending, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // Faz 11+12: 5 context kaynağı paralel çekilir — herhangi biri başarısız olursa boş string döner
        var contextTask = _contextBuilder.BuildConversationContextAsync(session);
        var parallelResults = await Task.WhenAll(
            FetchUserMemoryProfileAsync(userId, session.TopicId),
            FetchPerformanceProfileAsync(session.Id, session.TopicId),
            FetchWikiContextAsync(session.TopicId, userId),
            FetchPistonContextAsync(session.Id),
            FetchGoldExamplesAsync(session.TopicId)
        );

        var contextMessages = await contextTask;
        var systemPrompt = BuildTutorSystemPrompt(
            isQuizPending,
            memoryContext:    parallelResults[0],
            performanceHint:  parallelResults[1],
            wikiContext:      parallelResults[2],
            pistonContext:    parallelResults[3],
            goldExamples:     parallelResults[4]);
        var userMessage = BuildContextSummary(contextMessages);

        // AIAgentFactory: GitHub Models → Groq → Gemini (otomatik failover)
        await foreach (var chunk in _factory.StreamChatAsync(AgentRole.Tutor, systemPrompt, userMessage, ct))
        {
            yield return chunk;
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
        var msgs = context.TakeLast(8).ToList();
        if (msgs.Count == 0) return "(Yeni konuşma)";
        return string.Join("\n", msgs.Select(m => $"{(m.Role?.ToLower() == "user" ? "Kullanıcı" : "Asistan")}: {m.Content}"));
    }

    private string BuildTutorSystemPrompt(
        bool isQuizPending,
        string memoryContext   = "",
        string performanceHint = "",
        string wikiContext     = "",
        string pistonContext   = "",
        string goldExamples    = "")
    {
        var prompt = $$"""
            Sen Orka AI — Kullanıcının özel öğretmeni ve bilge bir mentorusun.
            {{memoryContext}}
            {{performanceHint}}
            {{wikiContext}}
            {{pistonContext}}
            {{goldExamples}}

            [TEMEL KURAL — SOHBET TARZI]:
            Chat ekranında ASLA uzun, maddeli, ansiklopedik yanıtlar verme.
            Özel ders hocası gibi davran: kısa cümleler kur, merak uyandır, öğrencinin düşünmesini sağla.
            Teknik bilgileri Wiki'ye bırak; sen sadece öğrenciye özel, samimi, konuşmalı anlatım yap.
            Yanıtların 3-6 cümle olsun. Anlatan değil, sorgulatan ol.

            [KİMLİK VE TON]:
            1. Samimi, cesaretlendirici, sabırlı bir mentor gibi konuş.
            2. Öğrencinin merakını kıvılcımla. Bir konsept anlattıktan sonra "Peki sence neden böyle?" veya "Bunu bir deneyelim mi?" gibi sorularla sürükle.
            3. Emoji kullanımı: tutumlu ama etkili (📌 🔥 💡 gibi).

            [ANLATIM KURALLARI]:
            - Konuyu anlatmak için 3-6 cümle yeterli. Özet geç, detayı öğrenci sorunca ver.
            - Kod örneği vereceksen kısa bir parçacık ver (5-10 satır), uzun bloklar verme.
            - "Bu konuyu tam anlamak için şunu da bilmek lazım..." gibi köprüler kur.
            - Kullanıcı selamlama yaparsa: sıcak, kısa karşılık ver ve bir konuya davet et.

            [SOHBET ÖRNEKLERİ]:
            - Kullanıcı: "nasılsın" → Sen: "Harikayım! Bugün ne öğrenmek istersin? 🚀"
            - Kullanıcı: "JavaScript'te promise nedir?" → Sen: "Promise, JavaScript'in 'söz verme' mekanizması. Asenkron işleminiz bitince sonucu teslim etmeyi taahhüt ediyor. Peki neden ihtiyaç duyuldu sence? 🤔"
            - Kullanıcı: "anladım" → Sen: "Harika! Pekiştirmek için küçük bir soru sorsam olur mu?"

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
