# Orka Learning OS Contract Map

Status: Pack 0 source-of-truth architecture map  
Scope: Main Learning OS Professionalization  
Last reviewed: 2026-05-18

## Purpose

Orka is a student-facing learning system. Tutor is the pedagogical owner of the learner experience; Korteks researches; RAG/Wiki provides source-grounded knowledge; quiz and assessment measure concepts and misconceptions; tools are governed capabilities; Central Exams is a domain module inside this architecture, not the architecture itself.

This document maps the current repo reality and locks the professionalization roadmap. It does not claim every target is already implemented.

## Source-Of-Truth Docs

Current source-of-truth:

- `docs/architecture/orka-learning-os-contract-map.md`
- `docs/project-state/current-roadmap.md`
- `docs/dev-contract.md`
- `docs/frontend-contract.md`
- `docs/tutor-pedagogy-visualization-contract.md`
- `docs/codex-skills/README.md`

Historical / reference docs:

- `docs/audit/orka-ana-tamir-roadmap-2026.md` - original Tutor-centered repair roadmap.
- `docs/audit/orka-v2.9-quality-reality-gate.md` - earlier quality reality gate.
- `docs/audit/orka-v2.10-heavy-learning-flow-eval.md` - gated heavy learning flow evaluation.
- `docs/audit/tool-activation-tutor-consumption-hardening.md` - tool activation and Tutor consumption hardening.
- `docs/audit/living-learning-organism-map.md` - optimistic system map; use as historical evidence, not a final professional launch claim.
- `docs/architecture/ORKA_MASTER_GUIDE.md` and `docs/architecture/ORKA_SYSTEM_ARCHITECTURE.md` - legacy architecture guides; useful for history but not precise enough for Pack 1-11 implementation ownership.

## Service Ownership Map

| Area | Current owners | Current evidence | Target owner after professionalization |
|---|---|---|---|
| Intent / request classification | `StudyIntentAnalyzer`, plan diagnostic entrypoints, orchestrator state checks | Plan flow requires approved intent before Korteks in `PlanDiagnosticService` and frontend plan intent UI | Pack 1 snapshot contract records approved intent as active learning state |
| Korteks research | `KorteksAgent`, SK plugins, `KorteksToolCaptureFilter` | Research can auto-invoke Semantic Kernel tools and capture source evidence | Pack 3 Korteks workflow produces bounded research and synthesis artifacts |
| Research compression / synthesis | `PlanResearchCompressor`, `PlanIntelligenceBriefBuilder` | Plan diagnostic compresses Korteks output before quiz/plan | Pack 3 makes synthesis a formal contract for plan, quiz, wiki, and Tutor |
| Plan generation | `DeepPlanAgent`, `PlanDiagnosticService` | Uses research brief, adaptive context, concept graph guidance and structural minimums | Pack 5 adds semantic plan scoring and curriculum sequencing gates |
| Plan quality | `DeepPlanAgent` structural rules, heavy eval docs, quality report support | Minimum module/lesson count exists; topic-specific semantic quality is not fully centralized | Pack 5 `PlanQualityScorer` and revision loop |
| Quiz / assessment generation | `AssessmentGrammarEngine`, `DiagnosticQuizQualityGate`, `QuizAgent`, `PlanDiagnosticService` | Assessment grammar creates concept/difficulty/misconception specs; quality gate blocks product-label leaks | Pack 6 unifies quiz generation and final item quality |
| Quiz attempt recording | `QuizAttemptRecorder` | Writes attempts, quiz run counts, XP, review pressure, learning events, learning signals | Pack 6 normalizes every assessment flow into one result pipeline |
| Misconception detection | `MistakeClassifierService`, `MisconceptionIntelligenceEvaluator`, `QuizAttemptRecorder` | Wrong answers create mistake classifications and remediation seeds | Pack 6 formal misconception engine with Tutor/Wiki/plan handoff |
| Knowledge tracing / mastery | `KnowledgeTracingService`, `ConceptMasteryService`, `SkillMasteryService` | Attempts update knowledge tracing and concept mastery | Pack 1 snapshot and Pack 6 assessment pipeline expose one learner-state contract |
| Learning memory | `LearningMemoryService` | Builds weak/strong topics, remediation-ready items, confidence summary | Pack 1 makes memory one input to `StudentContextSnapshot` |
| Adaptive planner | `AdaptiveStudyPlannerService`, `DeepPlanAgent` | Uses learner evidence but is not the single lesson planner contract | Pack 5 connects plan quality, curriculum sequencing, and memory |
| Tutor turn state | `TutorTurnStateAssembler`, `TutorWorkingMemoryService`, `TutorAgent` | Builds turn state from graph, profile, wiki/source, learning signals, IDE context | Pack 1 turns partial context into `ActiveLessonSnapshot` / `StudentContextSnapshot` |
| Tutor action planning | `TutorActionPlanner`, `TutorPolicyEngine` | Creates teaching mode, direct-answer policy, artifacts, tools | Pack 7 closes pedagogy and response policy around snapshot and tool ledger |
| Tutor tool orchestration | `TutorToolOrchestrator` | Durable `TutorToolCall` rows and stream events for allowlisted Tutor tools | Pack 2 becomes the unified tool runtime for all agent/tool planes |
| Semantic Kernel plugin plane | `Kernel` registration in `Program.cs`, `PluginTelemetryFilter`, SK plugins | Plugin telemetry records tool id and latency but user/session/topic/correlation are null | Pack 2 converts SK plugins into governed adapters or explicitly bounded auto-invoke scopes |
| Tutor pedagogy evaluation | `TutorPedagogyRubricService`, `TutorPedagogyEvaluationService`, `TutorPedagogyQualityGate` | Deterministic rubric and repair loop exist | Pack 7 adds end-to-end Tutor answer quality closure and golden scenarios |
| RAG/source evidence | `LearningSourceService`, `WikiEvidenceService`, `SourceQualityServices`, `RagEvaluationService` | Retrieves source chunks, wiki blocks, citation/evidence quality | Pack 4 makes source-to-citation lifecycle explicit and invalidation-safe |
| Wiki notebook / knowledge workspace | `WikiService`, `WikiLearningAssistant`, `WikiArtifactService`, `WikiCitationGuard` | Wiki exposes workspace state, assistant, citations, briefing/glossary/study assets | Pack 4 makes notebook organization and per-topic evidence lifecycle stable |
| OrkaLM / Notebook Studio | `LearningNotebookStudioService`, `NotebookExportService`, `LearningArtifactService`, `SourceEvidenceLifecycleService`, `SourceConceptLinkingService`, `SourceQuestionService`, `SourceCompareService`, `SourceQuestionThreadService`, `AudioOverviewService`, `FlashcardService`, `AssessmentBlueprintService`, `WikiLearningTraceWriter` | Wiki page-aware packs combine source evidence, snapshots, mastery, misconceptions, artifacts, audio, mind maps, flashcards, review quiz blueprints, slide outlines, safe media/export manifests, and deterministic slide export preview/Markdown/escaped HTML/manifest packages with explicit source/accessibility warnings. `WikiMainPanel` presents the Wiki Vault UX: page tree/list search, source/evidence filters, active page context, backlinks, outgoing links, local graph neighbors, page-aware Notebook Studio actions, OrkaLM source notebook context, deterministic source-to-concept graph summaries, selected-source/source-collection ask-source UX, deterministic multi-source compare/citation review, bounded source Q&A thread memory, and compact source-study status summaries. `WikiLearningTraceWriter` is the canonical non-generative writer for Tutor/student/quiz/repair/source/artifact/compare/Q&A learning traces into Wiki blocks. `SourceConceptLinkingService` is the canonical non-generative linker from uploaded sources to existing Wiki concept pages. `SourceQuestionService` is the canonical ask-source adapter that reuses the existing source/RAG/Tutor path while returning safe citation/evidence DTOs and optional Wiki traces. `SourceCompareService` is the canonical compare/review adapter over source lifecycle, citation checks, and source-to-concept links. `SourceQuestionThreadService` is the canonical bounded memory and source-study summary adapter over source Q&A safe summaries, citation review state, source readiness, and graph context. Phase 24-25 final closure verifies that public source quality/retrieval DTOs do not expose owner ids and source page evidence DTOs do not return raw chunk/highlight text. | OrkaLM remains a Wiki/source-aware study studio, not a standalone NotebookLM clone or full Obsidian clone; PPTX/video generation, semantic source contradiction detection, raw transcript storage, source-review scheduling, manual citation annotation, manual link editor, and advanced canvas graph editing are not enabled until a safe explicit phase is approved |
| Teaching artifacts | `TeachingArtifactService`, frontend `ArtifactCanvas`, Mermaid/image markdown contract | Tutor action plan can produce artifacts; frontend can render artifact canvas and Mermaid fallback | Pack 8 creates one artifact lifecycle for diagram/image/video/table/formula/study-note outputs |
| Tool capability governance | `ToolCapabilityService`, `ToolCapabilitiesContext`, tool capability endpoints | Frontend should read capability endpoint, not Tutor prose | Pack 2 and Pack 10 connect capabilities, ledger, telemetry, cost, and frontend states |
| Runtime telemetry / cost | `RuntimeTelemetryService`, provider telemetry, cost records | Custom `orka.tool` / `orka.cost` activities and SQL rows exist | Pack 10 aligns spans/events with the main learning flow and GenAI/tool semantics |
| Frontend learning workspace | `ChatPanel`, `ChatMessage`, `AgenticWorkspace`, `WikiMainPanel`, smoke guards | Metadata chips, learning trace, agent status rail, artifact canvas exist | Pack 9 synchronizes plan/quiz/tutor/wiki/tool state as one workspace |
| Central Exams domain | Central exam, question bank, curriculum, content ops, quality analytics services | KPSS works; YKS/LGS/YDS scaffold; results feed learning signals | Remains a domain module; must reuse snapshots, tools, assessment, wiki, and Tutor contracts |

### Pedagogical Productization Phase 1 Addendum

- `TutorActionPlanner` now emits a safe `TutorToolDecisionDto` alongside teaching mode, tool plans, and artifact plans.
- The decision is deterministic and uses existing learner state, remediation signals, source evidence readiness, Wiki/source/IDE context, review pressure, and research/artifact availability.
- Evidence-limited source intent no longer selects `source_grounded_answer`; it blocks source-grounded routing and prefers clarification or model-assisted explanation with source limits.
- `ChatResponseMetadata.TutorToolDecision` exposes only selected action, safe reason codes, learner-signal labels, allowed/blocked tool ids, evidence/readiness status, and student-safe summary.
- No provider, OpenAI Responses/Agents migration, remote MCP, or new tool execution surface is introduced by this polish.

### Pedagogical Productization Phase 2 Addendum

