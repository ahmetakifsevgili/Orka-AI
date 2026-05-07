# Three Lifetest Orka Flow

Date: 2026-05-07

Branch: `codex/deep-plan-quality-system-hardening`

Goal: prove that the fixed study flow is not locked to one hand-written topic and that Wiki remains active while OrkaLM becomes the Notebook/PDF source surface.

## Lifetest 1 - Java Algorithms And Data Structures

Input:

`java programlamada algoritmalar ve veri yapilari calismak istiyorum`

Expected:

- StudyIntentAnalyzer extracts `Java programlama`.
- Focus is `algoritmalar ve veri yapilari`.
- Korteks receives approved research intent, not the raw user sentence.
- Quiz count is between 15 and 25.
- Quiz stays on Java algorithms/data structures and does not leak C#, .NET or Visual Studio.
- Quiz result summary separates known and weak concepts.

Actual:

- Main topic: `Java programlama`
- Focus: `algoritmalar ve veri yapilari`
- Research intent: `Java programming algorithms and data structures learning path`
- Quiz count: 25
- Diagnostic summary includes `KnownConcepts`, `WeakConcepts`, `AccuracyPercent`, `MeasuredLevel`.

Status: PASS

## Lifetest 2 - KPSS Paragraph Speed

Input:

`KPSS paragraf sorularinda hizlanmak istiyorum`

Expected:

- StudyIntentAnalyzer keeps KPSS as the exam domain.
- Focus does not repeat the exam acronym unnecessarily.
- Research intent becomes English and researchable.
- Quiz count adapts to medium scope.

Actual:

- Main topic: `KPSS`
- Focus: `paragraf sorularinda hizlanmak`
- Research intent: `KPSS paragraph questions speed practice learning path`
- Quiz count: 20
- Diagnostic summary is generated without fake weak areas.

Status: PASS

## Lifetest 3 - SQL Indexes And Query Optimization

Input:

`SQL veritabani indeksleri ve sorgu optimizasyonu calismak istiyorum`

Expected:

- StudyIntentAnalyzer detects SQL as the programming/data domain.
- Focus removes duplicated `SQL`.
- Research intent is clean enough for Korteks.
- Quiz does not fall back to generic fake pipeline text.

Actual:

- Main topic: `SQL programlama`
- Focus: `veritabani indeksleri ve sorgu optimizasyonu`
- Research intent: `SQL programming database indexes and query optimization learning path`
- Quiz count: 22
- Diagnostic summary includes known/weak split.

Status: PASS

## Frontend Lifetest

Checks:

- `OrkaLM` exists in the left sidebar.
- `Wiki` remains active and unchanged as a learning map/wiki surface.
- `OrkaLM` reuses the same backend-backed PDF/TXT/MD source upload, source graph, evidence panel, briefing, glossary, mind map, recommendations and audio overview surface.
- Selecting another topic while inside Wiki/OrkaLM keeps the user in the same source surface instead of throwing them back to generic chat.

Status: PASS

## Validation

- `dotnet build --no-restore`: PASS, 0 warnings / 0 errors
- `dotnet test --no-build`: PASS, 89 infrastructure tests + 62 API tests
- `python -m pytest contract_tests/ -q`: PASS, 37 passed / 1 skipped
- `npm run typecheck`: PASS
- `npm run smoke:ui`: PASS
- `npm run smoke:contracts`: PASS
- `npm run build`: PASS
- Runtime backend `/health/live`, `/health/ready`, `/api/tools/capabilities`: 200
- Runtime frontend `/`, `/login`: 200

## Notes

- This is still not a full V3 adaptive exam engine.
- OrkaLM is a surfaced workspace for existing Notebook/PDF/source intelligence. Deeper NotebookLM-style optimization can move into V3.
- The three lifetests are now automated at backend test level so future regressions should break tests instead of surprising the user in the UI.
