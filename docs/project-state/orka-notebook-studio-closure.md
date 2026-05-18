# OrkaLM / Wiki-aware Notebook Studio Closure

Date: 2026-05-17

## Result

Status: professionally closed as a safe foundation. Phase 24-25 final safety/privacy/product closure result: PASS.

OrkaLM is not a generic NotebookLM clone. It is the Notebook-like study studio inside Orka's Learning OS. Its primary context is the student's Wiki page graph, uploaded/source evidence, plan state, quiz/misconception signals, mastery, snapshots, and Tutor pedagogy.

Final audit doc: `docs/project-state/orka-notebook-studio-final-audit.md`

## Product Shape

- Wiki is the Obsidian-like learning notebook: parent pages, child concept pages, remediation pages, source-backed pages, and review pages.
- Notebook Studio builds a `LearningNotebookPack` from a topic/milestone or a selected Wiki page.
- Packs collect completed concepts, weak concepts, misconception signals, source readiness, Wiki notebook state, artifact ids, warnings, and next actions.
- Pack artifacts include study guides, source digests, repair packs, worked examples, retrieval cards, audio overview/transcript, mind map, flashcard set, review quiz blueprint, and slide outline.
- Uploaded sources feed OrkaLM through `SourceEvidenceLifecycleService`; source-backed claims require ready/mixed evidence.

## Phase Matrix

