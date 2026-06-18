# Orka Module Contract Audit - 2026-06-18

## Scope

This audit reviewed the Tutor, Plan Diagnostic, Wiki/Source/Notebook, Audio/Classroom, provider routing, and test-contract surfaces. Three subagents inspected independent slices, and the main pass verified the active findings against source code before patching.

## Fixed In This Pass

### Tutor context and orchestration

- Real source/wiki/notebook/IDE context is now passed into the final Tutor system prompt for both non-stream and stream paths.
- Stream chat no longer emits the assistant answer before deterministic pedagogy evaluation runs. If the quality gate requires repair, the repaired answer is emitted instead.
- Stream `tool_started` events now use the same execution filter as `TutorToolOrchestrator`, preventing ghost tool states.
- Provider/tool failures inside Tutor tool execution now degrade to a persisted tool result instead of failing the whole Tutor turn.
- Tutor turn `SourceEvidenceCount` no longer double-counts notebook context that the policy engine already counted.

### Plan diagnostic

- Diagnostic quiz provider failures now fall back to the deterministic assessment grammar blueprint instead of failing plan start.
- Provider infrastructure exceptions are covered by regression tests.
- Generated plan steps preserve the materialized module/lesson order instead of re-sorting all lessons globally by lesson `Order`.

### Audio/Classroom context coherence

- Audio overview rejects same-user cross-topic or cross-session combinations between requested `topicId`/`sessionId` and selected `wikiPageId`/`sourceId`.
- Classroom rejects mismatched `audioOverviewJobId`, `wikiPageId`, `sourceId`, `topicId`, and `sessionId` combinations.
- Controllers convert these coherence violations to controlled `400 BadRequest` responses.

## Still Open / Follow-Up

- Wiki page mount still auto-loads several summarizer-backed study surfaces. This is functionally useful but can create provider fan-out on cache miss. Recommended follow-up: lazy-load briefing/glossary/timeline/mindmap/study-cards behind user action, viewport visibility, or a backend bundled cache endpoint.
- Audio content type is still fixed to `audio/mpeg`. This is acceptable for the current Edge-TTS path, but a future TTS provider should return content type and extension metadata.
- Stream chat now buffers the model answer until the deterministic pedagogy gate finishes. This is safer, but it trades away token-by-token streaming for the answer body. If true live streaming is required, add a pre-generation policy gate plus a post-generation repair fallback event.

## Verification

- `dotnet build Orka.API/Orka.API.csproj --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false -p:OutDir=D:\Orka\artifacts\codex-out\api-build\`
- `dotnet test Orka.Infrastructure.UnitTests/Orka.Infrastructure.UnitTests.csproj --filter "FullyQualifiedName~PlanDiagnosticTests" --no-restore --verbosity minimal -p:UseSharedCompilation=false -p:OutDir=D:\Orka\artifacts\codex-out\infra-tests\`
- `dotnet test Orka.API.Tests/Orka.API.Tests.csproj --filter "FullyQualifiedName~RequestBoundarySafetyTests" --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false -p:OutDir=D:\Orka\artifacts\codex-out\api-tests\`
- `dotnet test Orka.Infrastructure.UnitTests/Orka.Infrastructure.UnitTests.csproj --filter "FullyQualifiedName~LearningArchitectureTests|FullyQualifiedName~TutorPedagogyEvaluationTests" --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false -p:OutDir=D:\Orka\artifacts\codex-out\infra-tests\`
- `dotnet test Orka.API.Tests/Orka.API.Tests.csproj --filter "FullyQualifiedName~ToolActivationTutorConsumptionTests|FullyQualifiedName~UnifiedToolRuntimeTests|FullyQualifiedName~BackendLifeTests|FullyQualifiedName~LearningNotebookStudioTests" --no-restore --verbosity minimal -m:1 -p:UseSharedCompilation=false -p:OutDir=D:\Orka\artifacts\codex-out\api-tests\`
- `npm run typecheck` in `D:\Orka\Orka-Front`
- `git diff --check`
