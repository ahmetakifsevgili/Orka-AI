# Runtime Contract Probe

Local API: `http://127.0.0.1:5101`. Unique users/topics. No TestSprite. JWTs omitted. AI prose not asserted.

| Probe | Endpoint | Status | Evidence | Classification | Notes |
|---|---|---|---|---|---|
| health | `GET /health/live` | PASS | 200 | PASS | live ok |
| health-ready | `GET /health/ready` | PASS | 200 | PASS | ready ok |
| auth | `POST /api/auth/register` | PASS | 201 token | PASS | token omitted |
| auth-login | `POST /api/auth/login` | PASS | 200 token | PASS | token omitted |
| auth-refresh | `POST /api/auth/refresh` | PASS | 200 token | PASS | token omitted |
| topic/planIntent | `POST /api/topics` | PASS | 200 QuickReview | PASS | planIntent present |
| chat metadata | `POST /api/chat/send` | PASS | 200 metadata=True | PASS | AI prose not asserted |
| stream | `POST /api/chat/stream` | PASS | 200 | PASS | SSE returned; content not prose-asserted |
| source upload | `POST /api/sources/upload` | PASS | 200 a45296b7-a49c-4e9e-a201-b72bbe851956 | PASS | txt upload |
| source list | `GET /api/sources/topic/{topicId}` | PASS | 200 count=1 | PASS | user/topic scoped |
| source ask | `POST /api/sources/{id}/ask` | PASS | 200 citations=1 | PASS | citation metadata available |
| source delete | `DELETE /api/sources/{id}` | PASS | delete=200 ask=404 | PASS | deleted chunks hidden |
| wiki | `GET /api/wiki/{topicId}` | PASS | 200 | PASS | safe no-wiki list |
| wiki briefing | `GET /api/wiki/{topicId}/briefing` | PASS | 200 | PASS | bounded single call |
| quiz attempt | `POST /api/quiz/attempt` | PASS | 200 review=True | PASS | wrong attempt persisted |
| review | `GET /api/review/due + POST complete` | PASS | PASS | PASS | completed 0084e7a1-c14a-490b-800e-7126ab38840c |
| flashcard | `flashcards create/list/review/delete` | PASS | list=1 delete=200 | PASS | reviewItem linked |
| daily challenge | `GET/POST /api/daily-challenge` | PASS | first=False second=True | PASS | XP idempotent |
| badges/profile | `GET /api/user/gamification` | PASS | 200 badges=2 | PASS | profile exposure |
| notifications | `GET/read-all notifications` | PASS | list=1 readAll=200 | PASS | in-app first |
| audio | `GET /api/audio/overview/{unknown}` | PASS | 404 | PASS | unknown job hidden |
| classroom | `POST /api/classroom/session` | PASS | 200 6be9bfa1-fccc-425d-9189-07688e0a2317 | PASS | start only, ask is provider-dependent |
| cross-user isolation | `source/flashcard foreign access` | PASS | source=404 flashcard=404 | PASS | foreign resources hidden |

Summary: all bounded probes passed. Classroom ask/audio remains provider-dependent for UX design, not a backend blocker.

## Tutor Pedagogy / Visualization / YouTube Addendum Probe

Local API: `http://127.0.0.1:5101`. Unique user/topic. No TestSprite. JWTs omitted. AI prose was not exact-asserted.

| Probe | Endpoint | Status | Evidence | Classification | Notes |
|---|---|---|---|---|---|
| Mermaid diagram | `POST /api/chat/send` | PARTIAL | 200; one bounded run preserved Mermaid fenced content before rebuild, final updated-build run returned text without a Mermaid block. Metadata extraction is covered by focused unit test. | AI_NONDETERMINISTIC | Backend preserves/extracts Mermaid when present; model may ignore explicit diagram request. |
| YouTube teaching reference | `POST /api/chat/send` | PASS | 200; no seeded YouTube context; no named teacher/channel claim observed. | EXPECTED_FALLBACK | YouTube provider was not spammed. |
| no YouTube fallback | `POST /api/chat/send` | PASS | Tutor answered without claiming video evidence. | EXPECTED_FALLBACK | No reference card should render without metadata. |
| source vs YouTube priority | `upload + POST /api/chat/send` | PARTIAL | Source upload passed; bounded Tutor answer did not reliably emit doc citation. | AI_NONDETERMINISTIC | Existing source ask/Tutor citation proofs remain accepted; docs keep source priority rule. |
| visual tool readiness | focused code audit | PARTIAL | Tutor prompt supports Pollinations markdown image URLs; no callable visual generator endpoint was proven. | FRONTEND_DEPENDENT | Render markdown image or hide visual job UI until beta. |

Addendum summary: backend blockers none. Mermaid, YouTube pedagogy fallback, and visual markdown are now documented for frontend rendering; metadata extraction for Mermaid/Pollinations/YouTube markers is covered by deterministic unit tests.
## Feature Parity Runtime Probe Addendum

Added parity probes to contract tests:

| Probe | Expected |
| --- | --- |
| Bookmarks empty/list/create/update/delete | 200 with stable DTO and soft-delete response |
| `/api/health/live` alias | 200 |
| `/api/profile/xp` | 200 with `totalXP` |
| `/api/profile/badges` | 200 with `badges[]` |
| `/api/code/execute` validation | 400 for empty code without provider call |
| Push subscription create/list/delete | 200 with active/deleted state |

Provider-heavy Tutor SK auto-invocation remains outside permanent contract tests. The accepted deterministic guarantee is metadata/tool surface stability and no hard failure when provider-specific tools are unavailable.
