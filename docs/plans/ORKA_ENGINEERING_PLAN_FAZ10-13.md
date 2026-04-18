# ORKA AI — Kurumsal Mühendislik Planı (Faz 10–13)
> Oluşturulma: 2026-04-16 | Durum: Onay Bekliyor
> Redis: 127.0.0.1:6379 ✅ AÇIK | SQL Server LocalDB: ✅ AÇIK

---

## BÖLÜM 1 — ANALİZ VE KAPSAM

### 1.1 Mevcut Sistem Sınırları

**Güçlü Yönler (değiştirilmeyecek):**
- JWT auth + `[Authorize]` tüm controller'larda mevcut
- `ExceptionMiddleware` global hata yakalıyor
- `AIAgentFactory` 3 provider failover — GitHub Models → Groq → Gemini
- `SummarizerAgent` idempotency guard (`ConcurrentDictionary` + DB kontrolü)
- `IntentClassifierAgent` tek LLM çağrısıyla iki ajana veri üretiyor (maliyet verimliliği)
- Redis `RecordEvaluationAsync` session başına feedback log

**Doğrulanan Açıklar (kod okundu, teyit edildi):**

| Açık | Tespit Yeri | Kurumsal Risk |
|---|---|---|
| `IRedisMemoryService` Infrastructure'da | `RedisMemoryService.cs:1-10` | Katman ihlali — Infrastructure→Infrastructure bağımlılık |
| `IEvaluatorAgent` Core'da yok | `IAgents.cs` (tam dosya) | DI kayıtları `typeof` ile doğrulanamaz |
| `AgentRole.Evaluator` yok | `AIAgentFactory.cs:42-49` | EvaluatorAgent, Grader modelini paylaşıyor |
| `TutorAgent` Wiki okumıyor | `TutorAgent.cs:46-52` | KorteksAgent araştırması kullanılmıyor |
| Gold Example yok | `EvaluatorAgent.cs:57-59` | Puan kaydediliyor ama few-shot havuzu oluşmuyor |
| `TriggerBackgroundTasks` hata takibi yok | `AgentOrchestratorService.cs:223-248` | Sessiz hata — Redis veya DB yazma başarısız olursa izlenemez |
| Correlation ID yok | Tüm logger çağrıları | Bir request'in ajan zinciri boyunca izlenmesi imkansız |
| HealthCheck endpoint yok | `Program.cs` | Redis, SQL, AI provider durumu bilinmiyor |
| Polly yok | `AIAgentFactory.cs` | Manuel try/catch — retry policy, circuit breaker eksik |

### 1.2 Kurumsal Gereksinimler (Faz 10–13 kapsamı)

```
[Fonksiyonel]
  F1. IRedisMemoryService ve IEvaluatorAgent → Core katmanına taşı
  F2. AgentRole.Evaluator + ayrı model konfigürasyonu
  F3. TutorAgent → WikiService + PistonContext bağlantısı
  F4. EvaluatorAgent → Gold Example yazımı (puan >= 9)
  F5. TutorAgent → Gold Example okuma + few-shot enjeksiyonu
  F6. EvaluatorAgent → tüm ajanları kapsama (Summarizer, DeepPlan, Korteks)
  F7. SkillMastery entity + migration
  F8. AnalyzerAgent IsComplete → session.CompletedSections ilerletme + otomatik ders başlatma
  F9. Quiz başarısı → SkillMastery kaydı

[Gözlemlenebilirlik]
  O1. Correlation ID — her request'e UUID atanır, tüm log satırlarına propagate edilir
  O2. Structured logging — SessionId, UserId, AgentRole, CorrelationId her log'da
  O3. /health endpoint — Redis ping + SQL ping + AI provider availability
  O4. Ajan pipeline metrikleri — her agent call süresi, puan dağılımı, fallback oranı

[Hata Toleransı]
  E1. TriggerBackgroundTasks başarısızlıkları izlenebilir olmalı
  E2. Redis erişilemez olursa TutorAgent graceful degradation yapmalı (wiki/gold olmadan çalışır)
  E3. WikiService dönemezse TutorAgent sessizce devam etmeli (null guard)

[Güvenlik]
  S1. AI provider API key'leri User Secrets (mevcut) — prod'da Azure Key Vault
  S2. /health endpoint'i sadece internal network'ten erişilebilir olmalı
```

