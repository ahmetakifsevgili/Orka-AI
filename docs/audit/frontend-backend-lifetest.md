# Frontend Backend Lifetest

Phase: Frontend Integration + UX/Product Polish Against Frozen Backend Contract  
Backend: `http://localhost:5101`  
Frontend: Vite dev server, default `http://localhost:3000`

## Automated Validation

| Check | Expected | Actual | Result |
|---|---:|---:|---|
| `npm install` | dependencies installed | installed from lockfile | PASS |
| `npm run build` | production build succeeds | PASS | PASS |
| `npm run smoke:ui` | UI contract smoke succeeds | PASS | PASS |
| `npm run smoke:contracts` | endpoint/controller smoke succeeds | PASS | PASS |
| `npm run lint` | if available | no script | BLOCKED_WITH_REASON |
| `npm test` | if available | no script | BLOCKED_WITH_REASON |

## Manual / Runtime Checks

| Step | Expected | Actual | Result |
|---|---|---|---|
| App boot | frontend loads with existing Orka shell | `curl http://127.0.0.1:3000/` returned 200; `/login` returned 200 | PASS |
| Backend connection | frontend proxies `/api` to backend 5101 | config updated to 5101 default | PASS |
| Auth | login/register use backend auth endpoints | API client unchanged except base URL safety | PASS |
| Protected routes | `/app`, `/profile`, `/courses` require token | existing `ProtectedRoute` preserved | PASS |
| Capabilities | UI loads `/api/tools/capabilities` | `ToolCapabilitiesProvider` added | PASS |
| Tool visibility | no hardcoded provider states | capability context drives chips and IDE visibility | PASS |
| Chat/Tutor | stream path preserved | existing stream UI preserved | PASS |
| Chat metadata | render when backend metadata exists | additive chips added; SSE metadata not fabricated | PASS_WITH_NOTE |
| IDE/Piston | code sent only to backend `/api/code/run` | frontend never executes code locally | PASS |
| IDE result phases | compile/runtime/timeout/blocked/provider_missing shown safely | phase/error/runtime/truncation fields supported | PASS |
| Sources/Wiki | source upload/list/ask/citation UI exists | existing Wiki panel preserved | PASS |
| Learning | flashcards/review/daily/bookmarks wired | `LearningPanel` added | PASS |
| Provider tools | news/weather/crypto/Wolfram/YouTube/Mermaid state visible | capability strip added | PASS |
| Responsive/accessibility | touched buttons have labels/disabled states | no full redesign; obvious controls labelled | PASS_WITH_NOTE |

## Runtime Smoke Evidence

| URL | Expected | Actual | Result |
|---|---:|---:|---|
| `http://127.0.0.1:3000/` | 200 | 200 | PASS |
| `http://127.0.0.1:3000/login` | 200 | 200 | PASS |
| `http://127.0.0.1:5101/swagger/index.html` | 200 | 200 | PASS |
| `http://127.0.0.1:5101/health/live` | 200 | 200 | PASS |
| `http://127.0.0.1:5101/health/ready` | 200 | 200 | PASS |
| `http://127.0.0.1:5101/api/korteks/ping` | 401 unauthenticated | 401 | PASS |
| `http://127.0.0.1:5101/api/tools/capabilities` | 200 | 200 | PASS |
| `http://127.0.0.1:5101/api/tools/capabilities/ide_execution` | 200 | 200 | PASS |
| `http://127.0.0.1:5101/api/tools/capabilities/news` | 200 | 200 | PASS |
| `http://127.0.0.1:5101/api/tools/capabilities/weather` | 200 | 200 | PASS |
| `http://127.0.0.1:5101/api/tools/capabilities/crypto` | 200 | 200 | PASS |
| `http://127.0.0.1:5101/api/tools/capabilities/wolfram_alpha` | 200 | 200 | PASS |
| `http://127.0.0.1:5101/api/tools/capabilities/youtube_pedagogy` | 200 | 200 | PASS |

## Backend Contract Cross-Check

- Frontend uses `/api/tools/capabilities` and does not hardcode live provider availability.
- Frontend does not call GDELT, Open-Meteo, CoinGecko, Wolfram, NewsAPI, YouTube, or Piston directly.
- Frontend code execution uses backend `/api/code/run`; no client-side fake execution was added.
- Frontend treats provider fallback/disabled state as tool/capability state, not app failure.
- Frontend does not show crypto buy/sell/hold recommendation UI.
- Frontend does not present YouTube pedagogy as factual citation.

## Known Notes

- Streaming chat still cannot display final backend `metadata.usedTools` unless the backend emits it in SSE or includes metadata in session history. The UI is ready for additive metadata and does not infer provider state from prose.
- `visual_generation` remains a beta/gated visual capability; frontend exposes status only through capability chips.
- Wolfram remains AppId-gated; frontend shows capability status rather than pretending it is live.
