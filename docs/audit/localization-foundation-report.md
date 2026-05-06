# Localization Foundation Report

## Scope

This phase implements first-wave safe global UI language support. It is not a complete translation of every backend-generated sentence.

## Implemented Languages

| Locale | Language |
|---|---|
| `tr` | Turkish |
| `en` | English |
| `es` | Spanish |
| `fr` | French |
| `de` | German |
| `pt-BR` | Portuguese (Brazil) |
| `it` | Italian |
| `id` | Indonesian |
| `nl` | Dutch |
| `pl` | Polish |

## Architecture

| File | Purpose |
|---|---|
| `Orka-Front/src/i18n/languages.ts` | Locale list and normalization. |
| `Orka-Front/src/i18n/messages.ts` | UI message dictionaries with fallback. |
| `Orka-Front/src/contexts/LanguageContext.tsx` | React context, localStorage persistence, safe fallback. |

Fallback order:

1. selected locale
2. English
3. Turkish
4. key name

## Localized First-Wave Surfaces

| Surface | Status |
|---|---|
| Landing/homepage hero, nav, CTAs | IMPLEMENTED |
| Public language selector | IMPLEMENTED |
| App sidebar language selector | IMPLEMENTED |
| Settings language selector | IMPLEMENTED |
| Navigation labels | IMPLEMENTED |
| Dashboard study focus/next-step copy | IMPLEMENTED |
| Onboarding tour | IMPLEMENTED |
| Tutor welcome starter prompts | IMPLEMENTED |
| Audio Learning player labels | IMPLEMENTED |
| Smoke guard for language foundation | IMPLEMENTED |

## Not Translated By Design

- User-generated content.
- Backend-generated Tutor answers.
- Uploaded source content.
- Provider raw data.
- Code output, stderr, stack traces, and citations.
- API enum values used in contracts.

## Roadmap

| Item | Classification |
|---|---|
| Full app-wide copy extraction | LOCALIZATION_ROADMAP |
| Backend persistent locale preference normalization | LOCALIZATION_ROADMAP |
| CJK language QA | LOCALIZATION_ROADMAP |
| RTL language QA | LOCALIZATION_ROADMAP |
| Complex-script language QA | LOCALIZATION_ROADMAP |
