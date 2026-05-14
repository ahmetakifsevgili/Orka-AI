# Current Roadmap

## Current Phase

Central Exams pilot productization readiness

## Completed Phases

- V1 system-life
- Backend Coordination Pack A/B/C/D
- Coordination Backlog Cleanup
- System Closure Pack
- Production Safety Lite
- Mini blocker audit
- Codex Skills Anayasasi
- Stage 4 Small/Medium Feature Packs
  - Pack 1 - Learning Guidance Pack
  - Pack 2 - Coordination Visibility Pack
  - Pack 3 - Evidence Trust Pack
  - Pack 4 - Wiki Study Pack
- Stage 4 Small/Medium Feature Completion Audit
- Stage 5 - Production-ready enterprise hardening / scalability plan
- Stage 6B - Merkezi Sinavlar / Exam & Practice Content Engine
  - Pack 1 - Exam Framework Architecture
  - Pack 2 - Question Bank Core
  - Pack 3 - Structured Question Import Pipeline
  - Pack 4 - Central Exams Shell & KPSS Study Home MVP
  - Pack 5 - Practice Results -> Orka Learning Loop Integration
  - Pack 6 - Mini Deneme Engine MVP
  - Pack 7 - Multi-Exam Shell & Content Pack Expansion MVP
  - Pack 8 - Source-Grounded Question Draft Generation MVP
- Post-6B Professionalization
  - Pack A - Curriculum & Source Registry + Verification Gate
  - Pack A2 - Curriculum Graph Hardening
  - Pack B - Rich Question Model & Asset Infrastructure
  - Pack C - Import Pipeline v2: Rich Package + Standards Preview Adapters
  - Pack D - Content Operations Lite: Review, Publish Gate & Audit Trail
  - Pack E - KPSS Turkce Pilot UX + Original Pilot Content Flow
  - Pack F - Quality Analytics & Item Calibration

## Active Roadmap

1. System Closure Pack - complete
2. Production Safety Lite - complete
3. Mini blocker audit - complete
4. Codex Skills Anayasasi + small/medium features - complete
5. Production-ready enterprise hardening / scalability plan - complete
6. Stage 6B - Merkezi Sinavlar / Exam & Practice Content Engine - complete
7. Post-6B Professionalization - complete
8. Central Exams pilot productization readiness - current

## Stage 4 Closure Summary

- 10 discovery feature tamamlandi.
- 4 original pack tamamlandi:
  - Pack 1 - Learning Guidance Pack
  - Pack 2 - Coordination Visibility Pack
  - Pack 3 - Evidence Trust Pack
  - Pack 4 - Wiki Study Pack
- Mini fix gerekmedi.
- Validation gecti:
  - git status clean
  - npm run typecheck
  - npm run quick:smoke
  - RegressionGateScriptTests 5/5
  - git diff --check

## Stage 6B Closure Summary

- Closure doc: `docs/project-state/stage-6b-closure.md`
- Final audit: PASS
- Mini fix gerekmedi.
- Product outcome:
  - Central Exams Orka icinde entegre ogrenci-facing modul olarak tamamlandi.
  - KPSS calisan ilk sinav: study home, practice, persisted result/learning loop, mini-deneme.
  - YKS/LGS/YDS safe scaffold / coming-soon entry olarak eklendi.
  - Source-grounded question draft generation deterministic review-only seam olarak eklendi.
- Safety:
  - verified source metadata olmadan official curriculum claim yok.
  - official OSYM/MEB simulation claim yok.
  - success guarantee yok.
  - copyrighted/scraped content assumption yok.
  - Central Exams icinde PDF/OCR/NotebookLM dependency yok.
  - teacher/classroom/dershane workflow yok.
  - generated/imported content auto-publish edilmiyor.
- Validation gecti:
  - Pack 1-8 targeted backend tests
  - RegressionGateScriptTests
  - scripts/quick-coordination.ps1
  - scripts/quick-backend.ps1
  - Orka-Front npm run typecheck
  - Orka-Front npm run quick:smoke
  - git diff --check, sadece mevcut CRLF normalization warningleri

## Post-6B Professionalization Closure Summary

- Closure doc: `docs/project-state/post-6b-professionalization-closure.md`
- Final audit: PASS
- Mini fix: migration scope consistency duzeltildi; Content Ops tabloları `AddContentOperationsLite`, analytics tabloları `AddQuestionQualityAnalytics` icinde.
- Product outcome:
  - Curriculum/source registry ve official claim gate sertlestirildi.
  - Rich question, stimulus, asset ve accessibility modeli eklendi.
  - Rich package import preview/approval ve standards adapter seam eklendi.
  - Content Operations Lite review, publish readiness ve audit trail eklendi.
  - KPSS Turkce Paragraf pilot student flow Central Exams paneline baglandi.
  - Question quality analytics, item calibration ve coverage temeli eklendi.
- Safety:
  - verified metadata olmadan official curriculum claim yok.
  - official OSYM/MEB simulation claim yok.
  - success guarantee yok.
  - copyrighted/scraped content assumption yok.
  - PDF/OCR/NotebookLM dependency yok.
  - teacher/classroom/dershane workflow yok.
  - imported/generated content auto-publish edilmiyor.
  - score/net/ranking/percentile/placement yok.
- Validation gecti:
  - Post-6B A-F targeted backend tests
  - RegressionGateScriptTests
  - scripts/quick-coordination.ps1
  - scripts/quick-backend.ps1
  - Orka-Front npm run typecheck
  - Orka-Front npm run quick:smoke
  - git diff --check, sadece mevcut CRLF normalization warningleri

## Roadmap Rule

- Bu sira kullanici onayi olmadan degistirilmeyecek.
- Frontend Corporate Baseline bu backend roadmap'in resmi maddesi degildir.
- Central Exams pilot productization readiness current phase'tir.
- Yeni ara asama icat edilmeyecek.
- Stage 6C, global exam implementation veya teacher/institutional feature kullanici onayi olmadan baslatilmayacak.

## Codex Rule

- Yeni chat/branch basinda once bu dosya ve `docs/codex-skills/README.md` okunacak.
- Feature isi yapmadan once ilgili constitution dosyalari okunacak.
- Stage/commit kullanici acikca istemeden yapilmayacak.
- Post-6B Professionalization tamamlandi; siradaki isler Content Ops/Admin Lite UI,
  asset delivery hardening, KPSS pilot content, KPSS user-flow polish ve import
  standards hardening olarak ayrica onaylanmadan baslatilmayacak.
