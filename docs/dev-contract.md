# Orka Dev Contract

This document is the canonical local development and regression contract.

## Canonical URLs

- Backend API: `http://localhost:5065`
- Frontend dev server: `http://localhost:3000`
- Runtime/API smoke env var: `ORKA_API_URL`
- Frontend proxy env var: `VITE_API_PROXY_TARGET`

`5101` is a legacy audit/runtime port only. Do not use it as an active default
in scripts, contract tests, or new docs.

## Start Commands

```powershell
# Backend, SQL/local config
powershell -ExecutionPolicy Bypass -File scripts\start-api.ps1

# Backend, isolated in-memory smoke mode
powershell -ExecutionPolicy Bypass -File scripts\start-api.ps1 -InMemoryDatabase

# Frontend
powershell -ExecutionPolicy Bypass -File scripts\start-front.ps1
```

The Vite dev server uses port `3000` with `strictPort=true`; if the port is
busy, startup should fail visibly instead of silently moving to another port.

## Regression Baseline

Use this before and after stabilization or backend contract changes:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\quick-backend.ps1
```

The backend quick baseline covers auth token hardening, public security,
request-boundary guards, migration policy, logging/error leakage hardening,
health/swagger smoke, endpoint bridge smoke, source guards, runtime telemetry,
tool capability contracts, and auth-filtered tests.

Production logging must use masked references for learner/user/topic/session/
message/source/cache identifiers. Use `LogPrivacyGuard.SafeId` or
`LogPrivacyGuard.SafeTextRef` in backend logs instead of raw GUIDs, Redis keys,
local paths, prompt text, provider bodies, source chunks, tool payloads, answer
keys, owner ids, unsafe user ids, or exception stack traces.

The same quick baseline starts with the provider-free backend lifetest release
proof (`BackendLifeTests|PedagogicalReleaseClosureTests`). Test-host logging is
filtered only for known noisy categories; backend warnings/errors must remain
visible.

System Closure gates before frontend baseline:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\quick-coordination.ps1
powershell -ExecutionPolicy Bypass -File scripts\quick-backend.ps1
cd Orka-Front
npm run typecheck
npm run quick:smoke
cd ..
git diff --check
```

The deterministic quick line must remain external-network-free. Real provider,
Wikipedia/Wikidata, or other public HTTP checks stay behind explicit opt-in
environment flags and outside `quick-backend.ps1` / `quick-coordination.ps1`.

## Learning OS Proof Boundary

The deterministic release proof must keep the full learning loop visible:
approved intent, diagnostic quiz, wrong or blank answer, remediation seed,
Tutor next action, plan action, Wiki trace, Study Room state, and frontend
smoke coverage. Additive changes to these surfaces should prefer existing
assessment, question bank, Wiki, Tutor, and Study Room contracts instead of
creating parallel stores.

Audio classroom scope is `audio overview + TTS + typed follow-up question`.
Do not present it as live microphone, speech-to-speech, OpenAI Realtime, or an
institutional teacher/classroom workflow unless that becomes a separate
approved product phase.

`DataLifecycleTests` in this baseline require relational SQL Server coverage.
Use `(localdb)\OrkaLocalDB` locally or set
`ORKA_LIFECYCLE_SQLSERVER_BASE_CONNECTION` in CI. These tests should fail
visibly when SQL Server is not provisioned; do not silently skip them.

## GitHub CI Backend Gate

`.github/workflows/backend-release.yml` mirrors the backend release proof for
GitHub Actions. It runs on `windows-latest`, prepares SQL Server LocalDB, runs
`scripts\quick-backend.ps1`, runs `Orka.Infrastructure.UnitTests`, and finishes
with `git diff --check`.

The CI gate must stay provider-free. Do not add real provider secrets,
`ORKA_RUN_EXTERNAL_PROVIDER_TESTS`, or paid provider smoke checks to this
workflow without an explicit separate release decision.

## Backend Production Readiness

Backend production-readiness work keeps two proof lines separate:

- Deterministic release proof: `scripts\quick-backend.ps1`,
  `scripts\quick-coordination.ps1`, full API tests, Infrastructure unit tests,
  and GitHub backend release CI. This line is provider-free and must not need
  live AI keys.
- Optional live/staging proof: explicit provider smoke only after approval,
  with harmless synthetic input, no secret printing, no source chunks, and no
  load testing against paid providers.

