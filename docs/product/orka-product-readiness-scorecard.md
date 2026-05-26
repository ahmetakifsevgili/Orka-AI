# Orka Product Readiness Scorecard

Status: Product Coherence Phase 11 implemented.

Scores are product-readiness signals for Phase 11 planning. They are not claims
about learning outcomes.

| Module | Backend readiness | Frontend readiness | UX clarity | Beta priority | Risk | Next action |
|---|---|---|---|---|---|---|
| Home / Mission Control | High | High | High | P0 | Needs browser QA on real local data. | Validate beta flow and tune copy density. |
| Tutor | High | Medium-high | Medium-high | P0 | Existing chat remains detailed and can still feel separate. | Add richer mission context rail later if needed. |
| Study Room | High | High | High | P0 | Checkpoint UX is compact; richer session interaction can wait. | Validate personal-study-room wording in user testing. |
| Exam War Room | High | High | High | P0 | Existing detailed practice panel is still separate from overview. | Merge deeper practice flows after beta feedback. |
| Sources / Wiki Pro | High | High | High | P0 | Overview is clear; dense legacy Wiki workspace still needs later UX polish. | Keep source/Wiki details behind safe handoffs. |
| Notebook Studio | High | High | Medium-high | P1 | Pro overview is preview-first; richer pack authoring can wait. | Validate pack recommendations with real artifacts. |
| Code Learning IDE | High | High | Medium-high | P1 | Runtime detail depends on backend availability. | Validate blocked/limited runtime states in browser QA. |
| Review / Quiz | High | Medium-high | Medium | P0 | Reused surface is functional but not fully redesigned. | Polish due-review queue after beta cutline. |
| Progress / Memory | High | Medium-high | Medium | P1 | Reuses dashboard progress mode. | Split durable memory view later if needed. |
| Safety / Privacy | High | High | High | P1 | Must keep smoke checks current as UI evolves. | Maintain no-raw-payload frontend smoke gates. |
| Release Harness | High | N/A | High | P0 internal | Validation exists but should remain developer-facing. | Keep release harness docs/scripts; do not make student screen. |

## Overall Readiness

- Backend/core Learning OS: feature-complete enough to drive frontend beta
  design.
- Frontend/product map: implemented as a beta product shell.
- Current frontend: now consumes Phase 1-9 contracts through typed API wrappers
  and compact Product Coherence panels.

## Highest Phase 11 Risks

- Building another dashboard instead of a true Mission Control.
- Letting Chat remain the only apparent product center.
- Hiding warnings behind module pages rather than showing them on Home.
- Treating Study Room as institutional classroom management.
- Showing raw debug/DTO payloads while iterating quickly.
- Claiming source-backed/exam alignment/success without verified evidence.

## Phase 11 Implementation Order Completed

1. Add typed API clients and frontend DTOs for Phase 1-9 contracts.
2. Build Home / Mission Control shell and module cards.
3. Wire primary handoffs to existing screens.
4. Add Study Room and Exam War Room product screens.
5. Add Source / Wiki Pro overview and Notebook/Code Pro entry states.
6. Add safety/blocked/thin-evidence state handling and smoke checks.
7. Polish visual system and responsive behavior through build/browser validation.
