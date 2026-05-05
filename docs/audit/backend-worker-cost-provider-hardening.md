# Backend Worker + Cost + Provider Gate Hardening

Phase branch: `feature/backend-production-hardening-dirty-orka-parity`  
Previous accepted commit: `6fd3650 Harden dirty Orka backend tool parity`

## Baseline

| Check | Result |
|---|---|
| Worktree at start | clean |
| Branch | `feature/backend-production-hardening-dirty-orka-parity` |
| `dotnet restore` | PASS |
| `dotnet build` | PASS; one pre-existing `IOpenRouterService` hiding warning during initial baseline |
| `dotnet test --no-build` baseline | PASS, 36 passed |
| `python -m pytest contract_tests/ -q` baseline | PASS, 37 passed, 1 skipped |

## Worker / Job Inventory

| Job / worker | Trigger | Enabled/config | Dependencies | Retry/timeout/cancel | Telemetry/failure mode | Test/runtime status | Decision |
|---|---|---|---|---|---|---|---|
| `BackgroundTaskQueue` | services enqueue `BackgroundTaskItem` | hosted service always registered | in-process channel | bounded queue, `MaxAttempts`, timeout, cancellation token, linear retry delay | logs start/success/failure/timeout, one job failure does not kill loop | unit test rejects missing job type; runtime logs seen in dotnet tests | enabled and tested |
| Classroom interaction TTS | `ClassroomService` background enqueue | enabled with script/audio fallback | `IEdgeTtsService`, DB | queue timeout/retry wrapper | logs via queue, TTS failure degrades | dotnet tests exercise queue path | enabled and tested |
| Audio overview generation | `AudioOverviewService` | accepted script-only fallback | AI/TTS optional | service-scoped fallback | status contract `ready/script-only/failed` | existing contract/lifecycle tests | enabled and tested |
| Wiki/Summarizer background generation | session end / analyzer completion | provider-dependent | Summarizer/Wiki services | queue-wrapped in orchestrator paths | failure logs, user response not blocked | accepted lifecycle, provider fallback classified | gated and tested |
| Agent evaluator/analyzer loop | orchestrator background task | enabled | evaluator/analyzer/summarizer | queue `MaxAttempts=1`, timeout inherited | saves evaluation when possible, catches failures | dotnet tests exercise background path | enabled and tested |
| Topic auto-naming | orchestrator/topic service path | enabled | AI/provider optional | service fallback | no hard crash accepted | lifecycle accepted | enabled and tested |
| DailyChallenge scheduled worker | dirty Orka capability | not scheduled in target | DailyChallengeService, notification/push | not implemented as scheduled worker | lazy `GetOrCreateTodayAsync` is stable | contract tests cover today/submit/idempotency | deferred with documented acceptance note |
| SRS reminder worker | dirty Orka capability | not scheduled in target | ReviewItems, Notifications | not implemented as scheduled worker | due review API is stable | review contract tests | deferred with documented acceptance note |
| Push/Firebase delivery worker | dirty Orka capability | Firebase optional/not configured | PushSubscriptions, Notifications | no live push worker enabled | in-app first contract, push subscription CRUD | contract tests | disabled/gated with safe backend state |
| Provider metric background job | dirty Orka/LLMOps capability | replaced by runtime telemetry/cost records | DB | telemetry write is fail-safe | durable event/cost rows | unit tests | integrated and tested |

## Background Queue Hardening

Current queue behavior:

- job type is required; missing job type throws before enqueue.
- queue capacity is bounded at 256.
- job start/success/failure/timeout are logged with job type, attempt, user id and correlation id when provided.
- each item has `MaxAttempts` and optional timeout; default timeout is 60 seconds.
- retry delay is bounded and not a tight loop.
- cancellation is linked into each job.
- failure in one job is caught and does not terminate the hosted worker loop.

New coverage:

- `RuntimeTelemetryHardeningTests.BackgroundQueue_RejectsMissingJobType`

Remaining:

- deeper concurrent ordering/load tests are production hardening, not a frontend blocker.

## DailyChallenge / SRS / Notification Hardening

Current accepted behavior:

- Daily challenge has durable table-backed state and idempotent submit (`xpAwarded`/duplicate behavior).
- Review/SRS has durable `ReviewItem`, due endpoint and complete/update.
- Notification rows are in-app first and do not depend on Firebase.
- Push subscriptions are user-owned and tested.