Production/staging startup must keep protected gates enabled: explicit DB and
Redis configuration, explicit CORS/AllowedHosts, secure refresh-cookie settings,
Redis auth rate limiting, applied-migration readiness, and global/user AI cost
or token limits. Audio retention and Redis stream maintenance must stay bounded
and aggregate-based; do not reintroduce full audio payload scans in readiness
summaries.

Code Learning IDE Redis handoff context is summary-only. Redis may store bounded
status, phase, sanitized compile/runtime error, and safe Tutor summary, but must
not cache raw student code, stdout, stderr, tool payloads, stack traces, local
paths, secrets, or local runtime diagnostics.

Use this when frontend dependencies are available and you want the combined
local smoke line:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\quick-all.ps1
```

## Runtime Smoke

Run these only when the API is already running on the canonical backend URL:

```powershell
node scripts/healthcheck.mjs --base-url=http://localhost:5065 --quick

$env:ORKA_API_URL="http://localhost:5065"
pytest contract_tests/
```

`healthcheck.mjs` also reads `ORKA_API_URL` when `--base-url` is omitted.

## Optional External Provider Smoke

Real provider checks are opt-in and must not be part of the deterministic
quick baseline:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\provider-live-smoke.ps1 -Enable
```

If the gate or token is missing, the tests write an explicit skip reason and
return without calling an external provider.

Provider staging proof rules:

- Do not print provider secrets or `dotnet user-secrets list` values. Report
  configured true/false only.
- Keep GitHubModels, OpenRouter, Cohere, Groq, and Mistral as the default live
  smoke providers. Gemini stays outside the default smoke unless quota and auth
  are explicitly verified.
- A real completion/embedding success smoke requires an explicit token and an
  explicit call plan. Without a token, mark success proof blocked rather than
  pretending it passed.
- Invalid-token failure checks may prove safe provider failure behavior because
  they send only synthetic text and do not require a paid credential.
- Keyless public providers may be checked manually for reachability, but those
  checks stay out of deterministic quick scripts and CI.

## What To Run When

- Auth/security/request-boundary/migration/logging change: `scripts\quick-backend.ps1`
- Long-term adaptive learning/profile/Tutor next-action change: run `LongTermAdaptiveLearningTests` plus the learning snapshot, quiz pipeline, Tutor pedagogy policy, backend lifetest, API suite, infrastructure suite, and quick backend/coordination gates.
- Exam/curriculum depth/profile change: run `ExamCurriculumDepthTests`, Central Exam learning/deneme loop tests, assessment/quiz safety tests, long-term adaptive learning tests, backend lifetest, API suite, infrastructure suite, and quick backend/coordination gates.
- Source/Wiki intelligence/profile change: run `SourceWikiIntelligenceTests`, source evidence lifecycle, Wiki graph, Notebook Studio, long-term adaptive learning, exam curriculum depth, Tutor pedagogy policy, backend lifetest, API suite, infrastructure suite, and quick backend/coordination gates.
- Unified Orka learning state / Product Coherence change: run `OrkaLearningStateCoherenceTests`, `OrkaMissionControlTests`, `OrkaStudyCoachTests`, `OrkaExamWarRoomTests`, student simulation, long-term adaptive learning, exam curriculum depth, source/wiki intelligence, Tutor pedagogy policy, backend lifetest, public/security telemetry tests, API suite, infrastructure suite, and quick backend/coordination gates.
- Product map / frontend contract documentation change: run the relevant Product Coherence backend gates, `RegressionGateScriptTests`, `git diff --check`, and frontend build/typecheck only when frontend code or typed API clients are changed.
- Frontend static contract or build-impacting change: `scripts\quick-all.ps1`
- Runtime API process verification: `healthcheck.mjs --quick`
- External HTTP contract verification against a running API: `pytest contract_tests/`
- Provider-heavy or AI-quality work: keep out of the deterministic baseline unless explicitly gated.
- Additive migration work: generate an idempotent script and review it under `docs/deployment/migration-policy.md`.

## Learning OS Feature Completion Contract

Phase 1 adds a deterministic long-term adaptive profile on top of existing durable learning evidence. The contract is:

