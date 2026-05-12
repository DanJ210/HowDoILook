# Getting Started

## Prerequisites

- [Node.js](https://nodejs.org/) 20+
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker](https://www.docker.com/) (for local infrastructure containers)
- A [Replicate](https://replicate.com/) account and API token

## 1. Start Local Infrastructure (PostgreSQL + Azurite)

Infrastructure is defined in `ai-style-app/docker-compose.yml`.

```bash
cd ai-style-app
docker compose up -d
```

This starts:

- PostgreSQL (`postgres-dev`) on `localhost:5432`
- Azurite (`azurite`) on `localhost:10000-10002`

To stop:

```bash
docker compose down
```

To stop and remove volumes:

```bash
docker compose down -v
```

## 2. Configure Local Settings

The repository ships placeholder settings in `ai-style-app/backend/appsettings.json` and `ai-style-app/worker/appsettings.json`.
For local development, either edit those placeholders locally or provide the same values through environment variables using the .NET configuration keys shown below.

Backend settings:

```json
"Jwt": {
  "Key": "your-32-plus-character-local-jwt-key",
  "Issuer": "ai-style-app",
  "Audience": "ai-style-app-client"
},
"Queue": {
  "ConnectionString": "UseDevelopmentStorage=true",
  "QueueName": "style-jobs"
},
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Port=5432;Database=ai_style_app;Username=postgres;Password=postgres"
},
"Replicate": {
  "ApiToken": "r8_YOUR_REPLICATE_TOKEN",
  "WebhookSigningSecret": "whsec_dev_webhook_secret"
},
"BlobStorage": {
  "ConnectionString": "UseDevelopmentStorage=true",
  "ContainerName": "user-uploads"
}
```

Worker settings:

```json
"Queue": {
  "ConnectionString": "UseDevelopmentStorage=true",
  "QueueName": "style-jobs"
},
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Port=5432;Database=ai_style_app;Username=postgres;Password=postgres"
},
"Replicate": {
  "ApiToken": "r8_YOUR_REPLICATE_TOKEN",
  "WebhookBaseUrl": "https://abc123.ngrok.io"
}
```

For the worker, set `WebhookBaseUrl` to your publicly reachable URL (see ngrok step below).

Current generation model behavior:

- Worker uses Replicate model slug `flux-kontext-apps/change-haircut`.
- Worker resolves `latest_version.id` dynamically from Replicate at runtime.

## 3. Expose Webhook for Local Development (ngrok)

Replicate sends webhook callbacks to your backend. In local dev, use [ngrok](https://ngrok.com/) to expose the backend:

```bash
ngrok http 5000
```

Copy the `https://...ngrok.io` URL and set it in `ai-style-app/worker/appsettings.json` (or via `Replicate__WebhookBaseUrl`):

```json
"Replicate": {
  "WebhookBaseUrl": "https://abc123.ngrok.io"
}
```

## 4. Run Frontend

```bash
cd ai-style-app/frontend
npm install
npm run dev
```

Frontend runs on `http://localhost:5173`. API calls are proxied to `http://localhost:5000`.

## 5. Run Backend (Web API)

```bash
cd ai-style-app/backend
dotnet run
```

Backend runs on both `http://localhost:5000` and `https://localhost:5001` using `Properties/launchSettings.json` in development.

Swagger UI:

- `http://localhost:5000/swagger`
- `https://localhost:5001/swagger`

If HTTPS is not trusted yet:

```bash
dotnet dev-certs https --trust
```

The backend auto-applies EF Core migrations on startup in Development.

## 6. Run Worker

```bash
cd ai-style-app/worker
DOTNET_ENVIRONMENT=Development dotnet run
```

The worker connects to the local queue and polls `style-jobs` every 5 seconds.

On Windows PowerShell, use:

```powershell
$env:DOTNET_ENVIRONMENT="Development"
dotnet run
```

Using `ASPNETCORE_ENVIRONMENT` alone is not sufficient for the worker host.

## 7. What Is Running Locally

When local dev is fully started, these components should be running:

| Component | How it runs | Expected endpoint / signal |
|---|---|---|
| PostgreSQL | `docker compose` container `postgres-dev` | `localhost:5432` |
| Azurite | `docker compose` container `azurite` | `localhost:10000`, `10001`, `10002` |
| Frontend | `npm run dev` in `ai-style-app/frontend` | `http://localhost:5173` |
| Backend API | `dotnet run` in `ai-style-app/backend` | `http://localhost:5000` / `https://localhost:5001` |
| Worker | `dotnet run` in `ai-style-app/worker` | Log contains `Worker started. Polling queue 'style-jobs'.` |

## 8. Unit Testing

The repository now includes unit tests for both backend and frontend.

Backend tests (xUnit):

```bash
dotnet test ai-style-app/tests/AiStyleApp.Tests/AiStyleApp.Tests.csproj
```

Frontend tests (Vitest):

```bash
cd ai-style-app/frontend
npm run test
```

Use the command output as the current source of truth instead of relying on hard-coded pass counts in this document.

## 9. Environment Variables (Production)

If you deploy with environment variables, use the .NET configuration keys below:

| Variable | Used By | Description |
|---|---|---|
| `Queue__ConnectionString` | Backend, Worker | Azure Storage Queue connection string |
| `Queue__QueueName` | Backend, Worker | Queue name (default: `style-jobs`) |
| `ConnectionStrings__DefaultConnection` | Backend, Worker | PostgreSQL connection string |
| `Jwt__Key` | Backend | Signing key, minimum 32 characters |
| `Jwt__Issuer` | Backend | Token issuer (default: `ai-style-app`) |
| `Jwt__Audience` | Backend | Token audience (default: `ai-style-app-client`) |
| `BlobStorage__ConnectionString` | Backend | Azure Blob Storage connection string |
| `BlobStorage__ContainerName` | Backend | Blob container name (default: `user-uploads`) |
| `Replicate__ApiToken` | Backend, Worker | Replicate API token |
| `Replicate__WebhookSigningSecret` | Backend | HMAC secret for webhook verification |
| `Replicate__WebhookBaseUrl` | Worker | Publicly reachable base URL for callbacks |

Map these to .NET configuration using double-underscore notation, for example `Replicate__ApiToken`.

Security note: never commit `.env` or development appsettings files with real secrets.

## 10. Quick Health Check

After starting all services:

- Frontend: `http://localhost:5173`
- Backend API (HTTP): `http://localhost:5000`
- Backend API (HTTPS): `https://localhost:5001`
- Swagger UI (HTTP): `http://localhost:5000/swagger`
- Swagger UI (HTTPS): `https://localhost:5001/swagger`
- Worker logs include: `Worker started. Polling queue 'style-jobs'.`

## 11. Test Protected Endpoints in Swagger

`/api/style` endpoints require a Bearer token.

1. Open Swagger (`http://localhost:5000/swagger` or `https://localhost:5001/swagger`).
2. Run `POST /api/auth/token` with a request body like:

```json
{
  "username": "swagger-tester",
  "expiresInMinutes": 60
}
```

3. Copy `accessToken` from the response.
4. Click Authorize in Swagger and paste the token value.
5. Call `POST /api/style/generate` with an uploaded `imageUrl`.

If no token is provided, the API returns `401 Unauthorized` with `www-authenticate: Bearer`.

### Example request body for `POST /api/style/generate`

Upload an image first with `POST /api/upload/image`, then use the returned URL:

```json
{
  "name": "Summer look",
  "description": "Try a softer layered style.",
  "imageUrl": "https://your-host/api/upload/public/user-123/abc123.jpg",
  "isResultPublic": true,
  "haircut": "Layered",
  "hairColor": "Honey Blonde",
  "gender": "female"
}
```
