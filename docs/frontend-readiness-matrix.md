# Frontend Readiness Matrix

| Screen / Feature | Backend Endpoints | Classification | Notes |
|---|---|---|---|
| Auth screens | `/api/auth/register, /api/auth/login, /api/auth/refresh` | READY_FOR_FRONTEND | 201/200/409/401 covered |
| Dashboard | `/api/dashboard/stats, /api/user/gamification` | READY_FOR_FRONTEND | XP/badges/streak |
| Topic tree/sidebar | `/api/topics, /api/topics/tree` | READY_FOR_FRONTEND | planIntent available |
| Plan view | `topics + quiz plan diagnostic` | READY_WITH_UI_DECISION | provider-dependent plan generation |
| Tutor chat | `/api/chat/send` | READY_FOR_FRONTEND | metadata available |
| Streaming chat | `/api/chat/stream` | READY_WITH_UI_DECISION | SSE text; metadata SSE later |
| Source upload/manage | `/api/sources` | READY_FOR_FRONTEND | upload/list/patch/delete |
| Source ask / Notebook view | `/api/sources/{id}/ask` | READY_FOR_FRONTEND | citation metadata |
| Wiki page | `/api/wiki` | READY_FOR_FRONTEND | empty/404 documented |
| Briefing | `/api/wiki/{topicId}/briefing` | READY_WITH_UI_DECISION | provider-dependent |
| Glossary | `/api/wiki/{topicId}/glossary` | READY_WITH_UI_DECISION | provider-dependent |
| Study cards | `/api/wiki/{topicId}/study-cards` | READY_WITH_UI_DECISION | provider-dependent |
| Mindmap | `/api/wiki/{topicId}/mindmap` | READY_WITH_UI_DECISION | provider-dependent |
| Quiz | `/api/quiz/generate, /api/quiz/attempt` | READY_WITH_UI_DECISION | generate provider-dependent; attempt stable |
| Quiz history | `/api/quiz/history/{topicId}, /api/quiz/stats` | READY_FOR_FRONTEND | metadata exposed |
| Review page | `/api/review/due, complete` | READY_FOR_FRONTEND | runtime proven |
| Flashcards page | `/api/flashcards` | READY_FOR_FRONTEND | runtime proven |
| Daily Challenge | `/api/daily-challenge` | READY_FOR_FRONTEND | idempotent submit |
| Badges/profile | `/api/user/gamification` | READY_FOR_FRONTEND | badge persistence |
| Notifications center | `/api/notifications` | READY_FOR_FRONTEND | read/read-all |
| Korteks research/debug | `/api/korteks` | HIDE_UNTIL_BETA | debug/product decision |
| Audio overview | `/api/audio/overview` | READY_WITH_UI_DECISION | script-only UI needed |
| Classroom | `/api/classroom` | READY_WITH_UI_DECISION | session start stable; ask provider-dependent |
| Settings/profile | `/api/user` | READY_FOR_FRONTEND | settings persistence |
| Error/fallback banners | `metadata/fallback fields` | READY_FOR_FRONTEND | use structured state |

Backend blockers: none found. Hide until beta: direct diagnostics/test/code endpoints and Korteks debug UI unless product approves.
