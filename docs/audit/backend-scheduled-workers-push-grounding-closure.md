# Backend Scheduled Workers + Push + Grounding Signal Closure

Phase branch: `feature/backend-production-hardening-dirty-orka-parity`  
Accepted base commit: `3b6d7ed Harden workers cost provider gates`

## Baseline

| Check | Result |
|---|---|
| Worktree at start | clean |
| Branch | `feature/backend-production-hardening-dirty-orka-parity` |
| Latest accepted commit | `3b6d7ed Harden workers cost provider gates` |
| Telemetry migration file | present: `20260505022104_AddRuntimeTelemetryAndCostRecords` |
| DbContext telemetry sets | `ToolTelemetryEvents`, `CostRecords` present |
| `dotnet restore` | PASS |
| `dotnet build` | PASS |
| `dotnet test --no-build` baseline | PASS, 39 passed |
| `python -m pytest contract_tests/ -q` baseline | PASS when API is running on 5101; 37 passed, 1 skipped |

The first Python baseline attempt failed because no API process was listening on port 5101. After starting the backend, the same suite passed. This was classified as harness/server setup, not an application regression.

## Final Decisions

| Item | Code path | Final decision | Evidence |
|---|---|---|---|
| Scheduled SRS reminder worker | `SrsReminderWorker`, `SrsReminderWorkerService` | ACCEPTED_WITH_GATE | Hosted worker is registered but disabled by config unless `Workers:SrsReminder:Enabled=true`; disabled path records durable telemetry and exits safely. |
| Scheduled DailyChallenge worker | `DailyChallengeWorker`, `DailyChallengeWorkerService` | ACCEPTED_WITH_GATE | Hosted worker is registered but disabled by config unless `Workers:DailyChallenge:Enabled=true`; enabled service path is bounded and duplicate-safe. |
| Firebase / push delivery | `PushDeliveryService` | ACCEPTED_WITH_STUB | In-app notification remains authoritative; Firebase disabled/missing config returns safe result and durable `push_delivery` telemetry. |
| Citation-missing signal persistence | `EducatorCoreService.RecordAnswerQualitySignalsAsync` | ACCEPTED | Source context without `[doc:sourceId:pN]` persists `SourceCitationMissing`; YouTube teaching reference persists `TeachingMoveApplied`; tests prove DB rows. |
| Worker/tool telemetry consistency | `RuntimeTelemetryService` plus worker/push services | ACCEPTED | Worker disabled/enabled runs and push delivery create durable tool telemetry. |

## SRS Reminder Worker

Behavior:

- Registered as hosted service.
- Disabled by default without changing appsettings.
- `Workers:SrsReminder:Enabled=true` enables the loop.
- `Workers:SrsReminder:IntervalMinutes`, `BatchSize`, and `DuplicateWindowHours` are read from config with safe bounds.
- Due review selection is bounded.
- Duplicate notification prevention uses `(userId, type=srs-reminder, relatedEntityType=ReviewItem, relatedEntityId, duplicate window)`.
- Push delivery is attempted only after in-app notification creation.
- Missing push subscriptions route through safe disabled push result.
- Cancellation is respected.
- One failed batch logs and records telemetry without crashing startup.

Tests:

- Disabled config no-op emits `srs_reminder_worker` telemetry.
- Enabled run creates one notification.
- Second enabled run within duplicate window creates no duplicate.
- Push disabled path records `push_delivery` telemetry.

Runtime classification:

- Default production state: `ACCEPTED_WITH_GATE`.
- Live reminder delivery can be enabled by config after operational notification policy is chosen.

## DailyChallenge Worker

Behavior:

- Registered as hosted service.
- Disabled by default without changing appsettings.
- `Workers:DailyChallenge:Enabled=true` enables the loop.
- `Workers:DailyChallenge:IntervalMinutes`, `BatchSize`, and `DuplicateWindowHours` are read with safe bounds.
- Active daily challenge selection is bounded to today.
- Duplicate notification prevention uses `(userId, type=daily-challenge, relatedEntityType=DailyChallenge, relatedEntityId, duplicate window)`.
- Push delivery is never required for the in-app notification to exist.

