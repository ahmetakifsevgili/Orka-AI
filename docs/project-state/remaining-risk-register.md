# Remaining Risk Register

This register tracks the remaining non-deterministic and operational risks
after the Phase 1-3 closure. It is intentionally free of database internals,
secret names, provider payloads, raw prompts, and generated test artifacts.

## Current State

- Deterministic backend, frontend, projection, and provider-free life-proof gates
  are the default release proof.
- Live provider quality, quota behavior, staging Redis behavior, browser visual
  review with seeded learner states, and beta telemetry still require explicit
  operator evidence before broad production use.
- Generated reports, test results, screenshots, videos, and local runtime logs
  remain artifacts and must not be committed.

## Risk Register

| Risk area | Current evidence | Closure proof |
| --- | --- | --- |
| Live provider quality | Provider-free gates pass; external provider tests are opt-in. | Run the opt-in provider smoke script with approved tokens and a small Tutor quality set; record only pass/fail, provider name, and body-free safe failure category. |
| Gemini availability | Gemini is not treated as a dependable default because quota/auth can fail. | Keep Gemini disabled unless an operator explicitly enables and verifies it. |
| Fallback routing | Deterministic provider failure and fallback tests exist. | Confirm GitHubModels, OpenRouter, Cohere, Groq, and Mistral fail or pass with body-free user-safe diagnostics. |
| Redis runtime degradation | Runtime uses Redis-compatible memory for short-lived coordination, while durable evidence remains separate. | Repeat degraded Redis proof in staging: live stays available, ready health reports degraded state, and durable writes do not depend on cache success. |
| Runtime telemetry | Safe telemetry DTOs and privacy checks exist. | Verify events include category, operation, status, correlation reference, fallback/degrade reason, and bounded metadata only. |
| Browser visual confidence | Browser smoke covers core shell and projection binding. | Run seeded learner journeys across desktop and mobile; confirm no layout overflow and no raw prompt, provider payload, source chunk, answer key, local path, stack trace, token, or owner id appears. |
| Code runtime safety | Code Learning IDE has runtime and privacy guards. | Validate timeout, unsupported language, redaction, and safe Tutor summary behavior in the deployment runtime. |
| Production operations | Startup policies and release gates are documented. | Validate monitoring, backup and restore, secret rotation, provider quota alerts, runtime sandbox limits, and incident diagnostics in the deployment environment. |
| Beta learning usefulness | Deterministic loops prove contract coherence. | Run a small human-reviewed learner set covering plan-first, remediation, source-grounded answer, code repair, Wiki repair, and Study Room flows. |

## Execution Rules

- Keep `scripts\quick-backend.ps1`, `scripts\quick-coordination.ps1`, frontend
  quick smoke, and GitHub backend release CI provider-free.
- Use `scripts\provider-live-smoke.ps1 -Enable` only when live provider calls are
  approved.
- Do not print provider secrets, user secret values, raw provider bodies, raw
  prompts, source chunks, answer keys, local paths, stack traces, or raw learner
  submissions in reports.
- Treat provider/auth/quota failures as environment evidence unless a
  deterministic product contract fails.
- Do not commit generated reports, `.test-results`, Playwright output, `dist`,
  runtime logs, or local database files.
