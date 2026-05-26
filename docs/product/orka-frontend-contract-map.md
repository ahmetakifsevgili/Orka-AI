# Orka Backend-To-Frontend Contract Map

Status: Product Coherence Phase 10.

This document tells Phase 11 which backend contracts power each future screen.
It avoids pixel-level UI decisions and focuses on routes, data, states, actions,
and safety constraints.

## Contract Principles

- Home starts from Mission Control, not from a generic chat empty state.
- Every screen must support loading, empty, thin evidence, warning, and blocked
  states.
- Public UI must never render raw prompts, provider payloads, source chunks, raw
  tool payloads, debug traces, local paths, secrets, owner ids, unsafe user ids,
  stack traces, raw transcripts, pre-submit answer keys, or correct answers.
- Handoffs are visible user choices, not hidden autonomous edits/actions.
- Study Room/Classroom means personal AI study room only.

## Screen Matrix

| Screen | Suggested route | Endpoint(s) | DTO(s) | Required frontend states | Primary actions |
|---|---|---|---|---|---|
| Home / Mission Control | `/app` or `/app/home` | `GET /api/dashboard/today`, `GET /api/learning/mission-control`, `GET /api/learning/orka-state` | `DashboardTodayDto`, `OrkaMissionControlDto`, `OrkaLearningStateDto` | loading, no topic, thin evidence, warning, blocked, ready | open Tutor, Study Room, Review, Exam, Sources/Wiki, Notebook, Code, Progress |
| Tutor | `/app/tutor` | chat/session APIs, `GET /api/tutor/next-actions`, `GET /api/tutor/policy/topic/{topicId}` | tutor policy/action metadata | no topic, source limited, repair pending, checkpoint needed, provider unavailable | ask, repair, checkpoint, open Study Room, open Quiz, open Sources/Wiki |
| Study Room | `/app/study-room` | `GET /api/classroom/study-room`, `POST /api/classroom/study-room/start`, `POST /api/classroom/study-room/checkpoint` | `OrkaStudyRoomDto`, start/checkpoint DTOs | missing context, thin evidence, source blocked, checkpoint active, submitted, complete | start lesson, submit checkpoint, open Tutor, Review, Wiki, Notebook |
| Review / Quiz | `/app/review` | `GET /api/learning/topic/{topicId}/review/due`, quiz/adaptive endpoints | Review/SRS DTOs, quiz attempt DTOs | no due review, due review, checkpoint active, submitted, repair needed | review due item, take checkpoint, record attempt, open Tutor |
| Exam War Room | `/app/exams` | `GET /api/central-exams/{examCode}/war-room`, central exam study/practice/deneme APIs | `OrkaExamWarRoomDto`, central exam DTOs | no active exam, coverage limited, source unverified, weak outcome, deneme cluster, ready | repair outcome, practice type, review deneme, mini deneme, open Tutor/Study Room |
| Sources / Wiki Pro | `/app/sources` | `GET /api/sources/wiki-pro`, source upload/list/Q&A/compare/citation APIs, wiki APIs | `OrkaSourceWikiProDto`, source/wiki DTOs | no source, ready source, stale/deleted/insufficient source, citation missing, wiki repair | upload source, citation review, ask source, compare, repair wiki, open Notebook |
| Notebook Studio | `/app/notebook` | `GET /api/notebook-studio/pro`, existing pack/export preview APIs | `OrkaNotebookStudioProDto`, pack/export DTOs | no pack, pack ready, source blocked, preview only, export limited | create pack, open pack, preview export, create flashcards, open Review |
| Code Learning IDE | `/app/code` | `GET /api/code/learning-ide`, `GET /api/code/languages`, existing code run APIs | `OrkaCodeLearningIdeDto`, code runtime DTOs | runtime blocked, language unsupported, repeated error, blank attempt, stable success | run safe attempt, repair error, ask Tutor, create code note, review concept |
| Progress / Memory | `/app/progress` | `GET /api/dashboard/today`, `GET /api/learning/orka-state`, snapshot APIs | dashboard/snapshot/state DTOs | no evidence, thin evidence, progress ready, warnings | inspect weak concepts, open Review, open Tutor, open Sources/Wiki |
| Settings / Safety | `/app/settings` | existing user/settings/tool capability APIs | user/settings/tool DTOs | loading, provider off, tool limited, account ready | update settings, inspect capability, sign out |

## Required Field Groups

Home / Mission Control needs:

- `primaryMission`
- `primaryEntryPoint`
- `secondaryActions`
- `urgentWarnings`
- `todayFocus`
- `reviewLoad`
- `repairLoad`
- `examLoad`
- `sourceWikiLoad`
- `studyRoomSuggestion`
- `moduleCards`
- `evidenceConfidence`
- `reasonCodes`
- safe summary

Study Coach needs:

- `rhythmStatus`
- `recommendedPace`
- `todayPlan`
- `weeklyPlan`
- `focusPlan`
- `comebackPlan`
- `workload`
- `actions`
- `warnings`
- `reasonCodes`

Specialized modules need:

- readiness/status fields
- safe labels and summaries
- warning collections
- reason codes
- handoff actions
- optional scoped ids only when already safe for routing

## State Handling Requirements

Loading:

- Show a compact skeleton or spinner.
- Do not show fake progress values while loading.

Empty:

- New learner should see start diagnostic or quick start.
- Do not claim mastery without evidence.

Thin evidence:

- Say Orka needs a short diagnostic, first review, or source upload.
- Keep suggestions short and reversible.

Warning:

- Surface source/citation/runtime/context/module-conflict warnings.
- Prefer precise action buttons over long explanatory copy.

Blocked:

- Explain the safe reason: source blocked, runtime blocked, missing context,
  cross-user scope rejected, or provider unavailable.
- Do not expose debug details.

Ready:

- Show the primary action, reason, and handoff.
- Keep module cards scannable.

## Safety Constraints By Screen

- Tutor: no hidden prompt, provider body, or source chunk display.
- Study Room: no raw transcript or pre-submit answer key.
- Review / Quiz: no correct answer before submit.
- Exam War Room: no score/percentile/success guarantee.
- Sources / Wiki Pro: no source-backed claim without source/citation metadata.
- Notebook Studio: preview-only must be labeled if real export is not supported.
- Code IDE: no raw stack traces, local paths, secrets, or tool payloads.
- Progress: no unsafe raw ids or private logs.

## Phase 11 API Client Gap

Current frontend API clients do not yet expose typed methods for the Phase 1-9
contracts. Phase 11 should add typed client functions for:

- `LearningAPI.getOrkaState`
- `LearningAPI.getMissionControl`
- `LearningAPI.getStudyCoach`
- `CentralExamsAPI.getWarRoom`
- `SourcesAPI.getWikiPro`
- `ClassroomAPI.getStudyRoom`, `startStudyRoom`, `submitCheckpoint`
- `NotebookStudioAPI.getPro`
- `CodeAPI.getLearningIde`
- optional `EvaluationAPI.getUnifiedEvaluation` for internal/debug-safe release
  surfaces only, not a student-first screen.
