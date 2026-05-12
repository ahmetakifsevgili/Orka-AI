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
/// Yeni konu iin mfredat plan oluturur ve Topics tablosuna kaydeder.
///
/// Model seimi: GitHub Models (Meta-Llama-3.1-405B-Instruct)  Yksek akl yrtme.
/// Failover: AIAgentFactory  Groq  Gemini.
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
        _logger.LogInformation("[DeepPlan] Multi-Agent RAG dngs balyor. Konu: {Topic}", topicTitle);

        // 0. Sprint 1: Otonom Keif Faz (Korteks Entegrasyonu)
        // Eer dardan hazr bir aratrma raporu gelmemise, Korteks'i sahaya sr.
        DeepPlanGroundingMetadataDto? groundingMetadata = null;
        CompressedPlanResearchContextDto? compressedResearchContext = null;
        string compressedResearchPromptBlock = precompressedResearchPromptBlock ?? string.Empty;

        if (string.IsNullOrWhiteSpace(researchContext) && string.IsNullOrWhiteSpace(compressedResearchPromptBlock))
        {
            try
            {
                _logger.LogInformation("[DeepPlan] Mevcut aratrma verisi bulunamad. Korteks derin keif motoru tetikleniyor...");
                var korteksResult = await _korteks.RunResearchWithEvidenceAsync(topicTitle, userId, parentTopicId);
                groundingMetadata = ToDeepPlanGrounding(korteksResult);
                compressedResearchContext = _planResearchCompressor.Compress(korteksResult);
                compressedResearchPromptBlock = _planResearchCompressor.BuildPromptBlock(compressedResearchContext);
                _logger.LogInformation(
                    "[DeepPlan] Korteks kefi sktrld. Mode={GroundingMode} Sources={SourceCount} BlockLen={Len}",
                    compressedResearchContext.GroundingMode,
                    compressedResearchContext.SourceCount,
                    compressedResearchPromptBlock.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DeepPlan] Korteks aratrmas baarsz oldu. Planlama mevcut bilgilerle devam edecek.");
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

        // 1. Durum Snflandrmas (Supervisor Node)
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

        // 2. RAG Kalite Kontrol (Grader Node)
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
                _logger.LogWarning("[DeepPlan] Grader REDDETT. Hallucination veya alakasz context engellendi. (Sfr bilgi ile devam ediliyor)");
            }
        }

        var baselineDiagnostic = !string.IsNullOrWhiteSpace(diagnosticQuizSummary)
            ? diagnosticQuizSummary
            : await AnalyzeBaselineQuizResultsAsync(parentTopicId, userId);
        if (!string.IsNullOrWhiteSpace(baselineDiagnostic))
        {
            _logger.LogInformation("[DeepPlan] Baseline mikro-tehis raporu hazrland.");
        }

        //  Sprint 2: Mikro-Tehis + Korteks plan intelligence szgeci
        if (!string.IsNullOrWhiteSpace(compressedResearchPromptBlock))
        {
            var intelligenceBrief = PlanIntelligenceBriefBuilder.BuildForPlan(
                topicTitle,
                compressedResearchPromptBlock,
                baselineDiagnostic);
            contextInfo = $"\n\n{intelligenceBrief}\n\nBu filtrelenmis Korteks brief'ini yalnizca konu kapsami, guncellik, onkosul ve kaynak farkindaligi destegi olarak kullan; plan omurgasini concept graph sinyali, mikro-teshis ve adaptif baglam belirler.";
        }

        // Faz 17: Yaplandrlm Adaptif Balam (Personalization v1)
        var adaptiveContext = await _adaptiveBuilder.BuildAsync(userId, parentTopicId, topicTitle, userLevel);
        var adaptivePromptSection = BuildAdaptivePromptSection(adaptiveContext);

        string failedTopicsDiagnostic = "";
        if (!string.IsNullOrWhiteSpace(failedTopics))
        {
            failedTopicsDiagnostic = $"\n\n[DKKAT - MKRO TEHS (ZAYIFLIK Analizi)]:\nrenci u konularda HATA YAPMI veya zorlanm: {failedTopics}.\nMfredat eksiksiz ve kapsaml kar, ANCAK rencinin eksik olduu bu konulara matkapla (drill) in! Bu kavramlar getiinde mfredata ekstra 'Uygulamal rnekler', 'Derinlemesine Analiz' ve 'Pratik Lab' alt modlleri ekle. Dier bildii konularda standart anlatmla ge.";
        }

        // 3. YouTube Eitim Videosu Referans (en popler eitimcinin anlatm yaps)
        var youtubeReference = await FetchYouTubeEducationalReferenceAsync(parentTopicId, topicTitle);
        var graphPlanningGuidance = BuildConceptGraphPlanningGuidance();

        var systemPrompt = $$"""
            Sen akademik seviyede bir 'Mfredat Mimar (Curriculum Architect)' botusun.
            Grev: Verilen konuyu profesyonel, kapsaml ve konunun doasna uygun bir mfredata dntrmek.
            Mevcut kullancnn bilgi seviyesi: {{userLevel}}
            Konunun Alan / Kategorisi: {{intentCategory}}
            {{contextInfo}}

            [MKRO-TEHS RAPORU - RENCNN ZAYIFLIKLARI]:
            {{baselineDiagnostic}}

            {{adaptivePromptSection}}

            {{failedTopicsDiagnostic}}
            {{youtubeReference}}
            {{graphPlanningGuidance}}

            ORGANZASYON KURALI (TEHS ODAKLI MMAR):
            - [PLAN INTELLIGENCE BRIEF - LEARNING RESEARCH FILTERED] icindeki arastirma bulgularini yalnizca konu kapsami/guncellik/onkosul destegi olarak kullan; plan omurgasini concept graph sinyali, [MIKRO-TESHIS RAPORU] ve [ADAPTIF OGRENME BAGLAMI] belirler.
            - Korteks kaynak basliklarini, haber/SEO cumlelerini veya video basliklarini modul/ders basligi olarak kopyalama.
            - [MKRO-TEHS RAPORU] ve [ADAPTIF RENME BALAMI] iindeki zayf noktalar plana "Derinlemesine yiletirme" veya "Pratik Lab" dersleri olarak ekle.
            - Tekrar eden hata paternlerine (Mistake Patterns) ynelik ekstra pekitirme modlleri ner.
            - SRS / Gzden Geirme basks olan becerileri mfredatn bana veya ilgili modllere 'Hzl Tekrar' olarak serpitir.
            - rencinin zaten bildii (baarl olduu) ksmlar mfredatta 'Hzl zet' veya 'Hatrlatma' olarak daralt.
            - Kullanc cmlesinden (rn: "C# almak istiyorum") ASIL KONUYU kar ("C# Programlama"). Kesinlikle "Selamlama", "stek", "Giri" gibi konulardan bahsetme!
            - KALITE TABANI: En az 6 modul, her modulde en az 4 ders ve toplam en az 24 ders uret.
            - Programlama/teknoloji konularinda kalite tabani daha yuksektir: en az 6 modul, her modulde en az 4 ders ve toplam en az 24 ders uret.
            - Programlama planinda ilk modul konu mantigi ve on kosullarla baslamali; Orka IDE/sandbox yalnizca uygun pratik derslerinde destekleyici ortam olarak gecmelidir.
            - Plan cok kisa, jenerik veya iki-uc baslikli olursa sistem tarafindan reddedilecektir.
            - Her konuda ayni generic mimariyi kullan: onkosul -> ana kavram -> uygulama -> yanilgi onarimi -> pratik -> mastery kontrolu.
            - Konuya ozel sabit modul listesi uydurma; basliklari brief, teshis ve concept sinyallerinden turet.
            - Her ders iin o derste llecek ana beceriyi `skillTag` olarak belirle.
            - Her ders iin bir `intent` belirle:
              * "Core": Standart mfredat ak.
              * "DeepDive": [ADAPTIF RENME BALAMI] iindeki bir 'WeakConcept' (Zayf Kavram) iin derinlemesine teorik anlatm.
              * "PracticeLab": Procedural/lemsel hata paternlerine ynelik adm adm uygulama veya kodlama laboratuvar.
              * "QuickReview": SRS basks olan veya daha nce baarsz olunan konularn hzl tekrar.
              * "Remediation": Kavram yanlglarn (Conceptual) dzeltmek iin zel olarak tasarlanm telafi dersi.
              * "Assessment": Modl sonu veya kritik kavram sonras lme-deerlendirme (kk bir snav gibi).

            IKTI KURALI (KESNLKLE UYULACAK):
            SADECE aadaki JSON formatn dndr. Markdown veya aklama EKLEME.
            {
              "modules": [
                {
                  "title": "Modl Bal",
                  "emoji": "",
                  "lessons": [
                    { "title": "Ders Bal 1", "skillTag": "beceri-etiketi-1", "intent": "Core" },
                    { "title": "Ders Bal 2", "skillTag": "beceri-etiketi-2", "intent": "DeepDive" },
                    { "title": "Ders Bal 3", "skillTag": "beceri-etiketi-3", "intent": "PracticeLab" }
                  ]
                }
              ]
            }
            DL: Trke.
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

        _logger.LogError("[DeepPlan] Plan kalite kapisi tum denemelerde basarisiz. Kapsamli fallback uygulanyor.");
        setGrounding?.Invoke(groundingMetadata);
        return BuildQualityFallbackModules(topicTitle);
    }

    private static string BuildConceptGraphPlanningGuidance() => """

            [GENERIC CONCEPT GRAPH PLANLAMA KURALI]
            - Konuya ozel sabit rota, konu template'i veya ezber modul listesi kullanma.
            - Plan sirasi: onkosul -> ana kavram -> uygulama -> yanilgi onarimi -> pratik -> mastery kontrolu.
            - Zayif kavramlar remediation/practice olarak derinlestirilir; bilinen kavramlar hizli tekrar olur.
            - Basliklari kaynak basliklarindan kopyalama; concept graph, teshis ve learner state sinyallerinden turet.
            """;

    private static List<ModuleDefinition> BuildQualityFallbackModules(string topicTitle)
    {
        var title = string.IsNullOrWhiteSpace(topicTitle) ? "Konu" : topicTitle.Trim();
        return BuildConceptGraphFallbackModules(title);
    }

    private static List<ModuleDefinition> BuildConceptGraphFallbackModules(string title)
    {
        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? "Konu" : title.Trim();
        return
        [
            new($"{normalizedTitle} Onkosul Haritasi", "", new List<LessonDefinition>
            {
                new("Baslangic kavramlari ve kelime haritasi", "prerequisite", "Core"),
                new("Once bilinmesi gereken ayrimlar", "prerequisite-check", "Core"),
                new("Ilk kavram iliskileri", "concept-link", "Core"),
                new("Kisa on bilgi kontrolu", "readiness-check", "Assessment")
            }),
            new("Ana Kavram Omurgasi", "", new List<LessonDefinition>
            {
                new("Ana kavrami sade tanimlama", "core-concept", "Core"),
                new("Kavramin neden gerekli oldugu", "concept-reasoning", "Core"),
                new("Benzer kavramlardan ayirma", "contrast", "Core"),
                new("Mikro kontrol sorusu", "micro-check", "Assessment")
            }),
            new("Uygulama ve Ornekleme", "", new List<LessonDefinition>
            {
                new("Cozumlu mini ornek", "worked-example", "PracticeLab"),
                new("Adim adim uygulama", "guided-practice", "PracticeLab"),
                new("Kisit ve ipucu okuma", "evidence-reading", "PracticeLab"),
                new("Geri bildirimli deneme", "feedback-loop", "PracticeLab")
            }),
            new("Yanilgi Onarimi", "", new List<LessonDefinition>
            {
                new("Sik karistirilan nokta", "misconception", "Remediation"),
                new("Yanlis ornek uzerinden duzeltme", "error-repair", "Remediation"),
                new("Karsilastirma tablosu", "comparison", "Remediation"),
                new("Telafi mini quiz", "remediation-check", "Assessment")
            }),
            new("Karma Pratik", "", new List<LessonDefinition>
            {
                new("Kolaydan ortaya uygulama", "mixed-practice-easy", "PracticeLab"),
                new("Orta seviye karar sorulari", "mixed-practice-core", "PracticeLab"),
                new("Zorlayici senaryo", "challenge", "DeepDive"),
                new("Hata gunlugu ve tekrar", "review", "QuickReview")
            }),
            new("Mastery Kontrolu ve Sonraki Rota", "", new List<LessonDefinition>
            {
                new("Kavram bazli final kontrol", "mastery-check", "Assessment"),
                new("Eksik kalan kavramlar", "weakness-review", "QuickReview"),
                new("Kişisel pratik planı", "adaptive-plan", "Core"),
                new("Sonraki konu baglantisi", "next-step", "Core")
            })
        ];
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

            _logger.LogInformation("[DeepPlan] YouTube pedagojik referans aranyor: {Topic}", topicTitle);

            var searchResult = await youtubePlugin.SearchYouTubeVideos(topicTitle);
            if (searchResult.Contains("bulunamad", StringComparison.OrdinalIgnoreCase) ||
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
            _logger.LogWarning(ex, "[DeepPlan] YouTube referans ekme baarsz.");
            return string.Empty;
        }
    }

    /// <summary>LLM ktsn modl/ders yapsna parse eder. Baarsz olursa null dndrr.</summary>
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
                    var title = mod.GetProperty("title").GetString() ?? "Modl";
                    var emoji = mod.TryGetProperty("emoji", out var emojiProp) ? emojiProp.GetString() ?? "" : "";

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
                    else if (mod.TryGetProperty("topics", out var topicsProp)) // Geriye dnk uyumluluk
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
        catch { /* yoksay, null dnecek */ }

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
                Emoji = string.IsNullOrWhiteSpace(module.Emoji) ? "" : module.Emoji,
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

        var isProgramming = IsProgrammingTopic(topicTitle);
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
            "c#", "c sharp", "csharp", ".net", "dotnet", "asp.net", "programlama", "programming", "yazilim", "yazlm",
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
            tracedModules.Add(new("Kişisel Telafi ve Pratik", "target", []));
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

    /// <summary>3 seviyeli Topic hiyerarisi: Ana Konu  Modl  Ders. Her ders iin WikiPage oluturur.</summary>
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

            // Modl topic'i (2. seviye)
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

                // Her ders iin WikiPage
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

        _logger.LogInformation("[DeepPlan] {ModuleCount} modl, {LessonCount} ders oluturuldu.",
            modules.Count, totalLessons);
        return allLessonTopics;
    }

    /// <summary>Modl tanm: balk, emoji ve altndaki dersler.</summary>
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

            // 1. Baseline Quiz sonularn getir (en son 20 soru)
            var attempts = await db.QuizAttempts
                .Where(a => a.TopicId == topicId && a.UserId == userId)
                .OrderByDescending(a => a.CreatedAt)
                .Take(20)
                .ToListAsync();

            if (!attempts.Any()) return string.Empty;

            // 2. Yanl cevaplara odaklan
            var failedAttempts = attempts.Where(a => !a.IsCorrect).ToList();
            if (!failedAttempts.Any())
                return "renci baseline quiz'de kusursuz performans gsterdi. Mfredat ileri dzey, teknik detaylara bomadan, vizyoner ve hzl akl bir yapda hazrla. Temel kavramlar atla.";

            var failedSummary = string.Join("\n", failedAttempts.Select(a =>
                $"- Soru: {a.Question}\n  Beceri: {a.SkillTag}\n  Hata Analizi: {a.Explanation}"));

            // 3. LLM ile Mikro-Tehis yap
            var systemPrompt = """
                Sen akademik dzeyde bir 'Eitim Tehis Uzman (Diagnostic Educator)' botusun.
                Grevin: rencinin baseline quiz hatalarn analiz ederek, zihinsel modelindeki eksik paralar (micro-gaps) tespit etmek.

                ANALZ KURALI:
                - "u soruyu yanl yapt" deme. "Kullanc X'i biliyor ama Y kavramnn Z zerindeki etkisini yanl yorumluyor" gibi derinlikli konu.
                - Mfredat mimarna "u 3 kritik noktaya matkapla (drill) inmeli, u analojileri kullanmalsn" eklinde direktif ver.
                """;

            var userPrompt = $"renci Hata Raporu:\n{failedSummary}\n\nLtfen tehisini ve mfredat direktiflerini Trke olarak hazrla.";

            return await _factory.CompleteChatAsync(AgentRole.DeepPlan, systemPrompt, userPrompt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DeepPlan] Baseline analiz aamas baarsz oldu.");
            return string.Empty;
        }
    }

    public async Task<string> GenerateBaselineQuizAsync(string topicTitle)
    {
        _logger.LogInformation("[DeepPlan] Baseline Quiz iin Korteks kefi balatlyor: {Topic}", topicTitle);

        // 1. Korteks Kefi (Quiz sorularnn gerek dnya verilerine dayanmas iin)
        string compressedResearchPromptBlock;
        try
        {
            var korteksResult = await _korteks.RunResearchWithEvidenceAsync(topicTitle, Guid.Empty, null);
            var compressedResearch = _planResearchCompressor.Compress(korteksResult);
            compressedResearchPromptBlock = _planResearchCompressor.BuildPromptBlock(compressedResearch);
            _logger.LogInformation(
                "[DeepPlan] Baseline Quiz Korteks balam sktrld. Mode={GroundingMode} Sources={SourceCount} BlockLen={Len}",
                compressedResearch.GroundingMode,
                compressedResearch.SourceCount,
                compressedResearchPromptBlock.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DeepPlan] Baseline Quiz Korteks aratrmas baarsz oldu. Tanlama kurallaryla devam edilecek.");
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
            Sen profesyonel bir 'Eitim Tanlama Uzman (Educational Diagnostician)' botusun.
            Grevin: Kullancnn '{{topicTitle}}' konusundaki gerek bilgi seviyesini EN NCE AYRINTISINA KADAR tespit etmek iin 20 soru hazrlamak.

            {{quizIntelligenceBrief}}

            Eger GroundingMode FallbackInternalKnowledge veya BlockedProvider ise bu baglami guncel/kaynakli kanit gibi sunma.
            Bu durumda sorulari konu basligi, genel pedagojik tanilama kurallari ve temel mufredat kapsami ile uret.

            SORU DAILIMI VE DERNLK (Toplam 20 Soru):
            - 1-4: TEMEL KAVRAMLAR (Balang seviyesi, terminoloji kontrol)
            - 5-10: UYGULAMA VE SENARYO (Orta seviye, "nasl yaplr?" ve kod okuma)
            - 11-16: ANALZ VE PROBLEM ZME (leri seviye, hata ayklama ve mimari kararlar)
            - 17-20: UZMANLIK VE DERN KONULAR (Zorlayc, u durumlar ve optimizasyon)

            KALTE KURALLARI:
            - Sorulari sadece yukaridaki sikistirilmis arastirma baglami ve konu basligiyla destekle; ham Korteks raporu varsayma.
            - Genis bir kavram haritasini kapsa: temel kavram, onkosul, uygulama, analiz, hata ayiklama ve uc durumlar.
            - Soru tipleri tekrarlamasin: conceptual, procedural, application, analysis ve misconception_probe karisik kullanilsin.
            - Zorluk dagilimi dengeli olsun: kolay, orta ve zor sorulari acikca karistir.
            - Soru metinleri birbirinin kopyasi veya yakin tekrari olmasin.
            - En az 8 farkli conceptTag ve en az 4 farkli questionType kullan.
            - En az 5 soru yaygin kavram yanilgisini veya beklenen hata kategorisini hedeflesin.
            - Programlama/teknik konularda en az bir soru gercek kod parcasi, kod okuma veya hata ayiklama tarzi icersin.
            - Sadece "X nedir?" gibi ezber sorular YASAKTIR. Sorular gerek hayat problemlerini veya teknik senaryolar yanstmaldr.
            - Yanl seenekler (eldiriciler) mantkl ve kafa kartrc olmal.
            - Her soru icin uygun bir `skillTag`, `conceptTag`, `learningObjective`, `questionType` ve `expectedMisconceptionCategory` belirle.

            IKTI FORMATI (SADECE JSON):
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
                "learningObjective": "Ölçülen öğrenme hedefi",
                "questionType": "conceptual|procedural|application|analysis|misconception_probe",
                "expectedMisconceptionCategory": "Conceptual|Procedural|Calculation|Reading|Application|MisreadQuestion|Careless",
                "topic": "{{topicTitle}}"
              }
            ]

            DL: Trke.
            """;

        var rawQuiz = await _factory.CompleteChatAsync(AgentRole.DeepPlan, systemPrompt, $"Konu: \"{topicTitle}\" iin 20 adet derinlikli baseline sorusu ret.");
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
        sb.AppendLine("[ADAPTIF RENME BALAMI - STRKTREL VER]");
        sb.AppendLine($"- renci Seviyesi: {context.UserLevel}");

        if (context.QuizSummary != null && context.QuizSummary.TotalAttempts > 0)
        {
            sb.AppendLine($"- Son Performans: {context.QuizSummary.TotalAttempts} denemede %{Math.Round(context.QuizSummary.AverageAccuracy * 100)} baar.");
        }

        if (context.WeakSkills.Any())
        {
            sb.AppendLine("- Tespit Edilen Zayf Beceriler (SkillTags):");
            foreach (var s in context.WeakSkills.Take(5))
                sb.AppendLine($"  * {s.Skill} (Baar: %{Math.Round(s.Accuracy * 100)}, Hata: {s.WrongCount})");
        }

        if (context.WeakConcepts.Any())
        {
            sb.AppendLine("- Tekrarlayan Kavramsal Zayflklar (ConceptTags):");
            foreach (var c in context.WeakConcepts)
                sb.AppendLine($"  * {c.Concept} ({c.Frequency} kez hata yapld)");
        }

        if (context.MistakePatterns.Any())
        {
            sb.AppendLine("- Hata Pattern Analizi:");
            foreach (var p in context.MistakePatterns)
                sb.AppendLine($"  * {p.Label} ({p.Frequency} kez)");
        }

        if (context.DueReviewSkills.Any())
        {
            sb.AppendLine("- SRS Gzden Geirme Basks (Due for Review):");
            sb.AppendLine($"  * {string.Join(", ", context.DueReviewSkills)}");
        }

        if (!string.IsNullOrEmpty(context.StudentProfileSummary))
        {
            sb.AppendLine($"- renci Profil zeti: {context.StudentProfileSummary}");
        }

        if (!string.IsNullOrEmpty(context.PreviousSessionSummary))
        {
            sb.AppendLine($"- nceki Oturum zeti: {context.PreviousSessionSummary}");
        }

        sb.AppendLine("\nTALMAT: Mfredat hazrlarken yukardaki [ADAPTIF RENME BALAMI] verilerini temel al. Zayf kavramlara 'Deep Dive' veya 'Remedial' dersleri ekle, hata patternlerine uygun analojiler ner ve SRS basks olan konular pekitir.");

        return sb.ToString();
    }
}
