# Orka Dev Contract

This document is the canonical local development and regression contract.

## Canonical URLs

- Backend API: `http://localhost:5065`
- Frontend dev server: `http://localhost:3000`
- Runtime/API smoke env var: `ORKA_API_URL`
- Frontend proxy env var: `VITE_API_PROXY_TARGET`

`5101` is a legacy audit/runtime port only. Do not use it as an active default
in scripts, contract tests, or new docs.

## Start Commands

```powershell
# Backend, SQL/local config
powershell -ExecutionPolicy Bypass -File scripts\start-api.ps1

# Backend, isolated in-memory smoke mode
powershell -ExecutionPolicy Bypass -File scripts\start-api.ps1 -InMemoryDatabase

# Frontend
powershell -ExecutionPolicy Bypass -File scripts\start-front.ps1
```

The Vite dev server uses port `3000` with `strictPort=true`; if the port is
busy, startup should fail visibly instead of silently moving to another port.

## Regression Baseline

Use this before and after stabilization or backend contract changes:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\quick-backend.ps1
```

The backend quick baseline covers auth token hardening, public security,
request-boundary guards, migration policy, logging/error leakage hardening,
health/swagger smoke, endpoint bridge smoke, source guards, runtime telemetry,
tool capability contracts, and auth-filtered tests.

Production logging must use masked references for learner/user/topic/session/
message/source/cache identifiers. Use `LogPrivacyGuard.SafeId` or
`LogPrivacyGuard.SafeTextRef` in backend logs instead of raw GUIDs, Redis keys,
local paths, prompt text, provider bodies, source chunks, tool payloads, answer
keys, owner ids, unsafe user ids, or exception stack traces.

The same quick baseline starts with the provider-free backend lifetest release
proof (`BackendLifeTests|PedagogicalReleaseClosureTests`). Test-host logging is
filtered only for known noisy categories; backend warnings/errors must remain
visible.

System Closure gates before frontend baseline:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\quick-coordination.ps1
powershell -ExecutionPolicy Bypass -File scripts\quick-backend.ps1
cd Orka-Front
npm run typecheck
npm run quick:smoke
cd ..
git diff --check
```

The deterministic quick line must remain external-network-free. Real provider,
Wikipedia/Wikidata, or other public HTTP checks stay behind explicit opt-in
environment flags and outside `quick-backend.ps1` / `quick-coordination.ps1`.

`DataLifecycleTests` in this baseline require relational SQL Server coverage.
Use `(localdb)\OrkaLocalDB` locally or set
`ORKA_LIFECYCLE_SQLSERVER_BASE_CONNECTION` in CI. These tests should fail
visibly when SQL Server is not provisioned; do not silently skip them.

## GitHub CI Backend Gate

`.github/workflows/backend-release.yml` mirrors the backend release proof for
GitHub Actions. It runs on `windows-latest`, prepares SQL Server LocalDB, runs
`scripts\quick-backend.ps1`, runs `Orka.Infrastructure.UnitTests`, and finishes
with `git diff --check`.

The CI gate must stay provider-free. Do not add real provider secrets,
`ORKA_RUN_EXTERNAL_PROVIDER_TESTS`, or paid provider smoke checks to this
workflow without an explicit separate release decision.

## Backend Production Readiness

Backend production-readiness work keeps two proof lines separate:

- Deterministic release proof: `scripts\quick-backend.ps1`,
  `scripts\quick-coordination.ps1`, full API tests, Infrastructure unit tests,
  and GitHub backend release CI. This line is provider-free and must not need
  live AI keys.
- Optional live/staging proof: explicit provider smoke only after approval,
  with harmless synthetic input, no secret printing, no source chunks, and no
  load testing against paid providers.

Production/staging startup must keep protected gates enabled: explicit DB and
Redis configuration, explicit CORS/AllowedHosts, secure refresh-cookie settings,
Redis auth rate limiting, applied-migration readiness, and global/user AI cost
or token limits. Audio retention and Redis stream maintenance must stay bounded
and aggregate-based; do not reintroduce full audio payload scans in readiness
summaries.

