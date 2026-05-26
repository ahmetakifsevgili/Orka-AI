# Orka Comprehensive Multi-Agent Rescan - 2026-05-26

Scope: read-only audit of the current dirty workspace after the latest Gemini/Codex edits.

Repo: `D:/Orka`
Branch: `codex/heavy-learning-flow-eval-browser-qa`
HEAD: `72ee3ca0dcd3e9a28fe94e22a7ca1caefb7d0a63`
Date: 2026-05-26

## Executive Summary

This rescan used four subagents plus local verification:

- Backend + Security: provider limits, rate limits, auth, cancellation, retention, database drift.
- Pedagogy + Data: quiz generation, assessment calibration, learning signals, Redis, long-term profile data.
- Frontend + Release Gate: Wiki SSE, ProductCoherence reachability, CI/smoke gates, UI/runtime risks.
- 2026 Research Second Opinion: severity normalization and modern remediation patterns.

Short verdict: the system improved meaningfully since the previous audit, but it is not yet production-stable. The most serious issue is now generated quiz persistence: `QuizController.Generate` creates `AssessmentItem` records with `ConceptGraphSnapshotId = Guid.Empty` even though the relation is required, which can become a relational database FK failure. The rest of the high-priority risks cluster around idempotency, migration drift, answer-key leakage through rich JSON, client-supplied metadata poisoning, provider token budgets not reaching actual provider payloads, and expensive endpoint abuse protection.

## Verification Snapshot

Commands already run in this audit cycle:

- `npm run typecheck` in `D:/Orka/Orka-Front`: PASS.
- `npm run quick:smoke` in `D:/Orka/Orka-Front`: PASS.
- `npm run build` in `D:/Orka/Orka-Front`: PASS.
- Targeted backend tests: `dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --no-restore --filter "FullyQualifiedName~AuthTokenContractTests|FullyQualifiedName~CurriculumSourceRegistryTests|FullyQualifiedName~QuizAttemptSafetyTests|FullyQualifiedName~ChatParityTests|FullyQualifiedName~ProductionSafetyLiteTests|FullyQualifiedName~PublicSecuritySurfaceTests|FullyQualifiedName~OrkaMissionControlTests|FullyQualifiedName~OrkaStudyRoomTests"`: PASS, 89/89.

Important warnings and gates:

- `git diff --check`: FAIL per frontend/release subagent. Trailing whitespace / EOF hygiene exists in changed frontend/backend files.
- EF warning 10622 appeared during tests: global query filters on required relationships can produce unexpected results through inner joins.
- Frontend build warning: `InteractiveIDE.tsx` is both dynamically and statically imported, limiting chunk split benefits.
- Worktree is heavily dirty; this report describes the current workspace, not a clean committed revision.

## Active P0

### P0-1. Generated quiz can fail on relational FK

Status: active.

Evidence:

- `D:/Orka/Orka.API/Controllers/QuizController.cs:116`
- `D:/Orka/Orka.API/Controllers/QuizController.cs:122`
- `D:/Orka/Orka.API/Controllers/QuizController.cs:129`
- `D:/Orka/Orka.Core/Entities/AssessmentItem.cs:13`
- `D:/Orka/Orka.Infrastructure/Data/OrkaDbContext.cs:742`

Problem:

`QuizController.Generate` now tries to make generated quizzes durable by creating `QuizRun` and `AssessmentItem`, which is directionally correct. But the generated `AssessmentItem` gets `ConceptGraphSnapshotId = Guid.Empty` while the model treats the FK as required. On a real relational provider, a valid LLM quiz response can still die at `_db.SaveChangesAsync` with FK violation.

Modern fix:

- Either resolve/create a valid concept graph snapshot for the topic before creating generated items, or make the FK nullable for generated/ad hoc assessment items and add a migration.
- Add a relational integration test for `/api/quiz/generate` that proves generated items persist with valid FK semantics.

## Active P1

### P1-1. Quiz attempt idempotency is not behaviorally safe

Status: active.

Evidence:

- `D:/Orka/Orka.Infrastructure/Services/QuizAttemptRecorder.cs:88`
- `D:/Orka/Orka.Infrastructure/Services/QuizAttemptRecorder.cs:115`
- `D:/Orka/Orka.Infrastructure/Services/QuizAttemptRecorder.cs:321`
- `D:/Orka/Orka.Infrastructure/Data/OrkaDbContext.cs:554`

Problem:

The runtime model appears to move toward uniqueness, but the recorder still creates a new `QuizAttempt` per submit and does not return an existing result on duplicate submit. Double-click/retry can still cause a 500/constraint error or inflate `QuizRun.CorrectCount`, learning signals, SRS, or XP.

Modern fix:

- Add `clientAttemptId` or `Idempotency-Key`.
- Add a unique index over user/run/item/idempotency dimensions.
- On duplicate, return the previously computed result without writing KT/signal/SRS a second time.

### P1-2. Runtime model and migrations/snapshot appear drifted

Status: active.

Evidence:

- Runtime unique indexes: `D:/Orka/Orka.Infrastructure/Data/OrkaDbContext.cs:538`, `D:/Orka/Orka.Infrastructure/Data/OrkaDbContext.cs:555`
- Snapshot mismatch noted around `D:/Orka/Orka.Infrastructure/Migrations/OrkaDbContextModelSnapshot.cs:5572`

Problem:

Runtime `DbContext` contains new indexes/query filters, but migration snapshot does not clearly carry the same model. This means local tests against EF model can pass while production SQL schema lacks the intended constraints.

Modern fix:

- Run and gate `dotnet ef migrations has-pending-model-changes --project Orka.Infrastructure --startup-project Orka.API`.
- Add/update migration for quiz attempt unique indexes and query filter side effects.
- CI should fail if pending model changes exist.

### P1-3. Assessment calibration lacks topic ownership guard

Status: active.

Evidence:

- `D:/Orka/Orka.API/Controllers/AssessmentController.cs:106`
- `D:/Orka/Orka.API/Controllers/AssessmentController.cs:114`
- `D:/Orka/Orka.Infrastructure/Services/AssessmentCalibrationServices.cs:25`
- `D:/Orka/Orka.Infrastructure/Services/AssessmentCalibrationServices.cs:45`

Problem:

The controller accepts a `topicId` without proving it belongs to the current user. The service may filter items by user, but it can still persist a calibration run for a foreign or arbitrary topic id. That is a broken object-level authorization / learning evidence integrity issue.

Modern fix:

- Add controller-level `TopicBelongsToUserAsync` checks.
- Add service-level defensive validation so future controller paths cannot bypass ownership.
- Test cross-user `topicId` returns 404/403 and writes no run.

### P1-4. Answer-key leakage can survive inside `ContentJson`

Status: active.

Evidence:

- `D:/Orka/Orka.API/Controllers/QuestionsController.cs:52`
- `D:/Orka/Orka.Infrastructure/Services/QuestionBankService.cs:1219`
- `D:/Orka/Orka.Infrastructure/Services/QuestionBankService.cs:1233`
- `D:/Orka/Orka.Infrastructure/Services/QuestionBankService.cs:1270`
- `D:/Orka/Orka.Infrastructure/Services/CentralExamStudyService.cs:751`
- `D:/Orka/Orka.Infrastructure/Services/CentralExamStudyService.cs:764`

Problem:

Learner masking removes obvious fields like `IsCorrect`, `Explanation`, and `Explanations`, but rich `ContentJson` is returned as-is in question/practice payloads. If imported/generated rich JSON contains answer markers (`answerKey`, `correctAnswer`, option correctness, rubric hints), the learner can receive hidden answer data.

Modern fix:

- Create learner-safe DTOs for question bank and central exam starts.
- Scrub rich JSON through an allowlist schema, not a denylist.
- Add snapshot tests that fail on `answerKey`, `correctAnswer`, `isCorrect`, `solution`, `rubric`, and similar answer markers in learner payloads.

### P1-5. Client-supplied `SourceRefsJson` can poison learning metadata

Status: active.

Evidence:

- `D:/Orka/Orka.Core/DTOs/RecordQuizAttemptRequest.cs:35`
- `D:/Orka/Orka.API/Controllers/QuizController.cs:580`
- `D:/Orka/Orka.Infrastructure/Services/QuizAttemptRecorder.cs:759`
- `D:/Orka/Orka.Infrastructure/Services/LearningSignalService.cs:39`

Problem:

The controller strips `IsCorrect` and `Explanation`, but `SourceRefsJson` remains client-controlled and is merged into persisted learning metadata/signals. A client can inject answer keys, raw source chunks, fake mastery data, prompt text, or oversized metadata.

Modern fix:

- Treat learner attempt metadata as server-derived.
- Replace free-form `SourceRefsJson` with a versioned allowlist DTO.
- Apply size/depth limits and marker scrub before persistence.

### P1-6. Provider token budgets are not enforced in provider payloads

Status: active.

Evidence:

- `D:/Orka/Orka.Infrastructure/Services/AIAgentFactory.cs:285`
- `D:/Orka/Orka.Infrastructure/Services/AIAgentFactory.cs:318`
- `D:/Orka/Orka.Infrastructure/Services/OpenAICompatibleService.cs:58`
- `D:/Orka/Orka.Infrastructure/Services/GeminiService.cs:125`
- `D:/Orka/Orka.Infrastructure/Services/GroqService.cs:201`
- `D:/Orka/Orka.Infrastructure/Services/MistralService.cs:111`
- `D:/Orka/Orka.Infrastructure/Services/OpenRouterService.cs:48`
- `D:/Orka/Orka.Infrastructure/Services/GitHubModelsService.cs:82`

Problem:

The factory reads role `MaxOutputTokens`, but provider adapters do not consistently include the equivalent payload field. GitHub Models is hard-coded to 4096. This makes cost, latency, UX, and safety budgets advisory rather than enforceable.

Modern fix:

- Add a provider request options contract with `MaxOutputTokens`.
- Map it to each provider's exact field (`max_tokens`, `max_output_tokens`, `maxOutputTokens`, etc.).
- Add fake HTTP provider tests asserting the outgoing request body.

### P1-7. Expensive endpoint rate limiting is incomplete

Status: active.

Evidence:

- Policies exist: `D:/Orka/Orka.API/Extensions/AuthInfrastructureExtensions.cs:103`
- Applied controllers: `ChatController`, `CodeController`, `KorteksController`, `QuizController`, `SourcesController`
- Missing examples: `D:/Orka/Orka.API/Controllers/AudioController.cs:22`, `D:/Orka/Orka.API/Controllers/QuestionDraftGenerationController.cs:23`, `D:/Orka/Orka.API/Controllers/QuestionImportsController.cs:23`

Problem:

Rate limiting improved for several routes, but costly generation surfaces still appear unprotected or under-protected. Audio, question draft generation, question imports, and notebook-style generation can create cost and queue pressure.

Modern fix:

- Apply per-user and IP-partitioned token buckets to all expensive AI/media endpoints.
- Add concurrency caps for long-running generation.
- Prefer queued jobs for audio and bulk generation.
- Add burst tests expecting 429 after policy limits.

### P1-8. Wiki SSE bypasses authenticated refresh flow and can render raw JSON

Status: active.

Evidence:

- `D:/Orka/Orka-Front/src/components/WikiDrawer.tsx:191`
- `D:/Orka/Orka-Front/src/components/WikiDrawer.tsx:221`
- `D:/Orka/Orka-Front/src/components/WikiMainPanel.tsx:1632`
- `D:/Orka/Orka-Front/src/components/WikiMainPanel.tsx:1688`
- Refresh-aware helper: `D:/Orka/Orka-Front/src/services/api.ts:166`

Problem:

Wiki stream uses raw `fetch` with the current bearer token instead of the app's refresh-aware authenticated fetch. Expired access tokens can break Wiki chat. The SSE parser also falls back to appending unknown JSON payloads, which can expose protocol objects to users.

Modern fix:

- Add/use a refresh-aware streaming fetch helper.
- Parse typed SSE events only.
- Drop unknown events or log them safely; never append raw JSON to user-visible chat.

## Active P2

### P2-1. Non-stream chat cancellation is incomplete

Status: active but downgraded from earlier stream issue.

Evidence:

- Stream now passes request cancellation: `D:/Orka/Orka.API/Controllers/ChatController.cs:186`
- Non-stream path lacks full token chain: `D:/Orka/Orka.Core/Interfaces/IAgents.cs:12`, `D:/Orka/Orka.Core/Interfaces/IAgents.cs:19`, `D:/Orka/Orka.API/Controllers/ChatController.cs:105`
- `CancellationToken.None` remains in tutor/provider path: `D:/Orka/Orka.Infrastructure/Services/TutorAgent.cs:548`

Problem:

Streaming cancellation improved. Non-stream long LLM calls can continue after the client disconnects.

Modern fix:

- Thread `HttpContext.RequestAborted` from controller to orchestrator, agent, and provider calls.
- Add aborted request tests.

### P2-2. PII/raw message/source chunk retention needs an explicit policy

Status: active governance risk, not automatically a bug.

Evidence:

- Raw chat content: `D:/Orka/Orka.Core/Entities/Message.cs:15`
- Raw source chunk text: `D:/Orka/Orka.Core/Entities/SourceChunk.cs:12`
- Upload chunk persistence: `D:/Orka/Orka.Infrastructure/Services/LearningSourceService.cs:1226`
- Logging improved: `D:/Orka/Orka.Infrastructure/Services/AiDebugLogger.cs:10`, `D:/Orka/Orka.Infrastructure/Services/AiDebugLogger.cs:59`

Problem:

Educational systems often need raw learner messages and source chunks for continuity, RAG, review, and export. That can be acceptable only with a clear retention/minimization policy. Without one, GDPR/privacy and prompt-injection re-ingestion risk remains.

Modern fix:

- Define retention classes: chat, source chunks, derived embeddings, quiz evidence, logs.
- Add delete/export controls, encryption at rest, access audit, and PII redaction for secondary use.
- Keep raw prompts out of logs; current debug logging appears improved.

### P2-3. ProductCoherence is wired but not first-class reachable/proven

Status: changed, downgraded.

Evidence:

- Wired imports/rendering: `D:/Orka/Orka-Front/src/pages/Home.tsx:18`, `D:/Orka/Orka-Front/src/pages/Home.tsx:399`
- Default still legacy dashboard: `D:/Orka/Orka-Front/src/pages/Home.tsx:87`
- Sidebar nav exposes legacy ids: `D:/Orka/Orka-Front/src/components/LeftSidebar.tsx:59`
- CI only typecheck/build: `D:/Orka/.github/workflows/frontend-ci.yml:31`

Problem:

This is no longer a compile/API wrapper break. The remaining issue is product reachability and proof: users may not naturally enter new panels, and CI does not prove browser navigation.

Modern fix:

- Promote ProductCoherence IA into sidebar/default entry points.
- Add Playwright app-shell smoke for `home`, `study-room`, `exams`, `sources-wiki`, `notebook`, `code`.

### P2-4. EF global query filters have required-navigation risk

Status: active.

Evidence:

- Query filters added around `D:/Orka/Orka.Infrastructure/Data/OrkaDbContext.cs:4996`
- Test output emitted EF warning 10622 for required relationships.

Problem:

Global query filters are valid for soft delete/multitenancy, but EF required navigations can cause inner join filtering surprises. This is a known EF Core caveat, not a false positive.

Modern fix:

- Add matching filters to dependent entities or make relationships optional where filtered principals can disappear.
- Add tests for Include/query paths on filtered parents and children.

### P2-5. Release hygiene and CI are still weak

Status: active.

Evidence:

- `git diff --check` failed in subagent verification.
- `D:/Orka/.github/workflows/frontend-ci.yml` runs typecheck/build but not browser route smoke.
- Static smoke reads files with `fs.readFileSync`, not a real browser.

Problem:

The current CI/gates can miss runtime app-shell breakage and cannot currently pass diff hygiene.

Modern fix:

- Fix whitespace/EOF hygiene.
- Add quick smoke to CI.
- Add browser-based smoke for auth shell and ProductCoherence routes.
- Add report traceability: branch, commit, dirty state in lifetest/deep-learner reports.

## Active P3 / Conditional

### P3-1. Redis quiz anti-repeat cache exists but is not wired

Status: conditional.

Evidence:

- `D:/Orka/Orka.Core/Interfaces/IRedisMemoryService.cs:131`
- `D:/Orka/Orka.Infrastructure/Services/RedisMemoryService.cs:855`
- `D:/Orka/Orka.Infrastructure/Services/RedisMemoryService.cs:877`

Problem:

The cache exists, but generated/adaptive quiz flow does not visibly use it. If anti-repeat is a product promise, this is P2. If not, it is P3 technical debt.

Modern fix:

- Define a recent-exclusion policy using SQL + Redis.
- Apply it in generated/adaptive item selection.
- Test repeated quiz generation does not reselect recent items unless the bank is exhausted.

## Closed / Improved Since Previous Audit

These earlier critical findings are now closed or materially improved:

- Refresh token JSON/localStorage exposure is mostly closed in the main SPA/backend. Login/register now set refresh cookies and return access token/user only. `D:/Orka/Orka.API/Controllers/AuthController.cs:128`, `D:/Orka/Orka.Core/DTOs/Auth/AuthResponse.cs:5`
- Main chat SSE now uses authenticated fetch and discards unparsed JSON.
- Streaming RAG/Korteks bypass is fixed: stream path now detects `RESEARCH` and calls `IKorteksAgent`.
- Quiz generate topic ownership guard exists. `D:/Orka/Orka.API/Controllers/QuizController.cs:60`
- Quiz attempt topic/session guard exists. `D:/Orka/Orka.API/Controllers/QuizController.cs:184`
- Generated quiz answer keys are no longer obviously returned through option `isCorrect`; durable quiz work started.
- Curriculum mutation endpoints are Admin-gated.
- Soft-delete moved from manual checks toward global query filters.
- ProductCoherence compile/API wrapper mismatch is closed; backend endpoint/service surfaces exist.
- Audio blob auth improved through authenticated blob fetch.
- `quick-all.ps1` now includes frontend typecheck.
- Dead frontend pages `CourseDetail` and `QuizHistoryAndNotes` have no current references.
- Unused Cohere/HuggingFace service files were removed.

Residual caveat:

- Legacy static auth pages/scripts under `D:/Orka/Orka.API/wwwroot` still reference `refreshToken` / `orka_refresh`, and several scripts still expect `login.json.refreshToken`. The main SPA path is fixed, but static/legacy assets are stale and should be removed or updated.

## Recommended Fix Order

### First 24 hours

1. Fix generated quiz FK: valid concept graph snapshot or nullable generated-item FK plus migration.
2. Add/gate EF pending model changes and create missing migration(s).
3. Implement quiz attempt idempotency with existing-result return.
4. Add assessment calibration topic ownership guard.
5. Add learner-safe DTO/scrubber for question bank and central exam `ContentJson`.
6. Fix `git diff --check` hygiene so release gates can run.

### Next 3 days

1. Pass provider token budgets into every provider payload and test outgoing JSON.
2. Add rate limits/concurrency caps to audio, question draft, question imports, notebook/bulk generation.
3. Move Wiki SSE to authenticated streaming fetch and typed event parsing.
4. Replace free-form `SourceRefsJson` with allowlisted server-derived metadata.
5. Complete non-stream cancellation token propagation.

### Next 1 week

1. Define PII/data retention policy and technical controls.
2. Clean up EF query filter required-navigation warnings.
3. Add Redis/SQL recent-question exclusion if anti-repeat is product scope.
4. Promote ProductCoherence routes into real navigation.
5. Add Playwright/browser CI smoke for app shell and ProductCoherence routes.
6. Add report traceability to lifetest/deep-learner scripts.

## Minimum Proof Gates

- Relational `/api/quiz/generate` test proving generated `AssessmentItem` saves with valid FK semantics.
- Duplicate quiz submit test: same attempt returns same result and writes no second KT/signal/SRS/XP.
- `dotnet ef migrations has-pending-model-changes` CI gate.
- Cross-user calibration topic test: 404/403, no run persisted.
- Learner payload answer-marker snapshot tests for question bank and central exam.
- `SourceRefsJson` allowlist/size/depth tests.
- Fake provider HTTP tests for `max_tokens`, `max_output_tokens`, `maxOutputTokens`, etc.
- Burst/rate-limit tests for audio, draft generation, imports, notebook generation.
- Aborted non-stream chat request test.
- Wiki SSE auth refresh and typed parser tests.
- Browser route smoke for ProductCoherence panels.
- EF query filter Include tests around required relationships.

## 2026 Reference Baseline

- ASP.NET Core rate limiting supports named policies via `[EnableRateLimiting]` and endpoint/global policies; expensive endpoints should be explicitly covered.
- EF Core documents the required-navigation/global-query-filter caveat: required navigations can cause inner join filtering surprises.
- EF Core has `dotnet ef migrations has-pending-model-changes` for CI drift checks.
- OWASP API Security 2023 treats broken object/property-level authorization and excessive resource consumption as core API risks.
- OWASP LLM Top 10 2025 treats sensitive information disclosure, prompt injection, data poisoning, and unbounded consumption as first-class LLM application risks.
- OpenAI and other providers expose explicit output-token controls; cost/latency budgets should be enforced at request payload level, not only prompt policy level.
- Idempotent request patterns return the previous result for repeated equivalent operations instead of writing duplicate side effects.
- NIST Privacy Framework emphasizes data lifecycle, minimization, retention, and technical measures for privacy-risk management.

Source links used:

- https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit
- https://learn.microsoft.com/en-us/ef/core/querying/filters
- https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing
- https://owasp.org/API-Security/editions/2023/en/0x11-t10/
- https://owasp.org/www-project-top-10-for-large-language-model-applications/
- https://help.openai.com/en/articles/5072518-controlling-the-length-of-openai-model-responses
- https://docs.stripe.com/api/idempotent_requests
- https://www.nist.gov/privacy-framework
