# MVP Best-Look Plan (2 Weeks)

## Product North Star

A user uploads one photo and receives one automatically selected best-look recommendation with a generated preview.

The system chooses the recommendation. The user does not select a requested hairstyle before generation.

During the learning phase, the system may generate 3 to 4 candidates and optionally ask which result the user prefers. That feedback is training/evaluation data; it is not required to receive the automatic primary result.

## Definition of Done (MVP)

1. User can upload and analyze one photo.
2. System automatically ranks candidates and selects one primary recommendation.
3. System generates and returns the primary recommendation preview.
4. The primary result is available without requiring style selection, preferences, or finalization.
5. Telemetry, the system decision, generation outcome, and optional user feedback can be exported for future evaluation and model development.

## Must Build Now

## 1) Automatic Best-Decision Contract

Implementation status (2026-08-01): backend persistence, owner-scoped status retrieval, and public/feed lookup alignment are implemented. Frontend completion semantics remain open.

- Persist the system-selected primary recommendation and its generation job explicitly.
- Keep the automatic decision traceable to the telemetry snapshot and ranking inputs used at the time.
- Keep optional user preference labels separate from the system-selected primary result.

MVP acceptance:

- The primary recommendation and generated result are always retrievable by session/job ID.
- Status, public retrieval, and frontend state resolve to the same primary generation job.
- No user style selection is needed to complete the session.

## 2) Clear Session States

- Keep status states simple for UI:
  - `analyzing`
  - `generating`
  - `completed`
  - `failed`

Experimental feedback may add a secondary `ready_for_feedback` state, but it must not replace or block `completed` for the primary result.

MVP acceptance:

- Frontend can render one unambiguous state at any time.

## 3) Photo-Only Primary UX

- Keep one page if needed, but simplify hierarchy:
  - top: portrait upload and current status
  - middle: automatic best recommendation and generated primary result
  - bottom: optional feedback or experimental comparison when enabled
- Move telemetry/debug content behind a collapsed section.
- Do not add a hairstyle picker.
- Do not require gender, maintenance, vibe, color, beard, or other preferences in the primary flow without an explicit product change.

MVP acceptance:

- A first-time user can receive the automatic result by submitting only a valid portrait.
- Feedback controls are clearly optional.

## 4) Label Capture Integrity

- For each completed session, persist:
  - telemetry snapshot reference/version
  - system-selected recommendation and primary generation job ID
  - generation outcome
  - list of experimental variant job IDs shown, if any
  - optional user preference/ranking label, if provided
- Ensure export path can produce clean feature-label rows.

MVP acceptance:

- Completed sessions produce valid system-decision rows even without feedback.
- Feedback-bearing sessions produce unambiguous preference-label rows.

## 5) Controlled Exploration and Telemetry Coverage

The automatic primary result must always be delivered first. For a configurable minority of eligible sessions, generate additional candidates and offer an optional comparison after the primary result is available.

Exploration rules:

- Keep the current top-ranked style as the automatic primary result.
- Sample experimental challengers from the eligible catalog instead of always taking the next deterministic heuristic ranks.
- Record the candidate-selection probability (propensity), experiment version, candidate pool, shown order, and ranking score for every displayed style.
- Prefer pairwise comparisons with `Neither` and `No preference` outcomes over mandatory 1-to-5 ratings.
- Randomize presentation order so image position does not become a style label.
- Keep model version, prompt template, aspect ratio, and other generation settings comparable within a session.
- Record generation quality/failure separately so rendering artifacts are not learned as hairstyle preference.

Maintain two related exports:

1. **Exposure/outcome rows:** every completed analysis session, including sessions without feedback.
2. **Preference-label rows:** only comparisons with complete telemetry, displayed-candidate linkage, and an explicit outcome.

Coverage rules:

- Measure support over continuous telemetry feature ranges, not only coarse face-shape labels.
- Monitor sample counts and preference outcomes by style, telemetry range, analysis confidence, and image-quality range.
- Ensure every eligible style has a nonzero controlled chance of exposure where it is safe to show.
- Detect sparse or out-of-distribution telemetry regions and fall back to the validated heuristic instead of claiming a confident personalized result.
- Do not promote a learned ranker until offline evaluation beats the heuristic on held-out users and recent time-based data without materially degrading underrepresented telemetry regions.
- Treat "all face types" as a coverage objective, not a guaranteed state. Expand the catalog or model only when measured support is sufficient.

