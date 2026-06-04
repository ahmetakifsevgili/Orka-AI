using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orka.Core.Entities;
using Orka.Core.Enums;
using Orka.Core.Interfaces;
using Orka.Infrastructure.Data;
using Orka.Infrastructure.Utilities;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Globalization;
using Orka.Core.DTOs;
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
            diagnosticQuizSummary: diagnosticQuizSummary,
            allowConceptGraphFallback: false);
        var diagnostic = DiagnosticWeaknessSummary.Parse(diagnosticQuizSummary);
        modules = EnsureBasePlanQualityBeforeSave(modules, topicTitle, diagnostic);
        modules = ApplyDiagnosticTraceability(modules, diagnostic);
        var topics = await SaveModularSubTopicsAsync(parentTopicId, modules, userId);
        return new DeepPlanGenerationWithGroundingResultDto { Topics = topics, Grounding = grounding };
    }

    private async Task<List<ModuleDefinition>> GenerateModulesAsync(Guid parentTopicId, string topicTitle, Guid userId, string userLevel, string? researchContext = null, string? failedTopics = null, Action<DeepPlanGroundingMetadataDto?>? setGrounding = null, string? precompressedResearchPromptBlock = null, string? diagnosticQuizSummary = null, bool allowConceptGraphFallback = true)
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
                _logger.LogWarning(
                    "[DeepPlan] Korteks aratrmas baarsz oldu. Planlama mevcut bilgilerle devam edecek. ErrorType={ErrorType}",
                    LogPrivacyGuard.SafeExceptionType(ex));
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
            Sen akademik seviyede bir Curriculum Architect botusun.
            Gorev: Verilen konuyu profesyonel, kapsamli ve konunun dogasina uygun bir mufredata donusturmek.
            Mevcut kullanicinin bilgi seviyesi: {{userLevel}}
            Konunun Alan / Kategorisi: {{intentCategory}}
            {{contextInfo}}

            [MIKRO-TESHIS RAPORU - OGRENCININ ZAYIFLIKLARI]:
            {{baselineDiagnostic}}

            {{adaptivePromptSection}}

            {{failedTopicsDiagnostic}}
            {{youtubeReference}}
            {{graphPlanningGuidance}}

            ORGANIZASYON KURALI (TESHIS ODAKLI MIMAR):
            - [PLAN INTELLIGENCE BRIEF - LEARNING RESEARCH FILTERED] icindeki arastirma bulgularini yalnizca konu kapsami, guncellik ve onkosul destegi olarak kullan; plan omurgasini concept graph sinyali, [MIKRO-TESHIS RAPORU] ve [ADAPTIF OGRENME BAGLAMI] belirler.
            - Korteks kaynak basliklarini, haber/SEO cumlelerini veya video basliklarini modul/ders basligi olarak kopyalama.
            - Zayif noktalar varsa plana erken repair/review dersleri olarak yerlestir.
            - Her konuda ayni generic mimariyi kullanma; modul basliklari konuya ozel concept cluster, prerequisite iliskisi ve teshis sinyalinden turemelidir.
            - Onkosul, uygulama, pratik ve mastery kontrolu ogrenme islevleridir; bunlari tek basina sabit modul basligi olarak kullanma.
            - KALITE TABANI: En az 6 modul, her modulde en az 4 ders ve toplam en az 24 ders uret.
            - Orka IDE/sandbox yalnizca uygun pratik derslerinde destek ortami olarak kullanilir; plan omurgasi concept graph ve prerequisite iliskisinden turemelidir.
            - Her ders icin konuya ozel, olculebilir `skillTag` ve `learningObjective` yaz. `genel-kavram`, `intro`, `basics` gibi bos/generic degerler kullanma.
            - Her ders icin `prerequisiteConceptKeys`, `quizHook`, `tutorHook` ve `successCriteria` alanlarini doldur.
            - Her ders icin bir `intent` belirle: Core, DeepDive, PracticeLab, QuickReview, Remediation veya Assessment.

            CIKTI KURALI (KESINLIKLE UYULACAK):
            SADECE asagidaki JSON formatini dondur. Markdown veya aciklama EKLEME.
            {
              "modules": [
                {
                  "title": "Modul Basligi",
                  "emoji": "",
                  "lessons": [
                    {
                      "title": "Ders Basligi 1",
                      "skillTag": "konuya-ozel-concept-key",
                      "learningObjective": "Olculebilir ders hedefi",
                      "prerequisiteConceptKeys": ["gerekirse-onkosul-key"],
                      "intent": "Core",
                      "quizHook": "diagnostic_check|retrieval_practice|misconception_probe|micro_quiz",
                      "tutorHook": "explain_then_check|worked_example_then_micro_check|misconception_repair",
                      "successCriteria": ["Ogrenci bu kavrami ... ile kanitlar."]
                    }
                  ]
                }
              ]
            }
            DIL: Turkce.
            """;

        _logger.LogInformation("[DeepPlan] AIAgentFactory tetikleniyor. Model: {Model}, Seviye: {Level}",
            _factory.GetModel(AgentRole.DeepPlan), userLevel);

        var lastRejectionReason = "not_attempted";
        var lastRawPreview = string.Empty;
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var raw = await _factory.CompleteChatAsync(
                    AgentRole.DeepPlan,
                    systemPrompt,
                    BuildPlanGenerationUserPrompt(topicTitle, attempt, lastRejectionReason, lastRawPreview));
                var parsedModules = ParseModuleStructure(raw);
                if (TryAcceptPlanModules(parsedModules, topicTitle, out var acceptedModules, out var rejectionReason))
                {
                    setGrounding?.Invoke(groundingMetadata);
                    return acceptedModules;
                }
                lastRejectionReason = rejectionReason;
                lastRawPreview = BuildSafeRawPreview(raw);
                _logger.LogWarning(
                    "[DeepPlan] Plan kalite/parse reddi (Deneme {Attempt}/3). Reason={Reason}. Raw={Raw}",
                    attempt,
                    rejectionReason,
                    raw.Length > 200 ? raw[..200] + "..." : raw);
            }
            catch (Exception ex)
            {
                lastRejectionReason = $"provider_exception:{LogPrivacyGuard.SafeExceptionType(ex)}";
                lastRawPreview = string.Empty;
                _logger.LogWarning(
                    "[DeepPlan] Plan üretimi sırasında sağlayıcı hatası (Deneme {Attempt}/3). Hata={Error}",
                    attempt,
                    ex.Message);
            }
        }

        if (!allowConceptGraphFallback)
        {
            setGrounding?.Invoke(groundingMetadata);
            throw new InvalidOperationException($"DeepPlan provider did not produce a valid professional plan contract. Last rejection: {lastRejectionReason}");
        }

        _logger.LogWarning("[DeepPlan] Plan kalite kapisi tum provider denemelerinde basarisiz. Concept-graph fallback deneniyor.");
        var fallbackModules = BuildQualityFallbackModules(topicTitle);
        if (TryAcceptPlanModules(fallbackModules, topicTitle, out var acceptedFallback, out var fallbackRejection))
        {
            setGrounding?.Invoke(groundingMetadata);
            return acceptedFallback;
        }

        _logger.LogError("[DeepPlan] Plan kalite kapisi tum denemelerde basarisiz. Fallback de reddedildi. Reason={Reason}", fallbackRejection);
        setGrounding?.Invoke(groundingMetadata);
        throw new InvalidOperationException($"DeepPlan provider did not produce a valid professional plan contract. Last rejection: {lastRejectionReason}");
    }

    private static string BuildPlanGenerationUserPrompt(
        string topicTitle,
        int attempt,
        string lastRejectionReason,
        string lastRawPreview)
    {
        if (attempt <= 1)
        {
            return $"Konu: \"{topicTitle}\"";
        }

        var previousOutput = string.IsNullOrWhiteSpace(lastRawPreview)
            ? "Onceki cikti kullanilabilir degil veya provider hatasi olustu."
            : lastRawPreview;

        return $$"""
            Konu: "{{topicTitle}}"

            Onceki plan contract kalite kapisindan gecmedi.
            Red sebebi: {{lastRejectionReason}}

            Onceki cikti ozeti:
            {{previousOutput}}

            Simdi ayni konu icin sadece gecerli JSON dondur:
            - root property `modules` olacak.
            - en az 6 module olacak.
            - her module en az 4 lesson tasiyacak.
            - toplam en az 24 lesson olacak.
            - her lesson icin title, skillTag, learningObjective, prerequisiteConceptKeys, intent, quizHook, tutorHook, successCriteria dolu olacak.
            - skillTag ve module basliklari konuya ozel olacak; generic placeholder kullanma.
            - zayif concept varsa ilk 12 lesson icinde repair/review veya misconception_repair olarak yer alacak.
            - Markdown, aciklama veya ekstra metin yazma.
            """;
    }

    private static List<ModuleDefinition> BuildConceptGraphFallbackModules(string topicTitle)
    {
        var subject = string.IsNullOrWhiteSpace(topicTitle) ? "konu" : topicTitle.Trim();
        var subjectKey = NormalizePlanKey(subject);
        if (string.IsNullOrWhiteSpace(subjectKey)) subjectKey = "konu";

        var moduleSpecs = new[]
        {
            ("Onkosul Haritasi", "map", "prerequisite"),
            ("Ana Kavram Omurgasi", "network", "core"),
            ("Ornekleme ve Uygulama", "lab", "practice"),
            ("Yanilgi Onarimi", "repair", "remediation"),
            ("Karma Pratik ve Transfer", "target", "transfer"),
            ("Mastery Kontrolu", "check", "assessment")
        };

        var lessonSpecs = new[]
        {
            ("Kavrami tanimla", "Core", "retrieval_practice", "explain_then_check"),
            ("Ornek uzerinde ayirt et", "DeepDive", "misconception_probe", "worked_example_then_micro_check"),
            ("Mikro pratik yap", "PracticeLab", "micro_quiz", "guided_practice_then_feedback"),
            ("Kaniti ve sonraki adimi yaz", "Assessment", "diagnostic_check", "mastery_check_then_next_step")
        };

        return moduleSpecs
            .Select((module, moduleIndex) =>
            {
                var moduleKey = $"{subjectKey}-{module.Item3}";
                var lessons = lessonSpecs
                    .Select((lesson, lessonIndex) =>
                    {
                        var lessonKey = $"{moduleKey}-{lessonIndex + 1}";
                        return new LessonDefinition(
                            $"{subject} - {module.Item1}: {lesson.Item1}",
                            lessonKey,
                            lesson.Item2,
                            PhaseMetadata: SerializePlanPhaseMetadata(
                                moduleIndex + 1,
                                lessonIndex + 1,
                                module.Item3,
                                lesson.Item2,
                                new[] { moduleKey }),
                            LearningObjective: $"{subject} icin {module.Item1} kapsaminda {lesson.Item1.ToLowerInvariant()} becerisini kanitlar.",
                            PrerequisiteConceptKeys: lessonIndex == 0 ? [] : new[] { $"{moduleKey}-1" },
                            QuizHook: lesson.Item3,
                            TutorHook: lesson.Item4,
                            SuccessCriteria:
                            [
                                $"{subject} baglaminda {module.Item1} kavramini kendi cumleleriyle aciklar.",
                                $"{subject} icin {lesson.Item1.ToLowerInvariant()} adimini kisa bir kontrolle kanitlar."
                            ]);
                    })
                    .ToList();

                return new ModuleDefinition($"{subject} {module.Item1}", module.Item2, lessons);
            })
            .ToList();
    }

    private static List<ModuleDefinition> BuildQualityFallbackModules(string topicTitle) =>
        BuildConceptGraphFallbackModules(topicTitle);

    private static string SerializePlanPhaseMetadata(
        int moduleOrder,
        int lessonOrder,
        string conceptCluster,
        string intent,
        IReadOnlyList<string> prerequisiteConceptKeys) =>
        JsonSerializer.Serialize(new
        {
            moduleOrder,
            lessonOrder,
            conceptCluster,
            intent,
            sequenceReason = "concept_graph_fallback",
            quizHook = "diagnostic_check",
            tutorHook = "explain_then_check",
            prerequisiteConceptKeys
        });

    private static string BuildSafeRawPreview(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var compact = raw.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        while (compact.Contains("  ", StringComparison.Ordinal))
        {
            compact = compact.Replace("  ", " ", StringComparison.Ordinal);
        }

        return compact.Length <= 1600 ? compact : compact[..1600];
    }

    private static string BuildConceptGraphPlanningGuidance() => """

            [GENERIC CONCEPT GRAPH PLANLAMA KURALI]
            - Konuya ozel sabit rota, konu template'i veya ezber modul listesi kullanma.
            - Plan sirasi: onkosul -> ana kavram -> uygulama -> yanilgi onarimi -> pratik -> mastery kontrolu.
            - Zayif kavramlar remediation/practice olarak derinlestirilir; bilinen kavramlar hizli tekrar olur.
            - Basliklari kaynak basliklarindan kopyalama; concept graph, teshis ve learner state sinyallerinden turet.
            """;

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
            _logger.LogWarning(
                "[DeepPlan] YouTube referans ekme baarsz. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
            return string.Empty;
        }
    }

    /// <summary>LLM ktsn modl/ders yapsna parse eder. Baarsz olursa null dndrr.</summary>
    private static List<ModuleDefinition>? ParseModuleStructure(string raw)
    {
        try
        {
            var cleaned = ExtractJsonPayload(raw);
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                using var doc = JsonDocument.Parse(cleaned);
                if (!TryGetModuleArray(doc.RootElement, out var modulesArray))
                {
                    return null;
                }

                var modules = new List<ModuleDefinition>();
                foreach (var mod in modulesArray.EnumerateArray())
                {
                    if (mod.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var title = FirstJsonString(mod, "title", "moduleTitle", "name", "heading") ?? "Modul";
                    var emoji = mod.TryGetProperty("emoji", out var emojiProp) ? emojiProp.GetString() ?? "" : "";

                    var lessons = new List<LessonDefinition>();
                    if (FirstJsonArray(mod, out var lessonsProp, "lessons", "lessonSteps", "steps", "items", "subtopics", "topics"))
                    {
                        foreach (var l in lessonsProp.EnumerateArray())
                        {
                            if (l.ValueKind == JsonValueKind.String)
                            {
                                var titleText = l.GetString();
                                if (!string.IsNullOrWhiteSpace(titleText))
                                {
                                    lessons.Add(new LessonDefinition(titleText, NormalizePlanKey(titleText), "Core"));
                                }
                                continue;
                            }

                            if (l.ValueKind != JsonValueKind.Object)
                            {
                                continue;
                            }

                            var lTitle = FirstJsonString(l, "title", "lessonTitle", "name", "heading");
                            var lSkill = FirstJsonString(l, "skillTag", "conceptKey", "conceptTag", "skill", "targetConceptKey");
                            var lIntent = FirstJsonString(l, "intent", "lessonIntent", "type", "category") ?? "Core";
                            var lObjective = FirstJsonString(l, "learningObjective", "objective", "measurableOutcome", "outcome");
                            var lPrerequisites = ReadFirstJsonStringArray(l, "prerequisiteConceptKeys", "prerequisites", "requiredConcepts");
                            var lQuizHook = FirstJsonString(l, "quizHook", "assessmentHook", "diagnosticHook");
                            var lTutorHook = FirstJsonString(l, "tutorHook", "teachingHook", "tutorMove");
                            var lSuccessCriteria = ReadFirstJsonStringArray(l, "successCriteria", "successCriteriaItems", "masteryCriteria");

                            if (!string.IsNullOrWhiteSpace(lTitle))
                                lessons.Add(new LessonDefinition(
                                    lTitle,
                                    lSkill ?? NormalizePlanKey(lTitle),
                                    lIntent ?? "Core",
                                    null,
                                    lObjective,
                                    lPrerequisites,
                                    lQuizHook,
                                    lTutorHook,
                                    lSuccessCriteria));
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

    private static string? FirstJsonString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            if (value.ValueKind == JsonValueKind.Object &&
                value.TryGetProperty("hookType", out var hookType) &&
                hookType.ValueKind == JsonValueKind.String)
            {
                return hookType.GetString();
            }

            if (value.ValueKind == JsonValueKind.Object &&
                value.TryGetProperty("tutorMove", out var tutorMove) &&
                tutorMove.ValueKind == JsonValueKind.String)
            {
                return tutorMove.GetString();
            }
        }

        return null;
    }

    private static string? ExtractJsonPayload(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var objectStart = raw.IndexOf('{');
        var objectEnd = raw.LastIndexOf('}');
        var arrayStart = raw.IndexOf('[');
        var arrayEnd = raw.LastIndexOf(']');

        if (objectStart >= 0 && objectEnd > objectStart &&
            (arrayStart < 0 || objectStart < arrayStart))
        {
            return raw[objectStart..(objectEnd + 1)];
        }

        if (arrayStart >= 0 && arrayEnd > arrayStart)
        {
            return raw[arrayStart..(arrayEnd + 1)];
        }

        return null;
    }

    private static bool TryGetModuleArray(JsonElement root, out JsonElement modulesArray)
    {
        modulesArray = default;
        if (root.ValueKind == JsonValueKind.Array)
        {
            modulesArray = root;
            return true;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (FirstJsonArray(root, out modulesArray, "modules", "chapters", "units", "sections"))
        {
            return true;
        }

        foreach (var wrapperName in new[] { "plan", "curriculum", "coursePlan", "professionalPlan" })
        {
            if (root.TryGetProperty(wrapperName, out var wrapper) &&
                wrapper.ValueKind == JsonValueKind.Object &&
                FirstJsonArray(wrapper, out modulesArray, "modules", "chapters", "units", "sections"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool FirstJsonArray(JsonElement element, out JsonElement array, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var value) &&
                value.ValueKind == JsonValueKind.Array)
            {
                array = value;
                return true;
            }
        }

        array = default;
        return false;
    }

    private static string[] ReadJsonStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
    }

    private static string[] ReadFirstJsonStringArray(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var values = ReadJsonStringArray(element, propertyName);
            if (values.Length > 0)
            {
                return values;
            }
        }

        return [];
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
                        Intent = string.IsNullOrWhiteSpace(lesson.Intent) ? "Core" : lesson.Intent.Trim(),
                        LearningObjective = string.IsNullOrWhiteSpace(lesson.LearningObjective) ? null : lesson.LearningObjective.Trim(),
                        QuizHook = string.IsNullOrWhiteSpace(lesson.QuizHook) ? null : lesson.QuizHook.Trim(),
                        TutorHook = string.IsNullOrWhiteSpace(lesson.TutorHook) ? null : lesson.TutorHook.Trim(),
                        PrerequisiteConceptKeys = (lesson.PrerequisiteConceptKeys ?? [])
                            .Where(item => !string.IsNullOrWhiteSpace(item))
                            .Select(item => item.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(8)
                            .ToArray(),
                        SuccessCriteria = (lesson.SuccessCriteria ?? [])
                            .Where(item => !string.IsNullOrWhiteSpace(item))
                            .Select(item => item.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(6)
                            .ToArray()
                    })
                    .ToList()
            })
            .Where(module => module.Lessons.Count > 0)
            .ToList();

        cleaned = EnsureDistinctLessonSkillTags(cleaned);

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

        var genericLesson = cleaned.SelectMany(module => module.Lessons).FirstOrDefault(lesson =>
            LooksGenericPlanText(lesson.Title) ||
            LooksGenericSkillTag(lesson.SkillTag));
        if (genericLesson != null)
        {
            rejectionReason = $"generic_lesson_contract:{genericLesson.Title}:{genericLesson.SkillTag}";
            return false;
        }

        var genericModules = cleaned.Count(module => LooksGenericModuleTitle(module.Title, topicTitle));
        if (genericModules > Math.Max(1, cleaned.Count / 4))
        {
            rejectionReason = $"generic_module_titles:{genericModules}/{cleaned.Count}";
            return false;
        }

        var conceptDiversity = cleaned
            .SelectMany(module => module.Lessons)
            .Select(lesson => NormalizePlanKey(lesson.SkillTag))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var minimumConceptDiversity = Math.Min(12, Math.Max(8, minTotalLessons / 3));
        if (conceptDiversity < minimumConceptDiversity)
        {
            rejectionReason = $"concept_diversity_low:{conceptDiversity}/{minimumConceptDiversity}";
            return false;
        }

        acceptedModules = cleaned;
        rejectionReason = "accepted";
        return true;
    }

    private static List<ModuleDefinition> EnsureDistinctLessonSkillTags(List<ModuleDefinition> modules)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        return modules.Select(module => module with
        {
            Lessons = module.Lessons.Select(lesson =>
            {
                var normalized = NormalizePlanKey(lesson.SkillTag);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    normalized = NormalizePlanKey(lesson.Title);
                }

                var count = seen.GetValueOrDefault(normalized);
                seen[normalized] = count + 1;
                if (count == 0)
                {
                    return lesson with { SkillTag = normalized };
                }

                var titleKey = NormalizePlanKey(lesson.Title);
                var refined = string.IsNullOrWhiteSpace(titleKey)
                    ? $"{normalized}-{count + 1}"
                    : $"{normalized}-{titleKey}";

                var suffix = 2;
                var unique = refined;
                while (seen.ContainsKey(unique))
                {
                    unique = $"{refined}-{suffix++}";
                }

                seen[unique] = 1;
                return lesson with { SkillTag = unique };
            }).ToList()
        }).ToList();
    }

    private static bool LooksGenericPlanText(string? value)
    {
        var text = NormalizePlanKey(value);
        return string.IsNullOrWhiteSpace(text) ||
               text is "giris" or "introduction" or "basics" or "temel-bilgiler" or "genel-tekrar" or "konuyu-calis" or "read-notes" ||
               text.StartsWith("ders-basligi", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("lesson-title", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("modul-basligi", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksGenericModuleTitle(string? moduleTitle, string topicTitle)
    {
        var text = NormalizePlanKey(moduleTitle);
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var topicTokens = NormalizePlanKey(topicTitle)
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 3)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var moduleTokens = text
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 3)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var domainOverlap = topicTokens.Count > 0 && moduleTokens.Overlaps(topicTokens);

        var genericOnly = text is
            "fundamentals" or "core-concepts" or "practice" or "advanced" or
            "ana-kavram-omurgasi" or "uygulama-ve-ornekleme" or "yanilgi-onarimi" or
            "karma-pratik" or "mastery-kontrolu-ve-sonraki-rota" or "onkosul-haritasi" ||
            text.StartsWith("module-", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("modul-", StringComparison.OrdinalIgnoreCase);

        return genericOnly && !domainOverlap;
    }

    private static bool LooksGenericSkillTag(string? value)
    {
        var text = NormalizePlanKey(value);
        return string.IsNullOrWhiteSpace(text) ||
               text is "genel-kavram" or "general-concept" or "concept" or "skill" or "topic" or "core" or "default" ||
               text.Contains("no-specific-video", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("video-metadata", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("source-metadata", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("transcript-available", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePlanKey(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        var chars = text.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
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

        throw new InvalidOperationException("Generated plan failed professional plan contract validation before save.");
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
                        $"{label} targeted practice",
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
            return "targeted practice";
        }

        return mistake.Trim() switch
        {
            "Procedural" => "step-by-step practice lab",
            "Application" => "scenario application practice",
            "Reading" => "evidence-reading repair",
            "MisreadQuestion" => "question-reading checkpoint",
            "Conceptual" => "concept repair",
            "Careless" => "attention checkpoint",
            _ => "targeted practice"
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

        if (normalized.Contains("task result is the safest way", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Task Result", StringComparison.OrdinalIgnoreCase))
        {
            return "Task.Result blocking misconception";
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
        if (modules == null || !modules.Any())
        {
            throw new InvalidOperationException("deep_plan_modules_empty");
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrkaDbContext>();

        var parent = await db.Topics.FindAsync(parentTopicId);
        if (parent == null)
        {
            throw new InvalidOperationException("deep_plan_parent_topic_missing");
        }

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
                    MetadataJson    = BuildLessonContractMetadata(mod.Title, mi, lessonDef, li),
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

        if (allLessonTopics.Count == 0)
        {
            throw new InvalidOperationException("deep_plan_lessons_empty_after_save");
        }

        _logger.LogInformation("[DeepPlan] {ModuleCount} modl, {LessonCount} ders oluturuldu.",
            modules.Count, totalLessons);
        return allLessonTopics;
    }

    private static string BuildLessonContractMetadata(string moduleTitle, int moduleOrder, LessonDefinition lesson, int lessonOrder)
    {
        var conceptKey = NormalizePlanKey(lesson.SkillTag);
        if (string.IsNullOrWhiteSpace(conceptKey))
        {
            conceptKey = NormalizePlanKey(lesson.Title);
        }

        return JsonSerializer.Serialize(new
        {
            contractVersion = "professional-plan-v1",
            source = "deep_plan",
            moduleTitle,
            moduleOrder,
            lessonOrder,
            skillTag = lesson.SkillTag,
            conceptKey,
            learningObjective = string.IsNullOrWhiteSpace(lesson.LearningObjective)
                ? $"{lesson.Title} hedefini aciklayip yeni bir mini senaryoda uygulamak."
                : lesson.LearningObjective,
            sequenceReason = $"{moduleTitle} modulu icinde {lesson.Title} adimi prerequisite, kavram, uygulama ve kontrol sirasina gore yerlestirildi.",
            prerequisiteConceptKeys = lesson.PrerequisiteConceptKeys ?? [],
            quizHook = new
            {
                hookType = string.IsNullOrWhiteSpace(lesson.QuizHook)
                    ? lesson.Intent == "Assessment" ? "micro_quiz" : "retrieval_practice"
                    : lesson.QuizHook,
                conceptKey,
                difficultyBand = lesson.Intent == "DeepDive" ? "advanced" : "core"
            },
            tutorHook = new
            {
                tutorMove = !string.IsNullOrWhiteSpace(lesson.TutorHook)
                    ? lesson.TutorHook
                    : lesson.Intent switch
                {
                    "Remediation" => "misconception_repair",
                    "PracticeLab" => "worked_example_then_micro_check",
                    "DeepDive" => "scaffolded_explanation",
                    "Assessment" => "micro_check",
                    _ => "explain_then_check"
                },
                activeConceptKey = conceptKey
            },
            successCriteria = (lesson.SuccessCriteria?.Length ?? 0) > 0
                ? lesson.SuccessCriteria
                : new[]
            {
                $"{lesson.Title} kavramini kendi cumleleriyle aciklar.",
                $"{lesson.Title} icin bir mini uygulama veya kontrol sorusunu cevaplar."
            }
        });
    }

    /// <summary>Modl tanm: balk, emoji ve altndaki dersler.</summary>
    private record ModuleDefinition(string Title, string Emoji, List<LessonDefinition> Lessons);
    private record LessonDefinition(
        string Title,
        string SkillTag,
        string Intent = "Core",
        string? PhaseMetadata = null,
        string? LearningObjective = null,
        string[]? PrerequisiteConceptKeys = null,
        string? QuizHook = null,
        string? TutorHook = null,
        string[]? SuccessCriteria = null);

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
            _logger.LogWarning(
                "[DeepPlan] Baseline analiz aamas baarsz oldu. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
            return string.Empty;
        }
    }

    public async Task<string> GenerateBaselineQuizAsync(string topicTitle, Guid topicId, string language, int questionCount)
    {
        _logger.LogInformation("[DeepPlan] Baseline Quiz iin Korteks kefi balatlyor: {Topic} TopicId={TopicId} Language={Language} Count={Count}", topicTitle, topicId, language, questionCount);

        // 1. Korteks Kefi (Quiz sorularnn gerek dnya verilerine dayanmas iin)
        string compressedResearchPromptBlock;
        try
        {
            var korteksResult = await _korteks.RunResearchWithEvidenceAsync(topicTitle, topicId, null);
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
            _logger.LogWarning(
                "[DeepPlan] Baseline Quiz Korteks aratrmas baarsz oldu. Tanlama kurallaryla devam edilecek. ErrorType={ErrorType}",
                LogPrivacyGuard.SafeExceptionType(ex));
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

        var scale1 = (int)Math.Max(1, Math.Round(questionCount * 0.20));
        var scale2 = (int)Math.Max(1, Math.Round(questionCount * 0.30));
        var scale3 = (int)Math.Max(1, Math.Round(questionCount * 0.30));
        var scale4 = Math.Max(1, questionCount - (scale1 + scale2 + scale3));
        var totalScale = scale1 + scale2 + scale3 + scale4;

        var systemPrompt = $$"""
            Sen profesyonel bir 'Eitim Tanlama Uzman (Educational Diagnostician)' botusun.
            Grevin: Kullancnn '{{topicTitle}}' konusundaki gerek bilgi seviyesini EN NCE AYRINTISINA KADAR tespit etmek iin {{totalScale}} soru hazrlamak.

            {{quizIntelligenceBrief}}

            Eger GroundingMode FallbackInternalKnowledge veya BlockedProvider ise bu baglami guncel/kaynakli kanit gibi sunma.
            Bu durumda sorulari konu basligi, genel pedagojik tanilama kurallari ve temel mufredat kapsami ile uret.

            SORU DAILIMI VE DERNLK (Toplam {{totalScale}} Soru):
            - 1-{{scale1}}: TEMEL KAVRAMLAR (Balang seviyesi, terminoloji kontrol)
            - {{scale1 + 1}}-{{scale1 + scale2}}: UYGULAMA VE SENARYO (Orta seviye, "nasl yaplr?" ve kod okuma)
            - {{scale1 + scale2 + 1}}-{{scale1 + scale2 + scale3}}: ANALZ VE PROBLEM ZME (leri seviye, hata ayklama ve mimari kararlar)
            - {{scale1 + scale2 + scale3 + 1}}-{{totalScale}}: UZMANLIK VE DERN KONULAR (Zorlayc, u durumlar ve optimizasyon)

            KALTE KURALLARI:
            - Sorulari sadece yukaridaki sikistirilmis arastirma baglami ve konu basligiyla destekle; ham Korteks raporu varsayma.
            - Genis bir kavram haritasini kapsa: temel kavram, onkosul, uygulama, analiz, hata ayiklama ve uc durumlar.
            - Soru tipleri tekrarlamasin: conceptual, procedural, application, analysis ve misconception_probe karisik kullanilsin.
            - Zorluk dagilimi dengeli olsun: kolay, orta ve zor sorulari acikca karistir.
            - Soru metinleri birbirinin kopyasi veya yakin tekrari olmasin.
            - En az {{Math.Max(3, (int)Math.Ceiling(totalScale * 0.40))}} farkli conceptTag ve en az {{Math.Max(2, (int)Math.Ceiling(totalScale * 0.20))}} farkli questionType kullan.
            - En az {{Math.Max(2, (int)Math.Ceiling(totalScale * 0.25))}} soru yaygin kavram yanilgisini veya beklenen hata kategorisini hedeflesin.
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

            DL: {{language}}.
            Respond entirely in the requested language (DL). All questions, options, explanations, and tags should be generated using that language.
            """;

        var rawQuiz = await _factory.CompleteChatAsync(AgentRole.DeepPlan, systemPrompt, $"Konu: \"{topicTitle}\" iin {totalScale} adet derinlikli baseline sorusu ret. Dil: {language}.");
        var validatedQuiz = DiagnosticQuizQualityGate.EnsureQualityOrThrow(rawQuiz, topicTitle, totalScale, out _, null);

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
