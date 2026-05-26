# Orka Comprehensive Issue Audit

Date: 2026-05-25
Mode: read-only audit, no product code changes.
Workspace: `D:\Orka`

This report consolidates multi-agent scans plus local verification. It is meant to answer: what is actually broken, what is risk, what is only a coverage gap, and what previous audit claims are now closed in the current dirty snapshot.

## Method

Scans used:
- Backend/AI orchestration scan: chat stream/non-stream, provider routing, Gemini, Korteks, telemetry.
- Pedagogical/algorithm scan: assessment calibration, knowledge tracing, plan sequencing, quiz recorder, long-term adaptive learning.
- Data/security/infra scan: SQL scoping, Redis, PII persistence, auth/rate limit, health and worker surfaces.
- Frontend/API contract scan: app routes, API wrappers, SSE parsing, Wiki/source UI, audio, smoke scripts.
- Test/release scan: CI gates, quick scripts, omitted test suites, lifetest/healthcheck reports.
- Local verification: `rg` source checks and `npm run typecheck`.

Important context:
- The worktree is dirty. This audit describes the current snapshot, not clean `main`.
- `npm run typecheck` in `D:\Orka\Orka-Front` fails. That makes the frontend API wrapper issue confirmed, not speculative.
- No backend test suite was run in this pass.

Severity scale:
- `P0`: currently breaks app/build/core user flow.
- `P1`: high-impact bug/security/data integrity issue likely to affect users or production confidence.
- `P2`: important risk, edge-case bug, or production hardening gap.
- `P3`: cleanup/quality concern.
- `Coverage gap`: tests/gates do not protect an important behavior.

## Executive Summary

The system has strong architecture and a serious test inventory, but the current snapshot is not release-stable:

1. Frontend Product Coherence panels call API methods that do not exist in `api.ts`; `npm run typecheck` fails.
2. Long-term adaptive learning mixes `0-100` mastery score with `0-1` probability.
3. Question bank authoring DTO appears exposed through authenticated question endpoints with `IsCorrect`.
4. Chat stream and non-stream parity still has gaps around quiz, metadata, Wiki capture and SSE framing.
5. Gemini/Korteks provider contract is inconsistent with telemetry, model routing, max token enforcement and fallback.
6. Redis/security policy needs hardening: local Redis can pass config, X-Forwarded-For can spoof auth limiter partitions, raw examples/profile notes are cached.
7. CI/quick gates exclude several critical API test families; smoke tests are often string-presence checks rather than runtime contract tests.

The old headline claims are mixed:
- Assessment discrimination inversion is closed in this snapshot.
- Linear forgetting is closed in this snapshot.
- AdaptiveStudyPlanner prerequisite ordering is improved/closed.
- New subtler pedagogy/data bugs remain.

## P0 Findings

### P0-1: Frontend Product Coherence API wrapper mismatch breaks typecheck and likely runtime

Evidence:
- `ProductCoherencePanels.tsx` calls missing methods:
  - `LearningAPI.getMissionControl`, `getStudyCoach`, `getOrkaState`
  - `ClassroomAPI.getStudyRoom`, `startStudyRoom`, `submitStudyRoomCheckpoint`
  - `CentralExamsAPI.getWarRoom`
  - `SourcesAPI.getWikiPro`
  - `NotebookStudioAPI.getPro`
  - `CodeAPI.getLearningIde`
- `services/api.ts` contains the backend routes for some older APIs, but these exact wrapper functions are absent.
- Local `npm run typecheck` failed with `TS2339` errors at `ProductCoherencePanels.tsx` lines 436, 562, 571, 582, 680, 765, 815, 891.

Primary files:
- `D:/Orka/Orka-Front/src/components/ProductCoherencePanels.tsx`
- `D:/Orka/Orka-Front/src/services/api.ts`
- Backend routes exist in `LearningController`, `ClassroomController`, `SourcesController`, `NotebookStudioController`, `CentralExamsController`, `CodeController`.