MVP acceptance:

- The primary result never waits for optional exploration or feedback.
- Every experimental exposure can be reconstructed with its telemetry snapshot, candidate pool, selection probability, shown order, generation metadata, and outcome.
- Missing feedback is represented as missing, never as rejection.
- Coverage reports identify telemetry/style regions that do not yet have enough evidence for model training or evaluation.

## 6) Reliability Guardrails

- Preserve existing schema drift tests for Replicate models.
- Add focused tests for automatic primary behavior:
  - primary recommendation is generated without user selection
  - status polling continues through primary generation
  - experimental feedback cannot replace the primary result unintentionally
  - all-generation failure produces a stable actionable status

MVP acceptance:

- CI fails on contract, primary-result, or polling regressions.

## Defer Until After MVP

- Telemetry v3 schema redesign.
- Full ONNX stage interface refactor.
- Advanced research mapping/scoring module extraction.
- Multi-step polished redesign with share/gallery enhancements.
- Complex experimentation ramp controls beyond current pre-MVP defaults.

## Remove or Stop Doing Now

- Expanding debug telemetry UX in primary user flow.
- Building a user-directed hairstyle picker.
- Making optional preference controls part of the required path.
- Building features that do not improve automatic best-result completion or learning-data quality.
- Refactors that do not reduce production risk or improve the automatic recommendation flow.

## 2-Week PR Plan

## PR 1 (Days 1-3): Automatic Primary Backend Contract

Status: complete. The database now persists `primary_style_id`, `primary_style_item_id`, and `primary_generation_job_id`; status and public/feed retrieval resolve those IDs and use legacy discovery only for older rows.

- Persist the system-selected primary recommendation and generation job explicitly.
- Separate automatic primary semantics from optional user preference labels.
- Add backend tests for primary-result retrieval and ownership.

Exit:

- API exposes one unambiguous automatic primary result with tests passing.

## PR 2 (Days 4-6): Frontend Automatic-Result UX

Status: complete. The frontend accepts a portrait-only submission, polls through primary generation, completes without finalization, and keeps experiments, feedback, and telemetry secondary.

- Simplify recommendations page to portrait upload, progress, and the automatic primary result.
- Keep telemetry in collapsed "technical details" section.
- Keep experimental comparison and feedback secondary and optional.
- Continue polling through downstream primary generation.

Exit:

- User receives the automatic best result after submitting only a portrait.

## PR 3 (Days 7-9): Decision and Label Data Integrity

- Ensure telemetry, automatic decision, primary result, and shown experimental variants are linked.
- Validate analytics/export distinguishes system decisions from optional user preference labels.
- Add exposure/outcome rows for all completed sessions, not only sessions with finalization or feedback.
- Persist experiment version, candidate pool, shown order, and candidate-selection propensities.
- Add coverage reporting by style and telemetry range.
- Add data integrity checks/tests.

Exit:

- Decision, exposure, and preference datasets are complete, unambiguous, and suitable for bias-aware offline evaluation.

## PR 4 (Days 10-12): Reliability and Hardening

- Add/expand tests for webhook, automatic primary-result, and optional feedback interplay.
- Tighten failure messaging for failed generation sessions.
- Run focused regression suite.

Exit:

- Stable end-to-end flow under normal retry/duplicate conditions.

## PR 5 (Days 13-14): MVP Polish and Release Checklist

- UX copy cleanup for clarity.
- Docs update for new canonical flow and endpoint usage.
- Final smoke test pass across upload -> automatic recommendation -> generated primary result.

Exit:

- MVP is release-ready for controlled user feedback.

## Weekly Checkpoint Metrics

Track only what proves MVP value:

1. Recommendation session completion rate.
2. Automatic primary generation success rate.
3. Median time from upload to primary result.
4. Optional feedback rate and valid labeled-row rate.
5. Eligible-style exposure coverage across supported telemetry ranges.
6. Sparse/out-of-distribution session rate.

If these improve, the MVP is moving in the right direction.
