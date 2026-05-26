# Orka AI Learning OS

Orka is a personal AI learning operating system for students. It connects Tutor,
Mission Control, Study Rhythm Coach, Exam War Room, Source / Wiki Pro, AI Study
Room, Notebook Studio Pro, Code Learning IDE, quiz, review, mastery, memory,
dashboard, and release evaluation into one coherent learning product.

This repository is currently in **Product Coherence Phase 1-11 closure**. The
backend is feature-rich and strongly connected; the frontend now exposes a beta
Learning OS shell. The correct readiness label is **controlled beta ready**, not
unrestricted production-ready.

## Current Verdict

After the Product Coherence scan:

| Area | Status | Notes |
| --- | --- | --- |
| Backend richness | Excellent | Phase 1-9 contracts exist and are registered in the API container. |
| Cross-module coherence | High | Unified state, Mission Control, Study Coach, Exam War Room, Source/Wiki, Study Room, Notebook, Code IDE, Tutor, Dashboard, quiz/review/memory share deterministic contracts. |
| Frontend beta shell | Mostly ready | Home / Mission Control is now the first logged-in surface and routes to the core work modes. Some deeper legacy surfaces still need UX polish. |
| Safety/privacy | High | Public contracts are designed to avoid raw prompts, provider payloads, source chunks, tool payloads, stack traces, local paths, secrets, unsafe ids, raw transcripts, and pre-submit answer keys. |
| Release confidence | High for controlled beta | Provider-free tests and quick scripts cover Product Coherence gates. Live provider checks remain explicit opt-in. |

No official exam success, score, percentile, placement, curriculum alignment, or
guarantee claim is made by this README or by the Product Coherence contracts.

### Backend-Only Audit Scores

The current backend/product-core assessment, excluding frontend visual polish, is:

| Area | Score | Meaning |
| --- | ---: | --- |
| Backend feature richness | 9.1/10 | The Learning OS has broad, connected contracts for Tutor, Mission Control, Study Coach, Exam, Source/Wiki, Study Room, Notebook, Code IDE, review, memory, dashboard, and evaluation. |
| Cross-module coherence | 8.9/10 | Modules share deterministic state, reason codes, warnings, and handoffs instead of behaving as isolated utilities. |
| Learning intelligence / memory / mastery | 8.6/10 | Signals, mastery, knowledge tracing, memory, SRS, long-term learning, and exam/source profiles feed the OS, but real beta telemetry is still needed to prove day-to-day usefulness. |
| Mission / next-action quality | 8.7/10 | Mission Control and specialized modules can choose actionable next steps, degrade on thin evidence, and surface conflicts. |
| Exam / Source / Wiki / Study Room / Notebook / Code integration | 8.8/10 | Specialized workspaces exchange safe handoffs and warnings through shared DTOs and dashboard aggregation. |
| Safety / privacy / claim guard | 9.3/10 | Public payloads and release gates guard raw prompts, provider data, source chunks, answer keys, stack traces, unsafe ids, secrets, and guarantee claims. |
| Test / evaluation / release harness | 8.8/10 | API, infrastructure, Product Coherence, safety, and quick release scripts pass provider-free validation; one previously observed transient full-suite failure means determinism should still be monitored. |
| Tutor contract / pedagogical policy | 8.2/10 | Policy, orchestration, repair, Socratic behavior, source honesty, and handoffs are strong; live Tutor answer richness still needs human rubric review because default validation avoids provider calls. |
| Production hardening | 7.8/10 | Controlled beta is credible, but wider production still needs operational hardening: live answer-quality review, runtime sandbox deployment checks, monitoring, quotas, backups, secrets policy, and visual/performance QA. |

In short: the backend core is strong enough for controlled beta. The two lower
scores are not missing feature flags; they are the remaining proof and
operations work required before broad production.

### Optimization Targets Behind The Lower Scores

The `7.8/10` production-hardening score means:

- Keep provider-free CI as the default, but add an explicit human-reviewed live
  Tutor answer-quality run before public launch.
- Validate Code Learning IDE runtime isolation, timeouts, resource limits,
  unsupported-language behavior, and stack/local-path/secret redaction in the
  actual deployment environment.
- Keep Redis runtime context summary-only for Code Learning IDE sessions; do not
  cache raw student code, stdout, stderr, tool payloads, stack traces, or local
  paths for Tutor handoff.
- Add production monitoring, backup/restore checks, provider quota handling,
  secret rotation policy, and incident diagnostics that do not leak raw payloads.
- Track the previously observed transient full API suite failure pattern so
  release gates remain deterministic under repeated runs.
- Run fresh browser visual/performance QA with seeded learner states before
  opening beta to non-internal users.

The `8.2/10` Tutor-contract score means:

- The backend contract is strong, but live Tutor prose quality has not been
  proven by a human rubric in this audit because paid/provider calls are not part
  of default validation.