User impact:
- `/app` home and product navigation panels can fail with `... is not a function`.
- The current frontend cannot pass TypeScript validation.

Fix:
- Add the missing API wrappers and DTO return types in `api.ts`, or change panels to use existing wrappers.
- Add `npm run typecheck` to hard gate before any frontend merge.

Test/gate:
- `npm run typecheck`
- Playwright app shell smoke: open `/app`, click `home`, `study-room`, `sources-wiki`, `notebook`, `exams`, `code`, assert no console/page errors.

## P1 Findings

### P1-1: Mastery scale mixes `0-100` and `0-1`

Evidence:
- `KnowledgeTracingService` writes `ConceptMastery.MasteryScore = state.MasteryProbability * 100`.
- `LongTermAdaptiveLearningService` later assigns `concept.MasteryProbability = MaxNullable(..., mastery.MasteryScore)`.
- Stability thresholds use probability-style values like `0.75`.

Primary files:
- `D:/Orka/Orka.Infrastructure/Services/KnowledgeTracingService.cs`
- `D:/Orka/Orka.Infrastructure/Services/LongTermAdaptiveLearningService.cs`

Impact:
- A score like `40` can be interpreted as `40 >= 0.75`, making weak concepts look stable.
- Review/remediation pressure can be suppressed.

Fix:
- Normalize `MasteryScore / 100m` before assigning probability fields.
- Rename DTO fields to distinguish `scorePct` vs `probability`.
- Clamp probabilities to `[0,1]`.

Test:
- Seed only `ConceptMastery(MasteryScore=40, Confidence=.8)`.
- Assert long-term profile `MasteryProbability <= 1` and concept remains weak/remediation candidate.

### P1-2: QuestionBank endpoints return answer keys in visible DTO

Evidence:
- `QuestionsController` exposes `[Authorize] GET /api/questions` and `GET /api/questions/{id}` returning `QuestionItemDto`.
- `QuestionBankService.ToDto` maps each option with `IsCorrect = o.IsCorrect`.
- Explanation/rubric fields also travel in the authoring DTO.

Primary files:
- `D:/Orka/Orka.API/Controllers/QuestionsController.cs`
- `D:/Orka/Orka.Infrastructure/Services/QuestionBankService.cs`

Impact:
- If these endpoints are reachable from learner/practice surfaces, pre-submit answer keys leak.
- Assessment validity is degraded.

Fix:
- Split authoring/admin DTOs from learner/practice DTOs.
- Learner DTOs must strip `IsCorrect`, answer explanations and rubric keys until after submit.

Test:
- Authenticated non-author learner fetches system/published question.
- Response must not contain `isCorrect:true`, answer key, explanation, rubric answer.

### P1-3: Organic quiz route can enter `QuizPending` without generating quiz content

Evidence:
- In `AgentOrchestratorService`, `QUIZ` route sets `session.CurrentState = QuizPending`.
- The later quiz mode returns `session.PendingQuiz`, but the route does not guarantee `PendingQuiz` was created.

Primary file:
- `D:/Orka/Orka.Infrastructure/Services/AgentOrchestratorService.cs`

Impact:
- User asks for a quiz, confirms, then receives an empty/null quiz or a misleading "prepared quiz" response.

Fix:
- Generate and persist `PendingQuiz` when entering `QuizPending`, or generate it on confirmation before responding.

Test:
- Stream and non-stream: "quiz isterim" -> "evet".
- Assert `PendingQuiz != null`, response `MessageType=quiz`, and attempts can be recorded.

### P1-4: Stream sync branches lose metadata and Wiki capture parity

Evidence:
- Stream branches for baseline quiz, quiz mode, awaiting choice, quiz pending and plan-style sync responses yield `syncResponse` and exit.
- Normal stream branch later runs `BuildTutorMetadataAsync` and `AppendTutorTurnToWikiAsync`.
- Non-stream path attaches metadata and Wiki capture more consistently.

Primary file:
- `D:/Orka/Orka.Infrastructure/Services/AgentOrchestratorService.cs`