| Phase | Closure state | Evidence | Remaining non-blocking limits |
| --- | --- | --- | --- |
| 1. Notebook Foundation Research | Closed | This document classifies core, adapter, legacy, and unsafe-to-delete services. | No separate spreadsheet inventory; this Markdown is the source of truth. |
| 2. Notebook-aware Service Consolidation | Closed with adapters | Ownership is explicit: `LearningArtifactService` owns artifact lifecycle; `TeachingArtifactService` and `WikiArtifactService` remain compatibility adapters. | Broad service deletion is intentionally not done. |
| 3. Notebook Studio Domain Contract | Closed | `LearningNotebookPack` is durable and user-scoped; Wiki page fields are first-class columns plus safe metadata fallback. | Existing legacy packs can still read Wiki page info from metadata. |
| 4. Milestone Learning Pack MVP | Closed | Topic/milestone and Wiki page pack endpoints consume snapshots, source bundle, Wiki notebook, concept mastery, page blocks, questions, and repair notes. | Fully automatic generation on every plan-step completion is avoided; UI exposes explicit pack creation. |
| 5. Study Guide / Source Digest / Repair Pack | Closed | Outputs are `LearningArtifact` records with source basis and trust checks; source digest does not overclaim when evidence is insufficient. | Text is deterministic/safe, not provider-generated long-form prose. |
| 6. Audio Overview v2 | Closed with fallback | Pack-aware audio uses Notebook pack context first, trust-checks scripts, stores `AudioOverviewJob`, and exposes audio/script fallback artifacts. | Interactive voice and long podcast generation are out of scope. |
| 7. Mind Map + Flashcard + Review Quiz | Closed | Mind map uses `ConceptGraphSnapshot`; flashcards use `FlashcardService` + SRS; review quiz uses `AssessmentBlueprintService` with no answer key. | Review quiz launch is blueprint/action level, not a full quiz-session wizard in this phase. |
| 8. Slide Deck Outline | Closed | `slide_deck_outline` artifact carries speaker notes, checkpoint questions, source warning, accessibility, and video-ready metadata. | PPTX/video export is intentionally backlog. |
| 9. Frontend Notebook Studio | Closed as compact Studio surface | `NotebookStudioPanel` is wired into Wiki, shows pack state, warnings, concepts, next actions, grouped artifacts, audio fallback/player, and safe artifact rendering. | Dedicated advanced visual editors for mind map/slide/flashcard remain backlog. |
| 10. Production Closure, Browser E2E & Notebook UX Hardening | Closed | Migration/model snapshot were aligned for `LearningNotebookPack`; backend lifecycle tests cover Wiki page filtering and soft-delete hiding; frontend smoke covers production states, source readiness, payload safety, and mojibake guard. | Browser screenshot verification was limited by unavailable local Browser/Playwright tooling; typecheck, smoke, build, and backend validations are the closure proof. |
| 11. Advanced Media & Export Architecture | Closed as foundation | Media/export outputs are `LearningArtifact` backed: audio transcript, caption track, video-ready package, slide export manifest, narration script, visual instruction set, and media accessibility note. Frontend shows media/export readiness without claiming video/PPTX generation. | Full video generation, PPTX export, media CMS, and interactive voice remain future explicit phases. |
| 12. Slide Deck Export MVP & Deterministic Export Contract | Closed | `NotebookExportService` transforms existing slide outline/export manifest artifacts into user-scoped preview, markdown, escaped HTML, and manifest-only export results. Frontend shows honest export readiness and PPTX-disabled status. | Real PPTX generation remains intentionally disabled because no safe local presentation export dependency exists in the repo runtime. |
| 13. Advanced Deck UX, Local Export Decision & Media Roadmap Closure | Closed | Export preview now exposes source basis/readiness, slide count, warnings, accessibility, and a compact slide list. Markdown, escaped HTML, and manifest-only outputs carry source/accessibility/checkpoint metadata. PPTX remains honestly disabled after dependency inspection. | Full PPTX export, themed decks, video rendering, media asset library, and interactive voice remain future explicit phases. |
| 14. Final OrkaLM Professional Audit & Closure | PASS | `orka-notebook-studio-final-audit.md` verifies Phase 1-13 integration, scoring, service ownership, safety/privacy, migration/model state, frontend UX, and validation results. | Browser visual screenshots remain conditional on Browser/Playwright tooling availability. |
| 15. Wiki Vault UX Productization | Closed | `WikiMainPanel` now presents a student-facing Wiki Vault surface with page tree/list search, status/source/evidence filters, active page context badges, backlinks, outgoing links, local graph neighbors, and block group summaries while keeping `NotebookStudioPanel` page-aware. | This is not a full Obsidian clone or canvas graph editor; advanced visual graph editing and dedicated source-notebook UX remain future work. |
| 16. Tutor-Wiki Learning Trace Writer | Closed | `WikiLearningTraceWriter` is the canonical page-aware writer for Tutor explanations, student questions, quiz results, misconception/repair notes, artifact links, and safe source notes. Tutor/chat, quiz recording, and Notebook artifact creation now use the writer with dedupe and safety redaction. | Trace writing is intentionally bounded and non-blocking; richer trace analytics and advanced graph canvas remain future work. |
| 17. Dedicated OrkaLM Source Notebook UX | Closed | Source notebook summaries, source-centered packs, `orkalm_source` Wiki pages, source readiness/citation warnings, and source-mode `NotebookStudioPanel` wiring make uploaded sources usable without a separate app/table. | Richer ask-source routing and source graph UX remained backlog for Phase 18+. |
| 18. Source-to-Concept Auto-Linking & Advanced OrkaLM Graph | Closed | `SourceConceptLinkingService` deterministically links uploaded sources to Wiki concept pages through safe `WikiLink` rows, exposes source concept graph DTOs/endpoints, enriches source/wiki packs with linked context, and renders related concepts/supporting sources in OrkaLM/Wiki UI. | Links are safe heuristics, not truth claims; low-confidence candidates remain suggestions. Full graph canvas and manual link editor remain backlog; richer source Q&A is closed in Phase 19. |
| 19. Advanced Source Q&A / Ask-Source UX | Closed | `SourceQuestionService` wraps the existing source/RAG/Tutor ask path into a citation-safe selected-source/source-collection contract, strips raw chunks from public DTOs, labels evidence state, surfaces related concepts/pages, and can write safe Wiki trace blocks. | Multi-source compare is closed in Phase 20; source Q&A memory is closed in Phase 21. Manual citation review tooling and advanced graph canvas remain backlog. |
| 20. Multi-source Compare & Citation Review UX | Closed | `SourceCompareService` compares selected user-owned sources by readiness, evidence status, citation coverage, and Phase 18 source-to-concept overlap. It exposes safe citation review items from `SourceCitationChecks`, can write bounded Wiki trace notes, and renders compare/review state in OrkaLM mode. | This is deterministic trust/coverage review, not semantic contradiction detection or an academic citation manager. Source Q&A memory is closed in Phase 21; manual citation annotation and advanced graph canvas remain backlog. |
| 21. Source Q&A Conversation Memory & Review Workflow | Closed | `SourceQuestionThreadService` persists selected-source/source-collection Q&A as bounded `source_question_thread` LearningArtifacts, stores only safe questions, answer summaries, citation labels, review states, and warnings, supports follow-up, review updates, optional Wiki trace writing, and renders thread memory in OrkaLM mode. | This is not long-term raw transcript storage or a separate chat app. Rich semantic synthesis, manual citation annotation, scheduled source-review workflows, and advanced graph canvas remain backlog. |
| 22-23. Source Q&A Study Workflow & Advanced OrkaLM Graph Polish | Closed | `SourceQuestionThreadService.GetStudySummaryAsync` derives a source-study summary from existing Q&A memory artifacts, citation checks, source readiness, and source-to-concept links. OrkaLM mode shows source study status, review/degraded/citation-warning counts, linked concept count, compare readiness, and next actions beside Q&A memory. | This is not a scheduler, semantic contradiction detector, graph canvas, or manual citation manager. Long-term review scheduling and richer semantic synthesis remain backlog. |
| 24-25. Final Safety/Privacy Audit & Product Closure | PASS | Final audit verifies source notebook, ask-source, Q&A memory, compare/citation review, source-study summary, Wiki trace, Notebook packs, artifacts, and export surfaces. Legacy source quality/retrieval DTO owner-id exposure and source page raw chunk/highlight DTO leakage were removed without migration. | Browser visual proof remains conditional on local Browser/Playwright access. Phase 26 should only prepare selective staging/commit; it should not add product scope. |

