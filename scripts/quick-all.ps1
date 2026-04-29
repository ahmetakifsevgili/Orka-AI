$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$frontRoot = Join-Path $root "Orka-Front"
$npm = (Get-Command npm.cmd -ErrorAction Stop).Source

Write-Host "[quick-all] Git diff hygiene..."
git -C $root diff --check

Write-Host "[quick-all] Frontend smoke..."
& "$PSScriptRoot\run-command-with-timeout.ps1" `
    -TimeoutSeconds 35 `
    -WorkingDirectory $frontRoot `
    -FilePath $npm `
    -ArgumentList @("run", "quick:smoke")

Write-Host "[quick-all] Frontend production build..."
& "$PSScriptRoot\run-command-with-timeout.ps1" `
    -TimeoutSeconds 40 `
    -WorkingDirectory $frontRoot `
    -FilePath $npm `
    -ArgumentList @("run", "build", "--", "--logLevel", "error")

Write-Host "[quick-all] Backend quick smoke..."
& "$PSScriptRoot\quick-backend.ps1"

Write-Host "[quick-all] All quick checks passed."
