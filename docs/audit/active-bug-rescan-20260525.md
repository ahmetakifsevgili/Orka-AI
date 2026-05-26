# Orka Active Bug Rescan - 2026-05-25

## Snapshot

- Workspace: `D:\Orka`
- Branch: `codex/heavy-learning-flow-eval-browser-qa`
- HEAD: `72ee3ca0dcd3e9a28fe94e22a7ca1caefb7d0a63`
- Worktree: dirty; this report audits the current dirty workspace as-is.
- Purpose: re-check whether the previous audit findings are still active now that no edit agent is expected to be running in the background.

## Verification

| Check | Result |
|---|---:|
| `cd Orka-Front; npm run typecheck` | PASS |
| `cd Orka-Front; npm run quick:smoke` | PASS |
| `cd Orka-Front; npm run build` | PASS |
| `dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --no-restore --filter "FullyQualifiedName~ChatParityTests\|FullyQualifiedName~ProductionSafetyLiteTests\|FullyQualifiedName~QuizAttemptSafetyTests\|FullyQualifiedName~PublicSecuritySurfaceTests"` | PASS, 44/44 |

## Short Verdict

The repository is no longer in an obvious compile-broken state. The active problems are now mostly production/security/learning-integrity issues, not TypeScript contract failures.

The most important active bugs are still real:

1. Refresh token is still exposed to JavaScript and localStorage.
2. Non-admin users can still mutate system/global curriculum registry rows.
3. Quiz generate/attempt still lacks complete ownership protection.
4. Generated quizzes still return unsanitized answer-key-like data and are not durable learning evidence.
5. Quiz attempts are still not idempotent.
6. ProductCoherence panels compile but are still not routed into the app shell.
7. Expensive AI/code/audio/source/draft endpoints still lack broad rate limiting.
8. Chat stream cancellation and post-processing still need hardening.

## Active P1 Findings

### 1. Refresh Token JSON + localStorage Exposure

Status: ACTIVE

- Evidence: `AuthController` sets an HttpOnly refresh cookie but still returns refresh tokens in response JSON at `Orka.API/Controllers/AuthController.cs:87`, `:156`. `AuthResponse` exposes both `refreshToken` and `refresh_token` at `Orka.Core/DTOs/Auth/AuthResponse.cs:16`. Frontend stores `orka_refresh` in localStorage at `Orka-Front/src/services/api.ts:76`, `:151`, and `App.tsx:34`.
- Impact: XSS/browser-extension compromise can steal long-lived refresh tokens despite HttpOnly cookie support.
- Fix: remove refresh token fields from response body; refresh only via cookie/credentials; remove `orka_refresh` localStorage path.
- Test: login/register/refresh responses contain no `refreshToken`/`refresh_token`; refresh still works from cookie; `localStorage.getItem("orka_refresh") === null`.

### 2. Non-Admin Global Curriculum Mutation

Status: ACTIVE

- Evidence: `CurriculumController` uses `[Authorize]` only at `Orka.API/Controllers/CurriculumController.cs:9`; write endpoints at `:52`, `:110`, `:137` do not require Admin. `CurriculumSourceRegistryService` loads mutable rows with `(OwnerUserId == null || OwnerUserId == userId)` at `Orka.Infrastructure/Services/CurriculumSourceRegistryService.cs:191`, `:423`, `:487`.
- Impact: any authenticated user can mutate shared/system curriculum or registry metadata.
- Fix: system/global writes require Admin, or service must require `OwnerUserId == userId` and fork copies for user edits.
- Test: non-admin system version/source mutation returns 403/404; admin succeeds.

### 3. Quiz Ownership Boundary

Status: ACTIVE

- Evidence: `/api/quiz/generate` reads topic by `FindAsync(topicId)` at `Orka.API/Controllers/QuizController.cs:50` without `UserId` ownership. `/api/quiz/attempt` calls recorder directly at `QuizController.cs:91`; recorder persists caller user id with client-provided `TopicId`/`SessionId` at `Orka.Infrastructure/Services/QuizAttemptRecorder.cs:91`.
- Impact: a user with another user's topic/session GUID can generate quiz content or pollute their own learning state with foreign references.
- Fix: use `ResourceOwnershipGuard.OptionalTopicBelongsToUserAsync` and `OptionalSessionBelongsToUserAsync`; generate should query by `Id && UserId`.
- Test: cross-user generate/attempt with another user's topic/session returns 404/403.

