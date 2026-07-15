# Face Analysis and Intelligent Recommendation - Technical Spec (V1)

## 1. Purpose

Define a production-ready V1 for accurate face analysis and intelligent style recommendations that fits the existing HowDoILook architecture:

- frontend (Vue SPA)
- backend (ASP.NET Core API)
- worker (.NET 10 BackgroundService)
- shared data/contracts in data
- async queue-driven processing

This spec focuses on analysis and recommendation only. It does not replace the existing style generation pipeline.

Implementation status:

- The recommendations API, feedback persistence, queue publishing, and worker handler are implemented.
- ONNX landmark extraction is supported and can be enabled via worker configuration with `fan2_68_landmark.onnx`.
- Invalid ONNX landmark model files now fail fast with explicit analysis error codes.

## 2. Goals and Non-Goals

### Goals

- Produce stable, explainable face-analysis features from user photos.
- Generate ranked style recommendations with confidence and short rationale.
- Keep analysis async and resilient using the current backend -> queue -> worker pattern.
- Keep beard recommendations optional and only applicable when gender is male.
- Capture feedback signals to improve recommendation quality over time.

### Non-Goals

- Identity recognition or identity verification.
- Inference of sensitive traits.
- Full ML training pipeline in V1.
- Replacing Replicate generation models in V1.

## 3. V1 Scope

### In Scope

- Image quality gate (blur, exposure, pose, occlusion, min resolution).
- Face landmarks and geometry features.
- Region segmentation-derived features (hairline/forehead/jaw/beard coverage where applicable).
- Rule-based + weighted ranking recommender.
- Recommendation explanation strings for UI trust.
- Recommendation feedback capture.

### Out of Scope (V1)

- Personalized model fine-tuning.
- Real-time on-device inference in frontend.
- Multi-face support (reject if more than one face).

## 4. High-Level Architecture

```mermaid
flowchart LR
    A[Frontend: request recommendations] --> B[Backend API]
    B --> C[(PostgreSQL)]
    B --> D[Queue: style-jobs]
    D --> E[Worker: Face Analysis Handler]
    E --> C
    E --> F[Recommendation Engine]
    F --> C
    C --> B
    B --> A
```

### Architecture Notes

- Reuse existing queue and worker with a new jobType for analysis.
- Keep request/queue contracts in data and shared by backend and worker.
- Do not block API request on heavy analysis.

## 5. Pipeline Design

## 5.1 Stages

1. Validate input image URL accessibility and ownership.
2. Run quality gate.
3. Detect face and enforce exactly one face.
4. Extract landmarks and normalized geometry features.
5. Run region segmentation and derive region metrics.
6. Build analysis feature vector and confidence metrics.
7. Rank recommendation candidates with reasons.
8. Persist result and emit completion status.

## 5.2 Quality Gate Rules (Initial)

- Min image size: 768x768
- Max yaw/pitch threshold: configurable
- Blur threshold: configurable Laplacian variance
- Exposure threshold: configurable histogram bounds
- Face occlusion confidence: configurable minimum

If failed, mark analysis job failed with actionable user-facing retry message.

## 6. Data Contracts and DTOs

All contracts below are additive and versioned.

## 6.1 API DTOs (backend + frontend types)

### CreateRecommendationsRequest

```json
{
  "imageUrl": "https://...",
  "gender": "none | male | female",
  "preferences": {
    "maintenanceLevel": "low | medium | high",
    "styleVibe": "professional | casual | trendy",
    "allowHairColorChange": true,
    "allowBeardSuggestions": true
  }
}
```

### CreateRecommendationsResponse (202)

```json
{
  "analysisJobId": "uuid",
  "status": "Queued",
  "statusEndpoint": "/api/recommendations/jobs/{analysisJobId}"
}
```

### RecommendationJobStatusResponse

```json
{
  "analysisJobId": "uuid",
  "status": "Queued | Processing | Succeeded | Failed",
  "qualityGate": {
    "passed": true,
    "failureCode": "string | null",
    "message": "string | null"
  },
  "analysisSummary": {
    "faceShapeDistribution": {
      "oval": 0.1,
      "round": 0.2,
      "square": 0.6,
      "oblong": 0.1
    },
    "confidence": 0.87
  },
  "recommendations": [
    {
      "styleId": "string",
      "styleName": "string",
      "score": 0.92,
      "reasons": [
        "Balances jaw width",
        "Fits medium maintenance preference"
      ],
      "constraints": [
        "Requires moderate top volume"
      ]
    }
  ],
  "errorCode": "string | null",
  "errorMessage": "string | null"
}
```

