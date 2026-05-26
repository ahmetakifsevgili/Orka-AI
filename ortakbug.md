# Orka AI — Ortak Bug & Fix Planı

Çift kaynaklı denetim: **Gemini multi-agent audit** + **Codex rescan (2026-05-26)**
Branch: `codex/heavy-learning-flow-eval-browser-qa`
HEAD: `72ee3ca0dcd3e9a28fe94e22a7ca1caefb7d0a63`

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
- 607/607 backend testi geçiyor ✅

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
