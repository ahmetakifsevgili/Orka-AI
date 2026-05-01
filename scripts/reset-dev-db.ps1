param(
    [string]$InstanceName = "OrkaLocalDB",
    [string]$DatabaseName = "OrkaDB"
)

$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot

function Invoke-NativeChecked {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$FailureMessage
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $output = & $FilePath @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorActionPreference
    $text = ($output | Out-String).Trim()
    if (-not [string]::IsNullOrWhiteSpace($text)) {
        Write-Host $text
    }

    if ($exitCode -ne 0 -or $text -match "failed because|Unexpected error|error occurred|Cannot create") {
        throw "$FailureMessage (exit $exitCode). $text"
    }

    return $text
}

$preferredBackupRoot = "C:\tmp"
$fallbackBackupRoot = Join-Path $repo ".tmp"
$backupRoot = $preferredBackupRoot
try {
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
}
catch {
    $backupRoot = $fallbackBackupRoot
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
}
$stamp = Get-Date -Format "yyyyMMddHHmmss"
$backupDir = Join-Path $backupRoot "orka-db-backup-$stamp"

try {
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
}
catch {
    $backupRoot = $fallbackBackupRoot
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $backupDir = Join-Path $backupRoot "orka-db-backup-$stamp"
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
}

Get-Process Orka.API -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

foreach ($file in @("$env:USERPROFILE\$DatabaseName.mdf", "$env:USERPROFILE\${DatabaseName}_log.ldf")) {
    if (Test-Path -LiteralPath $file) {
        Move-Item -LiteralPath $file -Destination $backupDir -Force
    }
}

$existingInstances = Invoke-NativeChecked "sqllocaldb" @("info") "Could not list SQL LocalDB instances"
$hasInstance = $existingInstances -match "(?m)^$([regex]::Escape($InstanceName))$"

if ($hasInstance) {
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $instanceInfo = & sqllocaldb info $InstanceName 2>&1
    $instanceExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorActionPreference
    $infoText = ($instanceInfo | Out-String).Trim()
    if ($instanceExitCode -ne 0 -or $infoText -match "failed|Unexpected error|error occurred") {
        Write-Warning "LocalDB instance '$InstanceName' looks corrupted. Recreating it. Details: $infoText"
        $ErrorActionPreference = "Continue"
        & sqllocaldb stop $InstanceName -k 2>&1 | Write-Host
        & sqllocaldb delete $InstanceName 2>&1 | Write-Host
        $ErrorActionPreference = $previousErrorActionPreference
        $hasInstance = $false
    }
}

if (-not $hasInstance) {
    Invoke-NativeChecked "sqllocaldb" @("create", $InstanceName) "Could not create SQL LocalDB instance '$InstanceName'" | Out-Null
}

Invoke-NativeChecked "sqllocaldb" @("start", $InstanceName) "Could not start SQL LocalDB instance '$InstanceName'" | Out-Null

Invoke-NativeChecked "dotnet" @(
    "ef",
    "database",
    "update",
    "--project",
    (Join-Path $repo "Orka.Infrastructure\Orka.Infrastructure.csproj"),
    "--startup-project",
    (Join-Path $repo "Orka.API\Orka.API.csproj"),
    "--no-build"
) "Could not apply EF Core migrations" | Out-Null

Write-Host "Development database is ready. Old DB files, if any, were moved to $backupDir"
