# Orka AI — Tam Kapsamlı Kod Tarama Raporu

**Tarih:** 2026-05-01
**Kapsam:** `Orka.API/`, `Orka.Core/`, `Orka.Infrastructure/`, `Orka-Front/src/`
**Method:** 5 reviewer personası (`csharp-reviewer`, `security-reviewer`, `database-reviewer`, `silent-failure-hunter`, `performance-optimizer`) — ana thread'te sırayla uygulandı (subagent dispatch rate-limit'e takıldı; persona kuralları `~/.claude/agents/*.md`'den okundu).

---

## ⚠️ KORUMA NOTU — Codex P6 + Smoke Hardening (revert etme)

Bu raporun fix'lerini uygularken **aşağıdaki son commit içeriği KORUNMALI** (Codex tarafından son commit'te tamamlanan P6 EducatorCore + live smoke hardening):

### Değiştirilmemesi gereken bileşenler
- `Orka.Infrastructure/Services/EducatorCoreService.cs`
- `Orka.Core/Interfaces/IEducatorCoreService.cs`
- `Orka.Core/DTOs/EducatorDtos.cs`
- `TutorAgent`, `QuizAgent`, `EvaluatorAgent`, `DeepPlanAgent` içindeki **`educatorCoreContext`** ve **`TeachingReference`** akışı
- YouTube'un **factual source değil, pedagogy-only reference** olarak kullanılması
- `ClassroomService` AI timeout fallback ve **`[HOCA]/[ASISTAN]/[KONUK]`** transcript formatı
- `start-api.ps1 -InMemoryDatabase` dev fallback
- `reset-dev-db.ps1`'in LocalDB/migration fail durumunda **non-zero exit** ile fail etmesi
- `DashboardController` **EducatorCore kartı** ve **`learningBridge`** payload yapısı
- `WikiMainPanel` **source-evidence-trust-strip** UI bileşeni
- `LearningSignalTypes` sabitleri: `YouTubeReferenceUsed`, `NotebookSourceUsed`, `MisconceptionDetected`, `TeachingMoveApplied`, `SourceCitationMissing`
- `LearningSourceService` **source-ask-answer-without-doc-citation guard**

### P7 fix'leri uygulanırken çalıştırılacak smoke guard'lar (P0–P6 regresyon kontrolü)

```bash
npm run quick:smoke
npm run build -- --logLevel error
.\scripts\quick-backend.ps1
.\scripts\start-api.ps1 -NoBuild -InMemoryDatabase
.\scripts\live-smoke.ps1
.\scripts\live-user-smoke.ps1 -IncludeAi
```

Her bulgu fix'inden sonra en azından `quick:smoke` + `quick-backend.ps1` zorunlu, AI akışlarına dokunan fix'lerde `live-user-smoke.ps1 -IncludeAi` zorunlu.

---

## Özet (Headline)

| Kategori | 🔴 KRİTİK | 🟡 ORTA | 🟢 DÜŞÜK |
|---|---|---|---|
| C# / SOLID & async | 1 | 4 | 3 |
| Security | 1 | 3 | 2 |
| Database / EF Core | 0 | 4 | 2 |
| Silent Failures | 4 | 6 | 2 |
| Performance | 1 | 4 | 3 |
| **Toplam** | **7** | **21** | **12** |

**Genel durum:** Kod tabanı genel olarak disiplinli — **hiç** `.Result`/`.Wait()` blocking yok, **hiç** `async void` yok, **hiç** `new HttpClient(...)` yok (IHttpClientFactory kullanılıyor), **hiç** `FromSqlRaw` yok (SQL injection riski sıfır), JWT validation parametreleri tam, 137 yerde `LogError` ile bol logging. **Asıl risk** AuthController'da loggersız sessiz yutmalar ve global CORS açıklığında yoğunlaşıyor.

---

## 🔴 KRİTİK (hemen düzeltilmeli)

### K1. AuthController — sessiz exception yutma + logger yok
**Kategori:** Silent Failure / Security
**Dosya:** `Orka.API/Controllers/AuthController.cs:39-42, 66-69, 80-83`

3 ayrı endpoint (`Register`, `Login`, `Refresh`) `catch (Exception)` ile her şeyi yakalayıp generic mesajla 400/401 dönüyor; `_logger` constructor'a hiç enjekte edilmemiş.

