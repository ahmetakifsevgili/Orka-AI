# Frontend Contract Freeze

## ChatMessageResponse
Stable fields: `messageId`, `sessionId`, `topicId`, `content`, `role`, `createdAt`, `modelUsed`, `messageType`, `wikiUpdated`, `planCreated`, `wikiPageId`, `isNewTopic`, `topicTitle`, `metadata`.

`metadata` is optional but sync chat now returns it when possible. Inline `[doc:sourceId:pN]` citations stay in `content`. Do not infer source/tool/provider state from prose.

Metadata: `citations[]`, `usedTools[]`, `groundingMode`, `fallbackReason`, `sourceConfidence`, `providerWarnings[]`. Empty arrays are valid empty state; null confidence means unknown. Grounding values: `source_grounded`, `model_fallback`, `degraded`, `unknown`.

## Topic DTO
Stable fields: `id`, `title`, `parentTopicId`, `category`, `planIntent`, `emoji`, `currentPhase`, `order`, `totalSections`, `createdAt` when present. `GET /api/topics/{id}` returns these fields at the top level and keeps the legacy nested `topic` object for compatibility. Plan intents: `Core`, `DeepDive`, `PracticeLab`, `QuickReview`, `Remediation`, `Assessment`, `Module`, null. Read `planIntent` first; `category=Plan:*` is legacy compatibility.

## Source DTO
Fields: `id`, `topicId`, `sessionId`, `fileName`, `title`, `contentType`, `fileSize`, `chunkCount`, `status`, `isDeleted`, `version`, timestamps. Ask returns answer plus `citations[]`/metadata. Deleted source returns 404.

## Wiki DTOs
List returns page summaries. Page returns `page`, `blocks[]`, `sources[]`. Export returns markdown content or 404 if no ready content. Briefing/glossary/study-cards/mindmap are structured but provider-dependent.

## Review DTO
Fields: `id`, `topicId`, `reviewKey`, `skillTitle`, `skillTag`, `conceptTag`, `learningObjective`, `mistakeCategory`, `sourceType`, `sourceId`, `dueAt`, `lastReviewedAt`, `intervalDays`, `easeFactor`, `repetitionCount`, `lapseCount`, `successStreak`, `status`, `flashcardId`, `flashcardFront`, `flashcardBack`. Complete body: `quality` 0..5, optional `responseMode`, `notes`.

## Flashcard DTO
Fields: `id`, `topicId`, `learningSourceId`, `wikiPageId`, `quizAttemptId`, `front`, `back`, `hint`, `skillTag`, `conceptTag`, `learningObjective`, `difficulty`, `status`, `createdFrom`, `reviewItemId`, timestamps.

## DailyChallenge DTO
Fields: `id`, `topicId`, `date`, `sourceType`, `sourceSkillTag`, `sourceConceptTag`, `questions[]`, `status`, `score`, `correctCount`, timestamps. Submit returns `duplicate`, `xpAwarded`; duplicate submit must not double-award XP.

## Badge / UserBadge DTO
Gamification exposes `id`, `code`, `name`, `description`, `iconKey`, `ruleType`, `threshold`, `earnedAt`.

## Notification DTO
Fields: `id`, `type`, `title`, `body`, `status`, `severity`, `relatedEntityType`, `relatedEntityId`, `channel`, `pushStatus`, `errorMessage`, `createdAt`, `readAt`, `expiresAt`. In-app rows are authoritative; Firebase push is optional.

## AudioOverview DTO
Fields: `jobId`, `status`, `script`, `speakers[]`, `contentType`, `fileName`, `downloadUrl`, `fallbackReason`, `errorMessage`, `createdAt`, `updatedAt`. Status: `pending`, `generating`, `ready`, `script-only`, `failed`.

## Classroom DTO
Session start is stable. Ask returns `classroomSessionId`, `interactionId`, `answer`, `answerScript`, and `speakers[]`; `answerScript` is the frontend-facing alias for the same script content as `answer`. Ask/audio are provider/TTS-dependent. Frontend should treat classroom answer/audio availability as stateful, not guaranteed immediate media.

## Stable State Values
Topic phase/status are current code strings; unknown values render neutral. Wiki: pending/learning/ready/failed. Source: uploaded/processed/failed plus `isDeleted`. Review: active/archived. Flashcard: active/deleted. Daily challenge: active/completed. Notification: unread/read. Unknown strings must not crash UI.

