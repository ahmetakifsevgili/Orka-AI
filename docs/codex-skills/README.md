# Codex Skills Anayasasi

This folder (`docs/codex-skills/`) is the Stage 4 feature-work contract for Orka.
Before Codex plans or implements a small/medium feature, it should read the
relevant constitution files and use the prompt/report templates here.

Start every new chat or branch from the root `CODEX.md`, then read
`docs/project-state/current-roadmap.md` and this file. The roadmap order must not
change unless the user explicitly approves it.

This is not a real `.codex` skill package yet. It is repo-local operating law for
feature work. It can be converted into a Codex skill later if that becomes useful.

## When to read what

- Backend/API/data changes: `backend-feature-constitution.md`
- AI, RAG, Wiki, Chat, Korteks, provider, source/citation work: `ai-rag-feature-constitution.md`
- Frontend API client, DTO, stream/sync, UI contract work: `frontend-contract-constitution.md`
- New persistent data, Redis/cache/session/telemetry data, delete/privacy impact: `data-lifecycle-constitution.md`
- Every feature plan and completion report: `testing-gate-constitution.md`

## Default feature workflow

1. Identify the relevant constitution files.
2. Read them before writing a plan.
3. Produce a short plan that names the applicable checks.
4. Implement only after the user asks for implementation.
5. Run the smallest meaningful targeted tests plus the required quick gate.
6. Finish with `feature-completion-report-template.md`.

## Non-negotiables

- Do not add cross-user leakage risk.
- Do not bypass auth, ownership, quota, or lifecycle guards.
- Do not put external provider/network tests into deterministic quick gates.
- Do not hide unrun or failing tests in the final report.
- Do not stage or commit unless the user explicitly asks.
