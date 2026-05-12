param(
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
$coordinationFilter = "TopicTreeScopeContractTests|RagScopeIntegrationTests|DashboardAggregationTests|DashboardCoordinationHealthTests|ChatParityTests|QuizLearningPipelineTests|BackendCoordinationSmokeTests|KorteksContractTests|RegressionGateScriptTests"

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
