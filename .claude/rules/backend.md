# Backend Kuralları — C# / .NET 8 / Orka Mimarisi

## Katman Yapısı (Zorunlu)

```
Orka.Core           → Saf domain: Entity, Interface, Enum, DTO, Event
Orka.Infrastructure → Implementasyonlar: Service, DbContext, Migrations, SK Plugins
Orka.API            → HTTP katmanı: Controller, Middleware, Program.cs
```

**Bağımlılık yönü:** API → Infrastructure → Core. Core hiçbir zaman Infrastructure'a bağımlı olmaz.

## Dependency Injection Kuralları

- Tüm DI kayıtları **`Program.cs`** içindedir — başka yerde kayıt yapılmaz.
- Scoped servisler `AddScoped<IFoo, Foo>()` ile kayıt edilir.
- `SummarizerAgent` ve `AnalyzerAgent` `AddScoped` olarak kayıtlıdır ama `Task.Run` içinde kullanılırken **`IServiceScopeFactory`** ile yeni scope açılır (background task pattern).
- `IServiceScopeFactory` inject eden servisler thread-safe olarak her metodunda `using var scope = _scopeFactory.CreateScope();` açar.
- Singleton pattern gerekiyorsa `ConcurrentDictionary` gibi thread-safe yapılar kullanılır (bkz. `SummarizerAgent._inProgress`).

## Agent Mimarisi

| Agent | Sorumluluğu |
|---|---|
| `AgentOrchestratorService` | Merkezi routing — tüm `ProcessMessage*` akışları buradan geçer |
| `TutorAgent` | Ders anlatımı, quiz üretimi, cevap değerlendirme |
| `DeepPlanAgent` | Müfredat planı oluşturma (`GenerateAndSaveDeepPlanAsync`) |
| `AnalyzerAgent` | Sohbet tamamlanma analizi (`AnalyzeCompletionAsync`) |
| `SummarizerAgent` | Post-completion wiki özeti üretimi (idempotent) |
| `WikiAgent` | Wiki belgesine dayalı soru cevaplama (SSE stream) |
| `KorteksAgent` | İnternet araştırması + wiki entegrasyonu |

**Kural:** `RouterService` yalnızca `IGroqService`/`IAIService` inject eder — Agent/Orchestrator bağımlılığı yasaktır.

## Session State Machine

```
Değerler: Learning · QuizPending · QuizMode · BaselineQuizMode · AwaitingChoice
```

- State kontrolü `AgentOrchestratorService.ProcessMessageStreamAsync` içinde `isPlanMode`'dan **önce** yapılır.
- State geçişleri yalnızca `AgentOrchestratorService` içindeki özel `Handle*` metotlarından gerçekleşir.
- `session.TopicId` her zaman **parent** topic'i gösterir; alt konu gezintisi `CompletedSections` index'i ile yapılır.

## SSE Stream Protokolü

```csharp
// Yazım formatı:
await Response.WriteAsync($"data: {chunk}\n");
await Response.WriteAsync("\n");
await Response.Body.FlushAsync();

// Özel sinyaller (frontend bunları parse eder):
[THINKING: mesaj...]   → UI'da thinking state günceller, chat'e yazılmaz
[PLAN_READY]           → Müfredat hazır bildirimi + toast
[TOPIC_COMPLETE:guid]  → Konu tamamlandı → wiki kısayol kartı
[ERROR]: mesaj         → Hata bildirimi
[DONE]                 → Stream sonu (opsiyonel)
```

## Wiki Üretimi — Temel Kurallar

- **`AutoUpdateWikiAsync`** kaldırıldı — mesaj başına wiki üretimi yapılmaz.
- Wiki yalnızca şu koşullarda üretilir:
  1. Alt konu quiz'i geçildiğinde (`HandleQuizModeAsync` → `SummarizeAndSaveWikiAsync`)
  2. `AnalyzeCompletionAsync` TRUE dönerse (`TriggerBackgroundTasks`)
- `SummarizeAndSaveWikiAsync` **idempotent**'tir: `ConcurrentDictionary` kilit + DB kontrolü ile çift üretim engellenir.

## AI Servis Zinciri

Öncelik sırası: **Groq (Primary) → Mistral → SambaNova → Cerebras → OpenRouter (Fallback)**

- Tüm AI modelleri `appsettings.json > AI` bölümünde yapılandırılır.
- Chaos Monkey için `X-Chaos-Fail` header'ı ile belirli provider devre dışı bırakılabilir.
- Groq: `llama-3.3-70b-versatile` — ana ders anlatımı
- OpenRouter: Ajan'a özel modeller (`AI:OpenRouter:Agents:*:Model`) — DeepPlan, Wiki, Analyzer

## Semantic Kernel Kullanımı

- SK Plugin'leri `Orka.Infrastructure/SemanticKernel/Plugins/` altındadır.
- Plugin'ler DI'a `AddScoped<WikiPlugin>()` gibi kayıt edilir.
- `KorteksAgent` Tavily arama API'sini `TavilySearchPlugin` üzerinden kullanır.

## MediatR Event Sistemi

- Domain event'leri `Orka.Core/Events/` altında tanımlanır.
- Handler'lar `Orka.Infrastructure/Handlers/` altındadır.
- Mevcut event: `TopicCompletedEvent` → `TopicCompletedHandler`
- MediatR kayıtları `Program.cs`'de her iki assembly için yapılır.

## Veritabanı Kuralları

- **EF Core Code-First** — migration'lar `Orka.Infrastructure/Data/Migrations/` altındadır.
- `OrkaDbContext` doğrudan `using var scope = ...` içinde erişilir (scope-per-operation pattern).
- Repository pattern kullanılmaz — `DbContext` doğrudan inject edilir.
- Connection string: `appsettings.json > ConnectionStrings > DefaultConnection` (SQL Server LocalDB).
- Yeni migration: `cd Orka.Infrastructure && dotnet ef migrations add <İsim> --startup-project ../Orka.API`

## Controller Kuralları

- Tüm controller'lar `[Authorize]` attribute'u ile korunur (istisnalar `[AllowAnonymous]` ile işaretlenir).
- `userId` her zaman `Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)` ile alınır.
- SSE stream endpoint'leri `async Task` (void-return) olarak tanımlanır, `IActionResult` değil.
- Streaming response header seti: `Content-Type: text/event-stream`, `Cache-Control: no-cache`, `Connection: keep-alive`.

## Hata Yakalama

- Global hata yönetimi `ExceptionMiddleware` üzerinden yapılır.
- `Task.Run` fire-and-forget bloklarında her zaman `try/catch` bulundurulur ve `_logger.LogError` çağrılır.
- Background task'ta exception fırlatılması durumunda ana akış kesilmez.

## Adlandırma Kuralları

```csharp
// Interface → I prefix
IWikiService, IAgentOrchestrator

// Agent implementasyonları
TutorAgent, WikiAgent (soyut isim + Agent suffix)

// DTO'lar
SendMessageRequest, ChatMessageResponse (Request/Response suffix)

// Enum'lar
SessionState.Learning, WikiBlockType.Concept (PascalCase değerler)
```
