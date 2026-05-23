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
  "createdAt": "ISO 8601 datetime"
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
  "gender": "female"
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
| `GET` | `/api/jobs/{id}` | Required | — | `JobStatusResponse` |

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

## Queue Message Contract

Messages enqueued to `style-jobs` follow this schema (v1):

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

## Error Responses

| Status | Meaning |
|--------|---------|
| 400 | Bad request / validation failure |
| 401 | Missing or invalid JWT |
| 403 | Forbidden |
| 404 | Resource not found |
| 500 | Internal server error |
