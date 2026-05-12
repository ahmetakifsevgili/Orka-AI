# AI/RAG Feature Constitution

## Purpose

Keep AI-heavy features reliable, scoped, cost-aware, and evidence-grounded.

## Use When

Use this for Chat, Wiki, Korteks, RAG/source retrieval, evaluator/analyzer,
summarizer, provider routing, Semantic Kernel plugins, citations, or external
evidence work.

## Required Checklist

- Quota path: AI calls go through the existing budget/quota guard or have an explicit reason they do not.
- Cost record: successful and failed model calls produce cost/telemetry where the existing architecture expects it.
- Provider failures: timeout, 401, 403, 429, invalid response, and fallback behavior are user-safe.
- Stream safety: stream endpoints fail safely before/inside streams without corrupting response contracts.
- Evidence scope: source/topic/user scope is explicit and tested.
- Citation metadata: existing citation format is preserved; additive metadata explains source/topic relation when relevant.
- Unsupported claims: grounded flows test missing/unsupported citation behavior.
- Background isolation: evaluator/postprocess/feedback failures do not break successful user responses.
- External tests: real provider/network tests are behind env flags and outside deterministic quick scripts.
- Telemetry: provider failure, quota hit, degraded/fallback behavior is visible to existing telemetry where applicable.

## Red Lines

- New AI call path that bypasses quota/cost controls.
- External provider or public HTTP test added to `quick-backend.ps1` or `quick-coordination.ps1`.
- Grounded answer claims without source evidence or explicit degraded wording.
- Cross-user source, topic, Redis, or citation leakage.
- Provider raw error exposed to users.

## Test Expectation

Use fake/stub providers for deterministic tests. Run targeted AI/RAG tests and
`scripts/quick-coordination.ps1` for Chat, Wiki, Korteks, RAG, quiz, topic scope,
or dashboard coordination changes. Run `scripts/quick-backend.ps1` when security,
quota, config, or public API behavior changes.

## Report Expectation

Report provider path, quota/cost impact, fallback/degraded behavior, citation
impact, external test gating, and any remaining hallucination/evidence risk.
