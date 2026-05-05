# Backend Full Optimization Audit + Lifecycle Gate

Branch: `feature/backend-production-hardening-dirty-orka-parity`  
Base commit: `5fa6b72 Enable public provider fallbacks`  
Phase date: 2026-05-06

## Executive Summary

The backend was audited for startup, DI, EF/DB, Redis/cache, auth/security, Tutor/orchestrator, tool runtime/providers, source/RAG/wiki, workers/push, telemetry/cost and performance risks.

Confirmed backend optimization fixed in this phase:

- `wolfram_alpha` now uses the old Orka-compatible Wolfram Alpha LLM API endpoint: `https://www.wolframalpha.com/api/v1/llm-api`.

Confirmed already-safe areas:

- startup/DI has no provider-key crash in keyless mode.
- `news`, `weather`, `crypto` expose enabled public provider fallbacks.
- `wolfram_alpha` remains AppId-gated with explicit disabled fallback.
- IDE execution stays authenticated and sandbox-only.
- scheduled SRS/DailyChallenge workers are registered but default-disabled/safe.
- push/Firebase missing config degrades safely.
- provider/tool telemetry and cost records are durable and failure-safe.

## Findings

| Area | Finding | Classification | Action |
|---|---|---|---|
| Wolfram provider | Validation provider used `v1/result`; dirty Orka used Wolfram `llm-api`, which is better suited to Tutor/tool consumption. | `CONFIRMED_OPTIMIZATION` | Switched provider call to `https://www.wolframalpha.com/api/v1/llm-api?input=...&appid=...`; focused tests updated. |
| Contract tests baseline | `python -m pytest contract_tests/ -q` requires local API on port 5101. When server is not running it fails with connection refused. | `ALREADY_SAFE` | Final validation runs pytest with API running. |
| News provider | NewsAPI commercial key absent should not disable current-info capability during testing. | `ALREADY_SAFE` | GDELT public fallback already integrated in previous commit. |
| Weather provider | OpenWeather commercial key absent should not disable weather capability during testing. | `ALREADY_SAFE` | Open-Meteo public fallback already integrated in previous commit. |
| Crypto provider | Commercial market-data key absent should not disable educational market data. | `ALREADY_SAFE` | CoinGecko public endpoint already integrated in previous commit. |
| Wolfram key | Old Orka had plugin code but no user-secret AppId; reliable keyless Wolfram computation is not assumed. | `THEORETICAL_RISK` | Remains gated until `AI:WolframAlpha:AppId` or `WolframAlpha:AppId` is provided. |
| Redis outage | Existing health checks surface Redis failure; several learning-context calls have safe catches. Full Redis chaos was not run because it would disturb local runtime. | `THEORETICAL_RISK` | Keep as production chaos test item, not frontend blocker. |
| Public provider rate limits | GDELT/Open-Meteo/CoinGecko are keyless but can rate-limit. | `THEORETICAL_RISK` | Existing timeout/fallback/telemetry paths cover failure; avoid provider-heavy tests. |

## Startup / DI

Status: `ALREADY_SAFE`

- `dotnet build` succeeds with 0 warnings and 0 errors.
- Provider constructors do not require commercial keys.
- Hosted workers start disabled by default and do not crash startup.
- Tool capability endpoint returns provider states without auth and without provider keys.

## EF Core / DB

Status: `ALREADY_SAFE`

- Durable tables for ReviewItem, Flashcard, DailyChallenge, XpEvent, Badge/UserBadge, Notification, ToolTelemetryEvents and CostRecords are present from accepted migrations.
- Hot read paths inspected in recent phases already use bounded queries for review/daily/source and soft-delete filtering for source chunks.
- No destructive migration was needed in this phase.

Theoretical hardening:

- Add production DB query telemetry and slow-query sampling before high-volume launch.

## Redis / Cache

Status: `ALREADY_SAFE_WITH_NOTES`

- Tutor context reads are failure-contained.
- Code execution context is stored with bounded payloads.
- YouTube transcript proof has deterministic chunk retrieval fallback and does not require Redis vector availability.

