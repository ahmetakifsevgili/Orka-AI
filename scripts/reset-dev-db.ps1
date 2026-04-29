param(
    [string]$InstanceName = "OrkaLocalDB",
    [string]$DatabaseName = "OrkaDB"
)

$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
$backupRoot = "C:\tmp"
$stamp = Get-Date -Format "yyyyMMddHHmmss"
$backupDir = Join-Path $backupRoot "orka-db-backup-$stamp"

New-Item -ItemType Directory -Path $backupDir -Force | Out-Null

Get-Process Orka.API -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

foreach ($file in @("$env:USERPROFILE\$DatabaseName.mdf", "$env:USERPROFILE\${DatabaseName}_log.ldf")) {
    if (Test-Path -LiteralPath $file) {
        Move-Item -LiteralPath $file -Destination $backupDir -Force
    }
}

$existingInstances = (& sqllocaldb info) -join "`n"
if ($existingInstances -notmatch "(?m)^$([regex]::Escape($InstanceName))$") {
    & sqllocaldb create $InstanceName | Write-Host
}

& sqllocaldb start $InstanceName | Write-Host

& dotnet ef database update `
    --project (Join-Path $repo "Orka.Infrastructure\Orka.Infrastructure.csproj") `
    --startup-project (Join-Path $repo "Orka.API\Orka.API.csproj") `
    --no-build

Write-Host "Development database is ready. Old DB files, if any, were moved to $backupDir"
