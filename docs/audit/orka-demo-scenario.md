# Orka Demo Scenario

## Safe Narrative

Orka is an Organisation Knowledge Agent for personal learning. It organizes sources, mistakes, code errors, review, flashcards, daily challenges, wiki memory, provider tools, and goals into a personal study path.

Safe claim:

> Orka helps a learner start small, grounds answers when context exists, records learning signals where backend support exists, and turns mistakes or code errors into the next study step.

Do not claim:

- Full KPSS/YKS exam engine is complete.
- Orka diagnoses stress, anxiety, fatigue, ADHD, or any health condition.
- YouTube is factual authority by default.
- Crypto output is financial advice.
- Provider data is always available.
- Enterprise compliance, SOC2, HIPAA, GDPR, or production SLOs are certified.

## 2-Minute Demo Script

1. Open Orka on `Bugun`.
2. If the intro tour appears, use it as the opening explanation: Orka turns study actions into the next step.
3. Say: "Orka does not start as a blank chat box; it asks what the learner should do today."
4. Show `Hedef odagi`.
5. Pick `KPSS` or `Yazilim`, depending on the audience.
6. Click `Tutor ile devam et`.
7. Use a starter prompt such as `Konu ogren`.
8. Show that Tutor can answer and, when metadata exists, show basis/tool/citation/fallback/learning trace.
9. Close with: "As the learner solves, reviews, codes, uploads sources, and bookmarks, Orka turns those actions into the next step."

## 4-Minute Technical Demo Script

1. Dashboard:
   - Show Today's Focus.
   - Show the no-fake-data empty state if the user has no history.
   - Show that study focus is orientation only, not a fake exam algorithm.

2. Tutor:
   - Start from a starter prompt.
   - Mention Plan Mode and Korteks are backend-backed paths.
   - Show metadata/learning trace only if backend returns it.

3. IDE:
   - Open IDE from dashboard or nav.
   - Run a tiny safe code example or show the provider/sandbox state.
   - Explain compile/runtime/timeout errors are learning moments, not app crashes.
   - Send result to Tutor if available.

4. Practice:
   - Open LearningPanel.
   - Show flashcards, due review, daily challenge, bookmarks.
   - If empty, say: "This stays empty until real activity creates data."

5. Source/Wiki:
   - Show Wiki/source areas if the demo user has data.
   - Explain source citations are distinct from provider citations and YouTube pedagogy.

## Backup Plan If Live Data Is Unavailable

- Use dashboard empty state and explain it is intentionally honest.
- Use the refreshed intro tour as the short product explanation.
- Use starter prompts without claiming past personalization.
- Use IDE provider_missing/blocked/timeout state as a safe learning-state example.
- Use capability endpoint/docs to explain provider gates.
- Do not fake a successful provider/tool result.

## V3 Preview Boundary

Safe to mention:

- Study focus is a first step toward future exam-aware learning.
- Code-error learning, review, flashcards, daily challenge, bookmarks, source/wiki grounding, and Tutor already exist as the current learning loop.

Do not claim yet:

- Full KPSS/YKS algorithm.
- 3D classroom.
- Music/focus mode.
- Subscription/payment layer.
- Teacher dashboard or institution product.

## Recommended Demo Data

Use a disposable demo user with:

- one topic
- one small source document
- one flashcard
- one bookmark
- one IDE compile/runtime example
- one Tutor message with metadata, if available

Do not use real private user data.