- `TutorActionPlanner` now emits a safe `TutorLessonDeliveryDto` after the Phase 1 tool decision is made.
- The delivery contract is deterministic and chooses a teaching mode from learner level, mastery/confidence, quiz/remediation signals, source evidence readiness, and Tutor response policy.
- Supported delivery modes include `concept_explanation`, `guided_example`, `checkpoint_question`, `quiz_review`, `misconception_repair`, `prerequisite_repair`, `source_grounded_explanation`, `model_assisted_explanation`, and `ask_clarifying_question`.
- The lesson rubric is passed into Tutor prompt guidance and public chat metadata as safe structure, rubric flags, step labels, warnings, and student-visible summary. It does not expose raw prompts, provider/tool payloads, source chunks, local paths, owner ids, or answer keys.
- `AgentOrchestratorService` uses delivery metadata to make Wiki trace block typing cleaner: repair notes, source notes, checkpoints, worked examples, and Tutor explanations remain safe and deduped through existing trace paths.
- No provider, OpenAI Responses/Agents migration, remote MCP, or new generation surface is introduced by this polish.

### Pedagogical Productization Phase 3 Addendum

- `PlanSequencingService` now emits safe adaptive diagnostic and course-plan quality metadata without adding storage migrations or provider calls.
- `AdaptiveDiagnosticDto` captures provisional intent, learner level, placement basis, diagnostic questions, prerequisite/weak concept signals, plan readiness, warnings, and next action.
- `CoursePlanQualityDto` captures readiness status, milestone count, checkpoint coverage, repair loops, assessment alignment, source evidence status, overclaim risk, and recommended next action.
- Plan readiness is explicitly bounded: `ready`, `needs_diagnostic`, `needs_prerequisite_check`, `needs_repair`, `source_limited`, `thin_plan`, or `degraded`.
- Tutor turn state and `ChatResponseMetadata` carry the diagnostic/course-plan summary so Phase 1 tool decisions and Phase 2 lesson delivery can prefer diagnostic, prerequisite check, or remediation when plan evidence is thin.
- Frontend trace chips render only compact labels and safe summaries; no raw prompt, provider/tool payload, raw source chunk, owner id, or answer key is exposed.
- Source-backed course planning still requires source evidence; learner level and exam readiness remain provisional unless backed by assessment/mastery evidence.

### Pedagogical Productization Phase 4 Addendum

- `QuizAttemptRecorder`, `TutorTurnStateAssembler`, `TutorActionPlanner`, and `AgentOrchestratorService` now carry a safe `RemediationLessonDto`.
- The remediation contract distinguishes wrong-answer repair, blank/skipped prerequisite repair, student-confused guided reteach, weak-concept repair, misconception repair, and source-evidence review.
- A repair lesson includes bounded trigger, repair type, evidence basis labels, lesson shape, checkpoint, outcome policy, warnings, and student-visible summary. It does not expose raw prompts, provider/tool payloads, source chunks, local paths, owner ids, or pre-submit answer keys/correct answers.
- Tutor prompt guidance receives repair type, goal, checkpoint, and next action so telafi turns can follow micro-lesson -> worked example -> guided practice -> checkpoint instead of generic re-explanation.
- Wiki trace content records the repair type and checkpoint as safe learning trace text; Notebook Studio can continue treating repair state as a repair-pack candidate without auto-generating artifacts on every miss.
- No provider, OpenAI Responses/Agents migration, remote MCP, new paid call, or new execution surface is introduced by this polish.

### Pedagogical Productization Phase 5 Addendum

- `IWikiAutoCurationService` / `WikiAutoCurationService` now produces a safe `WikiCurationSummaryDto` for Wiki pages without new storage migrations, provider calls, or destructive cleanup.
- Curation summarizes page hygiene as `clean`, `duplicate_trace`, `stale_trace`, `repair_pending`, `source_limited`, or `degraded`, with retained/merged/suppressed/stale signal counts, warnings, next action, and student-visible summary.
- `WikiLearningTraceWriter` dedupes repeated traces with normalized safe text/title comparison in addition to durable tutor/quiz/artifact identifiers, while preserving student manual notes.
- `LearningMemoryService` and `ActiveLessonSnapshotService` now expose `LearningMemoryHygieneDto`: bounded memory status, retained signals, merged weak-concept labels, safe warnings, and safe summaries only.
- Chat metadata, Wiki page DTOs, and Notebook Studio pack metadata consume curated memory/Wiki context; they do not expose raw transcripts, prompts, provider/tool payloads, source chunks, local paths, owner ids, or answer keys.
- Source-linked Wiki context stays evidence-aware: stale/deleted/insufficient source states degrade with warnings, and Tutor-generated notes are not treated as source citations.

### Pedagogical Productization Phase 6 Addendum

- `IWikiCopilotService` / `WikiCopilotService` now produces a safe page-aware `WikiCopilotContextDto` without provider calls or hidden autonomous actions.
- Copilot reads Wiki page/block state, curation summary, source/evidence readiness, weak concepts, repair state, artifact count, and Notebook pack status.
- Suggestions are deterministic handoffs: repair/checkpoint, weak-concept review, source ask/citation inspection when evidence is ready, Tutor help for thin pages, curation guidance for noisy pages, and Notebook Studio pack actions when page context is meaningful.
- Source-grounded suggestions are blocked/degraded when evidence is stale, deleted, degraded, or insufficient.

### Pedagogical Productization Phase 7 Addendum

- `PedagogicalReleaseClosureTests.ProviderFreeLearningLoop_ConnectsPedagogicalProductizationSurfaces` is the final deterministic release harness for the combined learning loop.
- The harness connects topic/goal, adaptive diagnostic, course-plan quality, Tutor tool decision, lesson delivery, remediation lesson, blank quiz impact, learning snapshot, Wiki curation/Copilot, OrkaLM source notebook, Notebook Studio context, dashboard reachability, and public payload leak guards.
- It uses existing smoke/in-memory provider-free services only. It does not add new provider calls, Stripe calls, OpenAI Responses/Agents migration, real PPTX/video, Realtime, or graph-canvas scope.
- Stripe/payment code was not found in the audited release surface; payment safety is not applicable unless a future payment module is intentionally added.

### Backend Release Hardening Phase 1 Addendum

- `AiDebugLogger` is safe by default: it summarizes provider diagnostics with provider, operation, HTTP status, model, endpoint host/path, payload length/hash, and redaction counts only.
- Raw prompts, provider request/response bodies, source chunks, tool payloads, debug traces, stack traces, local paths, secrets, owner ids, unsafe user ids, and answer keys are not written by the AI debug logger.
- AI debug file writing is disabled by default and requires an explicit development-only opt-in through environment configuration.
- Provider non-success logging keeps status and safe body-length/hash diagnostics instead of writing raw provider bodies. Provider failure diagnostics are zero-body: `RedactedDiagnostic` contains provider/status/category/retryability/body length/hash metadata only, not raw or redacted response body excerpts. No provider architecture migration, new OpenAI API surface, or new provider call path was introduced.

### Backend Release Hardening Phase 2 Addendum

- `BackendLifeTests` is the senior-QA HTTP lifetest path from register/login through topics, plan/diagnostic, Tutor/chat, quiz/remediation, learning snapshot, Wiki Copilot, source upload/evidence, Notebook Studio export preview, dashboard, cross-user privacy, and degraded source states.
- `PedagogicalReleaseClosureTests` remains the deterministic combined learning-loop harness for diagnostic-first planning, course-plan quality, Tutor tool decision, lesson delivery, remediation, quiz impact, snapshot, Wiki Copilot, Notebook Studio, source notebook, dashboard, and public payload safety.
- `scripts/quick-backend.ps1` now runs `BackendLifeTests|PedagogicalReleaseClosureTests` as a backend lifetest release proof before stabilization and coordination baselines. The path uses `ApiSmokeFactory` provider-free replacements and does not add paid provider calls or provider architecture migration.

### Backend Release Hardening Phase 3 Addendum

- `ApiSmokeFactory` applies test-host-only logging filters for noisy release-validation categories: EF Core in-memory information logs, disabled scheduled worker information logs, background queue lifecycle information logs, and the benign MediatR license banner.
- The filters do not clear providers and do not raise the application-wide minimum to error; backend warning/error logs remain visible during release validation.
- Quick backend scripts remain deterministic, provider-free, and aligned with the lifetest release proof. No production logging behavior, provider architecture, OpenAI API surface, or paid provider path was changed.

### Backend Release Hardening Phase 4 Addendum

- `.github/workflows/backend-release.yml` is the CI mirror of the backend release proof.
- The workflow runs on `windows-latest`, restores `Orka.sln`, prepares SQL Server LocalDB for lifecycle tests, runs `scripts/quick-backend.ps1`, runs `Orka.Infrastructure.UnitTests`, and checks `git diff --check`.
- The workflow does not configure real AI provider credentials and does not run `ExternalProviderIntegrationTests` or `ORKA_RUN_EXTERNAL_PROVIDER_TESTS`.
- The current local branch has no matching open PR and the public GitHub repository currently reports no Actions workflows/runs before this local workflow addition, so remote CI status must be verified after the workflow is pushed.

### Backend Production Readiness Phase 1 Addendum

- `LogPrivacyGuard` is the canonical helper for production-safe application logs. It converts raw GUIDs and cache/text keys into stable short non-reversible references and sanitizes bounded log messages.
- Core backend logs now use `UserRef`, `TopicRef`, `SessionRef`, `MessageRef`, `SourceRef`, `WorkflowRef`, or `KeyRef` style fields where correlation is needed; raw user/topic/session/message/source ids should not be written to normal production logs.
- Touched production log paths avoid logging raw prompts, provider request/response bodies, source chunks, tool payloads, answer keys, local file paths, owner ids, unsafe user ids, or stack traces. Expected failures log safe error type/status metadata instead of exception bodies where practical.
- AI/provider diagnostic policy remains unchanged from the zero-leak hardening: debug file logging is disabled by default, development-only opt-in, and provider diagnostics remain body-free. No provider architecture migration, OpenAI Responses/Agents migration, or new AI/provider call path was introduced.

### Backend Production Readiness Phase 2 Addendum

- Live/staging provider proof is split from the deterministic release baseline. `quick-backend.ps1`, `quick-coordination.ps1`, and CI remain provider-free and must not require real AI credentials.
- Real AI provider success proof requires explicit configured credentials. When credentials are missing, success-call proof is blocked honestly; invalid-token failure checks may still run as an opt-in safety probe without paid provider use.
- Provider failure diagnostics remain body-free. Cohere, HuggingFace, and Cohere embedding failure paths now use `AiProviderFailureMapper` so diagnostics keep provider/status/category/body length/hash metadata only.
- Keyless public reference providers such as Wikipedia/Open-Meteo/CoinGecko may be smoke-checked manually, but they are external reference signals, not curriculum truth or source-grounded evidence.
- No new provider architecture, OpenAI Responses/Agents migration, hidden Tutor behavior, or product feature was introduced by this phase.

### Backend Production Readiness Phase 3 Addendum

- Backend scale/readiness closure keeps the deterministic baseline provider-free. `quick-backend.ps1`, `quick-coordination.ps1`, and GitHub backend release CI must not run live AI/provider smoke checks or require provider secrets.
- Database readiness is guarded by production startup checks, migration-readiness health checks, and `DbIndexAuditService` for core high-traffic learning tables. Missing index work should be additive and migration-reviewed.
- Background work is bounded: `BackgroundTaskQueue` uses a bounded channel and per-job timeout; scheduled SRS/daily/retention/Redis workers are configuration-gated and batch/interval bounded.
- Source/file processing is bounded by upload size, extracted character, page, chunk, per-upload embedding, per-user daily embedding, per-hour upload, and per-topic source limits.
- Audio retention summaries now use aggregate DB queries instead of materializing audio rows and byte payloads. Purge remains bounded to 100 overview jobs and 100 classroom interactions per pass.
- Provider cost safety remains configuration-driven: protected environments must configure global and user AI cost/token limits before startup can pass. Live provider smoke remains explicit and separate from CI.

