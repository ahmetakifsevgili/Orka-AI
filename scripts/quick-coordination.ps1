param(
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$commit = "unknown"
$branch = "unknown"
try {
    $commit = (git rev-parse HEAD 2>$null).Trim()
    $branch = (git rev-parse --abbrev-ref HEAD 2>$null).Trim()
} catch {}
Write-Host "[quick-coordination] Active Git Branch: $branch | Commit: $commit"
$dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
$coordinationFilter = "TopicTreeScopeContractTests|RagScopeIntegrationTests|DashboardAggregationTests|DashboardCoordinationHealthTests|ChatParityTests|QuizLearningPipelineTests|PlanDiagnosticApiFlowTests|BackendCoordinationSmokeTests|KorteksContractTests|RegressionGateScriptTests"

if (-not $NoBuild) {
    Write-Host "[quick-coordination] Building Orka.API.Tests..."
    & "$PSScriptRoot\run-command-with-timeout.ps1" `
        -TimeoutSeconds 60 `
        -WorkingDirectory $root `
        -FilePath $dotnet `
        -ArgumentList @("build", ".\Orka.API.Tests\Orka.API.Tests.csproj", "--configuration", "Debug", "--verbosity", "minimal", "/p:RestoreIgnoreFailedSources=true", "/nr:false")
}

Write-Host "[quick-coordination] Running coordination regression baseline..."
& "$PSScriptRoot\run-command-with-timeout.ps1" `
    -TimeoutSeconds 120 `
    -WorkingDirectory $root `
    -FilePath $dotnet `
    -ArgumentList @("test", ".\Orka.API.Tests\Orka.API.Tests.csproj", "--no-build", "--nologo", "--verbosity", "minimal", "--filter", $coordinationFilter, "--blame-hang", "--blame-hang-timeout", "30s")
