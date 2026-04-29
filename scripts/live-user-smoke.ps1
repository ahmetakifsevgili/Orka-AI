param(
    [string]$BaseUrl = "http://localhost:5065",
    [switch]$IncludeAi
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http

function Invoke-Json {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Url,
        [object]$Body = $null,
        [hashtable]$Headers = @{},
        [int]$TimeoutSec = 12
    )

    $params = @{
        Method = $Method
        Uri = $Url
        Headers = $Headers
        TimeoutSec = $TimeoutSec
        UseBasicParsing = $true
    }

    if ($null -ne $Body) {
        $params.ContentType = "application/json"
        $params.Body = ($Body | ConvertTo-Json -Depth 20)
    }

    Invoke-RestMethod @params
}

function Assert-HasValue {
    param(
        [object]$Value,
        [string]$Message
    )
    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        throw $Message
    }
}

Write-Host "[live-user-smoke] Diagnostics..."
$diagnostics = Invoke-Json -Method GET -Url "$BaseUrl/api/dev/diagnostics/config" -TimeoutSec 25
if (-not $diagnostics.database.canConnect) {
    $dbError = $diagnostics.database.error
    if ([string]::IsNullOrWhiteSpace([string]$dbError)) { $dbError = "no detailed error returned" }
    throw "Database is not connectable; live user smoke requires SQL readiness. Provider=$($diagnostics.database.provider); Error=$dbError"
}

$configuredProviders = @($diagnostics.providers | Where-Object { $_.configured })
$hasConfiguredAiProvider = $IncludeAi -and $configuredProviders.Count -gt 0
if ($configuredProviders.Count -eq 0) {
    Write-Warning "No AI provider key is configured. AI chat/source ask/TTS quality must be tested after keys are added."
}

Write-Host "[live-user-smoke] Auth register/login..."
$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$email = "live-smoke-$stamp@orka.local"
$password = "LiveSmoke123!"

$register = Invoke-Json -Method POST -Url "$BaseUrl/api/auth/register" -Body @{
    firstName = "Live"
    lastName = "Smoke"
    email = $email
    password = $password
}
Assert-HasValue $register.token "Register did not return token."

$login = Invoke-Json -Method POST -Url "$BaseUrl/api/auth/login" -Body @{
    email = $email
    password = $password
}
Assert-HasValue $login.token "Login did not return token."

$headers = @{ Authorization = "Bearer $($login.token)" }
$me = Invoke-Json -Method GET -Url "$BaseUrl/api/user/me" -Headers $headers
if ($me.email -ne $email) {
    throw "User/me returned unexpected email: $($me.email)"
}

Write-Host "[live-user-smoke] Topic, Wiki, Source upload..."
$topic = Invoke-Json -Method POST -Url "$BaseUrl/api/topics" -Headers $headers -Body @{
    title = "Live Smoke Kesirler"
    emoji = "M"
    category = "QA"
}
Assert-HasValue $topic.id "Topic create did not return id."
$topicId = [string]$topic.id

[void](Invoke-Json -Method GET -Url "$BaseUrl/api/wiki/$topicId" -Headers $headers)

$tempFile = Join-Path $env:TEMP "orka-live-smoke-$stamp.md"
@"
# Kesirler Canli Smoke

Payda esitleme, farkli paydali kesirleri toplarken once ortak payda bulma adimidir.
Ornek: 1/2 + 1/4 isleminde ortak payda 4 olur ve sonuc 3/4 olarak yazilir.
"@ | Set-Content -Path $tempFile -Encoding UTF8

$client = [System.Net.Http.HttpClient]::new()
try {
    $client.Timeout = [TimeSpan]::FromSeconds(15)
    $client.DefaultRequestHeaders.Authorization =
        [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $login.token)

    $multipart = [System.Net.Http.MultipartFormDataContent]::new()
    $multipart.Add([System.Net.Http.StringContent]::new($topicId), "TopicId")
    $bytes = [System.IO.File]::ReadAllBytes($tempFile)
    $fileContent = [System.Net.Http.ByteArrayContent]::new($bytes)
    $fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("text/markdown")
    $multipart.Add($fileContent, "File", [System.IO.Path]::GetFileName($tempFile))

    $uploadResponse = $client.PostAsync("$BaseUrl/api/sources/upload", $multipart).GetAwaiter().GetResult()
    $uploadText = $uploadResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if (-not $uploadResponse.IsSuccessStatusCode) {
        throw "Source upload failed: $([int]$uploadResponse.StatusCode) $uploadText"
    }
    $source = $uploadText | ConvertFrom-Json
}
finally {
    $client.Dispose()
    Remove-Item -Force $tempFile -ErrorAction SilentlyContinue
}

Assert-HasValue $source.id "Source upload did not return id."
if ($source.status -ne "ready") {
    throw "Source upload status is not ready: $($source.status)"
}
if ([int]$source.chunkCount -lt 1) {
    throw "Source upload did not create chunks."
}