### SubmitRecommendationFeedbackRequest

```json
{
  "analysisJobId": "uuid",
  "selectedStyleId": "string | null",
  "rating": 1,
  "feedbackTags": ["tooBold", "notMyStyle"],
  "comment": "string | null"
}
```

## 6.2 Queue Contract Extension (data/Queue)

Extend existing style-jobs message schema to support analysis jobs.

```json
{
  "jobId": "uuid",
  "jobType": "StyleGeneration | FaceAnalysis",
  "schemaVersion": 2,
  "imageUrl": "string",
  "gender": "string | null",
  "preferencesJson": "string | null"
}
```

Notes:
- Existing style generation fields remain for backward compatibility.
- Worker routes by jobType.

## 7. Database Changes (EF Core)

Additive schema only.

## 7.1 New Table: face_analysis_jobs

- id (uuid, pk)
- user_id (varchar128, indexed)
- image_url (varchar2048)
- gender (varchar50, nullable)
- preferences_json (jsonb, nullable)
- status (varchar50, indexed) // Queued, Processing, Succeeded, Failed
- quality_passed (bool, nullable)
- quality_failure_code (varchar100, nullable)
- quality_message (varchar1000, nullable)
- feature_vector_json (jsonb, nullable)
- analysis_confidence (double precision, nullable)
- recommendations_json (jsonb, nullable)
- error_code (varchar100, nullable)
- error_message (varchar2000, nullable)
- created_at_utc (timestamptz)
- started_at_utc (timestamptz, nullable)
- completed_at_utc (timestamptz, nullable)

## 7.2 New Table: recommendation_feedback

- id (uuid, pk)
- analysis_job_id (uuid, fk -> face_analysis_jobs.id, cascade delete)
- user_id (varchar128, indexed)
- selected_style_id (varchar100, nullable)
- rating (int, nullable)
- feedback_tags_json (jsonb, nullable)
- comment (varchar1000, nullable)
- created_at_utc (timestamptz)

## 7.3 Optional Future Table (Not Required for V1)

- style_catalog for central management of style metadata and constraints.

## 8. Backend API Endpoints

Add endpoints under /api/recommendations.

1. POST /api/recommendations
- Auth required.
- Creates face_analysis_jobs row with status Queued.
- Enqueues FaceAnalysis job message.
- Returns 202 Accepted.

2. GET /api/recommendations/jobs/{id}
- Auth required.
- Returns analysis status and recommendations payload.

3. POST /api/recommendations/feedback
- Auth required.
- Stores recommendation feedback for learning loop.

## 9. Worker Design

Add new handler in worker/Handlers.

## 9.1 Handler Responsibilities

- Deserialize FaceAnalysis job.
- Run stage pipeline with per-stage timing and structured logs.
- Persist intermediate and final status updates.
- Fail fast on invalid images with clear retry guidance.

## 9.2 Stage Error Codes

- ANALYSIS_IMAGE_UNREACHABLE
- ANALYSIS_MULTI_FACE_NOT_SUPPORTED
- ANALYSIS_NO_FACE_DETECTED
- ANALYSIS_QUALITY_TOO_LOW_RESOLUTION
- ANALYSIS_QUALITY_TOO_BLURRY
- ANALYSIS_QUALITY_BAD_EXPOSURE
- ANALYSIS_POOR_POSE
- ANALYSIS_SEGMENTATION_FAILED
- ANALYSIS_INTERNAL_ERROR

## 9.3 Recommendation Engine (V1)

Hybrid scoring approach:

- Rule score from style constraints and geometric heuristics.
- Preference score from maintenance/styleVibe inputs.
- Confidence penalty when analysis confidence is low.

Final score = 0.6 * ruleScore + 0.3 * preferenceScore + 0.1 * confidenceScore

Keep top 5 recommendations with explanation reasons.

## 10. Frontend Changes

Add recommendation flow in frontend/src:

- api/recommendationsApi.ts
- types/recommendations.ts
- stores/recommendations.ts
- pages/RecommendationsPage.vue

UX requirements:

