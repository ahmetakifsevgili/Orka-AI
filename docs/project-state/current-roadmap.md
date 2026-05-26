# Current Roadmap

## Current Phase

Main Learning OS Professionalization - closed

OrkaLM / Wiki-aware Notebook Studio - closed as safe foundation

Pedagogical Productization - in progress

Backend Release Hardening - in progress

Learning OS Feature Completion - in progress

Orka Product Coherence - in progress

## Completed Phases

- V1 system-life
- Backend Coordination Pack A/B/C/D
- Coordination Backlog Cleanup
- System Closure Pack
- Production Safety Lite
- Mini blocker audit
- Codex Skills Anayasasi
- Stage 4 Small/Medium Feature Packs
  - Pack 1 - Learning Guidance Pack
  - Pack 2 - Coordination Visibility Pack
  - Pack 3 - Evidence Trust Pack
  - Pack 4 - Wiki Study Pack
- Stage 4 Small/Medium Feature Completion Audit
- Stage 5 - Production-ready enterprise hardening / scalability plan
- Stage 6B - Merkezi Sinavlar / Exam & Practice Content Engine
  - Pack 1 - Exam Framework Architecture
  - Pack 2 - Question Bank Core
  - Pack 3 - Structured Question Import Pipeline
  - Pack 4 - Central Exams Shell & KPSS Study Home MVP
  - Pack 5 - Practice Results -> Orka Learning Loop Integration
  - Pack 6 - Mini Deneme Engine MVP
  - Pack 7 - Multi-Exam Shell & Content Pack Expansion MVP
  - Pack 8 - Source-Grounded Question Draft Generation MVP
- Post-6B Professionalization
  - Pack A - Curriculum & Source Registry + Verification Gate
  - Pack A2 - Curriculum Graph Hardening
  - Pack B - Rich Question Model & Asset Infrastructure
  - Pack C - Import Pipeline v2: Rich Package + Standards Preview Adapters
  - Pack D - Content Operations Lite: Review, Publish Gate & Audit Trail
  - Pack E - KPSS Turkce Pilot UX + Original Pilot Content Flow
  - Pack F - Quality Analytics & Item Calibration

## Active Roadmap

1. System Closure Pack - complete
2. Production Safety Lite - complete
3. Mini blocker audit - complete
4. Codex Skills Anayasasi + small/medium features - complete
5. Production-ready enterprise hardening / scalability plan - complete
6. Stage 6B - Merkezi Sinavlar / Exam & Practice Content Engine - complete
7. Post-6B Professionalization - complete
8. Central Exams pilot productization readiness - complete / superseded by Main Learning OS Professionalization
9. Main Learning OS Professionalization - closed
  - Pack 0 - Architecture Contract Map & Docs Convergence - complete
  - Pack 1 - ActiveLessonSnapshot & StudentContext Contract - complete
  - Pack 2 - Unified Tool Runtime & Kernel Governance - complete
  - Pack 3 - Korteks Research Workflow & Synthesis Contract - complete
  - Pack 4 - RAG / Source / Wiki Knowledge Lifecycle - complete
  - Pack 5 - Plan Quality & Curriculum Sequencing - complete
  - Pack 6 - Quiz / Assessment Quality & Misconception Engine - complete
  - Pack 7 - Tutor Pedagogy & Response Policy Closure - complete
  - Pack 8 - Learning Artifacts Engine - complete
  - Pack 9 - Frontend Learning Workspace Synchronization - complete
  - Pack 10 - Observability, Cost & Runtime Telemetry - complete
  - Pack 11 - Agentic Security & Trust Hardening - complete
10. OrkaLM / Wiki-aware Notebook Studio Professional Closure - complete
  - Wiki page-aware OrkaLM packs - complete
  - Source-aware study guide / source digest / repair pack - complete
  - Pack-aware audio overview foundation - complete
  - Concept graph mind map / flashcards / review quiz blueprint - complete
  - Slide outline / video-ready package foundation - complete
  - Compact Notebook Studio frontend surface in Wiki - complete
  - Phase 10 production closure / UX hardening / migration snapshot alignment - complete
  - Phase 11 advanced media/export architecture - complete
  - Phase 12 deterministic slide export MVP - complete
  - Phase 13 advanced deck UX / local export decision - complete
  - Phase 14 final OrkaLM professional audit - PASS
  - Phase 15 Wiki Vault UX productization - complete
  - Phase 16 Tutor-Wiki Learning Trace Writer - complete
  - Phase 17 OrkaLM Dedicated Source Notebook UX - complete
  - Phase 18 Source-to-Concept Auto-Linking & Advanced OrkaLM Graph - complete
  - Phase 19 Advanced Source Q&A / Ask-Source UX - complete
  - Phase 20 Multi-source Compare & Citation Review UX - complete
  - Phase 21 Source Q&A Conversation Memory & Review Workflow - complete
  - Phase 22-23 Source Q&A Study Workflow & Advanced OrkaLM Graph Polish - complete
  - Phase 24-25 Final Safety/Privacy Audit & Product Closure - PASS

