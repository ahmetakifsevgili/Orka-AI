using Xunit;
using System.Text.Json;
using Orka.Core.DTOs;

namespace Orka.API.Tests;

public sealed class RegressionGateScriptTests
{
    private static readonly string[] CommonTurkishMojibakePatterns =
    [
        "\u00C3\u00A7", // ç
        "\u00C3\u0087", // Ç
        "\u00C3\u00BC", // ü
        "\u00C3\u009C", // Ü
        "\u00C3\u00B6", // ö
        "\u00C3\u0096", // Ö
        "\u00C4\u00B1", // ı
        "\u00C4\u00B0", // İ
        "\u00C5\u009F", // ş
        "\u00C5\u009E", // Ş
        "\u00C4\u009F", // ğ
        "\u00C4\u009E", // Ğ
        "\u00C2\u00B7",
        "\u00E2\u0080\u0094",
        "\u00E2\u0080\u0093",
        "\uFFFD"
    ];

    private static readonly string[] TextExtensions =
    [
        ".cs",
        ".ts",
        ".tsx",
        ".js",
        ".mjs",
        ".md",
        ".ps1",
        ".json",
        ".css",
        ".html"
    ];

    private static readonly string[] MandatoryCoordinationTests =
    [
        "TopicTreeScopeContractTests",
        "RagScopeIntegrationTests",
        "DashboardAggregationTests",
        "DashboardCoordinationHealthTests",
        "ChatParityTests",
        "QuizLearningPipelineTests",
        "BackendCoordinationSmokeTests",
        "KorteksContractTests",
        "RegressionGateScriptTests"
    ];

    private static readonly string[] MandatoryBackendLifeTests =
    [
        "BackendLifeTests",
        "PedagogicalReleaseClosureTests"
    ];

    private static readonly string[] MandatoryProductCoherenceTests =
    [
        "OrkaUnifiedEvaluationHarnessTests",
        "StudentSimulationEvaluationTests",
        "OrkaCodeLearningIdeTests",
        "OrkaNotebookStudioProTests",
        "OrkaStudyRoomTests",
        "OrkaSourceWikiProTests",
        "OrkaExamWarRoomTests",
        "OrkaStudyCoachTests",
        "OrkaMissionControlTests",
        "OrkaLearningStateCoherenceTests"
    ];

    [Fact]
    public void QuickCoordinationRunsMandatoryCoordinationTests()
    {
        var script = Read("scripts/quick-coordination.ps1");

        Assert.Contains("coordination regression baseline", script, StringComparison.OrdinalIgnoreCase);
        foreach (var testName in MandatoryCoordinationTests)
            Assert.Contains(testName, script);
    }

    [Fact]
    public void SourceFilesDoNotContainCommonTurkishMojibake()
    {
        var root = FindRepoRoot();
        var dirty = EnumerateTextFilesForMojibakeScan(root)
            .Select(file => new
            {
                File = file,
                Text = File.ReadAllText(file)
            })
            .Select(item => new
            {
                item.File,
                Patterns = CommonTurkishMojibakePatterns
                    .Where(pattern => item.Text.Contains(pattern, StringComparison.Ordinal))
                    .Select(pattern => pattern == "\uFFFD" ? "replacement_char" : ToCodepoints(pattern))
                    .ToArray()
            })
            .Where(item => item.Patterns.Length > 0)
            .Select(item => $"{Path.GetRelativePath(root, item.File)} [{string.Join(", ", item.Patterns)}]")
            .ToArray();

        Assert.True(dirty.Length == 0, "Common Turkish mojibake patterns found:\n" + string.Join("\n", dirty));
    }

    [Fact]
    public void QuickBackendIncludesCoordinationRegressionBaseline()
    {
        var script = Read("scripts/quick-backend.ps1");

        Assert.Contains("coordination regression baseline", script, StringComparison.OrdinalIgnoreCase);
        foreach (var testName in MandatoryCoordinationTests)
            Assert.Contains(testName, script);
    }

    [Fact]
    public void QuickBackendRunsBackendLifetestReleaseProof()
    {
        var script = Read("scripts/quick-backend.ps1");

        Assert.Contains("backend lifetest release proof", script, StringComparison.OrdinalIgnoreCase);
        foreach (var testName in MandatoryBackendLifeTests)
            Assert.Contains(testName, script);
    }