- Reuse upload flow and job polling pattern.
- Show quality gate failures with specific retake guidance.
- Display ranked recommendations with reasons and confidence indicator.
- Allow user feedback submission after selection.

## 11. Security and Privacy

- Require authenticated user for recommendation endpoints.
- Do not infer or persist sensitive attributes.
- Avoid storing raw model outputs that are not required for product behavior.
- Keep feature vectors and recommendations scoped to owner userId.
- Keep secrets in environment variables only; no secrets in source.

## 12. Observability and Metrics

Emit metrics per stage:

- analysis_jobs_total
- analysis_success_rate
- quality_gate_fail_rate by failureCode
- recommendation_click_through_rate
- recommendation_feedback_rate
- recommendation_average_rating

Log with correlationId and jobId across backend and worker.

## 13. Rollout Plan

1. Add shared contracts and DB migration.
2. Implement backend endpoints and queue publish.
3. Implement worker FaceAnalysis handler and recommendation engine.
4. Implement frontend recommendations page and polling.
5. Update docs/api-contracts.md with new endpoints and schemas.
6. Add tests across backend, worker, and frontend store logic.
7. Enable with feature flag: Features:RecommendationsV1.

## 14. Testing Strategy

Backend tests:
- request validation and auth behavior
- status endpoint ownership checks
- feedback persistence validation

Worker tests:
- quality gate pass/fail transitions
- single-face constraint
- deterministic ranking output for fixed feature vectors

Frontend tests:
- polling lifecycle and terminal states
- quality failure message rendering
- recommendation selection and feedback submission

Validation commands:

- dotnet test ai-style-app/tests/AiStyleApp.Tests/AiStyleApp.Tests.csproj
- cd ai-style-app/frontend && npm test
- cd ai-style-app/backend && dotnet build
- cd ai-style-app/worker && dotnet build

## 15. Acceptance Criteria (V1)

- Recommendation request returns 202 and creates async job.
- Worker completes analysis and returns top recommendations for valid single-face input.
- Poor-quality input yields user-actionable failure response.
- Feedback endpoint stores user response linked to analysis job.
- No regression to existing style-generation flow.
- Contracts are documented in docs/api-contracts.md before merge.

## 16. V2 Plan: ONNX Face Telemetry + Replicate Style Generation

V1 validated async flow and recommendation plumbing. V2 makes recommendations image-driven by adding true face-aware ONNX inference while preserving existing Replicate haircut/beard generation.

### 16.1 V2 Objectives

- Detect and analyze the person (face ROI), not only full-image pixel heuristics.
- Produce stable, comparable telemetry for research mapping.
- Keep Replicate models as the generation engine for haircut/beard output.
- Decouple analysis and recommendation ranking from image generation model prompts.

Current note:

- The worker already performs ONNX landmark extraction in development, so the remaining V2 work is about making the telemetry and ranking fully research-grade rather than adding the first ONNX pass.

### 16.2 V2 Non-Goals

- Replacing Replicate haircut/beard generation with ONNX in V2.
- Training custom foundation models in V2.

## 17. V2 Pipeline Architecture

```mermaid
flowchart LR
    A[Frontend: recommendations] --> B[Backend: /api/recommendations]
    B --> C[(face_analysis_jobs)]
    B --> D[Queue: style-jobs]
    D --> E[Worker: FaceAnalysisJobHandler]

    E --> F[ONNX Stage 1: Face Detection]
    F --> G[ONNX Stage 2: Landmarks]
    G --> H[ONNX/Hybrid Stage 3: Hair/Beard Region Estimation]
    H --> I[Research Mapping + Ranking]

    I --> C
    C --> B
    B --> A

    A -. select recommendation .-> J[Existing style generation flow]
    J --> K[Replicate Hair/Beard Models]
```

### 17.1 Key Principle

Analysis and ranking become model-driven and explainable. Generation remains in current Replicate flow until a future decision changes it.

## 18. Telemetry Contract (Schema v3)

V2 introduces a strict, versioned telemetry payload inside `feature_vector_json`.

### 18.1 Required Fields

