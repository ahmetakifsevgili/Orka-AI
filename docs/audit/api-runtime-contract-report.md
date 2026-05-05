# API Runtime Contract Report

Runtime target: `http://127.0.0.1:5101`  
Phase: Backend Production Hardening + Full Dirty-Orka Feature Optimization

## Baseline Smoke

| Method/path | Auth | Expected | Actual | Result | Notes |
|---|---|---:|---:|---|---|
| GET `/swagger/index.html` | no | 200 | 200 | PASS | Swagger UI served. |
| GET `/health/live` | no | 200 | 200 | PASS | App booted. |
| GET `/health/ready` | no | 200 | 200 | PASS | Ready checks responded. |
| GET `/api/korteks/ping` | yes | 401 without token | 401 | PASS | Auth guard intact. |

## New Hardening Contract

| Method/path | Auth | Expected | Actual proof | Result | Body contract |
|---|---|---:|---|---|---|
| GET `/api/tools/capabilities` | yes | 200 | contract test | PASS | `{ tools, count, includeInternal, contract: "tool_capability_v1" }` |
| GET `/api/tools/capabilities/{toolId}` | yes | 200/404 | contract test with `wolfram_alpha` | PASS | `ToolCapabilityDto` |

Manual proof also returned:

- `/api/tools/capabilities` -> 200
- `contract` -> `tool_capability_v1`
- `count` -> 18
- `wolfram_alpha`, `ide_execution`, `weather`, `news`, `crypto` -> `Disabled / DISABLED_WITH_RUNTIME_STUB`
- `sources_query`, `bookmarks` -> `Enabled / INTEGRATED_AND_TESTED`

## Contract Decisions

- Cross-user and auth behavior remains guarded by accepted contract tests.
- Provider-heavy runtime calls were not spammed.
- Missing provider keys are represented as disabled/degraded capability rows.
- No frontend endpoints were added that execute unsafe dirty-Orka tools for normal users.

## Current Deterministic API Coverage

`python -m pytest contract_tests/ -q`:

- PASS: `37 passed`
- SKIPPED: `1 lifecycle AI/provider-heavy scenario`
- Warnings: two pytest mark warnings from existing lifecycle markers.
