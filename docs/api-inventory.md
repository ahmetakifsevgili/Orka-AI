# Backend API Inventory

Generated from controllers and Swagger for frontend contract freeze.

## Summary

- Base commit: `4b7b981 Complete backend schema and contract lifecycle`
- Branch: `feature/full-api-inventory-contract-freeze`
- Controllers inventoried: 21
- Endpoints: 95
- Auth-required endpoints: 85
- Public endpoints: 10
- Beta/hidden endpoints: 7

## Controllers

- AudioController: `api/audio`, actions=3, authDefault=true
- AuthController: `api/auth`, actions=6, authDefault=false
- ChatController: `api/chat`, actions=5, authDefault=true
- ClassroomController: `api/classroom`, actions=3, authDefault=true
- CodeController: `api/code`, actions=2, authDefault=true
- DailyChallengeController: `api/daily-challenge`, actions=2, authDefault=true
- DashboardController: `api/dashboard`, actions=3, authDefault=true
- DiagnosticsController: `api/dev/diagnostics`, actions=1, authDefault=false
- FlashcardsController: `api/flashcards`, actions=5, authDefault=true
- HealthController: `health`, actions=3, authDefault=false
- KorteksController: `api/korteks`, actions=5, authDefault=true
- LearningController: `api/learning`, actions=5, authDefault=true
- NotificationsController: `api/notifications`, actions=3, authDefault=true
- QuizController: `api/quiz`, actions=7, authDefault=true
- ReviewController: `api/review`, actions=2, authDefault=true
- SkillMasteryController: `api/skills`, actions=2, authDefault=true
- SourcesController: `api/sources`, actions=6, authDefault=true
- TestController: `api/test`, actions=4, authDefault=true
- TopicsController: `api/topics`, actions=9, authDefault=true
- UserController: `api/user`, actions=5, authDefault=false
- WikiController: `api/wiki`, actions=14, authDefault=true

## Endpoint Table

