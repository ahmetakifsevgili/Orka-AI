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
- Destructive migration (`DROP COLUMN`, `DROP TABLE`, geri alinamaz data rewrite) varsa DB backup/snapshot zorunludur.
- Rollback destructive migrationlarda sadece kod revert degildir; DB restore veya explicit rollback script gerekir.
- Migration apply sonrasi `/health/ready` kontrol edilir; `ef-migrations` check'i pending migration birakmamalidir.

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
- Staging/Production `SecurityHeaders` HSTS, nosniff, Referrer-Policy, Permissions-Policy, X-Frame-Options ve enforced CSP set eder.
- AI budget guard provider cagrisindan once user/global daily limitleri uygular; quota exceeded response user-safe kalir.
- Full httpOnly cookie migration, Redis scale mimarisi ve enterprise SLO/observability bu gate'in kapsaminda degildir.

## Codex Skills Gate

Feature isleri `docs/project-state/current-roadmap.md` ve `docs/codex-skills/`
anayasalarini takip eder. Stage 4 small/medium feature packs kapandi; current
phase Stage 5 Production-ready enterprise hardening / scalability plan'dir.

Before planning/coding:

- `docs/project-state/current-roadmap.md` okunur.
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
