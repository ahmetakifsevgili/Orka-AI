# Audio Learning Core + Global Language Foundation

## Executive Summary

This phase builds on `85cb94d Polish onboarding intro and V3 roadmap framing` and keeps the scope demo-safe. Orka now has a first-wave global UI language foundation and the existing audio/classroom capability is documented as AI Audio Learning, not a live classroom.

## Repo Baseline

| Item | Result |
|---|---|
| Active repo | `D:\Orka` |
| Starting branch | `feature/first-run-study-journey-demo-polish` |
| Phase branch | `feature/audio-learning-language-demo-readiness` |
| Starting commit | `85cb94d Polish onboarding intro and V3 roadmap framing` |
| First-run docs present | PASS |
| Onboarding patch present | PASS |

## First-Run Integration

| Check | Result |
|---|---|
| `docs/audit/first-run-study-journey-demo-polish.md` | PRESENT |
| `docs/audit/orka-demo-scenario.md` | PRESENT, updated with audio branch |
| `docs/audit/v3-safe-pull-forward-notes.md` | PRESENT |
| `PremiumOnboardingTour` | PRESENT, now language-aware |
| Full KPSS/YKS claim avoided | PASS |
| Fake personalization avoided | PASS |

## What Changed

- Added a 10-language first-wave UI foundation.
- Turkish remains the default language.
- Landing page now has a public language selector and translated hero/nav/CTA copy.
- App shell/sidebar has a language selector.
- Settings exposes the same language options.
- Dashboard study focus and next-step copy use localized labels where practical.
- Tutor welcome starter prompts are language-aware.
- Audio Learning UI labels are localized and framed as AI audio lesson, not live classroom.

## Runtime Notes

Backend-generated Tutor prose, user content, source content, provider data, code output, citations, and raw stack traces are intentionally not machine-translated on the frontend. Starter prompts guide Tutor into the selected language where frontend controls the prompt.

## Validation

| Check | Result |
|---|---|
| `npm run build` | PASS |
| `npm run smoke:ui` | PASS |
| `npm run smoke:contracts` | PASS |
| `npm run typecheck` | PASS |
| `GET /health/live` | PASS, 200 |
| `GET /health/ready` | PASS, 200 |
| `GET /api/tools/capabilities` | PASS, 200 |
| `GET /` frontend | PASS, 200 |
| `GET /login` frontend | PASS, 200 |

Backend code was not changed, so .NET tests were not rerun for this UI/docs-only phase.

## Remaining Notes

| Note | Classification |
|---|---|
| UI localization is first-wave/high-visibility, not a complete string extraction of the entire product. | LOCALIZATION_ROADMAP |
| Tutor answers are guided by starter prompt language but not globally forced by a backend language preference. | NON_BLOCKING_NOTE |
| Multilingual backend TTS voices are not claimed as fully proven. | AUDIO_ROADMAP |
| CJK/RTL/complex-script language support remains future QA work. | LOCALIZATION_ROADMAP |
