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