## Display Rules
Show citations from metadata first, inline tags second. Show fallback/provider banners from metadata. Empty arrays mean empty state, not loading.

## Tutor Pedagogy / Visualization / YouTube Addendum

Tutor may return Mermaid diagrams in `content` when the user asks for a diagram/schema/flowchart or when a process, architecture, lifecycle, dependency, or comparison benefits from a visual explanation. Frontend should render fenced `mermaid` code blocks and fall back to a normal code block if parsing fails. Mermaid is not a citation source. When detected, `metadata.usedTools[]` may include `{ name: "mermaid", status: "generated_text", evidence: "mermaid_fenced_block" }`.

Tutor may include markdown image URLs for visual explanations, especially Pollinations educational diagram URLs. Frontend can render these as normal markdown images or expose a later UI CTA. Dedicated backend visual job lifecycle is not frozen for Tutor chat; treat it as `READY_WITH_UI_DECISION`.

YouTube is a pedagogy/reference input by default, not factual grounding. Allowed uses are teaching flow, examples, analogies, common mistakes, practice ideas, and video recommendation only when actual YouTube evidence exists. Tutor must not claim it saw a specific video, teacher, or channel unless YouTube context is present. YouTube must not replace `[doc:sourceId:pN]` citations for factual source-backed claims.

When YouTube evidence is present or degraded, `metadata.usedTools[]` may include `{ name: "youtube", status: "ready|degraded|disabled", evidence: "youtube:<videoId-or-status>", fallbackReason: null|"youtube_degraded" }`. `providerWarnings[]` may include `youtube_disabled` or `youtube_degraded`.

Frontend rendering guidance:
- Render Mermaid blocks from `content`.
- Render YouTube/video reference cards only from `metadata.usedTools`, never from prose alone.
- Render fallback banners from `fallbackReason` and `providerWarnings[]`.
- Prefer `metadata.citations[]` for factual source chips; inline doc tags remain visible fallback.

Detailed addendum: `docs/tutor-pedagogy-visualization-contract.md`.
## Feature Parity Addendum: Bookmarks, Push, Tutor Tools

The backend now includes parity contracts for features carried forward from the original `D:\Orka` backend:

- `GET/POST/PATCH/DELETE /api/bookmarks`
- `GET/POST/DELETE /api/notifications/subscriptions`
- aliases under `/api/push/subscriptions`
- `/api/profile/xp`
- `/api/profile/badges`
- `/api/code/execute`
- flashcard legacy aliases under `/api/flashcards/topic/{topicId}`, `/api/flashcards/proposals/{topicId}`, `/api/flashcards/bulk`

`ChatMessageResponse.metadata.usedTools` may include safe tool hints for:

- `sources_query`
- `review_query`
- `flashcards`
- `daily_challenge`
- `bookmarks`
- `semantic_kernel`

Frontend must treat these as backend evidence/hints, not infer tool use from prose. External provider tools such as Wolfram, IDE auto-execution, Weather, News, and Crypto remain beta/hidden unless a later contract explicitly marks them ready.

## Tool Capability Contract

Frontend must not infer tool availability from Tutor prose. Use:

- `GET /api/tools/capabilities`
- `GET /api/tools/capabilities/{toolId}`

Each tool returns `toolId`, `displayName`, `category`, `status`, `riskLevel`, `requiresAuth`, `requiresAdmin`, `requiresExternalProvider`, `configKey`, `timeoutMs`, `costTracked`, `telemetryEnabled`, `fallbackMode`, `inputSchema`, `outputSchema`, `decision`, and `notes`.

Dirty-Orka tools are now explicit:

- `wolfram_alpha`: disabled stub unless provider key is configured.
- `ide_execution`: high-risk; SK auto-execution is disabled/admin-dev gated. Use `/api/code/run` or `/api/code/execute` for sandbox-backed API execution.
- `weather`, `news`, `crypto`: beta/disabled stubs until product/provider gates are enabled.
- `visual_generation`: beta visual generation; Mermaid remains low-risk text-only.
- `youtube_pedagogy`: pedagogy/reference by default, not factual grounding unless transcript/source evidence is explicit.

Frontend display rule: disabled, beta, admin-only, and dev-only tools should render as unavailable or gated controls, never as silently missing features.
