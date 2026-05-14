using Xunit;

namespace Orka.API.Tests;

public sealed class RegressionGateScriptTests
{
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

    [Fact]
    public void QuickCoordinationRunsMandatoryCoordinationTests()
    {
        var script = Read("scripts/quick-coordination.ps1");

        Assert.Contains("coordination regression baseline", script, StringComparison.OrdinalIgnoreCase);
        foreach (var testName in MandatoryCoordinationTests)
            Assert.Contains(testName, script);
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
    public void QuickBackendRunsProductionSafetyLiteTests()
    {
        var script = Read("scripts/quick-backend.ps1");

        Assert.Contains("ProductionSafetyLiteTests", script);
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

    private static string Read(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

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