### 4. Generated Quiz Trusted Evidence + Answer-Key Leakage

Status: ACTIVE

- Evidence: `/api/quiz/generate` returns LLM JSON directly and does not create `QuizRun`/`AssessmentItem`: `Orka.API/Controllers/QuizController.cs:45`, `DeepPlanAgent.cs:1004`, `DiagnosticQuizQualityGate.cs:28`. It does not sanitize learner payload for `isCorrect`, `correctAnswer`, `answerKey`, `explanation`.
- Impact: generated quizzes can expose answer-key fields and correct answers may not become trusted KT/mastery/learning-signal evidence.
- Fix: persist generated quiz as durable `QuizRun` + `AssessmentItem.GeneratedQuestionJson`; return only learner-safe DTO with item ids/options.
- Test: generated quiz response contains no answer-key fields; submit validates via server-side key and writes KT/mastery/signal rows.

### 5. Quiz Attempt Idempotency

Status: ACTIVE

- Evidence: recorder creates a new `QuizAttempt` on each submit at `QuizAttemptRecorder.cs:54`, `:115`; existing indexes at `OrkaDbContext.cs:538` are not unique. `QuizRun.CorrectCount` can increment on duplicate correct submit at `QuizAttemptRecorder.cs:313`.
- Impact: double-clicks/retries can inflate attempt counts, mastery, learning signals, SRS and calibration.
- Fix: add `ClientAttemptKey` or unique durable key such as `(UserId, QuizRunId, AssessmentItemId)`; duplicate submit returns existing result and skips side effects.
- Test: same `quizRunId + assessmentItemId` submitted twice creates one attempt and one learning update.

### 6. Provider Max Token Enforcement

Status: ACTIVE

- Evidence: `AIAgentFactory` reads role budget max tokens at `Orka.Infrastructure/Services/AIAgentFactory.cs:285`, but provider calls do not enforce it at `:318`. `GitHubModelsService` hardcodes `MaxTokens = 4096` at `GitHubModelsService.cs:82`; OpenAI-compatible, Groq and Gemini paths do not put role max tokens into provider payloads (`OpenAICompatibleService.cs:55`, `GroqService.cs:188`, `GeminiService.cs:125`).
- Impact: cost budgets estimate one limit while providers may generate longer output, increasing cost/latency.
- Fix: pass `MaxOutputTokens` to every provider request payload.
- Test: fake HTTP handler asserts `max_tokens`/`maxOutputTokens`/`MaxTokens` equals role budget.

### 7. Synchronous Chat Post-Processing

Status: ACTIVE

- Evidence: a scheduling API exists, but `AgentOrchestratorService.ScheduleTurnPostProcessingAsync` still calls `ProcessSynchronouslyAsync` at `Orka.Infrastructure/Services/AgentOrchestratorService.cs:565`. It is used in stream and non-stream paths at `:391`, `:752`.
- Impact: evaluator/analyzer/wiki/progression latency can block chat response or stream completion.
- Fix: queue non-critical post-processing; keep only response-critical metadata inline.
- Test: slow fake postprocessor does not delay `/api/chat/message` or `/api/chat/stream` completion.

### 8. Chat Stream Cancellation / Timeout Propagation

Status: CHANGED BUT ACTIVE

- Evidence: provider has a timeout at `AIAgentFactory.cs:179`, but `ChatController` does not pass `HttpContext.RequestAborted` into orchestrator stream at `Orka.API/Controllers/ChatController.cs:186`; `IAgents.cs:13` lacks cancellation token. `TutorAgent` uses `CancellationToken.None` after stream work at `TutorAgent.cs:542`, `:549`, `:555`.
- Impact: client disconnect can leave provider/tool/evaluator work running.
- Fix: add cancellation token through controller -> orchestrator -> tutor/provider stream; remove `CancellationToken.None` from follow-up work or queue it.
- Test: aborting stream cancels fake provider/tool and does not run post-answer side effects.