## Service Ownership Map

### Core OrkaLM Services

- `LearningNotebookStudioService`: OrkaLM orchestrator. It builds packs and pack artifacts. It does not own raw source truth or Tutor pedagogy.
- `SourceEvidenceLifecycleService`: source truth owner. It decides whether evidence is source grounded, mixed, stale, degraded, or insufficient.
- `LearningArtifactService`: canonical artifact lifecycle owner. Notebook outputs are persisted through this service.
- `AgenticTrustPolicyService`: local deterministic safety check for pack artifacts and audio scripts.
- `ActiveLessonSnapshotService`: current learner and lesson context source.
- `PlanSequencingService`: plan/step sequencing source.
- `AssessmentBlueprintService`: review quiz blueprint source.
- `ConceptGraphBuilder` and persisted concept graph entities: mind map source.
- `FlashcardService` and `ReviewSrsService`: flashcard and spaced review owners.

### Adapter / Compatibility Services

- `TeachingArtifactService`: still consumed by Tutor. It mirrors into `LearningArtifactService`; not safe to delete.
- `WikiArtifactService`: Wiki answer artifact adapter. It delegates/mirrors into `LearningArtifactService`; not safe to delete.
- `AudioOverviewService`: owns legacy audio endpoint and now prefers Notebook pack context when a pack exists. Legacy source/wiki/chat context remains a degraded fallback.
- `WikiService`: low-level Wiki page/block service used by Tutor, source import, chat post-processing, and Wiki UI. Not a duplicate of Notebook Studio.
- `WikiLearningServices`: source/evidence-aware Wiki notebook assistant layer. Not a replacement for `WikiService`.
- `SourceConceptLinkingService`: deterministic source-to-concept graph adapter. It uses existing source evidence, Wiki pages, concept keys/titles, and safe `WikiLink` metadata. It does not call AI and does not expose raw source chunks.
- `SourceQuestionService`: canonical ask-source adapter. It reuses the existing source ask/Tutor path but returns a safe `SourceQuestionResponseDto`, removes raw chunks/highlights from public citations, labels source basis/readiness, attaches related graph context, and writes Wiki traces only when requested.
- `SourceCompareService`: canonical deterministic multi-source compare/citation review adapter. It reads existing source lifecycle, `SourceCitationChecks`, and source-to-concept links; it never returns raw answer/claim/chunk/provider/debug/local-path payloads and never claims semantic agreement from weak deterministic data.
- `SourceQuestionThreadService`: canonical source Q&A memory and source-study summary adapter. It stores bounded thread state as `LearningArtifact` content, uses safe prior summaries for follow-up context, applies citation review states without upgrading unsupported answers to source-grounded, writes Wiki traces only from sanitized summaries, and derives compact source-study status from existing Q&A/citation/source graph data.

### Not Safe To Delete

- `AudioDialogueFormatter`: normalizes `[HOCA]` / `[ASISTAN]` / `[KONUK]` scripts.
- `EdgeTtsService`: current TTS adapter with script-only fallback if unavailable.
- `NotebookLmDtos`: legacy audio/Notebook DTO surface, still backing audio overview compatibility.
- `WikiMainPanel`, `NotebookStudioPanel`, `useLearningWorkspaceState`: frontend integration points for Wiki, OrkaLM, and workspace state.

## Flow Summary

### Wiki Page-aware OrkaLM

1. Student opens a Wiki page.
2. `WikiMainPanel` shows the Wiki Vault context: page tree/list, active page status, source/evidence badges, backlinks, outgoing links, and local graph neighbors.
3. `WikiMainPanel` passes the active page id/title to `NotebookStudioPanel`.
4. `NotebookStudioPanel` calls `POST /api/notebook-studio/wiki-page/{pageId}/pack`.
5. `LearningNotebookStudioService` loads the page, page blocks, questions, concepts, misconception keys, source readiness, snapshots, concept mastery, source evidence bundle, and Wiki notebook snapshot.
6. The service creates a `LearningNotebookPack` and safe `LearningArtifact` outputs.
7. Frontend renders pack state, warnings, concept signals, next actions, and grouped artifacts.