- Repair explanations should be reviewed for specificity, step sequencing,
  misconception handling, and whether they avoid motivational filler.
- Source-grounded Tutor answers should be sampled to verify that citations,
  source-limited warnings, and downgrade behavior feel honest to a real student.
- Tutor handoffs to Study Room, Quiz, Review, Exam War Room, Source/Wiki,
  Notebook, and Code IDE should be tested with real learner examples, not only
  deterministic DTO checks.
- The target before public launch is not more provider plumbing; it is a small,
  repeatable qualitative evaluation set for answer richness and learning value.

## What Orka Is

Orka is not just a chatbot. The intended product loop is:

1. The student opens Home / Mission Control.
2. Orka explains what to do today and why.
3. Orka routes the student to the best work mode: Tutor, Study Room, Review,
   Quiz, Exam War Room, Sources/Wiki, Notebook Studio, Code IDE, or Progress.
4. The action writes safe learning evidence through existing learning signal,
   mastery, memory, review, Wiki, source, artifact, or runtime paths.
5. The next Home state reflects the updated evidence.

The system degrades safely when evidence is thin: it recommends a short
diagnostic, quick start, review, or bounded repair instead of inventing mastery,
misconceptions, source support, or exam readiness.

## Product Coherence Modules

| Module | Backend contract | Main endpoint / surface | Role |
| --- | --- | --- | --- |
| Unified Learning State | `OrkaLearningStateDto` | `GET /api/learning/orka-state` | Central learner state, primary next action, feature readiness, conflicts, safety warnings. |
| Home / Mission Control | `OrkaMissionControlDto` | `GET /api/learning/mission-control` | Daily cockpit: primary mission, secondary actions, warnings, module cards, sections. |
| Study Rhythm Coach | `OrkaStudyCoachDto` | `GET /api/learning/study-coach` | Workload, pace, focus plan, comeback plan, rhythm status. |
| Exam War Room | `OrkaExamWarRoomDto` | `GET /api/central-exams/{examCode}/war-room` | Weak outcomes, deneme clusters, practice queue, exam repair handoffs. |
| Source / Wiki Pro | `OrkaSourceWikiProDto` | `GET /api/sources/wiki-pro` | Evidence readiness, citation warnings, Wiki repair, source-backed vs source-limited state. |
| AI Study Room | `OrkaStudyRoomDto` | `GET /api/classroom/study-room` | Personal AI study session plan, lesson mode, checkpoint plan, safe traces. |
| Notebook Studio Pro | `OrkaNotebookStudioProDto` | `GET /api/notebook-studio/pro` | Repair/review/exam/source/wiki/study-room/code packs and export previews. |
| Code Learning IDE | `OrkaCodeLearningIdeDto` | `GET /api/code/learning-ide` | Coding practice readiness, repeated error repair, safe runtime status. |
| Unified Evaluation | `OrkaUnifiedEvaluationDto` | Service/test harness | Scenario scorecard, consistency checks, safety sweep, release gate summary. |
| Dashboard Today | `DashboardTodayDto` | `GET /api/dashboard/today` | Compact aggregation of unified state and Product Coherence modules. |

Core registrations are in `Orka.API/Program.cs`. DTOs and interfaces live mainly
in `Orka.Core/DTOs/LearningArchitectureDtos.cs`,
`Orka.Core/DTOs/DashboardTodayDtos.cs`, `Orka.Core/DTOs/CentralExamDtos.cs`,
`Orka.Core/DTOs/SourceEvidenceLifecycleDtos.cs`, and
`Orka.Core/Interfaces/ILearningArchitectureServices.cs`.

## Frontend Product Shape

The logged-in app is organized around the Learning OS, not a marketing landing
page. The main beta surfaces are:

- Home / Mission Control
- Tutor
- Study Room
- Review / Quiz
- Exam War Room
- Sources / Wiki Pro
- Notebook Studio
- Code Learning IDE
- Progress / Memory
- Settings / Safety

The Phase 11 frontend work added typed API wrappers for the Product Coherence
contracts and compact panels in `Orka-Front/src/components/ProductCoherencePanels.tsx`.
`Orka-Front/src/pages/Home.tsx` now routes the main app views, and
`Orka-Front/src/services/api.ts` exposes the contract clients.

## Safety Boundaries

These are deliberate product and engineering boundaries:

- No new AI/provider call is required for Product Coherence validation.
- No paid provider call is part of the default release gate.
- No OpenAI Responses API, Agents SDK, or Realtime migration is included.
- No Google Cloud, Stripe/payment, subscription, mobile app, or teacher/admin
  classroom management workflow is included.
- Study Room/Classroom means a personal AI study room, not an institutional
  school or dershane management system.
- No real PPTX/video generation is claimed; Notebook Studio Pro exposes safe
  previews/outline/manifest style artifacts unless a real export path is
  separately implemented and validated.
