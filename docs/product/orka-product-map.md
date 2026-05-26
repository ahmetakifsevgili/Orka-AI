# Orka Product Map

Status: Product Coherence Phase 10.

This document maps Orka's completed backend Learning OS into the product shape
that Phase 11 should redesign. It is a product architecture document, not a UI
implementation.

## Product Definition

Orka is a personal learning operating system: a private AI study OS that helps a
student choose the right next learning action, move between work modes, and keep
learning evidence coherent over time.

It is not only a chatbot, not a generic course dashboard, and not a teacher or
institution management product. Study Room/Classroom means a personal AI study
room only.

## Target User

- An individual student who wants a guided learning workflow.
- A learner preparing for exams, repairing weak concepts, reviewing before
  forgetting, studying from sources, building notes/artifacts, or practicing
  code.
- A learner who needs the system to say what to do next, why it matters, and
  where to start.

## Product Promise

Orka can organize learning evidence and recommend safe next study actions.

Orka must not promise official curriculum alignment, score, percentile,
placement, admission, exam success, or guaranteed outcomes. Source-backed,
official, or curriculum-related labels require verified metadata and must be
downgraded when evidence is limited.

## Primary Experience Loop

1. The student opens Home / Mission Control.
2. Orka loads the unified state, today's mission, rhythm, warnings, and module
   readiness.
3. The student chooses a visible handoff: Tutor, Study Room, Review, Exam,
   Sources/Wiki, Notebook Studio, Quiz, Code IDE, or Progress.
4. The selected work mode records safe evidence: correct/wrong/blank answers,
   review completion, source status, wiki repair, study room checkpoints,
   notebook packs, code attempts, or exam practice.
5. The unified state and Mission Control update the next action.

The product should feel like: "Orka knows my learning state and tells me the
best next work mode."

## Main Screens

| Screen | Route suggestion | Primary backend contract | Purpose | Beta priority |
|---|---|---|---|---|
| Home / Mission Control | `/app` or `/app/home` | `OrkaMissionControlDto`, `DashboardTodayDto`, `OrkaLearningStateDto` | Start here; show today's mission, warnings, loads, and handoffs. | P0 |
| Tutor | `/app/tutor` | Tutor policy, next actions, chat/session APIs | Explain, repair, ask micro-checks, and route to work modes. | P0 |
| Study Room | `/app/study-room` | `OrkaStudyRoomDto` | Guided personal AI study session with checkpoint flow. | P0 |
| Review / Quiz | `/app/review` | Review/SRS, quiz attempt, adaptive assessment DTOs | Close due review and checkpoint understanding. | P0 |
| Exam War Room | `/app/exams` | `OrkaExamWarRoomDto` | Exam prep command center for weak outcomes and practice. | P0 |
| Sources / Wiki Pro | `/app/sources` | `OrkaSourceWikiProDto`, source/wiki APIs | Evidence workspace for sources, citations, and Wiki repair. | P0 |
| Notebook Studio | `/app/notebook` | `OrkaNotebookStudioProDto` | Build repair, review, exam, source, and summary packs. | P1 |
| Code Learning IDE | `/app/code` | `OrkaCodeLearningIdeDto`, code runtime APIs | Learning-aware code practice and safe runtime status. | P1 |
| Progress / Memory | `/app/progress` | `OrkaLearningStateDto`, snapshots, dashboard progress | Show durable learning state and trends. | P1 |
| Settings / Safety | `/app/settings` | account, safety, provider/runtime status | Account, privacy, tool capability, and safety controls. | P1 |

## Essential Beta Loop

The first beta should make this loop excellent:

1. Home shows one primary mission and why.
2. The student opens Tutor, Study Room, Review, Exam, Sources/Wiki, or Code.
3. The work mode records evidence or exposes a safe warning.
4. Orka returns the student to Home with a changed next action.

Everything else supports this loop.

## Main Work Modes

- Repair mode: repeated wrong answers, weak concept, prerequisite gap.
- Diagnostic mode: thin evidence, repeated blank/skipped answers, new learner.
- Review mode: due SRS, likely forgotten concept, stable review queue.
- Exam mode: weak exam outcome, deneme mistake cluster, question type gap.
- Evidence mode: source/citation/wiki warning, stale or insufficient source.
- Study Room mode: guided short lesson with safe topic/context.
- Artifact mode: turn evidence into study packs without raw payload exposure.
- Code mode: syntax/runtime/test repair and checkpoint coding practice.

## Handoff Model

Handoffs are visible suggestions. They are not hidden autonomous actions.

Examples:

- Home -> Tutor: explain or repair the primary mission.
- Home -> Study Room: start a guided lesson when topic/context is available.
- Home -> Review / Quiz: clear due review or take a checkpoint.
- Home -> Exam War Room: practice weak outcomes or review deneme mistakes.
- Home -> Sources / Wiki Pro: resolve evidence, citation, or Wiki repair issues.
- Home -> Notebook Studio: build a pack only when evidence supports it.
- Home -> Code IDE: practice or repair coding attempts safely.
- Tutor -> Study Room: convert explanation into guided lesson.
- Tutor -> Quiz: check understanding after explanation.
- Study Room -> Wiki / Notebook: write bounded traces or create summary pack.
- Exam War Room -> Tutor / Study Room / Practice: repair weak outcome.
- Source / Wiki Pro -> Tutor / Notebook / Exam: use source warnings and packs.
- Code IDE -> Tutor / Notebook / Review: explain safe error category or create
  code repair/review handoff.

## Beta Cutline

Must build in Phase 11:

- Home / Mission Control as the first student work surface.
- Clear module navigation for Tutor, Study Room, Review, Exam, Sources/Wiki,
  Notebook, Code, Progress, and Settings.
- Loading, empty, thin-evidence, warning, and blocked states for each core
  screen.
- Visible reason codes or human-readable reason labels.
- Safe handoff buttons that route to work modes with context.
- No raw DTO/debug JSON surfaces.

Can wait:

- Mobile app.
- Payment/subscription.
- Teacher/classroom/school management.
- Realtime voice.
- Real PPTX/video generation.
- Advanced graph canvas editing.
- Marketplace/plugin packaging.
- Official curriculum scraping or success prediction.

## Phase 11 Redesign Outcome

Phase 11 should produce a frontend beta that is not a marketing landing page and
not a loose collection of panels. It should make Orka's Learning OS feel like one
coherent student cockpit powered by the backend contracts listed in this map.