11. Pedagogical Productization - in progress
  - Phase 1 - Tutor Tool-Use Orchestration Polish - implemented pending full validation
    - Adds a safe Tutor tool decision contract on top of the existing TutorActionPlanner/TutorToolOrchestrator/UnifiedToolRuntime path.
    - Uses deterministic learner, remediation, source evidence, Wiki, IDE, review, artifact, and research signals first.
    - Does not add new AI/provider calls and does not migrate to OpenAI Responses or Agents SDK.
    - Evidence-limited source intent blocks source-grounded routing and exposes a safe clarification/degraded decision.
    - Blank/skipped remediation remains prerequisite/telafi oriented rather than fake misconception certainty.
  - Phase 2 - Professional Lesson Delivery Rubric - implemented pending full validation
    - Adds a safe `TutorLessonDeliveryDto` on top of the Phase 1 tool decision contract.
    - Uses deterministic learner level, mastery, quiz/remediation, source evidence, and policy signals to choose concept explanation, guided example, checkpoint, prerequisite repair, misconception repair, source-grounded explanation, or model-assisted explanation.
    - Feeds the lesson delivery mode into Tutor prompt guidance, chat metadata, frontend trace chips, and Wiki learning trace summaries without raw prompts, provider payloads, source chunks, or answer keys.
    - Does not add new AI/provider calls and does not migrate to OpenAI Responses or Agents SDK.
  - Phase 3 - Adaptive Diagnostic & Course-Plan Quality - implemented pending full validation
    - Adds safe `AdaptiveDiagnosticDto` and `CoursePlanQualityDto` metadata to plan sequencing, plan readiness, Tutor turn state, chat metadata, and frontend trace chips.
    - Uses deterministic intent, learner evidence, mastery/quiz/remediation, concept graph, prerequisite, source readiness, and plan quality signals.
    - Plan readiness is provisional and can be `ready`, `needs_diagnostic`, `needs_prerequisite_check`, `needs_repair`, `source_limited`, `thin_plan`, or `degraded`.
    - Course plans now expose milestone shape, checkpoint coverage, repair loops, recommended next action, and overclaim risk without raw prompts, provider payloads, source chunks, owner ids, or answer keys.
    - Does not add new AI/provider calls and does not migrate to OpenAI Responses or Agents SDK.
  - Phase 4 - Remediation Lesson Productization - implemented pending full validation
    - Adds safe `RemediationLessonDto` metadata across quiz learning impact, Tutor turn state/action plans, chat metadata, Wiki traces, and compact quiz/chat UI.
    - Wrong answers, blank/skipped answers, confused turns, weak concepts, and source-evidence gaps now produce distinct repair types: misconception repair, prerequisite repair, guided reteach, weak concept repair, confidence/prerequisite repair, or source evidence review.
    - Repair lessons expose only bounded student-facing fields: trigger, repair type, basis labels, micro-lesson shape, worked-example/guided-practice/checkpoint labels, next action, warnings, and mastery policy.
    - Blank/skipped answers remain post-submit safe and do not become fake misconception certainty.
    - Does not add new AI/provider calls and does not migrate to OpenAI Responses or Agents SDK.
  - Phase 5 - Wiki Auto-Curation & Learning Memory Cleanup - implemented pending full validation
    - Adds safe `WikiCurationSummaryDto` and `LearningMemoryHygieneDto` metadata across Wiki pages, learning snapshots, chat metadata, Notebook Studio context, and compact Wiki/chat UI.
    - `WikiAutoCurationService` computes page hygiene without provider calls: duplicate trace, stale trace, repair pending, source limited, degraded, or clean.
    - Wiki trace dedupe now compares normalized safe summaries in addition to durable ids, reducing repeated chat sludge without deleting manual student notes.
    - Learning memory exposes bounded hygiene/status summaries only; Tutor and Notebook Studio consume curated memory/context rather than raw transcripts.
    - Source-grounded Wiki blocks remain separate from Tutor-generated notes; stale/deleted/source-limited state degrades via warnings instead of overclaiming.
    - Does not add new AI/provider calls and does not migrate to OpenAI Responses or Agents SDK.
  - Phase 6 - Wiki Copilot UX - implemented pending full validation
    - Adds safe `WikiCopilotContextDto` metadata and a page-aware `IWikiCopilotService`.
    - Copilot suggestions are deterministic handoffs to Tutor, quiz/checkpoint, source review, Wiki curation, and Notebook Studio, not hidden autonomous actions.
    - Source-grounded actions are blocked/degraded unless source evidence is ready.
    - Does not add new AI/provider calls and does not migrate to OpenAI Responses or Agents SDK.
  - Phase 7 - Final Pedagogical E2E, Evaluation Harness & Release Closure - implemented pending full validation
    - Adds a provider-free release harness that connects diagnostic, course plan, Tutor tool decision, lesson delivery, remediation, quiz impact, learning snapshot, Wiki curation/Copilot, OrkaLM source notebook, Notebook Studio, dashboard, and public payload safety.
    - Uses deterministic smoke services only; no paid provider calls, Stripe calls, OpenAI migration, real PPTX/video, Realtime, or graph-canvas scope is added.
    - Stripe/payment code was not found in the audited repo surface, so payment release safety is not applicable for this phase.
    - Remaining pedagogical backlog: live provider pedagogy E2E proof, human-reviewed advanced Tutor evaluation harness, long-term adaptive syllabus optimization, optional graph canvas, optional semantic multi-source synthesis, optional real PPTX/video/Realtime voice.

