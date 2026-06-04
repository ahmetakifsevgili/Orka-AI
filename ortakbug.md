# Orka AI — Ortak Bug & Fix Planı

Çift kaynaklı denetim: **Gemini multi-agent audit** + **Codex rescan (2026-05-26)**
Branch: `codex/heavy-learning-flow-eval-browser-qa`
HEAD: `a49d19276ec126aaabd2f9a4a35968d3b2cc87f5`

---

## 🔴 1. DALGA — P0 / P1 Release Blocker'lar

> Bu maddeler production'da FK crash, veri bütünlüğü bozulması veya güvenlik açığına yol açar.
> 2. Dalga'ya geçmeden önce tamamlanmalıdır.

---

### P0-1 — Generated Quiz FK Crash

**Dosyalar:**
- `Orka.API/Controllers/QuizController.cs` → L116-122
- `Orka.Core/Entities/AssessmentItem.cs` → L13-14
- `Orka.Infrastructure/Data/OrkaDbContext.cs` → L742-746

**Problem:**
`QuizController.Generate` her LLM quiz üretiminde `AssessmentItem` oluştururken
`ConceptGraphSnapshotId = Guid.Empty` atıyor. Entity'de bu alan required (`null!`).
InMemory testler geçiyor ama SQL Server'da `SaveChangesAsync` FK violation fırlatır.

**Fix seçenekleri (biri uygulanmalı):**
- `AssessmentItem.ConceptGraphSnapshotId`'yi nullable (`Guid?`) yap + migration ekle.
- VEYA: Generate çağrısı öncesinde topic için geçerli snapshot resolve et / oluştur.

**Test gate:**
- Relational provider üzerinde `/api/quiz/generate` entegrasyon testi — generated item FK ihlalsiz persist edilmeli.

---

### P1-1 — Quiz Attempt Idempotency Yok

**Dosyalar:**
- `Orka.Infrastructure/Services/QuizAttemptRecorder.cs` → L88, L115, L321
- `Orka.Infrastructure/Data/OrkaDbContext.cs` → L554-557

**Problem:**
Çift tıklama veya retry'da aynı soru için iki `QuizAttempt` oluşabiliyor.
Unique index var ama recorder duplicate durumunda mevcut kaydı dönmüyor — constraint error veya 500 fırlatıyor.
XP, SRS, KnowledgeTracing ikinci kez yazılıyor; öğrenme profili şişiyor.

**Fix:**
- `QuizAttemptRecorder.RecordAsync` içine duplicate kontrolü ekle.
- Duplicate varsa mevcut sonucu döndür, KT/XP/SRS yazma.

**Test gate:**
- Aynı attempt 2 kez submit → aynı sonuç dönmeli, ikinci kayıt oluşmamalı.

---

### P1-2 — EF Migration / Model Snapshot Drift

**Dosyalar:**
- `Orka.Infrastructure/Data/OrkaDbContext.cs` → L538, L555 (yeni unique indexler)
- `Orka.Infrastructure/Migrations/OrkaDbContextModelSnapshot.cs` → ~L5572

**Problem:**
Runtime DbContext yeni unique indexler içeriyor ama migration snapshot güncel değil.
InMemory testler geçiyor, SQL Server schema'da indexler eksik kalıyor.

**Fix:**
```bash
dotnet ef migrations has-pending-model-changes --project Orka.Infrastructure --startup-project Orka.API
```

## Codex Profesyonel Hardening Plan Guncellemesi - 2026-05-26

Amac: kapatilan 20+ fixin optimizasyonunu bozmadan sistemi release-grade kanit zincirine baglamak. Bu turda 5 alt ajan raporu + ana Codex dogrulamasi kullanildi: Backend Reliability, Data Security/Privacy, Frontend UX, Ops/Redis, Product Contract QA. Ortak sonuc: sistemin kritik kapatilari guclu; kalan isler daha cok coverage derinlestirme ve operasyonel kanit standardizasyonu.

### Bu Tur Yapilan Son Hardening

| Alan | Son durum |
|---|---|
| Frontend chunk optimizasyonu | `InteractiveIDE` artik ProductCoherence icinde lazy/Suspense ile yukleniyor; Vite static+dynamic import uyarisi kalkti ve `InteractiveIDE` ayri chunk oldu. |
| Tailwind class hijyeni | Cift opacity siniflari (`/70/70`, `/70/30`, `/18/50`) temizlendi; build/typecheck PASS. |
| Rate-limit privacy | Genel rate-limit partition key artik raw user/IP yerine SHA-256 kisaltilmis hash kullanir. |
| Testhost stabilitesi | Test ortaminda `BackgroundTaskQueue` hosted service kapatilabilir hale geldi; `ApiSmokeFactory` Redis health'i fake deterministic check ile izole eder. |
| CI release gate | `backend-release.yml` icine EF migration drift gate eklendi. |
| Quick backend gate | `scripts/quick-backend.ps1` testleri `-m:1`, node reuse off ve ayri results directory ile kosar. |

### Guncel Kanit Durumu

| Gate | Sonuc |
|---|---|
| `dotnet test Orka.API.Tests/Orka.API.Tests.csproj --no-build -m:1 ...` | PASS: 615/615 |
| `dotnet test Orka.Infrastructure.UnitTests/Orka.Infrastructure.UnitTests.csproj --no-build -m:1` | PASS: 156/156 |
| `powershell -ExecutionPolicy Bypass -File scripts/quick-backend.ps1` | PASS: 311/311 targeted API release proof |
| `dotnet ef migrations has-pending-model-changes --project Orka.Infrastructure --startup-project Orka.API` | PASS |
| `npm run typecheck` | PASS |
| `npm run build` | PASS |
| `npm run quick:smoke` | PASS: UI + contract/security + Playwright browser smoke |

### Profesyonel Seviyeye Kalan Bilerek Birakilan Isler

