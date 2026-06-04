# Post-Commit Rescan Audit - a49d192

Tarih: 2026-05-26  
Branch: `codex/heavy-learning-flow-eval-browser-qa`  
HEAD: `a49d192` (`a49d19276ec126aaabd2f9a4a35968d3b2cc87f5`)  
Kapsam: `ortakbug.md`, onceki kapsamli audit, `a49d192` commit sonrasi kod durumu  
Mod: Salt okunur urun kodu denetimi. Bu rapor disinda kod degisikligi yapilmadi.

## Net Hukum

`a49d192` commit'i sistemi tamamen fixleyen bir commit degil; daha cok audit checkpoint + ortak plan + kismi toparlama gibi duruyor. Frontend typecheck/build/smoke geciyor, audio polling gibi bazi iyilestirmeler gelmis, ProductCoherence panelleri compile/render yoluna baglanmis. Fakat `ortakbug.md` icindeki birinci dalga P0/P1 maddelerin buyuk kismi halen `ACTIVE` veya `PARTIAL`.

Release acisindan en kritik uc nokta:

1. Generated quiz FK crash halen aktif: `ConceptGraphSnapshotId = Guid.Empty` yaziliyor.
2. EF migration/model drift halen aktif: `dotnet ef migrations has-pending-model-changes` fail verdi.
3. Wiki SSE auth/parser, ContentJson leak, SourceRefsJson poisoning ve provider token payload maddeleri kapanmamis.

## Kullanilan Alt Ajanlar

| Ajan | Rol | Odak |
|---|---|---|
| Meitner | Backend/Security Closure Auditor | P0/P1 backend blocker, EF drift, ownership, provider token, rate limit |
| Carson | Data/Pedagogy Auditor | ContentJson, SourceRefsJson, EF filter riski, Redis anti-repeat, duplicate attempt etkileri |
| Locke | Frontend/Release Auditor | Wiki SSE, ProductCoherence, CI smoke, audio polling, build/release hijyen |

Alt ajanlar read-only calisti. Uc farkli bolgeden gelen sonuc ayni: kismi iyilesme var, ama plan kapanmis sayilamaz.

## Ortakbug Maddeleri Durum Tablosu

| Madde | Durum | Kanit / Not |
|---|---|---|
| P0-1 Generated Quiz FK Crash | ACTIVE | `Orka.API/Controllers/QuizController.cs:122` halen `ConceptGraphSnapshotId = Guid.Empty`; `AssessmentItem.ConceptGraphSnapshotId` nullable degil; EF relationship required. |
| P1-1 Quiz Attempt Idempotency | ACTIVE / PARTIAL | Runtime unique index var ama `QuizAttemptRecorder` duplicate submit'i once okuyup idempotent sonuc dondurmuyor; yeni attempt ve yan etkiler yolu halen calisiyor. |
| P1-2 EF Migration/Model Snapshot Drift | ACTIVE | `dotnet ef migrations has-pending-model-changes` fail: "Changes have been made to the model since the last migration." |
| P1-3 Assessment Calibration Topic Ownership Guard | ACTIVE | `AssessmentController.RunCalibration` topic ownership check yapmadan `_calibration.RunAsync(userId, topicId, ct)` cagiriyor. |
| P1-4 ContentJson Answer-Key Leak | ACTIVE | `QuestionBankService` ve `CentralExamStudyService` `ContentJson` alanini ham payload'a tasiyor; yalniz `IsCorrect/Explanation` temizligi yeterli degil. |
| P1-5 SourceRefsJson Client Poisoning | ACTIVE / PARTIAL | `QuizController` sadece `IsCorrect/Explanation` temizliyor; `QuizAttemptRecorder` valid JSON icindeki bilinmeyen alanlari metadata'ya kopyalayabiliyor. |
| P1-6 Provider MaxOutputTokens Payload | ACTIVE | `AIAgentFactory` budget okuyor ama provider request body'lerine max token olarak iletilmiyor; bazi providerlarda hard-coded 4096 duruyor. |
| P1-7 Wiki SSE Auth + Raw JSON Parser | ACTIVE | `WikiDrawer.tsx` ve `WikiMainPanel.tsx` stream icin raw `fetch` kullaniyor; unknown JSON/string eventleri UI'a append edebiliyor. |
| P2-1 EF Global Query Filter Required-Navigation Warning | ACTIVE | EF 10622 warningleri test/EF komutlarinda devam ediyor; required child iliskiler query filter ile riskli. |
| Expensive Endpoint Rate Limits | PARTIAL | Chat/Code/Korteks/Quiz/Sources tarafinda var; Audio, QuestionDraftGeneration, QuestionImports gibi pahali endpointlerde eksik. |
| ProductCoherence Navigation | PARTIAL | Paneller `Home` icinde bagli, fakat sidebar eski nav listesinde; default view halen `dashboard`. |
| Audio Async UX | PARTIAL | Frontend polling hook gelmis; backend `POST /api/audio/overview` halen uzun TTS isini request icinde bekliyor, `202 + background job` degil. |
| Redis Anti-Repeat Quiz Cache | PARTIAL | Interface/implementation var, fakat generate/adaptive quiz product path'inde baglanti gorulmedi. |
| Browser/CI Smoke | PARTIAL | Frontend CI typecheck/build kosuyor; `quick:smoke` ve gercek browser/Playwright route smoke CI'da yok. |
| Commit Diff Hygiene | ACTIVE / REGRESSION | `git diff --check HEAD^ HEAD` trailing whitespace ve EOF sorunlariyla fail verdi. |

