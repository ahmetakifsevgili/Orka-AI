# SKILL: Yeni AI Servisi Ekle
## Bu pattern'i takip et

---

## Ne zaman kullanılır?
Yeni bir AI API entegrasyonu eklenecekse bu skill'i oku.

---

## Adımlar

### 1. Interface (Orka.Core/Interfaces/)
```csharp
public interface IYeniService
{
    Task<AIResponse> ChatAsync(
        List<ContextMessage> context,
        string userMessage,
        string systemPrompt,
        string model);
}
```

### 2. Implementation (Orka.Infrastructure/Services/AI/)
```csharp
public class YeniService : IYeniService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private const string BaseUrl = "https://api.yeni.com/v1/...";

    public YeniService(IConfiguration config, HttpClient http)
    {
        _http = http;
        _apiKey = config["AI:Yeni:ApiKey"]
            ?? throw new InvalidOperationException("Yeni API key eksik");
    }

    public async Task<AIResponse> ChatAsync(
        List<ContextMessage> context,
        string userMessage,
        string systemPrompt,
        string model)
    {
        // API çağrısı yap
        var res = await _http.PostAsJsonAsync(BaseUrl, body);
        res.EnsureSuccessStatusCode();

        var data = await res.Content.ReadFromJsonAsync<YeniResponse>();
        var content = data?.Result ?? "Hata oluştu.";

        return new AIResponse(content, model, 0, 0m);
    }
}
```

### 3. appsettings.json
```json
"AI": {
    "Yeni": {
        "ApiKey": "",
        "Model": "model-adı",
        "BaseUrl": "https://api.yeni.com"
    }
}
```

### 4. Program.cs DI Kaydı
```csharp
builder.Services.AddHttpClient<IYeniService, YeniService>();
```

### 5. RouterService'e Ekle
```csharp
// Hangi intent için kullanılacak?
if (intent == "yeni_intent")
{
    return await _yeni.ChatAsync(context, message, prompt, model);
}
```

---

## Önemli Kurallar
- Her zaman try/catch ile sar
- Rate limit → RateLimitException fırlat
- Hata → bir üst seviyede fallback devreye girer
- TokensUsed'ı kaydet (0 bile olsa)
