$ErrorActionPreference = "Stop"

function Wait-Url {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [int]$TimeoutSeconds = 25
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastError = $null

    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                return $response
            }
        }
        catch {
            $lastError = $_.Exception.Message
            Start-Sleep -Milliseconds 500
        }
    }

    throw "URL did not become ready in ${TimeoutSeconds}s: $Url. Last error: $lastError"
}

Write-Host "[live-smoke] Checking API live health..."
$live = Wait-Url "http://localhost:5065/health/live"
if ($live.StatusCode -ne 200) {
    throw "Expected /health/live 200, got $($live.StatusCode)"
}

Write-Host "[live-smoke] Checking Swagger JSON..."
$swagger = Wait-Url "http://localhost:5065/swagger/v1/swagger.json"
if ($swagger.Content -notmatch '"/api/auth/login"') {
    throw "Swagger JSON does not contain /api/auth/login"
}
if ($swagger.Content -notmatch '"/api/classroom/session"') {
    throw "Swagger JSON does not contain /api/classroom/session"
}

Write-Host "[live-smoke] Checking frontend dev server..."
$front = Wait-Url "http://localhost:3000/"
if ($front.Content -notmatch '<div id="root">' -and $front.Content -notmatch 'src="/src/') {
    throw "Frontend root HTML does not look like Vite app shell."
}

Write-Host "[live-smoke] Live smoke passed."
