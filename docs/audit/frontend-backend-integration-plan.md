# Frontend Backend Integration Plan

Phase: Frontend Integration + UX/Product Polish Against Frozen Backend Contract  
Target frontend: `D:\Orka-main-validation\Orka-Front`  
Backend base URL: `http://localhost:5101`

## Baseline

| Check | Result | Notes |
|---|---|---|
| Frontend path | PASS | Vite + React 19 + TypeScript under `Orka-Front`. |
| Package manager | PASS | npm with `package-lock.json`. |
| Initial dependency state | NOTE | `node_modules` was absent; `npm install` installed from lockfile. |
| `npm run build` baseline | PASS | Production build succeeded. |
| `npm run smoke:ui` baseline | PASS | Existing UI smoke passed. |
| `npm run smoke:contracts` baseline | PASS | Existing API/controller smoke passed. |
| Lint/test scripts | NOTE | No `lint` or `test` npm scripts are defined. |

## Current Frontend Structure

- App shell: `src/pages/Home.tsx`, `src/components/LeftSidebar.tsx`.
- Chat/Tutor: `src/components/ChatPanel.tsx`, `src/components/ChatMessage.tsx`.
- IDE/Piston: `src/components/InteractiveIDE.tsx`.
- Wiki/source/RAG: `src/components/WikiMainPanel.tsx`, `src/components/WikiDrawer.tsx`.
- Dashboard/profile/settings: `src/components/DashboardPanel.tsx`, `src/pages/Profile.tsx`, `src/components/SettingsPanel.tsx`.
- API layer: `src/services/api.ts`.
- Existing smoke tests: `scripts/smoke-ui.mjs`, `scripts/smoke-endpoints.mjs`.

## Backend Contract Integration Changes

| Area | Decision | Implementation |
|---|---|---|
| Backend base URL | Use frozen backend default | Vite proxy now defaults to `http://localhost:5101`; `VITE_API_PROXY_TARGET` can override. |
| Direct API base | Supported without hardcoding | `VITE_API_BASE_URL` can point directly to backend; fetch helpers use `buildApiUrl`. |
| Tool capability source of truth | Integrated | Added `ToolsAPI` and `ToolCapabilitiesProvider`; UI reads `/api/tools/capabilities`. |
| Tool visibility | Capability-driven | IDE nav is hidden unless `ide_execution` is visible and enabled. Tool chips use backend status/decision. |
| Chat metadata | Additive | `ChatResponseMetadata`, `UsedToolDto`, citations and fallback fields added to frontend types; `ChatMetadataChips` renders when metadata is present. |
| Streaming metadata | Accepted UI decision | Backend stream currently sends text chunks and session/topic headers, not final metadata event. UI does not fabricate provider/tool state from prose. |
| IDE/Piston response | Integrated | UI now supports `phase`, `compileError`, `runtimeError`, `durationMs`, `truncated`, `safeTutorSummary`, and `runtime`. |
| Learning features | Integrated in existing shell | Added `LearningPanel` for Flashcards, Review/SRS, Daily Challenge, and Bookmarks. |
| Sources/Wiki | Wired | Existing Wiki panel uploads/lists/asks sources, deletes sources through backend, and renders citations/page evidence. |
| Provider tools | Capability-visible | News/weather/crypto/Wolfram/YouTube/Mermaid/visual status appears through backend capability chips. |

## Screens Still Static Or Limited

- Courses remain mostly product/course catalog style and were not rewritten.
- Audio/Classroom UI exists through message audio/classroom components; no full new classroom page was added.
- Provider-specific action forms for News/Weather/Crypto/Wolfram were not added as separate screens; current product path is Tutor/tool runtime and capability visibility.
- Streaming chat metadata display is limited until backend emits metadata over SSE or exposes message metadata in session history.

## UX Polish Performed

- Added capability chips in Tutor header so available/gated tools are visible without a noisy raw JSON panel.
- Added Pratik panel inside the existing app shell instead of redesigning navigation.
- IDE output now separates compile/runtime/timeout/blocked phases and shows sandbox/runtime/duration/truncation labels.
- Code execution errors are presented as learning feedback and can be sent to Tutor with backend safe summary.
- Login backend-unavailable copy now points to the frozen backend URL.

## Integration Order Used

1. Baseline build/smoke.
2. Central API contract layer.
3. Capability provider and UI status chips.
4. Chat metadata additive rendering.
5. IDE/Piston response contract.
6. Durable learning panel wiring.
7. Smoke/build and runtime lifetest documentation.
