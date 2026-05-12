using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Orka.API.Tests;

public sealed class SourceRegressionGuardTests
{
    private const string MojibakeContinuation =
        @"(?:[\u0080-\u00bf]|\u20ac|\u201a|\u0192|\u201e|\u2026|\u2020|\u2021|\u02c6|\u2030|\u0160|\u2039|\u0152|\u017d|\u2018|\u2019|\u201c|\u201d|\u2022|\u2013|\u2014|\u02dc|\u2122|\u0161|\u203a|\u0153|\u017e|\u0178)";

    private static readonly Regex MojibakeRegex = new(
        $@"(?:[\u00c2-\u00c5]{MojibakeContinuation}|\u00e2{MojibakeContinuation}{{1,2}}|\u011f\u0178|\u00ef\u00b8|\ufffd)",
        RegexOptions.Compiled);

    [Fact]
    public void ProductSource_DoesNotContainCommonMojibakeMarkers()
    {
        var dirtyFiles = EnumerateProductSourceFiles()
            .Select(relative => (relative, text: ReadRepoText(relative)))
            .Where(item => MojibakeRegex.IsMatch(item.text))
            .Select(item => item.relative)
            .ToArray();

        Assert.True(dirtyFiles.Length == 0, "Mojibake markers found in: " + string.Join(", ", dirtyFiles));
    }

    [Fact]
    public void UserFacingTurkishCopy_DoesNotRegressToAsciiPlaceholders()
    {
        var guarded = new Dictionary<string, string[]>
        {
            ["Orka-Front/src/components/ChatPanel.tsx"] =
            [
                "Onayla ve arastir",
                "Niyet ayriliyor",
                "Baglam taraniyor",
                "Ogrenme yolu uretiliyor",
                "Yanit akisinda"
            ],
            ["Orka-Front/src/components/InteractiveIDE.tsx"] =
            [
                "Kod calistirma saglayicisi hazir degil",
                "Bu hata bir ogrenme sinyali olabilir",
                "Ogrenme notu"
            ],
            ["Orka-Front/src/components/WikiMainPanel.tsx"] =
            [
                "kaynaklarini yukle",
                "kaynak grafigi",
                "Kisisel Pekistirme"
            ],
            ["Orka.API/Controllers/ChatController.cs"] =
            [
                "Gunluk mesaj limitine ulasildi",
                "Istek islenemedi",
                "Oturum baslatilamadi"
            ],
            ["Orka.Infrastructure/Services/AgentOrchestratorService.cs"] =
            [
                "henuz tamamlandi",
                "asagidaki kisa telafi",
                "zayif becerilerine"
            ],
            ["Orka.Infrastructure/Services/DiagnosticQuizQualityGate.cs"] =
            [
                "asagidaki ornek",
                "hangi yanilgiye",
                "akil yurutmeyi"
            ]
        };

        var failures = guarded
            .SelectMany(entry =>
            {
                var text = ReadRepoText(entry.Key);
                return entry.Value
                    .Where(phrase => text.Contains(phrase, StringComparison.Ordinal))
                    .Select(phrase => $"{entry.Key}: {phrase}");
            })
            .ToArray();

        Assert.True(failures.Length == 0, "User-facing ASCII Turkish copy regressed: " + string.Join(" | ", failures));
    }

    [Theory]
    [InlineData("T\u00c3\u00bcrk\u00c3\u00a7e")]
    [InlineData("Ba\u00c5\u017flang\u00c4\u00b1\u00c3\u00a7")]
    [InlineData("Akademik \u00e2\u20ac\u201d kaynak")]
    [InlineData("sel\u00c3\u0192\u00c2\u00a7uk")]
    [InlineData("\u011f\u0178\u201c\u0161")]
    [InlineData("\u00ef\u00b8\u008f")]
    [InlineData("\u00c2\u00b7")]
    public void MojibakeGuard_DetectsKnownDirtyExamples(string dirty)
    {
        Assert.Matches(MojibakeRegex, dirty);
    }

    [Theory]
    [InlineData("Türkçe")]
    [InlineData("Français")]
    [InlineData("Español")]
    [InlineData("Português")]
    [InlineData("Üben")]
    [InlineData("hâlâ")]
    public void MojibakeGuard_AllowsValidUnicodeLanguageNames(string clean)
    {
        Assert.DoesNotMatch(MojibakeRegex, clean);
    }

