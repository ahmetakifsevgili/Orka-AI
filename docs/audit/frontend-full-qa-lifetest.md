# Frontend Full QA Lifetest

Phase: Frontend QA + UX Polish + Contract Regression + Runtime Infrastructure Gate  
Branch: `feature/backend-production-hardening-dirty-orka-parity`  
Starting commit: `3dfba59 Integrate frontend with backend contract and polish UX`  
Backend URL: `http://localhost:5101`  
Frontend URL: `http://localhost:3000`

## Environment And Safety

| Check | Expected | Actual | Result |
|---|---|---|---|
| Git branch | `feature/backend-production-hardening-dirty-orka-parity` | matched | PASS |
| Initial worktree | clean before edits | clean | PASS |
| appsettings inspection | no secret values printed | keys/sections inspected with values redacted | PASS |
| frontend `.env*` | no committed/local frontend env required | none found in `Orka-Front` root | PASS |
| frontend direct provider calls | none | no direct GDELT/Open-Meteo/CoinGecko/Wolfram/NewsAPI/YouTube/Judge0/Piston URLs in source/scripts | PASS |
| stale backend port | no active `5065` reference | one stale API client comment fixed to `5101` | PASS |
| service account JSON | not committed | no tracked service-account/firebase/key/secret JSON matched guard | PASS |
| generated artifacts | not tracked/staged | `node_modules`, `Orka-Front/node_modules`, `Orka-Front/dist`, `Orka-Front/build` not tracked | PASS |
| package lock | classify changes | no `package-lock.json` change in this phase | PASS |

## Automated Validation

| Command | Expected | Actual | Result |
|---|---|---|---|
| `dotnet restore` | PASS | all projects up to date | PASS |
| `dotnet build` | PASS, 0 warnings/errors | 0 warnings, 0 errors | PASS |
| `dotnet test --no-build` | PASS | 61 passed | PASS |
| `python -m pytest contract_tests/ -q` before backend runtime | should require running backend | connection refused because backend was not yet listening | PASS_WITH_NOTE |
| `python -m pytest contract_tests/ -q` with backend runtime | PASS | 37 passed, 1 skipped, 2 mark warnings | PASS |
| `npm run build` | PASS | serial build passed | PASS |
| `npm run smoke:ui` | PASS | passed | PASS |
| `npm run smoke:contracts` | PASS | passed | PASS |
| `npm run lint` | if available | missing script | NOTE |
| `npm test` | if available | missing script | NOTE |

Note: an intentionally parallel frontend validation run completed Vite asset generation but exited with a Node/Vite `UV_HANDLE_CLOSING` assertion. The same `npm run build` command passed when run serially, so final validation uses serial frontend commands.

## Backend Runtime Smoke

| URL | Expected | Actual | Result |
|---|---:|---:|---|
| `/swagger/index.html` | 200 | 200 | PASS |
| `/health/live` | 200 | 200 | PASS |
| `/health/ready` | 200 | 200 | PASS |
| `/health` | 200 | 200 | PASS |
| `/api/dev/diagnostics/config` | 200 in Development | 200 | PASS |
| `/api/korteks/ping` unauthenticated | 401 | 401 | PASS |
| `/api/tools/capabilities` | 200 | 200 | PASS |
| `/api/tools/capabilities/ide_execution` | 200 | 200 | PASS |
| `/api/tools/capabilities/news` | 200 | 200 | PASS |
| `/api/tools/capabilities/weather` | 200 | 200 | PASS |
| `/api/tools/capabilities/crypto` | 200 | 200 | PASS |
| `/api/tools/capabilities/wolfram_alpha` | 200 | 200 | PASS |
| `/api/tools/capabilities/youtube_pedagogy` | 200 | 200 | PASS |
| `/api/tools/capabilities/sources_query` | 200 | 200 | PASS |
| `/api/tools/capabilities/review_query` | 200 | 200 | PASS |
| `/api/tools/capabilities/flashcards` | 200 | 200 | PASS |
| `/api/tools/capabilities/daily_challenge` | 200 | 200 | PASS |
| `/api/tools/capabilities/bookmarks` | 200 | 200 | PASS |
| `/api/tools/capabilities/mermaid` | 200 | 200 | PASS |

