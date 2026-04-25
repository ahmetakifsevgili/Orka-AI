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
    private readonly IContextBuilder _contextBuilder;
    private readonly IAIAgentFactory _factory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGraderAgent _grader;
    private readonly IWikiService _wikiService;
    private readonly IStudentProfileService _profileService;
    private readonly ILogger<TutorAgent> _logger;
    private readonly IRedisMemoryService _redisService;

    // Wiki context için maksimum karakter sınırı (yaklaşık 1000 token)
    private const int WikiContextMaxChars = 4000;

    public TutorAgent(
        IContextBuilder contextBuilder,
        IAIAgentFactory factory,
        IServiceScopeFactory scopeFactory,
        IGraderAgent grader,
        IWikiService wikiService,
        IStudentProfileService profileService,
        ILogger<TutorAgent> logger,
        IRedisMemoryService redisService)
    {
        _contextBuilder = contextBuilder;
        _factory = factory;
        _scopeFactory = scopeFactory;
        _grader = grader;
        _wikiService = wikiService;
        _profileService = profileService;
        _logger = logger;
        _redisService = redisService;
    }

    public async Task<string> GetResponseAsync(Guid userId, string content, Session session, bool isQuizPending, string? goalContext = null)
    {
        // Faz 11+12: 5 context kaynağı paralel çekilir — herhangi biri başarısız olursa boş string döner
        var contextTask = _contextBuilder.BuildConversationContextAsync(session);
        var parallelResults = await Task.WhenAll(
            FetchUserMemoryProfileAsync(userId, session.TopicId),
            FetchPerformanceProfileAsync(session.Id, session.TopicId),
            FetchWikiContextAsync(session.TopicId, userId),
            FetchPistonContextAsync(session.Id),
            FetchGoldExamplesAsync(session.TopicId),
            _profileService.BuildProfileBlockAsync(userId)
        );

        var contextMessages = await contextTask;
        var systemPrompt = BuildTutorSystemPrompt(
            isQuizPending,
            memoryContext: parallelResults[0],
            performanceHint: parallelResults[1],
            wikiContext: parallelResults[2],
            pistonContext: parallelResults[3],
            goldExamples: parallelResults[4],
            studentProfile: parallelResults[5],
            goalContext: goalContext);
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

            [ZENGİN GÖRSEL VE DİYAGRAM KULLANIMI — ZORUNLU KURAL]:
            Kavramları anlatırken KESİNLİKLE markdown destekli görsel veya diyagram kullanmalısın!
            1. DİNAMİK RESİM SENTEZİ: Fotogerçekçi sahneler, haritalar veya olaylar için: `![Açıklama](https://image.pollinations.ai/prompt/URL_ENCODED_INGILIZCE_PROMPT?width=800&height=400&nologo=true)`
            2. MERMAID.JS DİYAGRAMLARI: Zihin haritaları, kronoloji, algoritmalar veya soyut kavramlar için markdown içinde ```mermaid kodu kullan.
            Dersi sadece kuru metinle bitirmek YASAKTIR. En az bir görsel veya diyagram ZORUNLUDUR.
            """;

        return await _factory.CompleteChatAsync(AgentRole.Tutor, systemPrompt, $"Konu: {lessonTitle}");
    }

    public async Task<string> GetRemedialLessonAsync(string lessonTitle, string weaknesses)
    {
        var systemPrompt = $$"""
            Sen Orka Akademi'nin baş öğretmenisin (TutorAgent).
            Öğrenci "{{lessonTitle}}" konusunun değerlendirme sınavında yeterli başarıyı gösteremedi.
            Aşağıda öğrencinin yaptığı hatalar (zayıf yönler) bulunuyor:
            [ZAYIF YÖNLER]: {{weaknesses}}

            GÖREV: Öğrencinin sıradaki konuya geçmeden önce bu konuyu tam olarak pekiştirmesi için telafi (remedial) dersi hazırla.
            
            KURALLAR:
            - Tamamen baştan konu anlatımı YAPMA. Sadece [ZAYIF YÖNLER] kısmında belirtilen hatalara ve eksikliklere odaklan.
            - Samimi, destekleyici ve motive edici bir giriş yap ("Hiç sorun değil, bazen bu konular kafa karıştırabilir. Hatalarımıza birlikte bakalım...").
            - Yeni açılardan anlat, farklı örnekler ver.
            - Hataları doğrudan hedef alarak nerede yanılmış olabileceğini şefkatle açıkla.

            [ZENGİN GÖRSEL VE DİYAGRAM KULLANIMI — ZORUNLU KURAL]:
            Telafi dersini kuru metinle geçiştirmek YASAKTIR. Anlaşılmayan kavramı somutlaştırmak için KESİNLİKLE:
            1. DİNAMİK RESİM: `![Açıklama](https://image.pollinations.ai/prompt/URL_ENCODED_INGILIZCE_PROMPT?width=800&height=400&nologo=true)`
            2. MERMAID DİYAGRAMI: Sebep-sonuç veya akışlar için ```mermaid bloğu kullan.
            Bunlardan en az birini kullanarak eksik parçayı görsel olarak tamamla!
            """;

        return await _factory.CompleteChatAsync(AgentRole.Tutor, systemPrompt, $"Telafi Dersi: {lessonTitle}");
    }

    public async Task<string> GenerateTopicQuizAsync(string topicTitle, Guid userId, Guid topicId, string? goalContext = null, string? researchContext = null, int questionCount = 5, string? weaknessContext = null)
    {
        var contextInfo = "";
        if (!string.IsNullOrWhiteSpace(researchContext))
        {
            _logger.LogInformation("[TutorAgent] Sınav soruları üretilmeden önce araştırma konteksti Grader denetiminden geçiyor...");
            var isRelevant = await _grader.IsContextRelevantAsync(topicTitle, researchContext, goalContext: goalContext);
            if (isRelevant)
            {
                contextInfo = $"\n\n[ARAŞTIRMA VERİLERİ (GÜNCEL BİLGİ KAYNAĞI — SORULARI BURADAN ÇIKARABİLİRSİN)]:\n{researchContext}\n\nYukarıdaki araştırma verilerini kullanarak daha güncel, gerçek dünya senaryolarına dayalı sorular üret.";
            }
        }

        // Faz 16: Adaptive Quiz — öğrencinin bilinen zayıf yönlerini quiz'e odakla
        string weaknessInfo = "";
        if (!string.IsNullOrWhiteSpace(weaknessContext))
        {
            weaknessInfo = $"\n\n[ZAYIF YÖN ODAĞI — ADAPTİF SINAV KURALI]:\nBu öğrenci daha önce şu konularda hatalar yaptı / zorlandı:\n{weaknessContext}\n\nKESİN KURAL: Soruların en az %40'ı bu zayıf yönlere odaklanmalı. Öğrencinin aynı hataları tekrarlamasını önlemek için bu kavramları farklı açılardan sor.";
            _logger.LogInformation("[TutorAgent] Adaptive Quiz: Zayıf yön odağı enjekte edildi.");
        }

        string pastQuestionsWarning = "";
        try 
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();
            var pastQuizMessages = await db.Messages
                .Include(m => m.Session)
                .Where(m => m.Session != null && m.Session.TopicId == topicId && m.UserId == userId && m.MessageType == MessageType.Quiz)
                .OrderByDescending(m => m.CreatedAt)
                .Take(5)
                .Select(m => m.Content)
                .ToListAsync();

            if (pastQuizMessages.Any())
            {
                var combinedPast = string.Join("\n---\n", pastQuizMessages);
                pastQuestionsWarning = $"\n\n[ÖNEMLİ - TEKRAR KURALI]: Öğrenci bu konuda daha önce quizler çözdü. AŞAĞIDAKİ SORULAR DAHA ÖNCE SORULDU:\n{combinedPast}\n\nLÜTFEN aynı mantığı, aynı soruyu veya benzer varyasyonları KESİNLİKLE TEKRARLAMA! Yepyeni açılardan, farklı vaka ve senaryolardan yola çıkarak taze ve zorlayıcı sorular üret.";
            }
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "Geçmiş quizleri çekerken hata oluştu.");
        }

        // Soru dağılımını soru sayısına göre dinamik oluştur
        var distribution = questionCount <= 5
            ? $"- 1-{Math.Min(2, questionCount)}: Temel kavramsal sorular (konuyu anlıyor mu?)\n- {Math.Min(3, questionCount)}-{questionCount}: Senaryo bazlı uygulama ve analiz"
            : questionCount <= 10
            ? $"- 1-3: Temel kavram ve tanım\n- 4-7: Uygulama, senaryo ve problem çözme\n- 8-{questionCount}: Analiz, sentez ve derinlemesine anlama"
            : $"- 1-4: Temel kavramlar\n- 5-9: Uygulama ve senaryo\n- 10-14: Analiz ve problem çözme\n- 15-{questionCount}: Uzman düzey ve kenar durumlar";

        var systemPrompt = $$"""
            Sen akademik düzeyde bir 'Eğitim Değerlendiricisi (Educational Assessor)' botusun.
            Görevin: Kullanıcının '{{topicTitle}}' konusundaki başarısını ölçmek için {{questionCount}} soruluk yüksek kaliteli bir test hazırlamak.
            Öğrencinin Özel Hedefi / Sınavı: {{(string.IsNullOrWhiteSpace(goalContext) ? "Genel Başarı" : goalContext)}}

            {{contextInfo}}
            {{weaknessInfo}}
            {{pastQuestionsWarning}}

            KONU AGNOSTİK KURAL (EN KRİTİK):
            - Testi hazırlarken ÖĞRENCİNİN HEDEFİNİ (Örn: KPSS, YKS, Mülakat) dikkate al. Zorluk derecesini ve soru tiplerini tamamen bu hedefe uygun hale getir.
            - Bu test 'Yazılım/Programlama' konusu için DEĞİL. Konu '{{topicTitle}}'dır.
            - Konuya göre uygun soru tipleri üret: Matematik ise hesap; Tarih ise analiz; Biyoloji ise kavram; Hukuk ise senaryo.
            - HİÇBİR ZAMAN yazılım, kod, IDE, API, programlama gibi terimler kullanma — konu gerektirmedikçe.

            SORU DAĞILIMI (Toplam {{questionCount}} Soru):
            {{distribution}}

            SORU KALİTESİ KURALI:
            - Basit tanımlama ("X nedir?") sorularından KAÇIN. Anlama, uygulama, analiz ve sentez odaklı ol.
            - Her soru bağımsız olmalı ve gerçekçi bir senaryoya ya da örneğe dayandırılmalı.
            - Seçenekler mantıklı çeldiriciler içermeli — cevabı belli olan ucuz sorular YASAK.
            - Konuya özel: formüller, tarihsel bağlam, bilimsel ilkeler, gerçek vakalar — neye uygunsa onu kullan.

            ÇIKTI KURALI:
            - SADECE aşağıdaki JSON dizisini döndür. Markdown, tırnak bloğu veya açıklama EKLEME.
            - Tüm sorular "multiple_choice" tipinde olacak. "coding" veya "open_ended" tipi YASAK.
            - Her sorunun mutlaka 4 şıkkı ve bir açıklaması olacak.
            
            [
              {
                "type": "multiple_choice",
                "question": "Soru metni — senaryo veya örneğe dayandırılmış",
                "options": [
                  { "text": "...", "isCorrect": false },
                  { "text": "...", "isCorrect": true },
                  { "text": "...", "isCorrect": false },
                  { "text": "...", "isCorrect": false }
                ],
                "explanation": "Doğru cevabın detaylı açıklaması, konuyla bağlantısı",
                "topic": "{{topicTitle}}"
              },
              ... (TOPLAM {{questionCount}} SORU)
            ]

            DİL: Türkçe.
            """;

        return await _factory.CompleteChatAsync(AgentRole.Tutor, systemPrompt, $"Konu: \"{topicTitle}\"");
    }

    public async Task<(double score, string feedback)> EvaluateQuizAnswerAsync(string question, string answer, string? goalContext = null)
    {
#if DEBUG
        // ── PLAYWRIGHT BACKDOOR (yalnızca DEBUG build'de aktif) ──────────────
        // E2E testlerde AI değerlendirmesini bypass etmek için kullanılır.
        // Release build'de bu blok derlenmez → production'da sıfır risk.
        if (answer.Contains("[PLAYWRIGHT_PASS_QUIZ]", StringComparison.Ordinal))
            return (1.0, "Otomatik Geçiş (E2E Test)");
        // ────────────────────────────────────────────────────────────────────
#endif

        // LaTeX/Unicode notasyon farkı judge'ı yanıltmasın diye sadeleşmiş formu da gönderiyoruz.
        var normalizedQuestion = LatexAnswerNormalizer.Normalize(question);
        var normalizedAnswer = LatexAnswerNormalizer.Normalize(answer);

        bool isCodingInfo = question.Contains("kod", StringComparison.OrdinalIgnoreCase) || 
                            question.Contains("ide", StringComparison.OrdinalIgnoreCase) || 
                            question.Contains("sınıf", StringComparison.OrdinalIgnoreCase) || 
                            question.Contains("fonksiyon", StringComparison.OrdinalIgnoreCase);

        var systemPrompt = isCodingInfo ? """
            Sen bir yazılım/kodlama öğretmenisin. Öğrencinin verdiği kodu veya yazılımsal cevabı değerlendirirsin.
            Kodun mantıksal olarak doğru olup olmadığına, istenen görevi yerine getirip getirmediğine bak. Sözdizimsel ufak hataları affedebilirsin ama ana mantık yanlışsa puan kır.
            """ : """
            Sen bir öğretmensin. Öğrencinin cevabını değerlendir.
            Öğrencinin Özel Hedefi / Sınavı: {{(string.IsNullOrWhiteSpace(goalContext) ? "Genel Başarı" : goalContext)}}
            Öğrencinin hedefini göz önünde bulundur. (Örn: KPSS ise genel geçer ifadeler eksik sayılabilir, detay aranabilir. Hobi ise mantığı anlaması yeterlidir.)
            
            LaTeX/Unicode notasyon farklılıklarını eşdeğer kabul et:
            - $\frac{1}{2}$, 1/2 ve ½ aynıdır.
            - \pi, π ve pi aynıdır.
            - \times, × ve * aynıdır.  \cdot ile * aynıdır.
            - x^{2}, x^2 ve x² aynıdır.
            - Boşluk ve markdown vurgusu (**, _, `) farkı önemsizdir.
            """;

        systemPrompt += """
            
            SADECE aşağıdaki JSON formatında yanıt ver. Başka hiçbir şey yazma. Markdown tırnaklarını kullanma:
            {
                "score": 0.8,
                "feedback": "Kısa, net ve yapıcı geri bildirim (Örn: Çoğunu doğru yaptın ama şurayı atladın)"
            }
            Not: score 0.0 ile 1.0 arasında bir değer olmalıdır (tamamen yanlış: 0.0, tamamen doğru: 1.0, kısmi doğru: 0.3, 0.5, 0.8 vb. olabilir).
            """;

        var userMessage = $"""
            Soru: {question}
            Sadeleşmiş Soru: {normalizedQuestion}

            Öğrenci Cevabı: {answer}
            Sadeleşmiş Cevap: {normalizedAnswer}
            """;

        try 
        {
            var resultStr = await _factory.CompleteChatAsync(AgentRole.Evaluator, systemPrompt, userMessage);
            var cleanJson = resultStr.Replace("```json", "").Replace("```", "").Trim();
            var result = System.Text.Json.JsonSerializer.Deserialize<QuizEvaluationResult>(cleanJson);
            return (result?.Score ?? 0.0, result?.Feedback ?? "Değerlendirme yapılamadı.");
        } 
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TutorAgent] Sınav değerlendirme (partial score) sırasında hata oluştu. Varsayılan olarak 0.0 dönülüyor.");
            return (0.0, "Cevabını değerlendirirken bir hata oluştu. Lütfen tekrar dener misin?");
        }
    }

    private class QuizEvaluationResult 
    {
        [System.Text.Json.Serialization.JsonPropertyName("score")]
        public double Score { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("feedback")]
        public string Feedback { get; set; }
    }

    public async IAsyncEnumerable<string> GetResponseStreamAsync(Guid userId, string content, Session session, bool isQuizPending, bool isVoiceMode = false, string? goalContext = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // Faz 11+12+B: 6 context kaynağı paralel çekilir — herhangi biri başarısız olursa boş string döner
        var contextTask = _contextBuilder.BuildConversationContextAsync(session);
        var parallelResults = await Task.WhenAll(
            FetchUserMemoryProfileAsync(userId, session.TopicId),
            FetchPerformanceProfileAsync(session.Id, session.TopicId),
            FetchWikiContextAsync(session.TopicId, userId),
            FetchPistonContextAsync(session.Id),
            FetchGoldExamplesAsync(session.TopicId),
            _profileService.BuildProfileBlockAsync(userId)
        );

        var contextMessages = await contextTask;
        var systemPrompt = BuildTutorSystemPrompt(
            isQuizPending,
            isVoiceMode,
            memoryContext: parallelResults[0],
            performanceHint: parallelResults[1],
            wikiContext: parallelResults[2],
            pistonContext: parallelResults[3],
            goldExamples: parallelResults[4],
            studentProfile: parallelResults[5],
            goalContext: goalContext);
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

            // Korteks araştırması tamamlandıysa prompt'a ipucu ekle
            string korteksHint = string.Empty;
            try
            {
                var korteksContent = await _redisService.GetKorteksResearchReportAsync(topicId.Value);
                if (!string.IsNullOrWhiteSpace(korteksContent))
                {
                    korteksHint = $"\n\n[KORTEKS ARAŞTIRMASI ÖZETİ]: Korteks Swarm derin araştırmayı tamamladı:\n{korteksContent}\n" +
                                  "Gerekirse 'Korteks Kütüphanesi'nden tam rapora bakabilirsin' diye yönlendir.";
                }
            }
            catch { /* Redis sinyali opsiyonel */ }

            if (string.IsNullOrWhiteSpace(wikiContent)) return korteksHint;

            // Token taşmasını önlemek için kırp
            if (wikiContent.Length > WikiContextMaxChars)
                wikiContent = wikiContent[..WikiContextMaxChars] + "\n[...devamı wiki panelinde]";

            _logger.LogInformation("[TutorAgent] Wiki context yüklendi: {CharCount} karakter. TopicId={TopicId}",
                wikiContent.Length, topicId.Value);

            return $"\n\n[KONU WİKİSİ — Bu konuda şimdiye kadar bilinenler]:\n{wikiContent}{korteksHint}";
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
        bool isVoiceMode = false,
        string memoryContext = "",
        string performanceHint = "",
        string wikiContext = "",
        string pistonContext = "",
        string goldExamples = "",
        string studentProfile = "",
        string? goalContext = null)
    {
        var prompt = $$"""
            Sen Orka AI — Kullanıcının özel öğretmeni ve bilge bir mentorusun.
            {{studentProfile}}
            
            [ÖĞRENCİNİN BU KONUDAKİ ÖZEL HEDEFİ / SINAVI]:
            {{(string.IsNullOrWhiteSpace(goalContext) ? "Belirtilmemiş (Genel)" : goalContext)}}

            GÖREV VE HEDEF UYUMU:
            Öğrencinin yukarıdaki hedefini ASLA GÖZARDI ETME. 
            Eğer hedef KPSS ise, anlatımlarını ÖSYM tarzına ve KPSS müfredatına uygun tut. Formüllere, ezber ipuçlarına odaklan.
            Eğer hedef YKS/LGS ise, üniversite/lise hazırlık konseptiyle ilerle.
            Eğer hedef Yazılım/Algoritma ise pratik yapmayı teşvik et.
            Eğer hedef Hobi ise fazla akademik detaya girme, günlük hayattan eğlenceli örnekler ver.

            [AŞAĞIDAKİ BAĞLAM VERİLERİNİ KULLAN (Varsa)]:
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

            [MATEMATİK VE FORMÜL RENDER — ZORUNLU]:
            Matematiksel ifadeleri, denklemleri, formülleri ASLA düz metin olarak yazma.
            KaTeX/LaTeX syntax'ı kullan (frontend otomatik render eder):
            - Satır içi: $a^2 + b^2 = c^2$
            - Blok seviyesi: $$\int_{0}^{\infty} e^{-x} dx = 1$$
            - Kesirler: $\frac{dy}{dx}$
            - Kök: $\sqrt{2}$ veya $\sqrt[3]{8}$
            - Grek harfleri: $\alpha, \beta, \pi, \theta, \Delta, \sigma$
            - Limit/toplam: $\lim_{n \to \infty}$, $\sum_{i=1}^{n}$
            KPSS, YKS, ALES, akademik konular için bu ZORUNLU. Matematik/fizik/kimya/mühendislik sorusunda LaTeX yoksa yanıt yarım kabul edilir.

            [ZENGİN GÖRSEL VE DİYAGRAM KULLANIMI — ZORUNLU]:
            Sıradan bir metin botu değil, görsel destekli bir eğitmensin. Kavramları anlatırken KESİNLİKLE aşağıdaki iki görselleştirme tekniğini kullanmalısın:

            A) DİNAMİK RESİM SENTEZİ (Fotogerçekçi Çizimler, Haritalar, Sahneler için):
            Eğer öğrencinin o olayı zihninde canlandırması gerekiyorsa (Tarihi savaşlar, coğrafi manzaralar, biyolojik hücreler), KESİNLİKLE şu markdown yapısını kullan:
            `![Görsel Açıklaması](https://image.pollinations.ai/prompt/{URL_ENCODED_DETAYLI_INGILIZCE_PROMPT}?width=800&height=400&nologo=true)`
            Örnek URL Formatı: `https://image.pollinations.ai/prompt/1453%20Conquest%20of%20Istanbul%20Ottoman%20epic%20cinematic%20painting?width=800&height=400&nologo=true`
            (Prompt kesinlikle İngilizce, detaylı ve boşlukları URL encode edilmiş '%20' şeklinde olmalıdır.)

            B) MERMAID.JS DİYAGRAMLARI (Soy ağaçları, Zaman çizelgeleri, Algoritmalar, Sebep-Sonuç ilişkileri için):
            Eğer bir tarihi akışı, yazılım algoritmasını, matematik akış şemasını veya zihin haritasını anlatıyorsan, KESİNLİKLE markdown code bloğu içinde `mermaid` dilini kullan.
            Örnek:
            ```mermaid
            timeline
                title Osmanlı Kuruluş Dönemi
                1299 : Söğüt'te Kuruluş
                1326 : Bursa'nın Fethi
            ```
            
            Görsel Kuralları:
            1. Ürettiğin resmi yönlendiren İngilizce prompt çok detaylı ve estetik (örn: 'highly detailed, cinematic') olmalı.
            2. Anlatı uzun sürüyorsa, metne boğmamak için aralara görsel veya diyagram serpiştir.
            3. Uydurma, fake domain kullanma. Sadece Pollinations.ai link formatını veya Mermaid.js kullan. Kırık link vermek YASAK.

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

        if (isVoiceMode)
        {
            prompt += """

                =========================================
                [KRİTİK MOD — SESLİ SINIF (VOICE CLASSROOM) AKTIF]
                =========================================
                UYARI: Bu mod aktifken yazdığın her kelime seslendirilecek.
                Sistemin sesi tetiklemesi için HER SATIRIN mutlaka [HOCA]: veya [ASISTAN]: etiketiyle başlaması ZORUNLUDUR.
                Bu etiketler olmadan yazdığın hiçbir metin kullanıcıya seslendirilemez — tamamen sessiz kalır.

                [ZORUNLU FORMAT — HER SATIR ETİKETLE BAŞLAMALI]:
                [HOCA]: <metin>
                [ASISTAN]: <metin>
                [HOCA]: <metin>
                ...

                YASAK FORMAT (asla böyle yazma — ses ÇIKMAZ):
                - Düz metin (etiketsiz)
                - Markdown başlıkları (## Başlık)
                - Madde işaretleri (* veya -)
                - Boş satırlar (etiketsiz)

                KARAKTERLER:
                - [HOCA]: Ana öğretmen. Konuyu detaylı ve bilgece anlatır. Türk aksanlı erkek sesi.
                - [ASISTAN]: Zeki ve meraklı asistan. Hocayı dinler, özetler, "Hocam yani şunu mu diyorsunuz?" diye sorar, öğrenciyi kontrol eder.

                PODCAST AKIŞ KURALLARI:
                1. Her konuşma sırası (turn) 1-3 cümleyi geçmesin. Hoca bir şey söylesin, Asistan devam etsin.
                2. Konunun küçük bir parçası bitince ASISTAN sorsun: "Ne dersin, buraya kadar anlaştık mı?"
                3. Öğrenci onay vermeden yeni alt konuya geçme.
                4. Eğer araştırma (Korteks) veya Wiki verisi varsa [HOCA] bunu doğal bir haber gibi aktar.

                ÖRNEK ÇIKTI:
                [HOCA]: Kuantum mekaniğinde süperpozisyon ilkesi, bir parçacığın aynı anda birden fazla durumda var olabileceğini söyler.
                [ASISTAN]: Yani Hocam, meşhur Schrödinger'in kedisi gibi — gözlemleyene kadar hem canlı hem ölü diyebilir miyiz?
                [HOCA]: Kesinlikle! Gözlem yapılana kadar tüm olasılıklar bir arada yaşar. Bu kuantum süperpozisyonun özüdür.
                [ASISTAN]: Çok ilginç! Peki arkadaşımız, bu kısmı anladık mı yoksa biraz daha açalım mı?
                =========================================
                """;
        }


        return prompt;
    }
}
