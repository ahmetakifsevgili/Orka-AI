# Frontend Contract Constitution

## Purpose

Prevent frontend/backend contract drift, stream/sync endpoint confusion, auth
wrapper bypass, and user-facing copy regressions.

## Use When

Use this for `api.ts`, frontend DTO/types, stream clients, Chat, Wiki, Korteks,
Dashboard, Sources, auth state, or user-visible copy changes.

## Required Checklist

- API contract: backend response shape change is reflected in frontend types/client code.
- Optional additives: new backend fields are optional unless the backend guarantees them.
- Stream vs sync: SSE endpoints and JSON endpoints are clearly separated.
- Auth wrapper: fetch/SSE calls use authenticated helpers and never send `Bearer null` or `Bearer undefined`.
- State UX: loading, error, empty, unauthorized, and retry states are considered.
- User-facing copy: no mojibake or accidental ASCII placeholder regression.
- Smoke impact: decide whether `npm run typecheck` and `npm run quick:smoke` are required.
- Backend impact: frontend changes that assume backend behavior cite the backend test/contract guard.

## Red Lines

- Hiding new contract shape behind broad `any`.
- Wiring an SSE parser to a sync JSON endpoint.
- Bypassing authenticated fetch/refresh wrappers.
- User-visible mojibake.
- Breaking current API response consumption without backend/frontend tests.

## Test Expectation

Run `npm run typecheck` for type or API client changes. Run `npm run quick:smoke`
for stream, auth, markdown/security, endpoint, or user-facing smoke changes.
Run backend contract tests if the frontend change depends on backend response
shape.

## Report Expectation

Report changed frontend contracts, optional/additive fields, stream/sync impact,
auth behavior, typecheck/smoke results, and any backend dependency.
