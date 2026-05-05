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

## Worker / Cost / Provider Gate Hardening Addendum

Runtime smoke after worker/cost/provider hardening:

| Method/path | Auth | Expected | Actual | Result |
|---|---|---:|---:|---|
| GET `/swagger/index.html` | no | 200 | 200 | PASS |
| GET `/health/live` | no | 200 | 200 | PASS |
| GET `/health/ready` | no | 200 | 200 | PASS |
| GET `/api/korteks/ping` | yes | 401 without token | 401 | PASS |
| GET `/api/tools/capabilities` | no | 200 | 200 | PASS |
| GET `/api/tools/capabilities/wolfram_alpha` | no | 200 | 200 | PASS |
| GET `/api/tools/capabilities/ide_execution` | no | 200 | 200 | PASS |
| GET `/api/tools/capabilities/weather` | no | 200 | 200 | PASS |
| GET `/api/tools/capabilities/news` | no | 200 | 200 | PASS |
| GET `/api/tools/capabilities/crypto` | no | 200 | 200 | PASS |

New durable runtime tables:

- `ToolTelemetryEvents`
- `CostRecords`

Migration apply result: PASS (`20260505022104_AddRuntimeTelemetryAndCostRecords`).

Unit/focused proof:

- runtime telemetry records tool events and cost records.
- unknown model cost estimation returns a safe fallback.
- background queue rejects missing job types.

## Scheduled Workers + Push + Grounding Closure Addendum

Runtime smoke after scheduled worker/push/grounding closure:

| Method/path | Auth | Expected | Actual | Result |
|---|---|---:|---:|---|
| GET `/swagger/index.html` | no | 200 | 200 | PASS |
| GET `/health/live` | no | 200 | 200 | PASS |
| GET `/health/ready` | no | 200 | 200 | PASS |
| GET `/api/korteks/ping` | yes | 401 without token | 401 | PASS |
| GET `/api/tools/capabilities` | no | 200 | 200 | PASS |
| GET `/api/tools/capabilities/daily_challenge` | no | 200 | 200 | PASS |
| GET `/api/tools/capabilities/review_query` | no | 200 | 200 | PASS |
| GET `/api/tools/capabilities/bookmarks` | no | 200 | 200 | PASS |

New backend runtime closure:

- `SrsReminderWorker` and `DailyChallengeWorker` are registered as hosted services.
- Both workers are disabled by default unless explicit config enables them.
- Disabled worker startup is safe and records durable telemetry through `RuntimeTelemetryService`.
- Enabled service paths are bounded and duplicate-notification safe.
- `PushDeliveryService` returns safe disabled/provider-missing/invalid-token results and records `push_delivery` telemetry.
- EducatorCore citation quality signals are persisted and covered by deterministic tests:
  - source context without `[doc:sourceId:pN]` -> `SourceCitationMissing`
  - source context with valid doc citation -> no missing signal
  - YouTube pedagogy reference -> `TeachingMoveApplied`

Focused proof:

- `dotnet test --no-build` -> PASS, 46 passed.
- `python -m pytest contract_tests/ -q` -> PASS, 37 passed, 1 skipped.

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