### 1.3 Kapsam Dışı (bu 4 fazda dokunulmayacak)

- Auth altyapısı (JWT yeterli)
- Frontend bileşenleri (Faz 13 için sadece SkillMastery API endpoint'i yeterli)
- Sandbox / Adversarial Loop (Faz 15-16)
- GraderAgent Consensus (Faz 14)
- Mevcut migration'lar (sadece SkillMastery için yeni migration)

---

## BÖLÜM 2 — MANTIKSAL MİMARİ

### 2.1 Katman Yapısı — Faz 10 Sonrası Hedef Durum

```
┌─────────────────────────────────────────────────────────┐
│                     Orka.Core                           │
│  Entities: Session, Message, Topic, QuizAttempt,        │
│            AgentEvaluation, SkillMastery [YENİ]         │
│  Enums:    SessionState, AgentRole (+ Evaluator) [FIX]  │
│  Interfaces:                                            │
│    IAgentOrchestrator, ITutorAgent, IAnalyzerAgent      │
│    ISummarizerAgent, IQuizAgent, IIntentClassifierAgent  │
│    IEvaluatorAgent [YENİ → Core]                        │
│    IRedisMemoryService [FIX → Core'a taşı]              │
│    ISkillMasteryService [YENİ]                          │
│  DTOs: SendMessageRequest, ChatMessageResponse, ...     │
└────────────────────┬────────────────────────────────────┘
                     │ bağımlılık yönü: tek yön ↓
┌────────────────────▼────────────────────────────────────┐
│                 Orka.Infrastructure                     │
│  Services:                                              │
│    TutorAgent [FAZ 11: +FetchWikiContextAsync,          │
│                         +FetchGoldExamplesAsync,        │
│                         +FetchPistonContextAsync]       │
│    EvaluatorAgent [FAZ 12: +SaveGoldExampleAsync]       │
│    SkillMasteryService [FAZ 13: YENİ]                   │
│    RedisMemoryService [FAZ 12: +GetGoldExamplesAsync,   │
│                                +SaveGoldExampleAsync]   │
│    AgentOrchestratorService [FAZ 13: completion routing]│
│    ... (mevcut diğer servisler değişmez)                │
│  Data: OrkaDbContext [FAZ 13: +SkillMasteries DbSet]    │
│  Migrations: [FAZ 13: AddSkillMastery]                  │
└────────────────────┬────────────────────────────────────┘
                     │
┌────────────────────▼────────────────────────────────────┐
│                    Orka.API                             │
│  Controllers: Chat, Quiz, Code, Topics, Wiki, Health    │
│               [FAZ 13: +SkillMasteryController YENİ]   │
│  Middleware: ExceptionMiddleware,                       │
│              CorrelationIdMiddleware [YENİ]             │
│  Program.cs: DI kayıtları                               │
└─────────────────────────────────────────────────────────┘
```

### 2.2 TutorAgent Context Pipeline — Faz 11 Hedef Mimarisi

**Mevcut durum (2 kaynak):**
```
TutorAgent.GetResponseStreamAsync()
  ├─ FetchUserMemoryProfileAsync()    → DB: başarısız quiz soruları
  └─ FetchPerformanceProfileAsync()  → Redis: evaluator notları
```

**Faz 11 sonrası (5 kaynak — hepsi Task.WhenAll ile paralel):**
```
TutorAgent.GetResponseStreamAsync()
  ├─ FetchUserMemoryProfileAsync()    → DB: başarısız quiz soruları
  ├─ FetchPerformanceProfileAsync()  → Redis: evaluator notları (mevcut)
  ├─ FetchWikiContextAsync()         → WikiService: konu wiki özeti      [FAZ 11]
  ├─ FetchPistonContextAsync()       → Redis: son kod çıktısı            [FAZ 11]
  └─ FetchGoldExamplesAsync()        → Redis: altın diyalog örnekleri    [FAZ 12]
        ↓
  BuildTutorSystemPrompt(
    isQuizPending,
    memoryContext,       → [USER MEMORY PROFILE]
    performanceHint,     → [CRITICAL LLMOPS FEEDBACK]
    wikiContext,         → [KONU WİKİSİ]
    pistonContext,       → [SON KOD ÇIKTISI]
    goldExamples         → [ALTIN ÖRNEK]
  )
```

> Herhangi bir kaynak başarısız olursa boş string döner, diğerleri etkilenmez.

### 2.3 Gold Example Veri Akışı — Faz 12

```
[Yazar — EvaluatorAgent]
  score >= 9?
    └─ YES → SaveGoldExampleAsync(topicId, userMsg, agentResp, score)
               LPUSH orka:gold:{topicId} {json}
               LTRIM 0 9        (max 10 örnek)
               EXPIRE 2592000   (30 gün)

[Okur — TutorAgent]
  FetchGoldExamplesAsync(topicId)
    └─ LRANGE orka:gold:{topicId} 0 1  (max 2 örnek)
         → "[ALTIN ÖRNEK 1]\nÖğrenci: ...\nEğitmen: ..."
```

### 2.4 Skill Mastery ve Kurs İlerleme — Faz 13

```
[Quiz başarısı]
  HandleQuizModeAsync()
    └─ SkillMasteryService.RecordMasteryAsync(userId, topicId, subTopicTitle, score:100)

[Konu tamamlandı]
  TriggerBackgroundTasks()
    └─ AnalyzerAgent.AnalyzeCompletionAsync() → IsComplete = true
         └─ session.CompletedSections += 1
              ├─ CompletedSections < subTopics.Count?
              │    └─ YES → GetFirstLessonAsync(nextTopic) → otomatik ders
              └─ NO → TopicCompletedEvent (MediatR)
```

### 2.5 Observability Katmanı

```
[Her Request]
  CorrelationIdMiddleware
    ├─ X-Correlation-Id header var → kullan
    └─ Yok → Guid.NewGuid() üret
    → Tüm log satırlarına propagate

[Background Tasks]
  capturedCorrelationId = _correlationContext.CorrelationId  (Task.Run'dan önce capture)
  _logger.LogInformation("[Background] {Agent} başladı. Correlation={Id}", ...)
  _logger.LogError(ex, "[Background] {Agent} HATA. Correlation={Id}", ...)

[Health Checks — /health]
  ├─ /health/live  → 200 (process ayakta)
  ├─ /health/ready → Redis ping + SQL ping
  └─ /health       → tüm check'ler
```

---

## BÖLÜM 3 — TEKNOLOJİ VE NEDEN

### 3.1 Mevcut Stack Trade-off Analizi

| Teknoloji | Kullanım | Karar |
|---|---|---|
| StackExchange.Redis | IConnectionMultiplexer Singleton | ✅ Yeterli. Faz 10-13 için değişiklik gerekmez. |
| Semantic Kernel | Sadece KorteksAgent | ✅ TutorAgent'ta SK gerekmez — prompt engineering yeterli. |
| MediatR | TopicCompletedEvent | ✅ Faz 13'te SkillMasteryService aynı event'i dinleyebilir. |
| EF Core Code-First | Kalıcı veri | ✅ SkillMastery için sadece yeni migration yeterli. |
| IAsyncEnumerable SSE | Tutor stream | ✅ SignalR overhead'i olmadan native SSE. |

### 3.2 Eklenmesi Gereken

**Polly — Faz 11**
- Neden: AIAgentFactory manuel try/catch içeriyor, exponential backoff yok.
- Kapsam: RetryPolicy (3 deneme, 2s→4s→8s), CircuitBreaker (5 ardışık hata → 30s devre dışı).
- Overhead: ~1ms per call — kabul edilebilir.

**AspNetCore.HealthChecks.Redis — Faz 10**
- Neden: Redis bağlantısı Faz 9'da kuruldu, kesinti izlenemiyor.
- NuGet: `AspNetCore.HealthChecks.Redis`

**CorrelationId Middleware — Faz 10**
- Neden: TriggerBackgroundTasks içindeki paralel ajanlar takip edilemiyor.
- Pattern: `capturedCorrelationId` — Task.Run dışında capture edilir (scope-safe).

### 3.3 Bilinçli Kabul Edilen Trade-off'lar

| Karar | Gerekçe |
|---|---|
| OpenTelemetry bu fazlarda yok | Grafana entegrasyonu Faz 17 kapsamında. Structured log yeterli. |
| Pub/Sub yerine Redis key | `orka:wiki-ready:{topicId}` key kontrolü, subscriber yönetimi overhead'i olmadan aynı sonucu üretiyor. |
| Kafka/RabbitMQ yok | Background task sayısı tek haneli. Message broker bu ölçekte gereksiz. |
| Vector DB yok | Cohere Embed Faz 10-13 kapsamında değil. Gold examples Redis'te JSON olarak yeterli. |

---

## BÖLÜM 4 — UYGULAMA PLANI

### Bağımlılık Zinciri

```
Faz 10 (Interface + Observability)
  └─→ Faz 11 (TutorAgent Bağlantı Katmanı)
        └─→ Faz 12 (Gold Examples)
        └─→ Faz 13 (Skill Mastery)   ← Faz 12 ile paralel geliştirilebilir
```

> Faz 12 ve 13 birbirinden bağımsız olarak geliştirilebilir,
> ancak ikisi de Faz 10 ve 11'in tamamlanmasını gerektirir.

---

### FAZ 10 — Mimari Temizlik + Observability

**Adım 10.1 — Interface Taşıma**
```
Orka.Core/Interfaces/IRedisMemoryService.cs  → yeni dosya (Infrastructure'dan taşı)
Orka.Core/Interfaces/IAgents.cs              → IEvaluatorAgent ekle
Orka.Core/Enums/AgentRole.cs                 → Evaluator değeri ekle
```
Doğrulama: `dotnet build Orka.Infrastructure/Orka.Infrastructure.csproj` → 0 hata.

**Adım 10.2 — appsettings.json**
```json
"AI": {
  "GitHubModels": {
    "Agents": {
      "Evaluator": { "Model": "gpt-4o-mini" }
    }
  }
}
```
`AIAgentFactory._modelMap` → `AgentRole.Evaluator` kaydı.
`EvaluatorAgent` → `AgentRole.Grader` → `AgentRole.Evaluator`.

**Adım 10.3 — CorrelationId Middleware**
```
Orka.Core/Interfaces/ICorrelationContext.cs     → interface
Orka.Infrastructure/Services/CorrelationContext.cs → AsyncLocal backed impl.
Orka.API/Middleware/CorrelationIdMiddleware.cs  → X-Correlation-Id okur/üretir
Program.cs → app.UseMiddleware<CorrelationIdMiddleware>() (ExceptionMiddleware'den önce)
```
`TriggerBackgroundTasks` → `var capturedCorrelationId = _correlationContext.CorrelationId`

**Adım 10.4 — Health Checks**
```
NuGet: AspNetCore.HealthChecks.Redis
Program.cs:
  builder.Services.AddHealthChecks()
    .AddRedis(redisConnectionString, name: "redis", timeout: TimeSpan.FromSeconds(3))
    .AddDbContextCheck<OrkaDbContext>(name: "sql-server")

Orka.API/Controllers/HealthController.cs:
  [AllowAnonymous] GET /health       → tüm check'ler
  [AllowAnonymous] GET /health/ready → Redis + SQL
  [AllowAnonymous] GET /health/live  → 200 (process ayakta)
```

**Adım 10.5 — TriggerBackgroundTasks Structured Logging**
```csharp
_logger.LogInformation(
    "[Background] {Agent} başladı. Session={SessionId} Correlation={CorrelationId}",
    agentName, capturedSessionId, capturedCorrelationId);

_logger.LogError(ex,
    "[Background] {Agent} HATA. Session={SessionId} Correlation={CorrelationId}",
    agentName, capturedSessionId, capturedCorrelationId);
```

**Faz 10 Çıkış Kriterleri:**
- [ ] `dotnet build` → 0 hata
- [ ] `GET /health` → `{"status":"Healthy","redis":"Healthy","sql-server":"Healthy"}`
- [ ] Her log satırında `CorrelationId` alanı mevcut
- [ ] EvaluatorAgent `AgentRole.Evaluator` modelini kullanıyor

---

### FAZ 11 — TutorAgent Bağlantı Katmanı

**Adım 11.1 — FetchWikiContextAsync**
```
TutorAgent'a private method:
  FetchWikiContextAsync(Guid? topicId) → Task<string>
  
  1. topicId null → "" döner
  2. WikiService.GetTopicPages(topicId) → pages
  3. pages boş → "" döner
  4. İçerik concat → max 2000 karakter truncate
  5. "[KONU WİKİSİ - BU KONUDA BİLİNENLER]:\n{content}" formatı
  6. try/catch → LogWarning → "" döner (asla exception fırlatmaz)
```

**Adım 11.2 — FetchPistonContextAsync**
```
TutorAgent'a private method:
  FetchPistonContextAsync(Guid sessionId) → Task<string>
  
  1. Redis StringGetAsync("orka:piston:{sessionId}:last")
  2. Değer yok → "" döner
  3. JSON deserialize → {Code, Stdout, Stderr, Language, ExecutedAt}
  4. "[SON KOD ÇIKTISI]:\n..." formatı
  5. try/catch → "" döner
```

**Adım 11.3 — CodeController Piston Redis Yazımı**
```
IRedisMemoryService'e:
  Task SetLastPistonResultAsync(Guid sessionId, string code, string stdout, string stderr, string language)
  Key: "orka:piston:{sessionId}:last" | TTL: 30 dakika

CodeController.RunCode() → başarılı sonuç sonrası SetLastPistonResultAsync çağrısı
```

**Adım 11.4 — KorteksAgent Wiki-Ready Sinyali**
```
IRedisMemoryService'e:
  Task SetWikiReadyAsync(Guid topicId)
  Key: "orka:wiki-ready:{topicId}" | TTL: 1 saat

KorteksAgent.RunResearchAsync() → AutoUpdateWikiAsync başarılıysa SetWikiReadyAsync
```

**Adım 11.5 — Task.WhenAll Paralel Fetch**
```csharp
var (memoryContext, performanceHint, wikiContext, pistonContext) =
    await (
        FetchUserMemoryProfileAsync(userId, session.TopicId),
        FetchPerformanceProfileAsync(session.Id),
        FetchWikiContextAsync(session.TopicId),
        FetchPistonContextAsync(session.Id)
    ).WhenAll4();

// BuildTutorSystemPrompt imzasına wikiContext ve pistonContext eklenir
```

**Faz 11 Çıkış Kriterleri:**
- [ ] Log: `[TutorAgent] Wiki context yüklendi: {charCount} karakter`
- [ ] Piston sonrası gönderilen mesajda Tutor kod bağlamında yanıt üretiyor
- [ ] Herhangi bir context kaynağı çökerse sistem çalışmaya devam ediyor

---

### FAZ 12 — Dynamic Few-Shot: Altın Örnek Kütüphanesi

**Adım 12.1 — Interface Genişletme**
```
Orka.Core/Interfaces/IRedisMemoryService.cs'e ekle:
  Task SaveGoldExampleAsync(Guid topicId, string userMessage, string agentResponse, int score);
  Task<IEnumerable<GoldExample>> GetGoldExamplesAsync(Guid topicId, int count = 2);

Orka.Core/DTOs/GoldExample.cs:
  public record GoldExample(string UserMessage, string AgentResponse, int Score, DateTime CreatedAt);
```

**Adım 12.2 — RedisMemoryService Implementasyonu**
```
SaveGoldExampleAsync:
  Key: "orka:gold:{topicId}" | Type: List
  LPUSH → LTRIM 0 9 → EXPIRE 2592000

GetGoldExamplesAsync:
  LRANGE 0 (count-1) → JSON deserialize → IEnumerable<GoldExample>
  Hata → boş liste döner
```

**Adım 12.3 — EvaluatorAgent Gold Yazımı**
```
EvaluateInteractionAsync() imzasına: Guid? topicId = null
  if (result.score >= 9 && topicId.HasValue)
      await _redisService.SaveGoldExampleAsync(topicId.Value, userMessage, agentResponse, result.score);

AgentOrchestratorService.TriggerBackgroundTasks() → topicId geçilir
```

**Adım 12.4 — EvaluatorAgent Scope Genişletme**
```
TriggerBackgroundTasks() → SummarizerAgent tamamlandıktan sonra:
  await evaluator.EvaluateInteractionAsync(sessionId, topicTitle, summary, "SummarizerAgent", topicId);
```

**Adım 12.5 — TutorAgent Few-Shot Entegrasyonu**
```
FetchGoldExamplesAsync(Guid? topicId) → Task<string>
  1. topicId null → "" döner
  2. GetGoldExamplesAsync(topicId, 2)
  3. Boş liste → "" döner
  4. "[ALTIN ÖRNEKLER]\nÖrnek 1:\nÖğrenci: ...\nSen: ..." formatı
  5. try/catch → "" döner

Task.WhenAll'a 5. kaynak olarak eklenir
BuildTutorSystemPrompt() → goldExamples parametresi eklenir
```

**Faz 12 Çıkış Kriterleri:**
- [ ] `redis-cli LRANGE orka:gold:{topicId} 0 -1` → kayıtlar doluyor
- [ ] Log: `[EvaluatorAgent] Altın örnek kaydedildi. TopicId={Id} Puan={Score}`
- [ ] Log: `[TutorAgent] 2 altın örnek yüklendi`
- [ ] SummarizerAgent çıktıları AgentEvaluations tablosuna yazılıyor

---

### FAZ 13 — Skill Mastery ve Kurs İlerleme Motoru

**Adım 13.1 — Entity ve Migration**
```csharp
// Orka.Core/Entities/SkillMastery.cs
public class SkillMastery
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid TopicId { get; set; }
    public string SubTopicTitle { get; set; } = string.Empty;
    public int QuizScore { get; set; }
    public DateTime MasteredAt { get; set; }
}

// OrkaDbContext: public DbSet<SkillMastery> SkillMasteries { get; set; }
// Migration: dotnet ef migrations add AddSkillMastery --startup-project ../Orka.API
```

**Adım 13.2 — ISkillMasteryService**
```
Orka.Core/Interfaces/ISkillMasteryService.cs:
  Task RecordMasteryAsync(Guid userId, Guid topicId, string subTopicTitle, int quizScore);
  Task<IEnumerable<SkillMastery>> GetUserSkillsAsync(Guid userId, Guid topicId);

Orka.Infrastructure/Services/SkillMasteryService.cs: implementasyon
Program.cs: builder.Services.AddScoped<ISkillMasteryService, SkillMasteryService>()
```

**Adım 13.3 — Quiz → SkillMastery**
```
HandleQuizModeAsync() → quiz doğru yanıtlandığında:
  await _skillMasteryService.RecordMasteryAsync(
      userId, session.TopicId.Value, currentSubTopicTitle, quizScore: 100);
```

**Adım 13.4 — AnalyzerAgent → Kurs İlerleme**
```
TriggerBackgroundTasks() → IsComplete = true:
  1. session.CompletedSections += 1
  2. await _db.SaveChangesAsync()
  3. var subTopics = await _topicService.GetSubTopicsAsync(session.TopicId)
  4. CompletedSections < subTopics.Count?
       YES → nextTopic = subTopics[session.CompletedSections]
             autoLesson = await _tutorAgent.GetFirstLessonAsync(parentTitle, nextTopic.Title)
             await SaveAiMessage(session, userId, autoLesson)
       NO  → MediatR.Publish(new TopicCompletedEvent(...))
```

**Adım 13.5 — TutorAgent Mastery Context**
```
FetchUserMemoryProfileAsync() güncellemesi:
  var masteredSkills = await db.SkillMasteries
      .Where(s => s.UserId == userId && s.TopicId == topicId)
      .Select(s => s.SubTopicTitle).ToListAsync();

  if (masteredSkills.Any())
      memoryContext += "\n[ÖĞRENCİ ZATEN BİLİYOR]: " + string.Join(", ", masteredSkills);
```

**Adım 13.6 — API Endpoint**
```
Orka.API/Controllers/SkillMasteryController.cs:
  [Authorize] GET /api/skills           → kullanıcının tüm skill'leri
  [Authorize] GET /api/skills/{topicId} → belirli konudaki skill'ler
```

**Faz 13 Çıkış Kriterleri:**
- [ ] Migration başarılı, `SkillMasteries` tablosu SQL'de mevcut
- [ ] Quiz sonrası `SELECT * FROM SkillMasteries` dolmuş
- [ ] Log: `[Orchestrator] Otomatik ders geçişi. Yeni konu: {Topic}`
- [ ] `GET /api/skills` → 200

---

## ÖZET: Faz Bağımlılık Matrisi

```
         Faz10  Faz11  Faz12  Faz13
Faz 10    ──    GEREKLİ GEREKLİ GEREKLİ
Faz 11    ✓      ──    GEREKLİ GEREKLİ
Faz 12    ✓      ✓      ──    BAĞIMSIZ
Faz 13    ✓      ✓    ÖNERİLİR  ──
```

> Faz 10 ve 11 sırayla tamamlanmalıdır.
> Faz 12 ve 13 paralel geliştirilebilir.
