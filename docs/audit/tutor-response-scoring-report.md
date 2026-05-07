# Tutor Response Scoring Report

Date: 2026-05-07

## Purpose

Tutor scoring exists to prevent Orka from sounding like a generic chat app after a diagnostic path exists.

The current deterministic scorer checks:
- topic relevance
- clear next step
- no fake persisted/source-backed claim without metadata
- no Visual Studio-first answer for coding topics
- coding practice should mention runnable practice, code, sandbox, or Orka IDE when appropriate
- no forbidden cross-domain leakage

## Product Boundary

Orka IDE belongs in Tutor/practice guidance, not in diagnostic quiz answers.

Correct:
- Tutor: "Bunu Orka IDE'de kucuk bir ornekle deneyebiliriz."
- IDE learning result: "Bu runtime hatasi bir pratik sinyali olabilir."

Wrong:
- Quiz option: "Orka IDE'de calistirirsan dogru cevap budur."
- Quiz explanation: "Bu soru Orka IDE odakli olcer."

This boundary is now represented in code:
- diagnostic quiz prompt forbids product UI labels
- diagnostic quiz quality gate rejects product-label leakage
- heavy quiz scorer treats it as critical fail

## Current Status

Status: PASS_WITH_NOTES

The scorer is deterministic. It is useful for regression protection, but it is not a full educational quality judge.

Future improvement:
- Add a gated LLM-as-judge only for offline QA, not default CI.
- Store scored Tutor response samples with evidence and screenshots for demo QA.

