# Testing Gate Constitution

## Purpose

Make feature validation predictable and keep deterministic regression gates free
from external/provider flakiness.

## Use When

Use this for every feature plan and every completion report.

## Required Checklist

- Targeted tests: unit/integration/API tests cover the behavior changed.
- Backend quick: decide if `scripts/quick-backend.ps1` is required.
- Coordination quick: decide if `scripts/quick-coordination.ps1` is required.
- Frontend checks: decide if `npm run typecheck` and `npm run quick:smoke` are required.
- External dependency: real provider/network tests are gated by env flags and not in quick scripts.
- Flaky risk: background timing, queues, streams, and clocks use fake/noop/deterministic helpers where possible.
- Migration: schema work includes model snapshot review and idempotent script smoke/review when needed.
- Hygiene: run `git diff --check` before final report.
- Transparency: report tests that were not run and why.

## Red Lines

- External provider/network dependency in mandatory quick gates.
- Skipping relevant tests without saying so.
- Hiding failing tests.
- Adding migration without script/review note.
- Treating frontend typecheck as optional when frontend types/API client changed.

## Gate Selection

- Backend/security/auth/request-boundary/lifecycle/migration/config: targeted tests + `scripts/quick-backend.ps1`.
- Chat/quiz/topic scope/RAG/Wiki/Korteks/dashboard coordination: targeted tests + `scripts/quick-coordination.ps1`.
- Frontend API client/types/stream/auth/markdown/user-facing copy: `npm run typecheck` and usually `npm run quick:smoke`.
- Docs-only changes: targeted static guard tests + `git diff --check`; full quick gates optional unless scripts/contracts changed.

## Report Expectation

The completion report must include command names, pass/fail status, skipped tests
with reasons, remaining risks, and a stage/commit note.