### Tutor / Quiz / Artifact Trace Flow

1. Tutor/chat post-processing calls `IWikiLearningTraceWriter` after a safe Tutor answer and metadata are available.
2. The writer resolves the best page by active Wiki page, concept key, source page, topic root, or a small safe fallback page.
3. Student questions are stored as `student_question`; Tutor notes are stored as `tutor_explanation`, `repair_note`, `source_note`, or `checkpoint` depending on metadata.
4. `QuizAttemptRecorder` writes post-submit `quiz_result` blocks and, for wrong/blank/partial outcomes, misconception or repair blocks. Client-provided correctness remains non-authoritative.
5. `LearningNotebookStudioService` writes `artifact_link` blocks for page-aware Notebook artifacts and safe `source_note` blocks for source digest outputs.
6. Dedupe uses page, block type, Tutor turn id, quiz attempt id, artifact id, and recent normalized content checks.
7. Block content is redacted for raw prompt/provider/tool/source/debug/local path/secret/answer-key markers before reaching public Wiki DTOs.

### Uploaded Source OrkaLM

1. Sources are uploaded through existing source APIs.
2. `SourceEvidenceLifecycleService` builds/refines source evidence bundles and Wiki notebook snapshots.
3. Notebook packs use only safe source summaries/evidence labels.
4. `source_digest` is `source_grounded` only when source evidence is ready/mixed; otherwise it is `evidence_insufficient`.

### Source-to-Concept OrkaLM Graph

1. A selected uploaded source can request `POST /api/sources/{sourceId}/concept-links/sync`.
2. `SourceConceptLinkingService` ensures a safe `orkalm_source` Wiki page exists for the source.
3. The service matches the source against existing Wiki concept pages using deterministic signals: concept key, page key/title, source title/file name, citation highlight/header hints, and bounded internal source text scoring.
4. High/medium confidence confirmed links are stored as idempotent `WikiLink` rows such as `source_supports` or `source_mentions`; low-confidence matches are returned as suggestions and are not overclaimed.
5. `GET /api/sources/{sourceId}/concept-links`, `GET /api/sources/topic/{topicId}/concept-graph`, and `GET /api/wiki/pages/{pageId}/source-links` expose safe graph summaries without raw source chunks, local paths, provider payloads, owner ids, or hidden prompts.
6. `WikiMainPanel` OrkaLM mode shows related concept pages, confidence labels, graph counts, sync action, warnings, and concept-page supporting sources.
7. Notebook Studio source and Wiki page packs include linked concept/source context as safe metadata and warnings, so generated artifacts can stay graph-aware without fabricating source-grounded claims.

### Artifact / Audio / Review Flow

1. A selected pack can request artifact generation.
2. Artifact content is built from pack state, source readiness, Wiki page context, and concept/misconception state.
3. Content is trust-checked before persistence.
4. Audio overview creates an `AudioOverviewJob` and safe transcript metadata.
5. Mind map uses concept graph.
6. Flashcard set creates cards and review items.
7. Review quiz creates a safe assessment blueprint without answer keys.
8. Slide outline creates speaker notes, checkpoints, and accessibility metadata without export.
9. Advanced media/export artifacts add transcript, caption, video-ready scene outline, slide export manifest, narration, visual instructions, and accessibility notes without binary export.
10. Slide export transforms the existing slide outline and manifest into a safe preview, markdown package, escaped HTML package, or manifest-only package.
11. Phase 13 hardens the deck UX and confirms that PPTX remains disabled until Orka itself has an approved safe export dependency.

## Phase 10 Production Closure

- `LearningNotebookPack` is represented consistently in entity, DbContext, migration, and model snapshot.
- Durable Wiki page fields are first-class: `WikiPageId`, `WikiPageTitle`, and `WikiPageKey`.
- Pack list supports topic/session and topic/wiki-page filtering.
- Soft-deleted packs are excluded from list/read flows.
- `NotebookStudioPanel` exposes source readiness, evidence status, stale/degraded/insufficient warnings, loading/empty/error states, grouped artifacts, and audio script fallback.
- Static frontend smoke guards verify Notebook Studio API wiring, production state copy, payload safety, answer-key safety, and mojibake protection.
- The UI remains a compact Wiki-integrated Studio surface rather than a separate app or admin dashboard.

## Phase 11 Advanced Media / Export Architecture

