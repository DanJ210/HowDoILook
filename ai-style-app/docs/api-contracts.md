# API Contracts

All endpoints are prefixed with `/api`. Protected endpoints require `Authorization: Bearer <token>`.

## Authentication

Use this endpoint to get a JWT for local development and Swagger testing.

| Method | Path | Auth | Request Body | Response |
|--------|------|------|--------------|----------|
| `POST` | `/api/auth/token` | Not required | `CreateTokenRequest` | `TokenResponse` |

### CreateTokenRequest

```json
{
  "username": "swagger-tester",
  "expiresInMinutes": 60
}
```

### TokenResponse

```json
{
  "accessToken": "jwt-token",
  "tokenType": "Bearer",
  "expiresAtUtc": "ISO 8601 datetime"
}
```

In Swagger, click **Authorize** and paste only the JWT value from `accessToken`.

## Style Items

| Method | Path | Auth | Request Body | Response |
|--------|------|------|--------------|----------|
| `GET` | `/api/style` | Required | — | `StyleItemResponse[]` |
| `GET` | `/api/style/{id}` | Required | — | `StyleItemResponse` |
| `GET` | `/api/style/feed` | Not required | — | `FeedPageResponse` |
| `POST` | `/api/style/generate` | Required | `GenerateStyleRequest` | `GenerateStyleResponse` (202) |
| `DELETE` | `/api/style/{id}` | Required | — | 204 / 404 |

### StyleItemResponse
### FeedPageResponse

Returned by `GET /api/style/feed`. Supports cursor-based pagination.

**Query parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `take` | int | 12 | Number of items per page (max recommended: 50) |
| `before` | ISO 8601 datetime | — | Cursor: only return items published before this timestamp |

```json
{
  "items": [
    {
      "styleItemId": "uuid",
      "jobId": "uuid",
      "name": "string",
      "description": "string",
      "resultImageUrl": "string",
      "publishedAtUtc": "ISO 8601 datetime"
    }
  ],
  "hasMore": true
}
```

Only jobs where `isResultPublic = true` and `status = Succeeded` appear in the feed, ordered by `completedAtUtc` descending. To fetch the next page, pass the `publishedAtUtc` of the last item as the `before` cursor.

### StyleItemResponse

```json
{
  "id": "uuid",
  "name": "string",
  "description": "string",
  "imageUrl": "string | null",
  "isResultPublic": false,
  "createdAt": "ISO 8601 datetime",
  "latestJobId": "uuid | null",
  "latestJobStatus": "Queued | Processing | Succeeded | Failed | TimedOut | Canceled | null"
}
```

### GenerateStyleRequest

```json
{
  "name": "string",
  "description": "string",
  "prompt": "string | null",
  "imageUrl": "https://...",
  "isResultPublic": false,
  "haircut": "No change | Bob | ...",
  "hairColor": "No change | Blonde | ...",
  "beardStyle": "No change | Stubble | Goatee | ...",
  "beardColor": "No change | Black | Dark Brown | ...",
  "gender": "none | male | female"
}
```

`imageUrl` is required by the current hairstyle-generation flow.
`beardStyle` and `beardColor` are optional and only applied when `gender` is `male`.

### Example GenerateStyleRequest

```json
{
  "name": "Summer look",
  "description": "Try a shorter haircut with subtle color changes.",
  "imageUrl": "https://api.example.com/api/upload/public/user-123/abc123.jpg",
  "isResultPublic": true,
  "haircut": "Layered",
  "hairColor": "Honey Blonde",
  "beardStyle": "Short Beard",
  "beardColor": "Dark Brown",
  "gender": "male"
}
```

### GenerateStyleResponse

```json
{
  "jobId": "uuid",
  "styleItemId": "uuid",
  "status": "Queued",
  "statusEndpoint": "/api/jobs/{jobId}"
}
```

The response is `202 Accepted`. Poll `statusEndpoint` to track progress.

## Jobs

| Method | Path | Auth | Request Body | Response |
|--------|------|------|--------------|----------|
| `GET` | `/api/jobs` | Required | — | `UserJobSummaryResponse[]` |
| `GET` | `/api/jobs/{id}` | Required | — | `JobStatusResponse` |
| `PUT` | `/api/jobs/{id}/visibility` | Required | `UpdateJobVisibilityRequest` | `UserJobSummaryResponse` |

