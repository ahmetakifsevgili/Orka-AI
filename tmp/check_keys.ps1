$keys = @(
    @{ name = "OpenRouter"; url = "https://openrouter.ai/api/v1/chat/completions"; key = "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"; body = '{"model": "anthropic/claude-3-5-haiku", "messages": [{"role": "user", "content": "OK"}]}' },
    @{ name = "Groq"; url = "https://api.groq.com/openai/v1/chat/completions"; key = "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"; body = '{"model": "llama-3.3-70b-versatile", "messages": [{"role": "user", "content": "OK"}]}' },
    @{ name = "Mistral"; url = "https://api.mistral.ai/v1/chat/completions"; key = "wVFeIqR9B7dezvL20HJNsZzRGf7myRUc"; body = '{"model": "mistral-small-latest", "messages": [{"role": "user", "content": "OK"}]}' },
    @{ name = "Gemini 1"; url = "https://generativelanguage.googleapis.com/v1/models/gemini-1.5-flash:generateContent?key=AIzaSyAnZvZPYURvhhbqyoa3-mB2VXGl20dwjz0"; key = ""; body = '{"contents":[{"parts":[{"text":"OK"}]}]}' },
    @{ name = "Gemini 2"; url = "https://generativelanguage.googleapis.com/v1/models/gemini-1.5-flash:generateContent?key=AIzaSyCkyhYK_oQ4u_l1gJpgTGR7LNyQJjT-o24"; key = ""; body = '{"contents":[{"parts":[{"text":"OK"}]}]}' },
    @{ name = "Gemini 3"; url = "https://generativelanguage.googleapis.com/v1/models/gemini-1.5-flash:generateContent?key=AIzaSyAaIdl1getQdZPDRKjWKtTK9ZXADxgrKAE"; key = ""; body = '{"contents":[{"parts":[{"text":"OK"}]}]}' }
)

foreach ($item in $keys) {
    Write-Host "Testing $($item.name)..." -NoNewline
    $headers = @{ "Content-Type" = "application/json" }
    if ($item.key -ne "") {
        $headers.Add("Authorization", "Bearer $($item.key)")
    }
    
    try {
        $response = Invoke-RestMethod -Uri $item.url -Method Post -Headers $headers -Body $item.body
        Write-Host " [SAĞLAM]" -ForegroundColor Green
    } catch {
        $err = $_.Exception.Message
        if ($_.Exception.Response -ne $null) {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $err = $reader.ReadToEnd()
        }
        $status = "PATLAK"
        if ($err -like "*429*" -or $err -like "*quota*") { $status = "KOTA DOLU" }
        Write-Host " [$status] - $err" -ForegroundColor Red
    }
    Write-Host "-----------------------------------"
}
