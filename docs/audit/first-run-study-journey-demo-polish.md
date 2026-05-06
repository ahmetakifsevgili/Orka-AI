# First-Run Study Journey + Demo Scenario Polish

## Executive Summary

This pass makes Orka easier to understand in the first five minutes. The work stays deliberately small: no backend rebuild, no full V3 exam engine, no fake progress, and no fake weak-skill signals.

The first-run surface now helps a learner choose a starting direction, enter Tutor with safer starter prompts, and understand that real personalization appears only after real activity such as solving, coding, reviewing, asking with sources, or saving bookmarks.

## Baseline

| Check | Result |
|---|---|
| Active repo | `D:\Orka` |
| Starting branch | `main` |
| Starting commit | `a0530c5 Merge pull request #7 from ahmetakifsevgili/feature/learning-experience-orka-identity-pass` |
| `npm run build` | PASS |
| `npm run smoke:ui` | PASS |
| `npm run smoke:contracts` | PASS |
| `npm run typecheck` | PASS |

## First-Run Audit

| Question | Finding | Classification | Decision |
|---|---|---|---|
| Does a new learner know what to do first? | Dashboard was improved previously, but the user still needed to invent the first prompt. | FIRST_RUN_GAP | Added starter prompts in Tutor welcome state. |
| Does Orka explain when real data appears? | Dashboard had a no-fake-data statement, but Tutor welcome copy still sounded broad. | EMPTY_STATE_GAP | Tutor welcome now explains real signals come from quiz, IDE, source, and review activity. |
| Can a demo presenter show a 3-5 minute flow? | The pieces existed but the path was not documented. | DEMO_FLOW_GAP | Added a dedicated demo scenario doc. |
| Is a lightweight exam/study focus useful before V3? | Yes, as orientation only. | EXAM_FOCUS_SKELETON_OPPORTUNITY | Added local study-focus preference chips. |
| Does the UX avoid fake personalization? | Yes, after this pass. | ALREADY_SAFE | No fake weak areas or progress were added. |

## What Changed

### First-Run Study Journey

- Dashboard now includes a lightweight "Hedef odagi" selector:
  - Genel calisma
  - KPSS
  - YKS
  - Dil
  - Yazilim
  - Matematik
- This selector is a local UI preference only. It shapes starter prompt language and does not claim backend adaptive exam mastery.
- Empty topic state now explains how real topic/source/code/review signals will populate the dashboard.

### Tutor Starter Prompts

The empty Tutor state now offers safe starter prompts:

- Konu ogren
- Kaynakla calis
- Kod hatasi coz
- Tekrar yap

These prompts use the selected study focus when relevant, but do not fake existing user knowledge.

### Demo-Friendly Transitions

- Dashboard now links clearly to Tutor, Practice, IDE, and Wiki.
- IDE quick access was added to the dashboard side actions so a presenter can show code-error learning without hunting through the UI.
- The first-run story is now:
  1. Open Bugun.
  2. Pick a study focus if desired.
  3. Start Tutor from a prompt.
  4. Show source/tool/learning trace when metadata exists.
  5. Open IDE for code learning.
  6. Return to Practice loop.

## What Was Not Implemented

| Item | Classification | Reason |
|---|---|---|
| Full KPSS algorithm | PRODUCT_ROADMAP | This phase only adds a study-focus skeleton. |
| Full YKS/exam curriculum engine | PRODUCT_ROADMAP | Needs separate V3 design and test strategy. |
| 3D classroom | PRODUCT_ROADMAP | Out of scope for first-run polish. |
| Music/focus mode | PRODUCT_ROADMAP | Out of scope and needs separate product decision. |
| Mobile redesign | PRODUCT_ROADMAP | User explicitly deprioritized mobile. |
| B2B/teacher dashboards | OUT_OF_SCOPE | Orka is currently personal learner-first. |

## Contract Safety

- No provider calls were added to the frontend.
- No client-side code execution was added.
- No fake weak areas, fake progress, or fake learning signals were added.
- Wolfram, YouTube, crypto, and provider behavior remain backend-contract driven.
- Study-focus selection is local UI framing, not a claim of persisted backend intelligence.

## Final Validation

| Command / Smoke | Result |
|---|---|
| `npm run build` | PASS |
| `npm run smoke:ui` | PASS |
| `npm run smoke:contracts` | PASS |
| `npm run typecheck` | PASS |
| `GET /health/live` on `http://127.0.0.1:5101` | PASS, 200 |
| `GET /health/ready` on `http://127.0.0.1:5101` | PASS, 200 |
| `GET /api/tools/capabilities` on `http://127.0.0.1:5101` | PASS, 200 |
| `GET /` on `http://127.0.0.1:3000` | PASS, 200 |
| `GET /login` on `http://127.0.0.1:3000` | PASS, 200 |

## Remaining Notes

| Note | Classification | Reason |
|---|---|---|
| Study focus is local UI guidance, not a full exam engine. | NON_BLOCKING_NOTE | Honest skeleton for orientation. |
| KPSS/YKS advanced planning belongs to V3. | PRODUCT_ROADMAP | Requires separate curriculum and scoring design. |
| Demo flow still benefits from a scripted video/pitch asset. | UX_POLISH | This pass prepares the product surface, not the video itself. |