**Etki:** Auth katmanında hiçbir başarısızlık iz bırakmıyor — DB hatası, race condition, hash kütüphanesi patlaması ayırt edilemiyor. `ExceptionMiddleware` zaten domain hatalarını maskeliyor; burada yeniden yakalanması middleware'i devre dışı bırakıyor.

**Fix:**
```csharp
public AuthController(IAuthService authService, IConfiguration configuration, ILogger<AuthController> logger)
{
    _authService = authService;
    _configuration = configuration;
    _logger = logger;
}

// Tüm try/catch (Exception) bloklarını kaldır — middleware NotFoundException/UnauthorizedException/BadRequestException
// mapping'ini zaten yapıyor. Domain exception'ları middleware'e bırak.
```

---

### K2. CORS — production'da AllowAnyOrigin + AllowAnyHeader + AllowAnyMethod
**Kategori:** Security
**Dosya:** `Orka.API/Program.cs:340-348`

```csharp
options.AddPolicy("OrkaCors", policy =>
{
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
});
```
Environment koşulu yok.

**Etki:** Bearer token Authorization header'da olduğu için CSRF doğrudan exploit değil ama her origin'in `/api/dashboard/*`, `/api/chat/*` gibi endpoint'leri tarayıcıdan çağırabilmesi compliance/risk açısından kabul edilemez. XSS bir başka domain'de yaşandığında tüm Orka API'sine açık.

**Fix:**
```csharp
options.AddPolicy("OrkaCors", policy =>
{
    if (builder.Environment.IsDevelopment())
        policy.WithOrigins("http://localhost:5173", "http://localhost:5174");
    else
        policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? []);

    policy.AllowAnyHeader().AllowAnyMethod();
});
```

---

### K3. AgentOrchestratorService — quiz parse hataları yutuluyor
**Kategori:** Silent Failure
**Dosya:** `Orka.Infrastructure/Services/AgentOrchestratorService.cs:1291, 1317`

```csharp
// :1291
catch { return response; }

// :1317
catch { /* fallback */ }
// ardından "Bu soru yüklenemedi" fallback JSON döner
```

**Etki:** LLM'den gelen quiz JSON bozulduğunda kullanıcı "Bu soru yüklenemedi" görüyor ama hangi LLM/hangi formatın bozulduğu loglanmıyor — Evaluator triad bu sinyali göremiyor, kalite trendi kayboluyor.

**Fix:**
```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex,
        "[Orchestrator] Quiz parse failed, using fallback. Snippet={Snippet}",
        response[..Math.Min(200, response.Length)]);
    return response;
}
```

---

### K4. SupervisorAgent — intent classification fallback log'suz
**Kategori:** Silent Failure
**Dosya:** `Orka.Infrastructure/Services/SupervisorAgent.cs:73`

```csharp
catch { return "GENERAL"; }
```

**Etki:** Intent classification başarısız olduğunda her kullanıcı "GENERAL" intent'iyle routing'e gidiyor; sistem yanlış agent'a yönleniyor — kalite düşüşü gözlemlenemiyor.

**Fix:** `_logger.LogWarning(ex, "[Supervisor] Intent classification failed; defaulting to GENERAL")`.

---

### K5. DeepPlanAgent — null fallback log'suz
**Kategori:** Silent Failure
**Dosya:** `Orka.Infrastructure/Services/DeepPlanAgent.cs:366`

```csharp
catch { /* yoksay, null dönecek */ }
```

**Etki:** Plan üretim hatası null olarak yukarı sızıyor, üst katmanda "plan oluşturulamadı" mesajı görünüyor ama failure root cause yok.

**Fix:** `_logger.LogError(ex, "[DeepPlanAgent] Phase failed, returning null")`.

> **NOT:** DeepPlanAgent dokunulurken `educatorCoreContext`/`TeachingReference` akışını koru (P6 commit). Sadece catch bloğu logla.

---

### K6. Frontend — 10 yerde sessiz `.catch(() => {})`
**Kategori:** Silent Failure

