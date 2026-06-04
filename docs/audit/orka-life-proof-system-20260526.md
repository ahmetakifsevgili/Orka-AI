# Orka Life Proof System - 2026-05-26

## Goal

Build a repeatable "is the system alive?" proof that exercises Orka like a real learner, not just as isolated unit tests.

The current implementation adds a first enterprise-style life proof gate around the existing Orka test assets:

- real authenticated API user journeys
- SQL/Redis readiness and diagnostics snapshot
- public payload safety sweep
- cross-user isolation checks
- provider-free default behavior
- optional authenticated browser app-shell proof
- optional backend release proof

## New/Updated Entry Points

| Command | Purpose |
|---|---|
| `powershell -ExecutionPolicy Bypass -File scripts/life-proof.ps1` | Full local life proof orchestrator. Starts API, runs real-user API lifetest, frontend smoke + authenticated browser proof, then backend release proof. |
| `node scripts/real-user-lifetest.mjs --api-url=http://localhost:5065` | API-only real-user lifetest against a running API. |
| `npm run life:browser` from `Orka-Front` | Authenticated app-shell Playwright proof. Skips by default unless `ORKA_ENABLE_BROWSER_LIFEPROOF=true`. |

## What It Proves Now

| Area | Current proof |
|---|---|
| Auth/session | Register, login, `/api/user/me`, token usage. |
| Topic/session shell | Topic create/list/detail. |
| Learning OS | Dashboard, Orka state, Mission Control, Study Coach. |
| Review/quiz | Learning signals, quiz attempt answer-key guard, flashcard create/review. |
| Study Room | Start session, wrong checkpoint, blank checkpoint. |
| Source/Wiki/Notebook | Markdown source upload, source list/quality/lifecycle/evidence, Notebook pack and preview. |
| Exams | KPSS home/war room/practice/deneme safe empty-state handling. |
| Code IDE | Learning IDE contract, code execution redaction probe, IDE error learning signals. |
| Privacy/safety | Public payload marker sweep and cross-user access denial. |
| SQL/Redis | `/health/ready` plus dev diagnostics snapshot showing SQL provider/connectivity and Redis online/ping/endpoint count. |
| Browser shell | Authenticated `/app` render, Mission Control and core module surfaces visible when enabled by `life-proof.ps1`. |

## Known Limits

This is now a strong black-box life proof, but not yet a full disposable persistence harness.

Still worth adding later:

- C# side-effect lifetest against disposable SQL Server database.
- Redis key assertions by run-scoped `userId/topicId/sessionId`.
- Cleanup proof through topic/account delete plus bounded Redis scan.
- More robust `data-testid` hooks for app-shell navigation.
- Nightly/staging live provider smoke, opt-in only.

## Subagent Consensus

- Use `real-user-lifetest.mjs` as the API journey base.
- Keep live AI/provider calls out of PR gates.
- Use Playwright for logged-in shell proof, but build preview with `VITE_API_BASE_URL`.
- For real SQL/Redis persistence, do not infer only from HTTP success; add direct row/key assertions in a future persistence mode.

## Recommended Rollout

1. PR/local: `life-proof.ps1 -SkipBackendProof` for faster API + browser proof.
2. Release candidate: full `life-proof.ps1`.
3. Nightly: full life proof plus provider-backed optional checks and future persistence side-effect mode.
