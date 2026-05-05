# Backend Feature Parity + Semantic Kernel Runtime

## Executive Summary

This addendum reconciles valuable backend capabilities found in `D:\Orka` into the accepted validation line without replacing the durable schema/contract work already accepted in `D:\Orka-main-validation`.

The target line remains canonical for:

- durable Review/SRS, Flashcards, DailyChallenge, XP events, Badges, Notifications
- Topic `planIntent`
- structured chat metadata
- source delete/update lifecycle
- audio status contract
- frontend API contract freeze

## Ported Now

| Capability | Status | Notes |
| --- | --- | --- |
| Bookmarks | PORTED_NOW | Added durable bookmark entity, API, soft delete, topic/message/source/wiki/review/flashcard references. |
| Push subscriptions | PORTED_NOW | Added optional subscription persistence. In-app notifications remain primary; Firebase/push delivery can be layered later. |
| Semantic Kernel learning plugins | MERGED_WITH_EXISTING | Added Sources, Review, Flashcards, DailyChallenge, Bookmarks, LearningMode, AgentDecision, Visuals plugins using validation services. |
| Plugin telemetry | PORTED_NOW | Added SK invocation filter for structured logs. |
| Chat usedTools hints | PORTED_NOW | Added safe Tutor tool-runtime metadata probe for source/review/flashcard/daily/bookmark/tool intents. |
| Route aliases | PORTED_NOW | Added `/api/health/*`, `/api/profile/xp`, `/api/profile/badges`, `/api/code/execute`, and flashcard legacy aliases. |

## Gated / Deferred

| Capability | Classification | Reason |
| --- | --- | --- |
| WolframAlpha direct plugin | HIDE_UNTIL_BETA | Provider/key dependent; should be enabled with explicit tool safety/metadata proof. |
| Weather / News / Crypto plugins | HIDE_UNTIL_BETA | Useful external-info tools but not core learning blockers; avoid unsupported advice or provider noise. |
| IDE execution as Tutor auto-tool | HIDE_UNTIL_BETA | `/api/code/run` and `/api/code/execute` exist; direct Tutor auto-execution needs sandbox/product guard. |
| TestCleanupController | DO_NOT_PORT_PUBLICLY | Destructive dev/test cleanup must not be public production API. |
| SRS/DailyChallenge background workers | PRODUCTION_HARDENING | Endpoint/service lifecycle is accepted; hosted timing workers need separate no-flake proof. |
| Cost tracking service | PRODUCTION_HARDENING | Existing token/cost fields remain; richer cost audit can follow without blocking frontend. |

## Semantic Kernel Runtime Contract

The accepted backend now exposes a broader SK plugin surface while preserving the existing Tutor context path. Plugins are registered through DI and are safe to initialize without requiring provider-heavy calls.

Frontend must continue to trust `ChatMessageResponse.metadata` rather than prose. Tool hints appear in `metadata.usedTools` when the request/response indicates a relevant backend tool surface:

- `sources_query`
- `review_query`
- `flashcards`
- `daily_challenge`
- `bookmarks`
- `semantic_kernel`
- `mermaid`
- `pollinations`
- `youtube`

Unavailable providers/tools should appear as `status=unavailable/degraded` with `fallbackReason` instead of causing a hard 500.

## Bookmarks API Contract

Routes:

- `GET /api/bookmarks`
- `GET /api/bookmarks?topicId={topicId}`
- `POST /api/bookmarks`
- `PATCH /api/bookmarks/{id}`
- `DELETE /api/bookmarks/{id}`

Auth is required. Cross-user resources are hidden by ownership checks. Delete is soft-delete (`status=deleted`).

Bookmark targets may include:

- `topicId`
- `sessionId`
- `messageId`
- `learningSourceId`
- `wikiPageId`
- `reviewItemId`
- `flashcardId`

## Push Subscription Contract

Routes:

- `GET /api/notifications/subscriptions`
- `POST /api/notifications/subscriptions`
- `DELETE /api/notifications/subscriptions/{id}`
- aliases under `/api/push/subscriptions`

Push subscriptions are optional. In-app `Notification` rows remain the durable first write. Firebase or web push failure must not block in-app notification creation.

## Route Compatibility

Compatibility aliases added:

- `/api/health/live`, `/api/health/ready`, `/api/health`
- `/api/profile/xp`
- `/api/profile/badges`
- `/api/code/execute`
- `/api/flashcards/topic/{topicId}`
- `/api/flashcards/proposals/{topicId}`
- `/api/flashcards/bulk`

## Remaining

True backend blockers: none currently known after this integration set.

Frontend UI decisions:

- where bookmark controls live
- whether push subscription UX is enabled immediately
- how to display tool banners for beta tools

Beta improvements:

- Tutor direct SK auto-invocation for Wolfram/IDE/Visual provider paths
- richer tool call evidence beyond safe metadata probing

Production hardening:

- hosted SRS/DailyChallenge workers
- richer cost tracking/audit
- provider-specific policy gates
