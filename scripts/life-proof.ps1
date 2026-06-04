param(
    [string]$ApiUrl = "http://localhost:5065",
    [string]$Personas = "new,repair,evidence-code",
    [switch]$IncludeAiProvider,
    [switch]$AllowUnready,
    [switch]$SkipCodeRun,
    [switch]$SkipFrontendSmoke,
    [switch]$SkipBackendProof,
    [switch]$NoStartApi,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$frontRoot = Join-Path $root "Orka-Front"
$reports = Join-Path $root "scripts\reports\life-proof"
$node = (Get-Command node.exe -ErrorAction Stop).Source
$npm = (Get-Command npm.cmd -ErrorAction Stop).Source

New-Item -ItemType Directory -Path $reports -Force | Out-Null

function Wait-OrkaUrl {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [int]$TimeoutSeconds = 90
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastError = $null
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 4
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                return $response
            }
        }
        catch {
            $lastError = $_.Exception.Message
            Start-Sleep -Milliseconds 750
        }
    }

    throw "URL did not become reachable in ${TimeoutSeconds}s: $Url. Last error: $lastError"
}

function Invoke-LifeProofCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [int]$TimeoutSeconds = 300
    )

    Write-Host "[life-proof] $Label"
    & "$PSScriptRoot\run-command-with-timeout.ps1" `
        -TimeoutSeconds $TimeoutSeconds `
        -WorkingDirectory $WorkingDirectory `
        -FilePath $FilePath `
        -ArgumentList $ArgumentList
}

$runId = "{0}-{1}" -f (Get-Date).ToUniversalTime().ToString("yyyyMMddHHmmssfff"), ([guid]::NewGuid().ToString("N").Substring(0, 8))
$summaryPath = Join-Path $reports "life-proof-$runId.md"

if (-not $NoStartApi) {
    if ($NoBuild) {
        & "$PSScriptRoot\start-api.ps1" -NoBuild
    }
    else {
        & "$PSScriptRoot\start-api.ps1"
    }
}

Wait-OrkaUrl "$ApiUrl/health/live" 120 | Out-Null

$lifeArgs = @(
    "scripts/real-user-lifetest.mjs",
    "--api-url=$ApiUrl",
    "--personas=$Personas",
    "--report-dir=$reports",
    "--run-id=$runId"
)
if ($IncludeAiProvider) { $lifeArgs += "--include-ai-provider" }
if ($AllowUnready) { $lifeArgs += "--allow-unready" }
if ($SkipCodeRun) { $lifeArgs += "--skip-code-run" }

Invoke-LifeProofCommand `
    -Label "Real-user API lifetest" `
    -WorkingDirectory $root `
    -FilePath $node `
    -ArgumentList $lifeArgs `
    -TimeoutSeconds 900

if (-not $SkipFrontendSmoke) {
    Invoke-LifeProofCommand `
        -Label "Frontend UI/contract/security/browser smoke" `
        -WorkingDirectory $frontRoot `
        -FilePath $npm `
        -ArgumentList @("run", "quick:smoke") `
        -TimeoutSeconds 300

    $env:ORKA_ENABLE_BROWSER_LIFEPROOF = "true"
    $env:ORKA_API_URL = $ApiUrl
    $env:VITE_API_BASE_URL = $ApiUrl
    try {
        Invoke-LifeProofCommand `
            -Label "Authenticated frontend app-shell life proof" `
            -WorkingDirectory $frontRoot `
            -FilePath $npm `
            -ArgumentList @("run", "life:browser") `
            -TimeoutSeconds 300
    }
    finally {
        Remove-Item Env:\ORKA_ENABLE_BROWSER_LIFEPROOF -ErrorAction SilentlyContinue
        Remove-Item Env:\ORKA_API_URL -ErrorAction SilentlyContinue
        Remove-Item Env:\VITE_API_BASE_URL -ErrorAction SilentlyContinue
    }
}

if (-not $SkipBackendProof) {
    Invoke-LifeProofCommand `
        -Label "Backend targeted release proof" `
        -WorkingDirectory $root `
        -FilePath "powershell.exe" `
        -ArgumentList @("-ExecutionPolicy", "Bypass", "-File", "scripts\quick-backend.ps1") `
        -TimeoutSeconds 900
}

$realUserReport = Join-Path $reports "real-user-lifetest-$runId.md"
$lines = @(
    "# Orka Life Proof $runId",
    "",
    "- API URL: $ApiUrl",
    "- Personas: $Personas",
    "- Provider calls: $(if ($IncludeAiProvider) { "enabled" } else { "disabled" })",
    "- Real-user API lifetest: PASS",
    "- Frontend smoke/browser life proof: $(if ($SkipFrontendSmoke) { "skipped" } else { "PASS" })",
    "- Backend targeted release proof: $(if ($SkipBackendProof) { "skipped" } else { "PASS" })",
    "- Real-user report: $realUserReport",
    "",
    "This command is the current Orka life proof gate: API user journeys, SQL/Redis diagnostics, public payload safety sweep, cross-user isolation, frontend smoke, and backend release proof."
)
$lines | Set-Content -Path $summaryPath -Encoding UTF8
Write-Host "[life-proof] Summary: $summaryPath"