```json
{
  "source": "worker-v2-onnx-analysis",
  "schemaVersion": 3,
  "imageInfo": {
    "width": 1536,
    "height": 2048
  },
  "faceDetection": {
    "faceCount": 1,
    "primaryFace": {
      "x": 420,
      "y": 280,
      "width": 620,
      "height": 760,
      "confidence": 0.97
    }
  },
  "stageTelemetry": [
    {
      "stage": "face-detection",
      "model": "<detector-model>",
      "modelVersion": "v1",
      "durationMs": 6.3,
      "metrics": {
        "faceCount": 1,
        "primaryFaceConfidence": 0.97
      }
    },
    {
      "stage": "landmarks",
      "model": "<landmark-model>",
      "modelVersion": "v1",
      "durationMs": 4.1,
      "metrics": {
        "landmarkConfidence": 0.94,
        "yaw": 0.03,
        "pitch": -0.02
      }
    },
    {
      "stage": "region-estimation",
      "model": "<region-model-or-hybrid>",
      "modelVersion": "v1",
      "durationMs": 3.8,
      "metrics": {
        "hairDensityEstimate": 0.63,
        "beardDensityEstimate": 0.41
      }
    }
  ],
  "quality": {},
  "landmarks": {},
  "segmentation": {}
}
```

### 18.2 Telemetry Reliability Rules

- If `faceCount != 1`, mark job failed with explicit code.
- If face confidence < configured threshold, mark failed.
- If landmark confidence < configured threshold, mark failed.
- Always include model name/version per stage to track drift.

## 19. Stage Contracts (Worker)

Define stage interfaces as swappable implementations:

- `IFaceDetectorStage` -> returns face boxes + confidence.
- `IFaceLandmarkModelStage` -> returns normalized landmarks + confidence.
- `IFaceRegionEstimationStage` -> returns hair/beard region metrics.

Implementation strategy:

1. Add ONNX implementations.
2. Keep heuristic implementations behind feature flag for fallback.
3. Use DI to select implementation by config.

## 20. Research Mapping Layer

Add a dedicated mapping module that converts telemetry features into recommendation scores. This module is data-driven and independent of ONNX model runtime.

Inputs:

- face geometry features (jaw/forehead/elongation/symmetry)
- quality metrics (blur/exposure/pose)
- beard/hair estimates
- user preferences (maintenance/styleVibe/hairColor/beard opt-in)

Outputs:

- ranked haircut/beard candidates
- rationale lines linked to measured features
- recommendation confidence with contribution breakdown

## 21. Replicate Integration Constraints

Replicate stays the rendering engine for final style generation in V2.

Rules:

1. Recommendation chooses style template and parameters.
2. Existing style generation endpoints queue Replicate jobs unchanged.
3. Telemetry influences what style to generate, not how Replicate is called.
4. Hair and beard stages remain optional and compatible with current two-stage pipeline.

### 21.1 Recommendation-to-Generation Decision Contract (Internal)

Use a single internal payload for the handoff between recommendation output and style generation request building.

```json
{
  "contractVersion": 1,
  "analysisJobId": "uuid",
  "userId": "string",
  "sourceImageUrl": "https://...",
  "selectedRecommendation": {
    "styleId": "textured-crop",
    "styleName": "Textured Crop",
    "score": 0.86,
    "reasons": [
      "Balances jaw width",
      "Fits medium maintenance preference"
    ]
  },
  "decision": {
    "haircut": "Textured Crop",
    "hairColor": "No change",
    "beardStyle": "No change",
    "beardColor": "No change",
    "pipelineMode": "HairOnly"
  },
  "guardrails": {
    "gender": "male | female | none",
    "allowBeardSuggestions": true,
    "qualityPassed": true,
    "analysisConfidence": 0.82,
    "minimumConfidenceRequired": 0.70
  },
  "telemetrySnapshot": {
    "source": "worker-v1-staged-analysis",
    "schemaVersion": 2,
    "landmarkConfidence": 0.78,
    "yaw": 0.12,
    "pitch": -0.03
  }
}
```

### 21.2 Contract Rules

- `analysisJobId` must refer to a `Succeeded` recommendations job owned by `userId`.
- `decision.haircut` is required; beard fields are optional.
- Beard fields must be `No change` unless `gender == male` and `allowBeardSuggestions == true`.
- If `qualityPassed == false` or `analysisConfidence < minimumConfidenceRequired`, do not enqueue style generation.
- `pipelineMode` must be one of `HairOnly`, `BeardOnly`, `HairThenBeard` and map directly to the existing style job pipeline behavior.
- This is an internal contract between recommendation and generation services; public API DTOs stay unchanged.