Impact:
- Stream users can miss metadata chips, Tutor trace updates, Wiki updated signals and learning traces.

Fix:
- Before stream sync branch exits, emit the same structured `metadata` event and apply the same Wiki capture decision where appropriate.

Test:
- Extend `ChatParityTests` for stream plan, stream quiz answer and stream quiz pending.
- Compare non-stream metadata vs stream metadata event.

### P1-5: Gemini model routing and telemetry can report the wrong model

Evidence:
- `AIAgentFactory.ResolveFallbackModel("Gemini")` looks for `AI:Gemini:Model` or `AI:Gemini:Agents:{role}:Model`.
- `GeminiService` actually uses `ModelTutor`, `ModelQuiz`, `ModelDeepPlan`.
- Factory telemetry records `attempt.Model`, while Gemini call uses `GenerateSmartAsync` and selects model internally.

Primary files:
- `D:/Orka/Orka.Infrastructure/Services/AIAgentFactory.cs`
- `D:/Orka/Orka.Infrastructure/Services/GeminiService.cs`

Impact:
- Cost/provider dashboard can say Gemini used a different model than the request endpoint actually used.
- Budgeting and regression diagnosis become unreliable.

Fix:
- Add a role-aware Gemini model resolver or call `GenerateWithModelAsync`.
- Record actual provider/model returned from the provider call.

Test:
- Force stream primary failover to Gemini.
- Assert telemetry model and Gemini endpoint model are both `gemini-*` and match the selected role.

### P1-6: Korteks research route sits outside the common provider fallback/cost contract

Evidence:
- `KorteksAgent` reads provider config but builds Semantic Kernel directly for one provider path.
- It does not use the `IAIAgentFactory` fallback chain, budget checks, circuit breaker or cost telemetry in the same way as chat providers.

Primary file:
- `D:/Orka/Orka.Infrastructure/Services/KorteksAgent.cs`

Impact:
- Research can degrade to fallback/internal knowledge while chat provider stack appears healthy.
- Cost and provider health visibility are incomplete.

Fix:
- Route Korteks LLM calls through a provider attempt chain with budget, circuit breaker and telemetry.

Test:
- First Korteks provider fails; second succeeds.
- Assert `GroundingMode`, provider telemetry and cost records.

### P1-7: Production Redis hardening policy is too weak

Evidence:
- Program fallback: Redis defaults to `localhost:6379,abortConnect=false` if connection string is absent.
- Production safety policy checks presence of `ConnectionStrings:Redis`, but not localhost, TLS, auth/ACL, DB isolation or fail-fast behavior.
- Default config uses local Redis-style values.

Primary files:
- `D:/Orka/Orka.API/Program.cs`
- `D:/Orka/Orka.API/Services/ProductionSafetyStartupPolicy.cs`
- `D:/Orka/Orka.API/appsettings.json`

Impact:
- Staging/production can accidentally use local, unauthenticated or shared Redis.
- Auth rate limit, prompt/cache, student profile and runtime memory share that risk.

Fix:
- In protected envs reject `localhost/127.0.0.1`, require `ssl=true`, credential/ACL, explicit DB/prefix and controlled fail-fast.

Test:
- ProductionSafety tests for local Redis rejected, TLS/auth required, managed Redis accepted.

### P1-8: Auth rate limit can be bypassed by spoofed `X-Forwarded-For`

Evidence:
- `AuthController.GetClientPartition()` reads first `X-Forwarded-For` value directly.
- No `UseForwardedHeaders`, `KnownProxies`, or `KnownNetworks` configuration was found.

Primary files:
- `D:/Orka/Orka.API/Controllers/AuthController.cs`
- `D:/Orka/Orka.API/Program.cs`

Impact:
- Attacker can rotate `X-Forwarded-For` per login/register attempt and bypass Redis auth limiter partitions.

Fix:
- Trust forwarded headers only behind known proxies.
- Otherwise ignore raw XFF and use normalized `RemoteIpAddress`.

Test:
- Same socket IP, different XFF headers, repeated failed login attempts must count against one partition.