## Data Ownership Map

| Data family | Current durable/cache stores | Owner rule |
|---|---|---|
| Session/message/topic | SQL `Sessions`, `Messages`, `Topics`; Redis stream/cache helpers | Orchestrator creates; Tutor reads through context/snapshot |
| Plan diagnostic | Redis plan state store, `QuizRun`, `AssessmentItem`, concept graph snapshots | Plan diagnostic owns start/answer lifecycle until Pack 1 snapshot wraps it |
| Concept graph | `ConceptGraphSnapshot`, `LearningConcept`, relations | Korteks/plan diagnostic builds; plan/quiz/wiki/tutor consume |
| Attempts and mastery | `QuizAttempt`, `QuizRun`, `KnowledgeTracingState`, `ConceptMastery`, `SkillMastery`, `ReviewItem` | Assessment pipeline writes; memory/planner/tutor consume |
| Tutor active memory | `TutorTurnState`, `TutorWorkingMemorySnapshot`, `TutorActionTrace`, `TutorToolCall`, `TutorPedagogyEvaluationRun` | Tutor services write; frontend renders safe metadata |
| Source/RAG/Wiki | `LearningSource`, chunks/pages, retrieval runs, citation checks, `WikiPage`, `WikiBlock`, quality reports | Source/Wiki services write; citation guard controls public answer confidence |
| Tool runtime | `TutorToolCall`, `ToolTelemetryEvent`, `CostRecord`, provider telemetry | Currently split between TutorToolOrchestrator and SK plugin filter; Pack 2 converges |
| Central exams | Exam framework, question bank, attempts, deneme, curriculum/content ops/quality analytics | Domain module; never forks memory/planner/tutor/wiki architecture |

## Current Flow Maps

### Chat / Tutor Learning Turn

```mermaid
flowchart TD
  U["Student message"] --> C["Chat/AgentOrchestratorService"]
  C --> S["Session state route"]
  S -->|"quiz/plan state"| QH["Synchronous quiz/plan handlers"]
  S -->|"normal tutor turn"| T["TutorAgent"]
  T --> CTX["Parallel legacy context fetches"]
  CTX --> TS["TutorTurnStateAssembler"]
  TS --> WM["TutorWorkingMemoryService"]
  TS --> AP["TutorActionPlanner + TutorPolicyEngine"]
  AP --> TO["TutorToolOrchestrator"]
  TO --> TTC["TutorToolCall rows + Redis tutor events"]
  AP --> ART["TeachingArtifactService"]
  T --> LLM["Tutor model completion"]
  LLM --> EVAL["TutorPedagogyEvaluation + QualityGate"]
  EVAL -->|"repair if needed"| LLM
  LLM --> RESP["Chat response + metadata post-processing"]
  RESP --> FE["ChatMessage metadata chips / AgentStatusRail / ArtifactCanvas"]
```

Current note: Tutor has a strong internal turn/action/pedagogy path, but it still starts from multiple partial context fetches rather than one `ActiveLessonSnapshot`.

### Plan Diagnostic Flow

```mermaid
flowchart TD
  START["Frontend plan request"] --> INTENT["StudyIntentAnalyzer"]
  INTENT -->|"student confirms"| PD["PlanDiagnosticService.Start"]
  PD --> KR["Build direct learning research / Korteks plugins"]
  KR --> COMP["PlanResearchCompressor"]
  COMP --> CG["ConceptGraphBuilder"]
  CG --> AG["AssessmentGrammarEngine"]
  AG --> AQ["AssessmentQualityService"]
  AQ --> QLLM["Generate diagnostic quiz JSON"]
  QLLM --> QR["QuizRun + AssessmentItems"]
  QR --> FE["Frontend quiz"]
  FE --> ANS["PlanDiagnosticService.RecordAnswer"]
  ANS --> REC["QuizAttemptRecorder"]
  REC --> PLAN["DeepPlanAgent from diagnostic"]
```

Current note: plan generation has strong structural floors and concept graph inputs. Semantic plan quality and curriculum sequencing are still not one central gate.

### Korteks Research To Synthesis

```mermaid
flowchart TD
  K["KorteksAgent"] --> SK["Semantic Kernel auto/function invocation"]
  SK --> PLUG["Web/Wikipedia/Academic/YouTube/Sources plugins"]
  PLUG --> KTC["KorteksToolCaptureFilter"]
  KTC --> EV["ToolCallEvidence + SourceEvidence"]
  EV --> RES["KorteksResearchResult"]
  RES --> PRC["PlanResearchCompressor"]
  PRC --> PIB["PlanIntelligenceBriefBuilder"]
  PIB --> PLAN["Plan prompt"]
  PIB --> QUIZ["Assessment/quiz prompt"]
```

Current note: Korteks has research capture, but SK tool calls are not yet governed by the same durable tool ledger as Tutor tools.

### Quiz Generation And Attempt Recording

```mermaid
flowchart TD
  CG["Concept graph"] --> GRAM["AssessmentGrammarEngine"]
  GRAM --> SPECS["Assessment item specs"]
  SPECS --> LLMQ["LLM diagnostic quiz"]
  LLMQ --> GATE["DiagnosticQuizQualityGate"]
  GATE --> META["Attach assessment metadata"]
  META --> STUDENT["Student answers"]
  STUDENT --> REC["QuizAttemptRecorder"]
  REC --> ATT["QuizAttempt + QuizRun"]
  REC --> KT["KnowledgeTracingService"]
  REC --> CM["ConceptMastery"]
  REC --> LS["LearningSignal"]
  REC --> REV["Review/SRS"]
```

Current note: attempt recording is mature. The missing professional contract is a single assessment orchestrator that all quiz/practice/deneme flows must use.

### Wrong Answer To Remediation

```mermaid
flowchart TD
  WRONG["Wrong or skipped answer"] --> MC["MistakeClassifierService"]
  MC --> MI["MisconceptionIntelligenceEvaluator"]
  MI --> KT["KnowledgeTracingState / ConceptMastery"]
  MI --> SIG["LearningSignal + remediation seed"]
  SIG --> MEM["LearningMemoryService"]
  MEM --> TUTOR["TutorTurnState / Tutor mode"]
  MEM --> WIKI["Wiki evidence weak concepts"]
  MEM --> REVIEW["ReviewItem / SRS"]
```

Current note: the path exists, but Pack 6 must prove every assessment type feeds the same remediation semantics.

### Source / RAG / Wiki Evidence

```mermaid
flowchart TD
  SRC["Learning sources"] --> CH["Chunks / retrieval"]
  CH --> RET["RetrieveTopicEvidence"]
  WIKI["Wiki pages/blocks"] --> WEB["WikiEvidenceService"]
  RET --> WEB
  CG["ConceptGraphSnapshot"] --> WEB
  KT["Mastery / tracing"] --> WEB
  SIG["Learning signals"] --> WEB
  WEB --> BUNDLE["WikiEvidenceBundle"]
  BUNDLE --> GUARD["WikiCitationGuard / answer policy"]
  GUARD --> WA["WikiLearningAssistant / source answer"]
  BUNDLE --> TUTOR["Tutor source context"]
```

Current note: there are good pieces, but the lifecycle from source upload to concept graph to notebook to deletion/invalidation-safe Tutor answer is not a single contract yet.

### Tool Execution Planes

```mermaid
flowchart TD
  subgraph "Tutor-governed tool plane"
    AP["TutorActionPlanner"] --> TO["TutorToolOrchestrator"]
    TO --> ALLOW["Allowlist + deterministic ExecuteAsync"]
    ALLOW --> ROW["TutorToolCall durable rows"]
    ROW --> STREAM["Redis tutor tool events"]
    ROW --> META["Chat metadata/tool statuses"]
  end

  subgraph "Semantic Kernel plugin plane"
    KERNEL["Kernel in Program.cs"] --> SKP["SK plugins"]
    SKP --> PF["PluginTelemetryFilter"]
    PF --> TTE["ToolTelemetryEvent"]
    KORT["KorteksToolCaptureFilter"] --> KEV["Korteks evidence lists"]
  end

  ROW -. "target convergence" .-> TTE
  PF -. "current gap: user/session/topic/correlation often null" .-> TTE
```

Current note: this is the largest architectural split. Pack 2 must converge it before tool-heavy product promises.

### Pack 2 Tool Runtime Governance Map

Pack 2 adds a bounded `UnifiedToolRuntime` layer. Semantic Kernel remains an execution adapter/plugin host, but tool policy now has a student-facing runtime contract that can record decisions, denied/degraded fallbacks, evidence mode, snapshot ids, Tutor turn ids, and safe result summaries.

| Tool / plane | Current owner plane | Runtime category | Can ground factual claims? | Durable trace | Target convergence |
|---|---|---|---:|---|---|
| `source_search` / `sources_query` | Tutor orchestrator + SK `SourcesQueryPlugin` | `source_grounding_tool` | Yes, only with source/citation evidence | `TutorToolCall`, `ToolRuntimeTrace`, telemetry | Keep Tutor-owned; SK is adapter-only |
| `wiki_search` / Wiki plugins | Tutor orchestrator + SK `WikiPlugin` | `wiki_notebook_tool` | Yes, only with Wiki/source evidence | `TutorToolCall`, `ToolRuntimeTrace`, telemetry | Route teaching use through Tutor/runtime |
| `ide_last_result` / `ide_execution` | Tutor context + explicit `/api/code/*` user action + disabled SK auto-run | `code_execution_tool` | No | `TutorToolCall`, `ToolRuntimeTrace`, IDE learning signals | Keep explicit user execution; Tutor consumes safe summary |
| `review_query`, `flashcard_query` | Tutor orchestrator + SK plugins | `tutor_learning_tool` | No | `TutorToolCall`, `ToolRuntimeTrace` | Use as learning memory evidence, not factual grounding |
| `wolfram_alpha` | Tutor orchestrator + provider plugin | `real_world_reference_tool` | Yes for computed reference when provider evidence exists | `ToolRuntimeTrace`, telemetry | Provider-gated, bounded fallback |
| `news`, `weather`, `crypto` | Tutor orchestrator + provider plugins | `real_world_reference_tool` | No curriculum truth; external reference only | `ToolRuntimeTrace`, telemetry | Label as current/external reference |
| `youtube_pedagogy`, `visual_generation` | SK/provider plugin plane | `media_reference_tool` | No by default | telemetry; runtime when Tutor-planned | Pedagogy/artifact aid, not factual source of truth |
| `knowledge_entity`, `geo_context`, `science_context`, `research_context`, `forum_signal` | Tutor real-world evidence service | `source_grounding_tool` / `media_reference_tool` | External evidence only; forum is misconception signal | `TeachingEvidenceItem`, `TutorToolCall`, `ToolRuntimeTrace` | Keep evidence cards bounded and cited |
| Remaining SK auto-invoked plugins | Semantic Kernel plugin plane | `deprecated_or_legacy_tool` until mapped | No by default | bounded `ToolTelemetryEvent` only | Pack 2 documents the gap; later packs may migrate per tool |

