using System.Text.RegularExpressions;
using Xunit;

namespace Orka.API.Tests;

public sealed class DevContractTests
{
    private const string BackendUrl = "http://localhost:5065";
    private const string FrontendUrl = "http://localhost:3000";

    [Fact]
    public void LaunchSettingsAndApiHttpUseCanonicalBackendPort()
    {
        var launchSettings = Read("Orka.API/Properties/launchSettings.json");
        var apiHttp = Read("Orka.API/Orka.API.http");

        Assert.Contains(BackendUrl, launchSettings);
        Assert.Contains("https://localhost:7187;http://localhost:5065", launchSettings);
        Assert.Contains(BackendUrl, apiHttp);
    }

    [Fact]
    public void ViteProxyUsesCanonicalBackendAndStrictFrontendPort()
    {
        var vite = Read("Orka-Front/vite.config.ts");

        Assert.Contains("port: 3000", vite);
        Assert.Contains("strictPort: true", vite);
        Assert.Contains("VITE_API_PROXY_TARGET", vite);
        Assert.Contains(BackendUrl, vite);
    }

    [Fact]
    public void RuntimeScriptsUseCanonicalPortsAndEnvFallbacks()
    {
        var startApi = Read("scripts/start-api.ps1");
        var startFront = Read("scripts/start-front.ps1");
        var healthcheck = Read("scripts/healthcheck.mjs");
        var liveSmoke = Read("scripts/live-smoke.ps1");
        var liveUserSmoke = Read("scripts/live-user-smoke.ps1");
        var loadSmoke = Read("scripts/load-smoke.ps1");

        Assert.Contains(":5065", startApi);
        Assert.Contains("--port 3000", startFront);
        Assert.Contains("process.env.ORKA_API_URL", healthcheck);
        Assert.Contains(BackendUrl, healthcheck);
        Assert.Contains(BackendUrl, liveSmoke);
        Assert.Contains(FrontendUrl, liveSmoke);
        Assert.Contains(BackendUrl, liveUserSmoke);
        Assert.Contains(BackendUrl, loadSmoke);
    }

    [Fact]
    public void ContractTestsDefaultToCanonicalBackendPort()
    {
        var config = Read("contract_tests/helpers/config.py");
        var readme = Read("contract_tests/README.md");

        Assert.Contains("ORKA_API_URL", config);
        Assert.Contains(BackendUrl, config);
        Assert.DoesNotContain("localhost:5101", config, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(BackendUrl, readme);
        Assert.DoesNotContain("localhost:5101", readme, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QuickBackendRunsStabilizationBaselineWithoutHardcodedSdkPath()
    {
        var script = Read("scripts/quick-backend.ps1");

        Assert.DoesNotContain("8.0.121", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MSBuild.dll", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dotnet.exe", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AuthTokenContractTests", script);
        Assert.Contains("PublicSecuritySurfaceTests", script);
        Assert.Contains("RequestBoundarySafetyTests", script);
        Assert.Contains("MigrationPolicyTests", script);
        Assert.Contains("BacklogBeforeProductionTests", script);
        Assert.Contains("AuthSwaggerHealthSmokeTests", script);
        Assert.Contains("EndpointBridgeSmokeTests", script);
        Assert.Contains("SourceRegressionGuardTests", script);
        Assert.Contains("RuntimeTelemetryHardeningTests", script);
        Assert.Contains("ToolCapabilityContractTests", script);
        Assert.Contains("FullyQualifiedName~Auth", script);
    }

    [Fact]
    public void DocsPublishTheCanonicalDevContract()
    {
        var devContract = Read("docs/dev-contract.md");
        var checklist = Read("scripts/CHECKLIST.md");
        var readme = Read("README.md");

        Assert.Contains(BackendUrl, devContract);
        Assert.Contains(FrontendUrl, devContract);
        Assert.Contains("quick-backend.ps1", devContract);
        Assert.Contains("quick-all.ps1", devContract);
        Assert.Contains("pytest contract_tests", devContract);

        Assert.Contains(BackendUrl, checklist);
        Assert.Contains(FrontendUrl, checklist);
        Assert.Contains("5101", checklist);
        Assert.Contains("legacy audit history", checklist, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("Local Dev Contract", readme);
        Assert.Contains(BackendUrl, readme);
        Assert.Contains(FrontendUrl, readme);
        Assert.Contains("docs/dev-contract.md", readme);
    }

    [Fact]
    public void ActiveDevContractFilesDoNotIntroduceLegacyPortAsDefault()
    {
        string[] activeFiles =
        [
            "contract_tests/helpers/config.py",
            "contract_tests/README.md",
            "scripts/healthcheck.mjs",
            "scripts/start-api.ps1",
            "scripts/start-front.ps1",
            "scripts/live-smoke.ps1",
            "scripts/live-user-smoke.ps1",
            "scripts/load-smoke.ps1",
            "Orka-Front/vite.config.ts",
            "docs/dev-contract.md"
        ];

        foreach (var file in activeFiles)
        {
            var text = Read(file);
            Assert.False(
                Regex.IsMatch(text, @"localhost:5101|127\.0\.0\.1:5101", RegexOptions.IgnoreCase),
                $"{file} must not use 5101 as an active default.");
        }
    }

    private static string Read(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "Orka.sln")))
                return current;

            var parent = Directory.GetParent(current);
            if (parent == null)
                break;

            current = parent.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