- New media/export artifact types are bounded and validated by the existing `LearningArtifactService`.
- `audio_transcript` and `caption_track` give audio/video text fallback and caption outline data without requiring a playable media file.
- `video_ready_package` stores scene ids, narration hints, visual instructions, timing hints, caption outline, accessibility notes, source labels, warnings, and `generatedVideo=false`.
- `slide_export_manifest` stores deck title, slide ids/titles, layout hints, citation labels, accessibility summary, `pptxGenerated=false`, and `exportReadiness=pptx_not_enabled`.
- `narration_script`, `visual_instruction_set`, and `media_accessibility_note` are safe preparation artifacts for future deterministic export or media tooling.
- `NotebookStudioPanel` exposes these actions and shows status/export-readiness/source badges without fake video player or fake PPTX download copy.
- No new provider, no Google Cloud, no new binary storage, no full video generation, and no real PPTX export were added.

## Phase 12 Slide Deck Export MVP

- `NotebookExportService` is a deterministic transformer, not a generation pipeline. It reads the user's own `LearningNotebookPack`, `slide_deck_outline`, and `slide_export_manifest` artifacts and returns safe export DTOs.
- Export endpoints are user-scoped through `NotebookStudioController`:
  - `GET /api/notebook-studio/packs/{packId}/export/preview`
  - `POST /api/notebook-studio/packs/{packId}/export`
- Supported formats:
  - `slide_preview`: deck title, slide count, slide titles, bullets, speaker-note availability, source labels, checkpoint questions, warnings, and accessibility summary.
  - `markdown`: bounded Markdown study/presentation package from the existing outline.
  - `html`: escaped static HTML package with no script/event/javascript/object/embed surface.
  - `manifest_only`: export readiness and source/accessibility metadata when full content export is not requested.
  - `pptx_local_proof`: explicitly returns `unsupported` / `pptx_not_enabled`; no fake file or download is exposed.
- The service does not call AI, does not create new learning content, does not expose local filesystem paths, and does not store raw export bodies in telemetry.
- Export results carry source basis, source readiness, evidence warnings, safety status, accessibility summary, and `expiresAt` metadata.
- Frontend `NotebookStudioPanel` exposes slide export preview/markdown/HTML/manifest actions and shows the PPTX status honestly as not enabled.
- Runtime telemetry records safe export status/count metadata only where the current telemetry service is available.

## Phase 13 Advanced Deck UX / Export Decision

- Export/PPTX decision: Orka keeps deterministic preview, Markdown, escaped HTML, and manifest-only exports for now. A tiny `pptx_local_proof` is not enabled because Orka's own `.csproj` / `package.json` runtime dependencies do not include an approved presentation export library. Bundled Codex workspace packages are not product runtime dependencies.
- The export service remains a deterministic transformer. It does not call AI, does not create new learning content, and does not expose local filesystem paths or binary data.
- The preview is now useful as a study-deck view: deck title, slide count, source basis, source readiness, warnings, accessibility summary, slide titles, bullet previews, checkpoint questions, speaker-note availability, and citation/source labels are surfaced.
- Markdown export includes source readiness, slide count, accessibility, speaker notes, checkpoint questions, source labels, and bounded warning copy.
- Escaped HTML export includes source readiness, accessibility, checkpoint questions, speaker-note markers, and warning sections while remaining static escaped markup.
- Manifest-only export is a bounded export package summary instead of a raw artifact dump: it records deck title, slide count, source basis/readiness, export readiness, PPTX status, accessibility, warnings, and slide ids/titles.
- Frontend `NotebookStudioPanel` shows the same truth: preview/export package exists; PPTX is not enabled; there is no fake video/PPTX download.
- Remaining media/export work is backlog by design: full PPTX export, themed deck export, video rendering, media asset library, interactive voice, and full NotebookLM parity.

## Phase 17 Dedicated OrkaLM Source Notebook UX

- OrkaLM is now a clearer source-centered surface for uploaded PDFs/TXT/MD sources while Wiki remains concept/page-centered.
- New safe source-notebook API surface:
  - `GET /api/sources/topic/{topicId}/notebook`
  - `GET /api/sources/{sourceId}/notebook`
  - `POST /api/notebook-studio/sources/{sourceId}/pack`
  - `POST /api/notebook-studio/topic/{topicId}/source-pack`
- Source notebook DTOs expose readiness, evidence status, citation coverage, warnings, linked `orkalm_source` Wiki pages, pack refs, and next actions. They do not expose raw chunks, local paths, owner ids, prompts, provider payloads, or debug payloads.
- Source packs reuse `LearningNotebookPack` and `LearningArtifactService`; no new source-notebook table or migration was added.
- A source pack creates or refreshes a safe `WikiPage.PageType=orkalm_source` page and can attach safe source-note traces through `WikiLearningTraceWriter`.
- `NotebookStudioPanel` can now operate in `source_notebook` mode and filters pack lists by `surface=source&sourceId=...`.
- `WikiMainPanel` OrkaLM mode shows source notebook status/readiness/citation warnings and passes selected source context to Notebook Studio.
- Existing raw source page viewer behavior was hardened: the UI no longer renders `chunk.text` or injects raw chunk text into suggested Tutor prompts; it uses citation labels/highlights instead.