## Kanit Detaylari

### P0 Generated Quiz FK Crash

`Orka.API/Controllers/QuizController.cs:116-122` generated quiz icin `AssessmentItem` olustururken `ConceptGraphSnapshotId = Guid.Empty` yaziyor. `Orka.Core/Entities/AssessmentItem.cs:13` alan nullable degil ve `Orka.Infrastructure/Data/OrkaDbContext.cs` relationship'i required olarak kuruyor. Bu nedenle relational DB'de gecersiz FK ile save path'i halen crash riski tasiyor.

### Quiz Attempt Idempotency

`Orka.Infrastructure/Services/QuizAttemptRecorder.cs:88` civarinda her submit icin yeni `QuizAttempt` olusturuluyor ve `:115` civarinda direkt add ediliyor. XP tarafinda kismi tekrar guard'i var, fakat duplicate request icin once mevcut attempt'i bulup ayni sonucu donduren idempotent server contract yok. KT, learning signal, quiz run counter ve long-term profile yan etkileri duplicate submit ile halen kirlenebilir.

### EF Migration Drift

Calistirilan gate:

```powershell
dotnet ef migrations has-pending-model-changes --project Orka.Infrastructure --startup-project Orka.API
```

Sonuc: FAIL. Build geciyor ama EF "Changes have been made to the model since the last migration. Add a new migration." diyerek model snapshot drift'i dogruluyor. Ayni kosuda EF 10622 global query filter / required navigation warningleri de geliyor.

### Calibration Ownership Guard

`Orka.API/Controllers/AssessmentController.cs:114-119` arasinda endpoint user id alip dogrudan `_calibration.RunAsync(userId, topicId, ct)` cagiriyor. Topic'in bu user'a ait oldugunu kanitlayan bir guard yok. Service iceride user/topic ile filtrelese bile arbitrary topic id ile calibration run metadata'si uretme riski devam ediyor.

### ContentJson Answer-Key Leak

`QuestionsController` ve `QuestionBankService` duz `IsCorrect/Explanation` alanlarini temizlemis, fakat zengin icerik JSON'u icindeki `answerKey`, `correctAnswer`, `solution`, `rubric` gibi alanlar icin canonical sanitizer gorulmedi. `QuestionBankService.cs:1219`, `:1233`, `:1270` ve `CentralExamStudyService.cs:751`, `:764`, `:783` `ContentJson` alanini ham donduruyor.

### SourceRefsJson Metadata Poisoning

`RecordQuizAttemptRequest.SourceRefsJson` client-controlled string olarak duruyor. `QuizController.StripClientSuppliedAnswerKey` sadece `IsCorrect` ve `Explanation` temizliyor. `QuizAttemptRecorder` valid JSON icindeki bilinmeyen property'leri metadata'ya alabiliyor; bu metadata learning signal payload'una kadar ilerleyebiliyor. Burada allowlist tabanli server-side metadata merge gerekiyor.

### Provider Token Budget