Runtime rules:

- Capability visibility and runtime permission are separate. `ToolCapabilityService` says whether a tool exists/visible; `UnifiedToolRuntimeService` decides whether it may run in the current learning context.
- Tutor must have a pedagogical purpose before executing learning tools.
- YouTube/media tools are reference or pedagogy aids, not factual grounding unless verified evidence exists.
- Current/news/market/weather tools are external references and must not become curriculum truth.
- Public DTOs expose safe summaries, evidence labels, citation URLs, statuses, ids, and fallback reasons only.
- Public DTOs must not expose raw provider payloads, secrets, hidden prompts, raw plugin arguments, raw model responses, local paths, or stack traces.
- Semantic Kernel plugin filter telemetry remains bounded. It records plugin/function/status/latency and explicitly does not store raw arguments or results.

### Pack 3 Korteks Synthesis Contract Map

Pack 3 adds `KorteksResearchWorkflow` as the durable boundary between raw Korteks research and downstream learning consumers. Korteks still researches through the existing Semantic Kernel plugin plane, but the output is normalized by `KorteksSynthesisService` before plan, quiz, Tutor, or Wiki surfaces consume it.

| Artifact | Owner | Consumers | Safety rule |
|---|---|---|---|
| `KorteksResearchResultDto` | `KorteksAgent` | synthesis service only, legacy formatter fallback | May contain raw report excerpt; not the canonical public learning contract |
| `CompressedPlanResearchContextDto` | `PlanResearchCompressor` | concept graph, plan brief, quiz brief | Bounded research hints only; does not override adaptive/diagnostic context |
| `KorteksResearchWorkflow` | `KorteksSynthesisService` | Plan diagnostic, Tutor research route, frontend contract, snapshots | User-scoped, durable, source-aware, no raw provider payload |
| `KorteksConsumerContextsDto.Plan` | `KorteksSynthesisService` | `PlanDiagnosticService`, `DeepPlanAgent` prompt bridge | Advisory research support; concept graph and diagnostic profile have priority |
| `KorteksConsumerContextsDto.Quiz` | `KorteksSynthesisService` | diagnostic quiz scope and future quiz engines | Scope only; no source names, URLs, Orka product labels, or answer leakage |
| `KorteksConsumerContextsDto.Tutor` | `KorteksSynthesisService` | research route Tutor prompt | Tutor may cite only accepted URL-backed evidence and must warn on fallback |
| `KorteksConsumerContextsDto.Wiki` | `KorteksSynthesisService` | future Wiki notebook lifecycle | Notebook seed only; not auto-generated final Wiki content |

Current flow:

```mermaid
flowchart LR
  KR["KorteksAgent research"] --> KE["Captured source/tool evidence"]
  KR --> CR["PlanResearchCompressor"]
  CR --> KS["KorteksSynthesisService"]
  KS --> KW["KorteksResearchWorkflow SQL"]
  KW --> PC["Plan consumer context"]
  KW --> QC["Quiz consumer context"]
  KW --> TC["Tutor consumer context"]
  KW --> WC["Wiki notebook seed context"]
  PC --> PD["PlanDiagnosticService"]
  TC --> OR["AgentOrchestrator research route"]
  KW --> ALS["ActiveLessonSnapshot evidence counts"]
```

Remaining gap: streaming Korteks research still returns the legacy SSE stream and does not persist a synthesis workflow until a structured/sync or internal research route is used. This is intentional for Pack 3 compatibility; a later workspace synchronization pack can attach stream completion to the same synthesis contract.

### Frontend Metadata / Render Flow

```mermaid
flowchart TD
  API["Chat stream / response"] --> CP["ChatPanel"]
  CP --> MSG["ChatMessage"]
  MSG --> CHIPS["ChatMetadataChips"]
  MSG --> TRACE["ChatLearningTrace / LiveTutorTrace"]
  MSG --> MD["Markdown + Mermaid + citation rendering"]
  CP --> RAIL["AgentStatusRail"]
  CP --> CANVAS["ArtifactCanvas"]
  WIKI["WikiMainPanel"] --> WTRACE["WikiLearningTraceSummary"]
```

Current note: frontend already renders metadata as first-class state in several places. Pack 9 should make this consistent across plan, quiz, wiki, artifacts, and tool status instead of relying on chat prose.

## Architecture Gap Register

| Gap | Current evidence | Risk | Target pack | Must fix before professional launch? | Can defer? |
|---|---|---|---|---|---|
| Context fragmentation | Tutor gathers conversation, wiki, notebook, learning signals, IDE, YouTube, review pressure separately | Different services can teach from different learner state | Pack 1 | Yes | No |
| Tool runtime split | `TutorToolOrchestrator` has durable user-scoped rows; SK plugin telemetry has null user/session/topic/correlation | Tool use cannot be audited uniformly; unsafe auto-tool expansion risk | Pack 2 | Yes | No |
| Korteks synthesis looseness | Korteks evidence compresses into plan/quiz prompts but synthesis is not a formal reusable artifact | Plan/quiz/wiki can interpret research differently | Pack 3 | Yes | No |
| RAG/Wiki lifecycle gaps | Evidence bundle exists; source upload -> graph -> notebook -> deletion-safe answer is not one lifecycle | Citation drift, stale notebook, unsafe source confidence | Pack 4 | Yes | No |
| Plan quality gaps | DeepPlan has structural minimums; semantic topic/curriculum coverage scorer is not centralized | Generic or overbroad plans can pass structure | Pack 5 | Yes | No |
| Quiz/assessment quality gaps | Assessment grammar is strong; final generated question quality and all assessment flows are not unified | Leaky, off-topic, or non-diagnostic items can slip | Pack 6 | Yes | No |
| Tutor pedagogy gaps | Rubric and repair exist; not all answers are forced through a single snapshot/tool/evidence contract | Tutor can be good but not fully deterministic/provable | Pack 7 | Yes | No |
| Artifact/media learning gaps | Mermaid/image/YouTube/wiki artifacts exist across content and metadata | Visual/video/diagram outputs can be inconsistent or prose-driven | Pack 8 | No for core, yes for polished launch | Partial |
| Frontend synchronization gaps | Chat metadata, AgentStatusRail, Wiki traces exist but are not one workspace contract | Backend intelligence may look like scattered chatbot extras | Pack 9 | Yes for product launch | No |
| Observability gaps | Custom telemetry and cost rows exist, but not full flow/span lineage | Hard to debug live quality, cost, tool drift | Pack 10 | Yes before scale | Partial for early local |
| Agentic security gaps | Pack 11 adds deterministic `AgenticTrust` checks across user/source/tool/tutor/memory/citation/public payload surfaces | Remaining risk is final-audit coverage across the full integrated flow | Final audit | Yes before public scale | No |
| Documentation drift | `ORKA_MASTER_GUIDE` and older audit docs contain historical claims and old labels | Developers may implement from stale architecture | Pack 0 | Yes | No |

## Locked Professionalization Roadmap

The implementation pack count is fixed unless explicitly changed by the user later. The final audit is not an implementation pack.

### Pack 1 - ActiveLessonSnapshot & StudentContext Contract

- Goal: create one lesson/student context contract used by Tutor, plan, quiz, Wiki, and tools.
- Why: current state is powerful but fragmented across context fetches and caches.
- Main services: `TutorTurnStateAssembler`, `TutorWorkingMemoryService`, `LearningMemoryService`, `KnowledgeTracingService`, `ConceptMasteryService`, `PlanDiagnosticService`, `WikiEvidenceService`.
- Out of scope: new Tutor behavior, new tools, frontend redesign.
- Required tests: snapshot user scope, stale snapshot rebuild, source deletion invalidation, no raw prompt/source payload in DTOs.
- Exit criteria: every core learning turn can name the active snapshot id and its evidence basis.

### Pack 2 - Unified Tool Runtime & Kernel Governance

- Goal: converge Tutor tools and Semantic Kernel plugin tools under one governed runtime.
- Why: SK plugin calls and Tutor tool calls currently have different audit semantics.
- Main services: `TutorToolOrchestrator`, `ToolCapabilityService`, `RuntimeTelemetryService`, `PluginTelemetryFilter`, `KorteksToolCaptureFilter`, SK plugins.
- Out of scope: adding new external providers.
- Required tests: no high-risk auto-invoke, all tool calls have user/session/topic/correlation when available, fallback states are safe.
- Exit criteria: every tool result has capability, ledger, telemetry, safety status, and consumption proof.

### Pack 3 - Korteks Research Workflow & Synthesis Contract

- Goal: formalize research -> synthesis outputs for plan, quiz, Tutor, and Wiki.
- Why: Korteks should research; synthesis should decide educational use.
- Main services: `KorteksAgent`, `PlanResearchCompressor`, `PlanIntelligenceBriefBuilder`, `ConceptGraphBuilder`, `PlanDiagnosticService`.
- Out of scope: new web scraping, new provider calls, content generation.
- Required tests: approved intent only, grounded/degraded modes, source evidence boundaries, synthesis schema stability.
- Exit criteria: plan/quiz/wiki/tutor consume a structured synthesis artifact, not loose research prose.

### Pack 4 - RAG / Source / Wiki Knowledge Lifecycle

- Goal: stabilize source upload/query/wiki notebook/citation lifecycle.
- Why: source-grounded learning must survive deletion, low confidence, and stale retrieval.
- Main services: `LearningSourceService`, `WikiEvidenceService`, `WikiLearningAssistant`, `WikiCitationGuard`, `WikiArtifactService`, `RagEvaluationService`, `SourceQualityServices`.
- Out of scope: PDF/OCR/scraping expansion, new cloud storage.
- Required tests: citation coverage, deleted source removal, source quality degraded states, per-topic notebook organization.
- Exit criteria: Tutor/Wiki can say exactly whether an answer is source-backed, wiki-backed, or model fallback.

### Pack 5 - Plan Quality & Curriculum Sequencing

- Goal: make plans topic-specific, concept-aware, prerequisite-aware, and remediation-aware.
- Why: structural module counts do not guarantee professional plans.
- Main services: `DeepPlanAgent`, `AdaptiveStudyPlannerService`, `LearningMemoryService`, `ConceptGraphBuilder`, curriculum services where relevant.
- Out of scope: Central Exams content population.
- Required tests: generic plan rejection, prerequisite order, weak-area emphasis, curriculum/source evidence where available.
- Exit criteria: low-quality or generic plans are revised or blocked before becoming the student's path.

### Pack 6 - Quiz / Assessment Quality & Misconception Engine

- Goal: unify assessment generation, answer recording, misconception detection, and remediation handoff.
- Why: quiz must measure concepts and drive learning, not just produce questions.
- Main services: `AssessmentGrammarEngine`, `AssessmentQualityService`, `DiagnosticQuizQualityGate`, `QuizAttemptRecorder`, `MistakeClassifierService`, `KnowledgeTracingService`.
- Out of scope: official scoring, percentile, full psychometric IRT.
- Required tests: answer leak rejection, final item quality, all assessment flows record comparable results, wrong answer changes remediation state.
- Exit criteria: any quiz/practice/deneme answer produces the same safe learning signal semantics.
- Release cleanup addendum: blank/skipped answers are treated as prerequisite/guided-repair signals, not as high-confidence misconceptions. They may drive repair notes and Tutor next actions, but they must not expose answer keys or claim mastery/source certainty.

