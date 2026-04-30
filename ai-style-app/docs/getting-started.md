# Getting Started

## Prerequisites

- [Node.js](https://nodejs.org/) 20+
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker](https://www.docker.com/) (for Azurite local storage emulation)
- [PostgreSQL](https://www.postgresql.org/) 15+ running locally on port 5432
- A [Replicate](https://replicate.com/) account and API token

## 1. Start Local Storage Emulator (Azurite)

The worker and backend depend on Azure Storage Queue. Run Azurite locally before starting either .NET project:

```bash
docker run -d --name azurite -p 10000:10000 -p 10001:10001 -p 10002:10002 \
  mcr.microsoft.com/azure-storage/azurite
```

If you use Docker Desktop, ensure the daemon is running first. If `azurite` already exists from a previous run, use:

```bash
docker start azurite
```

## 2. Start PostgreSQL

The backend and worker both require a PostgreSQL database. The development defaults expect:

| Setting | Value |
|---|---|
| Host | `localhost` |
| Port | `5432` |
| Database | `ai_style_app` |
| Username | `postgres` |
| Password | `postgres` |

Update `appsettings.Development.json` in `/backend` and `/worker` if your local PostgreSQL differs.

The backend automatically runs `dotnet ef database update` on startup in the Development environment, so the schema will be created automatically the first time it starts.

To run migrations manually:

```bash
dotnet ef database update --project ai-style-app/data/AiStyleApp.Data.csproj \
  --startup-project ai-style-app/backend/Backend.csproj
```

## 3. Configure Replicate API

Edit `ai-style-app/backend/appsettings.Development.json` and `ai-style-app/worker/appsettings.Development.json`:

```json
"Replicate": {
  "ApiToken": "r8_YOUR_REPLICATE_TOKEN",
  "ModelVersion": "stability-ai/sdxl:39ed52f2319f9b5b5474b42ab48c1e7a",
  "WebhookSecret": "dev-webhook-secret-change-me"
}
```

For the worker, also set `WebhookBaseUrl` to your publicly reachable URL (see step below for ngrok).

## 4. Expose Webhook for Local Development (ngrok)

Replicate sends webhook callbacks to your backend. In local dev, use [ngrok](https://ngrok.com/) to expose the backend:

```bash
ngrok http 5000
```

Copy the `https://...ngrok.io` URL and set it in `ai-style-app/worker/appsettings.Development.json`:

```json
"Replicate": {
  "WebhookBaseUrl": "https://abc123.ngrok.io"
}
```

## 5. Frontend

```bash
cd ai-style-app/frontend
npm install
npm run dev
```

Runs on `http://localhost:5173`. API calls are proxied to `http://localhost:5000`.

## 6. Backend (Web API)

```bash
cd ai-style-app/backend
dotnet run
```

Runs on both `http://localhost:5000` and `https://localhost:5001` using `Properties/launchSettings.json` in development.

Swagger UI is available at:

- `http://localhost:5000/swagger`
- `https://localhost:5001/swagger`

If HTTPS is not trusted yet on your machine:

```bash
dotnet dev-certs https --trust
```

The development JWT key is pre-configured in `appsettings.Development.json`. **Replace it with a strong secret before deploying.**

## 7. Worker

```bash
cd ai-style-app/worker
DOTNET_ENVIRONMENT=Development dotnet run
```

Connects to the local Azurite queue (`UseDevelopmentStorage=true`) and polls for `style-jobs` messages every 5 seconds.

`DOTNET_ENVIRONMENT=Development` is required for the worker to load `appsettings.Development.json`.

On Windows PowerShell, use:

```powershell
$env:DOTNET_ENVIRONMENT="Development"
dotnet run
```

Using `ASPNETCORE_ENVIRONMENT` is not sufficient for the worker host.

## 8. Environment Variables (Production)

Copy `infrastructure/local.env.example` to `.env` and fill in all values before deploying:

| Variable | Used By | Description |
|---|---|---|
| `QUEUE_CONNECTION_STRING` | Backend, Worker | Azure Storage Queue connection string |
| `QUEUE_NAME` | Backend, Worker | Queue name (default: `style-jobs`) |
| `ConnectionStrings__DefaultConnection` | Backend, Worker | PostgreSQL connection string |
| `JWT_KEY` | Backend | Signing key — minimum 32 characters |
| `JWT_ISSUER` | Backend | Token issuer (default: `ai-style-app`) |
| `JWT_AUDIENCE` | Backend | Token audience (default: `ai-style-app-client`) |
| `Replicate__ApiToken` | Backend, Worker | Replicate API token |
| `Replicate__ModelVersion` | Backend, Worker | Replicate model version string |
| `Replicate__WebhookSecret` | Backend | HMAC secret for webhook verification |
| `Replicate__WebhookBaseUrl` | Worker | Publicly reachable base URL for callbacks |

Map these to .NET configuration using double-underscore notation, e.g. `Replicate__ApiToken`.

> **Security**: Never commit `.env` or `appsettings.Development.json` with real secrets. Use Managed Identity in production instead of connection strings where possible.

## 9. Quick Health Check

After starting all services:

- Frontend: `http://localhost:5173`
- Backend API (HTTP): `http://localhost:5000`
- Backend API (HTTPS): `https://localhost:5001`
- Swagger UI (HTTP): `http://localhost:5000/swagger`
- Swagger UI (HTTPS): `https://localhost:5001/swagger`
- Worker logs should include: `Worker started. Polling queue 'style-jobs'.`

## 10. Test Protected Endpoints in Swagger

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
4. Click **Authorize** in Swagger and paste the token value.
5. Call `POST /api/style` again.

If no token is provided, the API correctly returns `401 Unauthorized` with `www-authenticate: Bearer`.