12. Learning OS Feature Completion - implemented pending full validation
  - Phase 1 - Long-Term Adaptive Learning Engine - implemented pending full validation
    - Adds a deterministic `ILongTermAdaptiveLearningService` that derives long-term learner profile, concept state, review pressure, next study actions, and weekly rhythm from existing durable evidence.
    - Reuses existing quiz attempts, knowledge tracing, concept mastery, SRS review items, learning signals, Wiki repair notes, and source evidence bundles; no migration is required.
    - Tracks safe concept states such as new, learning, weak, repaired, stable, due for review, and likely forgotten.
    - Tutor next actions and dashboard now consume the long-term adaptive profile through bounded safe DTOs.
    - One correct answer does not mark mastery stable; repeated correct answers can reduce pressure; repeated wrong or blank/skipped answers trigger cautious repair/prerequisite review.
    - Stale or insufficient source evidence creates source-review warnings without treating provider text or Wiki memory as source evidence.
    - No new AI/provider calls, paid calls, Google Cloud, OpenAI Responses/Agents migration, medical/psychological claims, or exam success guarantees were added.
  - Phase 2 - Exam & Curriculum Depth Pack - implemented pending full validation
    - Adds deterministic `IExamLearningProfileService` / `ExamLearningProfileService` on top of the existing exam framework, question bank, practice attempts, mini-deneme attempts, curriculum mappings, and source verification metadata.
    - Exam prep now exposes safe outcome readiness, question type readiness, weak/due/stable outcomes, exam next actions, and coverage/source warnings.
    - Repeated wrong answers trigger outcome repair; repeated blank answers trigger diagnostic/prerequisite review without fake misconception certainty; repeated success can mark an outcome stable without success guarantees.
    - Deneme mistake clusters can create `review_deneme_mistakes`; thin question coverage creates `question_coverage_limited`; unverified curriculum/source state creates honest source warnings.
    - Central Exams, Dashboard, and Tutor next-action metadata consume the exam profile through bounded DTOs with no raw prompts, provider payloads, source chunks, owner/user ids, or pre-submit answer keys.
    - No scraping, official curriculum/exam alignment claim, success guarantee, new AI/provider call, OpenAI API migration, migration, frontend redesign, teacher/classroom/dershane workflow, or payment feature was added.
  - Phase 3 - Source/Wiki Intelligence Deepening - implemented pending full validation
    - Adds deterministic `ISourceWikiIntelligenceService` / `SourceWikiIntelligenceService` over existing source lifecycle, source-to-concept links, source Q&A memory, citation review, Wiki curation, and Wiki page health.
    - Source/Wiki now exposes one safe profile for source readiness, citation readiness, Wiki repair/source-limited/stale state, linked concepts, source Q&A review pressure, warnings, and next actions.
    - Dashboard and Tutor next-action metadata consume this profile through bounded DTOs; raw source chunks, Wiki block bodies, prompts, provider payloads, tool/debug payloads, owner/user ids, paths, stack traces, and answer keys are not exposed.
    - Provider output and Wiki memory are still not treated as citation evidence; source-grounded claims remain blocked when evidence is stale, insufficient, deleted, degraded, or citation review needs attention.
    - No scraping, official/source-grounded overclaim, success guarantee, new AI/provider call, OpenAI API migration, migration, frontend redesign, teacher/classroom/dershane workflow, or payment feature was added.
  - Phase 4 - Student Simulation & Evaluation Harness - implemented pending full validation
    - Adds a deterministic provider-free student simulation harness in API tests, not a public diagnostic endpoint.
    - Scenario pack covers new learner, repeated wrong learner, blank/skipped learner, improving learner, forgotten/due-review learner, exam prep learner, source/wiki learner, and mixed Learning OS journey.
    - Evaluation scorecard checks long-term learning, exam profile, source/wiki profile, Tutor next actions, dashboard consistency, Wiki curation, privacy, overclaim safety, and cross-user protection through reason-code based pass/fail checks.
    - Serialized public simulation payloads are swept for raw prompt/provider/source/tool/debug markers, local paths, secrets, owner/user ids, stack traces, answer keys, arbitrary learner text, and arbitrary source text.
    - No AI judge, paid provider call, official/source-grounded/success guarantee, new AI/provider call, OpenAI API migration, migration, frontend redesign, mobile app, teacher/classroom/dershane workflow, or payment feature was added.
  - Closure decision:
    - Learning OS Feature Completion 1-4 is ready to close after the Phase 4 validation gate passes locally.