Phase 17 remains bounded: no new AI/provider calls, no Google Cloud, no full NotebookLM parity, no source graph canvas, no real PPTX/video generation, and no separate app.

## Phase 18 Source-to-Concept Auto-Linking & Advanced OrkaLM Graph

- `ISourceConceptLinkingService` / `SourceConceptLinkingService` provides the canonical deterministic source-to-concept linking path.
- Source links use existing `WikiLink` rows; no new migration was required because link type metadata is string-based and already supports source-oriented link types.
- New safe API surface:
  - `GET /api/sources/{sourceId}/concept-links`
  - `POST /api/sources/{sourceId}/concept-links/sync`
  - `GET /api/sources/topic/{topicId}/concept-graph`
  - `GET /api/wiki/pages/{pageId}/source-links`
- Link DTOs expose source title, source page id, concept key/title, Wiki page id, link type, confidence, basis, readiness/evidence status, warnings, and timestamps. They do not expose raw source chunks, owner ids, local paths, provider payloads, prompts, tool payloads, or debug traces.
- Sync is idempotent and user-scoped. Re-running link sync updates existing links instead of creating duplicates.
- `LearningNotebookStudioService` enriches source packs with linked concept keys/source ids and Wiki page packs with supporting source ids/counts where safe.
- `WikiMainPanel` shows OrkaLM related concept pages, confidence labels, source-to-concept graph counts, sync action, and concept page supporting-source context.

Phase 18 remains bounded: no new AI/provider calls, no Google Cloud, no graph canvas editor, no manual link editor, no real PPTX/video generation, no full NotebookLM parity, and no source Q&A rewrite.

## Phase 19 Advanced Source Q&A / Ask-Source UX

- `ISourceQuestionService` / `SourceQuestionService` is now the canonical backend path for selected-source, topic source-collection, and generic ask-source requests.
- Existing source/RAG/Tutor infrastructure is reused; Phase 19 adds no new AI/provider calls and no Google Cloud or paid provider integration.
- New safe API surface:
  - `POST /api/sources/ask`
  - `POST /api/sources/{sourceId}/ask`
  - `POST /api/sources/topic/{topicId}/ask`
- Response DTOs expose answer text, `sourceBasis`, `evidenceStatus`, `sourceReadiness`, safe citation labels, related concept/page links, warnings, safety status, next actions, and optional Wiki trace block id.
- Public source-question DTOs do not expose raw source chunks, raw highlight text, local paths, owner ids, prompts, provider/tool payloads, hidden prompts, debug traces, or answer keys.
- `source_grounded` is only allowed when citations/evidence support it; missing/invalid citations degrade to `evidence_insufficient` or `mixed`.
- Topic source-collection ask is a bounded MVP: it uses a user-owned primary ready source and labels the limitation as `source_collection_primary_source_only`; deterministic selected-source compare is closed in Phase 20, while richer semantic comparison remains backlog.
- If `writeWikiTrace=true`, the service writes safe `student_question` and `source_note`/`tutor_explanation` blocks through `WikiLearningTraceWriter`. Trace failures never break the source answer.
- `WikiMainPanel` OrkaLM mode shows selected-source and source-collection ask actions, source basis/readiness/evidence badges, citation chips, related concept/page links, warnings, and trace status.

Phase 19 remains bounded: no new provider calls, no full NotebookLM clone, no separate chat app, no raw source rendering, no conversation memory for source Q&A, no manual citation annotation UI, and no real PPTX/video generation.

## Phase 20 Multi-source Compare & Citation Review UX

- `ISourceCompareService` / `SourceCompareService` is now the canonical backend path for selected-source compare and citation review.
- New source endpoints are user-scoped:
  - `POST /api/sources/compare`
  - `POST /api/sources/topic/{topicId}/compare`
  - `GET /api/sources/{sourceId}/citation-review`
  - `GET /api/sources/topic/{topicId}/citation-review`