Theoretical hardening:

- Add explicit Redis chaos run in staging with Redis unavailable.

## Auth / Security

Status: `ALREADY_SAFE`

- Contract tests cover auth, duplicate registration, wrong password, protected endpoints and user isolation.
- IDE execution is auth-required and uses sandbox service; no host shell execution path is exposed.
- No appsettings/secrets were modified.
- No secrets were printed.

## Tutor / Orchestrator

Status: `ALREADY_SAFE`

- Tutor metadata remains backward-compatible.
- `UsedToolDto` supports provider/tool status, fallback, citations and grounding fields.
- Provider/tool failures become metadata/fallback and telemetry instead of 500.
- Cost writes after Tutor messages are covered by accepted runtime telemetry tests.

## Tool Runtime / Provider Matrix

| Tool | Status | Provider/fallback | Timeout | Telemetry | Safety decision |
|---|---|---|---|---|---|
| `ide_execution` | Enabled | Judge0/Piston sandbox | 30s | yes | Auth + sandbox, no host shell |
| `wolfram_alpha` | Disabled without AppId | Wolfram Alpha LLM API when AppId exists | 10s | yes | AppId-gated |
| `news` | Enabled | GDELT public fallback, NewsAPI override | 12s | yes | Source/date metadata required |
| `weather` | Enabled | Open-Meteo public fallback, OpenWeatherMap override | 10s | yes | Current context only |
| `crypto` | Enabled | CoinGecko public endpoint | 10s | yes | Educational data, no advice |
| `youtube_pedagogy` | Disabled without enable/key | transcript/pedagogy provider when configured | 12s | yes | Pedagogy only by default |
| `tavily_web_search` | Enabled if key exists | Tavily provider fallback | bounded | yes | Research context, separate from docs |
| `sources_query` | Enabled | user-owned docs | bounded | yes | deleted chunks ignored |
| `review_query` | Enabled | durable ReviewItem | bounded | yes | user isolated |
| `flashcards` | Enabled | durable Flashcard | bounded | yes | user isolated |
| `daily_challenge` | Enabled | durable challenge + XP idempotency | bounded | yes | user isolated |
| `bookmarks` | Enabled | durable bookmarks | bounded | yes | user isolated |
| `mermaid` | Enabled | text-only diagram | n/a | yes | no external provider |

## Source / RAG / Wiki

Status: `ALREADY_SAFE`

- Source delete/update lifecycle hides deleted chunks.
- Inline `[doc:sourceId:pN]` and structured citation metadata are preserved.
- Citation-missing signals persist.
- YouTube remains pedagogy/reference, not factual grounding by default.

## Workers / Push

Status: `ALREADY_SAFE`

- `BackgroundTaskQueue` requires job type and has retry/timeout behavior.
- SRS/DailyChallenge hosted workers are default-disabled and safe on startup.
- Push/Firebase missing config returns safe disabled/provider-missing behavior.
- Duplicate reminder/notification paths were hardened in prior phase.

## Telemetry / Cost

Status: `ALREADY_SAFE`

- `ToolTelemetryEvents` and `CostRecords` exist.
- RuntimeTelemetryService clamps negative values and caps metadata.
- Telemetry write failure does not break user flow.
- Disabled/fallback tools do not fake real provider spend.

## Performance / Optimization Notes

Confirmed fix:

- Wolfram now uses the Tutor-oriented LLM API endpoint, avoiding less useful short result formatting.

Theoretical risks for production hardening:

- add slow-query monitoring under production traffic.
- add provider circuit-breaker/rate-limit counters for public APIs.
- add Redis chaos staging run.
- add per-tool concurrency limits if public providers rate-limit under classroom load.

## Validation Evidence

- `git status --short` -> clean at phase start.
- `dotnet restore` -> PASS.
- `dotnet build` -> PASS, 0 warnings, 0 errors.
- `dotnet test --no-build` -> PASS, 61 passed.
- focused `FinalCoreIntelligenceTests` -> PASS, 9 passed.

Final runtime and contract validation are recorded in `backend-full-optimization-lifetest.md`.