- derive learner state from existing quiz attempts, knowledge tracing, concept mastery, SRS review items, learning signals, Wiki repair notes, and source evidence state;
- do not add provider calls or AI judges;
- do not add a migration unless durable evidence cannot be derived from current tables;
- one correct answer must not mark a concept stable;
- repeated correct evidence may reduce pressure;
- repeated wrong answers may trigger repair/prerequisite review;
- blank/skipped answers may trigger guided prerequisite review but must not become fake misconception certainty;
- source-limited evidence must create warnings/source-review actions, not source-backed claims;
- Tutor and dashboard may consume safe reason codes and next actions only;
- public DTOs must not expose raw transcripts, prompts, provider bodies, source chunks, tool payloads, local paths, owner/user ids, or answer keys.

Phase 2 adds a deterministic exam/curriculum learning profile on top of the existing Central Exams domain. The contract is:

- derive exam readiness from existing exam framework, question bank, practice attempts, mini-deneme attempts, curriculum mappings, and source verification metadata;
- do not scrape official content and do not claim official alignment unless verified source metadata allows it;
- do not add provider calls or AI judges;
- repeated wrong answers may trigger outcome repair;
- repeated blank/skipped answers may trigger diagnostic/prerequisite review but must not become fake misconception certainty;
- repeated correct evidence may mark an outcome stable, but never as a score/success guarantee;
- thin question coverage must surface `coverage_limited` or `question_coverage_limited`;
- source/curriculum verification gaps must surface honest warnings;
- Central Exams, Dashboard, and Tutor may consume only bounded exam profile DTOs;
- public DTOs must not expose pre-submit answer keys, raw prompts, provider payloads, source chunks, tool/debug payloads, local paths, owner/user ids, or stack traces.

Phase 3 adds a deterministic source/wiki intelligence profile on top of existing source lifecycle, Wiki curation, source Q&A memory, source compare, citation review, and source-to-concept links. The contract is:

- derive source/wiki state from existing source, evidence bundle, citation review, source Q&A, Wiki page/block, and source-to-concept data;
- do not add provider calls or AI judges;
- do not add a migration unless durable evidence cannot be derived from current tables;
- keep uploaded source evidence, Wiki notes, Tutor-generated explanations, and provider output as distinct evidence classes;
- stale/deleted/insufficient/degraded source state must create warnings and source-review actions, not source-grounded overclaims;
- citation review warnings must create `citation_review` or source review next actions;
- Wiki repair-pending/source-limited/stale/duplicate states must create safe handoff actions while preserving manual notes;
- Dashboard and Tutor may consume only bounded source/wiki profile DTOs and next-action metadata;
- provider output and Wiki memory must not be treated as citation evidence;
- public DTOs must not expose raw source chunks, raw Wiki block bodies, transcripts, prompts, provider bodies, tool payloads, local paths, owner/user ids, stack traces, or answer keys.

Phase 4 adds a deterministic student simulation and evaluation harness. The contract is:

- keep the harness provider-free, backend-only, and test-only unless a separate safe diagnostic endpoint is explicitly approved;
- seed realistic learner journeys from durable backend evidence: new learner, repeated wrong, blank/skipped, improving, forgotten/due review, exam prep, source/wiki, and mixed Learning OS journey;
- evaluate long-term adaptive profile, exam learning profile, source/wiki intelligence profile, Tutor next actions, dashboard today metadata, Wiki curation, source evidence state, privacy, overclaim safety, and cross-user protection together;
- use deterministic pass/fail scorecards with reason codes and user-safe summaries only;
- do not use AI judges, paid provider calls, scraped official content, or success/score/placement guarantees;
- public simulation payloads must not expose raw transcripts, prompts, provider bodies, source chunks, Wiki block bodies, tool/debug payloads, local paths, owner/user ids, stack traces, secrets, or answer keys.

## Orka Product Coherence Contract

Phase 1 adds a deterministic unified Orka learning state on top of the completed long-term, exam, source/wiki, simulation, quiz, mastery, review, Wiki, Tutor, Dashboard, and personal Study Room foundations. The contract is:

- compose existing services instead of duplicating module-specific learning rules;
- expose `OrkaLearningStateDto` through `/api/learning/orka-state` and `/api/dashboard/today`;
- choose one primary unified next action with optional secondary handoffs using safe reason codes only;
- treat repeated wrong answers as repair/prerequisite pressure and blank/skipped answers as guided diagnostic/prerequisite pressure, not fake misconception certainty;
- block source-grounded actions when source evidence is stale, deleted, insufficient, degraded, or citation review needs attention;
- surface module disagreement through bounded warnings such as `next_action_conflict`, `source_grounding_blocked`, `exam_learning_conflict`, `missing_topic_context`, and `thin_evidence`;
- let Tutor and Dashboard consume the same unified primary action or explicit conflict warnings;
- treat Study Room/Classroom as personal AI study room readiness only, not teacher/classroom/dershane management;
- keep the contract deterministic/provider-free by default with no new AI/provider calls, no OpenAI Responses API, no Agents SDK, no Realtime migration, and no Google Cloud;
- public DTOs must not expose raw transcripts, prompts, provider bodies, source chunks, Wiki block bodies, tool/debug payloads, local paths, owner/user ids, stack traces, secrets, answer keys, official claims, source-grounded overclaims, or success guarantees.

Phase 2 adds deterministic Orka Home / Mission Control backend on top of the unified learning state. The contract is:

- expose `OrkaMissionControlDto` through `/api/learning/mission-control` and include it in `/api/dashboard/today`;
- derive the Home contract from `OrkaLearningStateDto` plus existing long-term, exam, source/wiki, review, Wiki, personal Study Room, and Notebook Studio readiness signals;
- provide one primary mission, primary entry point, secondary actions, urgent warnings, load summaries, module cards, sections, evidence confidence, reason codes, and a user-safe summary;
- prioritize source-grounding blocks, repeated wrong/prerequisite repair, blank/skipped guided diagnostic, due review, weak exam outcomes, source/wiki warnings, checkpoint quiz, plan continuation, and optional Notebook/Wiki cleanup without AI judges;
- keep handoffs as suggestions only: Tutor, Study Room, Review, Exam, Sources, Wiki, Notebook Studio, Quiz/Checkpoint, and Progress do not execute hidden autonomous actions from Mission Control;
- treat Study Room/Classroom as personal AI study room readiness only, not teacher/classroom/dershane management;
- keep the contract deterministic/provider-free by default with no new AI/provider calls, no OpenAI Responses API, no Agents SDK, no Realtime migration, and no Google Cloud;
- public DTOs must not expose raw transcripts, prompts, provider bodies, source chunks, Wiki block bodies, tool/debug payloads, local paths, owner/user ids, stack traces, secrets, answer keys, official claims, source-grounded overclaims, or success guarantees.

Phase 3 adds deterministic Study Rhythm Coach / Life-Study Coach backend on top of unified Orka state and Mission Control. The contract is:

- expose `OrkaStudyCoachDto` through `/api/learning/study-coach` and include it in `/api/dashboard/today`;
- derive rhythm, workload, focus plan, comeback plan, actions, warnings, and safe summary from `OrkaLearningStateDto`, `OrkaMissionControlDto`, long-term weekly rhythm, review due state, repair pressure, exam profile, source/wiki warnings, personal Study Room readiness, and recent activity evidence;
- keep Mission Control as the source of "what first" and Study Coach as the source of "pace/rhythm/focus";
- use bounded rhythm statuses and reason codes only: thin evidence, repair-heavy, review-heavy, exam-heavy, source cleanup, comeback, focused, normal, or light;
- one wrong answer must not create a heavy repair day; repeated wrong/blank answers can create repair-heavy rhythm; due review can create review sprint; source stale/insufficient/deleted can create source cleanup; exam weak outcome can create exam-focused rhythm;
- comeback planning is practical study pacing after inactivity, not therapy, psychology, wellbeing, medical advice, ADHD/burnout labeling, or diagnosis;
- handoffs remain suggestions only: Tutor, Study Room, Review, Exam, Sources, Wiki, Notebook Studio, and Quiz do not execute hidden autonomous actions from Study Coach;
- treat Study Room/Classroom as personal AI study room readiness only, not teacher/classroom/dershane management;
- keep the contract deterministic/provider-free by default with no new AI/provider calls, no OpenAI Responses API, no Agents SDK, no Realtime migration, no Google Cloud, and no Stripe/payment code;
- public DTOs must not expose raw transcripts, prompts, provider bodies, source chunks, Wiki block bodies, tool/debug payloads, local paths, owner/user ids, stack traces, secrets, answer keys, official claims, source-grounded overclaims, success guarantees, or medical/psychological claims.

Phase 4 adds deterministic Exam War Room backend on top of the exam learning profile, unified Orka state, Mission Control, and Study Coach. The contract is:

- expose `OrkaExamWarRoomDto` through `/api/central-exams/{examCode}/war-room` and include compact `ExamWarRoom` metadata in `/api/dashboard/today`;
- derive active exam, readiness status, weak/due/stable outcomes, weak question types, deneme mistake clusters, practice queue, today exam mission, weekly exam plan, Tutor repair handoffs, Study Room handoffs, source/wiki warnings, curriculum coverage warnings, and conflict warnings from existing deterministic evidence;
- keep Mission Control as the whole-student "what first" contract, Study Coach as pace/rhythm/focus, and Exam War Room as the exam-specialized operating layer;
- repeated deneme mistake clusters should create `review_deneme_mistakes`; repeated wrong should create `repair_exam_outcome`; repeated blank/skipped should create `run_exam_diagnostic`; due outcomes should create `review_due_outcome`; stable repeated success may create low-priority `continue_exam_plan`;
- Study Room handoffs must require safe topic/lesson context and remain personal AI study room suggestions only, not teacher/classroom/dershane management;
- source/curriculum warnings must be explicit and bounded: no official alignment, score, percentile, placement, or exam success claim without verified metadata;
- keep the contract deterministic/provider-free by default with no new AI/provider calls, scraping, OpenAI Responses API, Agents SDK, Realtime migration, Google Cloud, Stripe/payment code, or migration;
- public DTOs must not expose raw transcripts, prompts, provider bodies, source chunks, Wiki block bodies, tool/debug payloads, local paths, owner/user ids, stack traces, secrets, pre-submit answer keys, correct answers, official claims, source-grounded overclaims, or success guarantees.

Phase 5 adds deterministic Source / Wiki Pro Pack backend on top of source lifecycle, Source/Wiki Intelligence, source Q&A memory, source compare/citation review, source-to-concept links, Wiki curation/Copilot, Notebook Studio, unified Orka state, Mission Control, Study Coach, Exam War Room, Tutor, and Dashboard. The contract is:

- expose `OrkaSourceWikiProDto` through `/api/sources/wiki-pro` and include compact `SourceWikiPro` metadata in `/api/dashboard/today`;
- derive source readiness, Wiki readiness, citation readiness, evidence map, linked concepts, linked exam outcomes, source-backed/source-limited concepts, stale/deleted/insufficient/degraded source lists, Wiki repair/duplicate/manual/tutor-trace/source-backed page lists, Notebook pack readiness, today source/wiki mission, recommended actions, handoffs, and warnings from existing deterministic evidence;
- keep Source / Wiki Pro as the evidence-workspace command center; Mission Control still answers the whole-student "what first", Study Coach answers pace/rhythm, and Exam War Room answers exam-specialized priority;
- block source-grounded/source-backed overclaims when source evidence is stale, deleted, insufficient, degraded, or citation review reports missing/unsupported/stale/needs-review evidence;
- treat provider output and Wiki memory as useful context only, never as citation/source evidence by themselves;
- preserve manual Wiki notes while duplicate/stale trace cleanup and repair-pending pages create safe handoff actions;
- keep Notebook Studio, Tutor, Study Room, Exam War Room, Mission Control, Study Coach, and Dashboard handoffs as suggestions only, not hidden autonomous edits/actions;
- treat Study Room/Classroom as personal AI study room readiness only, not teacher/classroom/dershane management;
- keep the contract deterministic/provider-free by default with no new AI/provider calls, scraping, OpenAI Responses API, Agents SDK, Realtime migration, Google Cloud, Stripe/payment code, frontend redesign, or migration;
- public DTOs must not expose raw transcripts, prompts, provider bodies, source chunks, Wiki block bodies, tool/debug payloads, local paths, owner/user ids, stack traces, secrets, pre-submit answer keys, correct answers, official claims, source-grounded overclaims, or success guarantees.

Phase 6 adds deterministic AI Study Room backend on top of the existing Classroom foundation, unified Orka state, Mission Control, Study Coach, Exam War Room, Source / Wiki Pro, Tutor handoffs, quiz/review/memory/Wiki/source evidence, and Dashboard. The contract is:

- expose `OrkaStudyRoomDto` through `/api/classroom/study-room`, start sessions through `/api/classroom/study-room/start`, submit safe checkpoints through `/api/classroom/study-room/checkpoint`, and include compact `StudyRoom` metadata in `/api/dashboard/today`;
- derive session readiness, mode, selected topic/concept/outcome, source/Wiki readiness, rhythm status, recommended pace, lesson plan, role plan, checkpoint plan, turn state, handoffs, warnings, reason codes, and safe summary from existing deterministic evidence;
- keep Study Room/Classroom as a personal AI study room only, not teacher/classroom/dershane management;
- keep roles as product modes (`ai_teacher`, `ai_assistant`, `student`), not claims about real human teachers;
- source-grounded lessons must be blocked or downgraded unless Source / Wiki Pro evidence allows source-backed metadata;
- checkpoint DTOs must hide pre-submit answer keys and only expose bounded post-submit feedback/response signals;
- completed starts/checkpoints may write safe learning signals and bounded Classroom traces only; do not dump raw transcripts, student free text, provider payloads, source chunks, or debug JSON;
- keep the contract deterministic/provider-free by default with no new AI/provider calls, paid provider calls, AI judge, OpenAI Responses API, Agents SDK, Realtime migration, Google Cloud, Stripe/payment code, frontend redesign, teacher/classroom management workflow, or migration;
- public DTOs must not expose raw transcripts, prompts, provider bodies, source chunks, Wiki block bodies, tool/debug payloads, local paths, owner/user ids, stack traces, secrets, pre-submit answer keys, correct answers, official claims, source-grounded overclaims, success guarantees, or medical/psychological/therapy claims.

Phase 7 adds deterministic Notebook Studio / Artifact Pro Pack backend on top of unified Orka state, Mission Control, Study Coach, Exam War Room, Source / Wiki Pro, AI Study Room, existing Notebook Studio packs, learning artifacts, Wiki/source evidence, quiz/review/memory, and export-preview metadata. The contract is:

- expose `IOrkaNotebookStudioProService` / `OrkaNotebookStudioProService`, `OrkaNotebookStudioProDto`, pack/action/warning/export-preview/evidence-link DTOs, `GET /api/notebook-studio/pro`, and compact `NotebookStudioPro` metadata in `/api/dashboard/today`;
- recommend artifact packs from existing durable evidence only: repair, review, exam outcome, deneme mistake, source study, Wiki cleanup, Study Room summary, Tutor lesson, flashcard, checkpoint quiz, slide outline, audio script, and artifact collection;
- keep export behavior preview-only unless a later explicit phase implements and validates real PPTX/video output;
- keep source-backed artifact claims gated by Source / Wiki Pro evidence; provider output and Wiki memory alone are not citation evidence;
- link artifacts to concepts, exam outcomes, sources, Wiki pages, and Study Room traces as safe metadata only, without raw source chunks, raw Wiki block bodies, raw transcripts, raw prompts, provider bodies, or debug/tool payloads;
- keep Tutor, Review, Source/Wiki, Exam War Room, Study Room, Dashboard, and Notebook Studio handoffs as suggestions only, not hidden autonomous edits/actions;
- keep the contract deterministic/provider-free by default with no new AI/provider calls, paid provider calls, AI judge, OpenAI Responses API, Agents SDK, Realtime migration, Google Cloud, Stripe/payment code, frontend redesign, teacher/classroom management workflow, migration, real PPTX/video generation, or official scraping;
- public DTOs must not expose raw transcripts, prompts, provider bodies, source chunks, Wiki block bodies, tool/debug payloads, local paths, owner/user ids, stack traces, secrets, pre-submit answer keys, correct answers, official claims, source-grounded overclaims, success guarantees, or medical/psychological/therapy claims.

Phase 8 adds deterministic Code Learning IDE + Tool Runtime Polish backend on top of unified Orka state, Mission Control, Study Coach, Tutor handoffs, quiz/mastery/review/memory, Wiki, Notebook Studio Pro, and tool capability metadata. The contract is:

- expose `IOrkaCodeLearningIdeService` / `OrkaCodeLearningIdeService`, `OrkaCodeLearningIdeDto`, runtime-readiness/session/exercise/attempt/action/handoff/warning DTOs, `GET /api/code/learning-ide`, and compact `CodeLearningIde` metadata in `/api/dashboard/today`;
- recommend coding actions from existing durable evidence only: quick diagnostic, syntax repair, runtime error repair, test failure repair, blank/no-attempt guided practice, weak coding concept practice, due review, checkpoint, Tutor handoff, code note, code repair pack, code checkpoint pack, and stable continuation;
- keep code execution capability bounded by existing sandbox/tool capability policy and never broaden runtime permissions without explicit approval;
- sanitize public code-run and Code Learning IDE payloads so stack traces, local paths, secrets, tokens, API keys, raw tool/debug markers, owner/user ids, prompts, provider bodies, source chunks, transcripts, pre-submit answer keys, and correct answers are not exposed;
- one error must not create heavy repair pressure; repeated syntax/runtime/test/blank signals may create bounded repair or diagnostic actions;
- keep Notebook Studio Pro, Tutor, Quiz, Review, Wiki, Dashboard, Mission Control, and Study Coach handoffs as suggestions only, not hidden autonomous edits/actions;
- keep the contract deterministic/provider-free by default with no new AI/provider calls, paid provider calls, AI judge, OpenAI Responses API, Agents SDK, Realtime migration, Google Cloud, Stripe/payment code, frontend redesign, teacher/classroom management workflow, migration, unsafe shell/system access, or official scraping;
- public DTOs must not expose raw transcripts, prompts, provider bodies, source chunks, Wiki block bodies, tool/debug payloads, local paths, owner/user ids, stack traces, secrets, pre-submit answer keys, correct answers, official claims, source-grounded overclaims, success guarantees, or unsafe runtime details.

Phase 9 adds deterministic Unified Evaluation / CI / Release Harness backend on top of the Phase 1-8 Product Coherence services, Tutor policy, Dashboard composition, student simulation, backend lifetest, release closure tests, and local quick scripts. The contract is:

- expose `IOrkaUnifiedEvaluationService` / `OrkaUnifiedEvaluationService`, `OrkaUnifiedEvaluationDto`, scenario result, scorecard, check, warning, safety sweep, and release gate summary DTOs;
- evaluate Unified State, Mission Control, Study Coach, Exam War Room, Source / Wiki Pro, AI Study Room, Notebook Studio Pro, Code Learning IDE, Tutor policy, Dashboard readiness, quiz/mastery/memory, review/SRS, safety/privacy, no-overclaim, cross-user safety, provider-free readiness, and release gate readiness together;
- use deterministic status, reason-code, and user-safe summary metadata only; do not use AI judges or paid provider calls;
- serialize public DTO outputs for safety sweep without exposing or returning raw prompts, provider payloads, source chunks, Wiki block bodies, tool/debug payloads, local paths, secrets, owner/user ids, stack traces, raw transcripts, pre-submit answer keys, or arbitrary raw learner/source phrases;
- keep source-grounded, official, and success claims gated by verified evidence metadata; provider output and Wiki memory alone are never citation evidence;
- keep local release scripts provider-free, non-destructive, and free of live API key requirements; `quick-backend.ps1` must include the Product Coherence release proof group and `RegressionGateScriptTests` must lock it;
- keep the contract deterministic/provider-free by default with no new AI/provider calls, paid provider calls, AI judge, OpenAI Responses API, Agents SDK, Realtime migration, Google Cloud, Stripe/payment code, frontend redesign, teacher/classroom management workflow, migration, unsafe runtime expansion, official scraping, or remote CI debugging unless explicitly requested.

Phase 10 adds UX Research / Product Map documentation on top of the completed backend Product Coherence contracts. The contract is:

- create product-level documentation only; do not implement frontend redesign in this phase;
- map Orka as a personal Learning OS / AI study OS, not a generic chatbot, teacher panel, school management system, or institutional classroom product;
- define Home / Mission Control, Tutor, Study Room, Review / Quiz, Exam War Room, Sources / Wiki Pro, Notebook Studio, Code Learning IDE, Progress / Memory, and Settings / Safety as the future product screen set;
- map each screen to backend endpoints, DTOs, loading/empty/thin-evidence/warning/blocked/ready states, actions, handoffs, and safety constraints;
- document learner journeys for new, struggling, blank/skipped, improving, forgotten, exam prep, source/wiki, Study Room, Notebook/artifact, code learning, and mixed Learning OS students;
- audit the existing frontend as a reusable foundation that does not yet consume Phase 1-9 Product Coherence DTOs;
- produce a Phase 11 frontend redesign brief and product readiness scorecard before UI implementation begins;
- keep Study Room/Classroom as personal AI study room only, not teacher/classroom/dershane management;
- add no AI/provider calls, paid provider calls, OpenAI Responses API, Agents SDK, Realtime migration, Google Cloud, Stripe/payment code, mobile app, migration, official scraping, unsafe runtime expansion, or frontend implementation;
- documentation and future frontend contracts must not expose raw prompts, provider payloads, source chunks, Wiki block bodies, tool/debug payloads, local paths, secrets, owner/user ids, stack traces, raw transcripts, pre-submit answer keys, official/source-grounded overclaims, or success guarantees.