13. Orka Product Coherence - in progress
  - Phase 1 - Orka OS Binding Layer / Unified Learning State - implemented pending full validation
    - Adds deterministic `IOrkaLearningStateService` / `OrkaLearningStateService` as the central safe backend contract over long-term adaptive learning, exam learning profile, source/wiki intelligence, quiz/mastery/review evidence, Wiki repair state, source lifecycle, and personal Study Room/Classroom readiness.
    - Adds `OrkaLearningStateDto`, `OrkaUnifiedNextActionDto`, `OrkaLearningSignalSummaryDto`, `OrkaFeatureReadinessDto`, and `OrkaLearningStateConflictDto` so future Home/Mission Control surfaces can read one compact state object instead of stitching module-specific rules.
    - Adds `/api/learning/orka-state` and includes the unified state in `/api/dashboard/today`.
    - `TutorResponsePolicyService` now consumes the unified next action and conflict/safety warnings before adding Tutor next-action metadata.
    - Unified arbitration can choose safe actions such as diagnostic, repair, review, source/citation review, exam practice, Study Room, flashcards, Wiki note update, or plan continuation from existing evidence only.
    - Conflict detection surfaces bounded reason codes such as `next_action_conflict`, `source_grounding_blocked`, `exam_learning_conflict`, `missing_topic_context`, and `thin_evidence`.
    - Study Room/Classroom remains a personal AI study room signal, not teacher/classroom/dershane management.
    - No new AI/provider calls, OpenAI Responses/Agents/Realtime migration, Google Cloud, migration, frontend redesign, official/success guarantee claim, or teacher/classroom management workflow was added.
  - Phase 2 - Orka Home / Mission Control Backend - implemented pending full validation
    - Adds deterministic `IOrkaMissionControlService` / `OrkaMissionControlService` on top of the unified Orka learning state.
    - Adds `OrkaMissionControlDto`, `OrkaTodayMissionDto`, `OrkaMissionActionDto`, `OrkaMissionSectionDto`, `OrkaMissionModuleCardDto`, and `OrkaMissionWarningDto`.
    - Adds `/api/learning/mission-control` and includes `MissionControl` in `/api/dashboard/today`.
    - Mission Control exposes one primary mission, primary entry point, secondary actions, urgent warnings, today focus, review/repair/exam/source-wiki load, Study Room suggestion, module cards, sections, evidence confidence, safe reason codes, and a bounded user-safe summary.
    - Mission prioritization respects source-grounding blocks, repeated wrong/prerequisite repair, blank/skipped guided diagnostic, due review, weak exam outcomes, source/wiki warnings, checkpoint quiz, plan continuation, and optional Notebook/Wiki cleanup.
    - Module cards cover Tutor, Study Room, Review, Exam, Sources, Wiki, Notebook Studio, Quiz/Checkpoint, and Progress as handoffs only; Study Room/Classroom remains a personal AI study room, not teacher/classroom/dershane management.
    - `StudentSimulationEvaluationTests` now sweeps Mission Control payloads and checks that the mixed Learning OS journey includes the Home contract.
    - No new AI/provider calls, OpenAI Responses/Agents/Realtime migration, Google Cloud, migration, frontend redesign, official/success guarantee claim, or teacher/classroom management workflow was added.
  - Phase 3 - Study Rhythm Coach / Life-Study Coach Backend - implemented pending full validation
    - Adds deterministic `IOrkaStudyCoachService` / `OrkaStudyCoachService` on top of unified Orka learning state and Mission Control.
    - Adds `OrkaStudyCoachDto`, workload, focus plan, comeback plan, action, and warning DTOs.
    - Adds `/api/learning/study-coach` and includes `StudyCoach` in `/api/dashboard/today`.
    - Study Coach answers how hard/how long/in what rhythm to study; Mission Control still answers what to do first.
    - Pacing can return light, normal, focused, repair-heavy, review-heavy, exam-heavy, source-cleanup, comeback, or thin-evidence rhythm from existing review, repair, exam, source/wiki, Study Room, and activity evidence.
    - Comeback planning is practical study pacing only; it does not make therapy, psychology, medical, wellbeing, ADHD, burnout, or diagnosis claims.
    - Study Room/Classroom remains a personal AI study room handoff, not teacher/classroom/dershane management.
    - No new AI/provider calls, OpenAI Responses/Agents/Realtime migration, Google Cloud, Stripe/payment feature, migration, frontend redesign, official/success guarantee claim, or teacher/classroom management workflow was added.
  - Phase 4 - Exam War Room Backend - implemented pending full validation
    - Adds deterministic `IOrkaExamWarRoomService` / `OrkaExamWarRoomService` on top of the existing exam learning profile, unified Orka state, Mission Control, and Study Coach.
    - Adds `OrkaExamWarRoomDto` and compact subject/topic/outcome/practice/deneme/action/warning DTOs for the future Exam War Room UI.
    - Adds `/api/central-exams/{examCode}/war-room` and includes `ExamWarRoom` in `/api/dashboard/today`.
    - Exam War Room exposes active exam, readiness status, weak/due/stable outcomes, weak question types, deneme mistake clusters, recommended practice queue, today exam mission, weekly exam plan, Tutor repair handoffs, personal Study Room handoffs when safe topic context exists, source/wiki warnings, curriculum coverage warnings, and conflict warnings.
    - Deneme mistake clusters create `review_deneme_mistakes`; repeated wrong creates `repair_exam_outcome`; repeated blank/skipped creates `run_exam_diagnostic`; due outcomes create `review_due_outcome`; stable repeated success can expose low-priority `continue_exam_plan`.
    - Source/curriculum verification limits create honest warnings such as `source_unverified`, `source_evidence_limited`, `question_coverage_limited`, `official_claim_blocked`, and `answer_key_guard`.
    - `StudentSimulationEvaluationTests` now includes Exam War Room in serialized public payload sweeps and mixed Learning OS consistency checks.
    - No new AI/provider calls, scraping, OpenAI Responses/Agents/Realtime migration, Google Cloud, Stripe/payment feature, migration, frontend redesign, official alignment claim, success guarantee claim, or teacher/classroom management workflow was added.
  - Phase 5 - Source / Wiki Pro Pack Backend - implemented pending full validation
    - Adds deterministic `IOrkaSourceWikiProService` / `OrkaSourceWikiProService` on top of existing source lifecycle, Source/Wiki Intelligence, citation review, source-to-concept links, Notebook Studio, unified Orka state, Mission Control, Study Coach, and Exam War Room.
    - Adds `OrkaSourceWikiProDto` and compact source/wiki/citation/concept/action/warning/evidence-map DTOs for the future Source / Wiki Pro UI.
    - Adds `/api/sources/wiki-pro` and includes `SourceWikiPro` in `/api/dashboard/today`.
    - Source / Wiki Pro exposes source readiness, Wiki readiness, citation readiness, linked concepts, linked exam outcomes, source-backed/source-limited concepts, stale/deleted/insufficient/degraded sources, Wiki repair/duplicate/manual/tutor-trace/source-backed pages, Notebook pack readiness, today source/wiki mission, handoffs, and conflict warnings.
    - Evidence priority blocks source-grounded overclaims for stale/deleted/insufficient/degraded sources or missing/unsupported/stale citations; provider output and Wiki memory alone never count as citation evidence.
    - `StudentSimulationEvaluationTests` now includes Source / Wiki Pro in serialized public payload sweeps and mixed Learning OS consistency checks.
    - No new AI/provider calls, scraping, OpenAI Responses/Agents/Realtime migration, Google Cloud, Stripe/payment feature, migration, frontend redesign, official/source-grounded overclaim, success guarantee claim, or teacher/classroom management workflow was added.
  - Phase 6 - AI Study Room Backend - implemented pending full validation
    - Adds deterministic `IOrkaStudyRoomService` / `OrkaStudyRoomService` on top of unified Orka state, Mission Control, Study Coach, Exam War Room, Source / Wiki Pro, Tutor handoffs, quiz/review/memory/Wiki/source evidence, and the existing Classroom session foundation.
    - Adds `OrkaStudyRoomDto` and compact session/plan/role/checkpoint/turn/action/warning DTOs for the future AI Study Room UI.
    - Adds `/api/classroom/study-room`, `/api/classroom/study-room/start`, and `/api/classroom/study-room/checkpoint`, and includes compact `StudyRoom` metadata in `/api/dashboard/today`.
    - Study Room modes include quick start, repair lesson, review lesson, exam outcome practice, source review lesson, Wiki repair lesson, checkpoint quiz, and continue plan.
    - Checkpoint handling hides answer keys before submit, records only bounded safe response signals, and writes safe Classroom learning traces through existing durable paths.
    - Study Room/Classroom remains a personal AI study room, not teacher/classroom/dershane management; Realtime voice is not implemented in this phase.
    - `StudentSimulationEvaluationTests` now includes Study Room in serialized public payload sweeps and mixed Learning OS consistency checks.
    - No new AI/provider calls, paid provider calls, OpenAI Responses/Agents/Realtime migration, Google Cloud, Stripe/payment feature, migration, frontend redesign, therapy/medical/psychological claim, official/source-grounded overclaim, success guarantee claim, or teacher/classroom management workflow was added.
  - Phase 7 - Notebook Studio / Artifact Pro Pack Backend - implemented pending full validation
    - Adds deterministic `IOrkaNotebookStudioProService` / `OrkaNotebookStudioProService` on top of unified Orka state, Mission Control, Study Coach, Exam War Room, Source / Wiki Pro, AI Study Room, existing Notebook Studio packs, learning artifacts, Wiki/source evidence, and export preview metadata.
    - Adds `OrkaNotebookStudioProDto` and compact pack/artifact/action/warning/export-preview/evidence-link DTOs for the future Notebook Studio / Artifact Pro UI.
    - Adds `/api/notebook-studio/pro` and includes compact `NotebookStudioPro` metadata in `/api/dashboard/today`.
    - Notebook Studio Pro recommends repair, review, exam outcome, deneme mistake, source study, Wiki cleanup, Study Room summary, flashcard, checkpoint quiz, slide outline, audio script, and artifact collection packs from existing durable evidence.
    - Export behavior remains preview-only: deterministic manifest/outline metadata can be surfaced, but real PPTX/video generation is not implemented or claimed.
    - Provider output and Wiki memory alone are not citation evidence; source-backed artifact claims are downgraded when source/citation evidence is stale, insufficient, degraded, or missing.
    - `StudentSimulationEvaluationTests` now includes Notebook Studio Pro in serialized public payload sweeps and mixed Learning OS consistency checks.
    - No new AI/provider calls, paid provider calls, OpenAI Responses/Agents/Realtime migration, Google Cloud, Stripe/payment feature, migration, frontend redesign, real PPTX/video generation, therapy/medical/psychological claim, official/source-grounded overclaim, success guarantee claim, or teacher/classroom management workflow was added.
  - Phase 8 - Code Learning IDE + Tool Runtime Polish Backend - implemented pending full validation
    - Adds deterministic `IOrkaCodeLearningIdeService` / `OrkaCodeLearningIdeService` on top of unified Orka state, Mission Control, Study Coach, Tutor handoffs, quiz/mastery/review/memory, Wiki traces, Notebook Studio Pro, and tool capability metadata.
    - Adds `OrkaCodeLearningIdeDto` and compact runtime-readiness, session, exercise, attempt, action, handoff, and warning DTOs for the future Code Learning IDE UI.
    - Adds `GET /api/code/learning-ide` and includes compact `CodeLearningIde` metadata in `/api/dashboard/today`.
    - Code Learning IDE recommends deterministic coding actions for quick start, syntax repair, runtime error repair, test failure repair, blank/no-attempt diagnostic, weak coding concept practice, due review, checkpoint, and stable continuation.
    - Existing code run responses and learning-signal payloads are sanitized before public return so stack traces, local paths, secrets, raw tool/debug markers, and unsafe identifiers are redacted.
    - Notebook Studio Pro can surface code repair and code checkpoint pack handoffs from existing code learning signals without creating hidden artifacts.
    - `StudentSimulationEvaluationTests` now includes Code Learning IDE in serialized public payload sweeps and mixed Learning OS consistency checks.
    - No new AI/provider calls, paid provider calls, OpenAI Responses/Agents/Realtime migration, Google Cloud, Stripe/payment feature, migration, frontend redesign, unsafe runtime permission expansion, official/success guarantee claim, or teacher/classroom management workflow was added.
  - Phase 9 - Unified Evaluation / CI / Release Harness - implemented pending full validation
    - Adds deterministic `IOrkaUnifiedEvaluationService` / `OrkaUnifiedEvaluationService` as a provider-free release harness over Unified State, Mission Control, Study Coach, Exam War Room, Source / Wiki Pro, AI Study Room, Notebook Studio Pro, Code Learning IDE, Tutor policy, quiz/mastery/review/memory readiness, and safety/privacy gates.
    - Adds `OrkaUnifiedEvaluationDto`, scenario result, scorecard, check, warning, safety sweep, and release gate summary DTOs with status/reason-code/user-safe summaries only.
    - Strengthens `StudentSimulationEvaluationTests` coverage through the new `OrkaUnifiedEvaluationHarnessTests`, which validates module consistency, public DTO safety sweeps, cross-user endpoint blocking, and release script coverage.
    - `scripts/quick-backend.ps1` now includes a Product Coherence release proof group for Phase 1-9 backend coherence tests; `RegressionGateScriptTests` locks that local release gate.
    - The harness remains deterministic/provider-free by default, uses no AI judge, calls no paid providers, adds no frontend redesign, and makes no official/source-grounded/success guarantee claims.
    - It blocks raw prompts, provider payloads, source chunks, tool/debug payloads, local paths, secrets, owner/user ids, stack traces, raw transcripts, and pre-submit answer keys from public evaluation payloads.
  - Phase 10 - UX Research / Product Map - implemented and locally validated
    - Adds product architecture docs under `docs/product/` for Orka's post-backend Learning OS frontend direction.
    - Defines the Orka product map, main screens, beta cutline, primary experience loop, work modes, and visible handoff model before Phase 11 frontend redesign.
    - Adds a backend-to-frontend contract matrix mapping Home, Tutor, Study Room, Review/Quiz, Exam War Room, Sources/Wiki Pro, Notebook Studio, Code Learning IDE, Progress, and Settings to backend endpoints/DTOs and safe UI states.
    - Adds learner journey maps for new learner, repeated wrong, blank/skipped, improving, forgotten/due review, exam prep, source/wiki, Study Room, Notebook/artifact, code learning, and mixed Learning OS journeys.
    - Adds the Phase 11 frontend redesign brief, existing frontend audit, and product readiness scorecard.
    - This phase is documentation/product architecture only: no frontend implementation, no migration, no provider call, no payment/subscription feature, no teacher/classroom/dershane management workflow, and no official/source-grounded/success guarantee claim.
  - Phase 11 - Frontend Redesign / Product Beta Polish - implemented pending full validation
    - Makes Home / Mission Control the first logged-in student surface in `Orka-Front/src/pages/Home.tsx`.
    - Adds typed frontend API wrappers and DTOs for Unified State, Mission Control, Study Coach, Exam War Room, Source / Wiki Pro, AI Study Room, Notebook Studio Pro, and Code Learning IDE.
    - Adds compact frontend work-mode panels for Home, Study Room, Exam War Room, Sources / Wiki Pro, Notebook Studio Pro, and Code Learning IDE while preserving Tutor, Review/Quiz, Progress, and existing detailed modules.
    - Updates navigation to the product map: Home, Tutor, Study Room, Review, Exams, Sources/Wiki, Notebook, Code, Progress, and Settings.
    - Keeps Study Room/Classroom as a personal AI study room only.
    - Adds no new AI/provider calls, OpenAI Responses/Agents/Realtime migration, Google Cloud, Stripe/payment feature, mobile app, unsafe runtime expansion, teacher/classroom/dershane management workflow, official/source-grounded overclaim, or success guarantee claim.
  - Remaining:
    - Product Coherence Phase 1-11 validation and beta user testing.

