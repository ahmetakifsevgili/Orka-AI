param(
    [switch]$Enable,
    [string]$Configuration = "Debug",
    [string]$Filter = "ExternalProviderIntegrationTests"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$enabledByEnv = [string]::Equals($env:ORKA_RUN_EXTERNAL_PROVIDER_TESTS, "true", [StringComparison]::OrdinalIgnoreCase)

if (-not $Enable -and -not $enabledByEnv) {
    Write-Host "[provider-live-smoke] Skipped. Pass -Enable or set ORKA_RUN_EXTERNAL_PROVIDER_TESTS=true to run live provider checks."
    exit 0
}

$env:ORKA_RUN_EXTERNAL_PROVIDER_TESTS = "true"

$providerKeys = @(
    "ORKA_EXTERNAL_GITHUB_MODELS_TOKEN",
    "AI__GitHubModels__Token",
    "ORKA_EXTERNAL_OPENROUTER_API_KEY",
    "AI__OpenRouter__ApiKey",
    "ORKA_EXTERNAL_COHERE_API_KEY",
    "AI__Cohere__ApiKey",
    "ORKA_EXTERNAL_GROQ_API_KEY",
    "AI__Groq__ApiKey",
    "ORKA_EXTERNAL_MISTRAL_API_KEY",
    "AI__Mistral__ApiKey"
)

Write-Host "[provider-live-smoke] Live provider checks are enabled. Token presence only; values are never printed."
foreach ($name in $providerKeys) {
    $configured = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))
    Write-Host ("[provider-live-smoke] {0} configured={1}" -f $name, $configured.ToString().ToLowerInvariant())
}

Write-Host "[provider-live-smoke] Gemini is intentionally excluded from the default live smoke. Enable and test it separately only when quota/auth are verified."

$dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
& $dotnet test (Join-Path $root "Orka.API.Tests\Orka.API.Tests.csproj") `
    --configuration $Configuration `
    --no-restore `
    --verbosity minimal `
    --filter $Filter

exit $LASTEXITCODE
