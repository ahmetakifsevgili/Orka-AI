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
using System.Globalization;
using Orka.Core.DTOs.Korteks;
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
    private const int MinimumGeneralModules = 6;
    private const int MinimumGeneralLessonsPerModule = 4;
    private const int MinimumGeneralTotalLessons = 24;
    private const int MinimumProgrammingModules = 6;
    private const int MinimumProgrammingLessonsPerModule = 4;
    private const int MinimumProgrammingTotalLessons = 24;

    private readonly IAIAgentFactory _factory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISupervisorAgent _supervisor;
    private readonly IGraderAgent _grader;
    private readonly IKorteksAgent _korteks;
    private readonly IPlanResearchCompressor _planResearchCompressor;
    private readonly IAdaptiveLearningContextBuilder _adaptiveBuilder;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DeepPlanAgent> _logger;

    public DeepPlanAgent(
        IAIAgentFactory factory,
        IServiceScopeFactory scopeFactory,
        ISupervisorAgent supervisor,
        IGraderAgent grader,
        IKorteksAgent korteks,
        IPlanResearchCompressor planResearchCompressor,
        IAdaptiveLearningContextBuilder adaptiveBuilder,
        IServiceProvider serviceProvider,
        ILogger<DeepPlanAgent> logger)
    {
        _factory          = factory;
        _scopeFactory     = scopeFactory;
        _supervisor       = supervisor;
        _grader           = grader;
        _korteks          = korteks;
        _planResearchCompressor = planResearchCompressor;
        _adaptiveBuilder  = adaptiveBuilder;
        _serviceProvider  = serviceProvider;
        _logger           = logger;
    }

    public async Task<List<Topic>> GenerateAndSaveDeepPlanAsync(
        Guid parentTopicId, string topicTitle, Guid userId, string userLevel = "Bilinmiyor", string? researchContext = null, string? failedTopics = null)
    {
        var result = await GenerateAndSaveDeepPlanWithGroundingAsync(parentTopicId, topicTitle, userId, userLevel, researchContext, failedTopics);
        return result.Topics;
    }

    public async Task<DeepPlanGenerationWithGroundingResultDto> GenerateAndSaveDeepPlanWithGroundingAsync(
        Guid parentTopicId, string topicTitle, Guid userId, string userLevel = "Bilinmiyor", string? researchContext = null, string? failedTopics = null)
    {
        DeepPlanGroundingMetadataDto? grounding = null;
        var modules = await GenerateModulesAsync(parentTopicId, topicTitle, userId, userLevel, researchContext, failedTopics, g => grounding = g);
        var topics = await SaveModularSubTopicsAsync(parentTopicId, modules, userId);
        return new DeepPlanGenerationWithGroundingResultDto { Topics = topics, Grounding = grounding };
    }

    public async Task<DeepPlanGenerationWithGroundingResultDto> GenerateAndSaveDeepPlanFromDiagnosticAsync(
        Guid parentTopicId,
        string topicTitle,
        Guid userId,
        string compressedResearchPromptBlock,
        string diagnosticQuizSummary,
        string userLevel = "Bilinmiyor")
    {
        DeepPlanGroundingMetadataDto? grounding = null;
        var modules = await GenerateModulesAsync(
            parentTopicId,
            topicTitle,
            userId,
            userLevel,
            researchContext: null,
            failedTopics: null,
            setGrounding: g => grounding = g,
            precompressedResearchPromptBlock: compressedResearchPromptBlock,
            diagnosticQuizSummary: diagnosticQuizSummary);
        var diagnostic = DiagnosticWeaknessSummary.Parse(diagnosticQuizSummary);
        modules = EnsureBasePlanQualityBeforeSave(modules, topicTitle, diagnostic);
        modules = ApplyDiagnosticTraceability(modules, diagnostic);
        var topics = await SaveModularSubTopicsAsync(parentTopicId, modules, userId);
        return new DeepPlanGenerationWithGroundingResultDto { Topics = topics, Grounding = grounding };
    }

    private async Task<List<ModuleDefinition>> GenerateModulesAsync(Guid parentTopicId, string topicTitle, Guid userId, string userLevel, string? researchContext = null, string? failedTopics = null, Action<DeepPlanGroundingMetadataDto?>? setGrounding = null, string? precompressedResearchPromptBlock = null, string? diagnosticQuizSummary = null)
    {
        _logger.LogInformation("[DeepPlan] Multi-Agent RAG döngüsü başlıyor. Konu: {Topic}", topicTitle);

        // 0. Sprint 1: Otonom Keşif Fazı (Korteks Entegrasyonu)
        // Eğer dışarıdan hazır bir araştırma raporu gelmemişse, Korteks'i sahaya sür.
        DeepPlanGroundingMetadataDto? groundingMetadata = null;
        CompressedPlanResearchContextDto? compressedResearchContext = null;
        string compressedResearchPromptBlock = precompressedResearchPromptBlock ?? string.Empty;

        if (string.IsNullOrWhiteSpace(researchContext) && string.IsNullOrWhiteSpace(compressedResearchPromptBlock))
        {
            try
            {
                _logger.LogInformation("[DeepPlan] Mevcut araştırma verisi bulunamadı. Korteks derin keşif motoru tetikleniyor...");
                var korteksResult = await _korteks.RunResearchWithEvidenceAsync(topicTitle, userId, parentTopicId);
                groundingMetadata = ToDeepPlanGrounding(korteksResult);
                compressedResearchContext = _planResearchCompressor.Compress(korteksResult);
                compressedResearchPromptBlock = _planResearchCompressor.BuildPromptBlock(compressedResearchContext);
                _logger.LogInformation(
                    "[DeepPlan] Korteks keşfi sıkıştırıldı. Mode={GroundingMode} Sources={SourceCount} BlockLen={Len}",
                    compressedResearchContext.GroundingMode,
                    compressedResearchContext.SourceCount,
                    compressedResearchPromptBlock.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DeepPlan] Korteks araştırması başarısız oldu. Planlama mevcut bilgilerle devam edecek.");
                groundingMetadata = new DeepPlanGroundingMetadataDto
                {
                    GroundingMode = GroundingMode.BlockedProvider,
                    SourceCount = 0,
                    IsFallback = true,
                    ProviderWarnings = [ex.Message]
                };
                var blockedResearch = new KorteksResearchResultDto
                {
                    Topic = topicTitle,
                    TopicId = parentTopicId,
                    GroundingMode = GroundingMode.BlockedProvider,
                    ProviderFailures = [ex.Message],
                    IsFallback = true,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                compressedResearchContext = _planResearchCompressor.Compress(blockedResearch);
                compressedResearchPromptBlock = _planResearchCompressor.BuildPromptBlock(compressedResearchContext);
            }
        }

        // 1. Durum Sınıflandırması (Supervisor Node)
        if (!string.IsNullOrWhiteSpace(researchContext) && groundingMetadata == null)
        {
            var proseOnlyResearch = new KorteksResearchResultDto
            {
                Topic = topicTitle,
                TopicId = parentTopicId,
                Report = researchContext,
                GroundingMode = GroundingMode.FallbackInternalKnowledge,
                Warnings = ["Research context was provided as prose without structured source evidence."],
                IsFallback = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            groundingMetadata = ToDeepPlanGrounding(proseOnlyResearch);
            compressedResearchContext = _planResearchCompressor.Compress(proseOnlyResearch);
            compressedResearchPromptBlock = _planResearchCompressor.BuildPromptBlock(compressedResearchContext);
            researchContext = null;
        }

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
                contextInfo = string.Empty;
            }
            else
            {
                _logger.LogWarning("[DeepPlan] Grader REDDETTİ. Hallucination veya alakasız context engellendi. (Sıfır bilgi ile devam ediliyor)");
            }
        }

        var baselineDiagnostic = !string.IsNullOrWhiteSpace(diagnosticQuizSummary)
            ? diagnosticQuizSummary
            : await AnalyzeBaselineQuizResultsAsync(parentTopicId, userId);
        if (!string.IsNullOrWhiteSpace(baselineDiagnostic))
        {
            _logger.LogInformation("[DeepPlan] Baseline mikro-teşhis raporu hazırlandı.");
        }

        // ── Sprint 2: Mikro-Teşhis + Korteks plan intelligence süzgeci ─────
        if (!string.IsNullOrWhiteSpace(compressedResearchPromptBlock))
        {
            var intelligenceBrief = PlanIntelligenceBriefBuilder.BuildForPlan(
                topicTitle,
                compressedResearchPromptBlock,
                baselineDiagnostic);
            contextInfo = $"\n\n{intelligenceBrief}\n\nBu filtrelenmiş Korteks brief'ini yalnızca konu kapsamı, güncellik, önkoşul ve kaynak farkındalığı desteği olarak kullan; plan omurgasını domain şablonu, mikro-teşhis ve adaptif bağlam belirler.";
        }

        // Faz 17: Yapılandırılmış Adaptif Bağlam (Personalization v1)
        var adaptiveContext = await _adaptiveBuilder.BuildAsync(userId, parentTopicId, topicTitle, userLevel);
        var adaptivePromptSection = BuildAdaptivePromptSection(adaptiveContext);

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

            [MİKRO-TEŞHİS RAPORU - ÖĞRENCİNİN ZAYIFLIKLARI]:
            {{baselineDiagnostic}}

            {{adaptivePromptSection}}

            {{failedTopicsDiagnostic}}
            {{youtubeReference}}
            {{domainGuidance}}

            ORGANİZASYON KURALI (TEŞHİS ODAKLI MİMARİ):
            - [PLAN INTELLIGENCE BRIEF - LEARNING RESEARCH FILTERED] icindeki arastirma bulgularini yalnizca konu kapsami/guncellik/onkosul destegi olarak kullan; plan omurgasini [MIKRO-TESHIS RAPORU], [ADAPTIF OGRENME BAGLAMI] ve domain sablonu belirler.
            - Korteks kaynak basliklarini, haber/SEO cumlelerini veya video basliklarini modul/ders basligi olarak kopyalama.
            - [MİKRO-TEŞHİS RAPORU] ve [ADAPTIF ÖĞRENME BAĞLAMI] içindeki zayıf noktaları plana "Derinlemesine İyileştirme" veya "Pratik Lab" dersleri olarak ekle.
            - Tekrar eden hata paternlerine (Mistake Patterns) yönelik ekstra pekiştirme modülleri öner.
            - SRS / Gözden Geçirme baskısı olan becerileri müfredatın başına veya ilgili modüllere 'Hızlı Tekrar' olarak serpiştir.
            - Öğrencinin zaten bildiği (başarılı olduğu) kısımları müfredatta 'Hızlı Özet' veya 'Hatırlatma' olarak daralt.
            - Kullanıcı cümlesinden (Örn: "C# çalışmak istiyorum") ASIL KONUYU çıkar ("C# Programlama"). Kesinlikle "Selamlaşma", "İstek", "Giriş" gibi konulardan bahsetme!
            - KALITE TABANI: En az 6 modul, her modulde en az 4 ders ve toplam en az 24 ders uret.
            - Programlama/teknoloji konularinda kalite tabani daha yuksektir: en az 6 modul, her modulde en az 4 ders ve toplam en az 24 ders uret.
            - Programlama planinda ilk modul konu mantigi ve on kosullarla baslamali; Orka IDE/sandbox yalnizca uygun pratik derslerinde destekleyici ortam olarak gecmelidir.
            - Plan cok kisa, jenerik veya iki-uc baslikli olursa sistem tarafindan reddedilecektir.
            - Programlama/teknoloji → "Temel Yapı Taşları → Uygulama Becerileri → İleri Düzey" yaklaşımı
            - Tarih/toplum → "Dönemsel sıralama" veya "Tematik gruplama"
            - Bilim/matematik → "Teorik temeller → Uygulamalı konular → İleri araştırma"
            - Her ders için o derste ölçülecek ana beceriyi `skillTag` olarak belirle.
            - Her ders için bir `intent` belirle:
              * "Core": Standart müfredat akışı.
              * "DeepDive": [ADAPTIF ÖĞRENME BAĞLAMI] içindeki bir 'WeakConcept' (Zayıf Kavram) için derinlemesine teorik anlatım.
              * "PracticeLab": Procedural/İşlemsel hata paternlerine yönelik adım adım uygulama veya kodlama laboratuvarı.
              * "QuickReview": SRS baskısı olan veya daha önce başarısız olunan konuların hızlı tekrarı.
              * "Remediation": Kavram yanılgılarını (Conceptual) düzeltmek için özel olarak tasarlanmış telafi dersi.
              * "Assessment": Modül sonu veya kritik kavram sonrası ölçme-değerlendirme (küçük bir sınav gibi).

            ÇIKTI KURALI (KESİNLİKLE UYULACAK):
            SADECE aşağıdaki JSON formatını döndür. Markdown veya açıklama EKLEME.
            {
              "modules": [
                {
                  "title": "Modül Başlığı",
                  "emoji": "🧱",
                  "lessons": [
                    { "title": "Ders Başlığı 1", "skillTag": "beceri-etiketi-1", "intent": "Core" },
                    { "title": "Ders Başlığı 2", "skillTag": "beceri-etiketi-2", "intent": "DeepDive" },
                    { "title": "Ders Başlığı 3", "skillTag": "beceri-etiketi-3", "intent": "PracticeLab" }
                  ]
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
            if (TryAcceptPlanModules(parsedModules, topicTitle, out var acceptedModules, out var rejectionReason))
            {
                setGrounding?.Invoke(groundingMetadata);
                return acceptedModules;
            }
            _logger.LogWarning(
                "[DeepPlan] Plan kalite/parse reddi (Deneme {Attempt}/2). Reason={Reason}. Raw={Raw}",
                attempt,
                rejectionReason,
                raw.Length > 200 ? raw[..200] + "..." : raw);
        }

        _logger.LogError("[DeepPlan] Plan kalite kapisi tum denemelerde basarisiz. Kapsamli fallback uygulanıyor.");
        setGrounding?.Invoke(groundingMetadata);
        return BuildQualityFallbackModules(topicTitle);
    }

    private enum PlanDomain
    {
        General,
        Programming,
        Exam,
        Algorithm,
        Math,
        Language,
        History
    }

    private static PlanDomain DetectPlanDomain(string topicTitle)
    {
        var text = (topicTitle ?? string.Empty).ToLowerInvariant();

        if (ContainsAny(text, "hackerrank", "leetcode", "algoritma", "algorithm", "data structure", "veri yap", "competitive", "coding interview", "two pointer", "dynamic programming"))
            return PlanDomain.Algorithm;

        if (IsProgrammingTopic(text))
            return PlanDomain.Programming;

        if (ContainsAny(text, "kpss", "yks", "tyt", "ayt", "ales", "dgs", "lgs", "sınav", "sinav", "deneme", "genel yetenek", "genel kultur", "genel kültür"))
            return PlanDomain.Exam;

        if (ContainsAny(text, "matematik", "olasılık", "olasilik", "kombinasyon", "permütasyon", "permutasyon", "türev", "turev", "integral", "geometri", "cebir", "problem"))
            return PlanDomain.Math;

        if (ContainsAny(text, "ielts", "toefl", "yds", "yökdil", "yokdil", "ingilizce", "almanca", "fransızca", "fransizca", "language", "speaking", "konuşma", "konusma", "dil öğren", "dil ogren"))
            return PlanDomain.Language;

        if (ContainsAny(text, "tarih", "history", "selcuk", "selcuklu", "seljuk", "osmanli", "ottoman", "roma", "medieval"))
            return PlanDomain.History;

        return PlanDomain.General;
    }

    private static string BuildDomainPlanningGuidance(string topicTitle)
    {
        return DetectPlanDomain(topicTitle) switch
        {
            PlanDomain.Programming => """

            [DOMAIN SABLONU - PROGRAMLAMA / YAZILIM]
            - Plan once dil/kavram ve problem okuma mantigini kurar; Orka IDE/sandbox uygun pratiklerde destekleyici calisma ortami olarak kullanilir.
            - Plan; temel dil modeli, kod okuma, uygulama, hata ayiklama, mini proje ve tekrar dongusunu birlikte tasimalidir.
            - Her modulde pratik, hata okuma veya mini refactor dersi bulunmalidir; urun arayuzu modul basligini ele gecirmemelidir.
            - Konu C#, .NET veya yazilim ise baslangic "Hello World" ile kalmamalidir; tipler, kontrol akisi, metotlar, OOP, koleksiyonlar ve proje akisi kurulmalidir.
            """,
            PlanDomain.Exam => """

            [DOMAIN SABLONU - SINAV HAZIRLIK]
            - Plan; konu anlatımı, ölçme, Deneme, Yanlis analizi ve tekrar döngüsünü birlikte taşımalıdır.
            - Her modülde konuya özgü alt kazanım, örnek soru tipi, süre stratejisi ve telafi çalışması bulunmalıdır.
            - KPSS/YKS/benzeri sınavlarda jenerik başlık kullanma; paragraf, problem, tarih kronolojisi, vatandaşlık veya alan kazanımı gibi somut dersler üret.
            """,
            PlanDomain.Algorithm => """

            [DOMAIN SABLONU - ALGORITMA / HACKERRANK]
            - Plan; pattern temelli ilerlemelidir: arrays, two pointers, sliding window, stack/queue, graph, Dynamic Programming ve complexity.
            - Her modulde test case okuma, edge-case analizi ve problem cozumu bulunmalidir; Orka IDE yalnizca kod pratiklerinde destekleyici ortamdir.
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
            PlanDomain.History => """

            [DOMAIN SABLONU - TARIH / SOSYAL BILIMLER]
            - Plan kronoloji, cografya, aktorler, olaylar, kurumlar, neden-sonuc ve miras eksenlerini birlikte tasimalidir.
            - Savas listesi veya isim ezberiyle kalma; actor-event eslestirme, zaman sirasi ve kisa neden-sonuc yazimi ekle.
            - Tarih planinda programlama/debugging/IDE dili kullanma; kaynak rotasi ve tarihsel kavramlar omurga olmalidir.
            """,
            _ => string.Empty
        };
    }

    private static List<ModuleDefinition>? BuildDomainFallbackModules(string topicTitle, bool massive)
    {
        var title = string.IsNullOrWhiteSpace(topicTitle) ? "Konu" : topicTitle.Trim();
        var modules = DetectPlanDomain(title) switch
        {
            PlanDomain.Programming => BuildProgrammingFallbackModules(title),
            PlanDomain.Exam => new List<ModuleDefinition>
            {
                new($"{title} Kazanım Haritası", "🎯", new List<LessonDefinition> { new("Sınav kapsamı", "strategy"), new("Zaman yönetimi", "timing") }),
                new("Paragraf Atölyesi", "📚", new List<LessonDefinition> { new("Ana fikir", "reading"), new("Çeldiriciler", "logic") }),
                new("Matematik Rotası", "🧮", new List<LessonDefinition> { new("Problem çözme", "math"), new("Hata izleme", "analysis") }),
                new("Deneme Döngüsü", "🔁", new List<LessonDefinition> { new("Skill raporu", "mastery"), new("Tekrar planı", "remediation") })
            },
            PlanDomain.Algorithm => BuildAlgorithmFallbackModules(title),
            PlanDomain.Math => new List<ModuleDefinition>
            {
                new($"{title} Formulun Kavram Sezgisi", "📐", new List<LessonDefinition> { new("Formulun sezgisel anlamı", "concept"), new("İşlem dili", "notation") }),
                new("Adım Adım Problem Çözümü", "✍️", new List<LessonDefinition> { new("Problem dili", "logic"), new("Tip sınıflandırma", "pattern") }),
                new("Uygulama Setleri", "🧮", new List<LessonDefinition> { new("Örnek zinciri", "practice"), new("Zamanlı mini quiz", "assessment") }),
                new("Telafi ve Mastery Kontrolü", "🔁", new List<LessonDefinition> { new("Hatalı skill telafisi", "remediation"), new("Tekrar etmeyen örnekler", "mastery") })
            },
            PlanDomain.Language => new List<ModuleDefinition>
            {
                new("Temel İfadeler ve Telaffuz", "🗣️", new List<LessonDefinition> { new("Günlük kalıplar", "vocabulary"), new("Telaffuz farkındalığı", "pronunciation") }),
                new("Grammar in Context", "📘", new List<LessonDefinition> { new("Bağlamsal yapılar", "grammar"), new("Hata düzeltme", "writing") }),
                new("Speaking & Role-play", "🎙️", new List<LessonDefinition> { new("Diyalog kurma", "speaking"), new("Feedback analizi", "fluency") }),
                new("Spaced Repetition Active Recall", "🔁", new List<LessonDefinition> { new("Spaced Repetition", "memory"), new("Haftalık kapanış", "review") })
            },
            PlanDomain.History => BuildHistoryFallbackModules(title),
            _ => null
        };

        if (modules != null && massive)
        {
            modules.Add(new($"{title} Pekiştirme Laboratuvarı", "🧪", new List<LessonDefinition> { new("Zayıf beceri çalışması", "remediation"), new("Kapanış kontrolü", "assessment") }));
        }

        return modules;
    }

    private static List<ModuleDefinition> BuildQualityFallbackModules(string topicTitle)
    {
        var title = string.IsNullOrWhiteSpace(topicTitle) ? "Konu" : topicTitle.Trim();
        var domain = DetectPlanDomain(title);

        if (domain == PlanDomain.Programming)
        {
            return BuildProgrammingFallbackModules(title);
        }

        var domainFallback = BuildDomainFallbackModules(title, massive: true);
        var expandedDomainFallback = EnsureFallbackQualityFloor(domainFallback, title);
        if (TryAcceptPlanModules(expandedDomainFallback, title, out var accepted, out _))
        {
            return accepted;
        }

        return BuildGeneralFallbackModules(title);
    }

    private static List<ModuleDefinition>? EnsureFallbackQualityFloor(List<ModuleDefinition>? modules, string title)
    {
        if (modules == null || modules.Count == 0)
        {
            return modules;
        }

        var result = modules
            .Where(module => !string.IsNullOrWhiteSpace(module.Title))
            .Select(module => module with
            {
                Lessons = module.Lessons
                    .Where(lesson => !string.IsNullOrWhiteSpace(lesson.Title))
                    .ToList()
            })
            .Where(module => module.Lessons.Count > 0)
            .ToList();

        foreach (var module in result.ToList())
        {
            var lessonIndex = module.Lessons.Count + 1;
            while (module.Lessons.Count < 4)
            {
                module.Lessons.Add(new LessonDefinition(
                    $"{module.Title} uygulama kontrolu {lessonIndex}",
                    $"{Slug(module.Title)}-practice-{lessonIndex}",
                    lessonIndex % 2 == 0 ? "PracticeLab" : "Core"));
                lessonIndex++;
            }
        }

        var moduleIndex = result.Count + 1;
        while (result.Count < 6)
        {
            result.Add(new ModuleDefinition(
                $"{title} Pekistirme Turu {moduleIndex}",
                "🧩",
                [
                    new LessonDefinition("Kavram ozeti ve on kosul kontrolu", $"fallback-{moduleIndex}-concept"),
                    new LessonDefinition("Uygulamali ornek ve adim adim cozum", $"fallback-{moduleIndex}-practice", "PracticeLab"),
                    new LessonDefinition("Sik hata ve yanilgi onarimi", $"fallback-{moduleIndex}-remediation", "Remediation"),
                    new LessonDefinition("Mini quiz ve sonraki adim", $"fallback-{moduleIndex}-assessment", "Assessment")
                ]));
            moduleIndex++;
        }

        return result;
    }

    private static void EnsureOrkaIdeLesson(List<ModuleDefinition> modules, string title)
    {
        var allText = string.Join(" | ", modules.Select(m => m.Title).Concat(modules.SelectMany(m => m.Lessons.Select(l => l.Title))));
        if (allText.Contains("Orka IDE", StringComparison.OrdinalIgnoreCase) ||
            allText.Contains("sandbox", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        modules.Insert(0, new ModuleDefinition(
            "Orka IDE ile Problem Cozme",
            "💻",
            [
                new LessonDefinition($"Orka IDE sandbox'ta {title} icin ilk mikro pratik", "orka-ide-first-practice", "PracticeLab"),
                new LessonDefinition("Test case okuma ve beklenen ciktiyi kurma", "orka-ide-testcase-reading", "PracticeLab"),
                new LessonDefinition("Hata mesajindan kok neden cikarma", "orka-ide-debug-root-cause", "Remediation"),
                new LessonDefinition("Tutor'a IDE sonucunu gonderip aciklama alma", "orka-ide-tutor-bridge", "PracticeLab")
            ]));
    }

    private static string Slug(string value)
    {
        var normalized = value.ToLowerInvariant()
            .Replace('ç', 'c')
            .Replace('ğ', 'g')
            .Replace('ı', 'i')
            .Replace('ö', 'o')
            .Replace('ş', 's')
            .Replace('ü', 'u');
        return System.Text.RegularExpressions.Regex.Replace(normalized, @"[^a-z0-9]+", "-").Trim('-');
    }

    private sealed record ProgrammingPlanProfile(
        string DisplayName,
        string SkillPrefix,
        string ProgramShape,
        string RuntimeConcept,
        string DataConcept,
        string AbstractionConcept,
        string ProjectShape);

    private static List<ModuleDefinition> BuildAlgorithmFallbackModules(string title) =>
    [
        new("Problem Okuma ve Java Kod Izleme", "book",
        [
            new("Input-output ve kisitlari okuma", "algo-problem-reading"),
            new("Kucuk Java kodunda veri akisini izleme", "java-code-trace", "PracticeLab"),
            new("Edge-case ve sinir deger listesi cikarma", "algo-edge-cases"),
            new("Yanlis varsayimi test case ile yakalama", "algo-testcase-misconception", "Remediation")
        ]),
        new("Diziler, Listeler ve Arama Mantigi", "array",
        [
            new("Array/List indeks modeli ve maliyetleri", "array-list-model"),
            new("Linear search ve binary search on kosullari", "search-preconditions"),
            new("Siralama sonrasi veri okuma", "sorting-read"),
            new("Arama/siralama mini pratigi", "search-sort-practice", "PracticeLab")
        ]),
        new("Karmasiklik ve Veri Yapisi Secimi", "chart",
        [
            new("Big-O'yu kod akisi uzerinden okuma", "big-o-code-reading"),
            new("HashMap, HashSet, Stack, Queue secimi", "data-structure-choice"),
            new("PriorityQueue ve Comparator karar noktasi", "priority-comparator"),
            new("Yanlis veri yapisi secimini onarma", "wrong-structure-remediation", "Remediation")
        ]),
        new("Two Pointers ve Algoritma Patternleri", "pattern",
        [
            new("Two Pointers on kosullari", "two-pointer"),
            new("Sliding window ile tekrar eden araliklar", "sliding-window", "PracticeLab"),
            new("Prefix sum ile tekrarli sorgular", "prefix-sum"),
            new("Greedy kararinin kanit ihtiyaci", "greedy-proof", "DeepDive")
        ]),
        new("Recursion, Graph ve Dynamic Programming", "graph",
        [
            new("Recursion base case ve call stack", "recursion-base-case", "Remediation"),
            new("BFS/DFS icin queue-stack farki", "bfs-dfs"),
            new("DP icin state ve tekrar eden alt problem", "dynamic-programming"),
            new("Kucuk problemden tablo/graph izine gecis", "trace-to-model", "PracticeLab")
        ]),
        new($"{title} Karma Pratik ve Mastery Kontrolu", "target",
        [
            new("Karar agaci: hangi problemde hangi pattern", "pattern-selection"),
            new("Zamanli mini problem seti", "timed-drills", "PracticeLab"),
            new("Yanlis cozumden kok neden cikarma", "mistake-analysis", "Remediation"),
            new("Final mastery quiz ve sonraki rota", "mastery-check", "Assessment")
        ])
    ];

    private static List<ModuleDefinition> BuildHistoryFallbackModules(string title)
    {
        var text = title.ToLowerInvariant();
        var isSeljuk = ContainsAny(text, "selcuk", "selcuklu", "seljuk");
        if (isSeljuk)
        {
            return
            [
                new("Koken ve Ilk Yukselis", "history",
                [
                    new("Oghuz/Kinik arka plani", "seljuk-origins"),
                    new("Khorasan ve Gaznelilerle mucadele", "seljuk-khorasan"),
                    new("Tughril ve Chaghri Beg rolleri", "seljuk-founders"),
                    new("Ilk harita ve kronoloji kontrolu", "seljuk-map-timeline", "Assessment")
                ]),
                new("Devletlesme ve Mesruiyet", "state",
                [
                    new("Dandanakan'in neden ve sonucu", "seljuk-dandanakan"),
                    new("Baghdad ve Abbasi mesruiyeti", "seljuk-baghdad"),
                    new("Sultanlik fikri ve siyasi guc", "seljuk-legitimacy"),
                    new("Actor-event eslestirme pratigi", "seljuk-actor-event", "PracticeLab")
                ]),
                new("Alp Arslan ve Malazgirt", "map",
                [
                    new("Bizans-Seljuk iliski zemini", "seljuk-byzantine"),
                    new("Malazgirt sebepleri", "seljuk-manzikert-cause"),
                    new("Malazgirt sonuclari", "seljuk-manzikert-effect"),
                    new("Anadolu baglantisi neden-sonuc yazimi", "seljuk-anatolia-link", "PracticeLab")
                ]),
                new("Meliksah ve Nizam al-Mulk Donemi", "institution",
                [
                    new("Yuksek donem ve siyasi genisleme", "seljuk-malikshah"),
                    new("Nizam al-Mulk ve vezirlik", "seljuk-nizam"),
                    new("Iqta ve idari duzen", "seljuk-iqta"),
                    new("Nizamiye medreseleri", "seljuk-nizamiya")
                ]),
                new("Kultur, Kurumlar ve Toplum", "culture",
                [
                    new("Ordu ve idari yapi", "seljuk-army-admin"),
                    new("Medrese ve ilim hayati", "seljuk-education"),
                    new("Sanat, mimari ve burokrasi", "seljuk-culture"),
                    new("Kurumlari siyasi tarihe baglama", "seljuk-institution-synthesis", "DeepDive")
                ]),
                new("Dagilma ve Miras", "review",
                [
                    new("Sencer donemi ve Katvan", "seljuk-sanjar-qatwan"),
                    new("Parcalanma nedenleri", "seljuk-fragmentation"),
                    new("Anadolu Selcuklu mirasi", "seljuk-anatolian-legacy"),
                    new("Karma kronoloji ve final seviye kontrolu", "seljuk-final-check", "Assessment")
                ])
            ];
        }

        return
        [
            new($"{title} Donem ve Kronoloji", "history",
            [
                new("Baslangic baglami", "history-context"),
                new("Zaman sirasi", "history-chronology"),
                new("Harita ve cografya baglami", "history-geography"),
                new("Ana kavram kontrolu", "history-baseline", "Assessment")
            ]),
            new("Aktorler ve Olaylar", "people",
            [
                new("Liderler ve gruplar", "history-actors"),
                new("Ana olaylar", "history-events"),
                new("Donum noktalari", "history-turning-points"),
                new("Actor-event pratigi", "history-actor-event", "PracticeLab")
            ]),
            new("Neden-Sonuc ve Kurumlar", "cause",
            [
                new("Sebep-sonuc zinciri", "history-cause-effect"),
                new("Idari kurumlar", "history-institutions"),
                new("Toplum ve ekonomi", "history-society"),
                new("Kisa neden-sonuc yazimi", "history-short-writing", "PracticeLab")
            ]),
            new("Kultur ve Miras", "culture",
            [
                new("Kultur ve sanat", "history-culture"),
                new("Uzun vadeli etkiler", "history-legacy"),
                new("Kaynak karsilastirma", "history-source-compare"),
                new("Yanilgi ayirma", "history-misconception", "Remediation")
            ]),
            new("Yanilgi Onarimi", "repair",
            [
                new("Karisan donemler", "history-period-confusion", "Remediation"),
                new("Karisan aktorler", "history-actor-confusion", "Remediation"),
                new("Kronoloji mini quiz", "history-timeline-quiz", "Assessment"),
                new("Harita-kavram baglantisi", "history-map-concept", "PracticeLab")
            ]),
            new("Karma Tarih Pratigi", "target",
            [
                new("Kronoloji testi", "history-chronology-test", "Assessment"),
                new("Neden-sonuc yazimi", "history-cause-writing", "PracticeLab"),
                new("Karma aktor-olay eslestirme", "history-mixed-match", "PracticeLab"),
                new("Final kontrol ve sonraki rota", "history-final-check", "Assessment")
            ])
        ];
    }

    private static ProgrammingPlanProfile DetectProgrammingProfile(string title)
    {
        var text = title.ToLowerInvariant();

        if (ContainsAny(text, "python", "pyhton", "django", "flask", "fastapi"))
        {
            return new("Python", "python", "script/module yapisi ve virtual environment", "exception ve stack trace okuma", "list/dict/comprehension ve dosya/JSON akisi", "fonksiyon, class ve paketleme", "CLI veya FastAPI tabanli mini proje");
        }

        if (ContainsAny(text, "javascript", "typescript", "node", "react", "frontend", "next"))
        {
            var display = ContainsAny(text, "typescript", "ts") ? "TypeScript" : ContainsAny(text, "react", "frontend", "next") ? "React/TypeScript" : "JavaScript";
            return new(display, "js-ts", "module, runtime ve package script akisi", "console/error trace ve async hata okuma", "array/object, promise ve API verisi", "component, function ve state ayrimi", "kucuk UI veya Node API mini projesi");
        }

        if (ContainsAny(text, "java ", " java", "spring"))
        {
            return new("Java", "java", "class, package ve main akisi", "exception ve stack trace okuma", "collection, stream ve dosya/JSON akisi", "OOP, interface ve service ayrimi", "CLI veya Spring tabanli mini proje");
        }

        if (ContainsAny(text, "sql", "database", "veritabani", "veri tabani", "postgres", "mssql"))
        {
            return new("SQL", "sql", "schema, tablo ve sorgu akisi", "query hata mesaji ve execution plan okuma", "join, index ve aggregation", "modelleme, transaction ve constraint ayrimi", "kucuk raporlama/veri analizi mini projesi");
        }

        if (ContainsAny(text, "c#", "c sharp", "csharp", ".net", "dotnet", "asp.net"))
        {
            return new("C#/.NET", "csharp", "program yapisi: using, namespace, class ve Main", "exception ve compile/runtime hata okuma", "List, Dictionary, LINQ ve JSON/dosya akisi", "class, interface, inheritance ve dependency ayrimi", "kucuk C#/.NET uygulamasi");
        }

        return new(title, "code", "dil/runtime kurulumu ve dosya yapisi", "derleme/runtime hata mesajini okuma", "veri yapilari ve input-output akisi", "fonksiyon, modul ve sorumluluk ayrimi", "kucuk calisan uygulama");
    }

    private static List<ModuleDefinition> BuildProgrammingFallbackModules(string title)
    {
        var profile = DetectProgrammingProfile(title);
        var subject = string.IsNullOrWhiteSpace(profile.DisplayName) ? title : profile.DisplayName;
        var prefix = profile.SkillPrefix;

        return
        [
            new($"{subject} Temel Okuma ve Calisma Modeli", "book",
            [
                new(profile.ProgramShape, $"{prefix}-program-shape"),
                new("Temel kod akisini elle izleme", $"{prefix}-code-trace"),
                new("Console/output, log ve hata mesajini okuma", $"{prefix}-output-errors", "PracticeLab"),
                new("Ilk mini alistirma: calistir, hatayi acikla, duzelt", $"{prefix}-feedback-loop", "PracticeLab")
            ]),
            new($"{subject} Dil ve Kavram Temeli", "🧱",
            [
                new("Degiskenler, tipler ve deger modeli", $"{prefix}-types"),
                new("Ifade, operator ve kontrol mantigi", $"{prefix}-operators"),
                new("Fonksiyon/metot parcalama", $"{prefix}-functions"),
                new("Okunabilir kod ve isimlendirme", $"{prefix}-readable-code")
            ]),
            new("Akis Kontrolu ve Problem Okuma", "🧭",
            [
                new("if/else veya pattern secimi", $"{prefix}-decision-flow"),
                new("Donguler ve tekrar eden isleri ayirma", $"{prefix}-loops", "PracticeLab"),
                new("Input-output senaryosu kurma", $"{prefix}-io-scenario"),
                new("Hata laboratuvari: yanlis kosul, sonsuz dongu, eksik veri", $"{prefix}-debugging", "PracticeLab")
            ]),
            new("Veri, Abstraction ve Tasarim", "🏗️",
            [
                new(profile.DataConcept, $"{prefix}-data-flow"),
                new(profile.AbstractionConcept, $"{prefix}-abstraction"),
                new("Kodu daha kucuk sorumluluklara bolme", $"{prefix}-refactor"),
                new("Mini refactor: tekrar eden kodu temizleme", $"{prefix}-mini-refactor", "PracticeLab")
            ]),
            new("Hata Yonetimi, Test ve Edge Case", "🧪",
            [
                new(profile.RuntimeConcept, $"{prefix}-runtime-errors"),
                new("Test senaryosu ve edge-case listesi", $"{prefix}-test-cases", "Assessment"),
                new("Yanlis cozumden kok neden cikarma", $"{prefix}-mistake-classification", "Remediation"),
                new("Kod calistirma sonucunu Tutor aciklamasina cevirme", $"{prefix}-tutor-bridge", "PracticeLab")
            ]),
            new("Mini Proje ve Mastery Dongusu", "🚀",
            [
                new(profile.ProjectShape, $"{prefix}-mini-project"),
                new("Kod okuma ve kucuk gelistirme turu", $"{prefix}-code-reading"),
                new("Yanlis cevap ve IDE hatalarini tekrar kartina cevirme", $"{prefix}-remediation-loop", "Remediation"),
                new($"Final pratik: {subject} ile calisan kucuk demo", $"{prefix}-final-practice", "Assessment")
            ])
        ];
    }

    private static List<ModuleDefinition> BuildGeneralFallbackModules(string title) =>
    [
        new($"{title} Kavram Haritasi", "🧭",
        [
            new("Ana kavramlari ayirma", "general-concepts"),
            new("On bilgi kontrolu", "general-baseline"),
            new("Kaynak ve wiki baglami", "general-grounding"),
            new("Bugunku hedefe baglama", "general-study-focus")
        ]),
        new("Temel Yapitaslari", "🧱",
        [
            new("Birinci temel kavram", "general-core-1"),
            new("Ikinci temel kavram", "general-core-2"),
            new("Kavramlar arasi bag", "general-relations"),
            new("Tutor ile kontrol sorusu", "general-tutor-check", "Assessment")
        ]),
        new("Uygulama ve Ornekler", "🧪",
        [
            new("Basit ornek", "general-example-basic"),
            new("Uygulamali senaryo", "general-scenario", "PracticeLab"),
            new("Karsilastirmali analiz", "general-compare"),
            new("Kendi ornegini kurma", "general-own-example", "PracticeLab")
        ]),
        new("Yanlislar ve Telafi", "🔁",
        [
            new("Sik yanlislar", "general-mistakes", "Remediation"),
            new("Telafi pratigi", "general-remediation", "Remediation"),
            new("Mini quiz", "general-assessment", "Assessment"),
            new("Yanlis cevabi review adimina cevirme", "general-review-pressure", "Remediation")
        ]),
        new("Pekistirme ve Sonraki Adim", "🚀",
        [
            new("Flashcard ozeti", "general-flashcards"),
            new("Review dongusu", "general-review", "QuickReview"),
            new("Sonraki kucuk hedef", "general-next-step"),
            new("Kapanis pratigi", "general-closing-practice", "Assessment")
        ]),
        new("Proje / Uygulama Kapanisi", "🧩",
        [
            new("Kucuk demo gorevi", "general-demo-task", "PracticeLab"),
            new("Ogrenilenleri kaynakla baglama", "general-source-link"),
            new("Eksik noktalar icin ikinci tur", "general-second-pass", "Remediation"),
            new("Final kontrol ve devam rotasi", "general-final-check", "Assessment")
        ])
    ];

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

            var payload = JsonSerializer.Serialize(new {
                Search = searchResult,
                BestVideoId = videoId,
                Transcript = transcript
            });

            Orka.Core.DTOs.TeachingReference? teachingReference = null;

            if (redis != null)
            {
                await redis.SaveYouTubeContextAsync(parentTopicId, payload);

                var educatorCore = _serviceProvider.GetService<IEducatorCoreService>();
                if (educatorCore != null)
                    teachingReference = await educatorCore.NormalizeTeachingReferenceAsync(parentTopicId, payload);
            }

            if (teachingReference != null)
            {
                var playlistsInfo = "";
                try {
                    using var doc = JsonDocument.Parse(payload);
                    playlistsInfo = doc.RootElement.TryGetProperty("Playlists", out var p) ? p.GetString() : "";
                } catch {}

                return $"""

                    [YOUTUBE EDUCATOR REFERENCE - PLANNING STYLE & RESOURCES]
                    Source: [youtube:{teachingReference.SourceId}] Status: {teachingReference.Status}
                    Teaching flow: {teachingReference.TeachingFlow}
                    {playlistsInfo}
                    Examples: {string.Join(" | ", teachingReference.Examples.Take(4))}
                    Common mistakes: {string.Join(" | ", teachingReference.CommonMistakes.Take(4))}
                    Practice ideas: {string.Join(" | ", teachingReference.PracticeIdeas.Take(4))}
                    Use this to improve curriculum sequence and recommend these high-quality playlists/channels to the user.
                    """;
            }

            return $"\n\n[YOUTUBE EDUCATOR REFERENCE - PLANNING STYLE ONLY]:\n{transcript}\nUse this only for teaching sequence and examples; do not treat it as factual proof.";
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

                    var lessons = new List<LessonDefinition>();
                    if (mod.TryGetProperty("lessons", out var lessonsProp))
                    {
                        foreach (var l in lessonsProp.EnumerateArray())
                        {
                            var lTitle = l.GetProperty("title").GetString();
                            var lSkill = l.TryGetProperty("skillTag", out var sProp) ? sProp.GetString() : "genel-kavram";
                            var lIntent = l.TryGetProperty("intent", out var iProp) ? iProp.GetString() : "Core";

                            if (!string.IsNullOrWhiteSpace(lTitle))
                                lessons.Add(new LessonDefinition(lTitle, lSkill ?? "genel-kavram", lIntent ?? "Core"));
                        }
                    }
                    else if (mod.TryGetProperty("topics", out var topicsProp)) // Geriye dönük uyumluluk
                    {
                        foreach (var t in topicsProp.EnumerateArray())
                        {
                            var tTitle = t.GetString();
                            if (!string.IsNullOrWhiteSpace(tTitle))
                                lessons.Add(new LessonDefinition(tTitle, "genel-kavram", "Core"));
                        }
                    }

                    if (lessons.Count > 0)
                        modules.Add(new ModuleDefinition(title, emoji, lessons));
                }

                if (modules.Count >= 2) return modules;
            }
        }
        catch { /* yoksay, null dönecek */ }

        return null;
    }

    private static bool TryAcceptPlanModules(
        List<ModuleDefinition>? modules,
        string topicTitle,
        out List<ModuleDefinition> acceptedModules,
        out string rejectionReason)
    {
        acceptedModules = [];

        if (modules == null || modules.Count == 0)
        {
            rejectionReason = "parse_failed";
            return false;
        }

        var cleaned = modules
            .Select(module => module with
            {
                Title = string.IsNullOrWhiteSpace(module.Title) ? "Modul" : module.Title.Trim(),
                Emoji = string.IsNullOrWhiteSpace(module.Emoji) ? "📘" : module.Emoji,
                Lessons = module.Lessons
                    .Where(lesson => !string.IsNullOrWhiteSpace(lesson.Title))
                    .Select(lesson => lesson with
                    {
                        Title = lesson.Title.Trim(),
                        SkillTag = string.IsNullOrWhiteSpace(lesson.SkillTag) ? "genel-kavram" : lesson.SkillTag.Trim(),
                        Intent = string.IsNullOrWhiteSpace(lesson.Intent) ? "Core" : lesson.Intent.Trim()
                    })
                    .ToList()
            })
            .Where(module => module.Lessons.Count > 0)
            .ToList();

        var domain = DetectPlanDomain(topicTitle);
        var isProgramming = IsProgrammingTopic(topicTitle) || domain is PlanDomain.Programming or PlanDomain.Algorithm;
        var minModules = isProgramming ? MinimumProgrammingModules : MinimumGeneralModules;
        var minLessonsPerModule = isProgramming ? MinimumProgrammingLessonsPerModule : MinimumGeneralLessonsPerModule;
        var minTotalLessons = isProgramming ? MinimumProgrammingTotalLessons : MinimumGeneralTotalLessons;
        var totalLessons = cleaned.Sum(module => module.Lessons.Count);

        if (cleaned.Count < minModules)
        {
            rejectionReason = $"too_few_modules:{cleaned.Count}/{minModules}";
            return false;
        }

        if (totalLessons < minTotalLessons)
        {
            rejectionReason = $"too_few_lessons:{totalLessons}/{minTotalLessons}";
            return false;
        }

        var thinModule = cleaned.FirstOrDefault(module => module.Lessons.Count < minLessonsPerModule);
        if (thinModule != null)
        {
            rejectionReason = $"thin_module:{thinModule.Title}:{thinModule.Lessons.Count}/{minLessonsPerModule}";
            return false;
        }

        acceptedModules = cleaned;
        rejectionReason = "accepted";
        return true;
    }

    private static List<ModuleDefinition> EnsureBasePlanQualityBeforeSave(
        List<ModuleDefinition> modules,
        string topicTitle,
        DiagnosticWeaknessSummary diagnostic)
    {
        if (TryAcceptPlanModules(modules, topicTitle, out var accepted, out _))
        {
            return accepted;
        }

        return BuildQualityFallbackModules(topicTitle);
    }

    private static bool IsProgrammingTopic(string? topicTitle)
    {
        var text = (topicTitle ?? string.Empty).ToLowerInvariant();
        return ContainsAny(text,
            "c#", "c sharp", "csharp", ".net", "dotnet", "asp.net", "programlama", "programming", "yazilim", "yazılım",
            "software", "backend", "frontend", "fullstack", "python", "javascript", "typescript", " java", "java ",
            "react", "node", "sql", "database", "veritabani", "veri tabani", "kod", "coding", "ide");
    }

    private static List<ModuleDefinition> ApplyDiagnosticTraceability(
        List<ModuleDefinition> modules,
        DiagnosticWeaknessSummary diagnostic)
    {
        if (!diagnostic.HasWeaknesses)
        {
            return modules;
        }

        var diagnosticLessons = diagnostic.WeakConcepts
            .Take(4)
            .Select((concept, index) =>
            {
                var mistake = diagnostic.MistakePatterns.Count == 0
                    ? null
                    : diagnostic.MistakePatterns[index % diagnostic.MistakePatterns.Count];
                var intent = IntentForMistake(mistake?.Value);
                var label = HumanizeDiagnosticLabel(concept.Value);
                var suffix = TitleSuffixForMistake(mistake?.Value);

                return new LessonDefinition(
                    $"{label} {suffix}",
                    $"diagnostic:{concept.Value}",
                    intent,
                    BuildDiagnosticPhaseMetadata(diagnostic, concept, mistake));
            })
            .ToList();

        if (diagnosticLessons.Count == 0)
        {
            diagnosticLessons = diagnostic.MistakePatterns
                .Take(3)
                .Select(mistake =>
                {
                    var label = HumanizeDiagnosticLabel(mistake.Value);
                    return new LessonDefinition(
                        $"{label} Calisma Pratigi",
                        $"diagnostic:mistake:{mistake.Value}",
                        IntentForMistake(mistake.Value),
                        BuildDiagnosticPhaseMetadata(diagnostic, null, mistake));
                })
                .ToList();
        }

        if (diagnosticLessons.Count == 0)
        {
            return modules;
        }

        var tracedModules = modules
            .Select(module => module with { Lessons = module.Lessons.ToList() })
            .ToList();

        if (tracedModules.Count == 0)
        {
            tracedModules.Add(new("Kisisel Telafi ve Pratik", "target", []));
        }

        for (var i = 0; i < diagnosticLessons.Count; i++)
        {
            tracedModules[i % tracedModules.Count].Lessons.Add(diagnosticLessons[i]);
        }

        return tracedModules;
    }

    private static string BuildDiagnosticPhaseMetadata(
        DiagnosticWeaknessSummary diagnostic,
        DiagnosticTraceEntry? focusConcept,
        DiagnosticTraceEntry? focusMistake)
    {
        return JsonSerializer.Serialize(new
        {
            source = "PlanDiagnostic",
            diagnosticWeakConcepts = diagnostic.WeakConcepts.Select(c => new { value = c.Value, count = c.Count }).ToList(),
            diagnosticMistakePatterns = diagnostic.MistakePatterns.Select(m => new { value = m.Value, count = m.Count }).ToList(),
            focusWeakConcept = focusConcept?.Value,
            focusWeakConceptLabel = focusConcept == null ? null : HumanizeDiagnosticLabel(focusConcept.Value),
            focusMistakePattern = focusMistake?.Value
        });
    }

    private static string IntentForMistake(string? mistake)
    {
        if (string.IsNullOrWhiteSpace(mistake))
        {
            return "Remediation";
        }

        return mistake.Trim() switch
        {
            "Procedural" => "PracticeLab",
            "Application" => "PracticeLab",
            "Reading" => "Remediation",
            "MisreadQuestion" => "Remediation",
            "Conceptual" => "DeepDive",
            "Careless" => "QuickReview",
            _ => "Remediation"
        };
    }

    private static string TitleSuffixForMistake(string? mistake)
    {
        if (string.IsNullOrWhiteSpace(mistake))
        {
            return "Calisma Pratigi";
        }

        return mistake.Trim() switch
        {
            "Procedural" => "Pratik Laboratuvari",
            "Application" => "Uygulama Pratigi",
            "Reading" => "Okuma Onarimi",
            "MisreadQuestion" => "Soru Okuma Kontrolu",
            "Conceptual" => "Kavram Onarimi",
            "Careless" => "Dikkat Kontrolu",
            _ => "Calisma Pratigi"
        };
    }

    private static string HumanizeDiagnosticLabel(string value)
    {
        var normalized = value
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Replace('/', ' ')
            .Replace('.', ' ')
            .Trim();

        while (normalized.Contains("  ", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "Eksik Kavram";
        }

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Equals("vs", StringComparison.OrdinalIgnoreCase)
                ? "vs"
                : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word.ToLowerInvariant()));
        return string.Join(' ', words);
    }

    /// <summary>3 seviyeli Topic hiyerarşisi: Ana Konu → Modül → Ders. Her ders için WikiPage oluşturur.</summary>
    private async Task<List<Topic>> SaveModularSubTopicsAsync(
        Guid parentTopicId, List<ModuleDefinition>? modules, Guid userId)
    {
        if (modules == null || !modules.Any()) return new List<Topic>();

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
                PlanIntent     = "Module",
                CurrentPhase   = TopicPhase.Planning,
                Order          = mi,
                CreatedAt      = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow,
                TotalSections  = mod.Lessons.Count
            };
            db.Topics.Add(moduleTopic);

            // Ders topic'leri (3. seviye)
            for (int li = 0; li < mod.Lessons.Count; li++)
            {
                var lessonDef = mod.Lessons[li];
                var lessonTopic = new Topic
                {
                    Id             = Guid.NewGuid(),
                    UserId         = userId,
                    ParentTopicId  = moduleTopic.Id,
                    Title          = lessonDef.Title,
                    Emoji          = mod.Emoji,
                    Category       = $"Plan:{lessonDef.Intent}",
                    PlanIntent     = lessonDef.Intent,
                    PhaseMetadata  = lessonDef.PhaseMetadata,
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
    private record ModuleDefinition(string Title, string Emoji, List<LessonDefinition> Lessons);
    private record LessonDefinition(string Title, string SkillTag, string Intent = "Core", string? PhaseMetadata = null);

    private sealed record DiagnosticWeaknessSummary(
        List<DiagnosticTraceEntry> WeakConcepts,
        List<DiagnosticTraceEntry> MistakePatterns)
    {
        public bool HasWeaknesses => WeakConcepts.Count > 0 || MistakePatterns.Count > 0;

        public static DiagnosticWeaknessSummary Parse(string? diagnosticQuizSummary)
        {
            if (string.IsNullOrWhiteSpace(diagnosticQuizSummary))
            {
                return new DiagnosticWeaknessSummary([], []);
            }

            return new DiagnosticWeaknessSummary(
                ParseLine(diagnosticQuizSummary, "WeakConcepts:"),
                ParseLine(diagnosticQuizSummary, "MistakePatterns:"));
        }

        private static List<DiagnosticTraceEntry> ParseLine(string text, string prefix)
        {
            var line = text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

            if (line == null)
            {
                return [];
            }

            var value = line[prefix.Length..].Trim();
            if (value.Length == 0 || value.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return [];
            }

            return value
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ParseEntry)
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
                .OrderByDescending(entry => entry.Count)
                .ThenBy(entry => entry.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static DiagnosticTraceEntry ParseEntry(string item)
        {
            var separator = item.LastIndexOf(':');
            if (separator <= 0)
            {
                return new DiagnosticTraceEntry(item.Trim(), 1);
            }

            var value = item[..separator].Trim();
            var countText = item[(separator + 1)..].Trim();
            return int.TryParse(countText, out var count)
                ? new DiagnosticTraceEntry(value, count)
                : new DiagnosticTraceEntry(item.Trim(), 1);
        }
    }

    private sealed record DiagnosticTraceEntry(string Value, int Count);

    private async Task<string> AnalyzeBaselineQuizResultsAsync(Guid topicId, Guid userId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

            // 1. Baseline Quiz sonuçlarını getir (en son 20 soru)
            var attempts = await db.QuizAttempts
                .Where(a => a.TopicId == topicId && a.UserId == userId)
                .OrderByDescending(a => a.CreatedAt)
                .Take(20)
                .ToListAsync();

            if (!attempts.Any()) return string.Empty;

            // 2. Yanlış cevaplara odaklan
            var failedAttempts = attempts.Where(a => !a.IsCorrect).ToList();
            if (!failedAttempts.Any())
                return "Öğrenci baseline quiz'de kusursuz performans gösterdi. Müfredatı ileri düzey, teknik detaylara boğmadan, vizyoner ve hızlı akışlı bir yapıda hazırla. Temel kavramları atla.";

            var failedSummary = string.Join("\n", failedAttempts.Select(a =>
                $"- Soru: {a.Question}\n  Beceri: {a.SkillTag}\n  Hata Analizi: {a.Explanation}"));

            // 3. LLM ile Mikro-Teşhis yap
            var systemPrompt = """
                Sen akademik düzeyde bir 'Eğitim Teşhis Uzmanı (Diagnostic Educator)' botusun.
                Görevin: Öğrencinin baseline quiz hatalarını analiz ederek, zihinsel modelindeki eksik parçaları (micro-gaps) tespit etmek.

                ANALİZ KURALI:
                - "Şu soruyu yanlış yaptı" deme. "Kullanıcı X'i biliyor ama Y kavramının Z üzerindeki etkisini yanlış yorumluyor" gibi derinlikli konuş.
                - Müfredat mimarına "Şu 3 kritik noktaya matkapla (drill) inmeli, şu analojileri kullanmalısın" şeklinde direktif ver.
                """;

            var userPrompt = $"Öğrenci Hata Raporu:\n{failedSummary}\n\nLütfen teşhisini ve müfredat direktiflerini Türkçe olarak hazırla.";

            return await _factory.CompleteChatAsync(AgentRole.DeepPlan, systemPrompt, userPrompt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DeepPlan] Baseline analiz aşaması başarısız oldu.");
            return string.Empty;
        }
    }

    public async Task<string> GenerateBaselineQuizAsync(string topicTitle)
    {
        _logger.LogInformation("[DeepPlan] Baseline Quiz için Korteks keşfi başlatılıyor: {Topic}", topicTitle);

        // 1. Korteks Keşfi (Quiz sorularının gerçek dünya verilerine dayanması için)
        string compressedResearchPromptBlock;
        try
        {
            var korteksResult = await _korteks.RunResearchWithEvidenceAsync(topicTitle, Guid.Empty, null);
            var compressedResearch = _planResearchCompressor.Compress(korteksResult);
            compressedResearchPromptBlock = _planResearchCompressor.BuildPromptBlock(compressedResearch);
            _logger.LogInformation(
                "[DeepPlan] Baseline Quiz Korteks bağlamı sıkıştırıldı. Mode={GroundingMode} Sources={SourceCount} BlockLen={Len}",
                compressedResearch.GroundingMode,
                compressedResearch.SourceCount,
                compressedResearchPromptBlock.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DeepPlan] Baseline Quiz Korteks araştırması başarısız oldu. Tanılama kurallarıyla devam edilecek.");
            var blockedResearch = new KorteksResearchResultDto
            {
                Topic = topicTitle,
                GroundingMode = GroundingMode.BlockedProvider,
                ProviderFailures = [ex.Message],
                IsFallback = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            var compressedResearch = _planResearchCompressor.Compress(blockedResearch);
            compressedResearchPromptBlock = _planResearchCompressor.BuildPromptBlock(compressedResearch);
        }

        var quizIntelligenceBrief = PlanIntelligenceBriefBuilder.BuildForDiagnosticQuiz(
            topicTitle,
            compressedResearchPromptBlock);

        var systemPrompt = $$"""
            Sen profesyonel bir 'Eğitim Tanılama Uzmanı (Educational Diagnostician)' botusun.
            Görevin: Kullanıcının '{{topicTitle}}' konusundaki gerçek bilgi seviyesini EN İNCE AYRINTISINA KADAR tespit etmek için 20 soru hazırlamak.

            {{quizIntelligenceBrief}}

            Eger GroundingMode FallbackInternalKnowledge veya BlockedProvider ise bu baglami guncel/kaynakli kanit gibi sunma.
            Bu durumda sorulari konu basligi, genel pedagojik tanilama kurallari ve temel mufredat kapsami ile uret.

            SORU DAĞILIMI VE DERİNLİK (Toplam 20 Soru):
            - 1-4: TEMEL KAVRAMLAR (Başlangıç seviyesi, terminoloji kontrolü)
            - 5-10: UYGULAMA VE SENARYO (Orta seviye, "nasıl yapılır?" ve kod okuma)
            - 11-16: ANALİZ VE PROBLEM ÇÖZME (İleri seviye, hata ayıklama ve mimari kararlar)
            - 17-20: UZMANLIK VE DERİN KONULAR (Zorlayıcı, uç durumlar ve optimizasyon)

            KALİTE KURALLARI:
            - Sorulari sadece yukaridaki sikistirilmis arastirma baglami ve konu basligiyla destekle; ham Korteks raporu varsayma.
            - Genis bir kavram haritasini kapsa: temel kavram, onkosul, uygulama, analiz, hata ayiklama ve uc durumlar.
            - Soru tipleri tekrarlamasin: conceptual, procedural, application, analysis ve misconception_probe karisik kullanilsin.
            - Zorluk dagilimi dengeli olsun: kolay, orta ve zor sorulari acikca karistir.
            - Soru metinleri birbirinin kopyasi veya yakin tekrari olmasin.
            - En az 8 farkli conceptTag ve en az 4 farkli questionType kullan.
            - En az 5 soru yaygin kavram yanilgisini veya beklenen hata kategorisini hedeflesin.
            - Programlama/teknik konularda en az bir soru gercek kod parcasi, kod okuma veya hata ayiklama tarzi icersin.
            - Sadece "X nedir?" gibi ezber soruları YASAKTIR. Sorular gerçek hayat problemlerini veya teknik senaryoları yansıtmalıdır.
            - Yanlış seçenekler (çeldiriciler) mantıklı ve kafa karıştırıcı olmalı.
            - Her soru icin uygun bir `skillTag`, `conceptTag`, `learningObjective`, `questionType` ve `expectedMisconceptionCategory` belirle.

            ÇIKTI FORMATI (SADECE JSON):
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
                "correctAnswer": "Dogru sik metni veya id'si",
                "explanation": "Detayli aciklama",
                "skillTag": "beceri-etiketi",
                "difficulty": "kolay|orta|zor",
                "conceptTag": "kavram-etiketi",
                "learningObjective": "Olculen ogrenme hedefi",
                "questionType": "conceptual|procedural|application|analysis|misconception_probe",
                "expectedMisconceptionCategory": "Conceptual|Procedural|Calculation|Reading|Application|MisreadQuestion|Careless",
                "topic": "{{topicTitle}}"
              }
            ]

            DİL: Türkçe.
            """;

        var rawQuiz = await _factory.CompleteChatAsync(AgentRole.DeepPlan, systemPrompt, $"Konu: \"{topicTitle}\" için 20 adet derinlikli baseline sorusu üret.");
        var validatedQuiz = DiagnosticQuizQualityGate.EnsureQualityOrFallback(rawQuiz, topicTitle, out var quality);
        if (!quality.IsAcceptable)
        {
            _logger.LogWarning(
                "[DeepPlan] Baseline quiz quality failed. Topic={Topic} Failures={Failures}",
                topicTitle,
                string.Join(" | ", quality.Failures));
        }

        return validatedQuiz;
    }

    private static DeepPlanGroundingMetadataDto ToDeepPlanGrounding(KorteksResearchResultDto result) =>
        new()
        {
            GroundingMode = result.GroundingMode,
            SourceCount = result.SourceCount,
            Sources = result.Sources,
            ProviderWarnings = result.ProviderFailures.Concat(result.Warnings).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            IsFallback = result.IsFallback
        };

    private static string AppendGroundingMetadata(string contextInfo, DeepPlanGroundingMetadataDto? grounding)
    {
        if (grounding == null)
        {
            return contextInfo;
        }

        return $"{contextInfo}\n\n{BuildGroundingPromptBlock(grounding)}";
    }

    private static string BuildGroundingPromptBlock(DeepPlanGroundingMetadataDto? grounding)
    {
        if (grounding == null)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[KORTEKS GROUNDING METADATA]");
        sb.AppendLine($"GroundingMode: {grounding.GroundingMode}");
        sb.AppendLine($"SourceCount: {grounding.SourceCount}");
        sb.AppendLine($"IsFallback: {grounding.IsFallback}");
        if (grounding.ProviderWarnings.Any())
        {
            sb.AppendLine($"ProviderWarnings: {string.Join(" | ", grounding.ProviderWarnings.Take(5))}");
        }

        if (grounding.Sources.Any())
        {
            sb.AppendLine("Sources:");
            foreach (var source in grounding.Sources.Take(8).Select((s, i) => new { Source = s, Index = i + 1 }))
            {
                var snippet = string.IsNullOrWhiteSpace(source.Source.Snippet) ? "" : $" Snippet={source.Source.Snippet}";
                sb.AppendLine($"{source.Index}. Provider={source.Source.Provider} Tool={source.Source.ToolName} Title={source.Source.Title} Url={source.Source.Url}{snippet}");
            }
        }
        else
        {
            sb.AppendLine("Sources: none with valid URLs.");
        }

        sb.AppendLine("Instruction: If GroundingMode is FallbackInternalKnowledge or BlockedProvider, generate a useful curriculum but do not claim it is source-grounded or based on current internet research.");
        return sb.ToString();
    }

    private string BuildAdaptivePromptSection(Orka.Core.DTOs.AdaptiveLearningContext context)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[ADAPTIF ÖĞRENME BAĞLAMI - STRÜKTÜREL VERİ]");
        sb.AppendLine($"- Öğrenci Seviyesi: {context.UserLevel}");

        if (context.QuizSummary != null && context.QuizSummary.TotalAttempts > 0)
        {
            sb.AppendLine($"- Son Performans: {context.QuizSummary.TotalAttempts} denemede %{Math.Round(context.QuizSummary.AverageAccuracy * 100)} başarı.");
        }

        if (context.WeakSkills.Any())
        {
            sb.AppendLine("- Tespit Edilen Zayıf Beceriler (SkillTags):");
            foreach (var s in context.WeakSkills.Take(5))
                sb.AppendLine($"  * {s.Skill} (Başarı: %{Math.Round(s.Accuracy * 100)}, Hata: {s.WrongCount})");
        }

        if (context.WeakConcepts.Any())
        {
            sb.AppendLine("- Tekrarlayan Kavramsal Zayıflıklar (ConceptTags):");
            foreach (var c in context.WeakConcepts)
                sb.AppendLine($"  * {c.Concept} ({c.Frequency} kez hata yapıldı)");
        }

        if (context.MistakePatterns.Any())
        {
            sb.AppendLine("- Hata Pattern Analizi:");
            foreach (var p in context.MistakePatterns)
                sb.AppendLine($"  * {p.Label} ({p.Frequency} kez)");
        }

        if (context.DueReviewSkills.Any())
        {
            sb.AppendLine("- SRS Gözden Geçirme Baskısı (Due for Review):");
            sb.AppendLine($"  * {string.Join(", ", context.DueReviewSkills)}");
        }

        if (!string.IsNullOrEmpty(context.StudentProfileSummary))
        {
            sb.AppendLine($"- Öğrenci Profil Özeti: {context.StudentProfileSummary}");
        }

        if (!string.IsNullOrEmpty(context.PreviousSessionSummary))
        {
            sb.AppendLine($"- Önceki Oturum Özeti: {context.PreviousSessionSummary}");
        }

        sb.AppendLine("\nTALİMAT: Müfredatı hazırlarken yukarıdaki [ADAPTIF ÖĞRENME BAĞLAMI] verilerini temel al. Zayıf kavramlara 'Deep Dive' veya 'Remedial' dersleri ekle, hata patternlerine uygun analojiler öner ve SRS baskısı olan konuları pekiştir.");

        return sb.ToString();
    }
}
