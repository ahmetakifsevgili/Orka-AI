param(
    [switch]$CleanViteCache
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$front = Join-Path $repo "Orka-Front"
$log = Join-Path $env:TEMP "orka-front-start.log"

$existing = netstat -ano | Select-String ":3000" | ForEach-Object { ($_ -split "\s+")[-1] } | Sort-Object -Unique
foreach ($processId in $existing) {
    if ($processId -match "^\d+$" -and [int]$processId -gt 0) {
        Stop-Process -Id ([int]$processId) -Force -ErrorAction SilentlyContinue
    }
}

if ($CleanViteCache) {
    Remove-Item -Recurse -Force (Join-Path $front "node_modules\.vite") -ErrorAction SilentlyContinue
}

Remove-Item -Force $log -ErrorAction SilentlyContinue

$npm = (Get-Command npm.cmd -ErrorAction Stop).Source
$cmd = "/c cd /d `"$front`" && `"$npm`" run dev -- --host 127.0.0.1 --port 3000 > `"$log`" 2>&1"
Start-Process -FilePath "cmd.exe" -ArgumentList $cmd -WindowStyle Hidden

Write-Host "Orka frontend starting. LOG=$log"
exit 0
