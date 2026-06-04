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
else {
    $developmentSettings = Join-Path $repo "Orka.API\appsettings.Development.json"
    if (Test-Path -LiteralPath $developmentSettings) {
        $settings = Get-Content -LiteralPath $developmentSettings -Raw | ConvertFrom-Json
        $connectionString = [string]$settings.ConnectionStrings.DefaultConnection
        $localDbMatch = [regex]::Match($connectionString, "(?i)(?:^|;)\s*Server\s*=\s*\(localdb\)\\([^;]+)")
        if ($localDbMatch.Success) {
            $instanceName = $localDbMatch.Groups[1].Value.Trim()
            $infoOutput = & sqllocaldb info $instanceName 2>&1
            if ($LASTEXITCODE -ne 0 -or (($infoOutput | Out-String) -match "failed|Unexpected error|error occurred")) {
                throw "LocalDB instance '$instanceName' is not usable. Run scripts\reset-dev-db.ps1, then retry scripts\start-api.ps1. Details: $($infoOutput | Out-String)"
            }

            $startOutput = & sqllocaldb start $instanceName 2>&1
            if ($LASTEXITCODE -ne 0 -or (($startOutput | Out-String) -match "failed|Unexpected error|error occurred")) {
                throw "Could not start LocalDB instance '$instanceName'. Run scripts\reset-dev-db.ps1, then retry scripts\start-api.ps1. Details: $($startOutput | Out-String)"
            }

            $sqlcmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
            if ($sqlcmd) {
                $serverName = "(localdb)\$instanceName"
                $probeOutput = & $sqlcmd.Source -S $serverName -E -Q "SET NOCOUNT ON; SELECT 1;" -l 10 -b 2>&1
                if ($LASTEXITCODE -ne 0) {
                    throw "LocalDB instance '$instanceName' started but SQL login/connectivity failed for '$serverName'. Run scripts\reset-dev-db.ps1 if this reports registry or SSPI errors. Details: $($probeOutput | Out-String)"
                }
            }
        }
    }
}

$lifeProofEnv = " && set `"RateLimits__Auth__Backend=InMemory`" && set `"RateLimits__Auth__AllowInMemoryFallback=true`" && set `"RateLimits__Auth__Register__PermitLimit=1000`" && set `"RateLimits__Auth__Login__PermitLimit=1000`" && set `"RateLimits__Auth__Refresh__PermitLimit=1000`""
$networkEnv = " && set `"HTTP_PROXY=`" && set `"HTTPS_PROXY=`" && set `"ALL_PROXY=`" && set `"NO_PROXY=localhost,127.0.0.1`""

$cmd = "/c cd /d `"$repo`" && set `"ASPNETCORE_ENVIRONMENT=Development`"$databaseEnv$lifeProofEnv$networkEnv && `"C:\Program Files\dotnet\dotnet.exe`" run --project `"$project`" --launch-profile http --no-build > `"$log`" 2>&1"
Start-Process -FilePath "cmd.exe" -ArgumentList $cmd -WindowStyle Hidden

Write-Host "Orka API starting. LOG=$log"
exit 0