`AIAgentFactory` role budget icindeki `MaxOutputTokens` degerini okuyor, ama provider adapter payload'larina tutarli sekilde yazmiyor. Gemini/Groq/GitHub/OpenRouter/Mistral/OpenAI-compatible servislerde ya hard-coded deger ya da eksik payload alani goruldu. Bu durum rol bazli butce politikasini etkisiz hale getiriyor.

### Wiki SSE Auth + Parser

`Orka-Front/src/components/WikiDrawer.tsx:191` ve `Orka-Front/src/components/WikiMainPanel.tsx:1632` stream icin `authenticatedFetch` yerine raw `fetch` kullaniyor. Parser tarafinda `content` olmayan JSON/string eventleri raw olarak UI'a append ediliyor. Bu hem auth/refresh davranisini kirilgan yapar hem de raw event/render riski tasir.

### ProductCoherence ve Frontend Reachability

`Home.tsx` yeni panelleri render edebilir hale getirmis, fakat `LeftSidebar.tsx` nav listesi halen eski id'lerle calisiyor. `home`, `study-room`, `sources-wiki`, `notebook`, `code` gibi yeni yuzeyler primer nav'da yok; default view de `dashboard`. Bu yuzden compile gecse bile kullanici akisi tam kapanmis degil.

### Audio Async UX

Frontend tarafinda `useAudioOverviewPolling` eklenmis ve Wiki paneline baglanmis. Ancak backend `AudioController.CreateOverview` halen `AudioOverviewService.CreateOverviewAsync` tamamlanana kadar request'i bekliyor. Gercek async mimari icin `202 Accepted`, job id, background queue ve polling endpoint contract'i lazim.

## Gate Sonuclari

| Gate | Sonuc | Not |
|---|---|---|
| `npm run typecheck` | PASS | Frontend typecheck geciyor. |
| `npm run quick:smoke` | PASS | Static smoke geciyor; gercek browser smoke degil. |
| `npm run quick:build` | PASS | Vite production build geciyor. |
| `dotnet test` | FAIL | 606/607 gecti; `AuthSwaggerHealthSmokeTests.HealthEndpoints_ReturnStructuredJson` Redis health `ServiceUnavailable` nedeniyle fail. |
| Targeted backend tests | PASS | 75/75 targeted test gecmis; subagent 67/67 benzer targeted set gectigini raporladi. |
| `dotnet ef migrations has-pending-model-changes` | FAIL | Model snapshot drift kesin. |
| `git diff --check HEAD^ HEAD` | FAIL | Commit trailing whitespace / EOF sorunlari getiriyor. |
| `git diff --check` | PASS | Working tree tracked diff temiz; fakat commit diff'i temiz degil. |

## Gemini'ye Verilecek En Net Talimat

Ikinci dalgaya gecmeden once bu commit uzerinde su closure seti tamamlanmali:

1. Generated quiz FK crash'i gercek data modeliyle kapat: valid snapshot bagla ya da nullable/optional domain contract'i bilincli tasarla.
2. EF drift'i kapat: migration + model snapshot + CI `has-pending-model-changes` gate.
3. Quiz attempt submit'i idempotent yap: duplicate request ayni sonucu dondursun, ikinci KT/signal/SRS/profile/quiz-run side effect uretmesin.
4. Assessment calibration endpointlerine topic ownership guard ekle.
5. `ContentJson` icin recursive answer-key sanitizer ekle ve `/api/questions` + central exam payload testleri yaz.
6. `SourceRefsJson` metadata merge'i allowlist'e indir, client-supplied unknown fields persist edilmesin.
7. Provider `MaxOutputTokens` degerini butun provider payload'larina testli sekilde gecir.
8. Wiki SSE stream'i `authenticatedFetch`/typed event parser ile yenile; unknown/raw event UI'a basmasin.
9. Pahali endpoint rate limitlerini Audio/Draft/Import dahil tamamla.
10. ProductCoherence nav ve browser route smoke'u CI'a ekle.
11. Commit hygiene icin `git diff --check HEAD^ HEAD` fail'lerini temizle.

## Sonuc

`a49d192` olumlu sinyaller tasiyor ama "fix tamam" demek icin erken. Su anki tabloya gore sistem build aliyor, fakat release blocker sinifinda kalan backend/data/security aciklari mevcut. Bu commit'ten sonra yapilacak en dogru hareket, `ortakbug.md` kapsamindaki P0/P1 closure'i once gercek kod ve testlerle bitirmek; sonra ikinci dalga optimizasyonlara gecmek.
