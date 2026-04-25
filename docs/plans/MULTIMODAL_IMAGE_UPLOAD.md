# Multimodal Image Upload — Detaylı Uygulama Planı

## Özet
Kullanıcı chat composer'dan resim yükleyebilmeli, AI bu resmi analyze edip soruya/probleme cevap verebilmeli. Vision-capable model (GitHub Models Gpt-4-vision veya fallback Gemini) routing'i gerek.

---

## 1. Frontend Değişiklikleri

### 1.1 Composer (ChatPanel.tsx)

**Mevcut durum:**
```tsx
<input 
  placeholder="Bir şey sor..."
  onKeyDown={handleKeyDown}
/>
<button onClick={handleSend}>Send</button>
```

**Hedef:**
```tsx
<input type="file" accept="image/*" ref={fileInputRef} hidden />

<div className="flex gap-2 items-center">
  <button onClick={() => fileInputRef.current?.click()}>
    📎 Resim Ekle
  </button>
  <input 
    placeholder="Resimi açıkla veya soru sor..."
    value={inputText}
  />
  <button onClick={handleSend} disabled={!inputText && !selectedImage}>
    Gönder
  </button>
</div>

{selectedImage && (
  <div className="mt-2 flex items-center gap-2">
    <img src={selectedImage.preview} className="h-12 rounded" />
    <button onClick={clearImage}>✕</button>
  </div>
)}
```

**State eklemeleri:**
```tsx
const [selectedImage, setSelectedImage] = useState<{
  file: File;
  preview: string;
} | null>(null);

const handleImageSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
  const file = e.target.files?.[0];
  if (!file) return;
  
  // Max 5MB, image/* validation
  if (file.size > 5 * 1024 * 1024) {
    toast.error("Resim 5MB'den büyük olamaz");
    return;
  }
  
  const preview = URL.createObjectURL(file);
  setSelectedImage({ file, preview });
};

const handleSend = async () => {
  if (!inputText && !selectedImage) return;
  
  // Multipart gönderimi (aşağıda)
  const formData = new FormData();
  formData.append("message", inputText || "");
  if (selectedImage) {
    formData.append("image", selectedImage.file);
  }
  
  const response = await ChatAPI.sendMessageWithImage(formData, activeTopic.id);
  
  setSelectedImage(null);
  setInputText("");
};
```

### 1.2 API Service (services/api.ts)

**Yeni endpoint:**
```ts
export const ChatAPI = {
  // Mevcut text-only endpoint
  sendMessage: (data: SendMessageRequest) => 
    api.post("/api/chat/send", data),
  
  // YENİ: Multipart image support
  sendMessageWithImage: (formData: FormData, topicId: Guid) => 
    api.post("/api/chat/send-with-image", formData, {
      headers: { "Content-Type": "multipart/form-data" }
    }),
  
  // Fallback: base64 encoded (opsiyonel)
  sendMessageWithBase64Image: (data: {
    message: string;
    imageBase64: string;
    imageMimeType: string;
    topicId: Guid;
  }) => api.post("/api/chat/send-with-image-base64", data),
};
```

### 1.3 Tipleri Güncelle (lib/types.ts)

```ts
export interface SendMessageRequest {
  content: string;
  sessionId: Guid;
  topicId: Guid;
  // YENİ
  imageBase64?: string;
  imageMimeType?: string;
}

export interface ChatMessage {
  id: string;
  role: "user" | "ai";
  type: MessageType;
  content: string;
  // YENİ
  imageUrl?: string;
  imageAlt?: string;
  // ...
}
```

### 1.4 ChatMessage Render (ChatMessage.tsx)

User mesajı resim içeriyorsa göster:
```tsx
{message.role === "user" && message.imageUrl && (
  <img 
    src={message.imageUrl} 
    alt={message.imageAlt} 
    className="max-w-64 h-auto rounded-lg mt-2 border border-zinc-700/60"
  />
)}
```

---

## 2. Backend Değişiklikleri

### 2.1 Controller — ChatController.cs

