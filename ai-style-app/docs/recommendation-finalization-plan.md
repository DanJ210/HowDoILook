# Recommendation Decision and Feedback Implementation Plan

## Purpose

Capture the execution plan for hardening the recommendation-first pipeline. The automatic system-selected primary is the product result; user selection/finalization is retained only as temporary experimentation feedback and must not control primary delivery.

## Confirmed Baseline (Already Implemented)

- Recommendation-first entrypoint and async flow are in place.
- Queue split is in place (`analysis-jobs` ingress, `style-jobs` generation).
- Face analysis, telemetry persistence, recommendation fan-out, and Replicate webhook loop are in place.
- Recommendation page exists with upload, quality gating, polling, and telemetry display.
- Pre-MVP experimentation mode is active (100% traffic) and feedback collection exists.
- Schema drift and payload contract tests exist for both hair and beard Replicate models.

## Current Product Constraint (Important)

Telemetry is being collected, but there is no trained model or validated research mapping yet that can reliably convert telemetry into the best haircut/beard/color recommendation.

What this means right now:

- Recommendation quality is still in a cold-start phase.
- The system must prioritize learning-signal collection over "final AI intelligence" claims.
- Optional comparisons among generated variants are one source of explicit preference labels, subject to exposure and selection bias controls.

Working assumption for pre-MVP:

- Keep experimentation traffic configurable and record the applied policy for every exposure.
- Treat explicit user comparisons as subjective preference labels linked to telemetry and exposure metadata, not universal ground truth.
- Use these labels to build the training/evaluation dataset for future recommendation model tuning.

## Target Product Direction

Deliver one automatic primary result after a photo-only submission. Additional variants and feedback remain optional and secondary.

### Target User Journey

1. Upload and Analyze.
2. Receive the system-selected generated primary result.
3. Optionally compare experimental variants and submit preference feedback.
4. Continue using the automatic primary as the canonical result.

## Phase Plan

## Phase 1: Recommendation Decision Contract Hardening

Status (2026-08-01): partially implemented. Analysis jobs now persist the primary style ID, post ID, and generation job ID. Owner-scoped status exposes these fields and falls back to legacy discovery only for older rows. Per-variant decision snapshots and enqueue-time guardrail snapshots remain open.

Goal: make analysis-to-generation handoff explicit and auditable.

- Add an internal persisted decision contract per generated variant:
  - analysis job id
  - selected style id/name
  - score and reasons
  - resolved haircut/hair color/beard decisions
  - pipeline mode and guardrail snapshot
- Enforce enqueue-time invariants:
  - no beard edits unless male and beard suggestions enabled
  - no generation enqueue when quality/confidence guardrails fail
- Add clear error codes for decision contract failures.

Exit criteria:

- Every style generation job can be traced to a concrete recommendation decision.
- Invalid decisions fail fast and are visible in status payloads.

## Phase 2: Automatic Primary Completion and Publication

Status (2026-08-02): implemented. Status and public retrieval resolve the persisted primary, and the photo-only frontend completes on primary generation success without finalization.

Goal: make the system-selected primary the completed and publicly retrievable result without user finalization.

- Add recommendation session state model:
  - analyzing
  - generating
  - completed
  - failed
- Keep the existing finalize endpoint as temporary experimentation compatibility only.
- Persist optional preference/finalization linkage separately from the automatic primary.
- Update public feed/source logic to use the persisted primary generation.

Exit criteria:

- Public and status retrieval resolve the same automatic primary variant.
- Primary delivery never requires finalization or feedback.

## Phase 3: Webhook and Multi-Stage Idempotency Hardening

Goal: make stage completion deterministic under duplicate/out-of-order callbacks.

- Lock valid state transitions for stage and overall job.
- Ignore duplicate webhook events that would regress terminal states.
- Handle out-of-order events and missing prediction IDs safely.
- Add replay tests for webhook event duplication and ordering issues.

Exit criteria:

- Replaying identical callbacks yields stable final DB state.

## Phase 4: Automatic-Result UX Polish

Goal: refine the photo-only automatic-result experience without adding required choices.

- Keep portrait upload, progress, and the automatic primary result as the page hierarchy.
- Keep telemetry visible but secondary (collapsed technical section).
- Keep succeeded experimental variants available in an optional comparison section.
- Keep feedback optional and separate from automatic completion.
- Add share or visibility controls only after the primary result is available.

Exit criteria:

- Users can complete the full journey without reading debug telemetry.
- The primary result remains obvious while optional comparison and sharing stay secondary.

## Phase 5: Telemetry v3 and Stage Interface Evolution

Goal: raise telemetry quality for research-grade recommendation tuning.

- Introduce v3 feature vector shape with face detection summary and richer stage metadata.
- Support v2/v3 parsing compatibility in status APIs during migration.
- Separate detector/landmark/region stages behind interfaces and DI.
- Add threshold gates per stage (configurable per environment).

Exit criteria:

- New jobs emit v3 telemetry.
- Existing records continue to work in status responses.

## Phase 6: Research Mapping Extraction

Goal: make ranking data-driven and explainable.

- Move scoring to a dedicated recommendation mapping module.
- Adopt candidate catalog-based constraints and compatibility inputs.
- Persist contribution breakdown for auditability and tuning.

Exit criteria:

- Deterministic ranking for fixed telemetry inputs.
- Human-readable reasons trace back to weighted contributions.

## Data Learning Loop (Parallel Track)

Goal: ensure the data collected now is actually usable for model training later.

- Define and lock a training row contract per recommendation session:
  - telemetry snapshot version
  - candidate variants shown
  - variant selected rank
  - user/context guardrails (gender, preferences)
  - outcome quality flags (job success/failure)
- Persist enough linkage to reconstruct the full decision set shown to the user.
- Add export validation checks so incomplete/ambiguous label rows are excluded.
- Track coverage metrics:
  - ranked-session rate
  - per-style selection counts
  - per-face-shape sample distribution

Exit criteria:

- Exported dataset has consistent feature/label rows suitable for offline training.
- Team can quantify whether enough labeled data exists before attempting model training.

## Phase 7: Reliability, Tests, and Rollout Controls

Goal: make rollout safe and measurable.

- Extend test matrix:
  - deterministic scoring tests
  - no-face/multi-face/low-confidence cases
  - finalize idempotency and ownership checks
  - webhook replay/out-of-order behavior
- Expand operational metrics and alerts for:
  - analysis success/failure rates
  - generation submission failures
  - webhook completion latency
  - finalize conversion rate
- Define ramp-down path for experimentation traffic post-MVP.

Exit criteria:

- CI blocks contract and flow regressions.
- Operators can tune rollout without code changes.

## Recommended PR Sequence

1. Phase 1 (decision contract hardening).
2. Phase 2 (automatic primary completion + publication).
3. Phase 3 (webhook idempotency hardening).
4. Phase 4 (UI/UX staged rebuild).
5. Phase 5 (telemetry v3 + stage interface migration).
6. Phase 6 (research mapping extraction).
7. Phase 7 (test and rollout hardening).

## Notes

- Keep recommendation-first as canonical entrypoint.
- Keep beard suggestions optional and male-only guardrail enforced.
- Do not gate frontend simplification on telemetry v3; automatic primary completion should ship earlier.
- Do not present current recommendations as model-trained personalization; present them as iterative guidance while learning data is collected.