### P1-9: Redis stores raw user/agent content and profile notes

Evidence:
- `SaveGoldExampleAsync` stores `userMessage` and `agentResponse` after truncation, not full sanitization.
- `RecordStudentProfileAsync` stores weaknesses as raw strings.
- Evaluator can save high-scoring Tutor dialogue as gold examples.

Primary files:
- `D:/Orka/Orka.Infrastructure/Services/RedisMemoryService.cs`
- `D:/Orka/Orka.Infrastructure/Services/EvaluatorAgent.cs`

Impact:
- PII, private source fragments or model output can live in Redis up to 30 days and later become few-shot prompt context.

Fix:
- Store only safe summaries or redacted content.
- Scrub email, phone, path, secret, raw prompt/source/tool markers.
- Scope gold examples by `userId + topicId` or make them opt-in.

Test:
- Gold example and student profile payloads with email, phone, path, `rawPrompt`, `rawSourceChunk`, secret markers must not persist in Redis.

### P1-10: Frontend SSE parser lacks persistent frame buffer

Evidence:
- `ChatPanel` parses the stream by splitting each network chunk on `\n`.
- JSON event split across TCP chunks can fail parsing and fall back into visible assistant text.

Primary file:
- `D:/Orka/Orka-Front/src/components/ChatPanel.tsx`

Impact:
- User may see partial JSON/event payloads.
- Metadata/artifact events can be lost.

Fix:
- Keep persistent buffer and parse SSE frames by `\n\n`.
- Treat `[DONE]` or structured `done` as terminal.

Test:
- Mock an SSE JSON event split into two chunks; UI must render only the token/content, not JSON.

### P1-11: Wiki source answer renders raw `answer` directly

Evidence:
- `WikiMainPanel` sets `sourceAnswer` from `SourceQuestionResponseDto.answer`.
- It renders with `RichMarkdown`.
- DTO has safety signal like `rawPayloadRemoved`, and thread DTO has `safeAnswerSummary`, but render path does not force safe field.

Primary files:
- `D:/Orka/Orka-Front/src/components/WikiMainPanel.tsx`
- `D:/Orka/Orka-Front/src/lib/types.ts`

Impact:
- If backend returns raw source/provider text by mistake, frontend will display it.

Fix:
- Render only `safeAnswerSummary` or a dedicated safe field.
- If `safety.rawPayloadRemoved !== true`, show safe warning instead of raw answer.

Test:
- Mock answer containing `rawSourceChunk` and `rawProviderPayload`; UI must not render it.

### P1-12: Protected route does not bootstrap refresh session

Evidence:
- `ProtectedRoute` only checks `localStorage` token presence.
- API layer has refresh flow, but route guard redirects before trying refresh.

Primary files:
- `D:/Orka/Orka-Front/src/App.tsx`
- `D:/Orka/Orka-Front/src/services/api.ts`

Impact:
- Valid refresh cookie but missing/expired access token can cause unnecessary logout.

Fix:
- Add auth bootstrap state: if access token missing, attempt refresh before redirecting.

Test:
- Delete `orka_token`, keep valid refresh cookie; `/app` should recover without login redirect.

## P2 Findings

### P2-1: Long-term forgetting is applied on update, not on read/selection

Evidence:
- Decay is applied inside `KnowledgeTracingService.UpdateFromAttemptAsync`.
- Adaptive selector uses stored `MasteryProbability` and stop rules without applying decay-on-read.

Impact:
- Old high-confidence mastery can remain "enough evidence" after long inactivity.

Fix:
- Central `ApplyDecayOnRead` helper used by adaptive selector, stop rules and long-term profile.
- Include `LastEvidenceAt` age in stop decision.

Test:
- 90-day-old `MasteryProbability=.9, Confidence=.8, EvidenceCount=3` should not immediately stop assessment.

### P2-2: PlanSequencing does not perform true topological sort

Evidence:
- `OrderConcepts` ranks prerequisite targets and weak concepts, but does not perform Kahn/DFS topo sort.
- `EvaluatePrerequisiteOrder` can flag violations after sequence generation; it does not repair the sequence.

