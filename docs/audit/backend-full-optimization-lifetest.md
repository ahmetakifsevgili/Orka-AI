# Backend Full Optimization Lifetest

Branch: `feature/backend-production-hardening-dirty-orka-parity`  
Base commit: `5fa6b72 Enable public provider fallbacks`  
Phase: Backend Full Optimization Audit + Lifecycle Stress Gate

## Runtime Target

`http://127.0.0.1:5101`

## Baseline

| Check | Expected | Actual | Result |
|---|---:|---:|---|
| `git status --short` | clean | clean | PASS |
| branch | `feature/backend-production-hardening-dirty-orka-parity` | correct | PASS |
| latest commit | `5fa6b72` or later | `5fa6b72` at start | PASS |
| `dotnet restore` | PASS | PASS | PASS |
| `dotnet build` | 0 warnings/errors | 0 warnings/errors | PASS |
| `dotnet test --no-build` | PASS | 61 passed | PASS |

Note: `python -m pytest contract_tests/ -q` requires the API to be running on port 5101. A run without the server produced connection-refused failures; final pytest proof is run with the server active.

## Full Lifecycle Runtime Table

| Step | Command / endpoint | Expected | Actual | Result |
|---|---|---:|---:|---|
| Startup | `dotnet run --urls http://localhost:5101` | app starts | process started on port 5101 | PASS |
| Swagger | `GET /swagger/index.html` | 200 | 200 | PASS |
| Health live | `GET /health/live` | 200 | 200 | PASS |
| Health ready | `GET /health/ready` | 200 | 200 | PASS |
| Auth guard | `GET /api/korteks/ping` without token | 401 | 401 | PASS |
| Capabilities | `GET /api/tools/capabilities` | 200 | 200 | PASS |
| IDE capability | `GET /api/tools/capabilities/ide_execution` | 200 + enabled | 200 | PASS |
| Wolfram capability | `GET /api/tools/capabilities/wolfram_alpha` | 200 + disabled if no AppId | 200 | PASS |
| News capability | `GET /api/tools/capabilities/news` | 200 + enabled | 200 | PASS |
| Weather capability | `GET /api/tools/capabilities/weather` | 200 + enabled | 200 | PASS |
| Crypto capability | `GET /api/tools/capabilities/crypto` | 200 + enabled | 200 | PASS |
| YouTube capability | `GET /api/tools/capabilities/youtube_pedagogy` | 200 | 200 | PASS |
| Sources capability | `GET /api/tools/capabilities/sources_query` | 200 | 200 | PASS |
| Review capability | `GET /api/tools/capabilities/review_query` | 200 | 200 | PASS |
| Flashcards capability | `GET /api/tools/capabilities/flashcards` | 200 | 200 | PASS |
| DailyChallenge capability | `GET /api/tools/capabilities/daily_challenge` | 200 | 200 | PASS |
| Bookmarks capability | `GET /api/tools/capabilities/bookmarks` | 200 | 200 | PASS |
| Mermaid capability | `GET /api/tools/capabilities/mermaid` | 200 | 200 | PASS |
| Contract tests | `python -m pytest contract_tests/ -q` | PASS | 37 passed, 1 skipped, 2 existing mark warnings | PASS |

## Stress / Chaos Checks

| Check | Expected | Actual | Result |
|---|---:|---:|---|
| Parallel health requests | all 200 | 12/12 returned 200 | PASS |
| Parallel capabilities requests | all 200 | 12/12 returned 200 | PASS |
| Provider malformed/fallback tests | no crash | covered by .NET tests | PASS |
| Background queue failure/timeout | no worker death | covered by prior tests | PASS |
| IDE safety | auth + sandbox, no host shell | covered by tests and capability contract | PASS |

## Provider Status

| Provider tool | Runtime status | Notes |
|---|---|---|
| `news` | Enabled | GDELT public fallback; NewsAPI override. |
| `weather` | Enabled | Open-Meteo public fallback; OpenWeatherMap override. |
| `crypto` | Enabled | CoinGecko public endpoint; no investment advice. |
| `wolfram_alpha` | AppId-gated | Uses Wolfram Alpha LLM API when configured. |
| `youtube_pedagogy` | Provider-gated | Key exists in user-secrets; enable flag controls active capability. |
| `ide_execution` | Enabled | Auth + sandbox only; no raw host shell. |

## Remaining Backend Blockers

None confirmed in deterministic/backend runtime proof. Remaining items are production hardening/product roadmap:

- Wolfram requires AppId for live computation.
- Public provider rate limits need production monitoring.
- Redis/provider chaos should be repeated in staging.