**Yeni endpoint:**
```csharp
[Authorize]
[HttpPost("send-with-image")]
public async Task SendMessageWithImageStream(
    [FromQuery] Guid topicId,
    IFormFile? image,
    [FromForm] string message)
{
    var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    
    // Validasyon
    if (string.IsNullOrWhiteSpace(message) && image == null)
        return BadRequest(new { error = "Mesaj veya resim gerekli" });
    
    if (image != null)
    {
        // Resim validasyonu
        if (image.Length > 5 * 1024 * 1024)
            return BadRequest(new { error = "Resim 5MB'den büyük" });
        
        var contentType = image.ContentType.ToLowerInvariant();
        if (!contentType.StartsWith("image/"))
            return BadRequest(new { error = "Dosya resim olmalı" });
    }
    
    // Resim → Base64 (geçici veya Azure blob store)
    string? imageBase64 = null;
    string? imageMimeType = null;
    
    if (image != null)
    {
        using var ms = new MemoryStream();
        await image.CopyToAsync(ms);
        imageBase64 = Convert.ToBase64String(ms.ToArray());
        imageMimeType = image.ContentType;
    }
    
    // Orchestrator'a delegat (image context ile)
    await _orchestrator.ProcessMessageStreamAsync(
        userId, 
        topicId, 
        message, 
        HttpContext.Response,
        imageBase64,    // YENİ param
        imageMimeType   // YENİ param
    );
}
```

### 2.2 AgentOrchestratorService.cs

**Signature güncelle:**
```csharp
public async Task ProcessMessageStreamAsync(
    Guid userId,
    Guid topicId,
    string content,
    HttpResponse response,
    string? imageBase64 = null,
    string? imageMimeType = null)
{
    // ... mevcut logic ...
    
    // Resim varsa session'a kontekst olarak ekle
    string? imageContext = null;
    if (!string.IsNullOrEmpty(imageBase64))
    {
        imageContext = $"[Resim: {imageMimeType}] (Base64 uzunluğu: {imageBase64.Length} char)";
        // Veya: vision-capable agent'e direkt Base64 gönder
    }
    
    // GetResponseStreamAsync'e imageBase64 ilet
    await foreach (var chunk in _tutorAgent.GetResponseStreamAsync(
        userId, 
        content, 
        session, 
        isQuizPending,
        imageBase64,      // YENİ
        imageMimeType))   // YENİ
    {
        // stream
    }
}
```

### 2.3 TutorAgent.cs — Vision Support

**Signature güncelle:**
```csharp
public async IAsyncEnumerable<string> GetResponseStreamAsync(
    Guid userId, 
    string content, 
    Session session, 
    bool isQuizPending,
    string? imageBase64 = null,       // YENİ
    string? imageMimeType = null,     // YENİ
    [EnumeratorCancellation] CancellationToken ct = default)
{
    // Mevcut context building
    var contextTask = _contextBuilder.BuildConversationContextAsync(session);
    // ... parallel tasks ...
    
    var systemPrompt = BuildTutorSystemPrompt(
        isQuizPending,
        memoryContext: parallelResults[0],
        // ... diğer params ...
    );
    
    // Kullanıcı mesajı + resim birlikte
    var userMessage = BuildContextSummary(contextMessages);
    
    if (!string.IsNullOrEmpty(imageBase64))
    {
        userMessage += $"\n\n[KULLANICI RESMİ]\nMIME: {imageMimeType}\nBase64: {imageBase64.Substring(0, Math.Min(100, imageBase64.Length))}...";
    }
    
    // Vision-capable model seçim (aşağıda)
    var agentRole = imageBase64 != null ? AgentRole.TutorWithVision : AgentRole.Tutor;
    
    await foreach (var chunk in _factory.StreamChatAsync(agentRole, systemPrompt, userMessage, ct))
    {
        yield return chunk;
    }
}
```

### 2.4 AIAgentFactory.cs — Vision Model Routing

**Enum ekle (Orka.Core/Enums/AgentRole.cs):**
```csharp
public enum AgentRole
{
    Tutor,
    TutorWithVision,  // YENİ
    DeepPlan,
    Analyzer,
    Summarizer,
    Wiki,
    Korteks,
    Evaluator,
    Grader,
}
```

**appsettings.json:**
```json
{
  "AI": {
    "Models": {
      "Tutor": {
        "Primary": "gpt-4-turbo",
        "Fallback1": "gpt-4o-mini"
      },
      "TutorWithVision": {
        "Primary": "gpt-4-vision",           // Vision support
        "Fallback1": "gemini-pro-vision",    // Vision support
        "Fallback2": "gpt-4o"                // Fallback (non-vision)
      },
      "DeepPlan": { /* ... */ }
    }
  }
}
```

**Factory logic:**
```csharp
private string SelectModelForRole(AgentRole role)
{
    return role switch
    {
        AgentRole.TutorWithVision => GetVisionModel(),
        AgentRole.Tutor => GetTextOnlyModel(),
        // ...
        _ => throw new NotSupportedException()
    };
}

private string GetVisionModel()
{
    // GitHub Models Gpt-4-vision kontrol (API key var mı?)
    if (HasApiKey("GitHub:ApiKey"))
        return "gpt-4-vision"; // GitHub Models variant
    
    // Fallback: Gemini vision
    if (HasApiKey("Google:ApiKey"))
        return "gemini-pro-vision";
    
    // Last resort: text-only model (cevap zayıf olacak)
    return GetTextOnlyModel();
}
```

