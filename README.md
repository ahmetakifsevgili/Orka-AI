# Orka AI Learning OS

Orka is a personal AI learning operating system for students. It connects Tutor,
Mission Control, Study Rhythm Coach, Exam War Room, Wiki, OrkaLM source study,
AI Study Room, Notebook Studio Pro, Code Learning IDE, quiz, review, mastery,
memory, dashboard, and release evaluation into one coherent learning product.

This repository is now in the **OrkaOS v1 professional closure** state. The
current codebase separates Wiki and OrkaLM as independent learning surfaces,
keeps source upload only inside OrkaLM, and provides feature-parity contracts for
study artifacts, metadata, graph, slide, diagram, export, and audio study flows
without cross-surface sync.

The current readiness label is **professional controlled release ready**:
security/privacy gates, strict AI fallback behavior, diagnostic quality gates,
Notebook Studio parity, audio context isolation, frontend browser evidence, and
OrkaOS v1 documentation are in place. Public production launch still requires
deployment-specific operations, monitoring, backup, quota, and live provider
quality review.

## Current Verdict

After the OrkaOS v1 closure:

| Area | Status | Notes |
| --- | --- | --- |
| Backend richness | Excellent | Learning contracts, Notebook Studio parity, diagnostic quality gates, source/Wiki isolation, audio context contracts, and release services are registered. |
| Cross-module coherence | High | Tutor, Mission Control, Study Coach, Exam War Room, Wiki, OrkaLM, Study Room, Notebook, Code IDE, Dashboard, quiz/review/memory share deterministic contracts. |
| Wiki / OrkaLM separation | Explicit | Wiki remains normal lesson flow; OrkaLM owns source upload and source notebook study. The two surfaces share feature contracts but do not sync or feed each other. |
| Frontend professional shell | Ready for controlled release | Browser-backed flows cover Notebook Studio parity, slide/UML/export previews, audio card state, captions, and classroom ask payload isolation. |
| Safety/privacy | High | Public contracts avoid raw prompts, provider payloads, source chunks, tool payloads, stack traces, local paths, secrets, unsafe ids, raw transcripts, refresh tokens, and pre-submit answer keys. |
| Release confidence | High for professional controlled release | Full API, infrastructure unit, frontend typecheck/build/smoke, and Playwright release evidence passed locally. Live provider checks remain explicit opt-in. |

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
   Quiz, Exam War Room, Wiki, OrkaLM, Notebook Studio, Code IDE, or Progress.
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
| Wiki | Wiki / Notebook feature contracts | Wiki APIs and frontend Wiki surface | Normal lesson flow, concept pages, graph, metadata, text artifacts, slides, diagrams, export previews, and Wiki-scoped audio study. |
| OrkaLM | Source notebook feature contracts | Source and Notebook Studio APIs | Source upload, citations, source notebook study, graph, metadata, text artifacts, slides, diagrams, export previews, and source-scoped audio study. |
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
- Wiki
- OrkaLM
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

dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --no-restore -m:1 -v:minimal
dotnet test .\Orka.Infrastructure.UnitTests\Orka.Infrastructure.UnitTests.csproj --no-restore -m:1 -v:minimal

powershell -ExecutionPolicy Bypass -File scripts\quick-backend.ps1
powershell -ExecutionPolicy Bypass -File scripts\quick-coordination.ps1

cd Orka-Front
npm run typecheck
npm run smoke:ui
npm run smoke:contracts
npm run smoke:security
npm run build
$env:PLAYWRIGHT_PORT='3108'; npx playwright test e2e/notebook-studio-contract.spec.ts --reporter=list
$env:PLAYWRIGHT_PORT='3109'; npx playwright test --reporter=list
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
npm run smoke:ui
npm run smoke:contracts
npm run smoke:security
npm run build
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
- `OrkaOSv1/01-OrkaOSv1-Arastirma-Pazar-Konumlandirma.md` - market, audience, and positioning research.
- `OrkaOSv1/02-OrkaOSv1-Sistem-Mimarisi-Calisma-Prensipleri.md` - system architecture and operating principles.
- `OrkaOSv1/03-OrkaOSv1-Ozellik-Katalogu-Model-Sistem-Baglantilari.md` - feature catalog, model roles, and system links.
- `OrkaOSv1/04-OrkaOSv1-UML-Diyagramlar-Roadmap.md` - detailed and aggregate Mermaid/UML diagrams plus roadmap.
- `OrkaOSv1/05-Dirty-Worktree-Commit-PR-Ayrim-Plani.md` - commit/PR split plan for the closure worktree.

## Known Limits Before Wider Production

Professional controlled release is credible, but these remain important before broad production:

- Run deployment-specific monitoring, backup/restore, provider quota, and secret
  rotation checks.
- Keep human-reviewed live Tutor/provider quality evaluation as an explicit
  launch gate.
- Continue browser visual QA with representative seeded learner data across key
  breakpoints after every major frontend pass.
- Keep live provider checks opt-in and separate from CI unless a release
  decision explicitly enables them.
- Treat any future Wiki-OrkaLM sync/feed as a separate architecture phase; the
  current system intentionally keeps those surfaces isolated.

## License

MIT - Ahmet Akif Sevgili
