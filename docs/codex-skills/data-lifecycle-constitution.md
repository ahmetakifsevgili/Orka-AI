# Data Lifecycle Constitution

## Purpose

Ensure new durable data, cache keys, session traces, telemetry, or files have
delete, retention, privacy, and migration behavior from day one.

## Use When

Use this when adding or changing entities, tables, migrations, Redis/cache keys,
session data, source data, telemetry/cost records, uploads, generated artifacts,
or delete flows.

## Required Checklist

- Account delete: behavior is defined for the new data.
- Topic delete: behavior is defined for topic-scoped data.
- Source/session delete: behavior is defined when relevant.
- Redis/cache cleanup: keys are bounded by known user/topic/session ids; no broad `KEYS`/global scan.
- Retention: TTL or retention worker impact is considered for temporary data.
- Privacy: anonymization/redaction is defined for telemetry, cost, logs, and metadata.
- Migration: schema changes are additive by default and snapshot stays aligned.
- Rollback/deploy: migration script or deployment note is planned when schema changes.
- Tests: lifecycle delete and cross-user no-leak behavior are covered.

## Red Lines

- New durable user data with no account-delete behavior.
- Session-keyed Redis context that survives topic/account delete unintentionally.
- Cross-user purge.
- Broad Redis scan in delete paths.
- Migration without rollback/deploy consideration.

## Test Expectation

Add or update lifecycle tests for account/topic/source/session delete as
applicable. Use relational SQL coverage for database lifecycle behavior when
InMemory is insufficient. Run `scripts/quick-backend.ps1` for lifecycle,
migration, retention, or privacy-impacting changes.

## Report Expectation

Report every new durable/cache key, delete behavior, retention/privacy impact,
migration/deploy notes, and lifecycle tests run.