Primary file:
- `D:/Orka/Orka.Infrastructure/Services/PlanSequencingService.cs`

Impact:
- A weak advanced concept can appear before its prerequisites.

Fix:
- Use prerequisite graph topological sorting; apply weak/remediation priority only among zero-in-degree candidates.

Test:
- A prerequisite B, B prerequisite C, C weak. Expected order: A, B, C.

### P2-3: Exam topic-level evidence can be copied to multiple outcomes

Evidence:
- `ExamLearningProfileService` includes `ExamOutcomeId == null && ExamTopicId == path.Topic.Id` answers in per-outcome evidence.

Impact:
- One generic topic answer can make every outcome under that topic weak or stable.

Fix:
- Treat topic-level evidence as low-confidence topic evidence, not full outcome evidence.

Test:
- Two outcomes under one topic; one topic-level answer should not stabilize/weaken both outcomes equally.

### P2-4: Quiz anti-repeat protects XP but not mastery updates

Evidence:
- `alreadyAwarded` affects XP awarding.
- KT and mastery updates still run for repeated verified attempts.

Primary file:
- `D:/Orka/Orka.Infrastructure/Services/QuizAttemptRecorder.cs`

Impact:
- Student can repeat the same item to inflate mastery/confidence.

Fix:
- Repeated same item/hash should be ignored for mastery or weighted as low-value retrieval repeat.

Test:
- Same `AssessmentItemId` answered correctly twice: no second XP and no second KT evidence inflation.

### P2-5: Recorder service can trust client correctness if called outside controller

Evidence:
- Controller strips `IsCorrect` and explanation for public API, which is good.
- Service-level `RecordAsync` still allows verified-looking correctness when no durable item is present through legacy/direct call paths.

Impact:
- Internal integrations can accidentally write client-supplied correctness into learning state.

Fix:
- Move "durable answer key required" invariant into recorder service.

Test:
- Direct service call with `AssessmentItemId=null`, `IsCorrect=true` must not update KT/mastery.

### P2-6: Plan diagnostic raw intent is stored in Redis state

Evidence:
- `RawStudyRequest` is saved into `PlanDiagnosticStateDto`.
- `RedisPlanDiagnosticStateStore` serializes the full state.

Impact:
- User may type PII/path/secrets in the raw request; it persists in Redis diagnostic cache.

Fix:
- Store approved/sanitized intent only.
- If raw is needed, store hash or short redacted text with short TTL.

Test:
- Raw request with `apiKey=...` and `C:\Users\...` must not appear in Redis state or response DTOs.

### P2-7: LLM prompt/context blocks persist in SQL/Redis and can be exposed through DTOs

Evidence:
- Korteks workflow entities store `PromptBlock`, `PlanContextJson`, `TutorContextJson`.
- Synthesis endpoint returns workflow DTO.
- Plan diagnostic Redis state stores compressed research prompt blocks.

Impact:
- Internal prompt rules, source summaries and user intent spread across DB, Redis and API surfaces.

Fix:
- Split internal prompt persistence from public DTO.
- Store prompt hash + safe summary where possible.
- Scrub markers and enforce TTL/cleanup.

Test:
- Synthesis API and Redis diagnostic snapshot must not contain `systemPrompt`, `developerPrompt`, `rawSourceChunk` or internal prompt headers.

### P2-8: Soft-delete/tenant safety is manual, not globally enforced

Evidence:
- `OrkaDbContext` has many `IsDeleted`, `UserId`, `OwnerUserId`, `Visibility` indexes.
- No `HasQueryFilter` found.

Impact:
- New endpoint/worker can omit `!IsDeleted` or ownership filters and leak deleted/cross-tenant content.

Fix:
- Add soft-delete marker interface + query filters, or repository/spec guard for tenant-scoped entities.

Test:
- Static/regression gate for queries touching `WikiBlocks`, `WikiPages`, `LearningSources`, `SourceChunks`.

