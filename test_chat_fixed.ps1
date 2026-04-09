$ApiUrl = "http://localhost:5065/api"

# 1. Register or Login
$loginBody = @{
    email = "test.awaitingchoice2@orka.api"
    password = "TestPassword123!"
    firstName = "Test"
    lastName = "User"
} | ConvertTo-Json

$token = ""

try {
    $registerRes = Invoke-RestMethod -Uri "$ApiUrl/auth/register" -Method Post -Body $loginBody -ContentType "application/json"
    $token = $registerRes.token
} catch {
    $loginBodyLogin = @{
        email = "test.awaitingchoice2@orka.api"
        password = "TestPassword123!"
    } | ConvertTo-Json
    $loginRes = Invoke-RestMethod -Uri "$ApiUrl/auth/login" -Method Post -Body $loginBodyLogin -ContentType "application/json"
    $token = $loginRes.token
}

Write-Host "Got token: $($token.Substring(0, 15))..."

$headers = @{
    Authorization = "Bearer $token"
}

# 2. Start a New Topic with "C# Generics"
$chatBody = @{
    content = "C# Generics çalışmak istiyorum"
} | ConvertTo-Json

$chatRes = Invoke-RestMethod -Uri "$ApiUrl/chat/message" -Method Post -Body $chatBody -ContentType "application/json" -Headers $headers

Write-Host "-------------------------------------"
Write-Host "AI RESPONSE FOR NEW TOPIC:"
Write-Output $chatRes.content
Write-Host "-------------------------------------"
Write-Host "Topic ID: $($chatRes.topicId)"
Write-Host "Session ID: $($chatRes.sessionId)"

# 3. Simulate Choosing Option 1
$planBody = @{
    content = "1"
    sessionId = $chatRes.sessionId
    topicId = $chatRes.topicId
} | ConvertTo-Json

$planRes = Invoke-RestMethod -Uri "$ApiUrl/chat/message" -Method Post -Body $planBody -ContentType "application/json" -Headers $headers

Write-Host "-------------------------------------"
Write-Host "AI RESPONSE FOR CHOICE '1':"
Write-Output $planRes.content
Write-Host "-------------------------------------"
