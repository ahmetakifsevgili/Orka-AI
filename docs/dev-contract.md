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

## What To Run When

- Auth/security/request-boundary/migration/logging change: `scripts\quick-backend.ps1`
- Frontend static contract or build-impacting change: `scripts\quick-all.ps1`
- Runtime API process verification: `healthcheck.mjs --quick`
- External HTTP contract verification against a running API: `pytest contract_tests/`
- Provider-heavy or AI-quality work: keep out of the deterministic baseline unless explicitly gated.
- Additive migration work: generate an idempotent script and review it under `docs/deployment/migration-policy.md`.

## Stage 4 Codex Skills Workflow

Small and medium feature work must follow the current roadmap in
`docs/project-state/current-roadmap.md` and the constitutions in
`docs/codex-skills/`.

Default flow:

1. Read `docs/project-state/current-roadmap.md`.
2. Read `docs/codex-skills/README.md`.
3. Read the applicable constitution files before planning:
   - backend/API/data: `backend-feature-constitution.md`
   - AI/RAG/Wiki/Chat/Korteks/source/citation: `ai-rag-feature-constitution.md`
   - frontend API/types/stream/UI contract: `frontend-contract-constitution.md`
   - persistent data/cache/session/delete/privacy: `data-lifecycle-constitution.md`
   - every feature: `testing-gate-constitution.md`
4. Use `feature-prompt-template.md` for new feature prompts.
5. Use `feature-completion-report-template.md` for final reports.
6. Do not stage or commit unless explicitly requested.
