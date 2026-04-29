using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using Orka.Infrastructure.SemanticKernel.Plugins;

namespace Orka.Infrastructure.Services;

/// <summary>
/// Yeni konu için müfredat planı oluşturur ve Topics tablosuna kaydeder.
///
/// Model seçimi: GitHub Models (Meta-Llama-3.1-405B-Instruct) — Yüksek akıl yürütme.
/// Failover: AIAgentFactory → Groq → Gemini.
/// </summary>
public class DeepPlanAgent : IDeepPlanAgent
{
    private readonly IAIAgentFactory _factory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISupervisorAgent _supervisor;
    private readonly IGraderAgent _grader;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DeepPlanAgent> _logger;

    public DeepPlanAgent(
        IAIAgentFactory factory,
        IServiceScopeFactory scopeFactory,
        ISupervisorAgent supervisor,
        IGraderAgent grader,
        IServiceProvider serviceProvider,
        ILogger<DeepPlanAgent> logger)
    {
        _factory          = factory;
        _scopeFactory     = scopeFactory;
        _supervisor       = supervisor;
        _grader           = grader;
        _serviceProvider  = serviceProvider;
        _logger           = logger;
    }

    public async Task<List<Topic>> GenerateAndSaveDeepPlanAsync(
        Guid parentTopicId, string topicTitle, Guid userId, string userLevel = "Bilinmiyor", string? researchContext = null, string? failedTopics = null)
    {
        var modules = await GenerateModulesAsync(parentTopicId, topicTitle, userLevel, researchContext, failedTopics);
        return await SaveModularSubTopicsAsync(parentTopicId, modules, userId);
    }

