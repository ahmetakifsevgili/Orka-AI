# Feature Prompt Template

Use this template for small/medium feature work.

```text
[Feature name] uygula.

Before planning or coding, read the relevant Codex Skills Anayasasi files:
- docs/codex-skills/backend-feature-constitution.md
- docs/codex-skills/testing-gate-constitution.md
- Add ai-rag/frontend/data-lifecycle constitution files if the feature touches those areas.

Scope:
- ...

Out of scope:
- No large refactor.
- No production enterprise hardening unless explicitly requested.
- No frontend redesign unless explicitly requested.
- Do not stage or commit.

Acceptance criteria:
- ...

Implementation constraints:
- Preserve existing API contracts unless an additive change is stated.
- Preserve ownership/user scope.
- Preserve deterministic quick gates.

Test plan:
- targeted tests
- scripts/quick-backend.ps1 if backend/security/lifecycle/config is affected
- scripts/quick-coordination.ps1 if chat/quiz/topic/RAG/wiki/Korteks/dashboard coordination is affected
- npm run typecheck and npm run quick:smoke if frontend contracts/UI are affected
- git diff --check

Final report format:
- changed files
- constitution checks applied
- test results
- frontend/backend contract impact
- lifecycle/migration/deploy impact
- remaining risks
- stage/commit not performed
```
