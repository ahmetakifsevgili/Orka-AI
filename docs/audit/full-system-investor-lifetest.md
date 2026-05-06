# Full System Investor Lifetest

Phase: Investor-grade system optimization and due-diligence gate  
Active repo: `D:\Orka`  
Backend URL: `http://localhost:5101`  
Frontend URL: `http://localhost:3000`

## Automated Validation

| Area | Command | Expected | Actual | Result |
|---|---|---|---|---|
| Git | `git status --short` | clean at start | clean | PASS |
| Git | `git rev-parse HEAD` vs `origin/main` | equal at start | equal | PASS |
| Backend | `dotnet restore` | success | success | PASS |
| Backend | `dotnet build` | 0 warnings/errors | 0 warnings/errors | PASS |
| Backend | `dotnet test --no-build` | pass | 61 passed | PASS |
| Frontend | `npm run build` | pass | pass | PASS |
| Frontend | `npm run smoke:ui` | pass | pass | PASS |
| Frontend | `npm run smoke:contracts` | pass | pass | PASS |
| Frontend | `npm run typecheck` | pass | pass | PASS |
| Contract tests | `python -m pytest contract_tests/ -q` with backend running | pass | 37 passed, 1 skipped; marker warnings closed in provider/Redis notes phase | PASS |

## Runtime Smoke

| Endpoint | Expected | Actual | Result |
|---|---|---|---|
| `/swagger/index.html` | 200 | 200 | PASS |
| `/health/live` | 200 | 200 | PASS |
| `/health/ready` | 200 or infra block | 200 | PASS |
| `/health` | 200 | 200 | PASS |
| `/api/tools/capabilities` | 200 | 200 | PASS |
| `/api/tools/capabilities/ide_execution` | 200 | 200 | PASS |
| `/api/tools/capabilities/news` | 200 | 200 | PASS |
| `/api/tools/capabilities/weather` | 200 | 200 | PASS |
| `/api/tools/capabilities/crypto` | 200 | 200 | PASS |
| `/api/tools/capabilities/wolfram_alpha` | 200 | 200 | PASS |
| `/api/tools/capabilities/youtube_pedagogy` | 200 | 200 | PASS |
| `/api/tools/capabilities/bookmarks` | 200 | 200 | PASS |
| `/api/tools/capabilities/mermaid` | 200 | 200 | PASS |
| frontend `/` | 200 | 200 | PASS |
| frontend `/login` | 200 | 200 | PASS |

## SQL Lifecycle

Required final proof:

- register unique user
- login
- create topic
- list/get topic
- create/list/delete bookmark or flashcard

| Step | Expected | Actual | Result |
|---|---|---|---|
| register unique user | 201 | 201 | PASS |
| login unique user | 200 and token present | 200, token not printed | PASS |
| create topic | 200 | 200, id present | PASS |
| list topic | created topic visible | visible | PASS |
| get topic | 200 | 200 | PASS |
| create bookmark | 200 | 200, id present | PASS |
| list bookmark | created bookmark visible | visible | PASS |
| delete bookmark | 200 deleted | deleted true | PASS |

## Redis Lifecycle

Required final proof:

- `/health/ready` and `/health` report Redis healthy, or Redis live proof is blocked honestly.
- No Redis flush/delete/reset.

| Step | Expected | Actual | Result |
|---|---|---|---|
| `/health/ready` | Redis healthy or infra block | 200 | PASS |
| `/health` payload | Redis visible | Redis evidence present | PASS |
| destructive Redis operations | none | none run | PASS |
| Redis degraded mini-proof | app survives invalid Redis override | second runtime stayed live; `/health/live` 200, `/health/ready` 503 with Redis unhealthy, capabilities 200 | PASS_WITH_NOTE |

## Provider Notes Closure

| Provider/tool | Expected | Actual | Result |
|---|---|---|---|
| YouTube Data API metadata | configured key proves metadata/search without printing value | 2 video metadata results; transcript still expected-degraded when unavailable | PASS_WITH_NOTE |
| News / GDELT | public fallback reachable | HTTP 200 with article records; latency can approach timeout | PASS_WITH_NOTE |
| Weather / Open-Meteo | public fallback reachable | Istanbul current data contained temperature/weather code | PASS |
| Crypto / CoinGecko | public fallback reachable | BTC price/timestamp present; no-investment-advice guard remains | PASS |

## Security / Artifact Guard

Required final proof:

- no staged secrets
- no `.env.local`
- no service account JSON
- no `node_modules`
- no `dist/build`
- no runtime logs/local DB files
- frontend has no direct provider execution calls

| Guard | Actual | Result |
|---|---|---|
| staged secrets | no staged secret values found | PASS |
| tracked `.env.local` / service account JSON | none found | PASS |
| tracked `node_modules` / frontend `dist` / build output | none found | PASS |
| tracked runtime logs/local DB files | none found after removing API runtime evidence files | PASS |
| frontend direct provider execution calls | no direct provider execution calls found; provider labels only | PASS |
| potential secret-pattern hits | fake/redaction test strings only, values masked during inspection | PASS |
