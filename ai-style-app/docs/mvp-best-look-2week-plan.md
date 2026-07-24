# MVP Best-Look Plan (2 Weeks)

## Product North Star

A user uploads one photo and gets one final best-look result they choose and can keep/share.

## Definition of Done (MVP)

1. User can upload and analyze one photo.
2. System generates 3 to 4 recommendation variants.
3. User selects one final winner.
4. The selected winner is saved as the canonical best look for that session.
5. Selection is captured as training label data tied to telemetry.

## Must Build Now

## 1) Final Selection Flow

- Add or finalize a single action for choosing the winner variant for a recommendation session.
- Persist `selectedGenerationJobId` (or equivalent canonical final selection field).
- Make selection owner-scoped and idempotent.

MVP acceptance:

- A selected winner is always retrievable by session/job id.
- Repeating the same selection request does not create duplicate effects.

## 2) Clear Session States

- Keep status states simple for UI:
  - `analyzing`
  - `generating`
  - `ready_for_selection`
  - `completed`
  - `failed`

MVP acceptance:

- Frontend can render one unambiguous state at any time.

## 3) Minimal UX for Decision

- Keep one page if needed, but simplify hierarchy:
  - top: upload and current status
  - middle: generated variants
  - bottom: single "Select Final Look" action
- Move telemetry/debug content behind a collapsed section.

MVP acceptance:

- A first-time user can finish flow without reading telemetry/debug details.

## 4) Label Capture Integrity

- For each completed session, persist:
  - telemetry snapshot reference/version
  - list of shown variant job ids
  - selected winner id
  - optional user ranking (if provided)
- Ensure export path can produce clean feature-label rows.

MVP acceptance:

- Completed sessions with final selection produce valid training rows.

## 5) Reliability Guardrails

- Preserve existing schema drift tests for Replicate models.
- Add focused tests for final selection behavior:
  - owner-only selection
  - idempotent selection
  - cannot select unknown/non-session variant

MVP acceptance:

- CI fails on contract or final-selection regressions.

## Defer Until After MVP

- Telemetry v3 schema redesign.
- Full ONNX stage interface refactor.
- Advanced research mapping/scoring module extraction.
- Multi-step polished redesign with share/gallery enhancements.
- Complex experimentation ramp controls beyond current pre-MVP defaults.

## Remove or Stop Doing Now

- Expanding debug telemetry UX in primary user flow.
- Building features that do not improve "select one best look" completion.
- Refactors that do not reduce production risk or unlock final selection.

## 2-Week PR Plan

## PR 1 (Days 1-3): Final Selection Backend Contract

- Add finalize/select endpoint if missing, or tighten existing one.
- Persist canonical selected winner per recommendation session.
- Add ownership + idempotency checks.
- Add backend tests for selection edge cases.

Exit:

- API supports robust final selection with tests passing.

## PR 2 (Days 4-6): Frontend MVP Selection UX

- Simplify recommendations page to prioritize variant comparison + final selection.
- Keep telemetry in collapsed "technical details" section.
- Add clear completion state after selection.

Exit:

- User can select winner in one obvious action.

## PR 3 (Days 7-9): Label Data Integrity

- Ensure selected winner and shown variants are linked for export/training.
- Validate analytics/export includes the final selection label row.
- Add data integrity checks/tests.

Exit:

- Training dataset rows are complete and unambiguous.

## PR 4 (Days 10-12): Reliability and Hardening

- Add/expand tests for webhook and final-selection interplay.
- Tighten failure messaging for failed generation sessions.
- Run focused regression suite.

Exit:

- Stable end-to-end flow under normal retry/duplicate conditions.

## PR 5 (Days 13-14): MVP Polish and Release Checklist

- UX copy cleanup for clarity.
- Docs update for new canonical flow and endpoint usage.
- Final smoke test pass across upload -> generate -> select.

Exit:

- MVP is release-ready for controlled user feedback.

## Weekly Checkpoint Metrics

Track only what proves MVP value:

1. Recommendation session completion rate.
2. Final selection rate.
3. Median time from upload to final selection.
4. Valid labeled-row rate for completed sessions.

If these improve, the MVP is moving in the right direction.
