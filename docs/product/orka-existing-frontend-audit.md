# Orka Existing Frontend Audit

Status: Product Coherence Phase 11 updated.

This audit originally documented what existed before Phase 11. It now also
records the Phase 11 frontend beta shell changes.

## Current Route Shape

`Orka-Front/src/App.tsx` exposes:

- `/`
- `/login`
- `/app`
- `/profile`
- `/courses`

`/app` renders `Home`, which is currently a single app shell with internal
`activeView` switching rather than product-level routes.

## Current App Views

Before Phase 11, `Home.tsx` valid views included:

- `dashboard`
- `chat`
- `settings`
- `wiki`
- `orkalm`
- `ide`
- `learning`
- `sources`
- `practice`
- `progress`
- `central-exams`

Rendered panels include:

- `DashboardPanel`
- `ChatPanel`
- `WikiMainPanel`
- `CentralExamsPanel`
- `LearningPanel`
- `InteractiveIDE`
- `SettingsPanel`
- `NotebookStudioPanel` inside Wiki/OrkaLM surfaces

## What Is Useful

- Auth/protected route shell already exists.
- Topic/session persistence already exists.
- Chat, Wiki/source, exam, learning/review, IDE, dashboard, and notebook panels
  provide reusable domain code.
- Existing API client includes many legacy source/wiki/exam/notebook/code
  endpoints.
- Existing UI already has safety-aware copy in several places, such as source
  warning labels and preview-only export language.

## What Is Stale Or Missing

- Frontend does not yet consume the Phase 1-9 contract names:
  `OrkaLearningState`, `MissionControl`, `StudyCoach`, `ExamWarRoom`,
  `SourceWikiPro`, `StudyRoom`, `NotebookStudioPro`, `CodeLearningIde`, or
  `UnifiedEvaluation`.
- Dashboard uses `DashboardAPI.getToday()` but frontend `DashboardTodayDto` is
  older and does not model the full Phase 1-9 compact payload.
- `CentralExamsPanel` uses study-home/practice flows, not the War Room contract.
- `WikiMainPanel` is powerful but dense; Source / Wiki Pro needs a clearer
  evidence command center.
- `NotebookStudioPanel` works with existing packs, but not the Pro contract.
- `InteractiveIDE` is a utility/practice surface, not a Code Learning IDE
  contract consumer.
- Study Room is not a first-class product route; audio/classroom affordances can
  confuse the personal study room meaning.
- Internal view switching makes deep linking and route-level product ownership
  harder.

## Reuse Candidates

- Keep auth, topic/session plumbing, API base client, toast handling, and safe
  storage helpers.
- Reuse ChatPanel messaging logic inside a Tutor route.
- Reuse Wiki/source components inside a Source / Wiki Pro shell after adding the
  Pro overview contract.
- Reuse CentralExamsPanel pieces inside Exam War Room.
- Reuse LearningPanel/QuizCard for Review / Quiz.
- Reuse NotebookStudioPanel after adding Pro overview and pack recommendations.
- Reuse InteractiveIDE after adding Code Learning IDE readiness and repair
  states.

## Redesign Candidates

- Replace default `/app` dashboard with Home / Mission Control.
- Promote Study Room to a first-class navigation item.
- Split Progress from Home.
- Rename or clarify `orkalm`/`sources` into Sources / Wiki Pro.
- Move Code IDE from generic practice/IDE framing into Code Learning IDE.
- Add consistent module cards, warning chips, reason labels, and handoff buttons.

## Postpone Or Remove From Beta

- Marketing landing as the logged-in main experience.
- Teacher/classroom/school management semantics.
- Payment/subscription surfaces.
- Realtime voice promises.
- Real PPTX/video export claims.
- Debug/release harness student screens.
- Any source-backed/official/success claims without evidence.

## Phase 11 Frontend Gap Summary

The existing frontend is useful as a component and workflow base, but it is not
yet mapped to the new Learning OS contracts. Phase 11 should not throw it away;
it should reframe it around Mission Control and make each module a clear
student work mode.

## Phase 11 Update

Phase 11 reframed the existing frontend around the Product Coherence map:

- `/app` now defaults to Home / Mission Control.
- `Home.tsx` normalizes old internal view ids into the beta work modes:
  Home, Tutor, Study Room, Review, Exams, Sources/Wiki, Notebook, Code,
  Progress, and Settings.
- `LeftSidebar.tsx` exposes those work modes directly.
- `src/lib/types.ts` and `src/services/api.ts` now include typed wrappers for
  Unified State, Mission Control, Study Coach, Exam War Room, Source / Wiki Pro,
  AI Study Room, Notebook Studio Pro, and Code Learning IDE.
- `ProductCoherencePanels.tsx` adds compact contract-driven panels for Home,
  Study Room, Exam War Room, Sources / Wiki Pro, Notebook Studio Pro, and Code
  Learning IDE.
- Existing Tutor, Review/Quiz, Progress, Wiki/source, exam, notebook, and IDE
  surfaces remain reusable rather than deleted.

No new provider calls, payment flows, Realtime voice, mobile app, unsafe runtime
expansion, official/source-grounded overclaim, success guarantee, or
teacher/classroom management workflow was added.
