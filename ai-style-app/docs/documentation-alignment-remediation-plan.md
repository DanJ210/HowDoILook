# Documentation and MVP Alignment Remediation Plan

> **Status:** Temporary working document
> **Created:** 2026-08-01
> **Remove when:** every required item below is complete, permanent documents reflect the implemented behavior, and the final validation checklist passes.

## Purpose

Track the gaps found while reviewing the repository READMEs, `ai-style-app/docs`, current runtime code, and configuration. This document is an execution aid, not a permanent product specification.

## Product North Star

The target product is recommendation-first and automatic:

1. A user uploads one portrait.
2. The system analyzes the portrait and uses available evidence to choose its best recommendation.
3. The system generates and returns a preview of that recommendation as the primary result.
4. The user does not request or select a hairstyle before generation.
5. Telemetry and optional feedback are retained to improve future recommendation quality.

Users do not directly create style-generation jobs. Generation remains downstream system behavior after the recommendation decision.

## Confirmed Product Direction (2026-08-01)

The product owner confirmed:

> A user posts one photo and receives the system's automatic "best" recommendation based on available data. The user does not select the hairstyle they want.

The system currently uses heuristic telemetry mapping rather than a trained recommendation model. Do not describe the present result as learned or data-trained personalization. Telemetry collected now is intended to support evaluation and a future data-driven ranking model.

Temporary learning mode may still generate 3 to 4 candidates and ask the user for optional ranking or preference feedback. That interaction exists to collect labels; it is not the target product decision flow and must not require the user to choose a requested hairstyle before receiving the automatic primary result.

Top-level alignment assessment:

- The upload, analysis, async queue, worker, Replicate, webhook, and status architecture remains aligned.
- Automatic recommendation ranking and downstream generation remain aligned conceptually.
- The active frontend is broader than the confirmed flow because it asks for gender, maintenance, vibe, hair-color, and beard preferences. The target first action should require only a portrait.
- The 3-to-4-variant final-selection experience should be treated as temporary data collection, not the long-term product north star.
- The current short-style and beard-heavy catalog needs evaluation against the intended launch audience before recommendation quality claims are made.

Remaining product confirmation:

- [ ] Confirm whether the launch catalog and audience are women only or broader.
- [x] Confirm that the system chooses the hairstyle automatically; there is no exact-hairstyle picker in the primary flow.
- [x] Confirm that face telemetry supports automatic ranking now and potentially a data-driven model later.

## Source-of-Truth Order During Remediation

When documents conflict, use this order until this plan is removed:

1. `docs/mvp-best-look-2week-plan.md` for MVP product scope and deferrals.
2. Implemented code and tests for current runtime behavior.
3. `docs/api-contracts.md` for the intended external contract, after checking it against code.
4. `docs/architecture.md` for current component and data flow, after checking it against code.
5. `docs/face-analysis-recommendation-spec.md` for analysis requirements and guardrails.
6. `docs/production-blueprint.md` only as a draft production direction; its commands are not currently deployment-ready.

Use this file to track known differences instead of silently choosing whichever document is newest.

## Decisions Required

Resolve these before updating permanent documents that depend on them.

- [x] **Primary user journey:** one portrait -> automatic system recommendation -> generated primary result; no requested-style selection.
- [x] **Primary result and experimentation:** optional user ranking remains label data only and cannot replace the system-selected primary result.
- [ ] **Public visibility:** decide when the automatic primary result becomes public and keep experimental candidates private by default.
- [ ] **Multiple ONNX detections:** decide how to distinguish false duplicate boxes from genuinely distinct faces. The selected rule must preserve the single-person upload guardrail.
- [ ] **Public URL configuration:** decide whether to split webhook callback base and public source-image base into separate settings. Preferred direction: `Replicate:WebhookBaseUrl` for callbacks and a separately named public asset/API base for local URL rewriting.
- [ ] Record each decision in `docs/architecture.md`, `docs/api-contracts.md`, and the analysis specification where applicable.

## Workstream 1: Session Completion and Polling

**Problem:** analysis is marked `Succeeded` before generation jobs are enqueued. The frontend previously stopped polling on terminal analysis status, so it could miss variant completion and `GENERATION_ALL_VARIANTS_FAILED`.