## OrkaLM Phase 24-25 Closure Summary

- Final audit result: PASS.
- Product outcome:
  - Wiki remains the concept/page learning notebook.
  - OrkaLM remains the source-centered notebook surface.
  - Notebook Studio consumes Wiki/source/topic packs, learning artifacts, source-study summaries, Q&A review state, and deterministic export contracts.
  - Source evidence and citation review control source-grounded labels across ask-source, compare, Q&A memory, source-study summary, packs, and artifacts.
- Mini fix:
  - Public source quality/retrieval DTOs no longer expose owner/user ids.
  - Source page evidence DTOs no longer return raw chunk text or raw highlight snippets.
- Safety:
  - no raw prompt/provider/tool/debug/source chunk/local path/secret/owner id/answer key public surface in audited OrkaLM flows.
  - no fake PPTX/video/full NotebookLM/semantic contradiction/classroom/success claim.
- Next phase:
  - Phase 26 should be Final Selective Staging / Commit Preparation only.

## Stage 4 Closure Summary

- 10 discovery feature tamamlandi.
- 4 original pack tamamlandi:
  - Pack 1 - Learning Guidance Pack
  - Pack 2 - Coordination Visibility Pack
  - Pack 3 - Evidence Trust Pack
  - Pack 4 - Wiki Study Pack