- Compare results include source readiness, evidence status, citation coverage, shared linked concepts, source-only concepts, warnings, next actions, optional Wiki trace block id, and safe citation review items.
- Citation review exposes supported/unsupported/missing/stale/needs-review statuses from existing citation checks without exposing raw source chunks, raw answer text, raw claim text, prompts, provider/tool/debug payloads, local paths, owner ids, secrets, or answer keys.
- OrkaLM source notebook UI now supports selecting 2+ sources, running compare, viewing compared source cards, shared concept links, citation review rows, and review warnings.
- Compare intentionally says coverage/overlap/review-needed. It does not claim semantic agreement, contradiction, source authority, official readiness, or success guarantees.
- If requested, compare can write a bounded source-note style Wiki trace; trace failures do not break compare.

Phase 20 remains bounded: no new AI/provider calls, no Google Cloud, no full NotebookLM clone, no academic citation manager, no manual citation annotation workflow, no advanced graph canvas, no real PPTX export, and no video generation. Source Q&A memory is closed in Phase 21 as bounded safe summaries, not raw transcript storage.

## Phase 21 Source Q&A Conversation Memory & Review Workflow

- `ISourceQuestionThreadService` / `SourceQuestionThreadService` is now the canonical backend path for source Q&A memory.
- Threads are stored as `source_question_thread` `LearningArtifact` rows, so no migration was needed.
- Stored thread memory is bounded and sanitized:
  - safe question text,
  - safe answer summary,
  - source basis,
  - evidence status,
  - citation labels/status only,
  - related concept/page links,
  - review status and warnings.
- Follow-up questions reuse only safe prior summaries. Raw source chunks, prompts, provider payloads, tool/debug traces, local paths, owner ids, secrets, and answer keys are not persisted or exposed.
- Review updates cannot turn unsupported or citation-missing turns into source-grounded truth; unsupported/missing/stale states remain review/degraded labels.
- Optional Wiki trace writing records safe student question and answer summary blocks through `WikiLearningTraceWriter`; trace failure remains non-blocking.
- Source Notebook packs include a safe Source Q&A memory summary in pack summary/metadata and warning state when reviewed turns need attention.
- Frontend OrkaLM mode now shows a compact Source Q&A memory panel with thread list, active thread, prior question/summary cards, source basis/review badges, follow-up input, review action, write-to-Wiki action, and related concept links.

Phase 21 remains bounded: no new AI/provider calls, no Google Cloud, no full NotebookLM clone, no separate chat app, no raw transcript/chunk store, no CRM-style review queue, no manual citation annotation manager, no advanced graph canvas, no real PPTX export, and no video generation.

## Phase 22-23 Source Q&A Study Workflow & Graph/Review Polish

- `GET /api/sources/study-summary` exposes a user-scoped source-study summary for a topic/source/Wiki page context.
- The summary is derived from existing `source_question_thread` artifacts, `SourceCitationChecks`, source readiness/status, and source-to-concept `WikiLink` rows; it adds no new table or migration.
- Summary fields include source count, thread count, turn count, reviewed/needs-review/degraded counts, citation warning count, related concept count, compare-ready source count, source readiness, evidence status, study status, recommended next action, next actions, recent safe questions, and warnings.
- `WikiMainPanel` OrkaLM mode now shows a compact `Source study status` band above Source Q&A memory so students can see what needs citation review, whether source evidence is degraded, whether concepts are linked, and what to do next.
- Notebook Studio source packs continue to consume safe Q&A memory summaries and warnings; unresolved/degraded source Q&A remains a warning, not a source-grounded claim.
- The workflow remains deterministic and bounded: no new AI/provider calls, no semantic contradiction detection, no fake scheduling/due dates, no raw source chunks, no raw provider/tool/debug payloads, no local paths, and no owner ids in public DTOs.

## Safety Rules

- No raw source chunks in public pack/artifact content.
- No hidden prompts, provider payloads, raw tool payloads, local paths, secrets, or debug traces.
- No pre-submit answer keys.
- No official curriculum/exam readiness or success guarantee claims.
- Stale/degraded/insufficient evidence cannot be presented as source-grounded.
- Other users cannot read packs or pack artifacts.
- Source-to-concept links are confidence-labeled graph hints. Low-confidence links are suggestions and never become source-backed claims by themselves.
- Ask-source answers are citation/evidence labeled. Citation chips are user-safe labels, not raw chunks or raw source excerpts.

## Validation Checklist

Required closure validation:

- `dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter "LearningNotebookStudioTests|WikiGraphContractTests|LearningArtifactsEngineTests|SourceEvidenceLifecycleTests|QuizAttemptSafetyTests|AssessmentQualityMisconceptionTests|TutorPedagogyPolicyTests|AgenticSecurityTrustTests|SourceRegressionGuardTests" --no-restore --verbosity minimal`
- `dotnet test .\Orka.API.Tests\Orka.API.Tests.csproj --filter RegressionGateScriptTests --no-restore --verbosity minimal`
- `dotnet test .\Orka.Infrastructure.UnitTests\Orka.Infrastructure.UnitTests.csproj --no-restore --verbosity minimal`
- `scripts\quick-coordination.ps1`
- `scripts\quick-backend.ps1`
- `cd Orka-Front && npm run typecheck`
- `cd Orka-Front && npm run quick:smoke`
- `cd Orka-Front && npm run quick:build`
- `git diff --check`