### 2.5 Database — Message Model

**Orka.Core/Entities/Message.cs:**
```csharp
public class Message
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SessionId { get; set; }
    
    public string Content { get; set; } = "";
    public string Role { get; set; } = "user"; // "user" | "ai"
    
    // YENİ: Image metadata
    public string? ImageUrl { get; set; }          // Stored URL (Azure blob, local, etc)
    public string? ImageAlt { get; set; }          // Alt text for accessibility
    public string? ImageMimeType { get; set; }     // "image/jpeg", "image/png"
    public long? ImageSizeBytes { get; set; }      // Audit trail
    
    public DateTime CreatedAt { get; set; }
    public int TokenCount { get; set; }
    public decimal EstimatedCost { get; set; }
}
```

**EF Migration:**
```bash
cd Orka.Infrastructure
dotnet ef migrations add AddImageFieldsToMessage --startup-project ../Orka.API
```

### 2.6 Image Storage Strategy

**Seçenek A: Base64 embed (test, demo)**
- Cevap veritabanında base64 string olarak tutma
- Con: storage büyür, db yavaşlasa
- Pro: simple, no external dependency

**Seçenek B: Azure Blob Storage (production)**
- Resim → blob'a upload, referans URL kaydet
- Con: Azure subscription gerek
- Pro: scalable, fast serving, GDPR-friendly (delete after X days)

**Seçenek C: Local /uploads folder (staging)**
- Dev-friendly, basit
- Prod'da tavsiye edilmez (scaling issue)

**Önerilen:** Seçenek B (production) + Seçenek A (fallback test)

---

## 3. AI Prompt Engineering

### 3.1 System Prompt Güncellemesi

Vision-capable agents için:
```
[YENİ BÖLÜM — RESİM ANALIZ KAVRAMı]

Eğer kullanıcı bir resim gönderiyorsa:
1. Resmi analiz et (şema, grafik, fotoğraf, tablo vb.).
2. Anlatımı resim içeriğine bağla: "Gördüğünüz diyagramda..." gibi.
3. Şu resmi rehber olarak kullan ama asla "Resim şunu söylüyor" yazma; 
   içeriği aç ve kendi sözcükleriyle anlat.
4. Resimle ilgili soru var ise direkt cevapla. 
   Eğer kapsamı aşarsa: "Resim X gösteriyor ama Y hakkında daha fazla bilgiye ihtiyacın varsa..."

[YANLIŞ]
"Resim bunu gösteriyor: ..." (description parroting)

[DOĞRU]
"Gördüğünüz grafikteki eğri şunu temsil ediyor: ..." (analysis + teach)
```

### 3.2 Safety Guardrails

```csharp
// ContentSanitizer.cs'ye ekle
public static bool IsImageSafe(string base64, string mimeType)
{
    // 1. MIME type whitelist
    var allowedTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
    if (!allowedTypes.Contains(mimeType.ToLowerInvariant()))
        return false;
    
    // 2. Size check (5MB max)
    // Base64 string length ≈ 4/3 * binary size
    var estimatedBytes = (base64.Length * 3) / 4;
    if (estimatedBytes > 5 * 1024 * 1024)
        return false;
    
    // 3. File signature validation (magic bytes)
    try
    {
        var bytes = Convert.FromBase64String(base64.Substring(0, 100));
        return IsSafeImageHeader(bytes);
    }
    catch { return false; }
}
```

---

## 4. API Endpoint Özeti

| Method | Endpoint | Input | Output |
|---|---|---|---|
| POST | `/api/chat/send` | `{ message, sessionId, topicId }` | SSE stream (text) |
| POST | `/api/chat/send-with-image` | FormData (message, image) | SSE stream (text) |
| POST | `/api/chat/send-with-image-base64` | `{ message, imageBase64, imageMimeType, topicId }` | SSE stream (text) |

---

## 5. Frontend UX Flow

```
1. Kullanıcı compose alanına mesaj yazıyor
2. "📎" icon'a tıklar → file picker açılır
3. Resim seçer → preview gösterilir (thumbnail)
4. Mesajı tamamlayabilir (opsiyonel)
5. "Gönder" basıyor → FormData + image + text gidiyor
6. Backend: image → Base64 → vision model
7. AI: resmi analyzeediyor + cevabını yazıyor
8. SSE stream → UI'da mesaj + AI cevabı görünür
9. Chat history'ye kaydediliyor (image metadata + URL)
```

