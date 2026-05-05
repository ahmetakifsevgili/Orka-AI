# Frontend Readiness Matrix

| Screen / Feature | Backend Endpoints | Classification | Notes |
|---|---|---|---|
| Auth screens | `/api/auth/register, /api/auth/login, /api/auth/refresh` | READY_FOR_FRONTEND | 201/200/409/401 covered |
| Dashboard | `/api/dashboard/stats, /api/user/gamification` | READY_FOR_FRONTEND | XP/badges/streak |
| Topic tree/sidebar | `/api/topics, /api/topics/tree` | READY_FOR_FRONTEND | planIntent available |
| Plan view | `topics + quiz plan diagnostic` | READY_WITH_UI_DECISION | provider-dependent plan generation |
| Tutor chat | `/api/chat/send` | READY_FOR_FRONTEND | metadata available |
| Tutor Mermaid diagrams | `/api/chat/send` content + metadata | READY_FOR_FRONTEND | render fenced `mermaid`; code fallback if invalid |
| Tutor YouTube teaching reference | `/api/chat/send` metadata.usedTools | READY_WITH_UI_DECISION | YouTube is pedagogy/reference, not factual proof |
| Visual/diagram generation | `/api/chat/send` content | READY_WITH_UI_DECISION | Mermaid and image markdown supported; no visual job UX frozen |
| YouTube/video reference card | `metadata.usedTools` | READY_WITH_UI_DECISION | render cards only from metadata/tool evidence |
| Pedagogy/fallback banners | `metadata.fallbackReason/providerWarnings` | READY_FOR_FRONTEND | show degraded YouTube/tool state without blocking answer |
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
## Feature Parity Addendum

| Screen / Feature | Backend State | Frontend Classification | Notes |
| --- | --- | --- | --- |
| Bookmarks | `GET/POST/PATCH/DELETE /api/bookmarks` | READY_FOR_FRONTEND | Can bookmark topic/message/source/wiki/review/flashcard contexts. |
| Push subscriptions | `/api/notifications/subscriptions`, `/api/push/subscriptions` | READY_WITH_UI_DECISION | In-app notifications remain primary; push enablement UX is optional. |
| Profile XP aliases | `/api/profile/xp`, `/api/profile/badges` | READY_FOR_FRONTEND | Compatibility surface over accepted gamification data. |
| Code execute alias | `/api/code/execute` | HIDE_UNTIL_BETA | Same validation/safety rules as `/api/code/run`. |
| Tutor SK tool hints | chat metadata `usedTools[]` | READY_WITH_UI_DECISION | Tool banners can be rendered from metadata, not prose. |
| External info tools | SK plugins deferred/gated | HIDE_UNTIL_BETA | Weather/news/crypto/Wolfram need provider and product safety gates. |
| Dev cleanup tooling | Not public | DEV_ADMIN_ONLY | Do not expose destructive cleanup in frontend. |

## Backend Hardening Tool Matrix Addendum

| Feature | Backend contract | Frontend Classification | Notes |
| --- | --- | --- | --- |
| Tool capability/status panel | `GET /api/tools/capabilities` | READY_FOR_FRONTEND | Use this instead of guessing tool availability from prose. |
| Wolfram tool | capability row + disabled stub | HIDE_UNTIL_BETA | Requires provider key and product approval. |
| IDE/SK auto-execution | capability row + disabled/admin-dev stub | HIDE_UNTIL_BETA | `/api/code/execute` remains the sandbox-backed API alias. |
| Weather | capability row + disabled/beta stub | HIDE_UNTIL_BETA | External info, not core learning evidence. |
| News | capability row + disabled/provider stub | HIDE_UNTIL_BETA | Must show dates/source if later enabled. |
| Crypto | capability row + disabled/beta stub | HIDE_UNTIL_BETA | Educational market data only; no financial advice. |
| Visual generation | capability row + existing Pollinations markdown plugin | READY_WITH_UI_DECISION | Render generated image links as illustrative. |
| Mermaid | content rendering contract | READY_FOR_FRONTEND | Text-only, low-risk. |
| Background worker controls | internal capability row only | PRODUCTION_HARDENING | Not a frontend blocker. |
| Cost tracking dashboard | internal capability row only | PRODUCTION_HARDENING | Not a frontend blocker. |
