# Copilot Instructions

## Repository scope

- Most application work lives under `ai-style-app/`.
- Keep changes focused and minimal. Avoid broad refactors unless the task requires them.

## Architecture

- `ai-style-app/frontend` is the Vue 3 + Vite + TypeScript SPA.
- `ai-style-app/backend` is the ASP.NET Core API.
- `ai-style-app/worker` processes queued recommendation analysis and downstream style-generation jobs.
- `ai-style-app/data` contains shared EF Core entities, migrations, and queue contracts used by backend and worker.

## MVP product direction

- The target product north star is one uploaded portrait -> one automatic system-selected best recommendation -> one generated primary result.
- Keep recommendation analysis as the only user entrypoint. Do not reintroduce direct user-created generation jobs.
- Do not add a hairstyle picker or require users to choose a requested style before generation.
- Treat 3-to-4-candidate generation and user ranking as temporary experimentation for label collection, not the target product decision flow. Optional feedback must not block delivery of the automatic primary result.
- Keep the system-selected primary generation aligned across status, public/feed retrieval, and frontend state. Keep optional user preference labels distinct in analytics.
- Current ranking is heuristic, not a trained personalization model. Collect telemetry and clean labels for future evaluation and data-driven ranking without overstating present intelligence.
- Do not train from deterministic top-candidate feedback alone. Experiments must record the eligible candidate set, randomized exposure probability, shown order, ranking/model version, generation quality, and explicit outcome; missing feedback is not a negative label.
- Measure support across continuous telemetry ranges and style exposures. Use a validated heuristic fallback for sparse or out-of-distribution regions rather than claiming coverage for every face.
- Keep telemetry-v3, advanced research mapping, and broad UI expansion deferred unless the active task explicitly requires them.

## Style generation flow

- Treat style generation as a downstream system flow that runs after recommendations are created and moves through backend, queue, worker, Replicate, and webhook callback.
- Keep shared request and queue contract changes aligned across frontend types, backend models/services, worker handlers, and `ai-style-app/docs/api-contracts.md`.
- Beard changes are optional and should only be applied when `gender` is `male`.

## Recommendations flow

- Treat recommendations as the only user entrypoint flow ("How do I look?") moving through backend endpoints, queue, worker processing, and status polling.
- Users upload one image and click Analyze and Recommend; they do not choose a hairstyle or directly create generation jobs.
- Keep the primary path photo-only. Do not make gender, maintenance, vibe, color, beard, or other preferences required without an explicit product change.
- Analysis `Succeeded` does not by itself mean the recommendation session is ready; session completion and polling must account for downstream primary generation and any experimental variants shown to the user.
- Preserve the stable `GENERATION_ALL_VARIANTS_FAILED` signal when analysis succeeds but every expected generation variant reaches a terminal non-success state.
- Keep shared request and queue contract changes aligned across backend models/services, worker handlers, and `ai-style-app/docs/api-contracts.md`.
- When recommendations behavior changes, update both `ai-style-app/docs/architecture.md` and `ai-style-app/docs/face-analysis-recommendation-spec.md` in the same PR.

## Face analysis guardrails

- Development currently enables ONNX face detection and landmark extraction.
- Invalid landmark artifacts should fail fast with explicit analysis error codes rather than falling back silently.
- Preserve the exactly-one-person upload guardrail. Detector fallback logic must not turn confirmed spatially distinct faces into an accepted single-face result.
- Keep analysis thresholds and fallback behavior covered by portrait and genuine multi-person fixtures rather than relying only on reactive threshold changes.

## Validation

- Backend tests: `dotnet test ai-style-app/tests/AiStyleApp.Tests/AiStyleApp.Tests.csproj`
- Frontend tests: `cd ai-style-app/frontend && npm test`
- Frontend production build: `cd ai-style-app/frontend && npm run build`
- Backend build: `cd ai-style-app/backend && dotnet build`
- Worker build: `cd ai-style-app/worker && dotnet build`
- Focused recommendations checks: `dotnet test ai-style-app/tests/AiStyleApp.Tests/AiStyleApp.Tests.csproj --filter "FullyQualifiedName~FaceAnalysisJobHandlerTests|FullyQualifiedName~OnnxFaceLandmarkStageTests"`

## Configuration and secrets

- Never commit secrets or real environment-specific values.
- Keep committed appsettings files as placeholders; use environment variables, user secrets, or local `appsettings.Development.json` overrides for local configuration.
- Do not commit ephemeral ngrok or tunnel hosts to base `appsettings.json` files.
- The current worker uses `Replicate:WebhookBaseUrl` for callbacks and to rewrite local source-image URLs for Replicate access. Keep both behaviors in mind until separate callback and public-asset settings are implemented.

## Documentation

- During the active alignment work, use `ai-style-app/docs/documentation-alignment-remediation-plan.md` as the temporary gap and progress tracker. Remove it and this reference when its completion gate passes.
- Use `ai-style-app/docs/mvp-best-look-2week-plan.md` as the source of truth for MVP scope and deferrals.
- Use implemented code and tests as the source of truth for current runtime behavior when permanent documents are stale.
- Keep `ai-style-app/docs/api-contracts.md` aligned with external contracts and `ai-style-app/docs/architecture.md` aligned with implemented system flow.
- Use `ai-style-app/docs/face-analysis-recommendation-spec.md` for analysis requirements and guardrails.
- Treat `ai-style-app/docs/production-blueprint.md` as draft production direction until its commands and infrastructure examples are updated and validated.
- Update planning documents with implementation status so completed phases are not presented as future work.