Tests:

- Enabled run creates one `daily-challenge` in-app notification.
- Second enabled run within duplicate window creates no duplicate.
- Existing DailyChallenge submit/idempotency contract tests remain green.

Runtime classification:

- Default production state: `ACCEPTED_WITH_GATE`.
- Worker can be enabled later without requiring real Firebase credentials.

## Firebase / Push Delivery

Behavior:

- `PushDeliveryService` does not require Firebase credentials for backend startup.
- If `Notifications:Push:Enabled` and `Firebase:Enabled` are not true, result is:
  - `Success=false`
  - `Status=disabled`
  - `ErrorCode=provider_disabled`
  - safe message: in-app notification was saved
- If provider is enabled but no subscription exists, result is `invalid_token`.
- If provider is enabled but Firebase config is missing, result is `provider_missing`.
- Live Firebase send is intentionally gated and not executed by default.
- No endpoint or service returns push tokens/secrets in user-facing messages.
- Every push attempt records durable `push_delivery` telemetry; telemetry write failure remains non-blocking through `RuntimeTelemetryService`.

Tests:

- Disabled provider returns safe no-op result.
- Disabled provider records durable telemetry.
- Returned safe message does not contain token text.

Runtime classification:

- `ACCEPTED_WITH_STUB`.
- Live Firebase delivery remains beta/ops gated until real credentials, invalid-token pruning, and retry policy are configured.

## Citation-Missing Persistent Signals

Existing accepted EducatorCore behavior is now covered by deterministic persistence tests.

Rules:

- Notebook/source context present and answer lacks `[doc:sourceId:pN]` -> persist `SourceCitationMissing`.
- Notebook/source context present and answer includes valid doc citation -> do not persist missing signal.
- YouTube teaching reference present -> persist `TeachingMoveApplied`.
- YouTube remains pedagogy/reference by default; it is not factual grounding unless explicitly sourced.
- Learning signal write failures are swallowed/logged inside EducatorCore and do not break answer flow.

Tests:

- `EducatorCore_DocContextWithoutCitationPersistsSourceCitationMissing`
- `EducatorCore_DocCitationPreventsMissingSignal`
- `EducatorCore_YouTubeReferencePersistsTeachingMoveApplied`

Runtime classification:

- `ACCEPTED`.

## Runtime Smoke Evidence

Expected smoke commands for this phase:

```powershell
$env:ASPNETCORE_URLS="http://localhost:5101"
dotnet run --project Orka.API/Orka.API.csproj --urls "http://localhost:5101"
curl.exe -i http://127.0.0.1:5101/swagger/index.html
curl.exe -i http://127.0.0.1:5101/health/live
curl.exe -i http://127.0.0.1:5101/health/ready
curl.exe -i http://127.0.0.1:5101/api/korteks/ping
curl.exe -i http://127.0.0.1:5101/api/tools/capabilities
curl.exe -i http://127.0.0.1:5101/api/tools/capabilities/daily_challenge
curl.exe -i http://127.0.0.1:5101/api/tools/capabilities/review_query
curl.exe -i http://127.0.0.1:5101/api/tools/capabilities/bookmarks
```

Observed during validation:

- Swagger: 200
- Health live: 200
- Health ready: 200
- Korteks ping without token: 401 expected
- Tool capability endpoints: 200
- Hosted worker startup with missing worker config: safe disabled logs and durable telemetry attempts

## Remaining Notes

True backend blockers: none identified in this closure.

Frontend UI decisions:

- Whether to surface SRS/daily reminder toggles.
- How to present push disabled/provider missing states to users.

Beta improvements:

- Enable live Firebase delivery with real provider credentials.
- Add invalid-token pruning and provider-specific retry classification after live Firebase integration.

Production hardening:

- Add operational dashboards for worker telemetry trends.
- Add load tests for large reminder batches.
