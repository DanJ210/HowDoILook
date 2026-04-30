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
| `POST` | `/api/style/generate` | Required | `GenerateStyleRequest` | `GenerateStyleResponse` (202) |
| `DELETE` | `/api/style/{id}` | Required | — | 204 / 404 |

### StyleItemResponse

```json
{
  "id": "uuid",
  "name": "string",
  "description": "string",
  "createdAt": "ISO 8601 datetime"
}
```

### GenerateStyleRequest

```json
{
  "name": "string",
  "description": "string",
  "prompt": "string"
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
                    → Failed
                    → TimedOut
```

Once a job reaches a terminal status (`Succeeded`, `Failed`, `TimedOut`, `Canceled`) it will not transition further.

## Webhooks

| Method | Path | Auth | Description |
|--------|------|------|-------------|
| `POST` | `/api/webhooks/replicate` | HMAC-SHA256 | Receives Replicate prediction callbacks |

The webhook verifies the `Webhook-Secret` header using HMAC-SHA256. Set `Replicate__WebhookSecret` in app settings to match the secret sent by Replicate. The endpoint is not protected by JWT.

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
  "schemaVersion": 1
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