### 9. ProductCoherence Panels Are Dead UI

Status: ACTIVE

- Evidence: panels exist in `Orka-Front/src/components/ProductCoherencePanels.tsx:420`, `:557`, `:674`, `:759`, `:809`, `:870`. `Home.tsx` does not import them; `VALID_VIEWS` at `Orka-Front/src/pages/Home.tsx:33` excludes their view ids; `renderMain` falls unknown views back to ChatPanel at `Home.tsx:430`.
- Impact: Mission Control, Study Room, Source/Wiki Pro, Notebook Pro, Exam War Room and Code IDE panels are implemented but not reachable from the main app shell.
- Fix: import panels in `Home`, add `VALID_VIEWS` ids, and add `renderMain` cases matching `toView()`.
- Test: each ProductCoherence target renders the intended panel in browser smoke.

### 10. Expensive Endpoint Rate Limiting Is Too Narrow

Status: ACTIVE

- Evidence: only `ChatLimiter` is configured/applied (`Program.cs:687`, `ChatController.cs:24`). Expensive endpoints such as audio generation and question draft preview are `[Authorize]` only (`AudioController.cs:8`, `:22`, `QuestionDraftGenerationController.cs:9`, `:23`).
- Impact: authenticated users can drive LLM/audio/code/source/draft costs without equivalent throttling.
- Fix: add per-user policies such as `AiGenerationLimiter`, `UploadEmbeddingLimiter`, `CodeRunLimiter`, `ResearchLimiter`.
- Test: burst each expensive endpoint and assert 429 after quota.

## Active P2 Findings

### Soft Delete Is Still Manual

Status: ACTIVE

- Evidence: `OrkaDbContext` has `IsDeleted` indexes but no global `HasQueryFilter`; Dashboard counts for `WikiPages/WikiBlocks` do not filter deleted rows at `DashboardController.cs:582`, `:585`, `:872`.
- Impact: one missed manual predicate can leak deleted data or skew dashboard counts.
- Fix: global query filters or scoped repository/spec helpers.
- Test: deleted wiki page/block does not affect dashboard counts or public reads.

### Raw Learner/Source Content Persistence

Status: ACTIVE

- Evidence: raw chat messages persist in `Message.Content` (`Message.cs:15`, `AgentOrchestratorService.cs:172`); extracted document chunks persist in `SourceChunk.Text` (`SourceChunk.cs:12`, `LearningSourceService.cs:1226`).
- Impact: PII/secrets in prompts/uploads can remain in SQL and flow into retrieval/prompt artifacts.
- Fix: retention/encryption/minimization policy; PII detector for secondary artifacts; stronger delete/anonymize tests.
- Test: email/phone/token/path upload + account delete leaves no DB/Redis residue.

### Audio Overview Media Auth

Status: ACTIVE

- Evidence: frontend uses direct `<audio src={AudioOverviewAPI.streamUrl(...)}` in `NotebookStudioPanel.tsx:584`; `streamUrl` only builds a URL at `api.ts:650`. Backend `/api/audio` is `[Authorize]` (`AudioController.cs:8`), and JWT expects Bearer header (`Program.cs:640`).
- Impact: cross-origin deployments can show ready audio but playback fails because media tag cannot add Bearer token.
- Fix: fetch audio with authenticated fetch/axios blob and attach object URL, or use signed URLs.
- Test: expired access token + valid refresh -> audio blob fetch refreshes and plays.

### Wiki SSE Auth Refresh / Event Parsing

Status: ACTIVE / CHANGED

- Evidence: `WikiDrawer` uses raw `fetch` and one-shot `storage.getToken()` at `WikiDrawer.tsx:191`, bypassing `authenticatedFetch` retry at `api.ts:175`. Parser may append JSON strings to chat if event lacks `content` at `WikiDrawer.tsx:221`.
- Impact: Wiki chat fails after access token expiry; metadata/citation events can appear as raw JSON text.
- Fix: use `authenticatedFetch`; switch on event type and render only token content.
- Test: expired access token with valid refresh succeeds; metadata/citation events are not displayed as chat text.