$sources = Invoke-Json -Method GET -Url "$BaseUrl/api/sources/topic/$topicId" -Headers $headers
if (@($sources).Count -lt 1) {
    throw "Topic source list is empty after upload."
}

$page = Invoke-Json -Method GET -Url "$BaseUrl/api/sources/$($source.id)/pages/1" -Headers $headers
if (@($page.chunks).Count -lt 1) {
    throw "Source page did not return chunks."
}

Write-Host "[live-user-smoke] Quiz metadata and learning summary..."
$quizRunId = [guid]::NewGuid().ToString()
[void](Invoke-Json -Method POST -Url "$BaseUrl/api/quiz/attempt" -Headers $headers -Body @{
    messageId = "live-smoke"
    quizRunId = $quizRunId
    topicId = $topicId
    questionId = "live-fractions-1"
    question = "1/2 + 1/4 islemini coz."
    selectedOptionId = "A) 2/6"
    isCorrect = $false
    explanation = "Payda esitleme eksik."
    skillTag = "kesirlerde-payda-esitleme"
    topicPath = "Matematik > Kesirler > Payda esitleme"
    difficulty = "orta"
    cognitiveType = "uygulama"
    questionHash = "live-smoke-fractions-001"
})

$summary = Invoke-Json -Method GET -Url "$BaseUrl/api/learning/topic/$topicId/summary" -Headers $headers
if ([int]$summary.totalAttempts -lt 1) {
    throw "Learning summary did not count quiz attempt."
}

$recommendations = Invoke-Json -Method GET -Url "$BaseUrl/api/wiki/$topicId/recommendations" -Headers $headers
Assert-HasValue $recommendations.topicId "Wiki recommendations did not return topicId."

Write-Host "[live-user-smoke] Classroom bridge..."
$classroom = Invoke-Json -Method POST -Url "$BaseUrl/api/classroom/session" -Headers $headers -Body @{
    topicId = $topicId
    transcript = "[HOCA]: Payda esitleme anlatiyoruz.`n[ASISTAN]: Ben ornekle pekistireyim.`n[KONUK]: Sinavda bu adim hiz kazandirir."
}
Assert-HasValue $classroom.id "Classroom session did not return id."

if ($hasConfiguredAiProvider) {
    $classAnswer = Invoke-Json -Method POST -Url "$BaseUrl/api/classroom/$($classroom.id)/ask" -Headers $headers -TimeoutSec 20 -Body @{
        question = "Bu kismi anlamadim."
        activeSegment = "[HOCA]: Ortak paydayi buluyoruz."
    }
    $classAnswerText = $classAnswer.answer
    if ([string]::IsNullOrWhiteSpace($classAnswerText)) {
        $classAnswerText = $classAnswer | ConvertTo-Json -Depth 10
    }
    if ($classAnswerText -notmatch "\[HOCA\]:" -or $classAnswerText -notmatch "\[ASISTAN\]:") {
        throw "Classroom answer did not preserve HOCA/ASISTAN speakers."
    }

    $interactionId = $classAnswer.interactionId
    if (-not $interactionId) { $interactionId = $classAnswer.InteractionId }
    if ($interactionId) {
        $audioStatus = $null
        try {
            $audioResponse = Invoke-WebRequest `
                -Method GET `
                -Uri "$BaseUrl/api/classroom/interaction/$interactionId/audio" `
                -Headers $headers `
                -TimeoutSec 10
            $audioStatus = [int]$audioResponse.StatusCode
        }
        catch {
            if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
                $audioStatus = [int]$_.Exception.Response.StatusCode
            }
            else {
                throw
            }
        }

        if ($audioStatus -ne 200 -and $audioStatus -ne 404) {
            throw "Classroom interaction audio endpoint returned unexpected status: $audioStatus"
        }
    }
}
else {
    Write-Warning "Skipping classroom ask. Pass -IncludeAi after provider keys/network are confirmed."
}

Write-Host "[live-user-smoke] Code validation and dashboard..."
$badCodeStatus = $null
try {
    $badCode = Invoke-WebRequest `
        -Method POST `
        -Uri "$BaseUrl/api/code/run" `
        -Headers $headers `
        -ContentType "application/json" `
        -Body (@{ topicId = $topicId; code = ""; language = "csharp" } | ConvertTo-Json) `
        -TimeoutSec 10
    $badCodeStatus = $badCode.StatusCode
}
catch [System.Net.WebException] {
    if ($_.Exception.Response) {
        $badCodeStatus = [int]$_.Exception.Response.StatusCode
    }
    else {
        throw
    }
}
if ($badCodeStatus -ne 400) {
    throw "Expected empty code validation to return 400, got $badCodeStatus"
}

$dashboard = Invoke-Json -Method GET -Url "$BaseUrl/api/dashboard/stats" -Headers $headers
Assert-HasValue $dashboard.learningSignalBook "Dashboard did not include learningSignalBook."

Write-Host "[live-user-smoke] Live user smoke passed."