### Pack 7 - Tutor Pedagogy & Response Policy Closure

- Goal: make Tutor the explicit pedagogical owner for every learning answer.
- Why: Tutor must use learner state, tool results, source evidence, and response policy consistently.
- Main services: `TutorAgent`, `TutorPolicyEngine`, `TutorActionPlanner`, `TutorPedagogyEvaluationService`, `TutorPedagogyQualityGate`, `TutorReflectionService`.
- Out of scope: new domain modules or broad UI redesign.
- Required tests: hint-first behavior, remediation language, source discipline, tool-result consumption, micro-checks, golden scenarios.
- Exit criteria: Tutor answers fail closed or repair when pedagogy/source/tool policy is violated.
- Implementation status: `ITutorResponsePolicyService` is the bounded Pack 7 convergence layer. It reads `TutorTurnState`, action trace, latest quiz attempt, source evidence bundle, tool calls, plan metadata, and snapshot ids, then emits safe teaching move, grounding, remediation, tool policy, next actions, and response-quality warnings. Public metadata/endpoints expose only labels and bounded summaries; raw source chunks, hidden prompts, provider payloads, raw tool output, and answer keys remain outside the contract.

### Pack 8 - Learning Artifacts Engine

- Goal: put diagrams, images, YouTube references, tables, formulas, code outputs, and wiki notes under one artifact lifecycle.
- Why: learning artifacts should be planned, accessible, safe, and concept-linked.
- Main services: `TeachingArtifactService`, `WikiArtifactService`, `TutorActionPlanner`, visual/YouTube/tool providers, frontend artifact rendering.
- Out of scope: AI image production scale, copyrighted media ingestion.
- Required tests: artifact source basis, alt/fallback, no provider raw payload, YouTube pedagogy-only rule.
- Exit criteria: artifacts are generated/rendered because they serve a learning objective, not because the model improvised them.

### Pack 9 - Frontend Learning Workspace Synchronization

- Goal: make plan, quiz, Tutor, Wiki, source evidence, tools, and artifacts feel like one workspace.
- Why: backend intelligence must be visible and usable, not hidden in prose.
- Main surfaces: `ChatPanel`, `ChatMessage`, `AgenticWorkspace`, `WikiMainPanel`, `ToolCapabilityStrip`, smoke scripts.
- Out of scope: full redesign, corporate baseline.
- Required tests: metadata-first UI, no answer-key leak, degraded states, mobile-safe core flow, safe copy.
- Exit criteria: a student can follow what Orka is doing, why, with which evidence, and what to do next.

### Pack 10 - Observability, Cost & Runtime Telemetry

- Goal: trace the whole learning flow from intent to answer and tool/cost outcomes.
- Why: professional systems need live diagnosis of quality, latency, fallback, and cost.
- Main services: `RuntimeTelemetryService`, provider telemetry, cost records, correlation context, tool runtime, chat metadata.
- Current implementation: `LearningRuntimeTelemetryService` normalizes existing `ToolTelemetryEvents`, `ToolRuntimeTraces`, and `CostRecords` into safe user-scoped runtime traces, correlation summaries, health summaries, topic flow summaries, and privacy checks. `ProductionReadinessService` includes the runtime telemetry section. Frontend consumes `LearningRuntimeAPI` through the learning workspace state and shows a compact runtime health strip.
- Storage rule: Pack 10 reuses existing telemetry/cost/tool trace tables; no new raw payload table or external APM dependency is introduced.
- Out of scope: production SLO promises and external APM deployment.
- Required tests: span/record linkage, correlation propagation, no raw payload leaks, deterministic quick gates remain external-network-free.
- Exit criteria: a degraded Tutor answer can be traced back through intent, research, sources, tools, model/provider, and quality gate.

### Pack 11 - Agentic Security & Trust Hardening

- Goal: close prompt injection, source poisoning, tool misuse, memory poisoning, and cross-user leakage fixtures.
- Why: Orka has multiple agents, tools, sources, and memories; trust boundaries must be explicit.
- Main services: source/RAG, tool runtime, Tutor, Korteks, memory, frontend metadata, regression guards.
- Current implementation: `IAgenticTrustPolicyService` is the bounded deterministic trust layer. It checks user messages, source/wiki-like content, tool requests, Tutor responses, memory write candidates, citation sets, and public payloads without new provider calls. It reuses `UnifiedToolRuntimeService` for capability/policy decisions, `SourceEvidenceLifecycleService` for citation trust, `TutorResponsePolicyService` for answer-key/source/claim checks, and `TelemetryPrivacyGuard` for public payload leak checks.
- Runtime audit: trust checks write safe `agentic_trust` events through `LearningRuntimeTelemetryService`; no raw prompt/source/tool/provider payload table is introduced.
- Frontend contract: `AgenticTrustAPI` and DTOs expose only issue category, severity, safe label/remediation, status, and timestamps. No raw malicious text or hidden/debug payload is part of the public contract.
- Out of scope: enterprise SOC/SIEM buildout.
- Required tests: malicious source text, hidden tool instruction, fake citation, memory poisoning, cross-user private data, excessive agency.
- Exit criteria: known agentic threat patterns are blocked, degraded, or surfaced safely.

### Final Audit + Closure

- Goal: verify Packs 1-11 together and close the phase.
- Not an implementation pack.
- Required proof: targeted backend/unit/frontend smoke, quick gates, docs, no unrelated work, no stage/commit unless requested.

## Guardrails

- Tutor is the pedagogical owner.
- Korteks researches; Tutor teaches.
- Synthesis converts research into plan/quiz/wiki/tutor-safe educational inputs.
- YouTube is pedagogy/reference, not factual grounding unless verified transcript/source evidence exists.
- Tools must be governed, traceable, capability-checked, and safe on fallback.
- No hidden provider/debug payload in public DTOs.
- Public student-facing API responses must use safe DTOs/projections; entities with `UserId`, owner ids, raw state JSON, prompt/tool/provider payloads, raw source chunks, or raw evidence payload hashes must not be returned directly.
- Quiz measures concepts and misconceptions, not product labels, UI terms, or internal Orka plumbing.
- Plans must be topic-specific, concept-aware, prerequisite-aware, and remediation-aware.
- RAG answers must distinguish sourced, wiki-backed, degraded, and model-fallback claims.
- Wiki notes must be source/evidence-aware and deletion-safe.
- Central Exams must reuse Orka architecture and must not fork memory, planner, tutor, or wiki logic.
- No teacher/classroom/dershane workflow unless explicitly planned later. Existing audio/classroom-style UX remains a personal learning mode, not an institutional product.

## Learning OS Feature Completion Addendum

### Phase 1 - Long-Term Adaptive Learning Engine

- Goal: turn existing durable learning evidence into a long-term study rhythm, not just per-turn Tutor memory.
- Main service: `ILongTermAdaptiveLearningService` / `LongTermAdaptiveLearningService`.
- Inputs: `KnowledgeTracingState`, `ConceptMastery`, `QuizAttempt`, `ReviewItem`, `LearningSignal`, Wiki repair/misconception blocks, source evidence bundles, and dashboard source health.
- Outputs: `LongTermLearningProfileDto`, `LongTermLearningConceptDto`, `AdaptiveReviewPressureDto`, `AdaptiveLearningRhythmDto`, and `AdaptiveNextStudyActionDto`.
- Student-facing semantics:
  - concept states: `new`, `learning`, `weak`, `repaired`, `stable`, `due_for_review`, `likely_forgotten`.
  - review priority: `none`, `low`, `medium`, `high`, `urgent`.
  - reason codes include `recent_wrong_answer`, `repeated_blank`, `prerequisite_gap`, `due_srs`, `likely_forgotten`, `weak_concept`, `repair_pending`, `source_evidence_limited`, and `stable_recent_success`.
  - recommended actions include `review`, `repair`, `checkpoint`, `continue_plan`, `source_review`, `create_flashcards`, and `take_quiz`.
- Integration points:
  - `LearningController` exposes the adaptive profile for all learning evidence or a user-owned topic scope.
  - `DashboardController` includes the long-term profile in `/api/dashboard/today`.
  - `TutorResponsePolicyService` folds long-term next actions into Tutor next-action metadata.
- Safety:
  - deterministic/provider-free by default;
  - no raw transcripts, prompts, provider payloads, source chunks, tool payloads, local paths, owner/user ids, or answer keys in public profile DTOs;
  - blank/skipped answers create prerequisite/guided-review pressure, not high-confidence misconception claims;
  - source review warnings do not turn provider output or Wiki memory into citation evidence.
- Out of scope: new provider calls, OpenAI API migration, medical/psychological diagnosis, official curriculum claims, exam success guarantees, mobile app, teacher/classroom/dershane workflows, and frontend redesign.

### Phase 2 - Exam & Curriculum Depth Pack

- Goal: make exam prep a first-class Learning OS layer instead of an isolated Central Exams shell.
- Main service: `IExamLearningProfileService` / `ExamLearningProfileService`.
- Inputs: `ExamDefinition` tree, `QuestionItem` / `QuestionOutcomeLink`, practice attempts, deneme attempts, curriculum outcome mappings, and source registry verification metadata.
- Outputs: `ExamLearningProfileDto`, `ExamOutcomeReadinessDto`, `ExamPracticeReadinessDto`, and `ExamNextActionDto`.
- Student-facing semantics:
  - outcome readiness can be `diagnostic_needed`, `watch`, `weak`, `prerequisite_gap`, `due_for_review`, `stable`, or `coverage_limited`;
  - next actions include `run_diagnostic`, `repair_outcome`, `review_due_outcome`, `practice_question_type`, `review_deneme_mistakes`, `source_review`, and `continue_exam_plan`;
  - reason codes include `weak_outcome`, `due_review`, `repeated_wrong`, `repeated_blank`, `prerequisite_gap`, `question_type_gap`, `deneme_mistake_cluster`, `coverage_limited`, `question_coverage_limited`, `source_unverified`, `source_evidence_limited`, and `stable_recent_success`.
- Integration points:
  - `CentralExamsController` exposes the exam learning profile.
  - `DashboardController` includes the exam profile in `/api/dashboard/today`.
  - `TutorResponsePolicyService` folds exam next actions into Tutor next-action metadata.
- Safety:
  - deterministic/provider-free by default;
  - no scraped official content and no official alignment claim without verified source metadata;
  - no exam success, score, percentile, or placement guarantee;
  - no pre-submit answer key, raw source chunk, prompt, provider/tool/debug payload, local path, owner/user id, or stack trace in public DTOs.
- Out of scope: new provider calls, OpenAI API migration, frontend redesign, mobile app, teacher/classroom/dershane workflows, payment/subscription, and broad curriculum data population.

### Phase 3 - Source/Wiki Intelligence Deepening

