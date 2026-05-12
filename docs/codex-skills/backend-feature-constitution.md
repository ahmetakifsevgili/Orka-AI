# Backend Feature Constitution

## Purpose

Prevent backend feature work from reopening ownership, security, lifecycle, API
contract, logging, or migration risks.

## Use When

Use this for controller, service, entity, DTO, middleware, Redis/cache, telemetry,
background job, migration, or API response changes.

## Required Checklist

- Ownership: every user-owned read/write has `userId` filtering or an explicit ownership guard.
- Cross-user safety: no query, cache key, dashboard metric, or background job can leak another user's data.
- Abuse guard: rate limit, quota, size limit, or backpressure is considered for public or expensive endpoints.
- Cost impact: AI/cost/telemetry side effects are understood and recorded when applicable.
- Lifecycle impact: account/topic/source/session delete behavior is defined for any new durable data.
- Redis/cache scope: keys include the correct user/topic/session scope and have purge/TTL behavior where needed.
- API contract: response changes are additive unless a breaking change is explicitly requested.
- Logging: logs are safe, redacted, and do not include secrets, raw tokens, raw provider errors, private file names, or personal data.
- Migration: schema changes are additive and safe by default; migration and snapshot stay aligned.
- Tests: targeted unit/integration/API tests cover the new behavior and safety boundary.

## Red Lines

- User data query without ownership filtering.
- Raw `ex.Message` or provider payload in a public response.
- Secret, token, connection string, or private user data in logs.
- Migration without model snapshot alignment.
- Breaking API response shape without frontend/type update and explicit approval.

## Test Expectation

Run targeted tests for the changed path. Run `scripts/quick-backend.ps1` for
auth, ownership, lifecycle, migration, public API, or production-safety changes.
Run `scripts/quick-coordination.ps1` if the change touches chat, quiz, topic
scope, RAG, Wiki, Korteks, or dashboard coordination behavior.

## Report Expectation

The completion report must say which checklist items applied, which tests ran,
whether API shape changed, whether migration/deploy work is needed, and whether
stage/commit was avoided.