Closure update:

- Scheduled DailyChallenge and SRS reminder workers are now registered as hosted services and are **ACCEPTED_WITH_GATE**.
- Both workers default to disabled without appsettings changes.
- Enabled service paths use bounded selection, duplicate notification prevention, Firebase-disabled no-op behavior and durable telemetry.
- Full closure evidence is tracked in `docs/audit/backend-scheduled-workers-push-grounding-closure.md`.

## Push/Firebase Delivery

Current production-safe state:

- Missing Firebase config does not crash startup.
- In-app notification is authoritative.
- Push subscription persistence exists.
- Live Firebase delivery is not enabled by default.

Closure update:

- Firebase live delivery remains gated, but the backend now has a `PushDeliveryService` runtime stub.
- Disabled or missing provider config returns a typed safe result and records `push_delivery` telemetry.
- In-app notification remains authoritative and is never blocked by Firebase availability.

## Cost / Token / Provider Ledger

Existing accepted behavior:

- `Message` stores `ModelUsed`, `TokensUsed`, `CostUSD`.
- `Session` stores `TotalTokensUsed`, `TotalCostUSD`.
- `TokenCostEstimator` returns a safe fallback estimate for unknown models.

New hardening:

- Added durable `CostRecords` table.
- Added `RuntimeTelemetryService.RecordCostAsync`.
- Tutor message persistence records a cost event after saving the assistant message.
- Cost write failure is swallowed/logged by the telemetry service and does not break user response.

Tests:

- `RuntimeTelemetry_RecordsToolEventAndCostRecord`
- `TokenCostEstimator_UnknownModelUsesSafeFallback`

## Durable Plugin / Tool Telemetry

New hardening:

- Added durable `ToolTelemetryEvents` table.
- Added `RuntimeTelemetryService.RecordToolEventAsync`.
- `TutorToolRuntime` records tool-hint events for detected tool/capability usage.
- `PluginTelemetryFilter` records SK function invocation events.
- Metadata JSON is size bounded to 4000 chars.
- Negative latency/tokens/cost are clamped.
- Telemetry write failure is logged and swallowed.

Tests:

- enabled/disabled/fallback event shape covered by `RuntimeTelemetry_RecordsToolEventAndCostRecord`.
- tool capability matrix covered by `ToolCapabilityContractTests` and Python contract tests.

## Provider Gates

| Provider/tool | Runtime status | Startup behavior | Notes |
|---|---|---|---|
| Tavily | key-gated | no-key does not throw; returns `[tavily:disabled]` | web grounding must be source-separated |
| Visual generation | beta/gated | no startup crash | markdown URL only; illustrative |
| YouTube pedagogy | beta/gated | no startup crash | pedagogy only by default |
| Wolfram | disabled stub | no startup crash | no live provider call by default |
| Weather | disabled stub | no startup crash | beta utility, not core evidence |
| News | disabled stub | no startup crash | current claims require provider evidence |
| Crypto | disabled stub | no startup crash | no financial advice |

## IDE Execution Safety

Decision: `DISABLED_WITH_RUNTIME_STUB_PENDING_SANDBOX`

- Normal Tutor/SK auto-execution remains disabled.
- `/api/code/run` and `/api/code/execute` remain the existing authenticated sandbox-backed API surface.
- Any future SK auto-execution must require dev/admin gate, language allowlist, timeout, output limit, no host shell, no filesystem escape and no environment/secret exposure.

## Grounding Citation-Missing Persistence

Current state:

- Chat/source metadata exposes citations, grounding mode, fallback reason and provider warnings.
- LearningSignal infrastructure exists.

Decision:

- durable `SourceCitationMissing` / `WikiCitationMissing` quality signals remain **GATED_PENDING_RUNTIME_PROOF**.
- Not implemented in this phase because it needs careful EducatorCore context visibility to avoid false positives.
- This is backend hardening debt, not a frontend blocker.

## Migration

Migration: `20260505022104_AddRuntimeTelemetryAndCostRecords`

Added:

- `CostRecords`
- `ToolTelemetryEvents`
- indexes for user/time and provider/model/time or tool/time

Local DB update: PASS.
