using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public class TutorAgent : ITutorAgent
{
    private readonly IAIServiceChain _chain;
    private readonly IContextBuilder _contextBuilder;
    private readonly IAIAgentFactory _factory;
    private readonly ILogger<TutorAgent> _logger;

    public TutorAgent(
        IAIServiceChain chain,
        IContextBuilder contextBuilder,
        IAIAgentFactory factory,
        ILogger<TutorAgent> logger)
    {
        _chain = chain;
        _contextBuilder = contextBuilder;
        _factory = factory;
        _logger = logger;
    }

    public async Task<string> GetResponseAsync(Guid userId, string content, Session session, bool isQuizPending)
    {
        var contextMessages = await _contextBuilder.BuildConversationContextAsync(session);
        var systemPrompt = BuildTutorSystemPrompt(isQuizPending);
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

    public async Task<string> GenerateQuizQuestionAsync(string topicTitle, string? researchContext = null)
    {
        var contextInfo = string.IsNullOrWhiteSpace(researchContext) 
            ? "" 
            : $"\n\n[ARAŞTIRMA VERİLERİ (GÜNCEL BİLGİ KAYNAĞI)]:\n{researchContext}\n\nLütfen yukarıdaki araştırma verilerini kullanarak konuyu daha güncel ve teknik bir seviyede sorgula.";

        var systemPrompt = $$"""
            Sen akademik düzeyde bir 'Eğitim Değerlendiricisi (Educational Assessor)' botusun.
            Görevin: Kullanıcının verilen konudaki kavramsal anlayışını tek bir soru ile ölçmek.

            {{contextInfo}}

            SORU TİPİ KURALI (KESİNLİKLE UYULACAK):
            - Mantık sorgulayan, geniş açılı bir çoktan seçmeli soru hazırla.
            - AŞIRI SPESİFİK API isimleri, versiyon numaraları veya ezber gerektiren detaylara ASLA girme.
            - Doğru cevap, konuya hakim biri tarafından mantıkla çıkarılabilir olmalı.

            ÇIKTI KURALI (KESİNLİKLE UYULACAK):
            - SADECE aşağıdaki JSON nesnesini döndür. Giriş metni, açıklama veya markdown EKLEME.
            - "text" alanlarına A), B), C) gibi ön ek EKLEME — sadece seçenek metnini yaz.

            {
              "question": "Net, düşünmeye sevk eden soru metni",
              "options": [
                { "text": "Seçenek metni (çeldirici)", "isCorrect": false },
                { "text": "Seçenek metni (doğru)", "isCorrect": true },
                { "text": "Seçenek metni (çeldirici)", "isCorrect": false },
                { "text": "Seçenek metni (çeldirici)", "isCorrect": false }
              ],
              "explanation": "Neden bu cevabın doğru olduğunun kısa ve net açıklaması."
            }

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
        var contextMessages = await _contextBuilder.BuildConversationContextAsync(session);
        var systemPrompt    = BuildTutorSystemPrompt(isQuizPending);
        var userMessage     = BuildContextSummary(contextMessages);

        // AIAgentFactory: GitHub Models → Groq → Gemini (otomatik failover)
        await foreach (var chunk in _factory.StreamChatAsync(AgentRole.Tutor, systemPrompt, userMessage, ct))
        {
            yield return chunk;
        }
    }

    private static string BuildContextSummary(IEnumerable<Message> context)
    {
        var msgs = context.TakeLast(8).ToList();
        if (msgs.Count == 0) return "(Yeni konuşma)";
        return string.Join("\n", msgs.Select(m => $"{(m.Role?.ToLower() == "user" ? "Kullanıcı" : "Asistan")}: {m.Content}"));
    }

    private string BuildTutorSystemPrompt(bool isQuizPending)
    {
        var prompt = """
            Sen Orka AI — Kullanıcının özel öğretmeni ve bilge bir mentorusun.

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