| Dosya | Satır | Bağlam |
|---|---|---|
| `Orka-Front/src/components/ChatPanel.tsx` | 97 | `UserAPI.getMe()` 401 yutuluyor |
| `Orka-Front/src/contexts/LanguageContext.tsx` | 104 | Dil tercih kaydetme |
| `Orka-Front/src/pages/Courses.tsx` | 59 | Kurs listesi yükleme |
| `Orka-Front/src/components/InteractiveIDE.tsx` | 158, 190 | IDE state save |
| `Orka-Front/src/pages/Home.tsx` | 146, 231 | Body var ama incelenmeli |
| `Orka-Front/src/components/SettingsPanel.tsx` | 147 | Ayar kaydı |
| `Orka-Front/src/components/WikiMainPanel.tsx` | 270, 428 | Wiki action save |

**Etki:** API başarısızlığı kullanıcıya görünmez biçimde yutuluyor; örn. ChatPanel:97 `UserAPI.getMe()` 401 verdiğinde isim "User" kalıyor ama refresh akışı tetiklenmiyor.

**Fix:** Min. `console.error` + `toast.error("...")`. Tercih edilen: merkezi `services/api.ts` interceptor'unda hata logging.

> **NOT:** WikiMainPanel'e dokunulurken `source-evidence-trust-strip` UI bileşeni korunmalı (P6).

---

### K7. DashboardController.GetStats — Users.FindAsync tracking + 7 sequential query
**Kategori:** Performance (hot path)
**Dosya:** `Orka.API/Controllers/DashboardController.cs:36`

```csharp
var user = await _dbContext.Users.FindAsync(userId);  // tracking'li
```
Sonra 6 sequential query daha (Topics x2, Messages, WikiPages.Count, QuizAttempts, LearningSignals).

**Etki:** Dashboard her açılışta ~7 ardışık SQL roundtrip'i. EF Core aynı DbContext'te paralelleştiremez ama context-bağımsız read'ler scope-per-query ile `Task.WhenAll` edilebilir.

**Fix kısa vade:**
```csharp
var user = await _dbContext.Users
    .AsNoTracking()
    .Where(u => u.Id == userId)
    .Select(u => new { u.TotalXP, u.CurrentStreak })
    .FirstOrDefaultAsync();
```
**Uzun vade:** scope-per-query pattern ile parallel read'ler.

---

## 🟡 ORTA (bu sprint içinde)

### C# / async / SOLID

#### O1. AgentOrchestratorService:521 — generic Exception
```csharp
if (session == null) throw new Exception("Oturum oluşturulamadı veya SmallTalk.");
```
**Fix:** Yeni `SessionCreationException` veya mevcut `BadRequestException` kullan.

#### O2. AgentOrchestratorService — God class (1397 satır)
SRP ihlali. Orchestrator + helpers + SaveAiMessage + transition logic + curriculum render hepsi bir dosyada.
**Fix:** Curriculum render → `CurriculumRenderer`, transition → `SessionTransitionService` olarak ayır.
> **NOT:** Refactor sırasında `educatorCoreContext` enjeksiyonunu ve P6 sinyal akışını koru.

#### O3. TutorAgent — God class (700+ satır)
Aynı pattern. Prompt building, history fetch, gold examples retrieval, persona prompt — hepsi tek class.
> **NOT:** `educatorCoreContext` ve `TeachingReference` akışı korunmalı.

#### O4. Public async metotlarda CancellationToken eksik
Controller'larda HTTP cancellation token (`HttpContext.RequestAborted`) çoğu yerde repository/service'lere geçirilmiyor. Uzun stream'lerde client disconnect ederse query devam ediyor.

### Security

#### O5. JwtKeyResolver:9 — DevelopmentSecret const repo'da
```csharp
private const string DevelopmentSecret = "ORKA_DEV_SECRET_KEY_FOR_LOCAL_AUTH_ONLY_64_CHARS_2026_01";
```
Yalnızca `isDevelopment=true`'da devreye girse de "dev fallback" tasarımı bir gün prod'a sızabilir.
**Fix:** `appsettings.Development.json`'a taşı veya `dotnet user-secrets`'e zorla; const'ı sil.

#### O6. DiagnosticsController — `[AllowAnonymous]` defense-in-depth zayıf
**Dosya:** `Orka.API/Controllers/DiagnosticsController.cs:12`
Class-level `[AllowAnonymous]` + her metotta `IsDevelopment()` kontrolü var. Bir gün biri yeni metot ekleyip env check'i unutursa → tüm provider config'i ifşa olur.
**Fix:** `[Authorize(Roles="Admin")]` class-level + `IsDevelopment()` env check'i koru (defense-in-depth).