## 22. API and UI Additions (V2)

### 22.1 API

Extend `RecommendationJobStatusResponse` with:

- `debugTelemetry` (already present in baseline implementation)
- `faceDetection` summary (faceCount, primaryFace confidence, bbox)

### 22.2 UI

Recommendations page should display:

- image dimensions
- face detection confidence and count
- per-stage timing and model metadata
- confidence/rationale for each recommendation

## 23. Rollout and Feature Flags

Use staged rollout:

1. `Features:OnnxFaceDetection` (shadow mode)
2. `Features:OnnxLandmarks`
3. `Features:OnnxRegionEstimation`
4. `Features:RecommendationResearchMappingV2`

`Features:OnnxFaceDetection`, `Features:OnnxLandmarks`, and `Features:OnnxRegionEstimation` are configuration-driven and may be enabled per environment as rollout progresses.

Rollout sequence:

1. Shadow mode: run ONNX and heuristic in parallel, persist both (internal only).
2. Compare telemetry stability and recommendation deltas.
3. Promote ONNX output to primary when acceptance thresholds are met.

## 24. Acceptance Criteria (V2)

- For valid portraits, face detection returns exactly one primary face with confidence >= threshold.
- Two different portraits produce meaningfully different face telemetry vectors.
- Telemetry includes non-empty stage metadata (model/version/duration/metrics).
- Recommendation ranking is traceable to telemetry features and preferences.
- Existing Replicate generation flow remains functional without contract regressions.

## 25. Open Decisions

- Final ONNX model selection for detection and landmarks (accuracy vs latency).
- Whether region estimation is ONNX segmentation or landmark-guided hybrid in V2.
- Threshold defaults for face confidence and landmark confidence by environment.

## 26. Execution Checklist (By File)

This section converts V2 into a concrete implementation sequence with minimal-risk PRs.

### PR 1: Worker Stage Interfaces + Feature Flags

Goal: introduce ONNX-ready abstractions without changing runtime behavior.

Files:

- `ai-style-app/worker/Services/FaceAnalysisPipeline.cs`
  - Add stage interfaces for detection, landmark inference, and region estimation.
  - Add telemetry object builders for face detection outputs.
- `ai-style-app/worker/Program.cs`
  - Register new interfaces in DI.
  - Add config-based implementation selection (`heuristic` vs `onnx`).
- `ai-style-app/worker/appsettings.json`
  - Add feature flags:
    - `Features:OnnxFaceDetection`
    - `Features:OnnxLandmarks`
    - `Features:OnnxRegionEstimation`
  - Add threshold settings (face confidence, landmark confidence).
- `ai-style-app/worker/appsettings.Development.json`
  - Add local defaults for feature flags and thresholds.

Exit criteria:

- No behavior change when flags are off.
- Worker build and existing face-analysis tests pass.

### PR 2: ONNX Face Detection Stage

Goal: detect primary face ROI and persist detection telemetry.

Files:

- `ai-style-app/worker/Services/FaceAnalysisPipeline.cs`
  - Invoke detector stage before quality/landmark scoring.
  - Fail with `ANALYSIS_NO_FACE_DETECTED` or `ANALYSIS_MULTI_FACE_NOT_SUPPORTED` as needed.
- `ai-style-app/worker/Services/Onnx/OnnxFaceDetectorStage.cs` (new)
  - Load ONNX detector model.
  - Return face boxes, confidence, and count.
- `ai-style-app/worker/Services/Onnx/OnnxSessionFactory.cs` (new)
  - Centralize ONNX runtime session creation and options.
- `ai-style-app/worker/Services/Onnx/OnnxImageTensorizer.cs` (new)
  - Convert ImageSharp image/ROI to model tensors.
- `ai-style-app/worker/Program.cs`
  - Register ONNX detector stage.

Exit criteria:

- Telemetry includes `faceDetection.faceCount` and `primaryFace` when successful.
- Detection failures return actionable error codes/messages.

### PR 3: ONNX Landmarks + ROI-based Quality

Goal: run landmark extraction on face ROI and shift quality checks to face-aware scoring.

Files:

- `ai-style-app/worker/Services/FaceAnalysisPipeline.cs`
  - Crop to primary face ROI for quality and landmark stages.
  - Persist yaw/pitch/landmark confidence telemetry.