- Mini fix gerekmedi.
- Validation gecti:
  - git status clean
  - npm run typecheck
  - npm run quick:smoke
  - RegressionGateScriptTests 5/5
  - git diff --check

## Stage 6B Closure Summary

- Closure doc: `docs/project-state/stage-6b-closure.md`
- Final audit: PASS
- Mini fix gerekmedi.
- Product outcome:
  - Central Exams Orka icinde entegre ogrenci-facing modul olarak tamamlandi.
  - KPSS calisan ilk sinav: study home, practice, persisted result/learning loop, mini-deneme.
  - YKS/LGS/YDS safe scaffold / coming-soon entry olarak eklendi.
  - Source-grounded question draft generation deterministic review-only seam olarak eklendi.
- Safety:
  - verified source metadata olmadan official curriculum claim yok.
  - official OSYM/MEB simulation claim yok.
  - success guarantee yok.
  - copyrighted/scraped content assumption yok.
  - Central Exams icinde PDF/OCR/NotebookLM dependency yok.
  - teacher/classroom/dershane workflow yok.
  - generated/imported content auto-publish edilmiyor.
- Validation gecti:
  - Pack 1-8 targeted backend tests
  - RegressionGateScriptTests
  - scripts/quick-coordination.ps1
  - scripts/quick-backend.ps1
  - Orka-Front npm run typecheck
  - Orka-Front npm run quick:smoke
  - git diff --check, sadece mevcut CRLF normalization warningleri

## Post-6B Professionalization Closure Summary

- Closure doc: `docs/project-state/post-6b-professionalization-closure.md`
- Final audit: PASS
- Mini fix: migration scope consistency duzeltildi; Content Ops tabloları `AddContentOperationsLite`, analytics tabloları `AddQuestionQualityAnalytics` icinde.
- Product outcome:
  - Curriculum/source registry ve official claim gate sertlestirildi.
  - Rich question, stimulus, asset ve accessibility modeli eklendi.
  - Rich package import preview/approval ve standards adapter seam eklendi.
  - Content Operations Lite review, publish readiness ve audit trail eklendi.
  - KPSS Turkce Paragraf pilot student flow Central Exams paneline baglandi.
  - Question quality analytics, item calibration ve coverage temeli eklendi.
- Safety:
  - verified metadata olmadan official curriculum claim yok.
  - official OSYM/MEB simulation claim yok.
  - success guarantee yok.
  - copyrighted/scraped content assumption yok.
  - PDF/OCR/NotebookLM dependency yok.
  - teacher/classroom/dershane workflow yok.
  - imported/generated content auto-publish edilmiyor.
  - score/net/ranking/percentile/placement yok.
- Validation gecti:
  - Post-6B A-F targeted backend tests
  - RegressionGateScriptTests
  - scripts/quick-coordination.ps1
  - scripts/quick-backend.ps1
  - Orka-Front npm run typecheck
  - Orka-Front npm run quick:smoke
  - git diff --check, sadece mevcut CRLF normalization warningleri

## Main Learning OS Professionalization

- Architecture contract map: `docs/architecture/orka-learning-os-contract-map.md`
- Purpose:
  - Orka ana ogrenci-facing learning system olarak kalir.
  - Tutor pedagojik sahiplik merkezidir.
  - Korteks arastirir; Tutor ogretir.
  - RAG/Wiki kaynakli bilgi workspace'idir.
  - Quiz/assessment kavram ve misconception olcer.
  - Tool'lar kontrolsuz agent magic degil, governance/ledger/telemetry ile calisan capability'lerdir.
  - Central Exams yalnizca Orka icindeki domain moduludur.
- Kernel / tool runtime karari:
  - Semantic Kernel, LLM-plugin/tool bridge ve adapter katmani olarak kalabilir.
  - Tool yetkisi, policy, ledger, user/session/topic/correlation ve fallback semantigi Orka tool runtime tarafinda olmalidir.
  - Tutor tool kullaniminda hedef kaynak `TutorToolOrchestrator` / future unified tool runtime'dir; Kernel tek basina authority degildir.

### Locked Implementation Packs

1. ActiveLessonSnapshot & StudentContext Contract
2. Unified Tool Runtime & Kernel Governance
3. Korteks Research Workflow & Synthesis Contract
4. RAG / Source / Wiki Knowledge Lifecycle
5. Plan Quality & Curriculum Sequencing
6. Quiz / Assessment Quality & Misconception Engine
7. Tutor Pedagogy & Response Policy Closure
8. Learning Artifacts Engine
9. Frontend Learning Workspace Synchronization
10. Observability, Cost & Runtime Telemetry
11. Agentic Security & Trust Hardening

Final audit:

- Main Learning OS Professionalization Final Audit + Closure
- Final audit implementation pack degildir.
- Closure doc: `docs/project-state/main-learning-os-professionalization-closure.md`
- Current final audit result: PASS.
- Final mini-fix closed the legacy chat/general quiz answer-key path:
  `QuizCard`/`quizParser` strip pre-submit answer keys, `/api/quiz/attempt`
  ignores client correctness, durable quiz items are server-authoritative, and
  no-key legacy attempts are observed-only.
