# Orka — Deploy Sonrası Doğrulama Kılavuzu

Kod fix'leri sonrası yapılması gerekenleri tek yerde toplar. Backend
restart + healthcheck + runtime doğrulama + opsiyonel LLM eval.

## 1) Backend Restart (zorunlu)

Yeni agent davranışları (SummarizerAgent kişiselleştirme, TutorAgent coding
kuralı, AgentOrchestratorService guard'ları) ancak yeniden başlatma
sonrası devreye girer.

```bash
# Çalışan backend terminalinde: Ctrl+C
cd D:/Orka
dotnet run --project Orka.API
```

`Now listening on: http://localhost:5065` görünene kadar bekle.

## 2) Healthcheck (zorunlu)

```bash
cd D:/Orka
node scripts/healthcheck.mjs
```

Hedef: **145/145 PASS, Grade A**. JSON + Markdown raporlar
`scripts/reports/` altına yazılır.

## 3) Runtime Doğrulama (manuel test)

### (a) Admin Sekmesi Görünüyor mu
- Login ol → üst menüde "Sistem Analitiği" sekmesi var mı?
- Yoksa hesabı admin yap:
  ```bash
  sqlcmd -S "(localdb)\mssqllocaldb" -d OrkaDb -i promote_admin.sql
  ```
- Logout → tekrar login → sekme gelmeli.

### (b) Wiki Kişiselleştirmesi
1. Yeni bir konu başlat.
2. Quiz'de kasıtlı **1-2 yanlış** ver.
3. Konu tamamlanınca wiki üret.
4. Wiki'de zayıf noktana özel vurgu/tekrar bölümü olmalı.
5. Yoksa backend log'unda `[Summarizer]` satırlarını ve profil
   okuma davranışını kontrol et.

### (c) Coding Sorusu Son Index'te mi
1. Programlama konusu başlat, quiz'i getir.
2. Coding sorusu varsa **en sondaki** olmalı.
3. Ortada görürsen:
   - UI'da QuizCard sort fallback sonunda gösterecek → yine ekranda
     en sonda olur.
   - Ama bu LLM prompt itaatsizliği işaretidir → prompt kuralını tekrar
     güçlendirmek gerekebilir.

### (d) Auto-Progression Çift Tetiklenmiyor mu
1. Bir konuyu quiz geçerek bitir.
2. Backend log'unda tek bir `[TOPIC_COMPLETE:` ve tek bir
   `[Summarizer]` satırı olmalı.
3. İki kere basıyorsa guard çalışmamış → `HandleTopicProgressionAsync`
   kontrol edilmeli.

### (e) Adaptive Diagnostic / Course Plan Kalitesi
1. Plan kalite endpoint'i veya yeni konu akisi calistiginda public DTO'da
   `adaptiveDiagnostic` ve `coursePlanQuality` alanlari gorunmeli.
2. Ogrenci kaniti azsa `planReadiness`/`readinessStatus` `needs_diagnostic`
   veya `thin_plan` olmali; plan kendini course-ready diye abartmamali.
3. Zayif/blank/wrong sinyal varsa `needs_repair` ve repair loop gorunmeli.
4. Kaynak hazir degilse source-backed plan iddiasi yok; sadece
   `source_limited`/warning etiketi olmali.
5. Chat metadata chip'leri compact kalmali; raw JSON, prompt, provider
   payload, raw source chunk, owner id veya answer key gorunmemeli.

## 4) LLM-Eval (Opsiyonel — promptfoo)

> Not: Windows'ta `better-sqlite3` native build sorunu çıkabilir. 3 yoldan
> biri:

### Seçenek A — En hızlı (native build atla)
```bash
cd D:/Orka/scripts/llm-eval
rm -rf node_modules package-lock.json
npm install --ignore-scripts

# Windows PowerShell:
$env:PROMPTFOO_DISABLE_CACHE="true"
npx promptfoo eval --env-file .env.local
```

### Seçenek B — Node 20 LTS'e düş (temiz)
```bash
nvm install 20.18.0
nvm use 20.18.0
cd D:/Orka/scripts/llm-eval
rm -rf node_modules package-lock.json
npm install
```

### Seçenek C — VS Build Tools kur (kalıcı)
```bash
winget install Microsoft.VisualStudio.2022.BuildTools --override "--add Microsoft.VisualStudio.Workload.VCTools --includeRecommended --quiet"
```
Terminali yeniden aç → `npm install` sorunsuz çalışır.

### Eval Koşma (A/B/C'den biri bitince)
```bash
cd D:/Orka/scripts/llm-eval

# JWT hazırla (orka_eval_runner@orka.ai)
node prepare-token.mjs

# Groq anahtarını user-secrets'tan al:
# cd D:/Orka/Orka.API && dotnet user-secrets list
# .env.local'a ekle:
#   GROQ_API_KEY=gsk_...

npx promptfoo eval --env-file .env.local
npx promptfoo view   # tarayıcıda sonuç raporu
```

Hedef eşikler (`.claude/rules/testing.md`):
- LLMOps avg score: ≥ 7.0/10
- Primary provider ratio: ≥ 85%

## 5) Hızlı Komut Özeti (kopyala-yapıştır)

```bash
# Backend
cd D:/Orka && dotnet run --project Orka.API

# Healthcheck
node scripts/healthcheck.mjs

# Frontend (ayrı terminal)
cd D:/Orka/Orka-Front && npm run dev

# Admin promote (gerekirse)
sqlcmd -S "(localdb)\mssqllocaldb" -d OrkaDb -i promote_admin.sql

# User-secrets (API anahtarları)
cd D:/Orka/Orka.API && dotnet user-secrets list

# Migration ekle
cd D:/Orka/Orka.Infrastructure && dotnet ef migrations add <İsim> --startup-project ../Orka.API
```

## 6) Bu Oturumda Yapılan Fix'ler (referans)

**Backend:**
- `AgentOrchestratorService.HandleTopicProgressionAsync` — çift
  `CompletedSections` increment guard (son AI mesajında
  `[TOPIC_COMPLETE:` sniff)
- `AgentOrchestratorService.HandleQuizModeAsync` — IDE kod cevabı
  (`**Quiz Sorusu:**` + ` ``` ` pattern) algılama ve `EvaluateQuizAnswerAsync`
  üzerinden değerlendirme
- `AgentOrchestratorService` — raw JSON array quiz detection
  (`[{...}]` pattern) `MessageType.Quiz` işaretlemesi
- `SummarizerAgent` — `IRedisMemoryService` inject, öğrenci profili
  (weakness) + son 5 yanlış `QuizAttempts` wiki prompt'una enjekte
- `TutorAgent.GenerateTopicQuizAsync` — coding sorusu yalnızca son
  index'te + en fazla 1 tane kuralı prompt'a eklendi

**Frontend:**
- `quizParser.ts` — `type` alanı korunuyor (coding sorular için şart)
- `QuizCard.tsx` — palet ihlalleri (green/red/blue → emerald/amber),
  coding-sort fallback (render öncesi sort)
- `InteractiveIDE.tsx` — palet ihlalleri (violet/blue/red → amber)
- `DashboardPanel.tsx` — `bg-gradient-to-br` kaldırıldı
- `SettingsPanel.tsx` — bozuk useEffect wrapper onarıldı
- `ChatMessage.tsx` — `quizData.topic` öncesi `Array.isArray` guard
- `lib/types.ts` — `quiz?: QuizData | QuizData[]` tip genişletmesi

**Yeni Dosyalar:**
- `scripts/llm-eval/promptfooconfig.yaml` (7 senaryo + LLM-as-judge)
- `scripts/llm-eval/prepare-token.mjs`
- `scripts/llm-eval/package.json`
- `scripts/llm-eval/README.md`
- `scripts/llm-eval/.gitignore`

## Production/Staging Migration Policy

```bash
# Staging/Production migration script uret
cd D:/Orka
dotnet ef migrations script --idempotent --project Orka.Infrastructure --startup-project Orka.API -o artifacts/migrations/<name>.sql
```

- `Database:AutoMigrateOnStartup=true` sadece local Development override olarak kullanilir.
- Staging ve Production'da startup auto-migration yasaktir; yanlislikla acilirsa API fail-fast eder.
- Deploy oncesi migration script'i review edilir.

## Backend Release Hardening Gate

- `AiDebugLogger` ham prompt, provider request/response body, source chunk,
  tool payload, debug trace, stack trace, local path, secret/token/api key,
  owner id, unsafe user id veya answer key yazmamalidir.
- AI debug dosya log'u varsayilan olarak kapali kalmali; sadece Development
  ortaminda acik opt-in ile guvenli ozet yazabilir.
- Provider hata log'lari ham body yerine status, body length/hash veya guvenli
  ozet kullanmalidir.
- Provider failure diagnostic (`RedactedDiagnostic`) body-free kalmalidir:
  raw veya redacted response body excerpt yok; yalnizca provider/status/category,
  retryability, body length ve non-reversible hash gibi guvenli metadata vardir.
- Production log privacy gate: backend loglari raw user/topic/session/message/
  source/cache id yazmamali; `UserRef`, `TopicRef`, `SessionRef`, `MessageRef`,
  `SourceRef`, `WorkflowRef`, `KeyRef` gibi maskeli ref kullanmalidir.
- Production loglari raw prompt/provider body/source chunk/tool payload/answer
  key/local path/stack trace/owner id/unsafe user id basmamali; hata icin
  mumkunse safe error type/status metadata kullanilmalidir.
- `quick-backend.ps1`, stabilization baseline'dan once provider-free backend
  lifetest release proof calistirmalidir:
  `BackendLifeTests|PedagogicalReleaseClosureTests`.
- Backend lifetest register/login'den topic/goal, diagnostic/plan, Tutor,
  quiz/remediation, snapshot/mastery, Wiki/Copilot, source evidence, Notebook
  Studio, dashboard, degraded states ve public payload safety akisini kanitlar.
- Test host log filtresi yalnizca release validasyonunu bogan gürültüyü
  azaltir: EF in-memory info loglari, disabled worker info loglari, background
  queue lifecycle info loglari ve MediatR license banner'i. Warning/error
  seviyesindeki gerçek backend problemleri gorunur kalmalidir.
- Quick script'ler provider-free kalmali, secret basmamali, timeout ile fail
  etmeli ve lifetest proof + regression baseline siralamasini korumalidir.
- GitHub CI backend release gate `.github/workflows/backend-release.yml`
  `windows-latest` uzerinde .NET 8, SQL Server LocalDB, `quick-backend.ps1`,
  Infrastructure unit testleri ve `git diff --check` calistirmalidir.
- CI gate real AI provider secret'i, `ORKA_RUN_EXTERNAL_PROVIDER_TESTS` veya
  paid provider smoke testi kullanmamalidir.
- Bu gate yeni AI/provider cagrisi, OpenAI API migrasyonu veya paid provider
  validasyonu gerektirmez.
- Backend Production Readiness Phase 2 provider staging proof:
  - Provider secret degerleri yazdirilmaz; yalnizca configured true/false
    raporlanir.
  - Live AI success smoke icin explicit token ve explicit call plan gerekir;
    token yoksa success proof blocked sayilir.
  - Invalid-token external provider testi opt-in calisabilir ve sadece safe
    failure davranisini kanitlar.
  - Wikipedia/Open-Meteo/CoinGecko gibi keyless public provider kontrolleri
    manuel smoke olabilir; quick script/CI baseline'a eklenmez.
  - Provider failure diagnostic body-free kalir; raw body excerpt yoktur.
- Backend Production Readiness Phase 3 scale/ops gate:
  - `BackgroundTaskQueue` bounded channel ve per-job timeout davranisini korur.
  - SRS, daily challenge, retention cleanup ve Redis stream maintenance
    worker'lari config-gated, batch/interval bounded ve safe-log kalir.
  - Protected Staging/Production startup DB/Redis/CORS/AllowedHosts/JWT/refresh
    cookie/rate limit/migration readiness/global-user AI cost limit guard'larini
    fail-closed kontrol eder.
  - Source upload/file/extraction/chunk/embedding/hourly/daily/topic limitleri
    korunur; raw source chunk loglanmaz.
  - Provider-free quick script/CI baseline'a live provider veya paid load testi
    eklenmez.
  - Audio retention summary tum audio byte payloadlarini materialize etmez;
    aggregate DB sorgulari ile sayim/toplam yapar.

## OrkaLM Phase 17 Source Notebook Gate

Phase 17 dogrulamasinda ek olarak kontrol edilir:

- `GET /api/sources/topic/{topicId}/notebook` user-scoped ve raw chunk/prompt/provider/local path sizdirmaz.
- `GET /api/sources/{sourceId}/notebook` baska kullanici kaynagini dondurmez.
- `POST /api/notebook-studio/sources/{sourceId}/pack` source-centered pack uretir ve `sourceSurface/sourceId` DTO alanlarini doldurur.
- Source pack ayni source icin duplicate source page spam'i yapmaz; `WikiPage.PageType=orkalm_source` kullanir.
- Frontend OrkaLM mode source readiness/evidence/citation warning gosterir.
- Source evidence panel raw `chunk.text` render etmez ve raw chunk'i Tutor prompt'una tasimaz.
- Yeni provider/Google Cloud/PPTX/video entegrasyonu eklenmez.
- Destructive migration (`DROP COLUMN`, `DROP TABLE`, geri alinamaz data rewrite) varsa DB backup/snapshot zorunludur.
- Rollback destructive migrationlarda sadece kod revert degildir; DB restore veya explicit rollback script gerekir.
- Migration apply sonrasi `/health/ready` kontrol edilir; `ef-migrations` check'i pending migration birakmamalidir.

## OrkaLM Phase 18 Source-to-Concept Graph Gate

Phase 18 dogrulamasinda ek olarak kontrol edilir:

- `GET /api/sources/{sourceId}/concept-links` user-scoped ve raw chunk/prompt/provider/local path/owner id sizdirmaz.
- `POST /api/sources/{sourceId}/concept-links/sync` idempotent calisir; ayni source/concept icin duplicate `WikiLink` uretmez.
- Exact concept key veya guclu title match high/medium confidence link uretir; dusuk confidence sonuc suggestion olarak kalir.
- Stale/deleted/insufficient source evidence source-backed claim'e donusmez; warning/degraded state gorunur.
- `GET /api/sources/topic/{topicId}/concept-graph` safe source/concept/page node ve edge DTO'lari dondurur.
- `GET /api/wiki/pages/{pageId}/source-links` concept page destekleyen source'lari user-scoped dondurur.
- Source ve Wiki page Notebook pack'leri linked concept/source context'i safe metadata olarak tasir.
- Frontend OrkaLM mode related concept pages, confidence label, sync action ve graph summary gosterir.
- Wiki concept page supporting sources gosterir; Tutor-generated note ile source-grounded note karistirilmaz.
- Yeni provider/Google Cloud/graph canvas/PPTX/video entegrasyonu eklenmez.

## OrkaLM Phase 19 Ask-Source UX Gate

Phase 19 dogrulamasinda ek olarak kontrol edilir:

- `POST /api/sources/{sourceId}/ask` user-scoped calisir; baska kullanici kaynagi NotFound/forbidden davranisi verir.
- `POST /api/sources/topic/{topicId}/ask` sadece kullanicinin kendi topic/source collection baglamini kullanir.
- `POST /api/sources/ask` sourceId/topicId baglamindan safe ask-source sonucu dondurur.
- Public `SourceQuestionResponseDto` raw chunk/highlight, prompt, provider/tool/debug payload, local path, owner id, secret veya answer key sizdirmaz.
- Citation label'lari kullanici guvenli etiketlerdir; raw source excerpt/chunk metni olarak render edilmez.
- Missing/stale/insufficient evidence `source_grounded` claim'e donusmez; `evidence_insufficient`, `mixed` veya `degraded` olarak gorunur.
- `writeWikiTrace=true` ise safe `student_question` ve source answer Wiki block'lari yazilir; trace failure ask-source cevabini bozmaz.
- Frontend OrkaLM mode selected-source ve source-collection ask action'larini, source basis/evidence/readiness label'larini, citation chips'i ve related concept/page linklerini gosterir.
- Yeni provider/Google Cloud/NotebookLM clone/multi-source compare/PPTX/video entegrasyonu eklenmez.

## OrkaLM Phase 20 Multi-source Compare & Citation Review Gate

Phase 20 dogrulamasinda ek olarak kontrol edilir:

- `POST /api/sources/compare` ve `POST /api/sources/topic/{topicId}/compare` sadece kullanicinin kendi kaynaklarini karsilastirir; baska kullanici kaynagi NotFound/forbidden davranisi verir.
- Compare sonucu source readiness, evidence status, citation coverage, shared/source-only concept overlap, warnings ve next actions dondurur.
- Compare semantic agreement/contradiction iddiasi uretmez; yalnizca deterministic coverage/overlap/review-needed state gosterir.
- `GET /api/sources/{sourceId}/citation-review` ve `GET /api/sources/topic/{topicId}/citation-review` supported/unsupported/missing/stale/needs_review durumlarini raw answer/claim/chunk olmadan dondurur.
- Public compare/review DTO'lari raw source chunk, prompt, provider/tool/debug payload, local path, owner id, secret veya answer key sizdirmaz.
- `writeWikiTrace=true` ise safe compare/review Wiki block'u yazilir; trace failure compare sonucunu bozmaz.
- Frontend OrkaLM mode source selection, compare selected action, compared source cards, citation review panel, readiness/evidence/citation warnings ve shared concept linklerini gosterir.
- Yeni provider/Google Cloud/full semantic compare/citation manager/PPTX/video entegrasyonu eklenmez.

## OrkaLM Phase 21 Source Q&A Memory Gate

Phase 21 dogrulamasinda ek olarak kontrol edilir:

- `GET /api/sources/question-threads`, `GET /api/sources/question-threads/{threadId}`, `POST /api/sources/question-threads`, `POST /api/sources/question-threads/{threadId}/ask`, `PATCH /api/sources/question-threads/{threadId}/review` ve `POST /api/sources/question-threads/{threadId}/wiki-trace` user-scoped calisir.
- Source Q&A thread memory `source_question_thread` LearningArtifact olarak saklanir; yeni provider/Google Cloud veya ayri chat app eklenmez.
- Thread DTO'lari safe question, safe answer summary, source basis, evidence status, citation label/status, related concept/page, review status ve warnings dondurur.
- Thread DTO/artifact/Wiki trace raw source chunk, prompt, provider/tool/debug payload, local path, owner id, secret veya answer key sizdirmaz.
- Follow-up context yalnizca bounded safe prior summary kullanir; raw chunk veya raw hidden prompt context kullanmaz.
- Unsupported/missing/stale citation review state source-grounded claim'e yukseltilmez.
- `writeWikiTrace` veya thread Wiki trace action safe student question/source answer summary block'u yazar; trace failure source Q&A memory akisini bozmaz.
- Source Notebook pack summary/metadata ilgili safe Q&A memory count/review warning bilgisini tasir.
- Frontend OrkaLM mode source Q&A thread list, active thread, prior question/summary cards, follow-up action, citation review label, unresolved/degraded warning ve write-to-Wiki action gosterir.
- Full NotebookLM clone, raw transcript store, CRM-style review queue, manual citation manager, PPTX/video entegrasyonu eklenmez.

## OrkaLM Phase 22-23 Source Study Workflow Gate

Phase 22-23 dogrulamasinda ek olarak kontrol edilir:

- `GET /api/sources/study-summary` user-scoped calisir ve topic/source/wiki page context disina veri sizdirmaz.
- Source study summary mevcut `source_question_thread` artifact'leri, citation check state, source readiness ve source-to-concept linklerinden deterministic olarak turetilir; yeni provider/Google Cloud/migration eklenmez.
- Summary thread/turn/review/degraded/citation warning/related concept/compare-ready source count, study status ve recommended next action dondurur.
- Summary DTO raw source chunk, prompt, provider/tool/debug payload, local path, owner id, secret veya answer key sizdirmaz.
- Frontend OrkaLM mode `Source study status` bandini, review/degraded/citation warning count'larini, linked concept count'ini ve next action etiketlerini gosterir.
- Source study workflow fake schedule/due date, semantic agreement/contradiction claim, manual citation manager, graph canvas, PPTX/video entegrasyonu veya NotebookLM parity iddiasi eklemez.

## Production/Staging CORS ve Environment Gates

- Staging/Production'da `Cors:AllowedOrigins` bos birakilamaz ve `*` kullanilamaz; API fail-fast eder.
- `Cors:AllowAnyOriginInDevelopment=true` sadece local Development override icindir.
- Env ornegi:
  ```powershell
  $env:Cors__AllowedOrigins__0 = "https://staging.orka.example"
  $env:Cors__AllowedOrigins__1 = "https://app.orka.example"
  ```
- Deploy checklist'te JWT signing secret, `JWT:RefreshTokenHashSecret`, Redis connection, AI provider secrets ve CORS originleri birlikte dogrulanir.
- Staging/Production CSP enforced gelir; gerekiyorsa sadece `SecurityHeaders:Csp:AdditionalConnectSrc` ile gerekli origin eklenir.
- `SecurityHeaders:Csp:Enabled=false` Production/Staging icin kabul edilmez; acil durumda once security owner onayi gerekir.

## Content Safety Transport Limits

- `ContentSafety:Uploads:MaxFileBytes` dosya limitidir.
- `ContentSafety:Uploads:MaxMultipartBodyBytes` multipart/form overhead limitidir; normalde `MaxFileBytes + 1MiB` olarak tutulur.
- Sources ve Korteks upload davranisi bu iki limit ile birlikte dogrulanir; transport limiti runtime guard'dan daha genis ve kontrollu olmalidir.

## Storage / Cost Context Migrations

- Additive cleanup migration `LearningSources.FileSizeBytes` ve `CostRecords.TopicId` ekler.
- Upload storage accounting source delete/recalculate testleriyle korunur.
- Topic/account delete `CostRecords.TopicId` ve scoped `MetadataJson` alanlarini anonymize eder.
- Migration script review komutu:
  ```powershell
  dotnet ef migrations script --idempotent --project Orka.Infrastructure --startup-project Orka.API -o artifacts/migrations/v1-cleanup.sql
  ```

## CI Relational Lifecycle Smoke

- `DataLifecycleTests` SQL Server relational smoke calistirir; EF InMemory tek basina yeterli kabul edilmez.
- Windows CI/local icin `(localdb)\OrkaLocalDB` instance'i gerekir. Hazirlamak icin:
  ```powershell
  powershell -ExecutionPolicy Bypass -File scripts\reset-dev-db.ps1
  ```

## OrkaLM / Wiki-aware Notebook Studio Closure Validation

```powershell
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter "LearningNotebookStudioTests|WikiGraphContractTests|LearningArtifactsEngineTests|SourceEvidenceLifecycleTests|QuizAttemptSafetyTests|AssessmentQualityMisconceptionTests|TutorPedagogyPolicyTests|AgenticSecurityTrustTests|SourceRegressionGuardTests" --no-restore --verbosity minimal
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter RegressionGateScriptTests --no-restore --verbosity minimal
dotnet test .\Orka.Infrastructure.UnitTests\Orka.Infrastructure.UnitTests.csproj --no-restore --verbosity minimal
scripts\quick-coordination.ps1
scripts\quick-backend.ps1
cd Orka-Front; npm run typecheck; npm run quick:smoke; npm run quick:build
git diff --check
```

- Notebook Studio must stay Wiki page-aware and source-evidence-aware.
- `source_digest` cannot claim source grounding unless evidence is ready or mixed.
- Review quiz artifacts cannot expose answer keys before submit.
- Audio overview must prefer Notebook pack context when a pack exists and degrade to script-only safely when TTS is unavailable.
- Frontend Notebook Studio copy should stay readable Turkish/ASCII and must not render raw prompt/provider/source/tool/debug payloads.

## OrkaLM Phase 11 Advanced Media / Export Gate

```powershell
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter "LearningNotebookStudioTests|LearningArtifactsEngineTests|SourceEvidenceLifecycleTests|AgenticSecurityTrustTests|QuizAttemptSafetyTests|SourceRegressionGuardTests" --no-restore --verbosity minimal
cd Orka-Front
npm run typecheck
npm run quick:smoke
npm run quick:build
cd ..
git diff --check
```

- `video_ready_package` is an outline/manifest artifact only; it must not claim a generated video exists.
- `slide_export_manifest` is an export-ready data artifact only; it must not claim PPTX generation/download exists.
- Audio transcript/caption artifacts must remain text fallback artifacts with no raw source chunks, prompts, provider payloads, local paths, answer keys, or debug traces.
- Notebook Studio UI must show media/export readiness labels without adding fake media players or fake PPTX download copy.

## OrkaLM Phase 12 Slide Export MVP Gate

```powershell
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter "LearningNotebookStudioTests|LearningArtifactsEngineTests|SourceEvidenceLifecycleTests|AgenticSecurityTrustTests|QuizAttemptSafetyTests|SourceRegressionGuardTests" --no-restore --verbosity minimal
cd Orka-Front
npm run typecheck
npm run quick:smoke
npm run quick:build
cd ..
git diff --check
```

- Slide export must transform existing `slide_deck_outline` / `slide_export_manifest` artifacts only; it must not call AI or generate new learning content.
- Supported export outputs are preview, Markdown, escaped HTML, and manifest-only.
- `pptx_local_proof` must return an honest unsupported / `pptx_not_enabled` result unless a safe local dependency is explicitly approved.
- Export DTOs must not expose raw prompt/provider/source/tool/debug payloads, local paths, owner ids, answer keys, or stack traces.
- Notebook Studio UI must show export readiness and PPTX-disabled status without fake download copy.

## OrkaLM Phase 13 Advanced Deck UX / Export Decision Gate

```powershell
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter "LearningNotebookStudioTests|LearningArtifactsEngineTests|SourceEvidenceLifecycleTests|AgenticSecurityTrustTests|QuizAttemptSafetyTests|SourceRegressionGuardTests" --no-restore --verbosity minimal
cd Orka-Front
npm run typecheck
npm run quick:smoke
npm run quick:build
cd ..
git diff --check
```

- PPTX decision must be evidence-based: keep `pptx_local_proof` unsupported unless Orka itself has an approved safe presentation export dependency.
- Export preview must show deck title, slide count, source basis/readiness, warnings, accessibility summary, slide list, checkpoints, speaker-note availability, and source labels.
- Markdown, escaped HTML, and manifest-only exports must remain deterministic transformations of existing safe artifacts; no AI calls or new learning generation.
- Export payloads must not expose raw prompt/provider/source/tool/debug payloads, local paths, owner ids, answer keys, stack traces, or secrets.
- Notebook Studio UI must show preview/Markdown/HTML/manifest availability and PPTX-disabled status without fake download, generated video, or official/success copy.

## OrkaLM Phase 14 Final Professional Audit Gate

Closure doc: `docs/project-state/orka-notebook-studio-final-audit.md`

```powershell
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter "LearningNotebookStudioTests|WikiGraphContractTests|LearningArtifactsEngineTests|SourceEvidenceLifecycleTests|QuizAttemptSafetyTests|AssessmentQualityMisconceptionTests|TutorPedagogyPolicyTests|AgenticSecurityTrustTests|SourceRegressionGuardTests" --no-restore --verbosity minimal
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter RegressionGateScriptTests --no-restore --verbosity minimal
dotnet test .\Orka.Infrastructure.UnitTests\Orka.Infrastructure.UnitTests.csproj --no-restore --verbosity minimal
scripts\quick-coordination.ps1
scripts\quick-backend.ps1
cd Orka-Front
npm run typecheck
npm run quick:smoke
npm run quick:build
cd ..
git diff --check
git status --short
```

- Final PASS requires no score 0-2, no core Wiki/OrkaLM/safety/export score 3, and no safety/trust/privacy score below 4.
- Wiki must remain page-aware: page graph, page blocks/questions/repair notes, source links, packs, and artifacts must connect.
- Export must remain deterministic and honest: preview, Markdown, escaped HTML, manifest-only, and `pptx_not_enabled` unless a safe runtime dependency is explicitly approved.
- Public DTOs/frontend must not expose raw prompt/provider/source/tool/debug payloads, local paths, owner ids, answer keys, stack traces, or secrets.
- Browser screenshot proof is preferred when Browser/Playwright is available; otherwise typecheck, smoke, build, and backend tests are the closure proof.

## OrkaLM Phase 15 Wiki Vault UX Gate

Before closing Wiki Vault UX productization:

```powershell
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter "LearningNotebookStudioTests|WikiGraphContractTests|SourceEvidenceLifecycleTests|AgenticSecurityTrustTests|QuizAttemptSafetyTests|SourceRegressionGuardTests" --no-restore --verbosity minimal
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter RegressionGateScriptTests --no-restore --verbosity minimal
dotnet test .\Orka.Infrastructure.UnitTests\Orka.Infrastructure.UnitTests.csproj --no-restore --verbosity minimal
scripts\quick-coordination.ps1
scripts\quick-backend.ps1
cd Orka-Front
npm run typecheck
npm run quick:smoke
npm run quick:build
cd ..
git diff --check
git status --short
```

- `WikiMainPanel` must show a student-facing Wiki Vault surface: page tree/list, search/filter, active page context badges, backlinks, outgoing links, local graph neighbors, and block group summaries.
- `NotebookStudioPanel` must remain page-aware through the active `wikiPageId` and must not leak packs across selected Wiki pages.
- Phase 15 must not add AI calls, Google Cloud, a full Obsidian clone, complex graph canvas editing, real PPTX export, or video generation.
- Public UI must not expose raw prompt/provider/source/tool/debug payloads, local paths, owner ids, answer keys, stack traces, secrets, official/success claims, or teacher/classroom/dershane copy.

## OrkaLM Phase 16 Tutor-Wiki Trace Writer Gate

Before closing Tutor-Wiki learning trace writing:

```powershell
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter "LearningNotebookStudioTests|WikiGraphContractTests|TutorPedagogyPolicyTests|QuizAttemptSafetyTests|AssessmentQualityMisconceptionTests|LearningArtifactsEngineTests|SourceEvidenceLifecycleTests|AgenticSecurityTrustTests|SourceRegressionGuardTests" --no-restore --verbosity minimal
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter RegressionGateScriptTests --no-restore --verbosity minimal
dotnet test .\Orka.Infrastructure.UnitTests\Orka.Infrastructure.UnitTests.csproj --no-restore --verbosity minimal
scripts\quick-coordination.ps1
scripts\quick-backend.ps1
cd Orka-Front
npm run typecheck
npm run quick:smoke
npm run quick:build
cd ..
git diff --check
git status --short
```

- `IWikiLearningTraceWriter` must remain the canonical path for Tutor/student/quiz/repair/source/artifact trace blocks.
- Trace writing must be page-aware, user-scoped, deduped, and non-blocking for chat, quiz, artifact, and source flows.
- Trace blocks must not store raw prompt/provider/source/tool/debug payloads, local paths, owner ids, pre-submit answer keys, stack traces, or secrets.
- Quiz traces must be post-submit only and must not trust client-provided correctness.
- Phase 16 must not add AI calls, Google Cloud, frontend redesign, hidden admin tooling, or teacher/classroom/dershane workflows.

## OrkaLM Phase 24-25 Final Safety / Product Closure Gate

Before closing final OrkaLM safety/privacy/product audit:

```powershell
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter "SourceEvidenceLifecycleTests|SourceRegressionGuardTests|WikiGraphContractTests|LearningNotebookStudioTests|LearningArtifactsEngineTests|TutorPedagogyPolicyTests|AgenticSecurityTrustTests|QuizAttemptSafetyTests" --no-restore --verbosity minimal
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter RegressionGateScriptTests --no-restore --verbosity minimal
dotnet test .\Orka.Infrastructure.UnitTests\Orka.Infrastructure.UnitTests.csproj --no-restore --verbosity minimal
scripts\quick-coordination.ps1
scripts\quick-backend.ps1
cd Orka-Front
npm run typecheck
npm run quick:smoke
npm run quick:build
cd ..
git diff --check
git status --short
git diff --cached --name-only
```

- Source notebook, ask-source, Q&A memory, compare/citation review, source-study summary, Wiki trace, Notebook packs, artifacts, and export surfaces must remain user-scoped.
- Public OrkaLM DTOs and frontend surfaces must not expose owner/user ids, raw source chunks, raw highlights, prompts, provider/tool/debug payloads, local paths, secrets, stack traces, or pre-submit answer keys.
- Source-grounded labels require ready/mixed evidence; stale/deleted/insufficient sources must degrade source Q&A, compare, study summary, packs, artifacts, and supporting-source UI.
- Multi-source compare remains deterministic trust/coverage/concept-overlap review, not semantic agreement or contradiction detection.
- PPTX/video remain honestly disabled unless a future explicit local proof adds product runtime support.
- Phase 24-25 is audit/closure only. Phase 26 should only prepare user-directed selective staging/commit.

## System Closure Gate

Frontend baseline oncesi deterministic kapanis hatti:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\quick-coordination.ps1
powershell -ExecutionPolicy Bypass -File scripts\quick-backend.ps1
cd Orka-Front
npm run typecheck
npm run quick:smoke
cd ..
git diff --check
```

- Mandatory quick hat external HTTP/provider bagimliligi tasimaz.
- External provider smoke sadece acik env flag ile manuel calistirilir.
- Stream/SSE client'lar `authenticatedFetch` kullanir; `Bearer null` gonderilmez.
- Auth cleanup sadece Orka localStorage key'lerini temizler.
- `dist`, `node_modules`, pasted image ve local report dosyalari commit'e alinmaz.
- Linux/container CI icin `ORKA_LIFECYCLE_SQLSERVER_BASE_CONNECTION` verilebilir. Test disposable database adini kendisi ekler.
- SQL Server yoksa `quick-backend.ps1` bilincli fail eder; bu testler sessizce skip edilmemeli.

## Main Learning OS Final Audit Gate

- Closure doc: `docs/project-state/main-learning-os-professionalization-closure.md`
- Pack 0-11 validation green olsa bile final closure PASS sayilmaz; profesyonel
  kalite scorecard da gecmeli.
- Legacy chat/general quiz answer-key mini-fix tamamlandi: aktif/pre-submit
  `QuizCard` answer key tasimaz, public `/api/quiz/attempt` client correctness'e
  guvenmez, durable item yoksa sonuc observed-only kalir.
- Final selective staging/commit artik kullanici yonlendirmesiyle yapilabilir.

## Production Safety Lite Gate

Frontend baseline ve kucuk/orta ozelliklerden once protected environment guardlari:

```powershell
cd D:/Orka
dotnet test Orka.API.Tests/Orka.API.Tests.csproj --filter ProductionSafetyLiteTests --no-restore --verbosity minimal
powershell -ExecutionPolicy Bypass -File scripts\quick-coordination.ps1
powershell -ExecutionPolicy Bypass -File scripts\quick-backend.ps1
git diff --check
git status --short
```

- `/health/live` public ve minimal kalir.
- `/health/ready` ve `/health` Staging/Production'da check detail, exception veya connection bilgisi sizdirmaz.
- Detailed system health yalnizca admin `/api/dashboard/system-health` uzerindedir.
- Staging/Production `AllowedHosts`, explicit CORS origin, SQL Server, Redis, JWT secrets, Redis auth limiter ve AI global/user budget olmadan baslamaz.
- Staging/Production refresh cookie config'i `Auth__RefreshCookie__Secure=true`, explicit SameSite ve `/api/auth` path guardlariyla dogrulanir.
- Staging/Production `SecurityHeaders` HSTS, nosniff, Referrer-Policy, Permissions-Policy, X-Frame-Options ve enforced CSP set eder.
- AI budget guard provider cagrisindan once user/global daily limitleri uygular; quota exceeded response user-safe kalir.
- Full httpOnly cookie migration, Redis scale mimarisi ve enterprise SLO/observability bu gate'in kapsaminda degildir.

## Codex Skills Gate

Feature isleri `docs/project-state/current-roadmap.md`, `docs/architecture/orka-learning-os-contract-map.md`
ve `docs/codex-skills/` anayasalarini takip eder. Stage 6B Central Exams ve
Post-6B Professionalization kapandi; current phase Main Learning OS
Professionalization'dir.

Stage 6B closure guard:

- Central Exams Orka icinde entegre ogrenci-facing modul olarak kalir.
- KPSS calisan ilk sinavdir; YKS/LGS/YDS safe scaffold / coming-soon kalir.
- Official curriculum / OSYM / MEB claim sadece verified source metadata ile olabilir.
- Success guarantee, copyrighted/scraped content assumption, PDF/OCR/NotebookLM dependency, teacher/classroom/dershane workflow ve auto-publish yoktur.
- Generated/imported questions draft / needs_review olarak kalir; publish existing question bank validation ile yapilir.

Before planning/coding:

- `docs/project-state/current-roadmap.md` okunur.
- Tutor/Korteks/RAG/Wiki/Quiz/Tool/Plan isi varsa `docs/architecture/orka-learning-os-contract-map.md` okunur.
- `docs/codex-skills/README.md` okunur.
- Her feature icin `testing-gate-constitution.md` okunur.
- Backend/API/data degisiyorsa `backend-feature-constitution.md` okunur.
- AI/RAG/Wiki/Chat/Korteks/source/citation degisiyorsa `ai-rag-feature-constitution.md` okunur.
- Frontend API/types/stream/UI contract degisiyorsa `frontend-contract-constitution.md` okunur.
- Yeni durable veri, Redis/cache/session veya delete/privacy etkisi varsa `data-lifecycle-constitution.md` okunur.

Feature prompt/report:

- Yeni feature prompt'u `feature-prompt-template.md` formatini takip eder.
- Final rapor `feature-completion-report-template.md` formatini takip eder.
- Test sonucunda calismayan veya atlanan gate varsa acikca yazilir.
- Stage/commit sadece kullanici acikca isterse yapilir.

## Optional External Provider Smoke

- Default regression'a dahil degildir; sadece env gate ile calisir.
- Calistirma:
  ```powershell
  $env:ORKA_RUN_EXTERNAL_PROVIDER_TESTS="true"
  $env:ORKA_EXTERNAL_GITHUB_MODELS_TOKEN="<token>"
  dotnet test Orka.API.Tests\Orka.API.Tests.csproj --filter ExternalProviderIntegrationTests --no-restore --verbosity minimal
  ```
- Env gate veya token yoksa test acik skip nedeni yazar ve provider cagrisi yapmaz.

## Paket 3 Dev Contract

Canonical local URLs:

- Backend API: `http://localhost:5065`
- Frontend dev server: `http://localhost:3000`
- Runtime/API smoke env var: `ORKA_API_URL`
- Frontend proxy env var: `VITE_API_PROXY_TARGET`

Quick regression:

```powershell
cd D:/Orka
powershell -ExecutionPolicy Bypass -File scripts\quick-backend.ps1
```

Full local quick line when frontend dependencies are available:

```powershell
cd D:/Orka
powershell -ExecutionPolicy Bypass -File scripts\quick-all.ps1
```

Runtime smoke when API is already running:

```powershell
node scripts/healthcheck.mjs --base-url=http://localhost:5065 --quick
$env:ORKA_API_URL="http://localhost:5065"; pytest contract_tests/
```

`5101` is legacy audit history only; do not use it as a new active default.

## OrkaLM / Notebook Studio Phase 10 Gate

Before closing OrkaLM production hardening:

- Confirm `LearningNotebookPack` entity, `OrkaDbContext`, `AddLearningNotebookStudio` migration, and `OrkaDbContextModelSnapshot` are aligned.
- Confirm Wiki page packs can be created, listed by `wikiPageId`, refreshed, and hidden when soft-deleted.
- Confirm topic/milestone packs still work after Wiki page filtering.
- Confirm Notebook Studio UI shows source readiness, evidence status, stale/degraded/insufficient warnings, grouped artifacts, audio fallback/player, and next actions.
- Confirm frontend smoke guards payload safety, answer-key safety, official/success/teacher/classroom copy, and Notebook Studio mojibake.
- Run:
  ```powershell
  dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter "LearningNotebookStudioTests|WikiGraphContractTests|LearningArtifactsEngineTests|SourceEvidenceLifecycleTests|QuizAttemptSafetyTests|AssessmentQualityMisconceptionTests|TutorPedagogyPolicyTests|AgenticSecurityTrustTests|SourceRegressionGuardTests" --no-restore --verbosity minimal
  dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter RegressionGateScriptTests --no-restore --verbosity minimal
  dotnet test .\Orka.Infrastructure.UnitTests\Orka.Infrastructure.UnitTests.csproj --no-restore --verbosity minimal
  scripts\quick-coordination.ps1
  scripts\quick-backend.ps1
  cd Orka-Front; npm run typecheck; npm run quick:smoke; npm run quick:build
  git diff --check
  ```
- If Browser or local Playwright tooling is available, capture WikiMainPanel / NotebookStudioPanel screenshots for selected pack, empty state, artifact list, audio fallback, and slide/mind-map/review actions.

## Phase 27 Post-closure Polish Gate

Before release-polish closure:

- Confirm `git status --short` before and after the phase; do not stage/commit without explicit user approval.
- Run a global public DTO/API privacy sweep for `userId`, `ownerId`, raw source chunks, prompt/provider/tool/debug payloads, local paths, secrets, and answer keys.
- Confirm public Tutor/source/Wiki/Notebook endpoints use safe DTOs or explicit projections rather than raw entities with owner ids or raw JSON payloads.
- Confirm touched frontend copy describes personal Tutor/audio lesson flows, not live classroom, teacher dashboard, dershane, or institutional workflows.
- Attempt Browser visual E2E for Wiki Vault, OrkaLM source notebook, source-study summary, source Q&A memory, compare/citation review, Notebook Studio, and export preview when local tooling/auth data allows it.
- For Browser proof, use seeded data where possible: create/select a topic and upload at least one safe source file before judging OrkaLM source notebook UX.
- Verify OrkaLM source mode renders source notebook data even when Wiki pages are empty; it must not remain stuck on Wiki generation polling.
- Check one narrow viewport smoke pass; compact sidebar behavior is acceptable, broad mobile redesign is not part of this gate.
- Confirm release-blocker cleanup has removed `%100`/guarantee-style student-facing copy, stabilized auth/security flake targets, and preserved a provider-free deterministic learning-loop smoke proof.
- Confirm blank/skipped quiz answers produce prerequisite/guided-repair learning impact and safe Wiki repair traces without pre-submit answer keys or fake misconception certainty.
- Run:
  ```powershell
  dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter "SourceEvidenceLifecycleTests|SourceRegressionGuardTests|WikiGraphContractTests|LearningNotebookStudioTests|LearningArtifactsEngineTests|TutorPedagogyPolicyTests|AgenticSecurityTrustTests|QuizAttemptSafetyTests" --no-restore --verbosity minimal
  dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter RegressionGateScriptTests --no-restore --verbosity minimal
  dotnet test .\Orka.Infrastructure.UnitTests\Orka.Infrastructure.UnitTests.csproj --no-restore --verbosity minimal
  scripts\quick-coordination.ps1
  scripts\quick-backend.ps1
  cd Orka-Front; npm run typecheck; npm run quick:smoke; npm run quick:build
  git diff --check
  git status --short
  git diff --cached --name-only
  ```

## Pedagogical Productization Phase 5 Gate

Before closing Wiki Auto-Curation & Learning Memory Cleanup:

- Confirm `WikiAutoCurationService` emits `WikiCurationSummaryDto` without raw prompt/provider/tool/source/debug payloads, local paths, owner ids, or answer keys.
- Confirm Wiki page hygiene can report clean, duplicate trace, stale trace, repair pending, source limited, or degraded states with safe warnings and next action.
- Confirm `WikiLearningTraceWriter` dedupes repeated normalized Tutor/repair/source/artifact traces while preserving student manual notes.
- Confirm `LearningMemoryService` and `ActiveLessonSnapshotService` expose bounded `LearningMemoryHygieneDto` summaries, not raw transcripts.
- Confirm Tutor/chat metadata and Notebook Studio pack metadata consume curated Wiki/memory summaries without treating Tutor notes as source citations.
- Confirm frontend Wiki/chat surfaces render compact curation/memory labels without raw JSON, raw chunks, answer keys, or overclaim copy.
- Run:
  ```powershell
  dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter "TutorPedagogyPolicyTests|QuizLearningPipelineTests|AssessmentQualityMisconceptionTests|QuizAttemptSafetyTests|LearningSnapshotTests|ChatParityTests|PlanQualityTests|PlanQualitySequencingTests|AssessmentBlueprintTests|AgenticSecurityTrustTests|WikiGraphContractTests|LearningNotebookStudioTests|SourceEvidenceLifecycleTests" --no-restore --verbosity minimal
  dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter RegressionGateScriptTests --no-restore --verbosity minimal
  dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --no-restore --verbosity minimal
  dotnet test .\Orka.Infrastructure.UnitTests\Orka.Infrastructure.UnitTests.csproj --no-restore --verbosity minimal
  scripts\quick-coordination.ps1
  scripts\quick-backend.ps1
  cd Orka-Front; npm run typecheck; npm run quick:smoke; npm run quick:build
  git diff --check
  git status --short
  git diff --cached --name-only
  ```

## Pedagogical Productization Phase 6 Gate

Before closing Wiki Copilot UX:

- Confirm `IWikiCopilotService` emits `WikiCopilotContextDto` without raw prompt/provider/tool/source/debug payloads, local paths, owner ids, or answer keys.
- Confirm Copilot is page-aware: active page title/type/concept, curation status, source readiness/evidence status, repair state, weak concepts, artifact count, and Notebook pack status are safe public fields.
- Confirm suggestions are deterministic handoffs, not hidden autonomous actions: repair pending -> repair/checkpoint, weak concept -> review/checkpoint, source ready -> ask/inspect source, source limited -> blocked/degraded source actions, thin page -> Tutor explanation.
- Confirm frontend Wiki Copilot panel renders compact next action/suggestions/warnings without raw JSON or source chunks.
- Confirm user scoping blocks other-user page copilot access.
- Run:
  ```powershell
  dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter "TutorPedagogyPolicyTests|QuizLearningPipelineTests|AssessmentQualityMisconceptionTests|QuizAttemptSafetyTests|LearningSnapshotTests|ChatParityTests|PlanQualityTests|PlanQualitySequencingTests|AssessmentBlueprintTests|AgenticSecurityTrustTests|WikiGraphContractTests|LearningNotebookStudioTests|SourceEvidenceLifecycleTests" --no-restore --verbosity minimal
  dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter RegressionGateScriptTests --no-restore --verbosity minimal
  dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --no-restore --verbosity minimal
  dotnet test .\Orka.Infrastructure.UnitTests\Orka.Infrastructure.UnitTests.csproj --no-restore --verbosity minimal
  scripts\quick-coordination.ps1
  scripts\quick-backend.ps1
  cd Orka-Front; npm run typecheck; npm run quick:smoke; npm run quick:build
  git diff --check
  git status --short
  git diff --cached --name-only
  ```

## Pedagogical Productization Phase 7 Gate

Before closing Final Pedagogical E2E, Evaluation Harness & Release Closure:

- Confirm `PedagogicalReleaseClosureTests` connects the provider-free learning loop end to end: topic/goal, adaptive diagnostic, course-plan quality, Tutor tool decision, lesson delivery, remediation, quiz impact, learning snapshot, Wiki curation/Copilot, OrkaLM source notebook, Notebook Studio pack, dashboard, and public payload safety.
- Confirm `BackendLifeTests` runs senior-QA style HTTP lifecycles from register/login through topics, plan/diagnostic, Tutor/chat, quiz/remediation, source upload/evidence, Wiki Copilot, Notebook Studio export preview, dashboard, cross-user privacy, and degraded source states.
- Confirm no new provider calls, paid calls, Google Cloud, OpenAI Responses/Agents migration, Stripe live calls, real PPTX/video, Realtime voice, graph canvas, or classroom/teacher workflow was added.
- Confirm public Phase 1-7 metadata surfaces do not expose raw prompt/provider/tool/source/debug payloads, local paths, owner ids, user ids, or pre-submit answer keys.
- Confirm Stripe/payment code is either absent/not applicable or audited for test/live mode, webhook signature safety, and secret hygiene without live external calls.
- Confirm frontend smoke covers compact Tutor/Wiki/Copilot/Notebook Studio metadata surfaces and no raw JSON/source/debug rendering.
- Run:
  ```powershell
  dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter "BackendLifeTests|PedagogicalReleaseClosureTests|TutorPedagogyPolicyTests|QuizLearningPipelineTests|AssessmentQualityMisconceptionTests|QuizAttemptSafetyTests|LearningSnapshotTests|ChatParityTests|PlanQualityTests|PlanQualitySequencingTests|AssessmentBlueprintTests|AgenticSecurityTrustTests|WikiGraphContractTests|LearningNotebookStudioTests|SourceEvidenceLifecycleTests" --no-restore --verbosity minimal
  dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter RegressionGateScriptTests --no-restore --verbosity minimal
  dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --no-restore --verbosity minimal
  dotnet test .\Orka.Infrastructure.UnitTests\Orka.Infrastructure.UnitTests.csproj --no-restore --verbosity minimal
  scripts\quick-coordination.ps1
  scripts\quick-backend.ps1
  cd Orka-Front; npm run typecheck; npm run quick:smoke; npm run quick:build
  git diff --check
  git status --short
  git diff --cached --name-only
  ```

## Pedagogical Productization Phase 2 Gate

Before closing Professional Lesson Delivery Rubric:

- Confirm `TutorActionPlanner` emits `TutorLessonDeliveryDto` after `TutorToolDecisionDto` without raw prompt/provider/tool/source/debug payloads, local paths, owner ids, or answer keys.
- Confirm low mastery prefers guided example or beginner-safe explanation, high mastery prefers checkpoint/extension, and unclear learner level keeps a diagnostic/checkpoint.
- Confirm confused learner, blank/skipped quiz impact, wrong answer, or misconception evidence produce safe remediation/telafi delivery (`prerequisite_repair` or `misconception_repair`) without fake diagnosis certainty.
- Confirm source-ready ask uses `source_grounded_explanation`; source-insufficient ask uses clarification or `model_assisted_explanation` with a source warning.
- Confirm lesson delivery metadata feeds Tutor prompt guidance, chat metadata/frontend chips, and Wiki trace block typing without adding AI/provider calls or migrating provider architecture.
- Run:
  ```powershell
  dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter "TutorPedagogyPolicyTests|QuizLearningPipelineTests|AssessmentQualityMisconceptionTests|QuizAttemptSafetyTests|LearningSnapshotTests|ChatParityTests|KorteksContractTests|UnifiedToolRuntimeTests|AgenticSecurityTrustTests|WikiGraphContractTests|LearningNotebookStudioTests|SourceEvidenceLifecycleTests" --no-restore --verbosity minimal
  dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter RegressionGateScriptTests --no-restore --verbosity minimal
  dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --no-restore --verbosity minimal
  dotnet test .\Orka.Infrastructure.UnitTests\Orka.Infrastructure.UnitTests.csproj --no-restore --verbosity minimal
  scripts\quick-coordination.ps1
  scripts\quick-backend.ps1
  cd Orka-Front; npm run typecheck; npm run quick:smoke; npm run quick:build
  git diff --check
  git status --short
  git diff --cached --name-only
  ```

## Pedagogical Productization Phase 1 Gate

Before closing Tutor tool-use orchestration polish:

- Confirm `TutorActionPlanner` emits `TutorToolDecisionDto` without raw prompt/provider/tool/source/debug payloads, local paths, owner ids, or answer keys.
- Confirm evidence-limited source intent blocks `source_grounded_answer` / `ask_source` and surfaces a safe clarification or model-assisted explanation decision.
- Confirm remediation/telafi signals from confused learner state, weak mastery, blank/skipped quiz impact, or misconception evidence prefer `start_remediation`.
- Confirm tool decisions remain deterministic and do not add new AI/provider calls or migrate provider architecture.
- Confirm Chat metadata/frontend render the selected action compactly without raw JSON or raw tool output.
- Run:
  ```powershell
  dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter "TutorPedagogyPolicyTests|QuizLearningPipelineTests|AssessmentQualityMisconceptionTests|QuizAttemptSafetyTests|LearningSnapshotTests|ChatParityTests|KorteksContractTests|UnifiedToolRuntimeTests|AgenticSecurityTrustTests|WikiGraphContractTests|LearningNotebookStudioTests|SourceEvidenceLifecycleTests" --no-restore --verbosity minimal
  dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter RegressionGateScriptTests --no-restore --verbosity minimal
  dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --no-restore --verbosity minimal
  dotnet test .\Orka.Infrastructure.UnitTests\Orka.Infrastructure.UnitTests.csproj --no-restore --verbosity minimal
  scripts\quick-coordination.ps1
  scripts\quick-backend.ps1
  cd Orka-Front; npm run typecheck; npm run quick:smoke; npm run quick:build
  git diff --check
  git status --short
  git diff --cached --name-only
  ```
## Phase 4 Remediation / Telafi Dersi Addendum
1. Yanlis cevap sonrasi public `learningImpact.remediationLesson` alaninda
   repair type, trigger, checkpoint ve mastery policy gorunmeli.
2. Bos/skip cevap `prerequisite_repair` veya guvenli prerequisite/confidence
   telafisi olmali; kesin yanilgi tani gibi sunulmamali.
3. Tutor/chat metadata telafi dersini compact label olarak gostermeli:
   raw JSON, prompt, provider payload, source chunk, owner id, answer key veya
   correct answer gorunmemeli.
4. Wiki repair trace bloklarinda telafi tipi, ozet ve checkpoint guvenli
   metin olarak yer almali; tekrar eden bloklar dedupe edilmeli.
5. Notebook Studio repair pack onerisi yalnizca mevcut repair state'ten
   gelmeli; her hatada otomatik artifact uretmemeli.
