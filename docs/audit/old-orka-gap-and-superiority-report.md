# Old Orka Gap And Superiority Report

Phase: Investor-grade system optimization and due-diligence gate  
Active repo: `D:\Orka`  
Read-only references: `D:\Orka-main-validation`, `D:\Orka-legacy-dirty-20260506-015255`, `D:\Orka-closure-proof`

## Executive Summary

The clean `D:\Orka` main branch already contains the core product capabilities previously found in dirty/reference Orka: Semantic Kernel plugin runtime, Tutor tool consumption, Piston/IDE pedagogy, provider tools, mistake classification, YouTube transcript pedagogy proof, source/RAG/wiki grounding, SRS/review, flashcards, daily challenge, bookmarks, push fallback, telemetry/cost records, and frontend contract integration.

The old dirty folder mainly contains local development artifacts that should not be reintroduced: `.claude` skill packs, TestSprite output, local logs, local DB backups, scratch SQL, and stale runtime evidence. No missing core backend capability was confirmed in this pass.

## Comparison Matrix

| Item | Reference evidence | Clean Orka status | Classification | Decision |
|---|---|---|---|---|
| Semantic Kernel Tutor plugin runtime | dirty/reference SK plugin files | `Program.cs` registers learning, source, provider, visual and pedagogy plugins | `CORE_ALREADY_PRESENT` | Keep clean implementation |
| Piston / IDE code learning loop | dirty roadmap and architecture docs | `PistonService`, `IdeExecutionPlugin`, `InteractiveIDE`, code learning signal tests | `BETTER_THAN_OLD_IN_CLEAN` | Keep backend-only sandbox path |
| Wolfram | dirty roadmap, SK plugin | Provider adapter and plugin are AppId-gated; LLM API endpoint documented | `BETTER_THAN_OLD_IN_CLEAN` | Live proof requires AppId provisioning |
| News/weather/crypto | dirty roadmap | GDELT, Open-Meteo, CoinGecko public fallbacks plus backend capability contract | `BETTER_THAN_OLD_IN_CLEAN` | Keep provider-gated backend calls |
| YouTube transcript pedagogy | dirty architecture/roadmap | Transcript provider abstraction, chunking/retrieval proof, teaching reference signal, YouTube Data API metadata/search proof | `BETTER_THAN_OLD_IN_CLEAN` | Transcript availability remains expected-degraded when public transcript is unavailable |
| SRS / review | dirty roadmap | Durable ReviewItem, SRS service, due/complete endpoints, worker gate | `BETTER_THAN_OLD_IN_CLEAN` | Keep gated workers |
| Flashcards | dirty roadmap/UI notes | Durable Flashcards API/service/frontend learning panel | `BETTER_THAN_OLD_IN_CLEAN` | Keep |
| Daily Challenge | dirty roadmap | Durable challenge/submission/idempotent XP path | `BETTER_THAN_OLD_IN_CLEAN` | Keep |
| Bookmarks | dirty roadmap | Durable bookmarks API/service/plugin/frontend panel | `BETTER_THAN_OLD_IN_CLEAN` | Keep |
| Push/Firebase | dirty roadmap | Push subscription and safe disabled/provider-missing delivery behavior | `CORE_ALREADY_PRESENT` | Provision Firebase later |
| Dashboard analytics | dirty v1.1 roadmap | Dashboard stats, system HUD, learning bridge surfaces | `CORE_ALREADY_PRESENT` | Keep; richer investor dashboard is product roadmap |
| `.claude` skills/rules | dirty backup only | not product runtime | `NEEDS_NOTE_ONLY` | Do not port |
| TestSprite outputs | dirty backup only | explicitly excluded from current QA gate | `DUPLICATE_OR_OBSOLETE` | Do not port |
| local logs / DB backups / scratch SQL | dirty backup only | not product code; tracked runtime evidence removed this phase | `UNSAFE_NOT_PORTED` | Keep out of repo |
| v3 platform items: teacher dashboard, collaboration, multi-tenant, 3D, plagiarism | dirty `V1_TO_V3_PLAN.md` | not implemented as core backend | `PRODUCT_LAYER_ROADMAP` | Document as roadmap, not blocker |

## What Clean Orka Does Better

- Uses a frozen backend/frontend contract with capability-driven tool visibility.
- Separates factual grounding from pedagogy references.
- Persists durable learning signals, review pressure, tool telemetry and cost records.
- Keeps risky providers behind backend gates and safe fallbacks.
- Uses public fallbacks for news/weather/crypto without frontend direct provider calls.
- Treats IDE execution as sandboxed backend pedagogy, not client-side execution.
- Includes contract tests, .NET tests, frontend smoke tests, and runtime evidence docs.

## Remaining Notes

- Wolfram live provider proof requires AppId.
- YouTube Data API metadata/search proof is complete with local user-secrets/config; transcript proof remains separate because YouTube Data API does not guarantee public transcript access.
- Redis vector activation for transcript search is a production/beta enhancement; deterministic chunk retrieval proof exists.
- Institution/teacher/multi-tenant surfaces are not part of the current Orka product direction. Personal study coaching, motivation/routine support, adaptive practice, 3D/classroom experience and KPSS-style algorithms remain product-layer roadmap items, not missing core readiness blockers.
