# OrkaOS v1 - Dirty Worktree Commit/PR Ayrim Plani

Tarih: 2026-06-04  
Branch: `feature/vertex-ai`  
Repo: `https://github.com/ahmetakifsevgili/Orka-AI.git`  
PR durumu: Bu branch icin aktif PR bulunmadi.  
GitHub CLI: Auth hazir, `repo` ve `workflow` scope mevcut.

## 1. Ozet

Worktree cok buyuk ve tek commit/tek PR olarak gonderilmesi riskli. Su an gorunen durum:

```text
Tracked modified files: 160
Untracked files/directories: 38
Total dirty entries: 198
```

Diff hacmi yaklasik olarak:

```text
api        files=20   +1449  -91
core       files=25   +625   -23
docs-ci    files=7    +495   -13
frontend   files=22   +2513  -622
infra      files=60   +9626  -2300
tests      files=24   +2933  -332
other      files=1    +5     -0
```

Bu nedenle dogru strateji:

- Tek devasa commit atma.
- Once kapsamlari ayir.
- Her commit icin dogrudan ilgili test kanitini yaz.
- PR acilacaksa draft PR olarak ac.
- CI kirilirsa `gh-fix-ci` workflow'una gec.

## 2. Neden Tek Commit Riskli?

Tek commit su riskleri tasir:

- Auth, AI reliability, diagnostic, question bank, frontend UI, Notebook Studio, audio, migrations ve dokumantasyon ayni review icinde karisir.
- Revert gerekirse tum sistem geri alinmak zorunda kalir.
- CI hatasinin hangi katmandan geldigi zor anlasilir.
- Review yapan kisi degisiklik niyetini okuyamaz.
- Benden onceki degisikliklerle bu turdaki degisiklikler ayni dosyalarda karisabilir.

## 3. Onerilen Commit Dilimleri

### Commit 1 - Release documentation / OrkaOS v1 docs

Kapsam:

- `OrkaOSv1/`

Amac:

- Sistem arastirmasi
- Pazar konumlandirma
- Mimari dokumani
- Ozellik/model baglantilari
- UML/roadmap
- Dirty worktree ayrim plani

Neden ayri?

- Koddan bagimsiz.
- Review'u kolay.
- Gerekirse release notu veya product spec olarak tek basina tasinir.

Onerilen commit mesaji:

```text
docs: add OrkaOS v1 system and release scope documentation
```

Onerilen stage:

```powershell
git add OrkaOSv1
git commit -m "docs: add OrkaOS v1 system and release scope documentation"
```

### Commit 2 - Security/privacy and auth hardening

Kapsam:

- `Orka.Core/DTOs/Auth/AuthResponse.cs`
- `Orka.API/Controllers/AuthController.cs`
- `Orka.API/Controllers/TutorController.cs`
- `Orka.API/Services/TutorPublicTraceProjection.cs`
- ilgili auth/privacy testleri

Amac:

- Refresh token body'den kalkar.
- Refresh token HttpOnly cookie ile kalir.
- Public tutor trace raw state/payload sizdirmaz.

Onerilen commit mesaji:

```text
fix: harden auth token responses and public tutor trace projection
```

Dogulama:

```powershell
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --no-restore --filter "AuthTokenContractTests|RegressionGateScriptTests" -m:1 -v:minimal
```

### Commit 3 - AI reliability, diagnostic and DeepPlan quality gates

Kapsam:

- `Orka.Infrastructure/Services/AIAgentFactory.cs`
- `Orka.Infrastructure/Services/PlanDiagnosticService.cs`
- `Orka.Infrastructure/Services/DiagnosticQuizQualityGate.cs`
- `Orka.Infrastructure/Services/DeepPlanAgent.cs`
- `Orka.Infrastructure/Services/PlanSequencingService.cs`
- ilgili infrastructure/API tests

Amac:

- Strict AI rollerinde fake/in-memory fallback yok.
- Diagnostic question count heuristic net.
- Blueprint contract deterministic ama provider yerine gizlice kullanilmiyor.
- DeepPlan thin/generic output kaydetmiyor.
- No evidence -> diagnostic, weak evidence -> repair.

Onerilen commit mesaji:

```text
fix: enforce strict AI diagnostics and plan quality gates
```

Dogulama:

```powershell
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --no-restore --filter "OrkaV29QualityRealityGateTests|AiReliabilityTests|PlanQualityGuardTests|PedagogicalReleaseClosureTests|PlanQualitySequencingTests" -m:1 -v:minimal
dotnet test .\Orka.Infrastructure.UnitTests\Orka.Infrastructure.UnitTests.csproj --no-restore --filter "DeepPlanDiagnosticTraceabilityTests|PlanDiagnosticTests|DiagnosticQuizQualityGateTests|LearningArchitectureTests" -m:1 -v:minimal
```

### Commit 4 - Chat quiz evidence and learning signal durability

Kapsam:

- `Orka.API/Controllers/ChatController.cs`
- `Orka.Infrastructure/Services/QuizAttemptRecorder.cs`
- `Orka.Core/Interfaces/IRedisMemoryService.cs`
- ilgili quiz/learning tests

Amac:

- Chat icinde quiz cevabi geldiğinde attempt/signal post-processor'a bagli kalmadan kaydedilir.
- Observed evidence mastery'yi sisirmez.

Onerilen commit mesaji:

```text
fix: record observed chat quiz evidence synchronously
```

Dogulama:

```powershell
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --no-restore --filter "BackendCoordinationSmokeTests|QuizLearningPipelineTests|QuizAttemptSafetyTests" -m:1 -v:minimal
```

### Commit 5 - Notebook Studio Wiki/OrkaLM parity and audio context

Kapsam:

- `Orka.Infrastructure/Services/AudioOverviewService.cs`
- `Orka.API/Controllers/AudioController.cs`
- `Orka.API/Controllers/ClassroomController.cs`
- `Orka.Core/DTOs/NotebookLmDtos.cs`
- `Orka.Infrastructure/Services/LearningNotebookStudioService.cs`
- `Orka.Infrastructure/Services/NotebookExportService.cs`
- ilgili Notebook Studio tests

Amac:

- Wiki ve OrkaLM feature parity korunur.
- Upload sadece OrkaLM'de kalir.
- Audio context contract netlesir.
- Caption/transcript/study room akisi surface ayrimi ile calisir.

Onerilen commit mesaji:

```text
feat: complete Notebook Studio parity and isolated audio context
```

Dogulama:

```powershell
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --no-restore --filter "LearningNotebookStudioTests|SourceRegressionGuardTests|EndpointBridgeSmokeTests|OrkaStudyRoomTests" -m:1 -v:minimal
```

### Commit 6 - Frontend professional UI and browser evidence

Kapsam:

- `Orka-Front/src/components/WikiMainPanel.tsx`
- `Orka-Front/src/components/NotebookStudioPanel.tsx`
- `Orka-Front/src/components/ClassroomAudioPlayer.tsx`
- `Orka-Front/src/i18n/messages.ts`
- `Orka-Front/src/services/api.ts`
- `Orka-Front/e2e/`
- `Orka-Front/playwright.config.ts`

Amac:

- Wiki upload hidden.
- OrkaLM upload visible.
- Properties/graph, text artifacts, slide/UML preview, export preview browser'da kanitlanir.
- Audio `<track kind="captions">`, study room open ve ask payload dogrulanir.
- Playwright timeout 180s.

Onerilen commit mesaji:

```text
feat: add frontend Notebook Studio parity and audio e2e coverage
```

Dogulama:

```powershell
cd D:\Orka\Orka-Front
npm run typecheck
npm run smoke:ui
npm run smoke:contracts
npm run smoke:security
npm run build
$env:PLAYWRIGHT_PORT='3108'; npx playwright test e2e/notebook-studio-contract.spec.ts --reporter=list
```

### Commit 7 - Question bank, imports, curriculum and migrations

Kapsam:

- `Orka.Core/DTOs/QuestionBankDtos.cs`
- `Orka.Core/Entities/QuestionBankEntities.cs`
- `Orka.Infrastructure/Services/QuestionBankService.cs`
- `Orka.Infrastructure/Services/QuestionImportService.cs`
- `Orka.Infrastructure/Migrations/*`
- `Orka.API/Controllers/Question*`
- ilgili question bank/import tests

Amac:

- Soru bankasi ve import pipeline profesyonel hale gelir.
- Curriculum/outcome mapping ve rich content destekleri ayrilir.

