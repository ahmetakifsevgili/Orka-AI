param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$msbuild = "C:\Program Files\dotnet\sdk\8.0.121\MSBuild.dll"

if (-not (Test-Path $msbuild)) {
    throw ".NET SDK 8.0.121 is required. Expected: $msbuild"
}

& "C:\Program Files\dotnet\dotnet.exe" exec $msbuild `
    (Join-Path $repo "Orka.API\Orka.API.csproj") `
    /t:Build `
    /p:Configuration=$Configuration `
    /p:RestoreIgnoreFailedSources=true `
    /v:minimal `
    /nr:false

exit $LASTEXITCODE