- [x] Define a session-level completion rule independent of analysis-only status.
- [x] Keep polling while expected variants are queued or processing.
- [x] Return the automatic primary result as soon as its required generation reaches a terminal success state; do not require user finalization.
- [x] Define fallback behavior when the primary generation fails but an experimental candidate succeeds: do not promote the experiment automatically.
- [x] Surface all-terminal failure as `GENERATION_ALL_VARIANTS_FAILED` without requiring a manual refresh.
- [x] Add frontend tests for analysis succeeded while variants are still processing.
- [x] Add frontend tests for delayed all-variants-failed detection.
- [x] Update `docs/api-contracts.md` with exact status and polling semantics.
- [x] Update `docs/getting-started.md` so its smoke test waits for session readiness, not merely terminal analysis status.

Exit criteria:

- The UI cannot stop polling before downstream generation reaches a user-actionable state.
- The documented smoke test observes the same completion rule as the frontend.

## Workstream 2: Automatic Best Result and Publication

**Problem:** current plans and implementation treat a user-selected generation as the canonical final result, while the confirmed product requires the system-selected primary recommendation to be the product result. User ranking should remain optional learning data unless explicitly changed later.

- [x] Define and persist the system-selected primary recommendation, post, and generation job unambiguously.
- [x] Keep `SelectedGenerationJobId` as legacy experimentation/finalization label metadata; it is not product-result state.
- [x] Make status and public/feed retrieval resolve the automatic primary generation consistently.
- [x] Keep experimental candidates private unless a separate research experience intentionally exposes them.
- [x] Ensure optional ranking/favorite feedback never blocks delivery of the primary result.
- [x] Add service tests proving owner-scoped automatic primary status is returned without finalization.
- [x] Correct `docs/api-contracts.md`, `docs/architecture.md`, and `docs/face-analysis-recommendation-spec.md` to distinguish primary output from optional feedback labels.
- [x] Update `docs/recommendation-finalization-plan.md` to mark user finalization as temporary learning-loop functionality rather than the target product flow.

Exit criteria:

- Status and public retrieval resolve to the system-selected primary generation without user selection.
- Analytics can connect telemetry, the system decision, candidates shown during experiments, and any optional user preference label without conflating them.

## Workstream 3: Bias-Aware Learning Data

**Problem:** current experimentation generates the deterministic top four heuristic candidates. Feedback is stored through `recommendation_feedback.selected_style_id`, and analytics exports only sessions with `SelectedGenerationJobId`. There is no persisted candidate pool, randomized exposure probability, experiment version, shown order, or no-feedback exposure row. Training directly from this data would reinforce the current heuristic and underrepresent styles or telemetry regions it rarely selects.

- [ ] Define immutable exposure records linked to analysis job, telemetry schema/version, system decision, candidate pool, displayed candidates, shown order, ranking scores, and experiment version.
- [ ] Persist each experimental candidate's selection probability (propensity) so offline evaluation can correct for nonuniform exposure.
- [ ] Sample challengers from eligible styles under controlled exploration instead of always taking ranks 2 through 4.
- [ ] Keep the top heuristic candidate as the automatic primary while exploration is collecting evidence.
- [ ] Add optional pairwise feedback with `left`, `right`, `left preferred`, `right preferred`, `neither`, and `no preference` semantics.
- [ ] Randomize comparison order and retain the presented order in the exposure record.
- [ ] Record generation model/version, prompt-template version, source image, aspect ratio, status, and quality outcome for every displayed generation.
- [ ] Store system-selected primary, experimental exposure, and user preference as distinct concepts; do not overload finalization fields.
- [ ] Export exposure/outcome rows for all completed sessions, including sessions with no feedback.
- [ ] Export preference-label rows only when telemetry, both compared generations, exposure metadata, and explicit outcome are complete.
- [ ] Treat missing feedback as missing, never as rejection.
- [ ] Add coverage reports over continuous telemetry ranges, style exposures, comparison outcomes, analysis confidence, and image quality.
- [ ] Define sparse/out-of-distribution thresholds that retain the validated heuristic rather than issuing unsupported learned recommendations.
- [ ] Split offline evaluation by user and time; compare learned rankers against the existing heuristic before rollout.
- [ ] Start exploration at a controlled traffic percentage and increase it only after generation quality, cost, and feedback-completion checks pass.
- [ ] Add migration, service, API, export, and integrity tests for the new exposure and preference contracts.

Exit criteria:

- Every displayed experimental candidate has reconstructable eligibility, probability, position, generation metadata, telemetry, and outcome.
- Coverage reporting identifies unsupported telemetry/style regions instead of implying universal performance.
- A learned ranker cannot be promoted unless it beats the heuristic on held-out data and does not materially regress sparse telemetry regions.

## Workstream 4: Single-Face Guardrail

**Problem:** the specification rejects multi-face uploads, but the ONNX detector currently replaces every multiple-detection result with heuristic fallback output. A genuine multi-face image can therefore bypass the guardrail.

- [ ] Implement the multiple-detection decision from **Decisions Required**.
- [ ] Do not collapse confirmed spatially distinct faces into a one-face heuristic result.
- [ ] If duplicate-box suppression is the issue, tune or test NMS/overlap behavior rather than broadly accepting all multi-detection inputs.
- [ ] Retain explicit `ANALYSIS_MULTI_FACE_NOT_SUPPORTED` behavior for genuine multi-face images.
- [ ] Add fixtures/tests for one face with duplicate detections and two distinct faces.
- [ ] Document exact fallback behavior in `docs/face-analysis-recommendation-spec.md` and `docs/architecture.md`.

Exit criteria:

- Known valid portraits pass, known multi-person images fail, and tests cover both outcomes.

## Workstream 5: Local Public Image Transport

**Problem:** local setup describes ngrok as webhook transport only. The worker also rewrites localhost/Azurite upload URLs through the configured public base and preflights them before submitting to Replicate.

- [ ] Implement or explicitly defer the public URL configuration split.
- [ ] Keep committed `appsettings.json` values portable; remove ephemeral ngrok hosts.
- [ ] Keep tokens and environment-specific values in user secrets, environment variables, or ignored local overrides.
- [ ] Rotate any credential exposed during local debugging or review.
- [ ] Update `docs/getting-started.md` to explain both callback and source-image transport roles.
- [ ] Add a local preflight check that verifies the exact uploaded image URL returns `200` through the public tunnel before starting a recommendation.
- [ ] Clarify that old failed queue jobs are not automatically resubmitted after configuration is corrected.
- [ ] Align `infrastructure/README.md` and `infrastructure/local.env.example` with the complete required configuration.

Exit criteria:

- A new developer can start the backend, expose it publicly, verify an uploaded image, run the worker, and reach Replicate without relying on undocumented configuration behavior.

## Workstream 6: Environment and Onboarding Documents

- [ ] Add `ConnectionStrings__DefaultConnection` to `infrastructure/local.env.example`.
- [ ] Add required Replicate token, webhook secret/base, and relevant feature keys to the environment template.
- [ ] Change the environment template blob container from `style-assets` to the runtime default `user-uploads`, unless the runtime is intentionally changed.
- [ ] Expand `infrastructure/README.md` so it no longer claims the incomplete template contains all required values.
- [ ] Verify PowerShell and cross-platform worker commands set `DOTNET_ENVIRONMENT=Development` correctly.
- [ ] Keep direct style-generation endpoints out of the documented user journey.

Exit criteria:

- The environment template and onboarding guides agree with backend and worker option binding and defaults.

## Workstream 7: Permanent Document Cleanup

### Root `README.md`

- [ ] Replace the style-generation-first architecture summary with recommendation-first analysis and downstream variant generation.
- [ ] Describe the automatic primary result and optional learning-label capture in current status.
- [ ] Avoid claiming the complete local flow is operational until the public image preflight and session polling checks pass.
- [ ] Keep long-term production aspirations clearly separated from current MVP capability.

### `docs/architecture.md`

- [ ] Update EF Core 8 references to EF Core 10.
- [ ] Add final-selection fields to the current runtime schema.
- [ ] Distinguish current final-selection persistence from the target automatic-primary product semantics.
- [ ] Separate implemented schema from historical recommendations/proposals.
- [ ] Correct generation fan-out, session-state, and publication behavior.
- [ ] Remove or label stale implementation history that competes with the current architecture.

### `docs/api-contracts.md`

- [ ] Make `bestVariant` the automatic primary output and define `selectedGenerationJobId` as optional experimentation feedback or replace it with a clearer label contract.
- [ ] Document session readiness separately from analysis terminal status.
- [ ] Preserve the special case where analysis is `Succeeded` but `errorCode` is `GENERATION_ALL_VARIANTS_FAILED` until a clearer session contract replaces it.
- [ ] Verify every route and example against controllers and DTOs.

