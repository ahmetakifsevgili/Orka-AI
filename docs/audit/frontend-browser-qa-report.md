# Frontend Browser QA Report

Date: 2026-05-07
Tool: Codex in-app browser / Browser Use

## Flow Tested

Runtime flow was exercised manually through the in-app browser:

1. Register/login with a unique user.
2. Open app dashboard.
3. Verify OrkaLM appears in the sidebar.
4. Open Tutor.
5. Enable Plan Mode.
6. Submit: `java programlamada algoritmalar calismak istiyorum`.
7. Verify intent card appears before research.
8. Approve research.
9. Verify staged planning state appears.
10. Verify quiz appears in one card.

## Evidence

Observed:
- Intent card appeared before Korteks:
  - main: Java programlama
  - focus: algoritmalar
  - research intent: Java programming algorithms learning path
- Approval action was required.
- Staged planning UI showed intent/research/synthesis/quiz/plan style phases.
- Quiz rendered as a card instead of chat command leakage.
- No visible `Quiz Cevabim` or `[SKIP_QUIZ]` leakage in this inspected flow.

Issue caught by browser QA:
- Java fallback quiz still contained async/IDE-flavored wording in an earlier runtime snapshot.

Fix applied:
- fallback quiz templates were made domain-neutral
- Orka IDE/sandbox was removed from quiz answers/explanations
- quality gate now rejects quiz product-label leakage

## Remaining Browser QA Note

Post-fix browser rerun:
- `http://127.0.0.1:3000/app` loaded successfully.
- Plan Mode produced the intent approval card before Korteks.
- Approved research intent was `Java programming algorithms learning path`.
- No app console errors were observed during the inspected intent/research stage.
- No visible Orka IDE/sandbox product-label leakage appeared in the inspected staged research text.

Remaining browser note:
- The live Korteks research stage did not reach the quiz card within the short browser inspection window. Quiz wording is still protected by backend unit tests and the gated heavy eval, where Orka IDE/sandbox in quiz text is now a critical fail.

Status: PASS_WITH_NOTE