- No official curriculum/exam alignment is claimed unless verified source
  metadata supports the label.
- No exam success, score, percentile, placement, or guarantee claim is made.
- No therapy, medical, psychological, ADHD, burnout, or wellbeing diagnosis
  claim is made.

Public student-facing DTOs must not expose raw prompts, hidden prompts,
provider payloads, source chunks, Wiki block bodies, tool payloads, debug traces,
local paths, secrets, API keys, tokens, owner ids, unsafe user ids, stack traces,
raw transcripts, pre-submit answer keys, or raw internal JSON dumps.

## Evidence And Release Gates

The Product Coherence release gate is provider-free and deterministic by
default. The main local checks are:

```powershell
cd D:/Orka

dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter "OrkaUnifiedEvaluationHarnessTests|StudentSimulationEvaluationTests|BackendLifeTests|PedagogicalReleaseClosureTests" --no-restore --verbosity minimal
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter "OrkaCodeLearningIdeTests|OrkaNotebookStudioProTests|OrkaStudyRoomTests|OrkaSourceWikiProTests|OrkaExamWarRoomTests|OrkaStudyCoachTests|OrkaMissionControlTests|OrkaLearningStateCoherenceTests" --no-restore --verbosity minimal
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter "PublicSecuritySurfaceTests|AgenticSecurityTrustTests|LearningRuntimeTelemetryTests" --no-restore --verbosity minimal
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter RegressionGateScriptTests --no-restore --verbosity minimal
dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --no-restore --verbosity minimal
dotnet test .\Orka.Infrastructure.UnitTests\Orka.Infrastructure.UnitTests.csproj --no-restore --verbosity minimal

powershell -ExecutionPolicy Bypass -File scripts\quick-backend.ps1
powershell -ExecutionPolicy Bypass -File scripts\quick-coordination.ps1

cd Orka-Front
npm run typecheck
npm run build
npm run quick:smoke
npm run quick:frontend
```

`scripts/quick-backend.ps1` includes the Product Coherence test group:

```text
OrkaUnifiedEvaluationHarnessTests
StudentSimulationEvaluationTests
OrkaCodeLearningIdeTests
OrkaNotebookStudioProTests
OrkaStudyRoomTests
OrkaSourceWikiProTests
OrkaExamWarRoomTests
OrkaStudyCoachTests
OrkaMissionControlTests
OrkaLearningStateCoherenceTests
```

## Local Dev Contract / Development

### Requirements

- .NET 8 SDK
- Node.js 18+
- SQL Server LocalDB or compatible SQL Server configuration
- Redis 7 for Redis-backed paths where enabled

### Run the API

```powershell
cd D:/Orka
powershell -ExecutionPolicy Bypass -File scripts\start-api.ps1
```

Default API URL:

```text
http://localhost:5065
```

### Run the Frontend

```powershell
cd D:/Orka
powershell -ExecutionPolicy Bypass -File scripts\start-front.ps1
```

Default frontend URL:

```text
http://localhost:3000
```

### Direct Frontend Commands

```powershell
cd D:/Orka/Orka-Front
npm run dev
npm run typecheck
npm run build
npm run quick:smoke
npm run quick:frontend
```

## Important Documentation

- `CODEX.md` - Codex workflow and repo rules.
- `docs/project-state/current-roadmap.md` - current roadmap and closure notes.
- `docs/architecture/orka-learning-os-contract-map.md` - Learning OS backend contract map.
- `docs/dev-contract.md` - provider-free dev/test contract.
- `scripts/CHECKLIST.md` - release and safety gates.
- `docs/codex-skills/README.md` - Codex skill constitutions and feature completion workflow.
- `docs/product/orka-product-map.md` - product architecture.
- `docs/product/orka-frontend-contract-map.md` - frontend/backend contract map.
- `docs/product/orka-learner-journeys.md` - learner journey map.
- `docs/product/phase-11-frontend-redesign-brief.md` - frontend beta implementation brief.
- `docs/product/orka-product-readiness-scorecard.md` - readiness/risk scorecard.
- `docs/product/orka-existing-frontend-audit.md` - frontend audit and Phase 11 update.

## Known Limits Before Wider Production

Controlled beta is credible, but these remain important before broad production:

- Run fresh browser visual QA with representative seeded learner data and
  screenshots across key breakpoints.
- Polish deeper legacy Wiki, Review/Quiz, and Progress/Memory surfaces after
  initial beta feedback.
- Expand Study Room checkpoint/session interaction beyond the compact contract
  once real users validate the workflow.
- Keep live provider checks opt-in and separate from CI unless a release
  decision explicitly enables them.
- Review operational deployment, monitoring, backups, provider quotas, and
  production secret policy separately from Product Coherence.

## License

MIT - Ahmet Akif Sevgili