### UserJobSummaryResponse

Returned by `GET /api/jobs` and `PUT /api/jobs/{id}/visibility`.

```json
{
  "jobId": "uuid",
  "styleItemId": "uuid",
  "styleName": "string",
  "status": "Queued | Processing | Succeeded | Failed | TimedOut | Canceled",
  "resultImageUrl": "string | null",
  "isResultPublic": false,
  "createdAtUtc": "ISO 8601 datetime",
  "completedAtUtc": "ISO 8601 datetime | null"
}
```

### UpdateJobVisibilityRequest

```json
{
  "isResultPublic": true
}
```

### JobStatusResponse

```json
{
  "id": "uuid",
  "styleItemId": "uuid",
  "status": "Queued | Processing | Succeeded | Failed | TimedOut | Canceled",
  "jobType": "string",
  "errorCode": "string | null",
  "errorMessage": "string | null",
  "resultJson": "json string | null",
  "resultImageUrl": "string | null",
  "externalPredictionId": "string | null",
  "createdAtUtc": "ISO 8601 datetime",
  "startedAtUtc": "ISO 8601 datetime | null",
  "completedAtUtc": "ISO 8601 datetime | null",
  "attemptCount": 0
}
```

### Job Lifecycle

```
Queued → Processing → Succeeded
  ↑        ↓
  └──── Queued (intermediate beard stage enqueue)
                    → Failed
                    → TimedOut
```

`externalPredictionId` always represents the active Replicate prediction for the current stage and may change between the hair and beard stages. Once a job reaches a terminal status (`Succeeded`, `Failed`, `TimedOut`, `Canceled`) it will not transition further.

## Recommendations (Current Implementation)

| Method | Path | Auth | Request Body | Response |
|--------|------|------|--------------|----------|
| `POST` | `/api/recommendations` | Required | `CreateRecommendationsRequest` | `CreateRecommendationsResponse` (202) |
| `GET` | `/api/recommendations/jobs/{id}` | Required | — | `RecommendationJobStatusResponse` |
| `POST` | `/api/recommendations/feedback` | Required | `SubmitRecommendationFeedbackRequest` | 202 |

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

Beard suggestions are only considered when `gender` is `male`, and the worker enforces `preferences.allowBeardSuggestions` when generating recommendation candidates.

### CreateRecommendationsResponse

```json
{
  "analysisJobId": "uuid",
  "status": "Queued",
  "statusEndpoint": "/api/recommendations/jobs/{analysisJobId}"
}
```

The response is `202 Accepted`. Poll `statusEndpoint` to track analysis completion.