## SQL / DB Connectivity Proof

| Proof | Expected | Actual | Result |
|---|---|---|---|
| Health check | SQL healthy | `/health` reported `sql-server Healthy` | PASS |
| Diagnostics | DB can connect | `databaseCanConnect=True`, provider `Microsoft.EntityFrameworkCore.SqlServer` | PASS |
| Register unique user | DB write | 201 | PASS |
| Login unique user | DB auth read/session path | 200 with token captured in memory only | PASS |
| Topic create | DB write | 200, returned id and `planIntent` field | PASS |
| Topic list | DB read | 200, created topic visible | PASS |
| Daily challenge read | protected learning read | 200 | PASS |
| Bookmark create/list/delete | learning feature write/read/delete | 200 / 200 / 200 | PASS |
| Flashcard create/delete | learning feature write/delete | 200 / 200 | PASS |

## Redis Connectivity / Fallback Proof

| Proof | Expected | Actual | Result |
|---|---|---|---|
| Health check | Redis healthy or documented fallback | `/health` reported `redis Healthy` | PASS |
| Diagnostics | Redis status visible without secrets | `redisStatus=online` | PASS |
| Worker startup | no Redis/provider crash | SRS and DailyChallenge hosted workers logged disabled by configuration | PASS |
| Redis-backed contract tests | fallback-safe behavior remains passing | contract tests passed with backend runtime | PASS |

## Auth / Session Lifecycle

| Step | Expected | Actual | Result |
|---|---|---:|---|
| Register unique email | 200/201 | 201 | PASS |
| Login unique email | 200 + token | 200 | PASS |
| Wrong password | 401 | 401 | PASS |
| Protected topics without token | 401 | 401 | PASS |
| Protected topics with token | 200 | 200 | PASS |
| Tool capabilities anonymous | 200 | 200 | PASS |

The JWT was captured only in process memory for request headers and was not printed.

## Frontend Runtime Smoke

| URL | Expected | Actual | Result |
|---|---:|---:|---|
| `http://127.0.0.1:3000/` | 200 | 200 | PASS |
| `http://127.0.0.1:3000/login` | 200 | 200 | PASS |

## Chat / Tutor Contract UX

| Area | Expected | Actual | Result |
|---|---|---|---|
| Streaming path | preserved | existing stream UI and `ChatAPI.streamMessage` route preserved | PASS |
| Metadata chips | render only when backend metadata exists | `ChatMetadataChips` additive rendering remains present | PASS |
| Tool/citation/fallback inference | no prose inference | UI does not infer provider state from message prose | PASS |
| SSE final metadata | documented limitation | backend SSE still does not emit final metadata event | PASS_WITH_NOTE |

## IDE / Piston UX

| Area | Expected | Actual | Result |
|---|---|---|---|
| Execution path | backend only | `CodeAPI.run` posts to backend `/api/code/run`; no client execution found | PASS |
| Capability gate | driven by backend | IDE nav uses `ide_execution` capability visibility | PASS |
| Phase rendering | compile/runtime/timeout/blocked/provider_missing visible | phase-aware UI remains wired | PASS |
| Learning handoff | safe summary and output can be sent to Tutor | IDE payload includes phase/runtime/duration/errors/safe summary | PASS |

## Source / Wiki / Learning UX

| Area | Expected | Actual | Result |
|---|---|---|---|
| Sources | upload/list/ask/delete through backend | API client exposes upload/list/ask/page/update/delete; active source delete UI present | PASS |
| Citations | no fake citations | source/wiki UI displays backend citations/page evidence only | PASS |
| Learning panel | durable backend surfaces | Flashcards, Review/SRS, DailyChallenge, Bookmarks wired | PASS |
| Empty/error states | user-friendly enough without redesign | smoke checks and component inspection pass | PASS_WITH_NOTE |

