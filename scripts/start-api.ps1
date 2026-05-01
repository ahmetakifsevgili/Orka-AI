param(
    [switch]$NoBuild,
    [switch]$InMemoryDatabase
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$log = Join-Path $env:TEMP "orka-api-start.log"

$existing = netstat -ano | Select-String ":5065" | ForEach-Object { ($_ -split "\s+")[-1] } | Sort-Object -Unique
foreach ($processId in $existing) {
    if ($processId -match "^\d+$" -and [int]$processId -gt 0) {
        Stop-Process -Id ([int]$processId) -Force -ErrorAction SilentlyContinue
    }
}
Stop-Process -Name "Orka.API" -Force -ErrorAction SilentlyContinue

if (-not $NoBuild) {
    & (Join-Path $PSScriptRoot "build-api.ps1")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Remove-Item -Force $log -ErrorAction SilentlyContinue

$project = Join-Path $repo "Orka.API\Orka.API.csproj"
$databaseEnv = ""
if ($InMemoryDatabase) {
    $databaseEnv = " && set `"Database__Provider=InMemory`" && set `"Database__InMemoryName=OrkaDevSmoke`""
}

$cmd = "/c cd /d `"$repo`" && set `"ASPNETCORE_ENVIRONMENT=Development`"$databaseEnv && `"C:\Program Files\dotnet\dotnet.exe`" run --project `"$project`" --launch-profile http --no-build > `"$log`" 2>&1"
Start-Process -FilePath "cmd.exe" -ArgumentList $cmd -WindowStyle Hidden

Write-Host "Orka API starting. LOG=$log"
exit 0