| Method | Path | Group | Auth | Path Params | Query Params | Swagger Responses | Readiness |
|---|---|---|---|---|---|---|---|
| GET | `/api/audio/overview/{jobId}/stream` | Audio | yes | jobId | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/audio/overview/{jobId}` | Audio | yes | jobId | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/audio/overview` | Audio | yes | - | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/auth/login` | Auth | no | - | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/auth/logout` | Auth | no | - | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/auth/refresh` | Auth | no | - | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/auth/register` | Auth | no | - | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/login` | Auth | no | - | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/register` | Auth | no | - | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/chat/message` | Chat | yes | - | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/chat/send` | Chat | yes | - | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/chat/session/end` | Chat | yes | - | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/chat/stream` | Chat | yes | - | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/chat/test-ai` | Chat | yes | - | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/classroom/{id}/ask` | Classroom | yes | id | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/classroom/interaction/{interactionId}/audio` | Classroom | yes | interactionId | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/classroom/session` | Classroom | yes | - | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/code/languages` | Code | yes | - | - | 200 | HIDE_UNTIL_BETA |
| POST | `/api/code/run` | Code | yes | - | - | 200 | HIDE_UNTIL_BETA |
| POST | `/api/daily-challenge/{challengeId}/submit` | DailyChallenge | yes | challengeId | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/daily-challenge` | DailyChallenge | yes | - | topicId | 200 | READY_FOR_FRONTEND |
| GET | `/api/dashboard/recent-activity` | Dashboard | yes | - | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/dashboard/stats` | Dashboard | yes | - | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/dashboard/system-health` | Dashboard | yes | - | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/dev/diagnostics/config` | Diagnostics | no | - | - | 200 | HIDE_UNTIL_BETA |
| POST | `/api/flashcards/{id}/review` | Flashcards | yes | id | - | 200 | READY_FOR_FRONTEND |
| DELETE | `/api/flashcards/{id}` | Flashcards | yes | id | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/flashcards/generate` | Flashcards | yes | - | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/flashcards` | Flashcards | yes | - | topicId | 200 | READY_FOR_FRONTEND |
| POST | `/api/flashcards` | Flashcards | yes | - | - | 200 | READY_FOR_FRONTEND |
| GET | `/health/live` | Health | no | - | - | 200 | READY_FOR_FRONTEND |
| GET | `/health/ready` | Health | no | - | - | 200 | READY_FOR_FRONTEND |
| GET | `/health` | Health | no | - | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/korteks/ping` | Korteks | yes | - | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/korteks/research-file` | Korteks | yes | - | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/korteks/research-stream` | Korteks | yes | - | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/korteks/research-sync` | Korteks | yes | - | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/korteks/research` | Korteks | yes | - | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/learning/review/{recommendationId}/complete` | Learning | yes | recommendationId | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/learning/signal` | Learning | yes | - | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/learning/topic/{topicId}/recommendations` | Learning | yes | topicId | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/learning/topic/{topicId}/review/due` | Learning | yes | topicId | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/learning/topic/{topicId}/summary` | Learning | yes | topicId | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/notifications/{id}/read` | Notifications | yes | id | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/notifications/read-all` | Notifications | yes | - | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/notifications` | Notifications | yes | - | includeRead | 200 | READY_FOR_FRONTEND |
| POST | `/api/quiz/attempt` | Quiz | yes | - | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/quiz/generate` | Quiz | yes | - | topicId | 200 | READY_FOR_FRONTEND |
| GET | `/api/quiz/history/{topicId}` | Quiz | yes | topicId | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/quiz/plan-diagnostic/{planRequestId}/attempt` | Quiz | yes | planRequestId | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/quiz/plan-diagnostic/finalize` | Quiz | yes | - | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/quiz/plan-diagnostic/start` | Quiz | yes | - | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/quiz/stats` | Quiz | yes | - | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/review/{id}/complete` | Review | yes | id | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/review/due` | Review | yes | - | topicId | 200 | READY_FOR_FRONTEND |
| GET | `/api/skills/{topicId}` | SkillMastery | yes | topicId | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/skills` | SkillMastery | yes | - | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/sources/{sourceId}/ask` | Sources | yes | sourceId | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/sources/{sourceId}/pages/{page}` | Sources | yes | sourceId, page | - | 200 | READY_FOR_FRONTEND |
| DELETE | `/api/sources/{sourceId}` | Sources | yes | sourceId | - | 200 | READY_FOR_FRONTEND |
| PATCH | `/api/sources/{sourceId}` | Sources | yes | sourceId | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/sources/topic/{topicId}` | Sources | yes | topicId | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/sources/upload` | Sources | yes | - | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/test/ping-embed` | Test | yes | - | text | 200 | HIDE_UNTIL_BETA |
| GET | `/api/test/ping-factory` | Test | yes | - | role | 200 | HIDE_UNTIL_BETA |
| GET | `/api/test/ping-github` | Test | yes | - | - | 200 | HIDE_UNTIL_BETA |
| GET | `/api/test/ping-groq` | Test | yes | - | - | 200 | HIDE_UNTIL_BETA |
| GET | `/api/topics/{id}/progress` | Topics | yes | id | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/topics/{id}/sessions/latest` | Topics | yes | id | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/topics/{id}/sessions` | Topics | yes | id | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/topics/{id}/subtopics` | Topics | yes | id | - | 200 | READY_FOR_FRONTEND |
| DELETE | `/api/topics/{id}` | Topics | yes | id | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/topics/{id}` | Topics | yes | id | - | 200 | READY_FOR_FRONTEND |
| PATCH | `/api/topics/{id}` | Topics | yes | id | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/topics` | Topics | yes | - | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/topics` | Topics | yes | - | - | 200 | READY_FOR_FRONTEND |
| DELETE | `/api/user/account` | User | yes | - | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/user/gamification` | User | yes | - | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/user/me` | User | yes | - | - | 200 | READY_FOR_FRONTEND |
| PATCH | `/api/user/profile` | User | yes | - | - | 200 | READY_FOR_FRONTEND |
| PATCH | `/api/user/settings` | User | yes | - | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/wiki/{topicId}/briefing` | Wiki | yes | topicId | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/wiki/{topicId}/chat` | Wiki | yes | topicId | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/wiki/{topicId}/export` | Wiki | yes | topicId | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/wiki/{topicId}/glossary` | Wiki | yes | topicId | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/wiki/{topicId}/mindmap` | Wiki | yes | topicId | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/wiki/{topicId}/recommendations` | Wiki | yes | topicId | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/wiki/{topicId}/research` | Wiki | yes | topicId | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/wiki/{topicId}/study-cards` | Wiki | yes | topicId | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/wiki/{topicId}/timeline` | Wiki | yes | topicId | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/wiki/{topicId}` | Wiki | yes | topicId | - | 200 | READY_FOR_FRONTEND |
| DELETE | `/api/wiki/block/{blockId}` | Wiki | yes | blockId | - | 200 | READY_FOR_FRONTEND |
| PUT | `/api/wiki/block/{blockId}` | Wiki | yes | blockId | - | 200 | READY_FOR_FRONTEND |
| POST | `/api/wiki/page/{pageId}/note` | Wiki | yes | pageId | - | 200 | READY_FOR_FRONTEND |
| GET | `/api/wiki/page/{pageId}` | Wiki | yes | pageId | - | 200 | READY_FOR_FRONTEND |

## Notes

- Swagger does not currently advertise JWT security consistently; auth was reconciled from controller attributes.
- `DiagnosticsController`, `TestController`, and direct `CodeController` endpoints should stay hidden unless intentionally exposed for beta/admin tooling.
- Provider-backed routes are contract-ready but UI must show degraded/provider-blocked states when returned.
- Tutor pedagogy/visualization addendum does not add new endpoints. Frontend should use `/api/chat/send` content plus `metadata.usedTools`, `metadata.fallbackReason`, and `metadata.providerWarnings` for Mermaid, visual markdown, and YouTube pedagogy/reference UI.
## Feature Parity Addendum

Additional backend parity endpoints now available:

- `GET /api/bookmarks`
- `POST /api/bookmarks`
- `PATCH /api/bookmarks/{id}`
- `DELETE /api/bookmarks/{id}`
- `GET /api/notifications/subscriptions`
- `POST /api/notifications/subscriptions`
- `DELETE /api/notifications/subscriptions/{id}`
- `GET /api/push/subscriptions`
- `POST /api/push/subscriptions`
- `DELETE /api/push/subscriptions/{id}`
- `GET /api/profile/xp`
- `GET /api/profile/badges`
- `POST /api/code/execute`
- `GET /api/health`, `GET /api/health/live`, `GET /api/health/ready`
- `GET /api/flashcards/topic/{topicId}`
- `GET /api/flashcards/proposals/{topicId}`
- `POST /api/flashcards/bulk`

These are compatibility/parity surfaces and do not replace the accepted canonical contracts.