    [Fact]
    public void QuickBackendRunsProductCoherenceReleaseProof()
    {
        var script = Read("scripts/quick-backend.ps1");

        Assert.Contains("product coherence release proof", script, StringComparison.OrdinalIgnoreCase);
        foreach (var testName in MandatoryProductCoherenceTests)
            Assert.Contains(testName, script);
    }

    [Fact]
    public void ApiSmokeFactoryKeepsTestLoggingFocusedWithoutSuppressingReleaseWarnings()
    {
        var factory = Read("Orka.API.Tests/ApiSmokeFactory.cs");

        Assert.Contains("ConfigureLogging", factory, StringComparison.Ordinal);
        Assert.Contains("Microsoft.EntityFrameworkCore\", LogLevel.Warning", factory, StringComparison.Ordinal);
        Assert.Contains("LuckyPennySoftware.MediatR.License\", LogLevel.None", factory, StringComparison.Ordinal);
        Assert.Contains("Orka.Infrastructure.Services.BackgroundTaskQueue\", LogLevel.Warning", factory, StringComparison.Ordinal);
        Assert.Contains("Orka.Infrastructure.Services.RetentionCleanupWorker\", LogLevel.Warning", factory, StringComparison.Ordinal);
        Assert.Contains("Orka.Infrastructure.Services.RedisStreamMaintenanceWorker\", LogLevel.Warning", factory, StringComparison.Ordinal);
        Assert.Contains("Orka.Infrastructure.Services.SrsReminderWorker\", LogLevel.Warning", factory, StringComparison.Ordinal);
        Assert.Contains("Orka.Infrastructure.Services.DailyChallengeWorker\", LogLevel.Warning", factory, StringComparison.Ordinal);
        Assert.DoesNotContain("ClearProviders()", factory, StringComparison.Ordinal);
        Assert.DoesNotContain("LogLevel.Error", factory, StringComparison.Ordinal);
    }