#### O7. Program.cs:67-68 — ConnectionMultiplexer.Connect synchronous
```csharp
StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnection)
```
`abortConnect=false` olsa bile startup'ta DNS-resolve hatasında app boot uzayabilir.
**Fix:** `ConnectAsync` + retry policy (Polly).

### Database / EF Core

#### O8. AgentOrchestratorService:1173-1183 — AsNoTracking eksik (curriculum render)
Read-only kullanım, change tracker'ı boşa şişiriyor.
**Fix:** Her iki sorguya `.AsNoTracking()`.

#### O9. DashboardController.cs:194 — CountAsync tutarlılık
`AsNoTracking` zincirsiz; CountAsync entity materialize etmediği için OK ama tutarlılık için zincire bağla.

#### O10. TutorAgent.cs:710 — `feedbacks.ToList().Any()` redundant
**Fix:** `feedbacks.Count > 0` (ICollection ise) veya direkt ToList sonrası `.Count`.

#### O11. SessionService.cs:44 — sync `.FirstOrDefault()`
Async context'te mi belirsiz. EF üzerindeyse kritik, in-memory liste üzerindeyse OK. Doğrula ve gerekirse `.FirstOrDefaultAsync()` kullan.

### Silent Failures

#### O12. RedisMemoryService — 7 yerde boş catch (parse fallback)
**Dosya:** `Orka.Infrastructure/Services/RedisMemoryService.cs:92, 237, 310, 359, 523, 691, 756`
"Bozuk veri ise raw dön" defensive parse fallback'leri. Çoğu kabul edilebilir ama hiçbiri `LogDebug` bile etmiyor.
**Fix:** `_logger.LogDebug(ex, "Malformed Redis payload, falling back. Key={Key}")`.

#### O13. AiDebugLogger.cs:49 — log altyapı yutması
```csharp
catch { /* Loglama asla uygulamayı kırmamalı */ }
```
Kabul edilebilir ama belgele (XML doc comment).

#### O14-15. CheckDatabaseAsync catch'leri logger eksik
**Dosya:** `Orka.API/Controllers/DashboardController.cs:391-394` ve `Orka.API/Controllers/DiagnosticsController.cs:89-98`
**Fix:** Logger inject edip `LogWarning` çağır.

### Performance

#### O16. DashboardController.GetSystemHealth — 4 Redis çağrısı sequential
**Dosya:** `Orka.API/Controllers/DashboardController.cs:248-251`
```csharp
var agentMetrics  = (await _redis.GetSystemMetricsAsync()).ToList();
var providerUsage = (await _redis.GetProviderUsageAsync()).ToList();
var redisHealth   = await _redis.GetRedisHealthAsync();
var cacheMetrics  = (await _redis.GetCacheMetricsAsync()).ToList();
```
DbContext'ten bağımsız → `Task.WhenAll` ile paralelleştirilebilir, ~%60 latency kazancı.
**Fix:**
```csharp
var (agentMetricsTask, providerUsageTask, redisHealthTask, cacheMetricsTask) =
    (_redis.GetSystemMetricsAsync(), _redis.GetProviderUsageAsync(),
     _redis.GetRedisHealthAsync(), _redis.GetCacheMetricsAsync());
await Task.WhenAll(agentMetricsTask, providerUsageTask, redisHealthTask, cacheMetricsTask);
```

#### O17. DashboardController.cs:173-180 — 2 ayrı SumAsync + CountAsync
Sessions üzerinde 2 ayrı SumAsync + 1 CountAsync. Tek projeksiyonla tek round-trip yapılabilir.
**Fix:**
```csharp
var sessionAgg = await _dbContext.Sessions.AsNoTracking()
    .GroupBy(_ => 1)
    .Select(g => new {
        TotalTokens = g.Sum(s => (int?)s.TotalTokensUsed) ?? 0,
        TotalCost   = g.Sum(s => (decimal?)s.TotalCostUSD) ?? 0m,
        Count       = g.Count()
    })
    .FirstOrDefaultAsync();
```

