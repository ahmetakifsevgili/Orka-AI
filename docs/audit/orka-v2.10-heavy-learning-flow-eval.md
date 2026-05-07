# Orka V2.10 Heavy Learning Flow Eval

Date: 2026-05-07
Branch: codex/heavy-learning-flow-eval-browser-qa
Purpose: measure the real learning pipeline, not only green tests.

## Executive Result

Status: PASS_WITH_NOTES

This gate adds a gated heavy evaluation layer for:

User request -> StudyIntentAnalyzer -> user approval -> Korteks research -> synthesis -> diagnostic quiz -> plan -> Tutor/Wiki/OrkaLM/IDE learning loop.

The heavy eval is not default CI. It runs with:

```powershell
$env:ORKA_RUN_HEAVY_EVAL="1"
$env:ORKA_HEAVY_FULL_FLOW_LIMIT="1"
python -m pytest contract_tests/heavy -q
```

Current final validation proof:
- 41 heavy eval scenarios passed with one live full-flow limit.
- 37 contract tests passed, 42 heavy/gated scenarios skipped by default.
- Targeted backend tests for intent, quiz quality, controller error safety, and lifecycle passed after fixes.
- Frontend `npm run build`, `npm run smoke:ui`, `npm run smoke:contracts`, and `npm run typecheck` passed.

## Research-Based Architecture Decision

The table/matrix idea must not become a hand-written encyclopedia of millions of topics.

Modern adaptive learning systems use:
- dynamic concept graphs / course maps
- prerequisite edges
- learner interaction events
- knowledge tracing
- diagnostic assessment metadata
- retrieval and synthesis over sources

So Orka should use tables as a persistent learning substrate, not as manually authored topic content.

Recommended interpretation:
- Store concepts extracted from Korteks/sources as graph nodes.
- Store prerequisite, misconception, practice, source, and assessment links as edges.
- Store user attempts, mistakes, reviews, IDE errors, bookmarks, and source actions as events.
- Let synthesis produce concept maps per topic/session.
- Let Tutor/plan read the graph and learner state.

Do not create a static table row for every possible topic.

Research references used:
- [Information Sciences 2025 TPR-KT](https://www.sciencedirect.com/science/article/pii/S0020025525007777)
- [Array 2025 CMDKT](https://doi.org/10.1016/j.array.2025.100523)
- [Scientific Reports 2025 personalized path generation](https://www.nature.com/articles/s41598-025-10497-x)
- [RPKT arXiv 2025](https://arxiv.org/abs/2508.11892)
- [GraphMASAL 2025](https://huggingface.co/papers/2511.11035)

## What Changed

### Intent and Start Gate

- Deterministic-first intent handling remains the first gate for common study requests.
- User approval remains mandatory before Korteks.
- Math probability + combination now preserves both concepts instead of collapsing to only combination.
- SQL optimization scenario now scores as a broader diagnostic and produces 24 questions, still inside 15-25.

### Quiz Quality

- Quiz fallback no longer uses Orka IDE/sandbox as a correct answer.
- Quiz fallback no longer leaks product labels into options or explanations.
- Live model quiz prompt now explicitly forbids Orka IDE/sandbox/product UI labels inside quiz questions, options, or correct answers.
- Quality gate rejects Orka IDE/sandbox leakage in diagnostic quiz output.
- Heavy eval marks Orka IDE/sandbox in quiz text as a critical fail.

Correct placement:
- Quiz measures concepts.
- Tutor and practice flow may mention Orka IDE/sandbox when guiding coding practice.

### Controller Safety

- Plan diagnostic start no longer branches on raw exception message for client output.
- Client receives a safe generic error if start request is invalid.

## Heavy Eval Coverage

Scenario groups:
1. Java algorithms
2. Java data structures + algorithms
3. SQL index/query optimization
4. KPSS paragraph speed
5. KPSS problem solving
6. C# async/await errors
7. Python pandas data analysis
8. Math probability/combination
9. IELTS speaking
10. Typo/noisy requests

Each scenario checks:
- intent/domain accuracy
- approved research intent quality
- no raw-message Korteks research
- quiz count 15-25
- no answer leakage
- no cross-domain leakage
- Tutor scoring rubric

## Known Notes

NON_BLOCKING_NOTE:
- The heavy eval is still partly deterministic. It measures quality guards and one limited live flow. Full provider-heavy live eval should stay gated to avoid cost/rate-limit damage.

PRODUCT_ROADMAP:
- Replace static-ish eval fixture expectations with learner graph snapshots as the system matures.
- Add persistent ConceptNode/ConceptEdge/AssessmentItem mapping if V3 moves to graph-backed adaptive planning.

BLOCKER:
- None identified in this pass after quiz product-label leak was fixed.