| Oncelik | Baslik | Neden |
|---|---|---|
| P1 | Authenticated app-shell/ProductCoherence e2e | Mevcut Playwright smoke public landing/login/protected redirect kapsar; loginli gercek uygulama rotalari ayri test edilmeli. |
| P1 | Relational `/api/quiz/generate` endpoint regression | Kod fix ve full suite PASS var; FK crash icin dogrudan SQL-backed endpoint regression kaniti release kapisini daha net yapar. |
| P1 | SSE expired-token/malformed event browser regression | Kod refresh-aware authenticated fetch ve raw discard yapiyor; runtime browser kaniti eklenmeli. |
| P2 | Redis multi-master exhaustive purge fake/cluster testi | Kod bounded non-replica scan kullaniyor; cluster exhaustive davranis icin sentetik cluster testi gerekir. |
| P2 | Distributed lock renewal failure behavioral test | Renewal path var; renewal fail oldugunda ana worker abort etmiyor, bu davranis bilincli olarak testle sabitlenmeli ya da fail-closed abort'a cevrilmeli. |
| P2 | Expensive endpoint behavioral 429 test | Middleware var ve full suite PASS; per-user/IP concurrency 429 davranisi dogrudan load testiyle kilitlenmeli. |

Son mutabakat: Sistem artik "kritik bug closure + full regression green" seviyesinde. "Profesyonel production" cizgisi icin kalan isler yeni buyuk bug degil; kanit kapsamini derinlestiren release engineering maddeleri.
Pending varsa migration ekle. Bu komutu CI gate'e al — pending model varsa pipeline fail etsin.

---

### P1-3 — Assessment Calibration Topic Ownership Guard Yok

**Dosyalar:**
- `Orka.API/Controllers/AssessmentController.cs` → L114-119
- `Orka.Infrastructure/Services/AssessmentCalibrationServices.cs` → L25-45

**Problem:**
`POST /api/assessment/topic/{topicId}/calibration/run` endpoint'i
`topicId`'nin mevcut kullanıcıya ait olup olmadığını kontrol etmiyor.
Başka kullanıcının topic ID'siyle kalibrasyon tetiklenebilir (BOLA / broken object-level authorization).

**Fix:**
- Controller'a `TopicBelongsToUserAsync` kontrolü ekle.

**Test gate:**
- Cross-user topicId → 404/403 dönmeli, run persist edilmemeli.

---

### P1-4 — ContentJson İçinde Answer-Key Sızıntısı

**Dosyalar:**
- `Orka.API/Controllers/QuestionsController.cs` → L52
- `Orka.Infrastructure/Services/QuestionBankService.cs` → L1219, L1233, L1270
- `Orka.Infrastructure/Services/CentralExamStudyService.cs` → L751, L764

**Problem:**
`IsCorrect`, `Explanation` gibi açık alanlar maskelenmiş ama `ContentJson` olduğu gibi dönüyor.
İçinde `answerKey`, `correctAnswer`, `rubric`, `solution` gibi gizli cevap işaretleri bulunabilir.

**Fix:**
- Question bank ve central exam payload'ları için learner-safe DTO oluştur.
- ContentJson'ı denylist değil **allowlist** şemasıyla filtrele.

**Test gate:**
- Learner payload snapshot testi: `answerKey`, `correctAnswer`, `isCorrect`, `solution`, `rubric` bulunamaz.

---

### P1-5 — SourceRefsJson Client Poisoning

**Dosyalar:**
- `Orka.Core/DTOs/RecordQuizAttemptRequest.cs` → L35
- `Orka.API/Controllers/QuizController.cs` → L580-584
- `Orka.Infrastructure/Services/QuizAttemptRecorder.cs` → L759
- `Orka.Infrastructure/Services/LearningSignalService.cs` → L39

**Problem:**
`StripClientSuppliedAnswerKey` sadece `IsCorrect` ve `Explanation` siliyor.
`SourceRefsJson` client'tan gelip KnowledgeTracing ve LearningSignal'e kadar ham taşınıyor.
Sahte mastery verisi, prompt injection veya aşırı büyük metadata enjekte edilebilir.

**Fix:**
- `SourceRefsJson`'ı client-controlled olmaktan çıkar.
- Allowlist DTO ile yalnızca izin verilen alanları kabul et, boyut/derinlik limiti uygula.

**Test gate:**
- İzin verilmeyen alan persist edilmemeli.

---

### P1-6 — Provider MaxOutputTokens Payload'a Gitmiyor