    [Fact]
    public void QuizCard_UsesReadableTurkishAndNeverPrintsRawQuizJson()
    {
        var quizCard = ReadRepoText("Orka-Front/src/components/QuizCard.tsx");

        Assert.Contains("quizRunId", quizCard);
        Assert.Contains("skillTag", quizCard);
        Assert.Contains("topicPath", quizCard);
        Assert.Contains("questionHash", quizCard);
        Assert.Contains("Quiz ak\u0131\u015f\u0131 tamamland\u0131", quizCard);
        Assert.Contains("Tekrar edilmesi iyi olur", quizCard);
        Assert.Contains("Bu cevap do\u011fru de\u011fil", quizCard);
        Assert.Contains("Do\u011fru", quizCard);
        Assert.DoesNotContain("Quiz Cevab\u0131m", quizCard, StringComparison.Ordinal);
        Assert.DoesNotContain("[SKIP_QUIZ]", quizCard, StringComparison.Ordinal);
        Assert.DoesNotContain("crypto.randomUUID", quizCard, StringComparison.Ordinal);
        Assert.DoesNotContain("Harika gidiyorsun", quizCard, StringComparison.Ordinal);
        Assert.DoesNotContain("Dogru", quizCard, StringComparison.Ordinal);
        Assert.DoesNotContain("Yanlis", quizCard, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON.stringify(quiz", quizCard, StringComparison.Ordinal);
        Assert.DoesNotContain("{JSON.stringify", quizCard, StringComparison.Ordinal);
    }

    [Fact]
    public void Pack3MisconceptionUi_UsesSafeProjectionAndDoesNotPrintRawEvaluatorPayload()
    {
        var quizCard = ReadRepoText("Orka-Front/src/components/QuizCard.tsx");
        var dashboard = ReadRepoText("Orka-Front/src/components/DashboardPanel.tsx");
        var chatMessage = ReadRepoText("Orka-Front/src/components/ChatMessage.tsx");

        Assert.Contains("Yanılgı sinyali", quizCard);
        Assert.Contains("Kanıt durumu", quizCard);
        Assert.Contains("remediationSeed", dashboard);
        Assert.Contains("Yanılgı sinyali güvenli şekilde işlendi", chatMessage);
        Assert.DoesNotContain("EvaluatorFeedback", quizCard + dashboard + chatMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("evaluationScore", quizCard + dashboard + chatMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("trace dump", quizCard + dashboard + chatMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pack4LearningMemoryUi_UsesSafeStudentProfileSummary()
    {
        var dashboard = ReadRepoText("Orka-Front/src/components/DashboardPanel.tsx");
        var api = ReadRepoText("Orka-Front/src/services/api.ts");
        var dtos = ReadRepoText("Orka.Core/DTOs/LearningArchitectureDtos.cs");

        Assert.Contains("Orka’nın öğrenci profili", dashboard);
        Assert.Contains("Güçlü ilerlediğin alanlar", dashboard);
        Assert.Contains("Tekrar gerektiren alanlar", dashboard);
        Assert.Contains("Orka’nın emin olmadığı alanlar", dashboard);
        Assert.Contains("Henüz yeterli öğrenme sinyali yok. Quiz, chat ve Wiki kullandıkça profil oluşur.", dashboard);
        Assert.Contains("learningMemory?: LearningMemoryLiteDto | null", api);
        Assert.Contains("GoalReadinessDto", dtos);
        Assert.DoesNotContain("EvaluatorFeedback", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("evaluationScore", dashboard, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("trace dump", dashboard, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("goalType", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("targetDate", dashboard, StringComparison.Ordinal);
    }

    [Fact]
    public void ClassroomSpeakerGuards_KeepTeacherAssistantAndGuestStable()
    {
        var backend = ReadRepoText("Orka.Infrastructure/Services/ClassroomService.cs");
        var frontend = ReadRepoText("Orka-Front/src/components/ClassroomAudioPlayer.tsx");

        Assert.Contains("HOCA", backend);
        Assert.Contains("ASISTAN", backend);
        Assert.Contains("KONUK", backend);
        Assert.Contains("NormalizeDialogue", backend);
        Assert.Contains("ParseSpeakers", backend);

        Assert.Contains("ensureClassroomDialogue", frontend);
        Assert.Contains("ASISTAN", frontend);
        Assert.Contains("KONUK", frontend);
        Assert.Contains("!hasAssistant", frontend);
        Assert.Contains("queuedLines", frontend);
        Assert.Contains("activeSegment", frontend);
    }

    [Fact]
    public void IdeAndCodeCopy_StayReadableAndLearningSignalAware()
    {
        var ide = ReadRepoText("Orka-Front/src/components/InteractiveIDE.tsx");
        var codeController = ReadRepoText("Orka.API/Controllers/CodeController.cs");

        Assert.Contains("Kodu \u00c7al\u0131\u015ft\u0131r", ide);
        Assert.Contains("Hocaya G\u00f6nder", ide);
        Assert.Contains("IdeRunCompleted", ide);
        Assert.Contains("IdeSentToTutor", ide);
        Assert.Contains("LearningAPI.recordSignal", ide);

        Assert.Contains("Kod bo\u015f olamaz.", codeController);
        Assert.Contains("IdeRunCompleted", codeController);
    }

    [Fact]
    public void P4ProductDepth_GuardsPlanTemplatesAndVisualLearning()
    {
        var landing = ReadRepoText("Orka-Front/src/pages/Landing.tsx");
        var tutor = ReadRepoText("Orka.Infrastructure/Services/TutorAgent.cs");
        var deepPlan = ReadRepoText("Orka.Infrastructure/Services/DeepPlanAgent.cs");

        Assert.Contains("\u00d6\u011frenci sinyali yakaland\u0131", landing);
        Assert.Contains("NotebookLM", landing);
        Assert.Contains("QA ve sistem g\u00fcveni", landing);

        Assert.Contains("[P4 G\u00d6RSEL \u00d6\u011eRENME VALIDATOR - ACTION PLAN \u00d6NCEL\u0130KL\u0130]", tutor);
        Assert.Contains("Mermaid", tutor);
        Assert.Contains("mikro kontrol sorusu", tutor);

        Assert.Contains("BuildConceptGraphPlanningGuidance", deepPlan);
        Assert.Contains("BuildConceptGraphFallbackModules", deepPlan);
        Assert.Contains("onkosul -> ana kavram", deepPlan);
        Assert.DoesNotContain("PlanDomain.", deepPlan);
        Assert.DoesNotContain("BuildDomainPlanningGuidance", deepPlan);
        Assert.DoesNotContain("BuildDomainFallbackModules", deepPlan);
    }

    [Fact]
    public void P5FeatureBridges_AreWiredIntoStableSurfaces()
    {
        var program = ReadRepoText("Orka.API/Program.cs");
        var classroomService = ReadRepoText("Orka.Infrastructure/Services/ClassroomService.cs");
        var classroomController = ReadRepoText("Orka.API/Controllers/ClassroomController.cs");
        var learningDtos = ReadRepoText("Orka.Core/DTOs/LearningDtos.cs");
        var api = ReadRepoText("Orka-Front/src/services/api.ts");
        var classroomPlayer = ReadRepoText("Orka-Front/src/components/ClassroomAudioPlayer.tsx");
        var onboarding = ReadRepoText("Orka-Front/src/components/PremiumOnboardingTour.tsx");
        var home = ReadRepoText("Orka-Front/src/pages/Home.tsx");
        var packageJson = ReadRepoText("Orka-Front/package.json");

        Assert.Contains("IEdgeTtsService", classroomService);
        Assert.Contains("InteractionId", learningDtos);
        Assert.Contains("GetInteractionAudio", classroomController);
        Assert.Contains("getInteractionAudio", api);
        Assert.Contains("tryPlayBackendAudio", classroomPlayer);
        Assert.Contains("speechSynthesis", classroomPlayer);

        Assert.Contains("YouTubeTranscriptPlugin", program);
        Assert.Contains("AddFromObject(sp.GetRequiredService<YouTubeTranscriptPlugin>())", program);

        Assert.Contains("driver.js", packageJson);
        Assert.Contains("usePremiumOnboarding", home);
        Assert.Contains("tour-new-topic", onboarding);
        Assert.Contains("tour-nav-dashboard", onboarding);
        Assert.Contains("tour-nav-wiki", onboarding);
        Assert.Contains("tour-nav-ide", onboarding);
    }

    [Fact]
    public void WikiV2_ReplacesRawWikiAgentChatPath()
    {
        var controller = ReadRepoText("Orka.API/Controllers/WikiController.cs");
        var program = ReadRepoText("Orka.API/Program.cs");
        var services = ReadRepoText("Orka.Infrastructure/Services/WikiLearningServices.cs");
        var api = ReadRepoText("Orka-Front/src/services/api.ts");
        var panel = ReadRepoText("Orka-Front/src/components/WikiMainPanel.tsx");

        Assert.Contains("IWikiLearningAssistant", controller);
        Assert.Contains("workspace-state", controller);
        Assert.DoesNotContain("IWikiAgent wikiAgent", controller);
        Assert.False(RepoFileExists("Orka.Core/Interfaces/IWikiAgent.cs"));
        Assert.False(RepoFileExists("Orka.Infrastructure/Services/WikiAgent.cs"));

        Assert.Contains("AddScoped<IWikiLearningAssistant, WikiLearningAssistant>", program);
        Assert.DoesNotContain("IWikiAgent", program);
        Assert.DoesNotContain("WikiAgent", program);
        Assert.Contains("WikiCitationGuard", services);
        Assert.Contains("RetrieveTopicEvidenceAsync", services);
        Assert.Contains("SourceCitationCheck", services);
        Assert.Contains("getWorkspaceState", api);
        Assert.Contains("getTopicQuality", api);
        Assert.Contains("artifact_ready", panel);
        Assert.Contains("TutorAPI.getArtifact", panel);
        Assert.Contains("Kaynak Sağlığı", panel);
    }

    [Fact]
    public void Phase0AndPhase2ClosureGuards_TextHealthAndNotebookToolsAreWired()
    {
        var program = ReadRepoText("Orka.API/Program.cs");
        var textController = ReadRepoText("Orka.API/Controllers/TextHealthController.cs");
        var textService = ReadRepoText("Orka.Infrastructure/Services/TextHealthService.cs");
        var summarizer = ReadRepoText("Orka.Infrastructure/Services/SummarizerAgent.cs");
        var ragEvalSet = ReadRepoText("Orka.API.Tests/TestData/RagMiniEvalSet.json");

        Assert.Contains("ITextHealthService", program);
        Assert.Contains("api/dev/text-health", textController);
        Assert.Contains("TextHealth:RepairEnabled", textController);
        Assert.Contains("DryRunAsync", textService);
        Assert.Contains("RepairAsync", textService);
        Assert.Contains("Dirty(0x00c3, 0x00bc)", textService);

        Assert.Contains("BuildNotebookToolEvidenceAsync", summarizer);
        Assert.Contains("IWikiEvidenceService", summarizer);
        Assert.Contains("RecordNotebookToolCitationChecksAsync", summarizer);
        Assert.Contains("SourceCitationChecks", summarizer);
        Assert.DoesNotContain("var source = await BuildNotebookToolSourceAsync", summarizer);

        Assert.Contains("source-grounded-pdf-qa", ragEvalSet);
        Assert.Contains("source-poisoning-chunk", ragEvalSet);
        Assert.Contains("notebook-tools-grounding", ragEvalSet);
    }

    [Fact]
    public void Phase4ShellReadModel_IsWiredWithoutReplacingStableRoutes()
    {
        var dashboardController = ReadRepoText("Orka.API/Controllers/DashboardController.cs");
        var api = ReadRepoText("Orka-Front/src/services/api.ts");
        var home = ReadRepoText("Orka-Front/src/pages/Home.tsx");
        var sidebar = ReadRepoText("Orka-Front/src/components/LeftSidebar.tsx");
        var dashboardPanel = ReadRepoText("Orka-Front/src/components/DashboardPanel.tsx");

        Assert.Contains("HttpGet(\"today\")", dashboardController);
        Assert.Contains("DashboardTodayDto", dashboardController);
        Assert.Contains("Kaynak yok", dashboardController);
        Assert.Contains("Citation desteklenmiyor", dashboardController);
        Assert.Contains("getToday", api);
        Assert.Contains("DashboardTodayDto", api);

        Assert.Contains("\"sources\"", home);
        Assert.Contains("\"practice\"", home);
        Assert.Contains("\"progress\"", home);
        Assert.Contains("Kaynaklar için önce bir konu seç.", home);

        Assert.Contains("labelKey: \"learn\"", sidebar);
        Assert.Contains("labelKey: \"sources\"", sidebar);
        Assert.Contains("labelKey: \"practice\"", sidebar);
        Assert.Contains("labelKey: \"review\"", sidebar);
        Assert.Contains("labelKey: \"progress\"", sidebar);

        Assert.Contains("todayFocusTitle", dashboardPanel);
        Assert.Contains("sourceHealthLabel", dashboardPanel);
        Assert.Contains("todayActionView", dashboardPanel);
        Assert.Contains("Çalışmaya geç", dashboardPanel);
        Assert.Contains("Kaynakları aç", dashboardPanel);
        Assert.DoesNotContain("Calismaya Gec", dashboardPanel);
        Assert.DoesNotContain("Henuz aktif", dashboardPanel);
        Assert.DoesNotContain("Hatasini", dashboardPanel);
    }

    [Fact]
    public void ProductionAuditGuards_BlockKnownCriticalRegressions()
    {
        var chat = ReadRepoText("Orka.API/Controllers/ChatController.cs");
        var testController = ReadRepoText("Orka.API/Controllers/TestController.cs");
        var classroomService = ReadRepoText("Orka.Infrastructure/Services/ClassroomService.cs");
        var sourcesController = ReadRepoText("Orka.API/Controllers/SourcesController.cs");
        var learningSourceService = ReadRepoText("Orka.Infrastructure/Services/LearningSourceService.cs");

        Assert.Contains("TryConsumeDailyMessageAsync", chat);
        Assert.Contains("DailyLimitLocks", chat);
        Assert.Contains("SemaphoreSlim", chat);
        Assert.Contains("DailyMessageCount++", chat);
        Assert.Contains("StatusCode(429", chat);
        Assert.Contains("Limits:FreeUserDailyMessages", chat);

        Assert.Contains("[Authorize(Roles = \"Admin\")]", testController);
        Assert.DoesNotContain("[AllowAnonymous]", testController);
        Assert.DoesNotContain("ex.Message", testController);

        Assert.Contains("IServiceScopeFactory", classroomService);
        Assert.Contains("AiAnswerTimeout", classroomService);
        Assert.Contains("GenerateClassroomAnswerOrFallbackAsync", classroomService);
        Assert.Contains("BuildProviderFallbackDialogue", classroomService);
        Assert.Contains("TtsTimeout", classroomService);
        Assert.Contains("QueueAudioGeneration(interaction.Id, answer)", classroomService);
        Assert.DoesNotContain("await TryAttachAudioAsync", classroomService);

        Assert.Contains("StorageUsedMB", learningSourceService);
        Assert.Contains("StorageLimitMB", learningSourceService);
        Assert.Contains("StorageQuotaExceededException", learningSourceService);
        Assert.Contains("Status413PayloadTooLarge", sourcesController);
    }

    [Fact]
    public void BackgroundJobs_UseCentralQueueInsteadOfRawTaskRun()
    {
        var program = ReadRepoText("Orka.API/Program.cs");
        var queueInterface = ReadRepoText("Orka.Core/Interfaces/IBackgroundTaskQueue.cs");
        var queueService = ReadRepoText("Orka.Infrastructure/Services/BackgroundTaskQueue.cs");

        Assert.Contains("AddHostedService", program);
        Assert.Contains("BackgroundTaskQueue", program);
        Assert.Contains("BackgroundTaskItem", queueInterface);
        Assert.Contains("Channel.CreateBounded", queueService);
        Assert.Contains("MaxAttempts", queueService);
        Assert.Contains("Timeout", queueService);

        string[] queuedServices =
        [
            "Orka.Infrastructure/Services/ClassroomService.cs",
            "Orka.Infrastructure/Services/AgentOrchestratorService.cs",
            "Orka.Infrastructure/Services/AIAgentFactory.cs",
            "Orka.Infrastructure/Services/ContextBuilder.cs",
            "Orka.Infrastructure/Services/LearningSignalService.cs"
        ];

        var rawTaskRunFiles = queuedServices
            .Where(relative => ReadRepoText(relative).Contains("Task.Run", StringComparison.Ordinal))
            .ToArray();

        Assert.True(rawTaskRunFiles.Length == 0,
            "Raw Task.Run usage should stay out of production background paths: " + string.Join(", ", rawTaskRunFiles));
    }

    [Fact]
    public void YouTubePlugin_UsesStructuredFallbacksAndSourceTags()
    {
        var plugin = ReadRepoText("Orka.Infrastructure/SemanticKernel/Plugins/YouTubeTranscriptPlugin.cs");

        Assert.Contains("AI:YouTube:ApiKey", plugin);
        Assert.Contains("[youtube:disabled]", plugin);
        Assert.Contains("[youtube:degraded]", plugin);
        Assert.Contains("[youtube:{videoId}]", plugin);
        Assert.DoesNotContain("ex.Message", plugin, StringComparison.Ordinal);
    }

    [Fact]
    public void P6EducatorCore_WiresSourceGroundingAndTeachingReference()
    {
        var program = ReadRepoText("Orka.API/Program.cs");
        var educatorCore = ReadRepoText("Orka.Infrastructure/Services/EducatorCoreService.cs");
        var tutor = ReadRepoText("Orka.Infrastructure/Services/TutorAgent.cs");
        var quiz = ReadRepoText("Orka.Infrastructure/Services/QuizAgent.cs");
        var evaluator = ReadRepoText("Orka.Infrastructure/Services/EvaluatorAgent.cs");
        var sources = ReadRepoText("Orka.Infrastructure/Services/LearningSourceService.cs");
        var dashboard = ReadRepoText("Orka.API/Controllers/DashboardController.cs");
        var hud = ReadRepoText("Orka-Front/src/components/SystemHealthHUD.tsx");
        var signalTypes = ReadRepoText("Orka.Core/Constants/LearningSignalTypes.cs");
        var educatorDtos = ReadRepoText("Orka.Core/DTOs/EducatorDtos.cs");

        Assert.Contains("IEducatorCoreService, EducatorCoreService", program);
        Assert.Contains("TeacherContext", educatorDtos);
        Assert.Contains("TeachingReference", educatorDtos);
        Assert.Contains("SourceUsage", educatorDtos);
        Assert.Contains("MisconceptionSignal", educatorDtos);
        Assert.Contains("EducatorQualityScore", educatorDtos);

        Assert.Contains("BuildTeacherContextAsync", educatorCore);
        Assert.Contains("NormalizeTeachingReferenceAsync", educatorCore);
        Assert.Contains("YOUTUBE TEACHING REFERENCE - PEDAGOGY ONLY", educatorCore);
        Assert.Contains("Do not treat YouTube as a factual source", educatorCore);
        Assert.Contains("SourceCitationMissing", educatorCore);

        Assert.Contains("educatorCoreContext", tutor);
        Assert.Contains("RecordAnswerQualitySignalsAsync", tutor);
        Assert.Contains("BuildYouTubeDistractorBlock", quiz);
        Assert.Contains("YOUTUBE PEDAGOGY QUALITY REFERENCE - NOT FACTUAL GROUNDING", evaluator);
        Assert.Contains("source-ask-answer-without-doc-citation", sources);
        Assert.Contains("educatorCoreSignals", dashboard);
        Assert.Contains("EducatorCore -> Tutor", dashboard);
        Assert.Contains("citationMissing", dashboard);
        Assert.Contains("educatorCore?:", hud);
        Assert.Contains("EducatorCore", hud);

        Assert.Contains("YouTubeReferenceUsed", signalTypes);
        Assert.Contains("NotebookSourceUsed", signalTypes);
        Assert.Contains("MisconceptionDetected", signalTypes);
        Assert.Contains("TeachingMoveApplied", signalTypes);
        Assert.Contains("SourceCitationMissing", signalTypes);
    }

    [Fact]
    public void PublicToolFallbacks_DoNotLeakRawExceptionMessages()
    {
        string[] files =
        [
            "Orka.Infrastructure/SemanticKernel/Plugins/AcademicSearchPlugin.cs",
            "Orka.Infrastructure/SemanticKernel/Plugins/TavilySearchPlugin.cs",
            "Orka.Infrastructure/SemanticKernel/Plugins/WikipediaPlugin.cs",
            "Orka.Infrastructure/Services/FileExtractionService.cs",
            "Orka.Infrastructure/Services/PistonService.cs",
            "Orka.API/Controllers/SourcesController.cs"
        ];

        var leakingFiles = files
            .Where(relative => ReadRepoText(relative).Contains("ex.Message", StringComparison.Ordinal))
            .ToArray();

        Assert.True(leakingFiles.Length == 0,
            "Public tool fallbacks must not echo raw exception messages: " + string.Join(", ", leakingFiles));
    }

    [Fact]
    public void DevDatabaseRecovery_UsesStableNamedLocalDbAndBackupScript()
    {
        var devSettings = ReadRepoText("Orka.API/appsettings.Development.json");
        var resetScript = ReadRepoText("scripts/reset-dev-db.ps1");
        var diagnostics = ReadRepoText("Orka.API/Controllers/DiagnosticsController.cs");
        var quickBackend = ReadRepoText("scripts/quick-backend.ps1");
        var devContract = ReadRepoText("docs/dev-contract.md");
        var checklist = ReadRepoText("scripts/CHECKLIST.md");

        Assert.Contains("OrkaLocalDB", devSettings);
        Assert.Contains("TrustServerCertificate=True", devSettings);
        Assert.Contains("orka-db-backup", resetScript);
        Assert.Contains("Invoke-NativeChecked \"dotnet\"", resetScript);
        Assert.Contains("\"database\"", resetScript);
        Assert.Contains("\"update\"", resetScript);
        Assert.Contains("Invoke-NativeChecked \"sqllocaldb\"", resetScript);
        Assert.Contains("\"start\"", resetScript);
        Assert.Contains("Database readiness check returned false", diagnostics);
        Assert.Contains("Assert-LifecycleSqlServerProvisioned", quickBackend);
        Assert.Contains("ORKA_LIFECYCLE_SQLSERVER_BASE_CONNECTION", quickBackend);
        Assert.Contains("DataLifecycleTests require", quickBackend);
        Assert.Contains("ORKA_LIFECYCLE_SQLSERVER_BASE_CONNECTION", devContract);
        Assert.Contains("sessizce skip edilmemeli", checklist);
    }

    [Fact]
    public void ProductionCorsDeploymentDocs_KeepExplicitAllowlistContract()
    {
        var migrationPolicy = ReadRepoText("docs/deployment/migration-policy.md");
        var checklist = ReadRepoText("scripts/CHECKLIST.md");
        var productionSettings = ReadRepoText("Orka.API/appsettings.Production.json");
        var stagingSettings = ReadRepoText("Orka.API/appsettings.Staging.json");

        Assert.Contains("Cors:AllowedOrigins", migrationPolicy);
        Assert.Contains("Empty values and `*` are rejected at startup", migrationPolicy);
        Assert.Contains("Cors__AllowedOrigins__0", migrationPolicy);
        Assert.Contains("Cors:AllowedOrigins", checklist);
        Assert.Contains("fail-fast", checklist);
        Assert.Contains("\"AllowedOrigins\": []", productionSettings);
        Assert.Contains("\"AllowedOrigins\": []", stagingSettings);
    }

    [Fact]
    public void PublicControllerErrors_DoNotEchoRawExceptionMessages()
    {
        string[] controllers =
        [
            "Orka.API/Controllers/AuthController.cs",
            "Orka.API/Controllers/ChatController.cs",
            "Orka.API/Controllers/WikiController.cs",
            "Orka.API/Controllers/QuizController.cs",
            "Orka.API/Controllers/KorteksController.cs",
            "Orka.API/Controllers/TestController.cs",
            "Orka.API/Controllers/DashboardController.cs",
            "Orka.API/Controllers/DiagnosticsController.cs"
        ];

        var leakingControllers = controllers
            .Where(relative => ReadRepoText(relative).Contains("ex.Message", StringComparison.Ordinal))
            .ToArray();

        Assert.True(leakingControllers.Length == 0,
            "Raw exception messages leak through controller responses or diagnostics: " + string.Join(", ", leakingControllers));
    }

    [Fact]
    public void ContentSafety_RenderersUseStrictMermaidAndSafeRemoteMediaPolicy()
    {
        var chat = ReadRepoText("Orka-Front/src/components/ChatMessage.tsx");
        var rich = ReadRepoText("Orka-Front/src/components/RichMarkdown.tsx");
        var helper = ReadRepoText("Orka-Front/src/lib/contentSafety.tsx");

        Assert.DoesNotContain("securityLevel: \"loose\"", chat, StringComparison.Ordinal);
        Assert.DoesNotContain("securityLevel: \"loose\"", rich, StringComparison.Ordinal);
        Assert.DoesNotContain("google.com/s2/favicons", chat, StringComparison.Ordinal);
        Assert.DoesNotContain("google.com/s2/favicons", rich, StringComparison.Ordinal);

        Assert.Contains("securityLevel: \"strict\"", chat);
        Assert.Contains("securityLevel: \"strict\"", rich);
        Assert.Contains("htmlLabels: false", chat);
        Assert.Contains("htmlLabels: false", rich);
        Assert.Contains("sanitizeMermaidSvg", chat);
        Assert.Contains("sanitizeMermaidSvg", rich);
        Assert.Contains("isAllowedRemoteImage", chat);
        Assert.Contains("BlockedImagePlaceholder", rich);

        Assert.Contains("image.pollinations.ai", helper);
        Assert.Contains("foreignObject", helper);
        Assert.Contains("SafeMarkdownLink", helper);
        Assert.Contains("SafeMarkdownImage", helper);
        Assert.Contains("safeMarkdownComponents", helper);
        Assert.Contains("rel=\"noopener noreferrer nofollow\"", helper);
        Assert.Contains("return value.startsWith(\"#\")", helper);
        var svgReferencePolicy = helper[helper.IndexOf("function isSafeSvgReference", StringComparison.Ordinal)..];
        Assert.DoesNotContain("parsed.protocol === \"http:\"", svgReferencePolicy);
        Assert.DoesNotContain("parsed.protocol === \"https:\"", svgReferencePolicy);
        Assert.DoesNotContain("gÃ", helper);
        Assert.DoesNotContain("Ä", helper);

        string[] markdownSurfaces =
        [
            "Orka-Front/src/components/AgenticWorkspace.tsx",
            "Orka-Front/src/components/QuizCard.tsx",
            "Orka-Front/src/components/WikiDrawer.tsx"
        ];

        foreach (var surfacePath in markdownSurfaces)
        {
            var source = ReadRepoText(surfacePath);
            Assert.Contains("safeMarkdownComponents", source);
            Assert.Equal(
                CountOccurrences(source, "<ReactMarkdown"),
                CountOccurrences(source, "components={safeMarkdownComponents}"));
        }

        var wikiMainPanel = ReadRepoText("Orka-Front/src/components/WikiMainPanel.tsx");
        Assert.DoesNotContain("ReactMarkdown", wikiMainPanel);
        Assert.Contains("<RichMarkdown", wikiMainPanel);

        Assert.Contains("...safeMarkdownComponents", chat);
    }

    [Fact]
    public void SystemClosureGuards_FrontendStreamAuthAndContractStayAligned()
    {
        var api = ReadRepoText("Orka-Front/src/services/api.ts");
        var types = ReadRepoText("Orka-Front/src/lib/types.ts");
        var endpointSmoke = ReadRepoText("Orka-Front/scripts/smoke-endpoints.mjs");
        var uiSmoke = ReadRepoText("Orka-Front/scripts/smoke-ui.mjs");

        Assert.Contains("export const authenticatedFetch", api);
        Assert.Contains("refreshAccessToken", api);
        Assert.Contains("authenticatedFetch(\"/api/chat/stream\"", api);
        Assert.Contains("authenticatedFetch(\"/api/korteks/research-stream\"", api);
        Assert.Contains("authenticatedFetch(\"/api/korteks/research-file\"", api);
        Assert.Contains("withCredentials: true", api);
        Assert.Contains("credentials: init.credentials ?? \"include\"", api);
        Assert.DoesNotContain("Bearer null", api, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer undefined", api, StringComparison.Ordinal);

        Assert.Contains("coordinationScope?:", api);
        Assert.Contains("coordinationHealth?:", api);
        Assert.Contains("sourceTopicId?:", types);
        Assert.Contains("sourceTopicTitle?:", types);
        Assert.Contains("scopeRelation?:", types);
        Assert.Contains("retrievalScope?:", types);
        Assert.Contains("KorteksSyncResponseDto", api);

        Assert.Contains("/api/korteks/research-stream", endpointSmoke);
        Assert.Contains("/api/korteks/research-file", endpointSmoke);
        Assert.Contains("Stream APIs use authenticated fetch wrapper", uiSmoke);
        Assert.Contains("Dashboard coordination contract is typed", uiSmoke);
    }

    [Fact]
    public void SystemClosureGuards_AuthCleanupIsScopedAndSecuritySmokeIsWired()
    {
        string[] productFiles =
        [
            "Orka-Front/src/services/api.ts",
            "Orka-Front/src/components/SettingsPanel.tsx",
            "Orka.API/wwwroot/app.html",
            "Orka.API/wwwroot/js/api.js"
        ];

        var broadClearFiles = productFiles
            .Where(relative => ReadRepoText(relative).Contains("localStorage.clear()", StringComparison.Ordinal))
            .ToArray();

        Assert.True(broadClearFiles.Length == 0,
            "Auth/session cleanup must remove scoped Orka keys, not clear all localStorage: " + string.Join(", ", broadClearFiles));

        var packageJson = ReadRepoText("Orka-Front/package.json");
        var securitySmoke = ReadRepoText("Orka-Front/scripts/smoke-security.mjs");

        Assert.Contains("smoke:security", packageJson);
        Assert.Contains("smoke:security", packageJson[packageJson.IndexOf("\"quick:smoke\"", StringComparison.Ordinal)..]);
        Assert.Contains("<script>alert(1)</script>", securitySmoke);
        Assert.Contains("javascript:", securitySmoke);
        Assert.Contains("xlink:href", securitySmoke);
        Assert.Contains("image.pollinations.ai", securitySmoke);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string ReadRepoText(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(fullPath), "Missing expected source file: " + fullPath);
        return File.ReadAllText(fullPath, Encoding.UTF8);
    }

    private static bool RepoFileExists(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(fullPath);
    }

    private static IEnumerable<string> EnumerateProductSourceFiles()
    {
        var root = FindRepoRoot();
        string[] sourceRoots =
        [
            "Orka.API",
            "Orka.Core",
            "Orka.Infrastructure",
            "Orka-Front/src",
            "Orka-Front/scripts",
            "docs"
        ];

        HashSet<string> extensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs",
            ".ts",
            ".tsx",
            ".js",
            ".mjs",
            ".json",
            ".md",
            ".css",
            ".html"
        };

        foreach (var relativeRoot in sourceRoots)
        {
            var fullRoot = Path.Combine(root, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(fullRoot))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories))
            {
                if (!extensions.Contains(Path.GetExtension(file)) || IsIgnoredSourceFile(root, file))
                {
                    continue;
                }

                yield return Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            }
        }
    }

    private static bool IsIgnoredSourceFile(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string[] ignoredDirectories =
        [
            "bin",
            "obj",
            "node_modules",
            "dist",
            "build",
            ".git",
            ".vs"
        ];

        if (parts.Any(part => ignoredDirectories.Contains(part, StringComparer.OrdinalIgnoreCase)))
        {
            return true;
        }

        var fileName = Path.GetFileName(file);
        return fileName.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
               || string.Equals(fileName, "OrkaDbContextModelSnapshot.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Orka.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