Use this when frontend dependencies are available and you want the combined
local smoke line:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\quick-all.ps1
```

## Runtime Smoke

Run these only when the API is already running on the canonical backend URL:

```powershell
node scripts/healthcheck.mjs --base-url=http://localhost:5065 --quick

$env:ORKA_API_URL="http://localhost:5065"
pytest contract_tests/
```

`healthcheck.mjs` also reads `ORKA_API_URL` when `--base-url` is omitted.

## Optional External Provider Smoke

Real provider checks are opt-in and must not be part of the deterministic
quick baseline:

```powershell
$env:ORKA_RUN_EXTERNAL_PROVIDER_TESTS="true"
$env:ORKA_EXTERNAL_GITHUB_MODELS_TOKEN="<token>"
dotnet test Orka.API.Tests\Orka.API.Tests.csproj --filter ExternalProviderIntegrationTests --no-restore --verbosity minimal
```

If the gate or token is missing, the tests write an explicit skip reason and
return without calling an external provider.

Provider staging proof rules:

- Do not print provider secrets or `dotnet user-secrets list` values. Report
  configured true/false only.
- A real completion/embedding success smoke requires an explicit token and an
  explicit call plan. Without a token, mark success proof blocked rather than
  pretending it passed.
- Invalid-token failure checks may prove safe provider failure behavior because
  they send only synthetic text and do not require a paid credential.
- Keyless public providers may be checked manually for reachability, but those
  checks stay out of deterministic quick scripts and CI.

## What To Run When

- Auth/security/request-boundary/migration/logging change: `scripts\quick-backend.ps1`
- Frontend static contract or build-impacting change: `scripts\quick-all.ps1`
- Runtime API process verification: `healthcheck.mjs --quick`
- External HTTP contract verification against a running API: `pytest contract_tests/`
- Provider-heavy or AI-quality work: keep out of the deterministic baseline unless explicitly gated.
- Additive migration work: generate an idempotent script and review it under `docs/deployment/migration-policy.md`.

## Codex Skills Feature Workflow

Feature work must follow the current roadmap in
`docs/project-state/current-roadmap.md` and the constitutions in
`docs/codex-skills/`. Stage 6B Central Exams and Post-6B Professionalization are
closed. The current phase is Main Learning OS Professionalization; Tutor,
Korteks, RAG/Wiki, Quiz, Plan, Tool, Memory, Telemetry, and Frontend work must
also read `docs/architecture/orka-learning-os-contract-map.md`.

Central Exams is an integrated Orka module, not a standalone KPSS app. It must
reuse Orka's exam framework, question bank, import pipeline, practice,
mini-deneme, learning signal, memory/planner/tutor, and wiki-study context
architecture. Do not add teacher/classroom/dershane workflows, official exam
claims without verified metadata, success guarantees, scraped content
assumptions, or auto-published generated/imported content.

Main Learning OS guard:

- Tutor is the pedagogical owner.
- Korteks researches; `KorteksResearchWorkflow` / synthesis contracts decide
  bounded educational use for plan, quiz, Tutor, and Wiki consumers.
- Semantic Kernel may bridge LLM plugins/tools, but Orka tool runtime owns
  policy, ledger, user/session/topic/correlation, fallback, and telemetry.
- RAG/Wiki must distinguish sourced, wiki-backed, degraded, and model-fallback
  claims.
- Quiz must measure concepts/misconceptions, not Orka UI/product labels.

Default flow:

1. Read `docs/project-state/current-roadmap.md`.
2. Read `docs/architecture/orka-learning-os-contract-map.md` for Tutor/Korteks/RAG/Wiki/Quiz/Tool/Plan work.
3. Read `docs/codex-skills/README.md`.
4. Read the applicable constitution files before planning:
   - backend/API/data: `backend-feature-constitution.md`
   - AI/RAG/Wiki/Chat/Korteks/source/citation: `ai-rag-feature-constitution.md`
   - frontend API/types/stream/UI contract: `frontend-contract-constitution.md`
   - persistent data/cache/session/delete/privacy: `data-lifecycle-constitution.md`
   - every feature: `testing-gate-constitution.md`
5. Use `feature-prompt-template.md` for new feature prompts.
6. Use `feature-completion-report-template.md` for final reports.
7. Do not stage or commit unless explicitly requested.