## Provider Tool UX

| Tool | Expected | Actual | Result |
|---|---|---|---|
| News | backend-sourced status, GDELT label when applicable | capability strip labels from backend notes | PASS |
| Weather | backend-sourced status, Open-Meteo label when applicable | capability strip labels from backend notes | PASS |
| Crypto | educational market data, no direct advice UI | no buy/sell/hold UI added | PASS |
| Wolfram | gated unless AppId-backed | capability strip shows AppId/gated status from backend | PASS |
| YouTube | pedagogy, not factual source | capability strip labels YouTube separately; no factual citation UI added | PASS |
| Mermaid | safe text/diagram capability | Mermaid remains lazy-loaded and capability-visible | PASS |
| Visual generation | beta/gated | capability-visible only | PASS |

## Responsive / Accessibility Notes

| Area | Expected | Actual | Result |
|---|---|---|---|
| Existing identity | preserved | no shell/theme rewrite | PASS |
| Touched icon buttons | labelled where added | source delete button has title and aria label | PASS |
| Metadata/tool chips | wrap rather than raw JSON | chip UI used, raw metadata hidden by default | PASS |
| Remaining responsive QA | deeper browser viewport pass | not expanded into a redesign pass | NOTE |

## Issues Found / Fixed

| Issue | Fix | Result |
|---|---|---|
| Stale `localhost:5065` comment in frontend API client | updated comment to `localhost:5101` | FIXED |
| Python contract tests fail when backend is not running | reran after backend runtime start; passed | FIXED_BY_PROCEDURE |
| Parallel frontend build/smoke produced transient Node/Vite assertion after build output | reran build serially; passed | FIXED_BY_PROCEDURE |

## Artifact / Secret Guard

| Guard | Result |
|---|---|
| `node_modules` tracked | NO |
| `Orka-Front/dist` tracked | NO |
| `Orka-Front/build` tracked | NO |
| `.env.local` staged | NO |
| service account JSON staged | NO |
| provider keys/JWTs staged | NO |
| runtime logs staged | NO |

## Main Merge Readiness

Commands run:

```powershell
git diff --stat main...HEAD
git log --oneline main..HEAD
git diff --name-status main...HEAD
```

Classification:

| Category | Status |
|---|---|
| Backend changes | PRESENT: accepted backend hardening, contracts, tests, workers, telemetry, provider tools, migrations |
| Frontend changes | PRESENT: frontend backend integration, capability UI, IDE UX, learning panel |
| Docs | PRESENT: API inventory, contract, lifecycle, QA/audit docs |
| Migrations | PRESENT: EF migrations for durable learning, bookmarks/push, runtime telemetry/cost |
| Config/secrets | NO appsettings/secret changes detected in branch delta |
| Generated files | PRESENT: EF migration designer/model snapshot files; no frontend build artifacts tracked |
| package-lock changes | NONE in branch delta |

Readiness:

- Ready to push branch: YES, after final validation commit.
- Ready to open PR to `main`: YES, with the branch-size note that it contains the full accepted backend + frontend integration history.
- Ready after PR merge to rename old `D:\Orka` to `D:\Orka-legacy-dirty`: YES, after PR review/merge, because runtime/backend/frontend QA gates are green.
- Blocker before making clean `D:\Orka-main-validation` the main local repo: NO true blocker found.

## Remaining Notes

- Frontend has no `lint` or `test` npm scripts.
- Streaming chat UI is metadata-ready, but backend SSE does not emit final metadata events yet; frontend intentionally does not infer metadata from prose.
- Public provider fallbacks may have external public rate limits.
- Wolfram remains AppId-gated.
- `visual_generation` remains beta/gated.