Current implementation note:
- The worker runs staged quality gating, face detection, ONNX landmark extraction when enabled, and segmentation proxies.
- `debugTelemetry.stages` includes per-stage model, model version, duration, metrics, and optional notes.
- Invalid ONNX landmark artifacts now fail fast with explicit analysis error codes instead of silently falling back.

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
    "faceShapeDistribution": null,
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
  "debugTelemetry": {
    "source": "worker-v1-staged-analysis",
    "schemaVersion": 2,
    "imageWidth": 1536,
    "imageHeight": 2048,
    "stages": [
      {
        "stage": "quality-gate",
        "model": "heuristic-quality-gate",
        "modelVersion": "v1",
        "durationMs": 1.42,
        "metrics": {
          "brightness": 0.54,
          "contrast": 0.16,
          "blurScore": 0.22,
          "centerOffset": 0.09
        },
        "notes": "square"
      }
    ]
  },
  "errorCode": "string | null",
  "errorMessage": "string | null"
}
```

Current implementation note:
- The worker emits `schemaVersion: 2` queue messages for both style generation and recommendations.
- Recommendations jobs use the same `style-jobs` transport and are handled by the worker's face-analysis path.
- Face shape is persisted at `face_analysis_jobs.feature_vector_json.faceShape` (for example, `"Square"`).
- API consumers should read face shape from `debugTelemetry.stages[]` where `stage = "landmarks"`; `notes` carries the lowercase shape label (for example, `"square"`).
- `analysisSummary.faceShapeDistribution` is currently a placeholder and is returned as `null`.

### SubmitRecommendationFeedbackRequest

```json
{
  "analysisJobId": "uuid",
  "selectedStyleId": "string | null",
  "rating": 1,
  "feedbackTags": [
    "tooBold",
    "notMyStyle"
  ],
  "comment": "string | null"
}
```

## Webhooks

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `POST` | `/api/webhooks/replicate` | HMAC-SHA256 | Receives Replicate prediction callbacks |

The webhook verifies Replicate signature headers (`webhook-id`, `webhook-timestamp`, `webhook-signature`) using HMAC-SHA256. Set `Replicate__WebhookSigningSecret` to the signing secret from Replicate. The endpoint is not protected by JWT.

## Uploads

| Method | Path | Auth | Request Body | Response |
|--------|------|------|--------------|----------|
| `POST` | `/api/upload/image` | Required | multipart form (`file`) | `UploadImageResponse` |
| `GET` | `/api/upload/public/{userId}/{fileName}` | Not required | — | Image bytes |

### UploadImageResponse

```json
{
  "url": "https://..."
}
```

## Internal Decision Contract

This internal payload defines the handoff from recommendations analysis to style generation request composition. It is not a public HTTP contract.

### RecommendationToGenerationDecision (Internal)

```json
{
  "contractVersion": 1,
  "analysisJobId": "uuid",
  "userId": "string",
  "sourceImageUrl": "https://...",
  "selectedRecommendation": {
    "styleId": "string",
    "styleName": "string",
    "score": 0.92,
    "reasons": ["string"]
  },
  "decision": {
    "haircut": "string",
    "hairColor": "string",
    "beardStyle": "string",
    "beardColor": "string",
    "pipelineMode": "HairOnly | BeardOnly | HairThenBeard"
  },
  "guardrails": {
    "gender": "none | male | female",
    "allowBeardSuggestions": true,
    "qualityPassed": true,
    "analysisConfidence": 0.87,
    "minimumConfidenceRequired": 0.7
  },
  "telemetrySnapshot": {
    "source": "worker-v1-staged-analysis",
    "schemaVersion": 2,
    "landmarkConfidence": 0.78,
    "yaw": 0.05,
    "pitch": -0.02
  }
}
```

Rules:

- `analysisJobId` must correspond to a succeeded recommendations job owned by `userId`.
- `decision.haircut` is required.
- Beard fields must remain `No change` unless `gender = male` and `allowBeardSuggestions = true`.
- If quality/confidence guardrails fail, style-generation enqueue should be blocked with an actionable error response.

## Queue Message Contract

Messages enqueued to `style-jobs` are currently emitted in schema v2 for style generation and recommendations.

### Schema v1 (Legacy)

```json
{
  "jobId": "uuid",
  "styleItemId": "uuid",
  "userId": "string",
  "jobType": "string",
  "prompt": "string",
  "enqueuedAtUtc": "ISO 8601 datetime",
  "correlationId": "string",
  "attempt": 1,
  "schemaVersion": 1,
  "imageUrl": "string | null",
  "haircut": "string | null",
  "hairColor": "string | null",
  "gender": "string | null"
}
```

### Schema v2 (Current)

V2 supports recommendation analysis jobs while keeping style-generation fields for backward compatibility.

```json
{
  "JobId": "uuid",
  "StyleItemId": "uuid",
  "UserId": "string",
  "JobType": "generate-style | face-analysis",
  "Prompt": "string",
  "EnqueuedAtUtc": "ISO 8601 datetime",
  "CorrelationId": "string",
  "Attempt": 0,
  "SchemaVersion": 2,
  "ImageUrl": "string | null",
  "Haircut": "string | null",
  "HairColor": "string | null",
  "BeardStyle": "string | null",
  "BeardColor": "string | null",
  "Gender": "string | null",
  "Stage": "string | null",
  "PreferencesJson": "string | null"
}
```

## Error Responses

| Status | Meaning |
|--------|---------|
| 400 | Bad request / validation failure |
| 401 | Missing or invalid JWT |
| 403 | Forbidden |
| 404 | Resource not found |
| 500 | Internal server error |