Phase 11 adds Frontend Redesign / Product Beta Polish on top of the completed backend and product-map contracts. The contract is:

- make Home / Mission Control the first logged-in student surface;
- expose the beta work modes as Home, Tutor, Study Room, Review, Exams, Sources/Wiki, Notebook, Code, Progress, and Settings;
- add typed frontend API wrappers and DTOs for Unified State, Mission Control, Study Coach, Exam War Room, Source / Wiki Pro, AI Study Room, Notebook Studio Pro, and Code Learning IDE;
- render compact loading, empty, thin-evidence, warning, blocked, and ready states without raw JSON/internal payloads;
- keep handoffs visible suggestions, not hidden autonomous actions;
- keep Study Room/Classroom as personal AI study room only, not teacher/classroom/dershane management;
- reuse existing Tutor, Review/Quiz, Wiki/source, exam, notebook, code, and progress components where safe rather than replacing working surfaces wholesale;
- add no AI/provider calls, paid provider calls, OpenAI Responses API, Agents SDK, Realtime migration, Google Cloud, Stripe/payment code, mobile app, official scraping, unsafe runtime expansion, teacher/classroom management workflow, official/source-grounded overclaim, or success guarantee;
- frontend surfaces must not expose raw prompts, provider payloads, source chunks, Wiki block bodies, tool/debug payloads, local paths, secrets, owner/user ids, stack traces, raw transcripts, pre-submit answer keys, or correct answers.

## Codex Skills Feature Workflow

Feature work must follow the current roadmap in
`docs/project-state/current-roadmap.md` and the constitutions in
`docs/codex-skills/`. Stage 6B Central Exams and Post-6B Professionalization are
closed. The current product-coherence phase binds Tutor, Mission Control, Study Coach,
Dashboard, Exam, Source/Wiki, Quiz, Review, Memory, Notebook Studio, Code Learning IDE,
personal Study Room, unified evaluation, product-map documentation, and frontend beta
shell work through safe backend, frontend, and documentation contracts. Related work must also
read `docs/architecture/orka-learning-os-contract-map.md`.

Central Exams is an integrated Orka module, not a standalone KPSS app. It must
reuse Orka's exam framework, question bank, import pipeline, practice,
mini-deneme, learning signal, memory/planner/tutor, and wiki-study context
architecture. Do not add teacher/classroom/dershane workflows, official exam
claims without verified metadata, success guarantees, scraped content
assumptions, or auto-published generated/imported content.

Main Learning OS guard:

- Tutor is the pedagogical owner.
- Korteks researches; `KorteksResearchWorkflow` / synthesis contracts decide
  bounded educational use for plan, quiz, Tutor, and Wiki consumers.
- Semantic Kernel may bridge LLM plugins/tools, but Orka tool runtime owns
  policy, ledger, user/session/topic/correlation, fallback, and telemetry.
- RAG/Wiki must distinguish sourced, wiki-backed, degraded, and model-fallback
  claims.
- Quiz must measure concepts/misconceptions, not Orka UI/product labels.

Default flow:

1. Read `docs/project-state/current-roadmap.md`.
2. Read `docs/architecture/orka-learning-os-contract-map.md` for Tutor/Korteks/RAG/Wiki/Quiz/Tool/Plan work.
3. Read `docs/codex-skills/README.md`.
4. Read the applicable constitution files before planning:
   - backend/API/data: `backend-feature-constitution.md`
   - AI/RAG/Wiki/Chat/Korteks/source/citation: `ai-rag-feature-constitution.md`
   - frontend API/types/stream/UI contract: `frontend-contract-constitution.md`
   - persistent data/cache/session/delete/privacy: `data-lifecycle-constitution.md`
   - every feature: `testing-gate-constitution.md`
5. Use `feature-prompt-template.md` for new feature prompts.
6. Use `feature-completion-report-template.md` for final reports.
7. Do not stage or commit unless explicitly requested.
