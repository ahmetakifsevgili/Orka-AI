# Dirty Orka Feature Parity Audit

Phase: Backend Production Hardening + Full Dirty-Orka Feature Optimization  
Source repo: `D:\Orka`  
Target repo: `D:\Orka-main-validation`  
Target branch: `feature/backend-production-hardening-dirty-orka-parity`

## Baseline Snapshot

| Check | Result | Evidence |
|---|---|---|
| Worktree before phase | clean | `git status --short` returned empty |
| Starting commit | `fd838b8` | `Complete backend feature parity and Semantic Kernel runtime` |
| .NET SDK | 8.0.121 | `dotnet --info` |
| Restore | PASS | `dotnet restore` |
| Build | PASS with one pre-existing warning | `IOpenRouterService.GenerateResponseStreamAsync` hides inherited member |
| dotnet test baseline | FIXED_TO_PASS | Plan fallback titles were improved, then `35/35` passed |
| contract tests | PASS | `36 passed, 1 skipped` |
| Swagger smoke | PASS | `/swagger/index.html` -> 200 |
| Health smoke | PASS | `/health/live` and `/health/ready` -> 200 |
| Korteks auth smoke | PASS | `/api/korteks/ping` -> 401 without token |

## A. Tutor / Semantic Kernel / Plugin Runtime

| Capability | Dirty Orka | Target before hardening | Target after hardening | State |
|---|---:|---:|---|---|
| Semantic Kernel registration | yes | yes | deterministic plugin registration preserved | INTEGRATED_AND_TESTED |
| Plugin telemetry filter | yes | yes | registered with SK kernel | INTEGRATED_AND_TESTED |
| Tool metadata into chat | partial | yes | `TutorToolRuntime` maps detected tool intent to `metadata.usedTools` | INTEGRATED_AND_TESTED |
| Tool capability model | no central contract | missing | `/api/tools/capabilities` added | INTEGRATED_AND_TESTED |
| Disabled provider behavior | mixed | partial | dangerous/missing tools now expose disabled stubs/capability status | INTEGRATED_AND_TESTED |
| Tutor direct auto-execution | yes for some dirty plugins | intentionally limited | high-risk auto-tools are disabled/gated | DISABLED_WITH_RUNTIME_STUB |

## B. Grounding / RAG / Source / Wiki / Summarizer / Korteks

| Capability | Target status | Notes |
|---|---|---|
| Source upload/extraction/chunks | INTEGRATED_AND_TESTED | Existing lifecycle and contract tests cover upload/delete/retrieval. |
| Structured citations | INTEGRATED_AND_TESTED | Chat metadata extracts `[doc:sourceId:pN]`. |
| Wiki grounding | INTEGRATED_AND_TESTED | Existing Wiki/Summarizer lifecycle accepted with source-aware citations. |
| Korteks web/research grounding | INTEGRATED_BEHIND_GATE | Tavily/Wikipedia/Academic paths exist; provider availability is classified. |
| YouTube pedagogy | INTEGRATED_BEHIND_GATE | YouTube is pedagogy/reference by default, not factual grounding. |
| Hallucination/fallback metadata | INTEGRATED_AND_TESTED | `groundingMode`, `fallbackReason`, `providerWarnings` contract exists. |
| Citation-missing telemetry | PRODUCTION_HARDENING | Contract exists; durable quality signal can be expanded later. |

## C. Tools

| Tool | Dirty Orka | Target before | Target after | Decision |
|---|---:|---:|---|---|
| Wolfram | plugin exists | missing | disabled SK stub + capability row | DISABLED_WITH_RUNTIME_STUB |
| IDE/code execution plugin | plugin exists | `/api/code/run` and `/api/code/execute`, no SK plugin | disabled SK stub; sandbox API remains | BETA_ADMIN_OR_DEV_ONLY |
| Weather | plugin exists | missing | disabled/beta capability + safe stub | DISABLED_WITH_RUNTIME_STUB |
| News | plugin exists | missing | disabled capability + safe stub | DISABLED_WITH_RUNTIME_STUB |
| Crypto | plugin exists | missing | disabled/beta capability + safe stub, no financial advice | DISABLED_WITH_RUNTIME_STUB |
| Visual generation | plugin exists | plugin exists | capability row and SK registration | INTEGRATED_BEHIND_GATE |
| Mermaid | prompt/metadata behavior | metadata extractor | capability row | INTEGRATED_AND_TESTED |
| YouTube | plugin exists | plugin exists | capability row; pedagogy boundary documented | INTEGRATED_BEHIND_GATE |
| Tavily web | plugin exists | plugin exists but constructor could throw on missing key | missing key now returns disabled response | INTEGRATED_BEHIND_GATE |
| SourcesQuery | plugin exists | plugin exists | capability row | INTEGRATED_AND_TESTED |
| ReviewQuery | plugin exists | plugin exists | capability row | INTEGRATED_AND_TESTED |
| Flashcard | plugin exists | plugin exists | capability row | INTEGRATED_AND_TESTED |
| DailyChallenge | plugin exists | plugin exists | capability row | INTEGRATED_AND_TESTED |
| Bookmark | plugin exists | plugin exists | capability row | INTEGRATED_AND_TESTED |

## D. Background Workers

| Capability | Current target | Decision |
|---|---|---|
| BackgroundTaskQueue | active hosted service | INTEGRATED_AND_TESTED |
| Classroom/audio background TTS | accepted with fallback | INTEGRATED_AND_TESTED |
| SRS reminders | not fully scheduled/proven | PRODUCTION_HARDENING |
| DailyChallenge worker | service/lazy path proven; scheduled worker not hardened | PRODUCTION_HARDENING |
| Push delivery worker/Firebase | in-app first + subscriptions; live Firebase optional | PRODUCTION_HARDENING |
| Retry/timeout/cancellation | queue has bounded attempts; broad worker policies need deeper load tests | PRODUCTION_HARDENING |

## E. Learning Features

| Feature | Current state |
|---|---|
| Flashcards | durable model, API, SRS link, tests |
| Review/SRS | durable ReviewItem, SM-2-ish update, tests |
| Bookmarks | durable model/API/plugin/capability added |
| Daily Challenge | durable model, idempotent XP tests |
| Skill mastery | accepted service and tests |
| Learning signals | accepted service; YouTube-specific signal remains beta/prod hardening |
| Classroom | accepted start/ask/fallback/user isolation |
| Audio Overview | accepted script-only fallback/status contract |
| XP/badges | durable XpEvent, Badge/UserBadge |

## F. Observability / LLMOps

| Capability | Current state | Decision |
|---|---|---|
| Token estimator | exists | INTEGRATED_AND_TESTED |
| Provider latency metrics | partial logs | PRODUCTION_HARDENING |
| Cost ledger | estimator exists, durable ledger not complete | PRODUCTION_HARDENING |
| Correlation context | exists | INTEGRATED_AND_TESTED |
| AI debug logging | logs exist; must avoid secrets | INTEGRATED_BEHIND_GATE |
| Plugin telemetry | SK filter registered | INTEGRATED_AND_TESTED |
| Evaluator/analyzer/summarizer loop | accepted backend lifecycle | INTEGRATED_AND_TESTED |

## G. API Contracts

New/updated backend contract surfaces:

- `GET /api/tools/capabilities`
- `GET /api/tools/capabilities/{toolId}`
- Dirty tools are no longer silent omissions; they are visible as enabled, beta, disabled stub, admin/dev-only, or production-hardening.
- Existing frontend-ready contracts remain canonical and unchanged.

