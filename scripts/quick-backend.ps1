$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
$msbuild = "C:\Program Files\dotnet\sdk\8.0.121\MSBuild.dll"
if (-not (Test-Path $msbuild)) {
    throw ".NET SDK 8.0.121 is required. Expected: $msbuild"
}

Write-Host "[quick-backend] Building Orka.API..."
& "$PSScriptRoot\run-command-with-timeout.ps1" `
    -TimeoutSeconds 35 `
    -WorkingDirectory $root `
    -FilePath $dotnet `
    -ArgumentList @("exec", $msbuild, ".\Orka.API\Orka.API.csproj", "/t:Build", "/p:Configuration=Debug", "/p:RestoreIgnoreFailedSources=true", "/v:minimal", "/nr:false")

Write-Host "[quick-backend] Building Orka.API.Tests..."
& "$PSScriptRoot\run-command-with-timeout.ps1" `
    -TimeoutSeconds 35 `
    -WorkingDirectory $root `
    -FilePath $dotnet `
    -ArgumentList @("exec", $msbuild, ".\Orka.API.Tests\Orka.API.Tests.csproj", "/t:Build", "/p:Configuration=Debug", "/p:RestoreIgnoreFailedSources=true", "/v:minimal", "/nr:false")

Write-Host "[quick-backend] Running endpoint bridge smoke tests..."
& "$PSScriptRoot\run-command-with-timeout.ps1" `
    -TimeoutSeconds 35 `
    -WorkingDirectory $root `
    -FilePath $dotnet `
    -ArgumentList @("test", ".\Orka.API.Tests\Orka.API.Tests.csproj", "--no-build", "--nologo", "--verbosity", "minimal", "--filter", "EndpointBridgeSmokeTests|PlanQualityGuardTests|SourceRegressionGuardTests", "--blame-hang", "--blame-hang-timeout", "15s")