- Goal: make uploaded sources, Wiki pages, citations, source Q&A memory, source compare, Tutor, Dashboard, Notebook Studio, long-term learning, and exam profile share one honest evidence state.
- Main service: `ISourceWikiIntelligenceService` / `SourceWikiIntelligenceService`.
- Inputs: `LearningSource`, `SourceEvidenceBundle`, source lifecycle summaries, source-to-concept links, source Q&A study summaries, citation review, `WikiPage`, `WikiBlock`, and Wiki curation summaries.
- Outputs: `SourceWikiIntelligenceProfileDto`, `SourceWikiEvidenceReadinessDto`, `WikiLearningPageReadinessDto`, and `SourceWikiNextActionDto`.
- Student-facing semantics:
  - source readiness and evidence status stay separate from Wiki notes and Tutor-generated explanations;
  - Wiki page health can surface repair-pending, source-limited, stale, duplicate-trace, degraded, or clean states;
  - next actions include `review_source`, `citation_review`, `repair_concept`, `review_source_questions`, `compare_sources`, `sync_source_concepts`, and `open_notebook_pack`;
  - reason codes include `source_evidence_limited`, `citation_review_needed`, `wiki_repair_pending`, `wiki_source_limited`, `source_question_review_needed`, and `source_concept_links_limited`.
- Integration points:
  - `SourcesController` exposes `/api/sources/wiki-intelligence`.
  - `DashboardController` includes the source/wiki profile in `/api/dashboard/today`.
  - `TutorResponsePolicyService` folds source/wiki actions and warnings into Tutor next-action metadata.
  - Existing Notebook Studio, source Q&A, source compare, Wiki curation, Wiki Copilot, long-term learning, and exam profile services remain deterministic/provider-free and consume only safe evidence/status metadata.
- Safety:
  - deterministic/provider-free by default;
  - raw source chunks, raw Wiki block bodies, prompts, provider payloads, tool payloads, debug traces, local paths, owner/user ids, stack traces, and answer keys are not returned;
  - provider output and Wiki memory are not citation evidence;
  - source-grounded, official, curriculum-aligned, or success claims require verified evidence metadata and remain blocked when evidence is stale, deleted, insufficient, degraded, or citation review needs attention.
- Out of scope: new provider calls, OpenAI API migration, source scraping, frontend redesign, mobile app, teacher/classroom/dershane workflows, payment/subscription, full PPTX/video/Realtime, and graph canvas work.

### Phase 4 - Student Simulation & Evaluation Harness

- Goal: prove the completed Learning OS features behave as one coherent backend across realistic learner journeys.
- Main proof surface: `StudentSimulationEvaluationTests` and its test-only deterministic harness. It is not a production endpoint.
- Inputs seeded by the harness: authenticated user/topic, quiz attempts, repeated wrong answers, blank/skipped answers, stable correct evidence, due SRS review, exam question/deneme evidence, source evidence, stale source state, and Wiki repair/source-limited state.
- Evaluated outputs:
  - `LongTermLearningProfileDto`;
  - `ExamLearningProfileDto`;
  - `SourceWikiIntelligenceProfileDto`;
  - Tutor next actions and Tutor response policy;
  - dashboard today profile metadata;
  - Wiki/source safety and cross-user access behavior.
- Scenario pack:
  - new learner with thin evidence;
  - repeated wrong learner;
  - blank/skipped learner;
  - improving learner;
  - forgotten/due-review learner;
  - exam prep learner;
  - source/wiki learner;
  - mixed Learning OS journey where Tutor, dashboard, long-term, exam, and source/wiki profiles must not contradict each other dangerously.
- Evaluation contract:
  - deterministic pass/fail scorecard with safe reason codes and user-safe summaries;
  - no AI judge and no provider call;
  - no raw transcript, prompt, provider payload, source chunk, tool/debug payload, local path, owner/user id, stack trace, answer key, official claim, source-grounded overclaim, or success guarantee in serialized public simulation payloads.
- Closure decision: Learning OS Feature Completion 1-4 may be closed when this harness and the existing backend lifetest/security/regression/full-test gates pass.

## Orka Product Coherence Addendum

### Phase 1 - Orka OS Binding Layer / Unified Learning State

- Goal: make Tutor, Dashboard, Exam, Source/Wiki, Review, Quiz, Memory, Notebook Studio, and personal Study Room/Classroom consume one safe learner-state contract instead of parallel next-action fragments.
- Main service: `IOrkaLearningStateService` / `OrkaLearningStateService`.
- Inputs: `LongTermLearningProfileDto`, `ExamLearningProfileDto`, `SourceWikiIntelligenceProfileDto`, quiz attempts, concept mastery, knowledge tracing, SRS/review items, learning signals, source lifecycle state, Wiki page health, and personal Study Room/Classroom session state.
- Outputs: `OrkaLearningStateDto`, `OrkaUnifiedNextActionDto`, `OrkaLearningSignalSummaryDto`, `OrkaFeatureReadinessDto`, and `OrkaLearningStateConflictDto`.
- Student-facing semantics:
  - one primary unified next action can be selected from diagnostic, plan continuation, repair, prerequisite repair, review, exam practice, deneme mistake review, source review, citation review, personal Study Room, checkpoint quiz, flashcards, and Wiki note update;
  - secondary actions preserve useful module-level handoffs without letting modules contradict each other silently;
  - conflict warnings include `next_action_conflict`, `source_grounding_blocked`, `exam_learning_conflict`, `stale_wiki_source`, `missing_topic_context`, and `thin_evidence`;
  - feature readiness summarizes whether long-term, exam, source/wiki, review, Study Room, Tutor, and Dashboard signals are ready, limited, blocked, or unavailable.
- Integration points:
  - `LearningController` exposes `/api/learning/orka-state` for the current user and optional user-owned topic/session scope.
  - `DashboardController` includes the unified state in `/api/dashboard/today` and uses the unified primary action for recommended entry point / next action.
  - `TutorResponsePolicyService` folds the unified next action and warnings into Tutor next-action metadata and blocks source-grounded overclaims when unified source state says evidence is insufficient.
  - `StudentSimulationEvaluationTests` and `OrkaLearningStateCoherenceTests` verify repeated-wrong, blank/skipped, source-insufficient, exam-weak-outcome, Study Room, dashboard/Tutor consistency, and public-payload safety behavior.
- Safety:
  - deterministic/provider-free by default;
  - no new provider call or AI judge;
  - no raw transcript, prompt, provider payload, source chunk, tool/debug payload, local path, owner/user id, stack trace, pre-submit answer key, official claim, source-grounded overclaim, or success guarantee in public unified-state DTOs;
  - Study Room/Classroom means personal AI study room only, not teacher/classroom/dershane management.
- Out of scope: frontend redesign, mobile app, payment/subscription, OpenAI Responses API, Agents SDK, Realtime voice, new AI/provider architecture, Google Cloud, and institutional classroom workflows.

### Phase 2 - Orka Home / Mission Control Backend

- Goal: turn the unified learning state into one student-facing "today cockpit" contract for the future Orka Home / Mission Control screen.
- Main service: `IOrkaMissionControlService` / `OrkaMissionControlService`.
- Inputs: `OrkaLearningStateDto`, long-term adaptive profile, exam learning profile, source/wiki intelligence profile, quiz/mastery/review signal summary, Wiki/source warnings, personal Study Room readiness, and Notebook Studio pack availability.
- Outputs: `OrkaMissionControlDto`, `OrkaTodayMissionDto`, `OrkaMissionActionDto`, `OrkaMissionSectionDto`, `OrkaMissionModuleCardDto`, and `OrkaMissionWarningDto`.
- Student-facing semantics:
  - one primary mission answers what the learner should do first today;
  - secondary actions preserve useful handoffs without turning them into hidden autonomous actions;
  - sections group work as `start_here`, `repair_today`, `review_due`, `exam_focus`, `source_wiki_attention`, `continue_learning`, `study_room`, `notebook_artifacts`, and `progress_snapshot`;
  - module cards summarize Tutor, Study Room, Review, Exam, Sources, Wiki, Notebook Studio, Quiz/Checkpoint, and Progress readiness;
  - load fields summarize review, repair, exam, and source/wiki pressure.
- Integration points:
  - `LearningController` exposes `/api/learning/mission-control` for the current user and optional user-owned topic/session scope.
  - `DashboardController` includes `MissionControl` in `/api/dashboard/today`.
  - `StudentSimulationEvaluationTests` includes Mission Control in serialized public payload sweeps and mixed-journey consistency checks.
  - `OrkaMissionControlTests` verifies new learner, repeated wrong, single wrong, due review, source-insufficient, exam weak-outcome, dashboard consistency, and cross-user behavior.
- Safety:
  - deterministic/provider-free by default;
  - no new provider call or AI judge;
  - no raw transcript, prompt, provider payload, source chunk, tool/debug payload, local path, owner/user id, stack trace, pre-submit answer key, official claim, source-grounded overclaim, or success guarantee in public Mission Control DTOs;
  - Study Room/Classroom means personal AI study room only, not teacher/classroom/dershane management.
- Out of scope: frontend redesign, mobile app, payment/subscription, OpenAI Responses API, Agents SDK, Realtime voice, new AI/provider architecture, Google Cloud, and institutional classroom workflows.

### Phase 3 - Study Rhythm Coach / Life-Study Coach Backend

- Goal: turn unified state plus Mission Control into a safe study-rhythm contract that answers pace, workload, focus mode, comeback plan, and handoff rhythm.
- Main service: `IOrkaStudyCoachService` / `OrkaStudyCoachService`.
- Inputs: `OrkaLearningStateDto`, `OrkaMissionControlDto`, long-term weekly rhythm, review due state, repair pressure, exam profile, source/wiki warnings, personal Study Room readiness, and recent activity timestamps from existing durable evidence.
- Outputs: `OrkaStudyCoachDto`, `OrkaStudyLoadDto`, `OrkaFocusPlanDto`, `OrkaComebackPlanDto`, `OrkaStudyCoachActionDto`, and `OrkaStudyCoachWarningDto`.
- Student-facing semantics:
  - Mission Control answers what to do first; Study Coach answers how hard, how long, and in what rhythm to do it;
  - rhythm statuses are bounded to `light`, `normal`, `focused`, `repair_heavy`, `review_heavy`, `exam_heavy`, `source_cleanup`, `comeback`, and `thin_evidence`;
  - focus modes are bounded to quick start, repair block, review sprint, exam block, source cleanup, Study Room lesson, or continue plan;
  - comeback planning is a small practical study ramp after inactivity, not a statement about the learner's wellbeing or psychology;
  - Study Room/Classroom remains personal AI study room context only, not teacher/classroom/dershane management.
- Integration:
  - `LearningController` exposes `/api/learning/study-coach` for the current user and optional user-owned topic/session scope.
  - `DashboardController` includes `StudyCoach` in `/api/dashboard/today`.
  - `StudentSimulationEvaluationTests` includes Study Coach in serialized public payload sweeps and mixed-journey consistency checks.
  - `OrkaStudyCoachTests` verifies new learner, one wrong, repeated wrong/blank, due review, source-insufficient, exam weak-outcome, comeback, dashboard consistency, and cross-user behavior.
- Safety:
  - deterministic/provider-free by default;
  - no new provider call or AI judge;
  - no Stripe/payment code;
  - no therapy, psychology, medical, wellbeing, ADHD, burnout, or diagnosis claim;
  - no raw transcript, prompt, provider payload, source chunk, tool/debug payload, local path, owner/user id, stack trace, pre-submit answer key, official claim, source-grounded overclaim, or success guarantee in public Study Coach DTOs.