---

## 6. Testing Checklist

- [ ] Upload validation: >5MB reject, non-image reject
- [ ] Preview render: thumbnail gösterir, clear butonu çalışır
- [ ] API: FormData multipart doğru ayrıştırılmış
- [ ] Model routing: vision model seçildi (logs kontrol)
- [ ] Prompt injection: resim içinde malicious content → AI "I can't analyze this" demeli
- [ ] DB migration: Message tablo yeni alanlar var
- [ ] Message history: resim URL + metadata kaydedilmiş
- [ ] Edge case: empty message + image only (should work)
- [ ] Edge case: image only without message (prompt: "Lütfen bu resimi açıkla")

---

## 7. Faz Bağımlılıkları

| Faz | Task | Dependency |
|---|---|---|
| Frontend-Image | Composer UI + state | None |
| Backend-Endpoint | send-with-image endpoint | ✓ Frontend-Image |
| DB | Message.Image* fields + migration | ✓ Backend-Endpoint |
| Vision-Model | Factory + appsettings | ✓ DB |
| Prompt-Eng | System prompt update + safety | ✓ Vision-Model |
| Storage | Azure Blob / Local /uploads | ✓ Prompt-Eng (can run in parallel) |
| Testing | E2E: composer → vision → response | ✓ All above |

---

## 8. Known Limitations & Future

| Sınırlama | Workaround | Priority |
|---|---|---|
| Single image per message | Multiple image upload (v2) | Low |
| No image editing (crop/rotate) | Client-side Canvas API | Low |
| Base64 in logs (privacy) | Redact logs, hash image for tracking | Medium |
| Vision model fallback → text only | Poor UX, degraded response | High → add better fallback prompt |
| Image storage scaling | Implement Azure Blob Storage ASAP | High |

---

## 9. Dosya Checklist — Değiştirilecekler

**Frontend:**
- [ ] `Orka-Front/src/components/ChatPanel.tsx` — file input + state + FormData sender
- [ ] `Orka-Front/src/components/ChatMessage.tsx` — image render (user message)
- [ ] `Orka-Front/src/services/api.ts` — `sendMessageWithImage` endpoint
- [ ] `Orka-Front/src/lib/types.ts` — `SendMessageRequest`, `ChatMessage` update

**Backend:**
- [ ] `Orka.API/Controllers/ChatController.cs` — `send-with-image` endpoint
- [ ] `Orka.Infrastructure/Services/AgentOrchestratorService.cs` — image param pass
- [ ] `Orka.Infrastructure/Services/TutorAgent.cs` — vision param + model selection
- [ ] `Orka.Core/Enums/AgentRole.cs` — `TutorWithVision` enum value
- [ ] `Orka.Core/Entities/Message.cs` — Image* fields
- [ ] `appsettings.json` — TutorWithVision model config
- [ ] `Orka.Infrastructure/Services/ContentSanitizer.cs` — `IsImageSafe` method
- [ ] `Orka.Infrastructure/Migrations/` — new migration file (EF generated)

---

## 10. Örnek İş Akışı (Kod)

### Frontend
```tsx
const handleSend = async () => {
  const formData = new FormData();
  formData.append("message", inputText);
  if (selectedImage) {
    formData.append("image", selectedImage.file);
  }
  
  try {
    const response = await fetch("/api/chat/send-with-image", {
      method: "POST",
      body: formData,
      headers: { Authorization: `Bearer ${token}` },
    });
    
    const reader = response.body?.getReader();
    while (true) {
      const { done, value } = await reader?.read() ?? { done: true };
      if (done) break;
      const text = new TextDecoder().decode(value);
      // Parse SSE → setState
    }
  } catch (err) {
    toast.error("Gönderme başarısız: " + err.message);
  }
};
```

### Backend
```csharp
[HttpPost("send-with-image")]
public async Task SendMessageWithImageStream(Guid topicId, IFormFile? image, [FromForm] string message)
{
    var file = await image.GetBytes(); // byte[]
    var base64 = Convert.ToBase64String(file);
    
    await _orchestrator.ProcessMessageStreamAsync(userId, topicId, message, HttpContext.Response, base64, image.ContentType);
}
```

---

## Son Notlar

- **Tüm image URL'leri HTTPS olmalı** (unsecure mixed content tarayıcı bloklarken)
- **Vision API rate limits:** GitHub Models / Gemini quota'sını kontrol et
- **GDPR compliance:** User resimlerini max X gün sonra sil (configurable)
- **Concurrent uploads:** DB transaction'lar safe mi kontrol et