Phase 10 also requires:

- model snapshot check for `LearningNotebookPack`
- Wiki page pack filtering test
- soft-deleted pack exclusion test
- frontend production state smoke check
- browser/screenshot proof when Browser or local Playwright tooling is available

Phase 11 also requires:

- advanced media/export artifact tests
- frontend smoke checks for video-ready and slide manifest actions
- no fake video/PPTX copy
- no raw prompt/provider/source/tool/debug payload in media artifacts
- no pre-submit answer key leakage

Phase 12 also requires:

- deterministic export service tests
- user-scope test for export preview/export
- markdown and escaped HTML export safety tests
- honest unsupported status for `pptx_local_proof`
- frontend smoke checks for export preview/action labels and no fake download copy

Phase 13 also requires:

- dependency-backed PPTX decision documented
- export preview usefulness checks for source basis/readiness, warnings, accessibility, and slide list
- Markdown/HTML/manifest safety checks for raw payloads, local paths, answer keys, and unsupported claims
- frontend smoke checks for PPTX-disabled copy, export preview labels, source warning, accessibility note, and no fake video/PPTX copy

Phase 17 also requires:

- source notebook endpoint user-scope tests
- source pack creation/filter tests
- source page duplicate prevention checks
- source digest/artifact raw-payload checks
- frontend smoke checks for source notebook API/UI, source-pack actions, and no raw chunk rendering

Phase 18 also requires:

- source concept link sync user-scope and idempotency tests
- exact concept key/title deterministic link test
- safe source concept graph DTO test with no raw chunks/local paths/provider payloads
- Wiki concept supporting-source endpoint test
- Notebook source/Wiki packs include linked context safely
- frontend smoke checks for concept link APIs, OrkaLM graph UI, supporting-source UI, confidence labels, and no fake source-backed claims

Phase 19 also requires:

- selected-source and topic source-collection ask endpoint user-scope tests
- missing/stale/invalid citation degradation checks
- public `SourceQuestionResponseDto` no raw chunk/local path/provider/tool/debug/prompt/owner id checks
- optional Wiki trace write tests for `student_question` and source answer blocks
- frontend smoke checks for ask-source API methods, selected/collection ask UI, source-basis labels, citation chips, related concept links, and no raw chunk rendering

## Intentional Backlog

- Full NotebookLM parity is not a goal for this phase.
- Interactive voice is not implemented.
- Video generation is not implemented; only video-ready package data exists.
- PPTX export is not implemented; slide preview, Markdown, escaped HTML, and manifest-only export packages exist.
- Themed deck export and `pptx_local_proof` remain disabled until Orka has an approved local export dependency and explicit user direction.
- Advanced visual mind map editor is not implemented.
- Advanced source notebook graph/citation cluster editor is not implemented.
- Richer semantic source comparison, manual citation annotation, long-term source Q&A study scheduling, and source Q&A citation-cluster graph UX remain backlog.
- Full automatic pack generation on every plan transition is intentionally avoided to prevent noisy artifact spam.
- Broad deletion/consolidation of legacy services is intentionally avoided until references and runtime behavior are audited in a cleanup-only phase.

## Phase 27 Post-closure Polish Gate

- Global public DTO/API privacy sweep must confirm no student-facing response exposes owner/user ids, raw source chunks, raw prompts, raw provider/tool/debug payloads, local paths, secrets, or pre-submit answer keys.
- Legacy classroom wording may remain in backend compatibility names, but student-facing copy should describe a personal AI audio lesson/Tutor mode rather than a live classroom or institutional workflow.
- Browser visual E2E should be attempted when local tooling and authenticated seeded data are available; otherwise report the limitation and rely on deterministic tests, typecheck, smoke, and build.
- Seeded Browser proof should include at least one topic and ready uploaded sources; OrkaLM source mode must not stay blocked by empty Wiki page polling when source notebook data is available.
- Narrow viewport smoke should verify source notebook content remains reachable; compact shell behavior is acceptable, full mobile redesign remains outside Phase 27.
- Provider nullable cleanup is allowed only when it is behaviorless and does not add or reroute provider calls.
- Phase 27 remains polish only: no new AI/provider calls, no real PPTX/video, no interactive voice, no graph canvas editor, and no teacher/classroom/dershane workflows.