- Out of scope: frontend redesign, mobile app, payment/subscription, OpenAI Responses API, Agents SDK, Realtime voice, new AI/provider architecture, Google Cloud, and institutional classroom workflows.

### Phase 4 - Exam War Room Backend

- Goal: turn the existing exam learning profile into a student-facing exam-prep command-center contract for the future Exam War Room UI.
- Main service: `IOrkaExamWarRoomService` / `OrkaExamWarRoomService`.
- Inputs: `ExamLearningProfileDto`, exam framework skeleton, unified Orka learning state, Mission Control, Study Coach, practice evidence, deneme evidence, question coverage, and curriculum/source verification warnings.
- Outputs: `OrkaExamWarRoomDto`, `ExamWarRoomSubjectDto`, `ExamWarRoomTopicDto`, `ExamWarRoomOutcomeDto`, `ExamWarRoomPracticePlanDto`, `ExamWarRoomDenemeInsightDto`, `ExamWarRoomActionDto`, and `ExamWarRoomWarningDto`.
- Student-facing semantics:
  - active exam and variant identify the current exam goal without claiming official alignment unless verified metadata permits it;
  - weak, due, and stable outcomes remain separate so repeated success can lower urgency without becoming a score or success guarantee;
  - deneme mistake clusters, repeated wrong answers, repeated blank/skipped answers, due review, weak question types, and thin coverage produce bounded exam actions;
  - today exam mission and weekly exam plan specialize exam prep while Mission Control still owns the whole-student "what first" decision;
  - Tutor repair handoffs and personal Study Room handoffs are suggestions only; Study Room is offered only when safe learning topic context exists.
- Integration:
  - `CentralExamsController` exposes `/api/central-exams/{examCode}/war-room`.
  - `DashboardController` includes compact `ExamWarRoom` in `/api/dashboard/today`.
  - `StudentSimulationEvaluationTests` includes Exam War Room in serialized public payload sweeps and mixed Learning OS consistency checks.
  - `OrkaExamWarRoomTests` verifies new learner, repeated blank, deneme cluster, stable success, source/curriculum warnings, Study Room context gating, dashboard integration, and cross-user evidence isolation.
- Safety:
  - deterministic/provider-free by default;
  - no new provider call, AI judge, scraping, migration, or official-content population;
  - no Stripe/payment code;
  - no official alignment, score, percentile, placement, or exam success guarantee claim;
  - no raw transcript, prompt, provider payload, source chunk, tool/debug payload, local path, owner/user id, stack trace, pre-submit answer key, or correct answer in public War Room DTOs.
- Out of scope: frontend redesign, mobile app, payment/subscription, OpenAI Responses API, Agents SDK, Realtime voice, new AI/provider architecture, Google Cloud, official scraping, and institutional classroom workflows.

### Phase 5 - Source / Wiki Pro Pack Backend

- Goal: turn source lifecycle, Wiki health, citation review, source-to-concept links, Notebook Studio readiness, and source/wiki handoffs into one student-facing evidence-workspace command-center contract.
- Main service: `IOrkaSourceWikiProService` / `OrkaSourceWikiProService`.
- Inputs: `SourceWikiIntelligenceProfileDto`, citation review, source-to-concept links, Wiki page/block curation signals, Notebook Studio pack rows, unified Orka learning state, Mission Control, Study Coach, and Exam War Room warnings.
- Outputs: `OrkaSourceWikiProDto`, `SourceWikiProSourceDto`, `SourceWikiProWikiPageDto`, `SourceWikiProCitationDto`, `SourceWikiProConceptLinkDto`, `SourceWikiProEvidenceMapDto`, `SourceWikiProActionDto`, and `SourceWikiProWarningDto`.
- Student-facing semantics:
  - Source / Wiki Pro specializes evidence workspace decisions while Mission Control still owns the whole-student "what first" decision;
  - source readiness, Wiki readiness, citation readiness, linked concepts, linked exam outcomes, source-backed/source-limited concepts, stale/deleted/insufficient/degraded sources, repair/duplicate/manual/tutor-trace/source-backed pages, and Notebook pack readiness remain separate fields;
  - source-grounded/source-backed claims are blocked or downgraded when evidence is stale, deleted, insufficient, degraded, or citation review is missing/unsupported/stale/needs-review;
  - provider output and Wiki memory alone do not count as citation/source evidence;
  - manual Wiki notes are preserved while duplicate/stale trace cleanup and repair-pending pages produce safe handoff actions.
- Integration:
  - `SourcesController` exposes `/api/sources/wiki-pro` for the current user and optional user-owned topic/source/wiki page scope.
  - `DashboardController` includes compact `SourceWikiPro` in `/api/dashboard/today`.
  - `StudentSimulationEvaluationTests` includes Source / Wiki Pro in serialized public payload sweeps and mixed Learning OS consistency checks.
  - `OrkaSourceWikiProTests` verifies new learner, ready source/concept/notebook handoff, stale/citation warning, Wiki repair/duplicate/manual note preservation, dashboard integration, and cross-user access blocking.
- Safety:
  - deterministic/provider-free by default;
  - no new provider call, AI judge, scraping, migration, or official-content population;
  - no Stripe/payment code;
  - no source-grounded, official, score, percentile, placement, or success guarantee claim without verified metadata;
  - no raw transcript, prompt, provider payload, source chunk, Wiki block body, tool/debug payload, local path, owner/user id, stack trace, pre-submit answer key, or correct answer in public Source / Wiki Pro DTOs.
- Out of scope: frontend redesign, mobile app, payment/subscription, OpenAI Responses API, Agents SDK, Realtime voice, new AI/provider architecture, Google Cloud, official scraping, semantic contradiction claims beyond evidence status, and institutional classroom workflows.

### Phase 6 - AI Study Room Backend

- Goal: turn the existing Classroom/Study Room foundation into a personal AI study-session backend contract connected to the whole Learning OS.
- Main service: `IOrkaStudyRoomService` / `OrkaStudyRoomService`.
- Inputs: `OrkaLearningStateDto`, `OrkaMissionControlDto`, `OrkaStudyCoachDto`, `OrkaExamWarRoomDto`, `OrkaSourceWikiProDto`, Tutor handoff policy, quiz/mastery/review/memory evidence, Wiki learning traces, source/citation readiness, Notebook Studio readiness, and user-owned Classroom session context.
- Outputs: `OrkaStudyRoomDto`, `OrkaStudyRoomPlanDto`, `OrkaStudyRoomRoleDto`, `OrkaStudyRoomCheckpointDto`, `OrkaStudyRoomTurnDto`, `OrkaStudyRoomActionDto`, and `OrkaStudyRoomWarningDto`.
- Student-facing semantics:
  - Study Room/Classroom means a personal AI study room only, not teacher/classroom/dershane management;
  - supported modes are quick start, repair lesson, review lesson, exam outcome practice, source review lesson, Wiki repair lesson, checkpoint quiz, and continue plan;
  - roles are product modes (`ai_teacher`, `ai_assistant`, `student`), not real human teacher claims;
  - checkpoint turns expose no pre-submit answer key and record only bounded safe response signals after submit;
  - source-grounded lessons are blocked or downgraded unless Source / Wiki Pro evidence allows them.
- Integration:
  - `ClassroomController` exposes `/api/classroom/study-room`, `/api/classroom/study-room/start`, and `/api/classroom/study-room/checkpoint`.
  - `DashboardController` includes compact `StudyRoom` metadata in `/api/dashboard/today`.
  - Completed starts/checkpoints write safe Classroom learning signals and bounded traces without raw transcript dumps.
  - `StudentSimulationEvaluationTests` includes Study Room in serialized public payload sweeps and mixed Learning OS consistency checks.
  - `OrkaStudyRoomTests` verifies new learner, repeated wrong, blank/skipped, due review, exam outcome, source insufficient, Wiki repair, checkpoint safety, dashboard integration, and cross-user access blocking.
- Safety:
  - deterministic/provider-free by default;
  - no new AI/provider call, paid provider call, AI judge, migration, or Realtime voice implementation;
  - no Stripe/payment code;
  - no therapy, psychology, medical, wellbeing, ADHD, burnout, diagnosis, official curriculum, score, percentile, placement, or success guarantee claim;
  - no raw transcript, prompt, provider payload, source chunk, Wiki block body, tool/debug payload, local path, owner/user id, stack trace, pre-submit answer key, or correct answer in public Study Room DTOs.
- Out of scope: frontend redesign, mobile app, payment/subscription, OpenAI Responses API, Agents SDK, Realtime voice, new AI/provider architecture, Google Cloud, official scraping, and institutional classroom workflows.

### Phase 7 - Notebook Studio / Artifact Pro Pack Backend

- Goal: make Notebook Studio a professional learning artifact workspace instead of a loose export surface.
- Inputs: `OrkaLearningStateDto`, `OrkaMissionControlDto`, `OrkaStudyCoachDto`, `OrkaExamWarRoomDto`, `OrkaSourceWikiProDto`, `OrkaStudyRoomDto`, `LearningNotebookPack`, `LearningArtifact`, Wiki/source evidence metadata, review/quiz/memory signals, and deterministic export-preview metadata.
- Contract:
  - `IOrkaNotebookStudioProService` / `OrkaNotebookStudioProService` produce `OrkaNotebookStudioProDto`;
  - `/api/notebook-studio/pro` exposes readiness, recommended packs, active pack, artifact queue, export previews, evidence links, handoffs, warnings, reason codes, and safe summary;
  - `/api/dashboard/today` includes compact `NotebookStudioPro` metadata for Home/Mission Control consumption.
- Behavior:
  - pack recommendations include repair, review, exam outcome, deneme mistake, source study, Wiki cleanup, Study Room summary, Tutor lesson, flashcard, checkpoint quiz, slide outline, audio script, and artifact collection;
  - source/citation blockers downgrade source-backed artifact claims and create source/citation review actions;
  - Study Room traces are linked only as bounded safe trace metadata, never raw transcripts;
  - export previews remain preview-only and explicitly do not claim real PPTX/video generation;
  - handoffs to Tutor, Review, Source/Wiki, Exam War Room, Study Room, and Dashboard are suggestions only, not hidden autonomous edits/actions.
- Safety:
  - deterministic/provider-free by default;
  - no new AI/provider call, paid provider call, AI judge, migration, real PPTX/video generation, or Realtime voice implementation;
  - no Stripe/payment code;
  - provider output and Wiki memory alone are not citation evidence;
  - no therapy, psychology, medical, wellbeing, ADHD, burnout, diagnosis, official curriculum, score, percentile, placement, source-grounded overclaim, or success guarantee claim;
  - no raw transcript, prompt, provider payload, source chunk, Wiki block body, tool/debug payload, local path, owner/user id, stack trace, pre-submit answer key, or correct answer in public Notebook Studio Pro DTOs.
- Out of scope: frontend redesign, mobile app, payment/subscription, OpenAI Responses API, Agents SDK, Realtime voice, new AI/provider architecture, Google Cloud, official scraping, real PPTX/video generation, and institutional classroom workflows.

### Phase 8 - Code Learning IDE + Tool Runtime Polish Backend

