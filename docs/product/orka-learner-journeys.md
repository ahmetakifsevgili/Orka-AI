# Orka Learner Journeys

Status: Product Coherence Phase 10.

Each journey describes the backend evidence, first screen, handoffs, safety
constraints, and acceptance criteria Phase 11 must support.

## Journey Matrix

| Journey | Student state | First screen/call | Recommended first action | Handoffs | Acceptance criteria |
|---|---|---|---|---|---|
| New learner | No durable evidence or topic history | Home: `GET /api/learning/mission-control` | start diagnostic or quick start | Tutor, Review/Quiz, Study Room only if context exists | No mastery claim; thin evidence is explicit. |
| Struggling learner | Repeated wrong answers or prerequisite gap | Home -> Tutor/Study Room | repair concept or repair prerequisite | Tutor, Study Room, Review, Notebook repair pack | One wrong does not overreact; repeated wrong creates repair. |
| Blank/skipped learner | Repeated blank/no answer signals | Home -> guided diagnostic | start diagnostic or prerequisite review | Tutor, Review/Quiz, Study Room | No fake misconception certainty. |
| Improving learner | Recent correct answers and stable concept | Home -> continue learning | continue plan or checkpoint quiz | Tutor, Quiz, Progress | Stable success reduces urgency without guarantee. |
| Forgotten learner | Due review backlog or likely forgotten concept | Home -> Review | review due concept | Review, Tutor, Flashcards, Notebook review pack | Due review appears as primary or secondary action. |
| Exam prep learner | Active exam and weak outcome/question type | Home -> Exam War Room | repair exam outcome or practice question type | Exam, Tutor, Study Room, Review, Notebook exam pack | No score/success/official alignment claim. |
| Source/wiki learner | Source evidence, citation, or Wiki repair issue | Home -> Sources / Wiki Pro | source review, citation review, or repair Wiki page | Sources, Wiki, Tutor, Notebook, Exam | Source-backed labels appear only with evidence. |
| Study Room learner | Topic/context ready for guided session | Home -> Study Room | start repair/review/exam/source lesson | Tutor, Quiz, Review, Wiki, Notebook | Study Room is personal AI study room, not teacher panel. |
| Notebook learner | Pack evidence ready or artifact handoff exists | Home -> Notebook Studio | create/open repair/review/exam/source pack | Notebook, Review, Sources, Tutor | Preview limitations are explicit; raw payloads absent. |
| Code learner | Coding topic or code attempt evidence exists | Home -> Code IDE | repair code error, checkpoint, or practice concept | Code, Tutor, Notebook, Review | Runtime limitations and redactions are visible. |
| Mixed Learning OS learner | Multiple loads and warnings compete | Home -> Mission Control | highest safe priority or conflict warning | Relevant modules from primary mission | Modules agree or emit explicit bounded warning. |

## Screen Sequences

### 1. New Learner

- Entry: `/app`.
- Calls: dashboard today, Mission Control, unified state.
- User sees: "start diagnostic" or "quick start", plus thin-evidence state.
- Phase 11 must show: short first action, no fake dashboard completion, no
  mastery score.

### 2. Struggling Learner

- Entry: Home primary mission.
- Calls: Mission Control, Study Coach, Tutor policy, optional Study Room.
- User sees: repair mission, reason `repeated_wrong` or `prerequisite_gap`.
- Handoff: Tutor repair first; Study Room only if topic/context is ready.
- Acceptance: Dashboard, Tutor, Mission Control, and Study Coach do not silently
  contradict the repair priority.

### 3. Blank/Skipped Learner

- Entry: Home or Review.
- Calls: Mission Control, Study Coach, Review/Quiz.
- User sees: guided diagnostic or prerequisite review.
- Acceptance: UI does not show misconception certainty from blank answers.

### 4. Improving Learner

- Entry: Home.
- Calls: unified state, Mission Control, Progress.
- User sees: continue plan, checkpoint, or lighter review.
- Acceptance: success is framed as recent evidence, not permanent mastery or
  guaranteed outcome.

### 5. Forgotten/Due Review Learner

- Entry: Home -> Review.
- Calls: Mission Control, Study Coach, Review/SRS.
- User sees: review sprint or due concept.
- Acceptance: due review appears in module card and action list.

### 6. Exam Prep Learner

- Entry: Home -> Exam War Room.
- Calls: Exam War Room, Mission Control, Study Coach.
- User sees: weak outcome, deneme cluster, question type gap, or diagnostic.
- Acceptance: no official or success guarantee copy; source/curriculum warnings
  remain visible.

### 7. Source/Wiki Learner

- Entry: Home -> Sources / Wiki Pro.
- Calls: Source / Wiki Pro, source lifecycle, Wiki.
- User sees: source readiness, stale/deleted/insufficient state, citation
  warnings, Wiki repair pages.
- Acceptance: provider output or Wiki memory alone never appears as citation
  evidence.

### 8. Study Room Learner

- Entry: Home handoff or Tutor handoff.
- Calls: Study Room GET/start/checkpoint.
- User sees: lesson mode, roles, checkpoint status, warnings.
- Acceptance: no raw transcript, no pre-submit answer key, no institutional
  classroom workflow.

### 9. Notebook Learner

- Entry: Home -> Notebook Studio.
- Calls: Notebook Studio Pro and existing pack/export preview endpoints.
- User sees: recommended packs, artifact queue, preview readiness.
- Acceptance: real PPTX/video is not claimed if only preview exists.

### 10. Code Learner

- Entry: Home -> Code IDE.
- Calls: Code Learning IDE, languages/runtime readiness, optional safe run.
- User sees: runtime status, active skill, error category, repair action.
- Acceptance: stack traces, paths, secrets, and tool payloads are redacted.

### 11. Mixed Learning OS Learner

- Entry: Home.
- Calls: Mission Control, Study Coach, unified state, relevant specialized
  module contracts.
- User sees: one primary mission plus bounded warnings if modules disagree.
- Acceptance: conflicts are surfaced instead of hidden.

## Safety And Claim Constraints

All journeys must preserve:

- no raw prompts or provider payloads;
- no source chunks, raw transcripts, raw tool payloads, debug traces, local
  paths, secrets, stack traces, owner ids, unsafe user ids, pre-submit answer
  keys, or correct answers;
- no score, percentile, placement, official alignment, or success guarantee;
- no therapy, diagnosis, mental-health, or wellbeing claims;
- no teacher/classroom/dershane management semantics.
