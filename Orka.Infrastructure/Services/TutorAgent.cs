using System.Collections.Generic;
using Orka.Core.Entities;
using Orka.Core.Interfaces;

namespace Orka.Infrastructure.Services;

public class TutorAgent : ITutorAgent
{
    private readonly IAIServiceChain _chain;
    private readonly IContextBuilder _contextBuilder;

    public TutorAgent(IAIServiceChain chain, IContextBuilder contextBuilder)
    {
        _chain = chain;
        _contextBuilder = contextBuilder;
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
        return await _chain.GetResponseWithFallbackAsync(contextMessages, systemPrompt);
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
            Sen Orka AI'nın profesyonel eğitmenisin.

            Kullanıcı "{parentTopicTitle}" konusunu Derinlemesine Plan ile öğrenmeye başladı.
            Plan başarıyla oluşturuldu ve Wiki'ye işlendi. Şimdi "{lessonTitle}" alt konusunu anlatıyorsun.

            [GÖREV]: Konuyu temel kavramlardan başlayarak, sıfır bilgiyle anlayan biri için öğret.
            - Türkçe yaz
            - Şu yapıyı kullan: (1) Net tanım, (2) 1 somut örnek, (3) 1 pratik kullanım senaryosu
            - Toplam 250-350 kelime — gereksiz laf kalabalığından kaçın
            - Sonunda 1 açık uçlu soru sor (kullanıcıyı düşündür, cevabı bekleme)
            {curriculumNote}
            """;

        return await _chain.GetResponseWithFallbackAsync(new List<Message>(), systemPrompt);
    }

    public async Task<string> GenerateQuizQuestionAsync(string topicTitle)
    {
        var systemPrompt = """
            Sen sınav yapan bir eğitmenisin.
            Verilen konu başlığı için şu adımları izle:

            1. Türkçe 1-2 kısa giriş cümlesi yaz (ör. "Hızlı bir soru sormak istiyorum:").
            2. Yanıtın SONUNA aşağıdaki formatı kullan — 4 seçenekli çoktan seçmeli soru,
               doğru seçeneğin 0-tabanlı indeksi (correctIndex):

            ```quiz
            {"question":"Soru metni buraya","options":["A) Seçenek 1","B) Seçenek 2","C) Seçenek 3","D) Seçenek 4"],"correctIndex":1}
            ```

            KURAL: JSON bloğu DİĞER metinden AYRI olmalı. Türkçe yaz. Açıklama ekleme.
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

    public async Task<string> GetResponseAsync(Guid userId, string content, Session session, bool isQuizPending)
    {
        var contextMessages = await _contextBuilder.BuildConversationContextAsync(session);

        var systemPrompt = $"""
            Sen Orka AI'nın profesyonel ve dostane eğitmenisin. Görevin kullanıcıya konuyu en açık ve derinlikli şekilde öğretmektir.

            [DÜŞÜNCE PROTOKOLÜ — Her yanıt öncesi bu adımları uygula]:
            1. Kullanıcının mevcut anlama seviyesini sohbet geçmişinden analiz et.
            2. Hangi kavramın eksik, belirsiz veya yanlış anlaşıldığını tespit et.
            3. En uygun pedagojik yaklaşımı seç (örnek, analoji, adım adım açıklama).
            4. Kısa ve öz, ancak eksiksiz bir yanıt hazırla.

            [YOĞUNLUK KURALI]:
            - Yanıt uzunluğunu sorunun karmaşıklığına göre ayarla.
            - Teknik bir kavramı 1 örnekle açıklayabiliyorsan 3 örnekle açıklama.
            - Kullanıcı selamlama veya çok kısa bir mesaj gönderirse kısa, samimi yanıt ver — yeni konu başlatma, laf kalabalığı yapma.
            - Kullanıcı açıkça "daha fazla anlat" demeden içerik genişletme.

            [KESİN KURAL]: Hiçbir zaman boş veya "Anlıyorum" gibi içeriksiz yanıt verme. Her yanıt öğretime katkı sağlamalıdır.
            """;

        if (isQuizPending)
        {
            systemPrompt += """

                [SİSTEM BİLDİRİMİ]: Arka planda konu özeti Wiki'ye kaydedildi ve test soruları hazırlandı.
                Kullanıcıya konunun tamamlandığını ve bilgilerin kayıt altına alındığını bildir.
                Şimdi hazırlanmış pekiştirme testini çözmek isteyip istemediğini sor.
                """;
        }

        return await _chain.GetResponseWithFallbackAsync(contextMessages, systemPrompt);
    }
}