### P2-9: Dev text-health endpoint can expose cross-tenant raw samples to Admin in production

Evidence:
- `TextHealthController` is Admin-auth but not development-only.
- `TextHealthService` scans multiple text tables without tenant/soft-delete scoping and returns `Sample`.

Impact:
- Prod admin endpoint can surface raw text/PII snippets from user content.

Fix:
- Make endpoint development-only, or add break-glass superadmin, tenant scope, redacted samples and audit logs.

Test:
- Production env `/api/dev/text-health` returns 404/403.
- Dry-run samples are redacted and exclude deleted rows.

### P2-10: Code execution and some Korteks research routes lack general per-user rate limits

Evidence:
- `CodeController.RunCode` is authenticated but no explicit rate limiter found.
- Korteks file research is limited, but sync/stream research routes do not show the same backpressure.

Impact:
- Authenticated user can stress sandbox/provider/CPU cost.

Fix:
- Add per-user token bucket/concurrency policies for code and research.

Test:
- Same user N+1 code/research requests returns 429; different users have separate partitions.

### P2-11: Gemini max token values are not sent to Gemini API

Evidence:
- `DetectTask` returns `maxTokens`.
- Gemini `generationConfig` omits `maxOutputTokens` in non-stream and stream requests.

Impact:
- Quiz/DeepPlan/Tutor response length and cost controls are not enforced at Gemini API level.

Fix:
- Add `maxOutputTokens = maxTokens` to Gemini request body.

Test:
- Fake `HttpMessageHandler` snapshot asserts `generationConfig.maxOutputTokens`.

### P2-12: Gemini parser has no first-class safety/empty-candidate handling

Evidence:
- Parser directly reads `candidates[0].content.parts[0].text`.
- No handling for `promptFeedback.blockReason`, `finishReason=SAFETY`, empty candidates or missing parts.

Impact:
- Safety block or schema variation becomes generic provider failure.

Fix:
- Guard Gemini schema and map safety blocks to explicit user-safe failure/degraded metadata.

Test:
- Fixtures: `promptFeedback.blockReason`, `finishReason=SAFETY`, empty candidates.

### P2-13: SSE contract has no explicit event names or done signal

Evidence:
- `ChatController` writes every chunk as `data: ...`.
- No `event:` names or `[DONE]`/structured done at end.
- Error is plain `[ERROR]`.

Impact:
- Frontend must guess whether each data line is token, JSON event, sentinel or error.

Fix:
- Use structured envelope or SSE `event: token|metadata|error|done`.

Test:
- SSE golden test for token, metadata, error and done event sequence.

### P2-14: Mid-stream provider failure can be recorded as success

Evidence:
- `AIAgentFactory` records success after first stream chunk.
- Later stream failure has no complete/partial telemetry distinction.

Impact:
- User sees partial answer/error while provider dashboard shows success.

Fix:
- Record `stream_completed` success only after enumeration completes; add `partial_failure`.

Test:
- Fake provider yields one chunk then throws; telemetry must record failure/partial.

### P2-15: Cost records are duplicated and role/provider labels are blurry

Evidence:
- Factory writes provider-attempt cost with `MessageId=null`.
- Orchestrator writes message cost with provider `AIAgentFactory`, role `Tutor`, model `GetModel(Tutor)`.
- DeepPlan/Quiz/Grader can be mislabeled at message level.

Impact:
- Dashboard can double-count or misattribute cost.

Fix:
- Separate provider-call telemetry from message billing, and link actual role/provider/model to message when possible.

Test:
- Snapshot cost records for tutor, quiz, plan and fallback.

### P2-16: Frontend localStorage JSON parse can crash runtime paths

Evidence:
- `storage.getUser` and ChatPanel parse `orka_user` without try/catch.

Impact:
- Corrupt localStorage can break chat or plan-ready stream path.

Fix:
- Add `safeJsonParse`; clear corrupt key and fallback to defaults.

Test:
- `localStorage.orka_user = "{"`; plan-ready stream must not crash.