### `docs/face-analysis-recommendation-spec.md`

- [ ] Replace publish-first/one-primary-plus-experiments language with the agreed MVP flow.
- [ ] Reconcile exactly-one-face requirements with detector fallback behavior.
- [ ] Move telemetry v3 and research mapping work into clearly deferred sections.
- [ ] Mark completed ONNX work as implemented instead of leaving it in execution checklists.

### `docs/production-blueprint.md`

- [ ] Mark the document draft/noncanonical until its examples are validated.
- [ ] Update container and CI examples from .NET 8 to .NET 10.
- [ ] Account for both `analysis-jobs` and `style-jobs` in resources and scaling.
- [ ] Do not reference Dockerfiles or health endpoints until they exist.
- [ ] Replace PascalCase EF SQL examples with the mapped snake_case table and column names.
- [ ] Remove completed recommendation implementation work from the unchecked deployment checklist.
- [ ] Validate Azure commands and architecture before restoring a canonical label.

### Planning and Prompt Notes

- [ ] Add implementation status to `docs/recommendation-finalization-plan.md` so completed phases are not presented as future work.
- [x] Update `docs/mvp-best-look-2week-plan.md` for the confirmed automatic-best product north star without expanding deferred scope.
- [ ] Change `hairColor` to Replicate wire key `hair_color` in `docs/prompts/README.md`, or explicitly distinguish internal DTO names from external payload keys.

## Workstream 8: Copilot Instruction Improvements

- [x] Make the automatic system-selected best recommendation the target product north star.
- [x] Treat 3-to-4-variant ranking as temporary optional label collection rather than the primary product flow.
- [x] Prohibit a hairstyle picker and keep the primary path photo-only.
- [x] State that worker ownership includes both analysis and generation jobs.
- [x] Require session completion logic to include downstream variant states.
- [x] Require the automatic primary result to stay aligned across status, feed/public retrieval, and frontend state while analytics keeps optional preference labels distinct.
- [x] Preserve the exactly-one-face guardrail when changing detector fallback logic.
- [x] Add this temporary plan to the context list with removal instructions.
- [x] Define the permanent document hierarchy so stale plans do not override current product scope.

Exit criteria:

- Future changes are routed to the correct owning code and documents without repeating the conflicts captured here.

## Validation Checklist

Run the checks relevant to each completed workstream. Before deleting this document, run all of them:

- [ ] `dotnet test ai-style-app/tests/AiStyleApp.Tests/AiStyleApp.Tests.csproj`
- [ ] `cd ai-style-app/frontend && npm test`
- [ ] `cd ai-style-app/frontend && npm run build`
- [ ] `cd ai-style-app/backend && dotnet build`
- [ ] `cd ai-style-app/worker && dotnet build`
- [ ] Complete a local smoke test: upload one portrait -> automatic recommendation -> generated primary result, with no style selection required.
- [x] Confirm a public/feed lookup resolves the automatic primary generation.
- [ ] Confirm analytics export distinguishes the system decision from any optional user preference label.
- [ ] Confirm a genuine multi-person image fails with the documented error.
- [ ] Confirm the tunneled source-image URL returns `200` before Replicate submission.
- [ ] Search permanent docs for stale `.NET 8`, EF Core 8, publish-first, one-canonical-best-before-selection, and single-queue claims.
- [ ] Remove this file and its reference from `.github/copilot-instructions.md` after all checks pass.

## Progress Log

Record only meaningful decisions and completed work so this remains concise.

| Date | Workstream | Outcome |
|---|---|---|
| 2026-08-01 | Audit | Initial cross-document and runtime alignment review completed; remediation tracker created. |
| 2026-08-01 | Copilot instructions | Added MVP scope, session-completion, selected-winner, face-guardrail, configuration, and document-hierarchy guidance. |
| 2026-08-01 | Product direction | Confirmed photo-only automatic best recommendation as the target experience; reclassified multi-variant user ranking as optional learning data. |
| 2026-08-01 | Automatic primary contract | Added persisted primary style/post/job linkage, owner-scoped status fields, public/feed alignment, legacy-row fallback, migration, and focused backend tests. Frontend polling alignment remains open. |
| 2026-08-02 | Frontend automatic result | Added photo-only submission, session-level polling through generation, automatic primary completion without finalization, secondary experiments/feedback, and aligned smoke-test contracts. |
