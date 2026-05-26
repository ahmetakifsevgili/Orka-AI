# Phase 11 Frontend Redesign Brief

Status: Product Coherence Phase 11 implemented.

Phase 11 redesigns the frontend around the completed backend Learning OS.
This brief is intentionally product/UX focused and does not prescribe exact
pixels.

## Design Goal

Make Orka feel like one coherent student cockpit:

- the student opens Orka and knows what to do first;
- every work mode has a clear reason to exist;
- handoffs between modules are visible and contextual;
- warnings are honest and actionable;
- raw internal data never leaks into the interface.

## Must-Have Screens

P0:

- Home / Mission Control
- Tutor
- Study Room
- Review / Quiz
- Exam War Room
- Sources / Wiki Pro

P1:

- Notebook Studio
- Code Learning IDE
- Progress / Memory
- Settings / Safety

## Suggested Routes

- `/app` or `/app/home`
- `/app/tutor`
- `/app/study-room`
- `/app/review`
- `/app/exams`
- `/app/sources`
- `/app/notebook`
- `/app/code`
- `/app/progress`
- `/app/settings`

The current `/app` shell can remain as the protected entry, but the default
student screen should become Home / Mission Control.

## Backend Endpoints

Phase 11 added typed frontend API wrappers for:

- `GET /api/dashboard/today`
- `GET /api/learning/orka-state`
- `GET /api/learning/mission-control`
- `GET /api/learning/study-coach`
- `GET /api/central-exams/{examCode}/war-room`
- `GET /api/sources/wiki-pro`
- `GET /api/classroom/study-room`
- `POST /api/classroom/study-room/start`
- `POST /api/classroom/study-room/checkpoint`
- `GET /api/notebook-studio/pro`
- `GET /api/code/learning-ide`

Existing topic, chat, quiz, source, wiki, notebook, exam, and code endpoints
are reused rather than replaced.

## Implementation Notes

- `/app` now defaults to Home / Mission Control.
- The sidebar exposes Home, Tutor, Study Room, Review, Exams, Sources/Wiki,
  Notebook, Code, Progress, and Settings.
- New compact product panels consume the Phase 1-9 contracts without rendering
  raw internal payloads.
- Tutor, Review/Quiz, Progress, and existing detailed module components remain
  available as reused work surfaces.
- No provider calls, OpenAI migration, Realtime voice, Google Cloud,
  Stripe/payment, mobile app, teacher/classroom management, official/success
  claim, or unsafe runtime expansion was added.

## Component Zones

Home / Mission Control:

- primary mission
- reason codes / "why now"
- urgent warnings
- today focus
- module cards
- secondary actions
- study rhythm summary
- progress snapshot

Work mode screens:

- header with current mission context
- primary work area
- warning/status rail
- handoff actions
- empty/thin-evidence state
- completion/next action summary

## State Handling

Every screen needs:

- loading state
- empty state
- thin-evidence state
- warning state
- blocked state
- ready state

Avoid fake placeholders that imply progress or mastery before evidence exists.

## Visual / Product Principles

- Build the actual student work surface, not a marketing landing page.
- Keep operational screens dense but calm.
- Use compact module cards and clear actions.
- Prefer icons for common actions and short labels for route choices.
- Make reason/warning labels scannable.
- Do not show giant hero marketing inside the app.
- Avoid hidden autonomous actions; handoffs should be explicit.

## What Not To Build In Phase 11

- Mobile app.
- Payment/subscription or Stripe integration.
- Teacher/classroom/school management.
- Realtime voice.
- Real PPTX/video generation.
- New AI/provider architecture.
- OpenAI Responses API, Agents SDK, or Realtime migration.
- Google Cloud integration.
- Official scraping or success/score prediction.

## Validation Plan

Minimum Phase 11 validation:

- Build/typecheck frontend.
- Smoke route navigation for all core screens.
- Verify Home can render Mission Control, Study Coach compact state, and module
  cards from mocked or live local backend responses.
- Verify each screen has empty/thin/warning/blocked state handling.
- Verify no raw prompt/provider/source/tool/debug/local path/secret/id/stack
  trace/transcript/answer-key fields render.
- Run backend Product Coherence gates after frontend API type changes.

## Beta Cutline

Phase 11 is beta-ready when a student can:

1. log in;
2. land on Home / Mission Control;
3. understand today's primary mission;
4. open the recommended module;
5. complete or attempt one learning action;
6. return to Home and see the next action update;
7. never see raw internal payloads or unsafe claims.
