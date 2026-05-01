using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace Orka.API.Tests;

public sealed class SourceRegressionGuardTests
{
    private static readonly Regex MojibakeRegex = new(
        "[\u00c2\u00c3\u00c4\u00c5\ufffd]|\u00e2\u20ac|\u011f\u0178",
        RegexOptions.Compiled);

    [Fact]
    public void ProductSource_DoesNotContainCommonMojibakeMarkers()
    {
        string[] files =
        [
            "Orka.API/Controllers/CodeController.cs",
            "Orka.Core/DTOs/Code/CodeRunRequest.cs",
            "Orka.Infrastructure/Services/ClassroomService.cs",
            "Orka.Infrastructure/Services/ContextBuilder.cs",
            "Orka.Infrastructure/Services/DeepPlanAgent.cs",
            "Orka.Infrastructure/Services/AgentOrchestratorService.cs",
            "Orka.Infrastructure/Services/TutorAgent.cs",
            "Orka.Infrastructure/SemanticKernel/Plugins/YouTubeTranscriptPlugin.cs",
            "Orka-Front/src/components/ClassroomAudioPlayer.tsx",
            "Orka-Front/src/components/InteractiveIDE.tsx",
            "Orka-Front/src/components/QuizCard.tsx",
            "Orka-Front/src/components/RichMarkdown.tsx",
            "Orka-Front/src/components/SystemHealthHUD.tsx",
            "Orka-Front/src/components/WikiMainPanel.tsx",
            "Orka-Front/src/pages/Landing.tsx",
            "Orka-Front/src/pages/Login.tsx",
            "Orka-Front/src/services/api.ts"
        ];

        var dirtyFiles = files
            .Select(relative => (relative, text: ReadRepoText(relative)))
            .Where(item => MojibakeRegex.IsMatch(item.text))
            .Select(item => item.relative)
            .ToArray();

        Assert.True(dirtyFiles.Length == 0, "Mojibake markers found in: " + string.Join(", ", dirtyFiles));
    }

    [Fact]
    public void QuizCard_UsesReadableTurkishAndNeverPrintsRawQuizJson()
    {
        var quizCard = ReadRepoText("Orka-Front/src/components/QuizCard.tsx");

        Assert.Contains("quizRunId", quizCard);
        Assert.Contains("skillTag", quizCard);
        Assert.Contains("topicPath", quizCard);
        Assert.Contains("questionHash", quizCard);
        Assert.Contains("Quiz Cevab\u0131m", quizCard);
        Assert.Contains("Do\u011fru", quizCard);
        Assert.Contains("Yanl\u0131\u015f", quizCard);
        Assert.DoesNotContain("Dogru", quizCard, StringComparison.Ordinal);
        Assert.DoesNotContain("Yanlis", quizCard, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON.stringify(quiz", quizCard, StringComparison.Ordinal);
        Assert.DoesNotContain("{JSON.stringify", quizCard, StringComparison.Ordinal);
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

        Assert.Contains("[P4 G\u00d6RSEL \u00d6\u011eRENME VALIDATOR]", tutor);
        Assert.Contains("Mermaid", tutor);
        Assert.Contains("mikro kontrol sorusu", tutor);

        Assert.Contains("PlanDomain.Exam", deepPlan);
        Assert.Contains("PlanDomain.Algorithm", deepPlan);
        Assert.Contains("PlanDomain.Math", deepPlan);
        Assert.Contains("PlanDomain.Language", deepPlan);
        Assert.Contains("Spaced Repetition", deepPlan);
        Assert.Contains("Speaking Prompt", deepPlan);
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

        Assert.Contains("OrkaLocalDB", devSettings);
        Assert.Contains("TrustServerCertificate=True", devSettings);
        Assert.Contains("orka-db-backup", resetScript);
        Assert.Contains("Invoke-NativeChecked \"dotnet\"", resetScript);
        Assert.Contains("\"database\"", resetScript);
        Assert.Contains("\"update\"", resetScript);
        Assert.Contains("Invoke-NativeChecked \"sqllocaldb\"", resetScript);
        Assert.Contains("\"start\"", resetScript);
        Assert.Contains("Database readiness check returned false", diagnostics);
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

    private static string ReadRepoText(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(fullPath), "Missing expected source file: " + fullPath);
        return File.ReadAllText(fullPath, Encoding.UTF8);
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
