$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$commit = "unknown"
$branch = "unknown"
try {
    $commit = (git rev-parse HEAD 2>$null).Trim()
    $branch = (git rev-parse --abbrev-ref HEAD 2>$null).Trim()
} catch {}
Write-Host "[quick-all] Active Git Branch: $branch | Commit: $commit"
$frontRoot = Join-Path $root "Orka-Front"
$npm = (Get-Command npm.cmd -ErrorAction Stop).Source
$dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source

Write-Host "[quick-all] Git diff hygiene..."
git -C $root diff --check

Write-Host "[quick-all] Frontend smoke..."
& "$PSScriptRoot\run-command-with-timeout.ps1" `
    -TimeoutSeconds 35 `
    -WorkingDirectory $frontRoot `
    -FilePath $npm `
    -ArgumentList @("run", "quick:smoke")

Write-Host "[quick-all] Frontend typecheck..."
& "$PSScriptRoot\run-command-with-timeout.ps1" `
    -TimeoutSeconds 45 `
    -WorkingDirectory $frontRoot `
    -FilePath $npm `
    -ArgumentList @("run", "typecheck")

Write-Host "[quick-all] Frontend Vitest unit tests..."
& "$PSScriptRoot\run-command-with-timeout.ps1" `
    -TimeoutSeconds 45 `
    -WorkingDirectory $frontRoot `
    -FilePath $npm `
    -ArgumentList @("run", "test:unit")

Write-Host "[quick-all] Frontend production build..."
& "$PSScriptRoot\run-command-with-timeout.ps1" `
    -TimeoutSeconds 40 `
    -WorkingDirectory $frontRoot `
    -FilePath $npm `
    -ArgumentList @("run", "build", "--", "--logLevel", "error")

Write-Host "[quick-all] MSBuild unit tests (Infrastructure)..."
& "$PSScriptRoot\run-command-with-timeout.ps1" `
    -TimeoutSeconds 90 `
    -WorkingDirectory $root `
    -FilePath $dotnet `
    -ArgumentList @("test", ".\Orka.Infrastructure.UnitTests\Orka.Infrastructure.UnitTests.csproj", "--nologo", "--verbosity", "minimal")

Write-Host "[quick-all] Backend quick smoke..."
& "$PSScriptRoot\quick-backend.ps1"

Write-Host "[quick-all] All quick checks passed."