- Goal: make coding practice a safe Learning OS mode instead of a detached run-code utility.
- Inputs: `OrkaLearningStateDto`, `OrkaMissionControlDto`, `OrkaStudyCoachDto`, `OrkaNotebookStudioProDto`, Tutor handoff policy, code execution learning signals, quiz/mastery/review/memory evidence, Wiki learning traces, learning artifacts, and tool capability metadata.
- Contract:
  - `IOrkaCodeLearningIdeService` / `OrkaCodeLearningIdeService` produce `OrkaCodeLearningIdeDto`;
  - `/api/code/learning-ide` exposes runtime readiness, active language/topic/skill/exercise, last attempt summary, repeated error summary, checkpoint status, repair status, handoffs, warnings, reason codes, and safe summary;
  - `/api/dashboard/today` includes compact `CodeLearningIde` metadata for Home/Mission Control consumption.
- Behavior:
  - code practice priority handles runtime/tool blockers, repeated syntax errors, repeated runtime errors, repeated test failures, repeated blank/no-attempt signals, weak coding concepts, due review, checkpoint challenges, and stable continuation;
  - one error does not create heavy repair pressure; repeated code-learning signals can create syntax/runtime/test repair actions;
  - unsupported or unsafe shell-like runtime requests return blocked/limited status in the Code IDE contract without broadening host execution permissions;
  - successful coding attempts can lower pressure through safe learning signals while failures create bounded repair handoffs;
  - Notebook Studio Pro can expose code repair/checkpoint pack handoffs from existing evidence.
- Safety:
  - deterministic/provider-free by default;
  - no new AI/provider call, paid provider call, AI judge, migration, or runtime permission expansion;
  - no OpenAI Responses API, Agents SDK, Realtime migration, Google Cloud, Stripe/payment code, frontend redesign, or teacher/classroom management workflow;
  - public Code Learning IDE and code-run DTOs must redact stack traces, local paths, secrets, tokens, API keys, raw tool/debug payload markers, owner/user ids, prompts, provider bodies, source chunks, transcripts, answer keys, and correct answers;
  - no official curriculum, score, percentile, placement, source-grounded overclaim, or success guarantee claim.
- Out of scope: frontend redesign, mobile app, payment/subscription, OpenAI API migration, Realtime voice, new AI/provider architecture, Google Cloud, unsafe shell/system access, official scraping, and institutional classroom workflows.

### Phase 9 - Unified Evaluation / CI / Release Harness

- Goal: prove the backend Learning OS works as one coherent product release gate, not as disconnected service tests.
- Inputs: `OrkaLearningStateDto`, `OrkaMissionControlDto`, `OrkaStudyCoachDto`, `OrkaExamWarRoomDto`, `OrkaSourceWikiProDto`, `OrkaStudyRoomDto`, `OrkaNotebookStudioProDto`, `OrkaCodeLearningIdeDto`, Tutor response policy metadata, dashboard composition behavior, student simulation scenarios, backend lifetest, release closure tests, and local quick scripts.
- Contract:
  - `IOrkaUnifiedEvaluationService` / `OrkaUnifiedEvaluationService` produce `OrkaUnifiedEvaluationDto`;
  - outputs include scenario results, scorecard checks, module consistency checks, public payload safety sweep, release gate summary, failing/warning checks, recommended fixes, reason codes, and safe summary;
  - status values remain bounded to pass/warning/fail/blocked style release metadata.
- Behavior:
  - the scorecard covers unified state, Mission Control, Study Coach, Exam War Room, Source / Wiki Pro, Study Room, Notebook Studio Pro, Code Learning IDE, Tutor policy, Dashboard readiness, quiz/mastery/memory, review/SRS, safety/privacy, no-overclaim, cross-user safety, provider-free behavior, and release gate readiness;
  - scenario coverage is validated by `StudentSimulationEvaluationTests` and strengthened by `OrkaUnifiedEvaluationHarnessTests`;
  - cross-module consistency catches dangerous disagreements such as source-grounded Tutor policy with blocked Source / Wiki Pro evidence, Study Room without safe context, Notebook source-backed pack overclaim, or Code IDE runtime blocked state without safe downgrade;
  - `scripts/quick-backend.ps1` includes a Product Coherence release proof group for Phase 1-9 backend coherence.
- Safety:
  - deterministic/provider-free by default and no AI judge;
  - no new AI/provider call, paid provider call, OpenAI Responses API, Agents SDK, Realtime migration, Google Cloud, Stripe/payment code, frontend redesign, teacher/classroom management workflow, unsafe runtime expansion, migration, or official scraping;
  - no raw prompts, provider payloads, source chunks, Wiki block bodies, tool/debug payloads, local paths, secrets, owner/user ids, stack traces, raw transcripts, pre-submit answer keys, official/source-grounded overclaims, or success guarantees in public evaluation DTOs.
- Out of scope: remote GitHub CI debugging, deployment architecture changes, provider-backed CI jobs, secrets, frontend redesign, mobile app, payment/subscription, OpenAI API migration, and institutional classroom workflows.

### Phase 10 - UX Research / Product Map

- Goal: turn the completed backend Learning OS into a clear product map before frontend redesign.
- Outputs:
  - `docs/product/orka-product-map.md`;
  - `docs/product/orka-frontend-contract-map.md`;
  - `docs/product/orka-learner-journeys.md`;
  - `docs/product/phase-11-frontend-redesign-brief.md`;
  - `docs/product/orka-product-readiness-scorecard.md`;
  - `docs/product/orka-existing-frontend-audit.md`.
- Product model:
  - Orka is a personal Learning OS / AI study OS, not only a chatbot and not a teacher/classroom/dershane management system.
  - Home / Mission Control is the future first screen.
  - Main work modes are Tutor, Study Room, Review / Quiz, Exam War Room, Sources / Wiki Pro, Notebook Studio, Code Learning IDE, Progress / Memory, and Settings / Safety.
  - Handoffs are visible suggestions, not hidden autonomous actions.
- Backend-to-frontend mapping:
  - Home consumes Dashboard Today, Mission Control, and unified Orka learning state.
  - Tutor consumes safe Tutor policy and next-action metadata.
  - Study Room consumes `OrkaStudyRoomDto`.
  - Exam consumes `OrkaExamWarRoomDto`.
  - Sources/Wiki consumes `OrkaSourceWikiProDto`.
  - Notebook consumes `OrkaNotebookStudioProDto`.
  - Code consumes `OrkaCodeLearningIdeDto`.
  - Progress consumes unified state, dashboard progress, and snapshots.
- Existing frontend audit:
  - current `/app` is a panel shell with dashboard/chat/wiki/source/exam/learning/IDE surfaces;
  - useful code should be reused, but Phase 1-9 Product Coherence DTOs are not yet first-class frontend API clients or screens.
- Safety:
  - documentation-only phase;
  - no frontend implementation, migration, provider call, paid validation, OpenAI Responses API, Agents SDK, Realtime migration, Google Cloud, Stripe/payment, mobile app, unsafe runtime expansion, official scraping, or teacher/classroom management workflow;
  - no official/source-grounded/success guarantee claims;
  - product docs preserve the no raw prompts/provider/source/tool/debug/local path/secret/id/stack trace/transcript/answer-key policy.
- Out of scope: building UI components, redesigning screens, mobile app, payment/subscription, Realtime voice, real PPTX/video, new provider architecture, and institutional classroom workflows.

## Terminology Glossary

| Term | Meaning |
|---|---|
| Tutor | The pedagogical owner that explains, remediates, asks micro-checks, and uses learner evidence safely. |
| Korteks | Research engine that gathers source/context evidence and produces research artifacts. |
| Synthesis | The bounded educational transformation from research evidence into plan/quiz/wiki/tutor inputs. |
| ActiveLessonSnapshot | Target contract for the current lesson state: intent, topic, concepts, mastery, sources, tools, plan, quiz, and next action. |
| StudentContextSnapshot | Target contract for learner state across topics: strengths, weak concepts, confidence, remediation, affect/load/style signals. |
| LearningMemory | Current service projection of strong/weak topics, misconceptions, remediation-ready items, and confidence. |
| KnowledgeTracing | Evidence-based per-concept mastery state updated from assessment attempts. |
| Mastery | Current estimate of concept/skill understanding with confidence and remediation need. |
| Misconception | A bounded wrong-answer pattern used for safe remediation, not a psychological diagnosis. |
| RAG evidence | Retrieved source/wiki chunks and citation metadata used to ground answers. |
| Wiki notebook | Student-facing topic knowledge workspace built from wiki pages, blocks, sources, and learning context. |
| OrkaLM source notebook | Source-centered Notebook Studio surface for uploaded PDFs/TXT/MD sources. It uses `LearningSource`, source evidence bundles, `orkalm_source` Wiki pages, deterministic source-to-concept links, and `LearningNotebookPack` metadata instead of a separate app/table. |
| Source-to-concept link | A user-scoped `WikiLink` relationship from an `orkalm_source` page to an existing concept Wiki page. Links are confidence-labeled graph hints based on deterministic evidence and do not by themselves create source-backed claims. |
| Ask-source | Source-centered question flow backed by `SourceQuestionService`. It can ask a selected source or bounded source collection, labels source basis/evidence/readiness, returns safe citation labels, can write Wiki traces, and never exposes raw chunks/prompts/provider payloads. |
| Wiki Copilot | Page-aware helper surface backed by `IWikiCopilotService`. It reads safe Wiki page, curation, source/evidence, repair, artifact, and Notebook Studio status and returns deterministic suggestions/handoffs without provider calls, raw payloads, hidden actions, or source-grounded claims without evidence. |
| Teaching artifact | Diagram, table, formula, image prompt, video reference, code result, or study note created for a learning objective. |
| Tool ledger | Target durable record for every governed tool call and result, including safety, fallback, and consumption state. |
| Tool capability | Backend contract describing whether a tool is enabled, gated, risky, provider-backed, and telemetry/cost tracked. |
| Runtime telemetry | Durable and trace-level records for tool/model/provider/cost/fallback behavior. |
| Pedagogy evaluation | Tutor response quality check for policy alignment, scaffolding, grounding, misconception repair, clarity, and safety. |
| Central Exams | Built-in domain module for exam prep using Orka's shared curriculum, question bank, assessment, learning, Tutor, and Wiki contracts. |
| Student simulation harness | Provider-free backend test harness that seeds realistic learner journeys and evaluates long-term learning, exam profile, source/wiki profile, Tutor, dashboard, Wiki/source safety, and privacy as one Learning OS. |

## Pack 0 Findings Summary

- Orka has a serious learning architecture foundation: Tutor turn state, working memory, action planning, tool calls, pedagogy evaluation, concept graph, assessment grammar, learning memory, RAG/Wiki evidence, and frontend metadata surfaces.
- The main professionalization blocker is not missing pieces; it is convergence. Context, tools, research, assessment, Wiki evidence, and frontend render state need common contracts.
- The largest current architectural split is the tool plane: Tutor-governed tools are durable and user-scoped; Semantic Kernel plugin telemetry is less contextual and should become an adapter under the same governance.
- Plan and quiz quality have meaningful gates, but professional topic-specific plan semantics and final quiz item quality need centralized scoring.
- Frontend already treats metadata as first-class in several places; Pack 9 should unify this into one learning workspace experience.
