# Frontend Contract Freeze

## ChatMessageResponse
Stable fields: `messageId`, `sessionId`, `topicId`, `content`, `role`, `createdAt`, `modelUsed`, `messageType`, `wikiUpdated`, `planCreated`, `wikiPageId`, `isNewTopic`, `topicTitle`, `metadata`.

`metadata` is optional but sync chat now returns it when possible. Inline `[doc:sourceId:pN]` citations stay in `content`. Do not infer source/tool/provider state from prose.

Metadata: `citations[]`, `usedTools[]`, `groundingMode`, `fallbackReason`, `sourceConfidence`, `providerWarnings[]`. Empty arrays are valid empty state; null confidence means unknown. Grounding values: `source_grounded`, `model_fallback`, `degraded`, `unknown`.

## Topic DTO
Stable fields: `id`, `title`, `parentTopicId`, `category`, `planIntent`, `emoji`, `currentPhase`, `order`, `totalSections`, `createdAt` when present. Plan intents: `Core`, `DeepDive`, `PracticeLab`, `QuickReview`, `Remediation`, `Assessment`, `Module`, null. Read `planIntent` first; `category=Plan:*` is legacy compatibility.

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
Session start is stable. Ask/audio are provider/TTS-dependent. Frontend should treat classroom answer/audio availability as stateful, not guaranteed immediate media.

## Stable State Values
Topic phase/status are current code strings; unknown values render neutral. Wiki: pending/learning/ready/failed. Source: uploaded/processed/failed plus `isDeleted`. Review: active/archived. Flashcard: active/deleted. Daily challenge: active/completed. Notification: unread/read. Unknown strings must not crash UI.

## Display Rules
Show citations from metadata first, inline tags second. Show fallback/provider banners from metadata. Empty arrays mean empty state, not loading.
