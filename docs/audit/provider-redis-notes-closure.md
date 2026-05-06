# Provider + Redis Notes Closure Gate

Phase date: 2026-05-06  
Active repo: `D:\Orka`  
Branch: `feature/provider-redis-notes-closure`

## Executive Summary

This closure pass targets the remaining non-blocking notes from the investor-grade due-diligence gate: pytest marker warnings, live provider confidence, YouTube Data API metadata proof, Redis degraded behavior, and docs truth cleanup.

No secrets were printed. No backup folders were modified. No Redis keys were flushed or deleted.

## Baseline Validation

| Check | Expected | Actual | Result |
|---|---|---|---|
| `dotnet restore` | success | all projects up to date | PASS |
| `dotnet build` | success, 0 warnings/errors | 0 warnings, 0 errors | PASS |
| `dotnet test --no-build` | success | 61 passed | PASS |
| `python -m pytest contract_tests/ -q` before marker fix | pass, previous marker warning visible | 37 passed, 1 skipped, 2 marker warnings | PASS_WITH_NOTE |
| frontend `npm run build` | success | success | PASS |
| frontend `npm run smoke:ui` | success | success | PASS |
| frontend `npm run smoke:contracts` | success | success | PASS |
| frontend `npm run typecheck` | success | success | PASS |

## Pytest Marker Warning Closure

Added explicit pytest marker registration for:

- `lifecycle`: full backend lifecycle/runtime integration tests
- `ai`: AI/provider-related contract tests

Final proof:

| Command | Expected | Actual | Result |
|---|---|---|---|
| `python -m pytest contract_tests/ -q` | pass without unknown marker warnings | 37 passed, 1 skipped, no marker warnings | PASS |

## YouTube Live Capability Proof

Configuration presence was checked without printing values.

| Proof | Expected | Actual | Result |
|---|---|---|---|
| local YouTube key/config presence | present or blocked honestly | present in local user-secrets/config path; value not printed | PASS |
| `/api/tools/capabilities/youtube_pedagogy` | 200, safe gated/beta state | 200, `status=Beta`, `decision=INTEGRATED_BEHIND_GATE` | PASS |
| YouTube Data API metadata/search | low-volume metadata result | 2 video results, video id/channel fields present | PASS_METADATA |
| transcript guarantee | do not overclaim | transcript availability remains separate from YouTube Data API metadata | EXPECTED_DEGRADED |
| pedagogy rule | not factual authority by default | capability notes preserve pedagogy/style/reference role | PASS |

Conclusion: YouTube Data API v3 metadata/search proof is live. Transcript proof remains `EXPECTED_DEGRADED` unless a transcript source is available for the selected video and provider path.

## Public Provider Live Smoke

Low-volume public provider checks were run once. These are smoke evidence, not permanent provider-heavy CI tests.

| Tool | Provider | Proof | Actual | Result |
|---|---|---|---|---|
| News | GDELT Doc API | public article endpoint | returned HTTP 200 with article records on direct curl; first PowerShell attempt hit a safe web exception near timeout | PASS_WITH_NOTE |
| Weather | Open-Meteo | Istanbul current weather endpoint | temperature and weather code present | PASS |
| Crypto | CoinGecko | BTC simple price endpoint | price and timestamp present | PASS |

Notes:

- News/GDELT is live but can be slow enough to approach the configured timeout. The backend fallback path remains correct if GDELT is slow or unavailable.
- Crypto remains educational market data only; the no-investment-advice guard remains part of the contract.
- No commercial provider key is required for these public smoke proofs.

## Redis Degraded Mini-Proof

Method: started a second backend runtime on port `5102` with a temporary invalid Redis connection string through environment override only:

- no appsettings edits
- no Redis container stop/delete
- no Redis key flush/delete
- no database reset

| Check | Expected | Actual | Result |
|---|---|---|---|
| degraded backend startup | app should not crash if Redis is unreachable | process stayed running | PASS |
| `GET /health/live` on `5102` | 200 | 200 | PASS |
| `GET /health/ready` on `5102` | Redis unhealthy/degraded is acceptable | 503, Redis unhealthy, SQL healthy | PASS_WITH_NOTE |
| `GET /api/tools/capabilities` on `5102` | 200 | 200 | PASS |
| `GET /api/korteks/ping` on `5102` without token | 401 | 401 | PASS |

Conclusion: Redis outage does not crash the runtime. Readiness correctly fails when Redis is unreachable, while live/basic capability surfaces remain available.

## Remaining Notes

| Item | Classification | Reason |
|---|---|---|
| Wolfram live proof | OPS_PROVISIONING | AppId is still required for real Wolfram Alpha LLM API proof. |
| YouTube transcript proof | NON_BLOCKING_NOTE | YouTube Data API metadata/search is live; transcript availability is separate and can degrade safely. |
| GDELT latency | NON_BLOCKING_NOTE | Public provider is reachable but can be slow; backend fallback handles slow/unavailable cases. |
| Production provider quotas/circuit monitoring | OPS_PROVISIONING | Public providers are acceptable for local demo but production needs quota/monitoring decisions. |
| Staging chaos repeat | OPS_PROVISIONING | Redis degraded behavior was proven locally; repeat in Google Cloud staging. |

## Security / Artifact Guard

Before commit, verify:

- no secrets or provider key values staged
- no `.env.local` staged
- no service account JSON staged
- no JWT values staged
- no `node_modules`
- no `dist`/`build` artifacts
- no runtime logs/local DB files