### Mobile WikiDrawer Overflow

Status: ACTIVE

- Evidence: drawer default width is `520px` at `WikiDrawer.tsx:60`; inline width is set at `WikiDrawer.tsx:264`; Courses overlay is fixed right at `Courses.tsx:107`.
- Impact: 360-390px mobile viewports likely overflow horizontally.
- Fix: clamp to `min(520px, 100vw)` or use full-screen mobile sheet.
- Test: browser screenshots at 360x740 and 390x844 with no horizontal scroll.

### Disabled Worker "Proof" Execution

Status: CHANGED BUT STILL ACTIVE AS SEMANTIC RISK

- Evidence: SRS/Daily host still calls `RunDisabledProofAsync` when disabled (`ScheduledWorkerHosts.cs:27`, `:81`). Services appear to early-return and only record disabled events (`SrsReminderWorkerService.cs:41`, `DailyChallengeWorkerService.cs:41`).
- Impact: not likely to send real notifications now, but disabled config still invokes worker service once; semantics are surprising.
- Fix: host should not call service when disabled unless explicitly requested for tests.
- Test: with worker flags false, no worker service calls or side effects occur.

### Release Gate Gaps

Status: ACTIVE

- Evidence: `.github/workflows/backend-release.yml` path filters exclude `Orka-Front/**` (`:5`) and job is backend-focused (`:80`). `scripts/quick-all.ps1` runs frontend `quick:smoke` and `build`, but not `typecheck` (`quick-all.ps1:10`, `:17`). Static smoke scripts are string-presence checks, not browser behavior.
- Impact: frontend and browser regressions can pass release checks.
- Fix: add frontend CI with `npm ci`, `typecheck`, `quick:smoke`, `build`, browser smoke; add critical API suite/coverage/migration apply gates.
- Test/gate: PR touching frontend must run browser-backed smoke; backend release gate runs critical full tests or explicit `ReleaseCritical` trait.

## Changed / Closed Findings

- Frontend compile contract: CLOSED. `npm run typecheck`, `quick:smoke`, and `build` pass.
- ProductCoherence backend/API contract: CHANGED/CLOSED. Services and endpoints exist (`LearningController`, `ClassroomController`, `NotebookStudioController`, `CodeController`, `SourcesController`, `CentralExamsController`), but frontend routing remains active bug.
- Chat SSE JSON/content handling in main chat: mostly CLOSED/CHANGED. Frontend now parses JSON events and does not render unparsable JSON as plain text, but formal SSE schema/done event remains a P2 hardening item.
- Auth `X-Forwarded-For` spoof in `AuthController`: mostly CLOSED. Production path uses remote IP; forwarded headers middleware exists. Deployment should still configure trusted proxies/networks.
- Redis production safety: CLOSED for protected environments. Startup rejects localhost Redis and requires Redis auth limiter/fail-closed policy.
- Refresh token raw DB storage: CLOSED. DB stores HMAC hashes and rotates tokens; browser exposure remains active.
- Central exam submit idempotency: CLOSED. Snapshot-key grading/idempotency tests exist.
- Question bank direct `IsCorrect` leak: CHANGED/CLOSED for direct option fields; residual risk remains for answer-key-like data inside raw `ContentJson`/stimulus JSON.
- Redis gold example cross-tenant key: CLOSED. Key is user-scoped and scrub-before-truncate is present.
- Startup auto-migration in protected envs: CLOSED/CHANGED. Protected env blocks unsafe auto-migrate; CI migration apply proof is still missing.

## Recommended Fix Order

1. Remove refresh token JSON/localStorage path.
2. Lock system curriculum/global registry writes behind Admin or user-owned copies.
3. Add quiz topic/session ownership guards.
4. Make generated quizzes durable and learner-safe.
5. Add quiz attempt idempotency.
6. Wire ProductCoherence panels into `Home` and add browser smoke.
7. Add rate limits for expensive non-chat endpoints.
8. Propagate stream cancellation and move post-processing to queue.
9. Add frontend CI, browser smoke, migration apply proof, report commit metadata.

