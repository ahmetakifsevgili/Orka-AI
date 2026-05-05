# Full Backend Lifecycle Regression

## Executive Verdict

`FULL_BACKEND_LIFECYCLE_ACCEPTED_WITH_NOTES`

The full stateful backend lifecycle completed with no true backend blockers after two small contract fixes. Final runtime classification:

- `PASS`: 49
- `EXPECTED_FALLBACK`: 2
- `AI_NONDETERMINISTIC`: 1
- `APPLICATION_BUG` / `BACKEND_BLOCKER`: 0

## Branch / Base

- Branch: `feature/full-backend-lifecycle-regression`
- Base commit: `c5c4092 Add Tutor pedagogy visualization contract`
- Final commit: recorded in the final execution report after commit creation

## Test Environment

- API port: `http://127.0.0.1:5101`
- DB state: existing local migrated DB, no clearing and no destructive reset
- Provider/tool assumptions: bounded AI calls allowed; no provider spam; Korteks live research skipped because earlier source-grounding proofs already cover it
- TestSprite: not used
- Secrets/JWTs: not printed

## Lifecycle Summary

The regression used one unique primary user, one unique secondary user for isolation, one root topic, one uploaded source, one chat session, one wrong quiz attempt, one review item, one flashcard, one daily challenge, one audio overview job, and one classroom session.

Two small contract issues were found on the first run and fixed:

- `GET /api/topics/{id}` returned `planIntent/category` only inside the nested `topic` object, while the frontend contract expects stable top-level fields too.
- `POST /api/classroom/{id}/ask` returned `answer`, but the frontend contract also expects `answerScript`.

Both were fixed with backward-compatible aliases and verified in the second lifecycle run.

## Step-by-Step Results

| Step | Endpoint(s) | Status | Evidence | Classification | Remaining issue |
|---|---|---|---|---|---|
| health | `GET /health/live`, `GET /health/ready` | PASS | both 200 | PASS | none |
| auth | register/login/refresh | PASS | register 201, login 200, refresh 200; tokens omitted | PASS | none |
| topics/planIntent | `POST/GET /api/topics`, `GET /api/topics/{id}` | PASS | top-level and nested `planIntent=QuickReview`, `category=Plan:QuickReview` | PASS | none |
| source upload | `POST /api/sources/upload` | PASS | status `ready`, `chunkCount=1` | PASS | none |
| source list/ask | `GET /api/sources/topic/{topicId}`, `POST /api/sources/{id}/ask` | PASS | source ask returned citation count 1 and unique fact evidence | PASS | none |
| chat metadata | `POST /api/chat/send` | PASS | content/session/metadata present; grounding field stable | PASS | Tutor source citation is model-dependent; source ask citation passed |
| Tutor Mermaid | `POST /api/chat/send` | PARTIAL | 200 and content present; model did not emit Mermaid in final run | AI_NONDETERMINISTIC | backend preservation/extraction covered by unit tests |
| Tutor YouTube no-fake | `POST /api/chat/send` | PASS | no named teacher/channel/video claim without context | PASS | no YouTube context loaded |
| quiz attempt | `POST /api/quiz/attempt` | PASS | wrong attempt persisted and review pressure returned | PASS | none |
| review/SRS | `GET /api/review/due`, `POST /api/review/{id}/complete` | PASS | concept review key present; lastReviewedAt/interval/repetition updated | PASS | none |
| flashcards | create/list/review/delete path | PASS | flashcard linked to review item; review updated path | PASS | none |
| daily challenge | today/submit/duplicate submit | PASS | first submit `duplicate=false`, second `duplicate=true`, XP stable | PASS | none |
| XP/badges/profile | `GET /api/user/gamification`, dashboard stats | PASS | XP and badges exposed; dashboard schema stable | PASS | none |
| notifications | list/read-all | PASS | in-app notification list stable; read-all 200 | PASS | none |
| wiki | list/export/session end/post-session list | PASS | no wiki returned safe empty/404 states, no 500 | PASS | background readiness not forced |
| Korteks | `GET /api/korteks/ping` | PASS | ping 200 | PASS | live research skipped to avoid provider spam |
| audio | create/status/stream fallback | PASS | job `script-only`, script present, status contract stable | EXPECTED_FALLBACK | Edge TTS unavailable is acceptable |
| classroom | start/ask/audio fallback | PASS | session active; ask returned `answer` and `answerScript`; audio 404 accepted | PASS | TTS audio is stateful |
| source lifecycle | patch/delete/ask deleted | PASS | patch 200, delete 200, deleted ask 404 | PASS | none |
| cross-user isolation | source/flashcard/review/audio/topic/notification | PASS | all foreign access hidden with 404 | PASS | none |
| final dashboard | recent activity/gamification | PASS | no 500, schema stable after journey | PASS | none |

## Endpoint / Schema Issues Found

1. Topic detail contract mismatch
   - File: `Orka.API/Controllers/TopicsController.cs`
   - Issue: top-level `planIntent` and `category` were missing from `GET /api/topics/{id}`.
   - Fix: added backward-compatible top-level topic fields while preserving nested `topic`.

2. Classroom ask alias mismatch
   - File: `Orka.Core/DTOs/LearningDtos.cs`
   - Issue: response had `answer`, but frontend contract expects `answerScript`.
   - Fix: added `AnswerScript` alias returning `Answer`.

## Provider / Tool / Nondeterministic Notes

- Mermaid: final live model response ignored explicit Mermaid instruction. This is not a backend blocker because content was returned, code block preservation is unchanged, and metadata extraction for Mermaid is deterministic-unit-tested.
- YouTube: no YouTube context was seeded; Tutor avoided fabricated teacher/channel/video claims. This is accepted fallback.
- Audio: Edge TTS returned script-only. Script-only status is part of the frozen contract.
- Korteks: live research-sync skipped to avoid provider/tool spam; ping passed and previous accepted source-grounding proof remains valid.

## Cross-User Isolation Summary

Second user attempts against the first user's source, flashcard, review item, audio job, topic, and notification all returned 404. No content leakage or mutation was observed.

## Contract Consistency Summary

- API inventory match: yes; no new endpoints added.
- Frontend contract match: updated for topic detail top-level fields and classroom `answerScript`.
- Metadata contract match: yes; chat metadata object remains stable.
- Empty/error states match: yes; wiki export 404 and audio script-only stream fallback are documented accepted states.
- Pedagogy/visualization contract match: yes; Mermaid runtime remains AI nondeterministic, not a backend schema/metadata failure.

## Build / Test Results

- Baseline build: PASS
- Baseline `contract_tests`: PASS, 33 passed, 1 skipped
- Lifecycle runtime: PASS with notes, 52 steps, 0 backend blockers
- Final build: PASS
- Final `contract_tests`: PASS, 33 passed, 1 skipped

## Remaining Before Frontend

True backend blockers:

- None.

Frontend UI decisions:

- Mermaid renderer fallback.
- YouTube reference card design from `metadata.usedTools`.
- Audio script-only UI.
- Classroom answer/audio state handling.

Beta improvements:

- Make Tutor Mermaid compliance more deterministic.
- Optional live Korteks research smoke in a provider-enabled beta environment.
- Optional visual generation job lifecycle if product wants more than markdown image URLs.

Production hardening:

- Register pytest custom marks for lifecycle/AI warnings.
- Add provider availability dashboards.
- Add long-running background/wiki summarizer observability.