Onerilen commit mesaji:

```text
feat: expand professional question bank and import pipeline
```

Dogulama:

```powershell
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --no-restore --filter "QuestionBankTests|QuestionImportTests|RichQuestionImportTests|ContentOperationsTests" -m:1 -v:minimal
```

### Commit 8 - CI, scripts and release automation

Kapsam:

- `.github/workflows/*`
- `scripts/*`
- `docs/audit/*`
- `life_tests/*`

Amac:

- Release ve audit komutlari standartlasir.
- Frontend/backend release gate CI'da daha takip edilebilir olur.

Onerilen commit mesaji:

```text
ci: align release gates and audit scripts
```

Dogulama:

```powershell
dotnet build .\Orka.API.Tests\Orka.API.Tests.csproj --no-restore -m:1
cd D:\Orka\Orka-Front
npm run smoke:contracts
```

## 4. Onerilen PR Stratejisi

En guvenli yol iki PR:

### PR A - OrkaOS v1 release documentation

Icerik:

- `OrkaOSv1/`

Neden:

- Hizli merge edilebilir.
- Kod riskinden bagimsiz.
- Product/mimari hafiza kalici olur.

### PR B - Orka professional closure implementation

Icerik:

- Security/privacy
- AI reliability
- Diagnostic/DeepPlan
- Notebook Studio parity
- Audio context
- Frontend/e2e
- Question bank/import pipeline
- CI/scripts

Neden:

- Release gate butunlugu korunur.
- Kodlar arasi baglanti cok oldugu icin fazla parcalanirsa PR'lar birbirine bagimli hale gelir.

Alternatif:

- Yukaridaki 8 commit tek draft PR icinde tutulur.
- Reviewer commit commit inceler.

## 5. Su An Neyi Yapmadim?

Bilerek yapilmayanlar:

- Devasa dirty worktree'yi tek commit yapmadim.
- User/onceden gelen degisiklikleri revert etmedim.
- PR acmadim, cunku branch icin PR yok ve once commit kapsamlari ayrilmali.
- CI log incelemesine girmedim, cunku ortada failing PR check bilgisi yok.

## 6. Final Release Gate Komutlari

Kod commitlerinden sonra son kapatma:

```powershell
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --no-restore -m:1 -v:minimal
dotnet test .\Orka.Infrastructure.UnitTests\Orka.Infrastructure.UnitTests.csproj --no-restore -m:1 -v:minimal

cd D:\Orka\Orka-Front
npm run typecheck
npm run smoke:ui
npm run smoke:contracts
npm run smoke:security
npm run build
$env:PLAYWRIGHT_PORT='3109'; npx playwright test --reporter=list
```

Son bilinen basarili durum:

```text
API full: 634/634
Infrastructure full: 176/176
Frontend typecheck/build/smoke: passed
Playwright full: 5 passed / 1 skipped
Notebook Studio contract: 2/2
```

## 7. PR Acma Komutlari

Commitler hazir olduktan sonra:

```powershell
git push -u origin feature/vertex-ai
gh pr create --draft --base main --head feature/vertex-ai --title "OrkaOS v1 professional closure" --body-file OrkaOSv1/05-Dirty-Worktree-Commit-PR-Ayrim-Plani.md
```

Not:

- Base branch `main` degilse `--base` degistirilmeli.
- CI fail olursa `gh-fix-ci` workflow'una gecilmeli.

## 8. CI Fail Olursa Kullanilacak GitHub Workflow

```powershell
gh pr checks <pr-number> --json name,state,bucket,link,startedAt,completedAt,workflow
gh run view <run-id> --log
```

veya skill script'i:

```powershell
python "C:\Users\ahmet\.codex\plugins\cache\openai-curated\github\2abb1c44\skills\gh-fix-ci\scripts\inspect_pr_checks.py" --repo "D:\Orka" --pr "<pr-number>"
```

## 9. Kisa Karar

Benim onerim:

1. Once `OrkaOSv1/` dokumantasyon commit'i.
2. Sonra implementation commitleri yukaridaki 7 teknik dilime bolunsun.
3. Hepsi tek draft PR'da toplanabilir.
4. CI fail ederse sadece fail eden check'e gore dar fix yapilsin.

