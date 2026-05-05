# Living Learning Organism Map

Phase: Investor-grade system optimization and due-diligence gate

## Loop Summary

Orka's current kernel behaves as an adaptive learning loop:

1. A learner acts through chat, quiz, source/wiki, IDE, flashcard, review, daily challenge or bookmark flows.
2. Backend services capture learning signals, tool telemetry, citations, mistakes, code errors, review outcomes and cost/provider metadata.
3. Durable state updates skill mastery, review pressure, flashcards, daily challenges, topic/session history, source/wiki memory and user-visible learning surfaces.
4. Tutor, DeepPlan and EducatorCore consume that state to adapt explanations, remediation, source priority and pedagogy.
5. Frontend surfaces the loop through chat metadata chips, citations, fallback notices, IDE learning summaries, learning panels and capability-driven tool status.

## Action To Adaptation Map

| User action | Signal capture | State update | Adaptive response | UI representation | Status |
|---|---|---|---|---|---|
| Tutor chat | metadata, citations, used tools, cost record, citation-missing signal | session/message/topic context, CostRecords, LearningSignals | Tutor respects source priority and provider fallbacks | chat messages, metadata chips, citations, fallback notices | `ALREADY_SAFE` |
| Wrong quiz answer | MistakeClassifier, QuizAttempt, LearningSignal | ReviewItem, SkillMastery pressure, XP/idempotency path | remediation and review pressure | quiz feedback, review due list | `ALREADY_SAFE` |
| IDE code run | compile/runtime/timeout result, code learning signal, tool telemetry | Redis Piston context, LearningSignal, session/topic context | Tutor explains error as pedagogy | IDE phase panels, safeTutorSummary, send-to-Tutor context | `ALREADY_SAFE` |
| Source upload/query | source chunks, citations, source ask evidence | Source/SourceChunk, topic grounding context | document-first Tutor/EducatorCore grounding | source list, citation chips, evidence panels | `ALREADY_SAFE` |
| Wiki activity | wiki blocks, glossary/mindmap/study-card states, learning signals | WikiPage/WikiBlock/topic context | wiki-backed explanations when docs are absent | wiki panels and source evidence strip | `ALREADY_SAFE` |
| Flashcard review | review result, optional learning signal | Flashcard, linked ReviewItem/SRS state | review due schedule changes | learning panel flashcards/review state | `ALREADY_SAFE` |
| Daily challenge | submission, score clamp, XP idempotency | DailyChallengeSubmission, XpEvent, weak-skill pressure | future challenge/review targeting | daily challenge panel and XP result | `ALREADY_SAFE` |
| Bookmark | bookmark create/list/delete metadata | Bookmark rows tied to topic/session/source/etc. | Tutor/plugin can recall saved context | bookmark panel and saved state | `ALREADY_SAFE` |
| YouTube pedagogy | transcript/degraded telemetry, TeachingMoveApplied | learning signal and teaching reference cache/context | teaching flow/examples/misconceptions, not factual authority | YouTube pedagogy tool chip/degraded state | `ALREADY_SAFE_WITH_PROVIDER_NOTE` |
| Provider tools | ToolTelemetryEvent, fallbackReason, source/citation metadata | telemetry/cost/fallback records | Tutor uses provider output only when available | provider tool chips and safe fallback notices | `ALREADY_SAFE` |

## Confirmed Core Gaps

No confirmed core learning-loop gap was found in this implementation pass. The remaining items are provisioning or product roadmap work:

- live Wolfram AppId proof
- live YouTube transcript provider proof
- staging Redis chaos test
- richer product analytics/teacher/multi-tenant surfaces

## Anti-Overclaim Notes

- Orka is a prototype with production-hardening gates, not a certified enterprise deployment.
- Local SQL/Redis runtime proof is required for this phase; production SLOs are not claimed.
- Provider public fallback behavior is implemented, but public provider rate limits remain an operational provisioning risk.