- Ready for user-directed final selective staging/commit.

## OrkaLM / Wiki-aware Notebook Studio Closure

- Closure doc: `docs/project-state/orka-notebook-studio-closure.md`
- Final audit doc: `docs/project-state/orka-notebook-studio-final-audit.md`
- Final audit result: PASS
- Product outcome:
  - Wiki remains an Obsidian-like page graph, not a daily summary feature.
  - OrkaLM packs can be generated from a selected Wiki page or topic/milestone context.
  - Packs carry source readiness, completed concepts, weak concepts, misconception signals, artifact ids, warnings, and next actions.
  - Notebook artifacts include study guide, briefing doc, source digest, repair pack, worked examples, retrieval cards, audio overview/script/transcript/caption, mind map, flashcards, review quiz blueprint, slide outline, video-ready package, and slide export manifest.
  - Slide export is deterministic and safe: preview, Markdown, escaped HTML, and manifest-only export packages are supported; Phase 13 hardened the deck preview UX and confirmed PPTX generation remains explicitly disabled until a safe local export dependency is approved.
  - Uploaded sources feed OrkaLM through the source evidence lifecycle; insufficient evidence cannot be presented as source-grounded.
  - Phase 14 verified the final professional closure: Wiki page-aware packs, source evidence, artifacts, audio fallback, mind map, flashcards/SRS, review quiz safety, slide/export UX, privacy/trust, docs, tests, and migration/model alignment are acceptable for closure.
  - Phase 15 productizes the Wiki vault UX: page tree/list, search/filter, active page context badges, backlinks/outgoing links, local graph neighbors, block group summaries, and page-aware Notebook Studio remain visible in one student-facing Wiki surface.
  - Phase 16 adds a canonical Wiki Learning Trace Writer so Tutor turns, student questions, quiz outcomes, misconception/repair signals, Notebook artifacts, and safe source notes can write page-aware, deduped Wiki blocks without new AI calls or raw payload leakage.
  - Phase 17 adds the dedicated OrkaLM source notebook UX: uploaded sources now have safe source-notebook summary endpoints, source-centered pack creation, `orkalm_source` Wiki pages, source readiness/citation warnings, and `NotebookStudioPanel` source-pack actions without exposing raw source chunks.
  - Phase 18 adds deterministic source-to-concept linking: uploaded sources can sync safe `WikiLink` rows to concept pages, OrkaLM shows source-concept graph summaries, Wiki concept pages show supporting sources, and Notebook packs carry linked source/concept metadata without new AI calls or raw source exposure.
  - Phase 19 adds the advanced ask-source UX: selected-source and source-collection questions now use `SourceQuestionService`, return citation-safe DTOs with source basis/readiness/warnings, can write deduped Wiki trace blocks, and never expose raw chunks or provider/debug payloads.
  - Phase 20 adds deterministic multi-source compare and citation review: selected sources are compared by readiness, evidence status, citation coverage, and source-to-concept overlap; citation review exposes supported/unsupported/missing/stale states without raw answer, claim, chunk, provider, or path payloads.
  - Phase 21 adds source Q&A conversation memory: selected-source/source-collection questions can be stored as bounded `source_question_thread` LearningArtifacts, continued with safe prior summaries, reviewed/degraded by citation state, written to Wiki on request, and included in source Notebook pack summaries without raw chunk/provider/prompt/debug payloads.
  - Phase 22-23 adds a compact source-study workflow layer: OrkaLM now exposes a safe source study summary over Q&A threads, citation review, source readiness, and source-to-concept links so the UI can show review/degraded/citation-warning counts, linked concept counts, compare readiness, and next actions without new AI calls or raw source storage.
  - Phase 27 is post-closure polish, not new OrkaLM scope: it performs a global public DTO/API projection privacy sweep, aligns legacy classroom wording with personal audio lesson language, attempts Browser visual E2E where tooling/auth allows it, reviews frontend bundle output, and cleans behaviorless provider nullable warnings.
  - Release-blocker cleanup after the whole-system learning intelligence audit removes student-facing certainty/guarantee copy, hardens refresh-token and chaos-header regression determinism, strengthens blank/skipped answer prerequisite repair semantics, and adds deterministic provider-free smoke proof for the learning loop without adding AI/provider calls.
  - Pedagogical Productization Phase 6 adds Wiki Copilot as a page-aware helper layer: safe `WikiCopilotContextDto`, deterministic suggestions, read-only `/api/wiki/page/{pageId}/copilot`, compact Wiki panel, and Tutor/quiz/source/Notebook Studio handoff guidance without new AI/provider calls or raw payload exposure.
- Safety:
  - raw source chunks, prompts, provider payloads, raw tool payloads, debug traces, local paths, secrets, and pre-submit answer keys are not exposed.
  - official curriculum/exam readiness and success guarantees remain blocked.
  - broad legacy service deletion is intentionally out of scope; compatibility adapters are documented.
- Backlog:
  - advanced visual mind map editor, full review quiz wizard from pack, interactive voice, themed deck export, real PPTX export, and real video generation remain future work.

## Roadmap Rule

- Bu sira kullanici onayi olmadan degistirilmeyecek.
- Frontend Corporate Baseline bu backend roadmap'in resmi maddesi degildir.
- Main Learning OS Professionalization is closed; next work requires explicit
  user direction.
- OrkaLM / Wiki-aware Notebook Studio is closed as a safe foundation; future
  NotebookLM-parity media/export work requires explicit user direction.
- Phase 15 Wiki Vault UX productization is a frontend hardening layer on top of
  the closed OrkaLM foundation; it does not add AI calls, Google Cloud, real
  PPTX/video generation, or a full Obsidian clone.
- Phase 17 OrkaLM source notebook UX is a source-centered product layer on the
  shared Notebook Studio engine; it does not add provider calls, full
  NotebookLM parity, real PPTX/video generation, or a separate app.
- Phase 18 source-to-concept linking is deterministic and safe-first; low
  confidence links remain suggestions, source evidence controls source-backed
  labels, and no raw source chunks are exposed.
- Phase 19 ask-source UX reuses existing source/RAG/Tutor infrastructure; it
  adds no provider calls, labels evidence-insufficient answers honestly, and
  keeps raw chunks/prompts/provider payloads out of public DTOs and UI.