- `ai-style-app/worker/Services/Onnx/OnnxFaceLandmarkStage.cs` (new)
  - Run landmark model and return normalized keypoints + confidence.
- `ai-style-app/worker/Services/HeuristicFaceQualityStage.cs` (optional split/new)
  - Keep fallback implementation while ONNX rollout is gated.

Exit criteria:

- Portrait-mode blurred background no longer dominates blur gate decisions.
- Landmark confidence present in telemetry when enabled.

### PR 4: Region Estimation (Hair/Beard)

Goal: produce stable hair/beard region metrics based on ROI or segmentation.

Files:

- `ai-style-app/worker/Services/FaceAnalysisPipeline.cs`
  - Use region stage output instead of full-frame proxies.
- `ai-style-app/worker/Services/Onnx/OnnxFaceRegionEstimationStage.cs` (new)
  - Implement ONNX segmentation or hybrid ROI algorithm.
- `ai-style-app/worker/Services/Onnx/OnnxModelOptions.cs` (new)
  - Configure model paths, execution provider, input sizes.

Exit criteria:

- Telemetry reports `hairDensityEstimate` and `beardDensityEstimate` from ROI-aware analysis.

### PR 5: Backend Telemetry Contract + Parsing Hardening

Goal: expose complete debug telemetry for validation and future research mapping.

Files:

- `ai-style-app/backend/Models/RecommendationModels.cs`
  - Add/extend DTOs for face detection summary and stage telemetry.
- `ai-style-app/backend/Services/RecommendationService.cs`
  - Parse schema v2/v3 feature vectors (case-insensitive fields).
  - Map face detection and stage telemetry to response DTOs.
- `ai-style-app/docs/api-contracts.md`
  - Document `debugTelemetry` and `faceDetection` response shape.

Exit criteria:

- API returns non-empty stage telemetry/model metadata for new jobs.

### PR 6: Frontend Debug and Operator Visibility

Goal: make telemetry auditable in UI for validation.

Files:

- `ai-style-app/frontend/src/types/api.ts`
  - Add face detection + stage telemetry client types.
- `ai-style-app/frontend/src/pages/RecommendationsPage.vue`
  - Show face count, primary face confidence, ROI dimensions, per-stage metrics.
- `ai-style-app/frontend/src/stores/recommendations.test.ts`
  - Update fixtures and assertions for telemetry presence.

Exit criteria:

- Operators can verify image-driven analysis directly from UI.

### PR 7: Research Mapping Layer

Goal: decouple recommendation scoring from hardcoded heuristic constants.

Files:

- `ai-style-app/worker/Services/RecommendationResearchMapper.cs` (new)
  - Map telemetry features to recommendation priors/weights from research table.
- `ai-style-app/worker/Services/FaceAnalysisPipeline.cs`
  - Replace inline scoring composition with mapper call.
- `ai-style-app/docs/face-analysis-recommendation-spec.md`
  - Add reference table source/versioning notes.

Exit criteria:

- Recommendation reasons include traceable feature contributions.

### PR 8: Evaluation Harness + Regression Dataset

Goal: prevent regressions and verify recommendations differ for distinct portraits.

Files:

- `ai-style-app/tests/AiStyleApp.Tests/FaceAnalysisQualityStageTests.cs`
  - Add ROI-centric cases and confidence threshold tests.
- `ai-style-app/tests/AiStyleApp.Tests/FaceAnalysisJobHandlerTests.cs`
  - Add detector failure path tests (no face, multi-face, low confidence).
- `ai-style-app/tests/AiStyleApp.Tests/RecommendationServiceTests.cs` (new)
  - Assert telemetry parsing and response mapping for schema v2/v3.
- `ai-style-app/docs/architecture.md`
  - Add evaluation metrics and rollout gates.

Exit criteria:

- Distinct portrait fixtures produce measurably distinct telemetry vectors.
- All targeted tests pass in Release.

## 27. Operational Runbook (Short)

After each PR:

1. Build and test
   - `dotnet test ai-style-app/tests/AiStyleApp.Tests/AiStyleApp.Tests.csproj -c Release`
   - `cd ai-style-app/frontend && npm test && npm run build`
2. Run backend + worker with feature flags off, then on for target stage.
3. Submit at least two different portrait images and compare telemetry in UI.
4. Confirm recommendation ranking shifts when telemetry differs.
