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
        modules = ApplyDiagnosticTraceability(modules, DiagnosticWeaknessSummary.Parse(diagnosticQuizSummary));
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

        // ── Sprint 2: Mikro-Teşhis (Baseline Analizi) ─────────────────────
        if (!string.IsNullOrWhiteSpace(compressedResearchPromptBlock))
        {
            contextInfo = $"\n\n{compressedResearchPromptBlock}\n\nBu sıkıştırılmış araştırma bağlamını yalnızca konu kapsamı, güncellik ve kaynak farkındalığı desteği olarak kullan.";
        }

        var baselineDiagnostic = !string.IsNullOrWhiteSpace(diagnosticQuizSummary)
            ? diagnosticQuizSummary
            : await AnalyzeBaselineQuizResultsAsync(parentTopicId, userId);
        if (!string.IsNullOrWhiteSpace(baselineDiagnostic))
        {
            _logger.LogInformation("[DeepPlan] Baseline mikro-teşhis raporu hazırlandı.");
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
            - [SIKISTIRILMIS PLAN ARASTIRMA BAGLAMI] içindeki bounded Korteks bulgularını konu kapsamı/güncellik desteği olarak kullan; [ADAPTIF ÖĞRENME BAĞLAMI] önceliklidir.
            - [MİKRO-TEŞHİS RAPORU] ve [ADAPTIF ÖĞRENME BAĞLAMI] içindeki zayıf noktaları plana "Derinlemesine İyileştirme" veya "Pratik Lab" dersleri olarak ekle.
            - Tekrar eden hata paternlerine (Mistake Patterns) yönelik ekstra pekiştirme modülleri öner.
            - SRS / Gözden Geçirme baskısı olan becerileri müfredatın başına veya ilgili modüllere 'Hızlı Tekrar' olarak serpiştir.
            - Öğrencinin zaten bildiği (başarılı olduğu) kısımları müfredatta 'Hızlı Özet' veya 'Hatırlatma' olarak daralt.
            - Kullanıcı cümlesinden (Örn: "C# çalışmak istiyorum") ASIL KONUYU çıkar ("C# Programlama"). Kesinlikle "Selamlaşma", "İstek", "Giriş" gibi konulardan bahsetme!
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
                    { "title": "Ders Başlığı 2", "skillTag": "beceri-etiketi-2", "intent": "DeepDive" }
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
            if (parsedModules != null)
            {
                setGrounding?.Invoke(groundingMetadata);
                return parsedModules;
            }
            _logger.LogWarning("[DeepPlan] Parse hatası (Deneme {Attempt}/2). Çıktı: {Raw}", attempt, raw.Length > 200 ? raw[..200] + "..." : raw);
        }

        _logger.LogError("[DeepPlan] JSON parse tüm denemelerde başarısız. Kapsamlı Fallback uygulanıyor.");
        var domainFallback = BuildDomainFallbackModules(topicTitle, massive: false);
        if (domainFallback != null)
        {
            setGrounding?.Invoke(groundingMetadata);
            return domainFallback;
        }

        // Gelişmiş Dinamik Fallback (Sıradan olmaktan arındırılmış)
        setGrounding?.Invoke(groundingMetadata);
        return new List<ModuleDefinition>
        {
            new($"{topicTitle} Sistem Yapısı", "🧱", new List<LessonDefinition> { new($"{topicTitle} Çekirdek Mimarisi", "basics"), new($"{topicTitle} Kurulum", "setup") }),
            new($"{topicTitle} Süreç İşletimi", "⚙️", new List<LessonDefinition> { new($"{topicTitle} Veri İşleme", "process"), new($"{topicTitle} Senaryo Analizi", "analysis") }),
            new($"{topicTitle} Performans Ayarları", "🚀", new List<LessonDefinition> { new($"{topicTitle} Optimizasyon", "optimization"), new($"{topicTitle} Problem Çözme", "troubleshooting") })
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
                new($"{title} Kazanım Haritası", "🎯", new List<LessonDefinition> { new("Sınav kapsamı", "strategy"), new("Zaman yönetimi", "timing") }),
                new("Paragraf Atölyesi", "📚", new List<LessonDefinition> { new("Ana fikir", "reading"), new("Çeldiriciler", "logic") }),
                new("Matematik Rotası", "🧮", new List<LessonDefinition> { new("Problem çözme", "math"), new("Hata izleme", "analysis") }),
                new("Deneme Döngüsü", "🔁", new List<LessonDefinition> { new("Skill raporu", "mastery"), new("Tekrar planı", "remediation") })
            },
            PlanDomain.Algorithm => new List<ModuleDefinition>
            {
                new("Problem Okuma", "🧠", new List<LessonDefinition> { new("Input-output analizi", "complexity"), new("Edge-case listesi", "testing") }),
                new("Pointers & Window", "🧭", new List<LessonDefinition> { new("Two Pointers", "algo"), new("Sliding Window", "algo") }),
                new("Traversal Pratiği", "🕸️", new List<LessonDefinition> { new("BFS/DFS", "graph"), new("Debug teknikleri", "ide") }),
                new("DP & Optimizasyon", "⚙️", new List<LessonDefinition> { new("State tanımı", "dp"), new("Memoization", "optimization") })
            },
            PlanDomain.Math => new List<ModuleDefinition>
            {
                new($"{title} Kavram Sezgisi", "📐", new List<LessonDefinition> { new("Formül görselleştirme", "concept"), new("İşlem dili", "notation") }),
                new("Adım Adım Çözüm", "✍️", new List<LessonDefinition> { new("Verilen-istenen ayrımı", "logic"), new("Tip sınıflandırma", "pattern") }),
                new("Uygulama Setleri", "🧮", new List<LessonDefinition> { new("Örnek zinciri", "practice"), new("Zamanlı mini quiz", "assessment") }),
                new("Mastery Kontrolü", "🔁", new List<LessonDefinition> { new("Hatalı skill telafisi", "remediation"), new("Tekrar etmeyen örnekler", "mastery") })
            },
            PlanDomain.Language => new List<ModuleDefinition>
            {
                new("Temel İfadeler", "🗣️", new List<LessonDefinition> { new("Günlük kalıplar", "vocabulary"), new("Telaffuz farkındalığı", "pronunciation") }),
                new("Grammar in Context", "📘", new List<LessonDefinition> { new("Bağlamsal yapılar", "grammar"), new("Hata düzeltme", "writing") }),
                new("Speaking & Role-play", "🎙️", new List<LessonDefinition> { new("Diyalog kurma", "speaking"), new("Feedback analizi", "fluency") }),
                new("Active Recall", "🔁", new List<LessonDefinition> { new("Spaced repetition", "memory"), new("Haftalık kapanış", "review") })
            },
            _ => null
        };

        if (modules != null && massive)
        {
            modules.Add(new($"{title} Pekiştirme Laboratuvarı", "🧪", new List<LessonDefinition> { new("Zayıf beceri çalışması", "remediation"), new("Kapanış kontrolü", "assessment") }));
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
    private List<ModuleDefinition>? ParseModuleStructure(string raw)
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

                _logger.LogWarning(
                    "[DeepPlan] Module structure parsed but module count below threshold. ModuleCount={Count}",
                    modules.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[DeepPlan] Module structure parse failed. RawSnippet={Snippet}",
                raw.Length > 200 ? raw[..200] : raw);
        }

        return null;
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
                        $"{label} Tanisal Calisma",
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

        var tracedModules = new List<ModuleDefinition>(modules.Count + 1)
        {
            new("Tanisal Iyilestirme", "🧭", diagnosticLessons)
        };
        tracedModules.AddRange(modules);
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
            return "Tanisal Calisma";
        }

        return mistake.Trim() switch
        {
            "Procedural" => "Pratik Laboratuvari",
            "Application" => "Uygulama Pratigi",
            "Reading" => "Okuma Onarimi",
            "MisreadQuestion" => "Soru Okuma Kontrolu",
            "Conceptual" => "Kavram Onarimi",
            "Careless" => "Dikkat Kontrolu",
            _ => "Tanisal Calisma"
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
            return "Tanisal Zayiflik";
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

        var systemPrompt = $$"""
            Sen profesyonel bir 'Eğitim Tanılama Uzmanı (Educational Diagnostician)' botusun.
            Görevin: Kullanıcının '{{topicTitle}}' konusundaki gerçek bilgi seviyesini EN İNCE AYRINTISINA KADAR tespit etmek için 20 soru hazırlamak.

            [SIKISTIRILMIS QUIZ ARASTIRMA BAGLAMI]
            {{compressedResearchPromptBlock}}

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
