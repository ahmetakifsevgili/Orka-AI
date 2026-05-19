$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
$lifeTestFilter = "BackendLifeTests|PedagogicalReleaseClosureTests"
$regressionFilter = "DevContractTests|ContentSafetyTests|AiReliabilityTests|DataLifecycleTests|AuthTokenContractTests|PublicSecuritySurfaceTests|RequestBoundarySafetyTests|MigrationPolicyTests|BacklogBeforeProductionTests|ProductionSafetyLiteTests|AuthSwaggerHealthSmokeTests|EndpointBridgeSmokeTests|SourceRegressionGuardTests|RuntimeTelemetryHardeningTests|ToolCapabilityContractTests|FullyQualifiedName~Auth"
$coordinationFilter = "TopicTreeScopeContractTests|RagScopeIntegrationTests|DashboardAggregationTests|DashboardCoordinationHealthTests|ChatParityTests|QuizLearningPipelineTests|BackendCoordinationSmokeTests|KorteksContractTests|RegressionGateScriptTests"

function Assert-LifecycleSqlServerProvisioned {
    if (-not [string]::IsNullOrWhiteSpace($env:ORKA_LIFECYCLE_SQLSERVER_BASE_CONNECTION)) {
        Write-Host "[quick-backend] Relational lifecycle smoke will use ORKA_LIFECYCLE_SQLSERVER_BASE_CONNECTION."
        return
    }

    $sqllocaldb = Get-Command sqllocaldb.exe -ErrorAction SilentlyContinue
    if ($null -eq $sqllocaldb) {
        throw "DataLifecycleTests require SQL Server LocalDB `(localdb)\OrkaLocalDB` or ORKA_LIFECYCLE_SQLSERVER_BASE_CONNECTION. Run scripts\reset-dev-db.ps1 on Windows or provision SQL Server in CI."
    }

    $sqllocaldbPath = $sqllocaldb.Source
    & $sqllocaldbPath info OrkaLocalDB *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "DataLifecycleTests require the LocalDB instance OrkaLocalDB. Run scripts\reset-dev-db.ps1 or set ORKA_LIFECYCLE_SQLSERVER_BASE_CONNECTION in CI."
    }

    & $sqllocaldbPath start OrkaLocalDB *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not start LocalDB instance OrkaLocalDB for DataLifecycleTests. Run scripts\reset-dev-db.ps1 or set ORKA_LIFECYCLE_SQLSERVER_BASE_CONNECTION."
    }
}

Write-Host "[quick-backend] Building Orka.API..."
& "$PSScriptRoot\run-command-with-timeout.ps1" `
    -TimeoutSeconds 60 `
    -WorkingDirectory $root `
    -FilePath $dotnet `
    -ArgumentList @("build", ".\Orka.API\Orka.API.csproj", "--configuration", "Debug", "--verbosity", "minimal", "/p:RestoreIgnoreFailedSources=true", "/nr:false")

Write-Host "[quick-backend] Building Orka.API.Tests..."
& "$PSScriptRoot\run-command-with-timeout.ps1" `
    -TimeoutSeconds 60 `
    -WorkingDirectory $root `
    -FilePath $dotnet `
    -ArgumentList @("build", ".\Orka.API.Tests\Orka.API.Tests.csproj", "--configuration", "Debug", "--verbosity", "minimal", "/p:RestoreIgnoreFailedSources=true", "/nr:false")

Assert-LifecycleSqlServerProvisioned

Write-Host "[quick-backend] Running backend lifetest release proof..."
& "$PSScriptRoot\run-command-with-timeout.ps1" `
    -TimeoutSeconds 180 `
    -WorkingDirectory $root `
    -FilePath $dotnet `
    -ArgumentList @("test", ".\Orka.API.Tests\Orka.API.Tests.csproj", "--no-build", "--nologo", "--verbosity", "minimal", "--filter", $lifeTestFilter, "--blame-hang", "--blame-hang-timeout", "45s")

Write-Host "[quick-backend] Running stabilization regression baseline..."
& "$PSScriptRoot\run-command-with-timeout.ps1" `
    -TimeoutSeconds 120 `
    -WorkingDirectory $root `
    -FilePath $dotnet `
    -ArgumentList @("test", ".\Orka.API.Tests\Orka.API.Tests.csproj", "--no-build", "--nologo", "--verbosity", "minimal", "--filter", $regressionFilter, "--blame-hang", "--blame-hang-timeout", "30s")

Write-Host "[quick-backend] Running coordination regression baseline..."
& "$PSScriptRoot\run-command-with-timeout.ps1" `
    -TimeoutSeconds 120 `
    -WorkingDirectory $root `
    -FilePath $dotnet `
    -ArgumentList @("test", ".\Orka.API.Tests\Orka.API.Tests.csproj", "--no-build", "--nologo", "--verbosity", "minimal", "--filter", $coordinationFilter, "--blame-hang", "--blame-hang-timeout", "30s")
