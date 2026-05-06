# V3 Safe Pull-Forward Notes

## Summary

The legacy roadmap in `D:\Orka-legacy-dirty-20260506-015255\docs\roadmap\V1_TO_V3_PLAN.md` was reviewed as read-only reference. The current patch intentionally pulls forward only low-risk orientation pieces that make Orka easier to understand before V3.

This is not a V3 implementation phase. No full KPSS/YKS engine, 3D classroom, music/focus mode, subscription layer, mobile redesign, collaboration, teacher dashboard, or B2B platform work was added.

## Pulled Forward Now

| Item | Source idea | Current decision | What changed |
|---|---|---|---|
| First-run guidance | v1/v3 onboarding and demo clarity | SAFE_PULL_FORWARD | The in-app tour now explains Orka as a personal study path, not a generic chat box. |
| Study focus skeleton | Exam/curriculum roadmap | SAFE_PULL_FORWARD | Existing lightweight focus chips remain orientation-only. They shape starter prompts and do not claim exam mastery. |
| Learning loop visibility | SRS, flashcard, daily challenge, mistake loop | SAFE_PULL_FORWARD | Tour copy now connects Dashboard, Tutor, LearningPanel, Wiki/source, and IDE into one loop. |
| Code-error pedagogy | IDE/Piston roadmap | SAFE_PULL_FORWARD | Tour copy frames compile/runtime/timeout results as learning moments. |
| Demo narrative | Product readiness | SAFE_PULL_FORWARD | Demo docs explain what to show and what not to claim. |

## Deliberately Deferred

| Item | Classification | Reason |
|---|---|---|
| Full KPSS/YKS algorithm | PRODUCT_ROADMAP | Needs curriculum mapping, scoring rules, question taxonomy, and backend proof. |
| MEB/OpenSyllabus matching | PRODUCT_ROADMAP | Requires data source decision and correctness review. |
| 3D classroom / WebGL models | PRODUCT_ROADMAP | Valuable later, but not needed for current demo readiness. |
| Ambient music / focus rhythm | PRODUCT_ROADMAP | Needs separate UX and rights/licensing decisions. |
| Subscription/payment layer | PRODUCT_ROADMAP | Requires commercial model and payment compliance. |
| Teacher dashboard / B2B multi-tenant | OUT_OF_CURRENT_SCOPE | Current Orka is learner-first, not institution-first. |
| Collaborative learning / Yjs | PRODUCT_ROADMAP | Requires websocket/CRDT architecture and moderation model. |
| Community question bank | PRODUCT_ROADMAP | Needs quality, moderation, attribution, and abuse controls. |
| STT/pronunciation checker | PRODUCT_ROADMAP | Useful for language learning, but requires provider and privacy decisions. |

## Safe Claims

- Orka is a personal AI teacher and study coach.
- Orka organizes sources, code errors, flashcards, review, daily challenge, wiki, tools, and goals into a learning path.
- Orka shows real signals when the backend has real learner activity.
- Current study focus is a starter orientation, not a complete exam engine.

## Claims Not To Make Yet

- Full KPSS/YKS adaptive curriculum is complete.
- 3D classroom is implemented.
- Orka diagnoses health, stress, fatigue, ADHD, anxiety, or any medical condition.
- Orka is an institution/teacher dashboard product.
- Orka has certified production compliance or measured production SLOs.