- Phase 21 source Q&A memory uses safe summaries only; follow-up context never
  persists raw chunks/prompts/provider/tool/debug payloads, and unsupported
  citation states remain review/degraded labels rather than source-backed truth.
- Phase 22-23 source-study workflow derives status from existing source Q&A
  memory, citation checks, compare/source graph context, and source lifecycle;
  it adds no scheduler, raw transcript store, semantic contradiction detector,
  or provider call.
- Phase 27 post-closure polish may only fix privacy/copy/build/warning issues;
  it must not add product scope, provider calls, real PPTX/video generation,
  interactive voice, graph canvas editing, or teacher/classroom/dershane
  workflows.
- Release-blocker cleanup after the learning intelligence audit is limited to
  copy safety, deterministic regression proof, provider-free smoke coverage, and
  blank/skipped answer repair semantics. It must not add new providers or claim
  official/success guarantees.
- Backend Release Hardening Phase 1 hardens AI/provider logging: raw provider
  requests, responses, prompts, source chunks, tool payloads, stack traces, and
  secrets are not written by `AiDebugLogger`; file logging is disabled by
  default and development-only opt-in. Provider error logs retain status/length
  diagnostics instead of raw bodies. Final zero-leak hardening also keeps
  provider failure diagnostics body-free: `RedactedDiagnostic` stores safe
  status/category/body length/hash metadata only, not redacted body excerpts or
  arbitrary learner/source text echoed by a provider. No AI/provider calls or
  OpenAI API migration were added.
- Backend Release Hardening Phase 2 adds the provider-free backend lifetest
  release proof to `scripts/quick-backend.ps1`: `BackendLifeTests` and
  `PedagogicalReleaseClosureTests` now run before the stabilization and
  coordination baselines, proving auth, topic/goal, diagnostic/plan, Tutor,
  quiz/remediation, mastery/snapshot, Wiki/Copilot, source evidence, Notebook
  Studio, dashboard, degraded states, and public-payload safety without paid
  provider calls.
- Backend Release Hardening Phase 3 polishes the backend release test host:
  `ApiSmokeFactory` now filters only noisy test categories such as EF
  in-memory info logs, disabled worker info logs, background queue lifecycle
  info logs, and the MediatR license banner. Warning/error logs remain visible,
  quick scripts remain provider-free, and no production logging behavior,
  AI/provider calls, or OpenAI API migration were added.
- Backend Release Hardening Phase 4 adds GitHub CI / PR closure alignment:
  `.github/workflows/backend-release.yml` now runs the provider-free backend
  release proof on Windows with .NET 8, SQL Server LocalDB preparation,
  `scripts/quick-backend.ps1`, Infrastructure unit tests, and `git diff --check`.
  The local checkout has no active GitHub Actions run history yet and the
  current branch has no open PR, so CI pass/fail is established by this workflow
  definition plus local validation until a remote run is triggered. No
  AI/provider calls, paid provider validation, or OpenAI API migration were
  added.
- Backend Production Readiness Phase 1 hardens production log privacy:
  application logs now prefer masked/correlated refs (`UserRef`, `TopicRef`,
  `SessionRef`, `MessageRef`, `KeyRef`) instead of raw learner/topic/session
  identifiers in the core learning flow. `LogPrivacyGuard` provides stable
  non-reversible refs plus bounded safe messages and exception-type-only
  diagnostics. Production-facing log paths touched in Tutor, quiz, Wiki,
  source/notebook, planning, Redis/cache, runtime telemetry, and background
  job services no longer intentionally write raw prompts, provider bodies,
  source chunks, tool payloads, answer keys, local paths, stack traces, owner
  ids, or unsafe user ids. AI debug logging remains disabled by default and
  development-only opt-in. No new AI/provider calls or OpenAI API migration
  were added.
- Backend Production Readiness Phase 2 audits live/staging provider readiness:
  provider configuration is mapped without printing secrets, missing live AI
  credentials block success-call proof honestly, `ExternalProviderIntegrationTests`
  remain explicit opt-in, invalid-token failure checks are safe, and keyless
  public reference providers can be smoke-checked without joining the
  deterministic quick baseline. Cohere, HuggingFace, and Cohere embedding
  failures now use body-free provider diagnostics, and startup migration failure
  logging keeps exception type only. No new product provider path or OpenAI API
  migration was added.
- Backend Production Readiness Phase 3 closes the scale/ops pass: database
  index coverage, background queues/workers, Redis stream trim guards, upload
  and source chunk limits, provider timeout/retry/cost gates, production startup
  safety, and quick script/CI alignment were reviewed. A small production
  scalability fix moved audio-retention readiness summaries from materializing
  all audio rows/byte payloads to aggregate DB queries. Provider-free CI remains
  the default release baseline; live provider checks remain explicit opt-in and
  must not be added to quick scripts or CI. No migration, provider architecture
  change, OpenAI API migration, or new AI feature was added.
- Pedagogical Productization Phase 6 Wiki Copilot is deterministic and
  page-aware. It may suggest safe handoffs to Tutor, Quiz, source review,
  repair, curation, and Notebook Studio, but it must not execute hidden
  autonomous actions, add provider calls, expose raw payloads, or claim source
  grounding without evidence.
- Central Exams pilot productization readiness onceki faz olarak tamamlanmistir.
- Yeni ara asama icat edilmeyecek.
- Stage 6C, global exam implementation veya teacher/institutional feature kullanici onayi olmadan baslatilmayacak.

## Codex Rule

- Yeni chat/branch basinda once bu dosya ve `docs/codex-skills/README.md` okunacak.
- Feature isi yapmadan once ilgili constitution dosyalari okunacak.
- Stage/commit kullanici acikca istemeden yapilmayacak.
- Post-6B Professionalization tamamlandi.
- Main Learning OS Professionalization sirasinda once `docs/architecture/orka-learning-os-contract-map.md`
  okunacak; Pack 1-11 sirasi kullanici onayi olmadan degistirilmeyecek.
- Content Ops/Admin Lite UI, asset delivery hardening, KPSS pilot content, KPSS
  user-flow polish ve import standards hardening bu ana Learning OS tamiri
  kapatilmadan ayrica baslatilmayacak.