### P2-17: Classroom player ignores ready backend audio on first play

Evidence:
- `audioOverviewJobId` is used in classroom session start payload.
- First `handlePlay` uses browser `speechSynthesis`.
- Backend interaction audio is only used for follow-up answers.

Impact:
- Even ready Edge-TTS audio overview is not used for initial lesson playback.

Fix:
- If `audioOverviewJobId` is ready, play backend `AudioOverviewAPI.streamUrl(jobId)` first; fallback to browser TTS.

Test:
- Ready audio job id -> first play fetches/plays backend audio.

### P2-18: Chat stream catch can leave message in streaming state

Evidence:
- Chat stream catch updates placeholder content but does not set `isStreaming: false`.

Impact:
- Error message can keep spinner/loading state.

Fix:
- Set `isStreaming: false` in catch update.

Test:
- Network error mock for `/api/chat/stream`; spinner closes.

### P2-19: Home view alias behavior is internally inconsistent

Evidence:
- `normalizeView` maps legacy `wiki`, `orkalm`, `sources`, `learning`, `practice`, `central-exams`, `ide`.
- Later `handleViewChange` still has branches that are unreachable after normalization.

Impact:
- Old route intentions silently land on new panels; classic WikiMainPanel/OrkaLM can be bypassed.

Fix:
- Define canonical behavior for each legacy alias and remove unreachable branches.

Test:
- Unit/smoke for `handleViewChange("wiki")`, `"orkalm"`, `"sources"`, `"practice"`.

## Coverage Gaps And Gate Problems

### CG-1: CI/quick backend excludes critical API test families

Evidence:
- `.github/workflows/backend-release.yml` runs `scripts/quick-backend.ps1` and infra unit tests.
- `quick-backend.ps1` uses filters.
- Critical suites like `AgenticSecurityTrustTests`, `WikiGraphContractTests`, `SourceEvidenceLifecycleTests`, `TutorPedagogyPolicyTests`, `QuizAttemptSafetyTests` and many exam/question/content tests are not all in the quick release proof.

Risk:
- Security, raw payload, wiki/source and pedagogy regressions can pass merge gate.

Fix:
- Add full `dotnet test Orka.API.Tests` job or a second critical omitted suites job.

### CG-2: Gemini provider contract is not tested against production `GeminiService`

Evidence:
- `AiReliabilityTests` mostly use fake `IGeminiService`.
- Request body, safety block parsing, empty candidate handling and model routing are not covered on the real service.

Fix:
- Add `GeminiProviderContractTests` with fake `HttpMessageHandler`.

### CG-3: Chat stream research integration is not protected end-to-end

Evidence:
- Stream tests check basic `data:` and postprocessor.
- Korteks endpoint tests are separate.
- No `/api/chat/stream` test proves research intent calls Korteks and injects bounded context.

Fix:
- Fake `IKorteksAgent`; assert stream research route calls Korteks, emits safe context, and does not leak raw source.

### CG-4: Wiki trace PII policy focuses on raw markers, not general PII

Evidence:
- Tests assert `rawProviderPayload`, `rawSourceChunk`, paths and similar markers.
- Email/phone/identity-style values in Wiki trace content need broader tests.

Fix:
- Add PII-seeded Wiki trace tests for email, phone, local path, token-like strings.

### CG-5: Frontend smoke tests are mostly string-presence checks

Evidence:
- `smoke-ui.mjs` and `smoke-endpoints.mjs` check `includes(...)` for symbols/routes.
- They do not mount panels, call wrappers or validate DTO shapes.
- `smoke-security.mjs` includes a corpus but does not push it through sanitizer/render output.

Fix:
- Add Playwright smoke for app shell and route navigation with console/pageerror fail.
- Add lightweight HTTP/schema contract smoke for key endpoints.
- Add sanitizer output checks.

### CG-6: Runtime reports are not tied to current commit

Evidence:
- Latest healthcheck report showed API down (`fetch failed`) and grade F, but this is environment/setup signal.
- Existing lifetest can pass with warnings/skips, including provider disabled.