    private async Task<List<ModuleDefinition>> GenerateModulesAsync(Guid parentTopicId, string topicTitle, string userLevel, string? researchContext = null, string? failedTopics = null)
    {
        _logger.LogInformation("[DeepPlan] Multi-Agent RAG döngüsü başlıyor. Konu: {Topic}", topicTitle);

        // 1. Durum Sınıflandırması (Supervisor Node)
        var intentCategory = await _supervisor.ClassifyIntentAsync(topicTitle);
        _logger.LogInformation("[DeepPlan] Katman: Supervisor -> Kategori: {Category}", intentCategory);

        // 2. RAG Kalite Kontrolü (Grader Node)
        var contextInfo = "";
        if (!string.IsNullOrWhiteSpace(researchContext))
        {
            _logger.LogInformation("[DeepPlan] Katman: Grader -> Research Context denetleniyor...");
            var isRelevant = await _grader.IsContextRelevantAsync(topicTitle, researchContext);
            if (isRelevant)
            {
                contextInfo = $"\n\n[ARAŞTIRMA VERİLERİ (GÜNCEL BİLGİ KAYNAĞI)]:\n{researchContext}\n\nLütfen yukarıdaki güncel verileri kullanarak konuyu mantıksal ve pedagojik bölümlere ayır.";
            }
            else
            {
                _logger.LogWarning("[DeepPlan] Grader REDDETTİ. Hallucination veya alakasız context engellendi. (Sıfır bilgi ile devam ediliyor)");
            }
        }

        string failedTopicsDiagnostic = "";
        if (!string.IsNullOrWhiteSpace(failedTopics))
        {
            failedTopicsDiagnostic = $"\n\n[DİKKAT - MİKRO TEŞHİS (ZAYIFLIK Analizi)]:\nÖğrenci şu konularda HATA YAPMIŞ veya zorlanmış: {failedTopics}.\nMüfredatı eksiksiz ve kapsamlı çıkar, ANCAK öğrencinin eksik olduğu bu konulara matkapla (drill) in! Bu kavramlar geçtiğinde müfredata ekstra 'Uygulamalı Örnekler', 'Derinlemesine Analiz' ve 'Pratik Lab' alt modülleri ekle. Diğer bildiği konularda standart anlatımla geç.";
        }

        // 3. YouTube Eğitim Videosu Referansı (en popüler eğitimcinin anlatım yapısı)
        var youtubeReference = await FetchYouTubeEducationalReferenceAsync(parentTopicId, topicTitle);
        var domainGuidance = BuildDomainPlanningGuidance(topicTitle);

        var systemPrompt = $$"""
            Sen akademik seviyede bir 'Müfredat Mimarı (Curriculum Architect)' botusun.
            Görev: Verilen konuyu profesyonel, kapsamlı ve konunun doğasına uygun bir müfredata dönüştürmek.
            Mevcut kullanıcının bilgi seviyesi: {{userLevel}}
            Konunun Alanı / Kategorisi: {{intentCategory}}
            {{contextInfo}}
            {{failedTopicsDiagnostic}}
            {{youtubeReference}}
            {{domainGuidance}}

            ORGANİZASYON KURALI (KRİTİK — KONUYA GÖRE AKILLI YAPILANDIR):
            - Kullanıcı cümlesinden (Örn: "C# çalışmak istiyorum") ASIL KONUYU çıkar ("C# Programlama"). Kesinlikle "Selamlaşma", "İstek", "Giriş" gibi konulardan bahsetme!
            - Programlama/teknoloji → "Temel Yapı Taşları → Uygulama Becerileri → İleri Düzey" yaklaşımı
            - Tarih/toplum → "Dönemsel sıralama" veya "Tematik gruplama"
            - Bilim/matematik → "Teorik temeller → Uygulamalı konular → İleri araştırma"
            - Sanat/dil → "Temel İfadeler → Dil Bilgisi → İleri Seviye Konuşma"

            MODÜL VE DERS İSİMLENDİRME KURALI (ŞİDDETLİ UYARI):
            - "Bölüm 1", "Modül 1", "Giriş", "Genel Bakış", "Temel Kavramlar", "Sonuç", "Uygulama", "Selamlaşma" gibi JENERİK, İÇİ BOŞ BAŞLIKLAR KESİNLİKLE YASAKTIR!
            - Modül başlıkları temanın teknik ve mantıksal özeti olmalı (Örn: JS için "Veri Tipleri ve Fonksiyonel Yaklaşım").
            - Ders (Topic) başlıkları tamamen spesifik olmalı (Örn: "Primitive vs Reference Type Farkları").
            - Müfredat SADECE seçilen uzmanlık alanının profesyonel teknik içeriğiyle dolu olmalıdır.
            - Her modülde 2 ile 4 arası DERS konusu olsun. Toplam 3 ile 5 MODÜL üret.
            - Kullanıcının seviyesi '{{userLevel}}' olduğu için içerik yoğunluğunu buna göre ayarla.

            ÇIKTI KURALI (KESİNLİKLE UYULACAK):
            SADECE aşağıdaki JSON formatını döndür. Markdown, açıklama veya başka metin EKLEME. ` ```json ` bloklarını bile kullanmadan direkt { ile başlayan JSON ver.
            {
              "modules": [
                {
                  "title": "Spesifik Modül Başlığı",
                  "emoji": "🧱",
                  "topics": ["Spesifik Ders 1", "Spesifik Ders 2"]
                }
              ]
            }
            DİL: Türkçe.
            """;

        _logger.LogInformation("[DeepPlan] AIAgentFactory tetikleniyor. Model: {Model}, Seviye: {Level}",
            _factory.GetModel(AgentRole.DeepPlan), userLevel);

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            var raw = await _factory.CompleteChatAsync(AgentRole.DeepPlan, systemPrompt, $"Konu: \"{topicTitle}\"");
            var parsedModules = ParseModuleStructure(raw);
            if (parsedModules != null)
            {
                return parsedModules;
            }
            _logger.LogWarning("[DeepPlan] Parse hatası (Deneme {Attempt}/2). Çıktı: {Raw}", attempt, raw.Length > 200 ? raw[..200] + "..." : raw);
        }

        _logger.LogError("[DeepPlan] JSON parse tüm denemelerde başarısız. Kapsamlı Fallback uygulanıyor.");
        var domainFallback = BuildDomainFallbackModules(topicTitle, massive: false);
        if (domainFallback != null)
        {
            return domainFallback;
        }

