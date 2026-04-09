
$aiConfigs = @(
    @{ Name = "Groq"; ApiKey = "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"; Url = "https://api.groq.com/openai/v1/chat/completions"; Model = "llama-3.3-70b-versatile" },
    @{ Name = "SambaNova"; ApiKey = "222d0e04-8a41-49ce-aea1-3588a57c330a"; Url = "https://api.sambanova.ai/v1/chat/completions"; Model = "Meta-Llama-3.3-70B-Instruct" },
    @{ Name = "Cerebras"; ApiKey = "csk-6jrhj3nd9cjrxy3586w5jwm99c24mree3kcc9retdmd4649r"; Url = "https://api.cerebras.ai/v1/chat/completions"; Model = "llama3.1-8b" },
    @{ Name = "OpenRouter"; ApiKey = "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"; Url = "https://openrouter.ai/api/v1/chat/completions"; Model = "anthropic/claude-3-5-haiku" },
    @{ Name = "Mistral"; ApiKey = "wVFeIqR9B7dezvL20HJNsZzRGf7myRUc"; Url = "https://api.mistral.ai/v1/chat/completions"; Model = "mistral-small-latest" }
)

foreach ($config in $aiConfigs) {
    Write-Host "Testing $($config.Name)..." -ForegroundColor Cyan
    $headers = @{
        "Authorization" = "Bearer $($config.ApiKey)"
        "Content-Type"  = "application/json"
    }
    
    $body = @{
        model = $config.Model
        messages = @(
            @{ role = "user"; content = "say hi" }
        )
        max_tokens = 5
    } | ConvertTo-Json

    try {
        $response = Invoke-RestMethod -Uri $config.Url -Method Post -Headers $headers -Body $body -ErrorAction Stop
        Write-Host "Success: $($response.choices[0].message.content)" -ForegroundColor Green
    } catch {
        Write-Host "FAILED: $($config.Name)" -ForegroundColor Red
        if ($_.Exception.Response) {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $errorBody = $reader.ReadToEnd()
            Write-Host "Error Body: $errorBody" -ForegroundColor Yellow
        } else {
            Write-Host "Error Message: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
    Write-Host "-------------------------------------------"
}