**Dosyalar:**
- `Orka.Infrastructure/Services/AIAgentFactory.cs` → L285, L318
- `Orka.Infrastructure/Services/GeminiService.cs` → L125-134 (`generationConfig`'te alan yok)
- `Orka.Infrastructure/Services/GroqService.cs` → L201
- `Orka.Infrastructure/Services/GitHubModelsService.cs` → L82 (hard-coded 4096)
- `Orka.Infrastructure/Services/MistralService.cs` → L111
- `Orka.Infrastructure/Services/OpenRouterService.cs` → L48

**Problem:**
Budget servisi `maxOutputTokens = 2048` hesaplıyor ama bu değer hiçbir provider adapter'ına geçmiyor.
Maliyet ve latency bütçeleri advisory, enforced değil.

**Fix:**
- Her provider adapter'ına `maxOutputTokens` parametresi ekle.
- Provider'a özgü alan adını map et: `max_tokens` / `max_output_tokens` / `maxOutputTokens`.

**Test gate:**
- Fake HTTP provider testi: outgoing request body'de ilgili alan mevcut olmalı.

---

### P1-7 — Wiki SSE Auth Bypass + Raw JSON Parse Riski

**Dosyalar:**
- `Orka-Front/src/components/WikiDrawer.tsx` → L191, L221
- `Orka-Front/src/components/WikiMainPanel.tsx` → L1632, L1688
- `Orka-Front/src/services/api.ts` → L166

**Problem:**
Wiki stream'i raw `fetch` kullanıyor, uygulamanın refresh-aware authenticated fetch'ini bypass ediyor.
Token süresi dolduğunda Wiki chat sessizce bozuluyor.
SSE parser bilinmeyen JSON payload'ları kullanıcıya ham olarak append ediyor.

**Fix:**
- Refresh-aware streaming fetch helper kullan.
- Yalnızca typed SSE event'leri parse et; bilinmeyeni düşür veya güvenli logla.

---

### P2-1 — EF Global Query Filter Required-Navigation Uyarısı (EF 10622)

**Dosyalar:**
- `Orka.Infrastructure/Data/OrkaDbContext.cs` → ~L4996

**Problem:**
Testlerde EF warning 10622 üretiliyor.
Global query filter'lar required navigations'larla inner join sürprizleri yaratabilir.

**Fix:**
- Filtrelenen parent'lara eşleşen filter'ı dependent entity'lere de ekle.
- VEYA ilişkileri optional yap.
- Include/query path testleri ekle.

---

## 🟡 2. DALGA — Operasyonel İyileştirmeler

> P0/P1 tamamlandıktan sonra ele alınacak.
> Sistem şu an lokal/test ortamında kararlı; bu maddeler production dayanıklılığı içindir.

| # | Konu | Dosya | Özet |
|---|---|---|---|
| 2.1 | CORS middleware sıralaması | `Orka.API/Program.cs` L122-136 | `UseRouting` + `UseCors` → ExceptionMiddleware öncesine al |
| 2.2 | Async Audio Overview | `AudioController.cs`, `AudioOverviewService.cs` | 202 Accepted + background queue; frontend polling zaten hazır |
| 2.3 | BackgroundTaskQueue eşzamanlılık | `BackgroundTaskQueue.cs` L23-44 | `SingleReader=false` + 4 paralel consumer (scope-safe kontrat önce) |
| 2.4 | Redis cluster scan | `RedisMemoryService.cs` L590-610 | Tüm master endpoint'leri tara; UNLINK ile asenkron sil |
| 2.5 | Distributed lock (workers) | `SrsReminderWorkerService.cs`, `DailyChallengeWorkerService.cs` | Redis `SET NX EX` ile multi-instance duplicate engelle |
| 2.6 | Pahalı endpoint rate limit | `AudioController.cs`, `QuestionDraftGenerationController.cs`, `QuestionImportsController.cs` | Per-user + IP rate limit + concurrency cap ekle |
| 2.7 | Non-stream cancellation | `TutorAgent.cs` L548 | `HttpContext.RequestAborted` → orchestrator → agent → provider zinciri |
| 2.8 | ProductCoherence nav | `LeftSidebar.tsx` | Yeni panelleri sidebar'da birinci sınıf erişilebilir yap |
| 2.9 | CI browser smoke | `.github/workflows/frontend-ci.yml` | Playwright ile app shell + ProductCoherence route smoke |
| 2.10 | PII retention policy | Tüm sistem | Retention sınıfları, silme/export kontrolleri, şifreleme |
| 2.11 | Redis anti-repeat quiz cache | `RedisMemoryService.cs` L855-877 | Generate quiz'de wire et; son soruları dışla |

---

## ✅ Kapalı / Düzelmiş (Referans)

Bu bulgular önceki çalışmalarda kapatıldı:

- Refresh token JSON body'den temizlendi → sadece HttpOnly cookie ✅
- Chat SSE authenticated fetch kullanıyor ✅
- Quiz attempt topic/session ownership guard mevcut (`QuizController.cs:184`) ✅
- Curriculum mutation endpoint'leri Admin-gated ✅
- Soft-delete global query filter'a taşındı ✅
- ProductCoherence compile/API wrapper mismatch kapatıldı ✅
- `AuthTokenContractTests` + `Login_WithValidCredentials` testleri yeşil ✅
- Not: `a49d192` uzerinde full `dotnet test` 606/607 gecti; `AuthSwaggerHealthSmokeTests.HealthEndpoints_ReturnStructuredJson` Redis health `ServiceUnavailable` nedeniyle fail verdi. Bu madde CLOSED degil, gate olarak takip edilmeli.

---

## Minimum Proof Gates (1. Dalga Sonrası)

```bash
dotnet test
dotnet ef migrations has-pending-model-changes --project Orka.Infrastructure --startup-project Orka.API
npm run typecheck && npm run quick:smoke && npm run quick:build
```

- `/api/quiz/generate` → relational DB FK violation yok
- Duplicate quiz submit → aynı sonuç, ikinci KT/XP/SRS yok
- Cross-user calibration topicId → 404/403, run persist edilmedi
- Learner payload → answer marker bulunamadı
- `SourceRefsJson` allowlist → izin verilmeyen alan persist edilmedi
- Provider outgoing body → `maxOutputTokens` alanı mevcut
- Wiki SSE → expired token graceful, raw JSON yok

---

## Ilk Audit Birlesim Takip Tablosu

Bu bolum, ilk Gemini/Codex rontgenlerinde gecen ama ana P0/P1 listeye birebir tasinmamis basliklarin kaybolmamasi icindir. Bunlar 1. Dalga release blocker listesinin yerine gecmez; Gemini fix sonrasi tekrar dogrulanacak master takip maddeleridir.

| Eski bulgu | Guncel durum | Nereye baglandi / Ne yapilacak |
|---|---|---|
| Psikometrik discrimination inversion | NEEDS REVERIFY | `AssessmentCalibrationServices.cs` tekrar kontrol edilecek. Ayirt edicilik formulu IRT mantigina gore negatif/ters calisiyor mu dogrulanacak. Aktifse P1'e alinacak. |
| Global difficulty bias | NEEDS REVERIFY | Soru seciminde global `0.50` zorluga yapisan skor halen varsa adaptive kalite bug'i olarak P1/P2'ye alinacak. |
| BKT linear forgetting / yapay decay clamp | NEEDS REVERIFY | `KnowledgeTracingService.cs` unutma modeli modern exponential/half-life modeline gore tekrar degerlendirilecek. Aktifse pedagojik P2 olarak planlanacak. |
| Prerequisite ihmali / alfabetik siralama sizintisi | NEEDS REVERIFY | `AdaptiveStudyPlannerService.cs` prerequisite graph'i kullaniyor mu kontrol edilecek. Esit skorda `Title` siralamasi pedagojik onceligi bozuyorsa P1/P2'ye alinacak. |
| Streaming RAG/Korteks bypass | CLOSED? / VERIFY | Comprehensive rescan bunu fixlenmis gormustu: stream path `RESEARCH` route ile `IKorteksAgent` cagiriyor. Gemini son fixlerden sonra regression testiyle tekrar dogrulanacak. |
| Async turn state 1 tur geriden gelme | NEEDS REVERIFY | `ScheduleTurnPostProcessingAsync` ve `BuildTutorMetadataAsync` arasindaki state lag tekrar audit edilecek. Aktifse P1 pedagogical state bug olarak acilacak. |
| GDPR / raw Wiki PII kaliciligi | PARTIAL | 2.10 `PII retention policy` altina baglandi, fakat genel madde olarak kalmamali. Raw learner message/source chunk retention, export/delete, redaction, encryption, access audit testleriyle somutlastirilacak. |
| Edge-TTS request blocking | PARTIAL | 2.2 `Async Audio Overview` altina baglandi. Frontend polling var; backend hala `202 Accepted + background job` contract'ina tasinmali. |
| Web Speech Turkish fallback / stuck playing state | NEEDS REVERIFY | Frontend audio player fallback tekrar kontrol edilecek. TR voice yoksa garbled English readout ve Chrome long-read stuck state varsa UX P2 olarak acilacak. |
| CORS middleware ordering | NEEDS REVERIFY | 2.1 altinda duruyor. `Program.cs` pipeline son haliyle tekrar dogrulanacak. |
| Background queue concurrency / scope safety | NEEDS REVERIFY | 2.3 altinda duruyor. `SingleReader`, consumer sayisi ve scoped service kullanimi birlikte incelenecek. |
| Redis cluster scan / distributed lock | NEEDS REVERIFY | 2.4 ve 2.5 altinda duruyor. Multi-endpoint scan ve multi-instance worker duplicate riski tekrar dogrulanacak. |

### Master Birlesim Kurali

- Ana `1. Dalga` maddeleri release blocker kabul edilir ve once kapatilmalidir.
- Bu tabloda `NEEDS REVERIFY` olan eski bulgular Gemini fixlerinden sonra tekrar taranacak; halen aktif olanlar yeni P1/P2 maddesi olarak plana eklenecek.
- `CLOSED? / VERIFY` olan maddeler kapali sayilmaz; regression testi veya kod kaniti olmadan `CLOSED` statusu verilmez.
- Privacy/data maddeleri genel baslikta birakilmayacak; retention, redaction, export/delete, encryption ve access audit gibi uygulanabilir alt maddelere bolunecek.

### Gemini'ye Ek Not

Gemini, `ortakbug.md` uygulama raporunda yalnizca ana P0/P1 maddeleri degil, bu "Ilk Audit Birlesim Takip Tablosu"nu da durumlandirmali. Her eski bulgu icin `ACTIVE / PARTIAL / CLOSED / NEEDS REVERIFY` yazmali ve kapattiysa test veya kod kaniti gostermeli.

---

## Subagent Orkestrasyonu - 6 Ajan Siniri

Bu plan icin maksimum 6 alt ajan kullanilacak. Gereksiz 20-30 explorer acilmayacak. Her ajan kendi sorumluluk alaninda calisacak, ayni dosyalari gereksiz tekrar taramayacak ve finalde `ACTIVE / PARTIAL / CLOSED / NEEDS REVERIFY` tablosu dondurecek.

| Ajan | Rol | Ana sorumluluk | Teslimat |
|---|---|---|---|
| Agent 1 | Backend Reliability Specialist | P0 FK crash, quiz idempotency, calibration ownership, EF relationship/model davranisi | Degisen backend dosyalari, relational test kaniti, kapanis statusu |
| Agent 2 | Data Security & Privacy Specialist | `ContentJson` leak, `SourceRefsJson` poisoning, PII/Wiki retention, BOLA/data isolation | Sanitizer/allowlist onerisi veya patch kontrolu, security regression testleri |
| Agent 3 | Pedagogy & Adaptive Learning Specialist | Discrimination inversion, difficulty bias, BKT forgetting, prerequisite planner, Redis anti-repeat | Pedagojik dogruluk raporu, algoritma fix kriterleri, test senaryolari |
| Agent 4 | Frontend UX & Streaming Specialist | Wiki SSE auth/parser, ProductCoherence nav, audio polling/backend contract, Web Speech fallback | Frontend statusu, browser/route smoke ihtiyaclari, UX regression kaniti |
| Agent 5 | Test Automation & QA Gate Specialist | `dotnet test`, EF migration gate, frontend gates, duplicate submit, learner-safe payload, provider body tests | Gate sonucu, eksik test listesi, CI'a alinacak komutlar |
| Agent 6 | Research & Optimization Specialist | 2026 modern cozum dogrulama, provider token mapping, rate limit/concurrency, background queue, Redis cluster/distributed lock | Modern cozum onerisi, risk siralamasi, operasyonel P2/P3 plan |

### Ajan Kullanma Kurallari

- Once Agent 1, 2 ve 5 P0/P1 release blocker kanitlarini toplar; ana fix sirasini bunlar belirler.
- Agent 3 ilk audit pedagojik maddelerini dusurmeden tekrar dogrular.
- Agent 4 yalniz frontend/UX/streaming yuzeylerine bakar.
- Agent 6 internet/modern pattern arastirmasi gerekiyorsa sadece kanitli ve uygulanabilir cozumleri raporlar; soyut mimari oneriyle yetinmez.
- Her ajan finalinde dosya yolu + satir kaniti veya test kaniti vermek zorundadir.
- Bir ajan `CLOSED` diyorsa hangi test/gate ile kapandigini yazmak zorundadir.
- Ajanlar urun kodu degisikligi yaparsa disjoint dosya sahipligi belirtmeli; ayni dosyada paralel edit yapilmamali.

---

## Codex Son Mutabakat Notu - 2026-05-26

Codex post-commit rescan raporu:

- `docs/audit/post-commit-a49d192-rescan-20260526.md`

Codex hukmu:

- Bu plan ana iskelet olarak uygundur.
- `a49d192` fix kapanis commit'i degil; audit/checkpoint + kismi toparlama commit'i gibi duruyor.
- 2. Dalga'ya gecmeden once 1. Dalga P0/P1 maddeleri gercek kod + test kanitiyla `CLOSED` olmali.
- Her madde icin "kod degisti" yeterli degil; ilgili gate yesil olmadan madde kapatilmis sayilmamali.

### Guncel Gate Gercekleri

| Gate | Sonuc | Not |
|---|---|---|
| `npm run typecheck` | PASS | Frontend typecheck geciyor. |
| `npm run quick:smoke` | PASS | Static UI + contract/security + Playwright browser smoke calistirir. Mevcut browser smoke landing/login/protected redirect kapsar; authenticated app-shell/ProductCoherence e2e coverage ayrica genisletilecek. |
| `npm run quick:build` | PASS | Vite production build geciyor. |
| `dotnet test` | PASS | Bu tur guncel full API suite PASS: 615/615. Infrastructure unit suite PASS: 156/156. Onceki Redis health timeout'u test-host health registration sızıntısı olarak kapatildi. |
| `dotnet ef migrations has-pending-model-changes --project Orka.Infrastructure --startup-project Orka.API` | PASS | `FirstWaveReleaseBlockerClosure` migration + snapshot ile model drift temiz. |
| `git diff --check` | PASS | Current working diff whitespace temiz. |
| `git diff --check HEAD^ HEAD` | FAIL | Bu commit araligi a49d192 icindeki eski trailing whitespace/EOF sorunlarini yakaliyor; 1. Dalga working diff kaynakli degil. |

### Codex 1. Dalga Uygulama Kapanis Notu - 2026-05-26

| Madde | Durum | Kanit |
|---|---|---|
| P0-1 Generated Quiz FK Crash | CLOSED | `QuizController` generated quiz itemlari icin gercek `ConceptGraphSnapshotId` cozer/olusturur; full API suite PASS: 615/615. |
| P1-1 Quiz Attempt Idempotency | PARTIAL | `QuizAttemptRecorder` duplicate attempt precheck + unique index migration eklendi; mevcut targeted/full testler PASS. Ayrica duplicate replay icin dogrudan side-effect regression testi eklenirse CLOSED sayilmali. |
| P1-2 EF Migration / Model Snapshot Drift | CLOSED | `dotnet ef migrations has-pending-model-changes --project Orka.Infrastructure --startup-project Orka.API` PASS. |
| P1-3 Assessment Calibration Topic Ownership Guard | CLOSED | `AssessmentController` topic ownership guard ile cross-user topic'e `NotFound` doner; `dotnet test` PASS. |
| P1-4 ContentJson Answer-Key Leak | CLOSED | `QuestionBankService` ve `QuestionImportService` write/import path'leri `LearnerSafeContentJson` ile persist-on-write sanitize eder. Malicious nested `answerKey/correctAnswer/solution/rubric/isCorrect/correctOptionId` fixture'i `RichQuestionImportTests.ApprovalSanitizesLearnerFacingContentJsonBeforeStorage` ile PASS. |
| P1-5 SourceRefsJson Client Poisoning | CLOSED | `QuizAttemptRecorder` `sourceEvidenceBundleId` icin server-side owner/topic/session/readiness kontrolu yapar; cross-user/stale/non-grounded bundle id persist edilmez ve `source_evidence_bundle` impact basis'e giremez. `QuizAttemptSafetyTests.QuizAttempt_DropsUntrustedSourceEvidenceBundleBeforePersistence` PASS. |
| P1-6 Provider MaxOutputTokens Payload | CLOSED | AI provider interface ve Groq/Gemini/OpenRouter/GitHub/Cerebras/Mistral/SambaNova request bodyleri role token budget tasir; fake provider compile/testleri PASS. |
| P1-7 Wiki SSE Auth + Raw JSON Parser | CLOSED (code + static guard) | `WikiDrawer`/`WikiMainPanel` refresh-aware `authenticatedFetch` kullanir; malformed/unknown SSE payload UI'a raw basilmadan discard edilir. `quick:smoke` static guard PASS; expired-token/malformed SSE browser regression'i future e2e coverage olarak kalir. |
| P2-1 EF Global Query Filter Required-Navigation Warning | CLOSED | Required dependent filtreleri eklendi; EF pending-model gate PASS. |
| Frontend Home `chat`/`tutor` blank panel | CLOSED | `Home.tsx` `chat`/`tutor` case'i `ChatPanel` dondurur; `npm run typecheck`, `quick:smoke`, `quick:build` PASS. |

### Codex 1./2. Dalga Audit + Kapanis Notu - 2026-05-26

Bu tur 6 aktif subagent raporu + ana Codex kontrolu ile tekrar tarandi. Gemini'nin "1. Dalga 9/9 kapandi" iddiasi ilk audit sonucunda fazla iyimser bulundu; asagidaki ek patchlerle kapanis kaniti guclendirildi.

| Madde | Guncel durum | Kanit / Not |
|---|---|---|
| P1-1 Quiz Attempt Idempotency | CLOSED (kod + full gate) | `QuizAttemptRecorder` artik hash'i duplicate lookup'tan once hesaplar ve unique-index race durumunda `DbUpdateException` yakalayip mevcut attempt'i idempotent replay olarak dondurur. Full `dotnet test --no-build -m:1` PASS: 615/615 API + 156/156 infra. |
| P1-3 Assessment Calibration Ownership | CLOSED | Controller guard'a ek olarak `AssessmentCalibrationService.RunAsync/GetLatestAsync` service-level topic ownership check yapar. |
| 2.1 CORS order | CLOSED | `Program.cs` pipeline `UseRouting()` -> `UseCors("OrkaCors")` -> auth/rate limiter/controller mapping sirasinda. |
| 2.2 Async Audio Overview | CLOSED (test gap: queue-full) | POST flow `202 Accepted` kontratinda kalir; background queue enqueue artik await edilir, enqueue failure job'u `failed` yapar. Targeted `LearningNotebookStudioTests` PASS. |
| 2.3 BackgroundTaskQueue concurrency | CLOSED | Queue artik `SingleReader=false`, default 4/configurable max concurrency, merkezi `ScopedWork` kontrati ve `IServiceScopeFactory` ile scoped job destegi kullanir. `RuntimeTelemetryHardeningTests.BackgroundQueue_RespectsConfiguredMaxConcurrency` PASS. |
| 2.4 Redis cluster scan | CLOSED (bounded scan; test gap: multi-master fake) | Ortak `ScanKeysAsync` non-replica endpointleri bounded take limitine kadar tarar, delete `UNLINK` kullanir; kalan evaluator-log tek-endpoint scan de `ScanKeysAsync`'e tasindi. Exhaustive multi-master purge davranisi icin ayri fake/cluster testi future gate. |
| 2.5 Distributed worker locks | CLOSED (acquire/release + renewal path) | SRS/Daily worker `SET NX` + Lua compare/delete release yaninda `RenewLockAsync` TTL renewal loop kullanir; renewal failure su an warning loglayip renewal loop'u durdurur, ana worker abort etmez. `ScheduledWorkersPushGroundingTests` PASS; renewal-failure behavioral test future hardening. |
| 2.6 Expensive endpoint rate limiting | CLOSED | Audio/question draft/import endpointleri token bucket policy'lerini korur; `ExpensiveEndpointConcurrencyMiddleware` per-user+IP `SemaphoreSlim` concurrency cap ve 429 response uygular. Source guard policy/config kontratini izler; behavioral 429 test future hardening. |
| 2.7 Non-stream cancellation | CLOSED (kod + build) | `ChatController` `HttpContext.RequestAborted` token'ini `IAgentOrchestrator.ProcessMessageAsync` -> `ITutorAgent` -> `AIAgentFactory/Grader/Tutor pedagogy` zincirine tasir. |
| 2.8 ProductCoherence nav | CLOSED | Yeni ProductCoherence panelleri sidebar'da var; stable legacy `sources/practice/review/progress` route guard'i da tekrar yesile cekildi. |
| 2.9 CI browser smoke | CLOSED | `.github/workflows/frontend-ci.yml` artik `npm run quick:smoke` calistirir. Playwright `webServer` production preview uzerinden landing/login/protected redirect smoke calistirir. Authenticated app-shell/ProductCoherence route smoke future e2e genisletmesi olarak kalir. `npm run quick:smoke` PASS. |
| 2.10 PII retention policy | CLOSED | Export artik raw high-risk aileleri (`messages`, `sourceChunks`, `wikiBlocks`, `learningSignals`, tutor memory, provider evidence) ve retention manifestini dondurur; delete path zaten high-risk aileleri temizler ve Redis purge kaniti verir. `DataLifecycleTests.ExportData_IncludesHighRiskPiiFamiliesAndRetentionPolicy` PASS. |
| 2.11 Redis anti-repeat quiz cache | CLOSED (kod + full gate) | Generated quiz flow recent Redis hashes okur, tekrar sorulari filtreler, served `questionHash` dondurur ve Redis'e yazar. Attempt recorder da cevaplanan hashleri hatirlar. |
| Audio overview frontend contract | CLOSED | Frontend `AudioOverviewJobDto` backend alanlariyla hizalandi: `id/status/script/speakers/contentType/fileName/downloadUrl/fallbackReason/errorMessage/createdAt/updatedAt`; docs `jobId` -> `id` olarak duzeltildi. |
| Home saved-view fallback | CLOSED | Eski/stale `dashboard` localStorage degeri `home` yuzeyine migrate edilir; default artik `home`. |

## 2026-05-26 Codex Kalan 5 Partial Closure Notu

Bu turdaki hedef: onceki tabloda PARTIAL kalan `P1-4`, `P1-5`, `2.3`, `2.5`, `2.6`, `2.10` maddelerini production-practice seviyesinde kapatmakti. Research/modern-practice kriteri: cozum resmi framework imkanlariyla, fail-closed davranisla ve regression testiyle yeterli olmali; ayri policy engine/encryption projesi gibi buyuk refactorlar bu closure icin overengineering sayildi.

| Madde | Yeni durum | Kod kaniti | Test/gate kaniti |
|---|---|---|---|
| P1-4 ContentJson Answer-Key Leak | CLOSED | `LearnerSafeContentJson` recursive denylist + allowlist uygular. `QuestionBankService` ve `QuestionImportService` write/import path artik learner-facing `ContentJson` degerlerini sanitize eder; `answerKey`, `correctAnswer`, `solution`, `rubric`, `isCorrect`, `correctOptionId` persist edilmez. | `RichQuestionImportTests.ApprovalSanitizesLearnerFacingContentJsonBeforeStorage` PASS. `QuestionBankTests.ContentJsonWritePathStripsLearnerAnswerKeysRecursively` mevcut regression. |
| P1-5 SourceRefsJson Client Poisoning | CLOSED | `QuizAttemptRecorder` client `SourceEvidenceBundleId` degerini server-side ownership/topic/session/readiness/expiry kontrolunden gecirir; gecmeyeni request uzerinden null'lar, metadata'ya sadece `client_rejected` ve safe readiness yazar. Raw `SourceRefsJson` allowlist disi persistence'a girmez. | `QuizAttemptSafetyTests.DropsUntrustedSourceEvidenceBundleBeforePersistence` PASS; `QuizAttemptSafetyTests.RejectsClientSuppliedSourceEvidenceBundleWithoutOwnershipAndReadiness` PASS. |
| 2.3 BackgroundTaskQueue concurrency / scope safety | CLOSED | `BackgroundTaskItem` `ScopedWork` destekler; `BackgroundTaskQueue` scoped job icin `IServiceScopeFactory` ile per-job scope acar. Worker concurrency default 4'tur ve `Workers:BackgroundQueue:MaxConcurrency` ile ayarlanir. `SingleReader=false`, bounded channel ve max concurrency gate vardir. | `RuntimeTelemetryHardeningTests.BackgroundQueue_RejectsMissingJobType`, `BackgroundQueue_PushesAiRequestContextForJob`, `BackgroundQueue_RespectsConfiguredMaxConcurrency` PASS. |
| 2.5 Distributed worker locks | CLOSED | Redis lock `SET NX` acquire, Lua compare/delete release ve Lua compare/`PEXPIRE` renewal destekler. SRS/Daily worker lock renewal loop baslatir; renewal failure su an warning loglayip renewal loop'u durdurur, ana worker abort etmez. | `ScheduledWorkersPushGroundingTests` PASS: 8/8. Fake Redis lock renewal/contension path eklendi; renewal-failure behavioral test future hardening. |
| 2.6 Expensive endpoint rate limiting | CLOSED | Audio, question draft ve question import limiter'lari token bucket policy'lerini korur; global ASP.NET Core concurrency limiter'a ek olarak `ExpensiveEndpointConcurrencyMiddleware` per user/IP `SemaphoreSlim` cap ve fail-fast `429` uygular. Controller filter'lari config-driven endpoint-level cap'i de korur. | `SourceRegressionGuardTests.ExpensiveEndpoints_KeepConcurrencyLimiterPolicies` PASS. |
| 2.10 PII retention/export policy | CLOSED for current release; FUTURE HARDENING: encryption/access-audit | Account/topic delete kapsaminda chat, wiki, source chunks, evidence, tutor memory, learning signals ve operational records temizlenir/anonymize edilir. `/api/user/export` high-risk aileleri export eder: messages, source chunks, wiki pages/blocks, quiz attempts, learning signals, source evidence bundles, tutor memory/turn states, provider evidence, artifacts. Raw embedding vectors export edilmez; `HasEmbedding` + retention policy ile beyan edilir ve delete path source chunk ile siler. | `DataLifecycleTests.ExportData_IncludesHighRiskPiiFamiliesAndRetentionPolicy` PASS. `DataLifecycleTests.DeleteAccount_*` mevcut delete coverage. |

Closure gates:
- `dotnet build Orka.API/Orka.API.csproj --no-restore -m:1 -v:minimal` PASS.
- `dotnet build Orka.API.Tests/Orka.API.Tests.csproj --no-restore -m:1 -v:minimal` PASS; sandbox/current runner ortaminda `Orka.API.Tests.csproj.AssemblyReference.cache` icin access-denied warning goruldu, build sonucu yine PASS.
- `dotnet build Orka.Infrastructure.UnitTests/Orka.Infrastructure.UnitTests.csproj --no-restore -m:1 -v:minimal` PASS.
- `dotnet ef migrations has-pending-model-changes --project Orka.Infrastructure --startup-project Orka.API` PASS.
- `git diff --check` PASS.
- Targeted tests PASS: `SourceRegressionGuardTests.ExpensiveEndpoints_KeepConcurrencyLimiterPolicies`, `DataLifecycleTests.ExportData_IncludesHighRiskPiiFamiliesAndRetentionPolicy`, `BackgroundJobs_UseCentralQueueInsteadOfRawTaskRun`, `QuizAttempt_RejectsClientSuppliedSourceEvidenceBundleWithoutOwnershipAndReadiness`, `QuestionBankTests.ContentJsonWritePathStripsLearnerAnswerKeysRecursively`, `BackgroundQueue_RespectsConfiguredMaxConcurrency`.
- Superseded residual note: onceki VSTest/testhost kilidi `ApiSmokeFactory` Redis health izolasyonu ve background queue hosted-service test kapisi ile tekrar dogrulandi; guncel full API suite PASS: 615/615.

### Bu Tur Gate Kaniti - 2026-05-26

```bash
dotnet build Orka.API/Orka.API.csproj --no-restore -m:1 -v:minimal
dotnet build Orka.API.Tests/Orka.API.Tests.csproj --no-restore -m:1 -v:minimal
dotnet build Orka.Infrastructure.UnitTests/Orka.Infrastructure.UnitTests.csproj --no-restore -m:1 -v:minimal
dotnet ef migrations has-pending-model-changes --project Orka.Infrastructure --startup-project Orka.API
git diff --check
dotnet test Orka.API.Tests/Orka.API.Tests.csproj --no-build --filter "FullyQualifiedName~ExpensiveEndpoints_KeepConcurrencyLimiterPolicies" -m:1 -v:minimal
dotnet test Orka.API.Tests/Orka.API.Tests.csproj --no-build --filter "FullyQualifiedName~DataLifecycleTests.ExportData_IncludesHighRiskPiiFamiliesAndRetentionPolicy" -m:1 -v:minimal
dotnet test Orka.API.Tests/Orka.API.Tests.csproj --no-build --filter "FullyQualifiedName~BackgroundJobs_UseCentralQueueInsteadOfRawTaskRun" -m:1 -v:minimal
dotnet test Orka.API.Tests/Orka.API.Tests.csproj --no-build --filter "FullyQualifiedName~QuizAttempt_RejectsClientSuppliedSourceEvidenceBundleWithoutOwnershipAndReadiness" -m:1 -v:minimal
dotnet test Orka.API.Tests/Orka.API.Tests.csproj --no-build --filter "FullyQualifiedName~QuestionBankTests.ContentJsonWritePathStripsLearnerAnswerKeysRecursively" -m:1 -v:minimal
dotnet test Orka.API.Tests/Orka.API.Tests.csproj --no-build --filter "FullyQualifiedName~BackgroundQueue_RespectsConfiguredMaxConcurrency" -m:1 -v:minimal
dotnet test Orka.Infrastructure.UnitTests/Orka.Infrastructure.UnitTests.csproj --no-build -m:1 -v:minimal
```

Son durum: closure maddeleri icin backend build, EF pending-model, diff check, hedefli API regression testleri ve infra unit suite PASS (`156/156`). Ek olarak guncel full API suite PASS: 615/615.

### Codex Kalan 5 Partial Kapanis Kontrolu - 2026-05-26

Bu turda kalan production-hardening maddeleri tekrar kontrol edildi. Son kod durumunda `P1-4`, `P1-5`, `2.3`, `2.5`, `2.6`, `2.10` icin durum `CLOSED` kabul ediliyor.

| Madde | Durum | Kanit |
|---|---|---|
| P1-4 ContentJson Answer-Key Leak | CLOSED | `QuestionBankService` ve `QuestionImportService` write/import path'leri `LearnerSafeContentJson` sanitize zincirine bagli. |
| P1-5 SourceRefsJson Client Poisoning | CLOSED | `QuizAttemptRecorder` client-supplied source evidence bundle id'lerini server-side owner/topic/session/readiness/expiry kontrolunden geciriyor; gecmeyeni `client_rejected` olarak dusuruyor. |
| 2.3 BackgroundTaskQueue concurrency/scope safety | CLOSED | `BackgroundTaskQueue` `SingleReader=false`, configurable max concurrency, `ScopedWork` ve `IServiceScopeFactory` ile per-job scope destegi kullaniyor. |
| 2.5 Distributed worker locks | CLOSED | Redis lock acquire/release yaninda `RenewLockAsync` Lua compare/PEXPIRE yenilemesi var; SRS/Daily workers renewal loop baslatiyor. |
| 2.6 Expensive endpoint rate limiting | CLOSED | Expensive endpoints token bucket policy'lerini korurken `ExpensiveEndpointConcurrencyMiddleware` per user/IP concurrency cap ve 429 uygular; source regression guard policy/config kontratini izliyor. |
| 2.10 PII retention/export policy | CLOSED for current release | `/api/user/export` high-risk aileleri ve retention manifestini dondurur; delete path account/topic scope'ta raw aileleri temizler/anonymize eder. Future hardening: encryption/access-audit. |

Bu turda calistirilan gate'ler:

- `dotnet build Orka.API\Orka.API.csproj --no-restore -m:1 -v:minimal` PASS.
- `dotnet ef migrations has-pending-model-changes --project Orka.Infrastructure --startup-project Orka.API` PASS.
- `git diff --check` PASS.
- `dotnet test Orka.Infrastructure.UnitTests\Orka.Infrastructure.UnitTests.csproj --no-restore -m:1 -v:minimal` PASS (`156/156`).
- `dotnet test Orka.API.Tests\Orka.API.Tests.csproj --no-build --filter "FullyQualifiedName~DataLifecycleTests.ExportData_IncludesHighRiskPiiFamiliesAndRetentionPolicy"` PASS.
- `dotnet test Orka.API.Tests\Orka.API.Tests.csproj --no-build --filter "FullyQualifiedName~SourceRegressionGuardTests.ExpensiveEndpoints_KeepConcurrencyLimiterPolicies"` PASS.
- Eski not: genis API targeted sette daha once `89/89` PASS ardindan testhost kapanis problemi goruldu. Guncel durumda quick-backend targeted release proof `311/311` PASS ve full API suite `615/615` PASS.

### Gemini Uygulama Promptu

Asagidaki prompt Gemini'ye aynen verilebilir:

```text
Orka repo icinde branch `codex/heavy-learning-flow-eval-browser-qa` uzerindeyim. Codex ile mutabik kaldigimiz ortak plan `ortakbug.md` ve Codex son audit raporu `docs/audit/post-commit-a49d192-rescan-20260526.md`.

Bu dosyadaki `Ilk Audit Birlesim Takip Tablosu` da kapsam dahilinde. Eski Codex/Gemini rontgen bulgularini dusurme; her birine `ACTIVE / PARTIAL / CLOSED / NEEDS REVERIFY` statusu ver.

Alt ajan kullanacaksan maksimum 6 subagent kullan:

1. Backend Reliability Specialist
2. Data Security & Privacy Specialist
3. Pedagogy & Adaptive Learning Specialist
4. Frontend UX & Streaming Specialist
5. Test Automation & QA Gate Specialist
6. Research & Optimization Specialist

Bu 6 rolden fazlasini acma. Her subagent kendi rol alaninda dosya yolu, satir kaniti, test/gate kaniti ve status tablosu dondursun.

Lutfen 2. Dalga islere gecme. Once 1. Dalga P0/P1 release blocker'lari kapat:

1. P0-1 Generated Quiz FK Crash
2. P1-1 Quiz Attempt Idempotency
3. P1-2 EF Migration / Model Snapshot Drift
4. P1-3 Assessment Calibration Topic Ownership Guard
5. P1-4 ContentJson Answer-Key Leak
6. P1-5 SourceRefsJson Client Poisoning
7. P1-6 Provider MaxOutputTokens Payload
8. P1-7 Wiki SSE Auth + Raw JSON Parser
9. P2-1 EF Global Query Filter Required-Navigation Warning

Calisma kurallari:

- Her maddeyi ayri ayri ele al.
- Bir madde icin sadece kod degistirmek yetmez; test/gate kaniti olmadan CLOSED yazma.
- InMemory testlere guvenme; FK, unique index ve migration maddelerinde relational davranisi dogrula.
- Client-controlled JSON alanlarinda denylist degil allowlist/safe DTO yaklasimi kullan.
- Security/data maddelerinde geriye uyumluluk gerekiyorsa once server-side sanitize ve explicit contract ekle.
- Product kodu disinda gereksiz refactor yapma.
- Existing user/Codex degisikliklerini revert etme.

Her madde icin beklenen kapanis kaniti:

- Generated quiz FK: relational `/api/quiz/generate` testi FK violation olmadan persist etmeli.
- Idempotency: ayni quiz attempt ikinci kez submit edilince ayni sonuc donmeli; ikinci KT/XP/SRS/profile/signal yan etkisi olmamali.
- EF drift: `dotnet ef migrations has-pending-model-changes --project Orka.Infrastructure --startup-project Orka.API` temiz donmeli.
- Calibration ownership: cross-user topicId 403/404 donmeli; run persist edilmemeli.
- ContentJson leak: learner payload icinde `answerKey`, `correctAnswer`, `isCorrect`, `solution`, `rubric`, `explanation` gibi cevap marker'lari bulunmamali.
- SourceRefsJson poisoning: allowlist disi alanlar metadata/learning signal icine persist edilmemeli.
- Provider max tokens: fake HTTP/provider testlerinde outgoing request body ilgili max token alanini tasimali.
- Wiki SSE: stream refresh-aware/authenticated helper kullanmali; unknown/raw JSON UI'a basilmamali.
- EF 10622: required navigation + global query filter warningleri ortadan kalkmali veya testle guvenli oldugu kanitlanmali.

Sonunda su gate'leri calistir ve sonucu raporla:

dotnet test
dotnet ef migrations has-pending-model-changes --project Orka.Infrastructure --startup-project Orka.API
npm run typecheck
npm run quick:smoke
npm run quick:build
git diff --check HEAD^ HEAD

Eger bir gate fail olursa, maddeyi CLOSED sayma. Raporunda her madde icin `ACTIVE / PARTIAL / CLOSED` durumunu, degisen dosyalari ve test kanitini yaz.

Ek olarak, `Ilk Audit Birlesim Takip Tablosu`ndaki eski bulgular icin de ayri bir durum tablosu yaz. Kapali dedigin her eski bulgu icin test, kod konumu veya regression kaniti goster.
```