Fix:
- Store report commit/timestamp and require latest-after-change runtime report.
- Add strict warning mode for release.

## Closed Or Improved Previous Findings

The following prior audit claims are no longer true as originally stated in this snapshot:

- Assessment discrimination inversion: closed. `AssessmentCalibrationServices` now uses `4*p*(1-p)*(1-skip)` rather than `abs(p-.5)`.
- Global middle difficulty bias: mostly closed. Item quality uses `mastery` in `difficultyFit`, not a fixed global `.50`.
- Linear forgetting: closed. `KnowledgeTracingService` uses exponential decay.
- AdaptiveStudyPlanner prerequisite ignorance: closed for `AdaptiveStudyPlannerService`; it now loads `ConceptRelations` and topologically sorts. Separate `PlanSequencingService` still has a topo risk.
- Public quiz attempt client correctness: controller-level strip is improved; service-level invariant still needs hardening.
- Health detail leak: improved; production/staging readiness response hides details.
- Auth Redis limiter fail-closed: improved in protected env policy.
- Wiki raw marker sanitization: improved through `WikiLearningTraceWriter` marker/path redaction and AgentOrchestrator basic email/phone scrub.
- Source soft-delete main flow: improved; source delete marks source/chunks deleted and page chunk reading filters `!IsDeleted`.

Remaining nuance:
- The new discrimination formula is a variance proxy, not true point-biserial discrimination. Random 50/50 items can still look strong without ability correlation.
- Exponential decay exists, but decay-on-read is missing in selection/stop rules.
- Planner topo exists in one service, but `PlanSequencingService` has a separate ordering risk.

## Recommended Fix Order

1. Fix P0 frontend API wrapper/typecheck failure.
2. Fix P1 mastery scale normalization.
3. Split QuestionBank learner/admin DTOs to stop answer key exposure.
4. Fix auth rate-limit XFF spoof and Redis production hardening.
5. Fix chat stream quiz/metadata/Wiki parity and SSE parser buffering.
6. Fix Gemini model routing, max tokens and safety parser.
7. Bring Korteks research under provider fallback/cost telemetry.
8. Redact Redis gold examples/profile notes and diagnostic raw intent.
9. Add full/critical test gates and Playwright app shell smoke.

## Minimum Verification After Fixes

Frontend:

```powershell
cd D:\Orka\Orka-Front
npm run typecheck
npm run smoke:ui
npm run smoke:contracts
npm run smoke:security
npm run build
```

Backend critical tests:

```powershell
cd D:\Orka
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter "ChatParityTests|AiReliabilityTests|AgenticSecurityTrustTests|WikiGraphContractTests|SourceEvidenceLifecycleTests|QuizAttemptSafetyTests|AssessmentQualityMisconceptionTests|PlanQualitySequencingTests" --no-restore --verbosity minimal
dotnet test .\Orka.Infrastructure.UnitTests\Orka.Infrastructure.UnitTests.csproj --no-restore --verbosity minimal
```

Release gate:

```powershell
.\scripts\quick-backend.ps1
.\scripts\quick-coordination.ps1
```

Runtime:

```powershell
node scripts/healthcheck.mjs --base-url=http://localhost:5065 --quick
node scripts/real-user-lifetest.mjs --strict-warnings
```

Gemini-specific:
- Add fake-HTTP `GeminiProviderContractTests` before relying on live provider tests.
- Live Gemini smoke should be opt-in and not required for provider-free CI.

## Final Assessment

Current snapshot is not production-ready because the frontend does not typecheck and several P1 backend/data risks remain. The codebase is not "bad"; it has a lot of serious guardrails. The issue is that the guardrails are uneven: some mature areas have deep tests, while new Product Coherence, Gemini provider, stream parity and Redis/privacy paths are ahead of the gates that should protect them.

The safest path is not another broad refactor. Fix the P0/P1 list in small slices, attach the missing tests to each slice, then run a full API/infra/frontend gate before asking Gemini or another agent to perform larger changes.
