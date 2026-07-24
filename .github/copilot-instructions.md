# Copilot Instructions

## Repository scope

- Most application work lives under `ai-style-app/`.
- Keep changes focused and minimal. Avoid broad refactors unless the task requires them.

## Architecture

- `ai-style-app/frontend` is the Vue 3 + Vite + TypeScript SPA.
- `ai-style-app/backend` is the ASP.NET Core API.
- `ai-style-app/worker` processes queued style jobs.
- `ai-style-app/data` contains shared EF Core entities, migrations, and queue contracts used by backend and worker.

## Style generation flow

- Treat style generation as a downstream system flow that runs after recommendations are created and moves through backend, queue, worker, Replicate, and webhook callback.
- Keep shared request and queue contract changes aligned across frontend types, backend models/services, worker handlers, and `ai-style-app/docs/api-contracts.md`.
- Beard changes are optional and should only be applied when `gender` is `male`.

## Recommendations flow

- Treat recommendations as the only user entrypoint flow ("How do I look?") moving through backend endpoints, queue, worker processing, and status polling.
- Users upload one image and click Analyze and Recommend; they do not directly create generation jobs.
- Keep shared request and queue contract changes aligned across backend models/services, worker handlers, and `ai-style-app/docs/api-contracts.md`.
- When recommendations behavior changes, update both `ai-style-app/docs/architecture.md` and `ai-style-app/docs/face-analysis-recommendation-spec.md` in the same PR.
- Development currently enables ONNX landmark extraction; invalid landmark artifacts should fail fast with explicit analysis error codes rather than falling back silently.

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

## Documentation

- Use these docs as the primary source of repository context:
  - `ai-style-app/docs/architecture.md`
  - `ai-style-app/docs/api-contracts.md`
  - `ai-style-app/docs/face-analysis-recommendation-spec.md`
  - `ai-style-app/docs/getting-started.md`
  - `ai-style-app/docs/recommendation-finalization-plan.md`
  - `ai-style-app/docs/mvp-best-look-2week-plan.md`