#### O18. WikiMainPanel.tsx — 27 inline handler/style
**Dosya:** `Orka-Front/src/components/WikiMainPanel.tsx`
React her render'da yeni referans → child memoization etkisiz.
**Fix:** `useCallback`/`useMemo` zorunlu.
> **NOT:** `source-evidence-trust-strip` bileşeni korunmalı.

#### O19. LeftSidebar.tsx — 11 inline handler
Topic listesi her keystroke'da re-render.

---

## 🟢 DÜŞÜK (ileride)

### C#
- D1. 26 yerde `.Include(` — bazıları projeksiyonla daraltılabilir.
- D2. `Orka.Core/Constants/LearningSignalTypes.cs` — Core saf domain kuralı için periyodik audit. **NOT:** P6 sinyal sabitleri (`YouTubeReferenceUsed`, `NotebookSourceUsed`, `MisconceptionDetected`, `TeachingMoveApplied`, `SourceCitationMissing`) korunmalı.
- D3. Static helper class'lar (örn. `ToUserDto` mapper) — sealed/static eklenebilir.

### Security
- D4. `appsettings.json` JWT Issuer/Audience prod-style mı kontrol et.
- D5. HSTS, X-Content-Type-Options, X-Frame-Options security header middleware eklenmemiş.

### Database
- D6. `OrkaDbContext` — composite index audit (örn. Messages için `(UserId, CreatedAt)`).
- D7. Migration backup disiplini — rollback prosedürü belgelenmemiş.

### Silent Failures / Frontend
- D8. 9 yerde `key={i}` (unstable React key) — özellikle WikiMainPanel:658,674,1399,1498,1513 dinamik liste düzeltilmeli.
- D9. ChatPanel.tsx — 23 hook çağrısı, hiç `useMemo`/`useCallback` yok. Performans ölçülmedi; büyürse orta seviyeye çıkar.

### Performance
- D10. DashboardController.cs:140-148 — `recentMessages.GroupBy(m => m.CreatedAt.Date)` client-side; SQL tarafında DATE() ile yapılabilir.
- D11. SaveAiMessage — her mesajda ayrı SaveChangesAsync; batching opportunity (low öncelik, akış zaten chunk'lı).
- D12. SemanticKernel `Kernel` Scoped registered (`Program.cs:284`) — Singleton'a alınabilir (plugins state-less).

---

## Önerilen Uygulama Sırası

1. **K1 — AuthController logger inject + try/catch kaldır** → ~15 dk PR. Smoke: `quick-backend.ps1` + `live-user-smoke.ps1 -IncludeAi`.
2. **K2 — CORS production allowlist** → 1 PR. Smoke: full set.
3. **O6 — DiagnosticsController `[Authorize(Roles="Admin")]`** → 1 satır. Smoke: `live-smoke.ps1`.
4. **K3, K4, K5, O12, O13, O14, O15 — Logger ekleme batch'i** → 1 PR (sadece logging eklemeleri, davranış değişmiyor). Smoke: `quick:smoke` + `quick-backend.ps1`.
5. **K6 — Frontend silent catch'leri** → 1 PR (toast.error veya merkezi handler). Smoke: `npm run quick:smoke` + `npm run build`.
6. **K7 + O16 + O17 — Dashboard performance** → 1 PR. Smoke: `live-user-smoke.ps1 -IncludeAi` (Admin dashboard akışı).
7. **O18 + O19 — Frontend memoization** → 1 PR. Smoke: `npm run quick:smoke` + `npm run build`.
8. **O2 + O3 — God class refactoring** → ileri sprint, ayrı plan. **EducatorCore akışı korunmalı.**

---

## Genel Değerlendirme

Mimari disiplin (DI, factory pattern, IHttpClientFactory, async-only DB, layered architecture) **çok iyi** durumda. Asıl risk **observability boşluklarında** — exception'lar yutuluyor ama log gitmiyor; bu LLM ürünü için kritik çünkü Evaluator/SkillMastery sinyalleri bu silent failure'ları çözmeden geçerli olamaz. Tek kritik *security* açığı CORS.

**Hızlı kazançlar:** K1, K2, K3-K5, O16 sırasıyla bir gün içinde uygulanabilir → güvenlik + observability + dashboard latency'sinde belirgin iyileşme, P6 akışına dokunmadan.