        // Gelişmiş Dinamik Fallback (Sıradan olmaktan arındırılmış)
        return new List<ModuleDefinition>
        {
            new($"{topicTitle} Sistem Yapısı ve Metodoloji", "🧱", new List<string> { $"{topicTitle} Çekirdek Mimarisi", $"{topicTitle} Kurulum ve Parametre Yönetimi" }),
            new($"{topicTitle} Süreç İşletimi ve Entegrasyon", "⚙️", new List<string> { $"{topicTitle} Veri İşleme Metotları", $"{topicTitle} Endüstriyel Senaryo Analizi" }),
            new($"{topicTitle} Uzmanlık ve Performans Ayarları", "🚀", new List<string> { $"{topicTitle} Kaynak Optimizasyon Teknikleri", $"{topicTitle} İleri Düzey Problem Çözme" })
        };
    }

    private enum PlanDomain
    {
        General,
        Exam,
        Algorithm,
        Math,
        Language
    }

    private static PlanDomain DetectPlanDomain(string topicTitle)
    {
        var text = (topicTitle ?? string.Empty).ToLowerInvariant();

        if (ContainsAny(text, "hackerrank", "leetcode", "algoritma", "algorithm", "data structure", "veri yap", "competitive", "coding interview", "two pointer", "dynamic programming"))
            return PlanDomain.Algorithm;

        if (ContainsAny(text, "kpss", "yks", "tyt", "ayt", "ales", "dgs", "lgs", "sınav", "sinav", "deneme", "genel yetenek", "genel kultur", "genel kültür"))
            return PlanDomain.Exam;

        if (ContainsAny(text, "matematik", "olasılık", "olasilik", "kombinasyon", "permütasyon", "permutasyon", "türev", "turev", "integral", "geometri", "cebir", "problem"))
            return PlanDomain.Math;

        if (ContainsAny(text, "ielts", "toefl", "yds", "yökdil", "yokdil", "ingilizce", "almanca", "fransızca", "fransizca", "language", "speaking", "konuşma", "konusma", "dil öğren", "dil ogren"))
            return PlanDomain.Language;

        return PlanDomain.General;
    }

    private static string BuildDomainPlanningGuidance(string topicTitle)
    {
        return DetectPlanDomain(topicTitle) switch
        {
            PlanDomain.Exam => """

            [DOMAIN SABLONU - SINAV HAZIRLIK]
            - Plan; konu anlatımı, ölçme, Deneme, Yanlis analizi ve tekrar döngüsünü birlikte taşımalıdır.
            - Her modülde konuya özgü alt kazanım, örnek soru tipi, süre stratejisi ve telafi çalışması bulunmalıdır.
            - KPSS/YKS/benzeri sınavlarda jenerik başlık kullanma; paragraf, problem, tarih kronolojisi, vatandaşlık veya alan kazanımı gibi somut dersler üret.
            """,
            PlanDomain.Algorithm => """

            [DOMAIN SABLONU - ALGORITMA / HACKERRANK]
            - Plan; pattern temelli ilerlemelidir: arrays, two pointers, sliding window, stack/queue, graph, Dynamic Programming ve complexity.
            - Her modülde IDE pratiği, test case okuma, edge-case analizi ve HackerRank tarzı problem çözümü bulunmalıdır.
            - Sadece teori verme; kodlama becerisi, yanlış çözüm teşhisi ve mikro refactor alıştırmaları ekle.
            """,
            PlanDomain.Math => """

            [DOMAIN SABLONU - MATEMATIK]
            - Plan; Formulun sezgisel anlamı, adım adım örnek, Karma problem çözümü ve yanlış türü analiziyle ilerlemelidir.
            - Her modül kavram, işlem, problem dili, görsel/diagram ve Telafi mini quiz dengesini taşımalıdır.
            - Öğrencinin zayıf alt becerisi varsa aynı kazanımı daha fazla örnek ve mikro kontrol sorusuyla pekiştir.
            """,
            PlanDomain.Language => """

            [DOMAIN SABLONU - DIL OGRENIMI]
            - Plan; kelime, gramer, dinleme, telaffuz, writing ve speaking prompt pratiklerini birlikte taşımalıdır.
            - Spaced Repetition tekrarları, Speaking Prompt görevleri ve kısa role-play alıştırmaları ekle.
            - Dil öğreniminde sadece kural anlatma; aktif üretim, hata düzeltme ve seviye uyumlu günlük pratik ver.
            """,
            _ => string.Empty
        };
    }

    private static List<ModuleDefinition>? BuildDomainFallbackModules(string topicTitle, bool massive)
    {
        var title = string.IsNullOrWhiteSpace(topicTitle) ? "Konu" : topicTitle.Trim();
        var modules = DetectPlanDomain(title) switch
        {
            PlanDomain.Exam => new List<ModuleDefinition>
            {
                new($"{title} Kazanım Haritası ve Sınav Stratejisi", "🎯", new List<string> { "Sınav kapsamını alt kazanımlara bölme", "Zaman yönetimi ve Deneme okuma stratejisi", "Yanlis analizi defteri kurma" }),
                new("Paragraf ve Sözel Akıl Yürütme Atölyesi", "📚", new List<string> { "Paragraf ana fikir ve çıkarım soruları", "Çeldirici seçenekleri ayıklama", "Süre baskısında okuma tekniği" }),
                new("Matematik Problem Çözme Rotası", "🧮", new List<string> { "Temel işlem ve oran-orantı problemleri", "Karma problem dili çözümleme", "Hatalı çözümden geri izleme" }),
                new("Deneme, Yanlis Analizi ve Telafi Döngüsü", "🔁", new List<string> { "Deneme sonrası skill bazlı rapor", "Yanlis kümelerine göre tekrar planı", "Mikro quiz ile mastery kontrolü" })
            },
            PlanDomain.Algorithm => new List<ModuleDefinition>
            {
                new("Problem Okuma ve Complexity Temeli", "🧠", new List<string> { "Input-output sözleşmesini çıkarma", "Big-O sezgisi ve sınır analizi", "Edge-case listesi hazırlama" }),
                new("Two Pointers ve Sliding Window Patternleri", "🧭", new List<string> { "Two Pointers karar ağacı", "Sliding Window sabit/değişken pencere", "HackerRank test case simülasyonu" }),
                new("Stack, Queue ve Graph Traversal Pratiği", "🕸️", new List<string> { "Stack ile parantez ve monoton yapı", "BFS/DFS karar noktaları", "IDE içinde debug ve iz sürme" }),
                new("Dynamic Programming ve Optimizasyon", "⚙️", new List<string> { "State tanımı ve transition yazma", "Memoization vs tabulation", "Yanlış DP modelini refactor etme" })
            },
            PlanDomain.Math => new List<ModuleDefinition>
            {
                new($"{title} Kavram Sezgisi", "📐", new List<string> { "Formulun nereden geldiğini görselleştirme", "Temel sembol ve işlem dili", "Kavram yanılgısı kontrol soruları" }),
                new("Adım Adım Problem Çözme", "✍️", new List<string> { "Verilen-istenen ayrıştırma", "Problem tipini sınıflandırma", "Karma örneği küçük parçalara bölme" }),
                new("Uygulamalı Soru Setleri", "🧮", new List<string> { "Kolaydan zora örnek zinciri", "Çeldirici işlem hataları", "Zamanlı mini quiz" }),
                new("Telafi ve Mastery Kontrolü", "🔁", new List<string> { "Yanlış skill için Telafi dersi", "Mikro kontrol sorusu", "Benzer ama tekrar etmeyen problem üretimi" })
            },
            PlanDomain.Language => new List<ModuleDefinition>
            {
                new("Telaffuz ve Temel İfade Kalıpları", "🗣️", new List<string> { "Günlük ifade setleri", "Telaffuz farkındalığı", "Kısa tekrar kartları" }),
                new("Grammar in Context", "📘", new List<string> { "Seviye uyumlu gramer yapıları", "Yanlış cümle düzeltme", "Mini writing görevi" }),
                new("Speaking Prompt ve Role-play", "🎙️", new List<string> { "Speaking Prompt ile cevap kurma", "Hoca-asistan role-play pratiği", "Akıcılık ve kelime seçimi feedback'i" }),
                new("Spaced Repetition ve Aktif Hatırlama", "🔁", new List<string> { "Spaced Repetition kelime döngüsü", "Dinleme sonrası özet çıkarma", "Haftalık mastery konuşması" })
            },
            _ => null
        };

        if (modules != null && massive)
        {
            modules.Add(new($"{title} Kişisel Pekiştirme Laboratuvarı", "🧪", new List<string> { "Zayıf beceriye özel ek çalışma", "Kaynaklı açıklama ve örnek", "Mikro quiz kapanış kontrolü" }));
        }

        return modules;
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        return keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> FetchYouTubeEducationalReferenceAsync(Guid parentTopicId, string topicTitle)
    {
        try
        {
            var youtubePlugin = _serviceProvider.GetService<YouTubeTranscriptPlugin>();
            if (youtubePlugin == null) return string.Empty;

            var redis = _serviceProvider.GetService<IRedisMemoryService>();

            _logger.LogInformation("[DeepPlan] YouTube pedagojik referans aranıyor: {Topic}", topicTitle);

            var searchResult = await youtubePlugin.SearchYouTubeVideos(topicTitle);
            if (searchResult.Contains("bulunamadı", StringComparison.OrdinalIgnoreCase) ||
                searchResult.Contains("hata", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            var match = System.Text.RegularExpressions.Regex.Match(searchResult, @"VideoId:\s*`([^`]+)`");
            if (!match.Success) return string.Empty;

            var videoId = match.Groups[1].Value;
            var transcript = await youtubePlugin.GetVideoTranscript(videoId);
            if (transcript.Contains("bulunamadı", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            if (redis != null)
            {
                var payload = JsonSerializer.Serialize(new {
                    Search = searchResult,
                    BestVideoId = videoId,
                    Transcript = transcript
                });
                await redis.SaveYouTubeContextAsync(parentTopicId, payload);
            }

            return $"\n\n[YOUTUBE EĞİTİM REFERANSI (KOPYALAMA İÇİN DEĞİL, ANLATIM YAPISI İÇİN)]:\n{transcript}\n\nLütfen yukarıdaki popüler eğitim videosunun anlatım sırasını, konuları nasıl böldüğünü ve pedagojik akışını incele. Müfredatı oluştururken bu popüler yapıyı referans al (kopyalama, ilham al).";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DeepPlan] YouTube referans çekme başarısız.");
            return string.Empty;
        }
    }

    /// <summary>LLM çıktısını modül/ders yapısına parse eder. Başarısız olursa null döndürür.</summary>
    private static List<ModuleDefinition>? ParseModuleStructure(string raw)
    {
        try
        {
            var s = raw.IndexOf('{');
            var e = raw.LastIndexOf('}');
            if (s >= 0 && e > s)
            {
                var cleaned = raw[s..(e + 1)];
                using var doc = JsonDocument.Parse(cleaned);
                var modulesArray = doc.RootElement.GetProperty("modules").EnumerateArray();

                var modules = new List<ModuleDefinition>();
                foreach (var mod in modulesArray)
                {
                    var title = mod.GetProperty("title").GetString() ?? "Modül";
                    var emoji = mod.TryGetProperty("emoji", out var emojiProp) ? emojiProp.GetString() ?? "📖" : "📖";
                    var topics = mod.GetProperty("topics").EnumerateArray()
                        .Select(t => t.GetString())
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Select(t => t!)
                        .ToList();

                    if (topics.Count > 0)
                        modules.Add(new ModuleDefinition(title, emoji, topics));
                }

                if (modules.Count >= 2) return modules;
            }
        }
        catch { /* yoksay, null dönecek */ }

        return null;
    }

    /// <summary>3 seviyeli Topic hiyerarşisi: Ana Konu → Modül → Ders. Her ders için WikiPage oluşturur.</summary>
    private async Task<List<Topic>> SaveModularSubTopicsAsync(
        Guid parentTopicId, List<ModuleDefinition> modules, Guid userId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var parent = await db.Topics.FindAsync(parentTopicId);
        if (parent == null) return new List<Topic>();

        var allLessonTopics = new List<Topic>();
        var allWikiPages = new List<WikiPage>();
        int totalLessons = 0;

        for (int mi = 0; mi < modules.Count; mi++)
        {
            var mod = modules[mi];

            // Modül topic'i (2. seviye)
            var moduleTopic = new Topic
            {
                Id             = Guid.NewGuid(),
                UserId         = userId,
                ParentTopicId  = parentTopicId,
                Title          = mod.Title,
                Emoji          = mod.Emoji,
                Category       = "Plan",
                CurrentPhase   = TopicPhase.Planning,
                Order          = mi,
                CreatedAt      = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow,
                TotalSections  = mod.Topics.Count
            };
            db.Topics.Add(moduleTopic);

            // Ders topic'leri (3. seviye)
            for (int li = 0; li < mod.Topics.Count; li++)
            {
                var lessonTopic = new Topic
                {
                    Id             = Guid.NewGuid(),
                    UserId         = userId,
                    ParentTopicId  = moduleTopic.Id,
                    Title          = mod.Topics[li],
                    Emoji          = mod.Emoji,
                    Category       = "Plan",
                    CurrentPhase   = TopicPhase.Discovery,
                    Order          = li,
                    CreatedAt      = DateTime.UtcNow,
                    LastAccessedAt = DateTime.UtcNow,
                    TotalSections  = 1
                };
                db.Topics.Add(lessonTopic);
                allLessonTopics.Add(lessonTopic);
                totalLessons++;

                // Her ders için WikiPage
                allWikiPages.Add(new WikiPage
                {
                    Id         = Guid.NewGuid(),
                    TopicId    = lessonTopic.Id,
                    UserId     = userId,
                    Title      = lessonTopic.Title,
                    OrderIndex = totalLessons,
                    Status     = "pending",
                    CreatedAt  = DateTime.UtcNow,
                    UpdatedAt  = DateTime.UtcNow
                });
            }
        }

        db.WikiPages.AddRange(allWikiPages);

        parent.TotalSections = totalLessons;
        parent.CurrentPhase  = TopicPhase.Planning;

        await db.SaveChangesAsync();

        _logger.LogInformation("[DeepPlan] {ModuleCount} modül, {LessonCount} ders oluşturuldu.",
            modules.Count, totalLessons);
        return allLessonTopics;
    }

    /// <summary>Modül tanımı: başlık, emoji ve altındaki dersler.</summary>
    private record ModuleDefinition(string Title, string Emoji, List<string> Topics);

    public async Task<string> GenerateBaselineQuizAsync(string topicTitle)
    {
        var systemPrompt = $$"""
            Sen bir 'Eğitim Tanılama Uzmanı (Educational Diagnostician)' botusun.
            Görevin: Kullanıcının '{{topicTitle}}' konusundaki bilgi seviyesini EN İNCE AYRINTISINA KADAR tespit etmek için 20 soru hazırlamak.

            SORU DAĞILIMI VE DERİNLİK (Toplam 20 Soru):
            - 1-4: TEMEL KAVRAMLAR (Başlangıç seviyesi, terminoloji kontrolü)
            - 5-10: UYGULAMA VE SENARYO (Orta seviye, "nasıl yapılır?" ve kod okuma)
            - 11-16: ANALİZ VE PROBLEM ÇÖZME (İleri seviye, hata ayıklama ve mimari kararlar)
            - 17-20: UZMANLIK VE DERİN KONULAR (Zorlayıcı, uç durumlar ve optimizasyon)

            KALİTE VE FORMAT KURALLARI:
            - Sadece "X nedir?" gibi ezber soruları YASAKTIR. Sorular gerçek hayat problemlerini veya teknik senaryoları yansıtmalıdır.
            - Yanlış seçenekler (çeldiriciler) mantıklı ve kafa karıştırıcı olmalı.
            - Çıktı SADECE saf JSON dizisi olmalıdır.
            - "type" alanı çok önemlidir. Eğer soru sözel ve kavramsal bir bilgiyse "multiple_choice" (4 şık ekle), ALGORİTMA VEYA KODLAMA gerektiriyorsa "coding" (şıkları boş bırak) yap.

            [
              {
                "type": "multiple_choice", // veya "coding"
                "question": "Soru metni...",
                "options": [
                  { "text": "...", "isCorrect": false },
                  { "text": "...", "isCorrect": true },
                  { "text": "...", "isCorrect": false },
                  { "text": "...", "isCorrect": false }
                ],
                "explanation": "Detaylı açıklama",
                "topic": "{{topicTitle}}"
              },
              ... (TOPLAM 20 SORU)
            ]

            DİL: Türkçe.
            """;

        return await _factory.CompleteChatAsync(AgentRole.DeepPlan, systemPrompt, $"Konu: \"{topicTitle}\"");
    }
}
