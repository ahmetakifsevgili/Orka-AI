# Learning Experience Polish + Orka Identity Pass

## Executive Summary

This pass turns the existing Orka frontend from a chat-first surface into a calmer personal study environment. The backend contract was not changed. The UI now opens around a study focus, shows learning-loop context more visibly, and frames code errors, tool metadata, citations, flashcards, review, daily challenge, and bookmarks as connected learning surfaces.

No fake progress, fake weakness, fake tool execution, or fake learning-signal persistence was added.

## Product Experience Audit

| Finding | Classification | Decision |
|---|---|---|
| App defaulted into chat, so the first feeling was close to a generic chatbot. | CONFIRMED_CHATBOX_FEEL | Default entry now opens the dashboard/study focus. |
| Dashboard had real stats but did not directly answer "what should I study today?". | CONFIRMED_STUDY_HOME_GAP | Added backend-backed Today's Focus and Next Small Step sections. |
| Tutor metadata existed but was too technical and easy to miss. | CONFIRMED_TUTOR_POLISH_GAP | Added a compact learning trace card under assistant responses when metadata exists. |
| IDE errors were technically correct but needed stronger learning framing. | CONFIRMED_IDE_POLISH_GAP | Added phase-aware learning notes for success, compile, runtime, timeout, blocked, provider-missing, and network states. |
| Flashcards, review, daily challenge, and bookmarks worked but felt separate. | CONFIRMED_LEARNING_LOOP_VISIBILITY_GAP | Added a Learning Loop summary that ties the surfaces together without inventing data. |
| Primary UX should not frame Orka as institution/admin software. | CONFIRMED_PRODUCT_NARRATIVE_GAP | Navigation and main flows now emphasize Today, Tutor, Practice, Wiki, and IDE. |

## What Changed

### Personal Study Home

- The default app view is now the dashboard instead of chat.
- Added a "Bugunku odak" card using real topic, weak-skill, recent signal, and quiz data when available.
- If no learning data exists, the empty state explicitly says Orka will show patterns only after real learning signals exist.
- Added clear next actions: continue with Tutor or open the review/practice loop.

### Tutor Premium Response UX

- Assistant responses still render the main answer normally.
- When backend metadata exists, Orka now shows:
  - grounding/basis
  - tool chips
  - citation count
  - fallback notice
  - a small learning-impact note
- The UI does not infer tool use from prose and does not claim a signal was saved unless backend metadata supports the context.

### IDE Learning UX

- Code output now includes an educational note:
  - success: ask Tutor to check the output
  - compile error: language-rule/symbol signal
  - timeout: loop/input-size hint
  - blocked: sandbox boundary
  - provider missing: no fake execution
  - network error: retry without losing code
- Frontend still sends code only to the backend sandbox API.

### LearningPanel As One Loop

- Added a top Learning Loop summary.
- Shows real counts for flashcards, due reviews, daily challenge availability, and bookmarks.
- Copy now frames the panel as one adaptive loop instead of disconnected boxes.

### Source/Wiki/Grounding UX

- Existing citation and source rendering stayed intact.
- Tutor metadata now separates grounding/tool/citation/fallback information more clearly.
- YouTube remains pedagogy reference, not factual authority.

### Product Narrative Cleanup

- The primary navigation now opens with "Bugun" and "Tutor" instead of a chat-first framing.
- No B2B, institution, teacher-dashboard, or school-admin feature was added.
- Existing admin-only LLMOps surfaces remain gated and were not promoted in learner UX.

## Desktop / Responsive Notes

- This pass is desktop-first.
- The new sections use constrained grids and wrapping chips to avoid cramped desktop layouts.
- No mobile-first redesign was attempted.

## Accessibility Notes

- New action buttons include visible focus rings.
- IDE output toggle already has aria-label behavior and was preserved.
- New learning cards avoid hover-only critical information.

## Tests Run

Initial baseline:

| Command | Result |
|---|---|
| `npm run build` | PASS |
| `npm run smoke:ui` | PASS |
| `npm run smoke:contracts` | PASS |
| `npm run typecheck` | PASS |

Final validation:

| Command / Smoke | Result |
|---|---|
| `npm run build` | PASS |
| `npm run smoke:ui` | PASS |
| `npm run smoke:contracts` | PASS |
| `npm run typecheck` | PASS |
| `python -m pytest contract_tests/ -q` | PASS, 37 passed / 1 skipped |
| `GET /health/live` on `http://127.0.0.1:5101` | PASS, 200 |
| `GET /health/ready` on `http://127.0.0.1:5101` | PASS, 200 |
| `GET /api/tools/capabilities` on `http://127.0.0.1:5101` | PASS, 200 |
| `GET /` on `http://127.0.0.1:3000` | PASS, 200 |
| `GET /login` on `http://127.0.0.1:3000` | PASS, 200 |

## Remaining Notes

| Note | Classification | Reason |
|---|---|---|
| Some UI strings in existing files are still legacy Turkish encoding in source display, but smoke guards pass. | NON_BLOCKING_NOTE | Not introduced by this phase. |
| Streaming chat metadata still depends on backend final metadata events; frontend does not infer metadata from prose. | NON_BLOCKING_NOTE | Correct contract behavior. |
| Deeper V3 features such as 3D class modeling and KPSS-specific advanced algorithm surfaces are not implemented here. | PRODUCT_ROADMAP | This phase is polish and identity, not V3 expansion. |
| Mobile-specific redesign is intentionally out of scope. | PRODUCT_ROADMAP | User explicitly deprioritized mobile. |