    [Fact]
    public void BackendReleaseWorkflowRunsProviderFreeReleaseProof()
    {
        var workflow = Read(".github/workflows/backend-release.yml");

        Assert.Contains("Backend Release", workflow, StringComparison.Ordinal);
        Assert.Contains("windows-latest", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet restore .\\Orka.sln", workflow, StringComparison.Ordinal);
        Assert.Contains(".\\scripts\\quick-backend.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("Orka.Infrastructure.UnitTests.csproj", workflow, StringComparison.Ordinal);
        Assert.Contains("git diff --check", workflow, StringComparison.Ordinal);
        Assert.Contains("sqllocaldb.exe", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("ExternalProviderIntegrationTests", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("ORKA_RUN_EXTERNAL_PROVIDER_TESTS", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("OPENAI_API_KEY", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GROQ_API_KEY", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QuickBackendRunsProductionSafetyLiteTests()
    {
        var script = Read("scripts/quick-backend.ps1");

        Assert.Contains("ProductionSafetyLiteTests", script);
    }

    [Fact]
    public void AudioRetentionSummaryDoesNotMaterializeAudioPayloadTables()
    {
        var service = Read("Orka.Infrastructure/Services/StandardsAndProductionServices.cs");
        var start = service.IndexOf("public async Task<AudioRetentionSummaryDto> GetAudioRetentionSummaryAsync", StringComparison.Ordinal);
        var end = service.IndexOf("public async Task<AudioRetentionSummaryDto> PurgeExpiredAudioAsync", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Audio retention summary method was not found.");
        var method = service[start..end];

        Assert.Contains("BuildAudioRetentionAggregateAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("AudioOverviewJobs.AsNoTracking().ToListAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("ClassroomInteractions.AsNoTracking().ToListAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("AudioBytes.Length", method, StringComparison.Ordinal);
        Assert.DoesNotContain("AudioBytes?.LongLength", method, StringComparison.Ordinal);
    }

    [Fact]
    public void QuickRegressionScriptsDoNotRunExternalProviderTests()
    {
        var quickBackend = Read("scripts/quick-backend.ps1");
        var quickCoordination = Read("scripts/quick-coordination.ps1");
        var externalTests = Read("Orka.API.Tests/ExternalProviderIntegrationTests.cs");
        var smokeFactory = Read("Orka.API.Tests/ApiSmokeFactory.cs");
        var devContract = Read("docs/dev-contract.md");

        Assert.DoesNotContain("ExternalProviderIntegrationTests", quickBackend, StringComparison.Ordinal);
        Assert.DoesNotContain("ExternalProviderIntegrationTests", quickCoordination, StringComparison.Ordinal);
        Assert.DoesNotContain("ORKA_RUN_EXTERNAL_PROVIDER_TESTS", quickBackend, StringComparison.Ordinal);
        Assert.DoesNotContain("ORKA_RUN_EXTERNAL_PROVIDER_TESTS", quickCoordination, StringComparison.Ordinal);

        Assert.Contains("ORKA_RUN_EXTERNAL_PROVIDER_TESTS", externalTests);
        Assert.Contains("[Trait(\"Category\", \"External\")]", externalTests);
        Assert.Contains("SmokeRealWorldEvidenceService", smokeFactory);
        Assert.Contains("IRealWorldEvidenceService, SmokeRealWorldEvidenceService", smokeFactory);
        Assert.Contains("ORKA_RUN_EXTERNAL_PROVIDER_TESTS", devContract);
        Assert.Contains("must not be part of the deterministic", devContract, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CodexSkillsConstitutionDocsArePresentAndLinked()
    {
        string[] requiredDocs =
        [
            "CODEX.md",
            "docs/project-state/current-roadmap.md",
            "docs/codex-skills/README.md",
            "docs/codex-skills/backend-feature-constitution.md",
            "docs/codex-skills/ai-rag-feature-constitution.md",
            "docs/codex-skills/frontend-contract-constitution.md",
            "docs/codex-skills/data-lifecycle-constitution.md",
            "docs/codex-skills/testing-gate-constitution.md",
            "docs/codex-skills/feature-prompt-template.md",
            "docs/codex-skills/feature-completion-report-template.md"
        ];

        foreach (var relativePath in requiredDocs)
            Assert.True(File.Exists(Path.Combine(FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar))), $"{relativePath} is missing.");

        var codexEntrypoint = Read("CODEX.md");
        var roadmap = Read("docs/project-state/current-roadmap.md");
        var readme = Read("README.md");
        var codexSkillsReadme = Read("docs/codex-skills/README.md");
        var devContract = Read("docs/dev-contract.md");
        var checklist = Read("scripts/CHECKLIST.md");

        Assert.Contains("docs/project-state/current-roadmap.md", codexEntrypoint);
        Assert.Contains("docs/codex-skills/README.md", codexEntrypoint);
        Assert.Contains("Do not stage/commit unless explicitly requested", codexEntrypoint);
        Assert.Contains("Do not reorder roadmap without user approval", codexEntrypoint);
        Assert.Contains("Central Exams pilot productization readiness", roadmap);
        Assert.Contains("Post-6B Professionalization", roadmap);
        Assert.Contains("Stage 5 - Production-ready enterprise hardening / scalability plan", roadmap);
        Assert.Contains("Stage 6B - Merkezi Sinavlar / Exam & Practice Content Engine", roadmap);
        Assert.Contains("docs/project-state/stage-6b-closure.md", roadmap);
        Assert.Contains("docs/project-state/post-6b-professionalization-closure.md", roadmap);
        Assert.Contains("Codex Skills Anayasasi + small/medium features", roadmap);
        Assert.Contains("Stage 4 Small/Medium Feature Completion Audit", roadmap);
        Assert.Contains("Stage 6B - Merkezi Sinavlar / Exam & Practice Content Engine is closed", codexEntrypoint);
        Assert.Contains("post-6B productization / frontend-content readiness", codexEntrypoint);
        Assert.Contains("CODEX.md", readme);
        Assert.Contains("CODEX.md", codexSkillsReadme);
        Assert.Contains("docs/project-state/current-roadmap.md", readme);
        Assert.Contains("docs/project-state/current-roadmap.md", codexSkillsReadme);
        Assert.Contains("docs/project-state/current-roadmap.md", devContract);
        Assert.Contains("docs/project-state/current-roadmap.md", checklist);
        Assert.Contains("docs/codex-skills", readme);
        Assert.Contains("docs/codex-skills", codexSkillsReadme);
        Assert.Contains("docs/codex-skills", devContract);
        Assert.Contains("docs/codex-skills", checklist);
        Assert.Contains("backend-feature-constitution.md", checklist);
        Assert.Contains("ai-rag-feature-constitution.md", checklist);
        Assert.Contains("frontend-contract-constitution.md", checklist);
        Assert.Contains("data-lifecycle-constitution.md", checklist);
        Assert.Contains("testing-gate-constitution.md", checklist);
        Assert.Contains("feature-prompt-template.md", checklist);
        Assert.Contains("feature-completion-report-template.md", checklist);
    }

    [Fact]
    public void PublicLearningQualityDtosDoNotSerializeOwnerIdsOrRawPayloadHashes()
    {
        var payloads = new[]
        {
            JsonSerializer.Serialize(new LearningQualityReportDto { Id = Guid.NewGuid(), UserId = Guid.NewGuid() }),
            JsonSerializer.Serialize(new AssessmentCalibrationRunDto { Id = Guid.NewGuid(), UserId = Guid.NewGuid() }),
            JsonSerializer.Serialize(new AdaptiveAssessmentSessionDto { Id = Guid.NewGuid(), UserId = Guid.NewGuid() }),
            JsonSerializer.Serialize(new StandardsSummaryDto { UserId = Guid.NewGuid() }),
            JsonSerializer.Serialize(new StandardsValidationRunDto { Id = Guid.NewGuid(), UserId = Guid.NewGuid() }),
            JsonSerializer.Serialize(new StandardsExportRunDto { Id = Guid.NewGuid(), UserId = Guid.NewGuid() }),
            JsonSerializer.Serialize(new TeachingEvidenceCardDto
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                Title = "Safe public evidence card",
                RawPayloadHash = "hash_should_stay_internal"
            })
        };

        foreach (var payload in payloads)
        {
            Assert.DoesNotContain("userId", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ownerId", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("rawPayloadHash", payload, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hash_should_stay_internal", payload, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TutorPublicTraceEndpointsUseSafeProjections()
    {
        var controller = Read("Orka.API/Controllers/TutorController.cs");

        Assert.Contains("learnerProfile = profile == null ? null : new", controller);
        Assert.Contains("workingMemory = memory == null ? null : new", controller);
        Assert.Contains("latestTurnState = latestTurn == null ? null : new", controller);
        Assert.DoesNotContain("learnerProfile = profile,", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("workingMemory = memory,", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("latestTurnState = latestTurn", controller.Replace("latestTurnState = latestTurn == null ? null : new", string.Empty), StringComparison.Ordinal);
        Assert.DoesNotContain("return Ok(new { trace, tools, artifacts, evidence, reflections, pedagogyRuns, pedagogyScores })", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("RawPayloadJson", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("RawPayloadHash", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("StateJson", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("SnapshotJson", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("RunJson", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("ResultJson", controller, StringComparison.Ordinal);
    }

    private static string Read(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static IEnumerable<string> EnumerateTextFilesForMojibakeScan(string root)
    {
        string[] includeRoots =
        [
            "Orka.API",
            "Orka.Core",
            "Orka.Infrastructure",
            "Orka.API.Tests",
            "Orka.Infrastructure.UnitTests",
            "Orka-Front/src",
            "Orka-Front/scripts",
            "docs",
            "scripts"
        ];

        foreach (var includeRoot in includeRoots)
        {
            var absolute = Path.Combine(root, includeRoot.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(absolute)) continue;

            foreach (var file in Directory.EnumerateFiles(absolute, "*", SearchOption.AllDirectories))
            {
                var normalized = file.Replace(Path.DirectorySeparatorChar, '/');
                if (normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains("/dist/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TextExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }

        foreach (var file in new[] { "README.md", "CODEX.md" })
        {
            var absolute = Path.Combine(root, file);
            if (File.Exists(absolute)) yield return absolute;
        }
    }

    private static string ToCodepoints(string value) =>
        string.Join("+", value.Select(ch => $"U+{(int)ch:X4}"));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Orka.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root could not be located.");
    }
}
