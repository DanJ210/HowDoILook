# Production Blueprint — HowDoILook / AI Style App

> **Status:** Pre-production. App is end-to-end functional locally. This document is the canonical plan for deploying to Azure.

---

## Table of Contents

1. [Azure Resource Inventory](#1-azure-resource-inventory)
2. [Architecture Diagram (Production)](#2-architecture-diagram-production)
3. [Required Code Changes Before Deploy](#3-required-code-changes-before-deploy)
4. [Configuration & Secrets Strategy](#4-configuration--secrets-strategy)
5. [Infrastructure as Code (Bicep)](#5-infrastructure-as-code-bicep)
6. [Container Strategy](#6-container-strategy)
7. [CI/CD Pipeline (GitHub Actions)](#7-cicd-pipeline-github-actions)
8. [Database Migration Strategy](#8-database-migration-strategy)
9. [Security Hardening Checklist](#9-security-hardening-checklist)
10. [Operational Runbook](#10-operational-runbook)
11. [Go-Live Checklist](#11-go-live-checklist)

---

## 1. Azure Resource Inventory

| Resource | SKU / Tier | Purpose |
|---|---|---|
| **Azure Container Apps** (backend) | Consumption | ASP.NET Core API, auto-scales to 0 |
| **Azure Container Apps** (worker) | Consumption | .NET 8 BackgroundService, scales by queue depth |
| **Azure Container Registry (ACR)** | Basic → Standard | Stores `backend` and `worker` Docker images |
| **Azure Database for PostgreSQL — Flexible Server** | Burstable B1ms (dev) → General Purpose (prod) | Persistent data store |
| **Azure Storage Account** (app data) | Standard LRS | Blob container `user-uploads`, Queue `style-jobs` |
| **Azure Key Vault** | Standard | All secrets (Replicate token, JWT key, DB password, webhook secret) |
| **Azure Static Web Apps** | Free / Standard | Vue 3 SPA — global CDN, PR preview environments |
| **Azure Application Insights** | Pay-as-you-go | Distributed traces, exceptions, custom metrics |
| **Azure Log Analytics Workspace** | Pay-as-you-go | Central log sink for Container Apps + App Insights |
| **Azure Front Door** (optional) | Standard | Global load balancing, WAF, custom domain TLS |

**Estimated minimum monthly cost (dev/staging):** ~$30–60 USD (Container Apps consumption + PostgreSQL B1ms + Storage).

---

## 2. Architecture Diagram (Production)

```
Browser
  │  HTTPS (custom domain)
  ▼
Azure Static Web Apps  ──── serves Vue 3 SPA ────────────────────────────────┐
                                                                              │
  │  /api/* → Container Apps Ingress (HTTPS)                                 │
  ▼                                                                           │
Azure Container Apps — backend                                                │
  ├── reads secrets from Key Vault (via managed identity)                    │
  ├── writes blobs → Azure Blob Storage                                       │
  ├── enqueues messages → Azure Storage Queue                                 │
  ├── writes/reads → Azure PostgreSQL Flexible Server                        │
  └── POST /api/webhooks/replicate ← Replicate (HMAC verified)               │
                                                                              │
Azure Container Apps — worker                                                 │
  ├── dequeues → Azure Storage Queue (scale rule: queue depth)               │
  ├── reads/writes → Azure PostgreSQL Flexible Server                        │
  ├── calls Replicate API (Bearer token from Key Vault)                      │
  └── webhook base URL = backend Container Apps FQDN (no ngrok)             │
                                                                              │
Key Vault ←─ Managed Identity ─── both Container Apps                        │
Application Insights ←─────────── both Container Apps + Static Web Apps ────┘
```

**Key difference from dev:** No ngrok. The worker's `WebhookBaseUrl` is the backend Container App's stable HTTPS URL. Azurite is replaced by real Azure Storage. Dev auth endpoint is removed or env-gated.

---

## 3. Required Code Changes Before Deploy

### 3.1 Remove / Gate the Dev Auth Endpoint

`AuthController.cs` currently issues tokens for any username in `Development`. This **must** be replaced before production.

**Options (choose one):**
- Integrate Azure AD B2C or Microsoft Entra External ID.
- Add a proper registration/login flow with password hashing (BCrypt + EF Core `Users` table).
- Gate the dev endpoint strictly: `if (!_env.IsDevelopment()) return NotFound();` (already sufficient if the container runs `Production`).

The dev endpoint is acceptable for `ASPNETCORE_ENVIRONMENT=Development` only. Ensure production containers set `ASPNETCORE_ENVIRONMENT=Production`.

### 3.2 Secure the Public Image Endpoint

`GET /api/upload/public/{userId}/{fileName}` is unauthenticated so Replicate can fetch images. In production, **use Azure Blob SAS URLs instead**:

1. In `BlobStorageService`, generate a SAS URL with 1-hour expiry after upload:
   ```csharp
   // Replace the public passthrough endpoint with a SAS URL
   var sasUri = blobClient.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddHours(1));
   return sasUri.ToString();
   ```
2. The worker passes this SAS URL directly to Replicate — no URL rewriting needed.
3. Remove the `GET /api/upload/public/...` endpoint entirely.

> This change eliminates the unauthenticated blob proxy endpoint which is the largest security gap in the current code.

### 3.3 CORS Lockdown

`Program.cs` currently allows all origins in development. In production, restrict to your Static Web Apps domain:

```csharp
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("https://your-app.azurestaticapps.net", "https://yourdomain.com")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});
```

### 3.4 Disable EF Core Auto-Migration on Startup

`Program.cs` currently calls `db.Database.MigrateAsync()` on startup. In production:
- Run migrations as a separate job in the CI/CD pipeline (see §8).
- Remove or env-gate the auto-migrate call to avoid race conditions when multiple backend replicas start simultaneously.

### 3.5 Worker `WebhookBaseUrl`

Remove the ngrok URL. Set `Replicate:WebhookBaseUrl` to the backend Container App's HTTPS ingress URL via an environment variable / Key Vault reference.

### 3.6 Structured Logging / App Insights

Add the Application Insights SDK to both projects:

```xml
<!-- both .csproj files -->
<PackageReference Include="Microsoft.ApplicationInsights.AspNetCore" Version="2.*" />
<PackageReference Include="Microsoft.ApplicationInsights.WorkerService" Version="2.*" />
```

```csharp
// Program.cs (backend)
builder.Services.AddApplicationInsightsTelemetry();

// Program.cs (worker)
builder.Services.AddApplicationInsightsTelemetryWorkerService();
```

Set `APPLICATIONINSIGHTS_CONNECTION_STRING` via Key Vault reference.

---

## 4. Configuration & Secrets Strategy

### Key Vault Secrets

| Secret Name | Maps To | Notes |
|---|---|---|
| `jwt-key` | `Jwt:Key` | Minimum 32 chars, randomly generated (`openssl rand -base64 32`) |
| `replicate-api-token` | `Replicate:ApiToken` | Rotate from Replicate dashboard; **current dev token must be rotated** |
| `replicate-webhook-secret` | `Replicate:WebhookSigningSecret` | Set in Replicate dashboard per-webhook |
| `db-connection-string` | `ConnectionStrings:DefaultConnection` | Full PostgreSQL connection string |
| `storage-connection-string` | `Queue:ConnectionString` + `BlobStorage:ConnectionString` | Azure Storage Account connection string |
| `appinsights-connection-string` | `APPLICATIONINSIGHTS_CONNECTION_STRING` | From App Insights resource |

### Container App Environment Variables

Use Key Vault references in Container Apps secrets:

```json
{
  "name": "jwt-key",
  "keyVaultUrl": "https://your-kv.vault.azure.net/secrets/jwt-key",
  "identity": "system"
}
```

Then reference as `secretRef` in environment variables. This avoids storing plain text secrets anywhere in Container Apps configuration.

### `appsettings.json` — Non-Secret Config

Keep non-secret defaults in `appsettings.json`. Secrets come exclusively from environment variables backed by Key Vault at runtime.

```json
{
  "Jwt": {
    "Issuer": "ai-style-app",
    "Audience": "ai-style-app-client"
  },
  "Queue": {
    "QueueName": "style-jobs"
  },
  "BlobStorage": {
    "ContainerName": "user-uploads"
  },
  "Replicate": {
    "ModelVersion": "black-forest-labs/flux-dev",
    "ImagePromptStrength": 0.35
  }
}
```

---

## 5. Infrastructure as Code (Bicep)

> Place all Bicep files in `ai-style-app/infrastructure/bicep/`.

### File Layout

```
infrastructure/
  bicep/
    main.bicep              # orchestrates all modules
    modules/
      containerApps.bicep   # backend + worker Container Apps + environment
      database.bicep        # PostgreSQL Flexible Server
      storage.bicep         # Storage Account (blobs + queues)
      keyVault.bicep        # Key Vault + access policies
      staticWebApp.bicep    # Static Web Apps resource
      appInsights.bicep     # App Insights + Log Analytics
      registry.bicep        # Azure Container Registry
    params/
      dev.bicepparam        # dev parameter file
      prod.bicepparam       # prod parameter file
```

### `main.bicep` (outline)

```bicep
targetScope = 'resourceGroup'

param location string = resourceGroup().location
param environment string = 'prod'
param replicateApiToken string  // passed from pipeline, stored in Key Vault

module kv 'modules/keyVault.bicep' = { ... }
module storage 'modules/storage.bicep' = { ... }
module db 'modules/database.bicep' = { ... }
module registry 'modules/registry.bicep' = { ... }
module insights 'modules/appInsights.bicep' = { ... }
module aca 'modules/containerApps.bicep' = {
  params: {
    keyVaultName: kv.outputs.name
    storageConnectionStringSecretUri: storage.outputs.connectionStringSecretUri
    dbConnectionStringSecretUri: db.outputs.connectionStringSecretUri
    ...
  }
}
module swa 'modules/staticWebApp.bicep' = { ... }
```

### Deploy Command

```bash
# One-time resource group creation
az group create --name rg-howdoilook-prod --location eastus

# Deploy infrastructure
az deployment group create \
  --resource-group rg-howdoilook-prod \
  --template-file infrastructure/bicep/main.bicep \
  --parameters infrastructure/bicep/params/prod.bicepparam \
  --parameters replicateApiToken=$REPLICATE_API_TOKEN
```

---

## 6. Container Strategy

### Dockerfiles

**`ai-style-app/backend/Dockerfile`**
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["backend/Backend.csproj", "backend/"]
COPY ["data/AiStyleApp.Data.csproj", "data/"]
RUN dotnet restore "backend/Backend.csproj"
COPY . .
RUN dotnet publish "backend/Backend.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Backend.dll"]
```

**`ai-style-app/worker/Dockerfile`** (same pattern, swap `Backend.csproj` / `Backend.dll` for `Worker.csproj` / `Worker.dll`)

Build context for both Dockerfiles is `ai-style-app/` so both `backend/` and `data/` are accessible.

### Image Tags

Use immutable SHA-based tags in production:

```
your-acr.azurecr.io/howdoilook/backend:sha-<git-sha>
your-acr.azurecr.io/howdoilook/worker:sha-<git-sha>
```

Never deploy `:latest` in production.

### Container Apps Scale Rules

**Backend:** HTTP-based scaling (min 1, max 5 replicas).
```yaml
scale:
  minReplicas: 1
  maxReplicas: 5
  rules:
    - name: http-scaler
      http:
        metadata:
          concurrentRequests: "50"
```

**Worker:** Queue-depth-based scaling (min 0, max 3 replicas).
```yaml
scale:
  minReplicas: 0
  maxReplicas: 3
  rules:
    - name: queue-scaler
      azureQueue:
        queueName: style-jobs
        queueLength: "5"
        auth:
          - secretRef: storage-connection-string
            triggerParameter: connection
```

---

## 7. CI/CD Pipeline (GitHub Actions)

> Place at `.github/workflows/deploy.yml`.

```yaml
name: Build & Deploy

on:
  push:
    branches: [master]
  pull_request:
    branches: [master]

env:
  REGISTRY: ${{ secrets.ACR_LOGIN_SERVER }}
  RESOURCE_GROUP: rg-howdoilook-prod
  BACKEND_APP: ca-howdoilook-backend
  WORKER_APP: ca-howdoilook-worker
  SWA_APP: swa-howdoilook

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.x' }
      - run: dotnet test ai-style-app/HowDoILook.sln

  build-push:
    needs: test
    runs-on: ubuntu-latest
    if: github.event_name == 'push'
    outputs:
      image-tag: ${{ steps.tag.outputs.tag }}
    steps:
      - uses: actions/checkout@v4
      - id: tag
        run: echo "tag=sha-$(git rev-parse --short HEAD)" >> $GITHUB_OUTPUT

      - name: Log in to ACR
        uses: azure/docker-login@v2
        with:
          login-server: ${{ secrets.ACR_LOGIN_SERVER }}
          username: ${{ secrets.ACR_USERNAME }}
          password: ${{ secrets.ACR_PASSWORD }}

      - name: Build & push backend
        run: |
          docker build -f ai-style-app/backend/Dockerfile ai-style-app \
            -t $REGISTRY/backend:${{ steps.tag.outputs.tag }}
          docker push $REGISTRY/backend:${{ steps.tag.outputs.tag }}

      - name: Build & push worker
        run: |
          docker build -f ai-style-app/worker/Dockerfile ai-style-app \
            -t $REGISTRY/worker:${{ steps.tag.outputs.tag }}
          docker push $REGISTRY/worker:${{ steps.tag.outputs.tag }}

  migrate:
    needs: build-push
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.x' }
      - name: Run EF Core migrations
        env:
          ConnectionStrings__DefaultConnection: ${{ secrets.PROD_DB_CONNECTION_STRING }}
        run: |
          dotnet tool install --global dotnet-ef
          dotnet ef database update \
            --project ai-style-app/data/AiStyleApp.Data.csproj \
            --startup-project ai-style-app/backend/Backend.csproj \
            --no-build  # built in previous job; or rebuild here

  deploy:
    needs: [build-push, migrate]
    runs-on: ubuntu-latest
    steps:
      - uses: azure/login@v2
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}

      - name: Deploy backend Container App
        run: |
          az containerapp update \
            --name $BACKEND_APP \
            --resource-group $RESOURCE_GROUP \
            --image $REGISTRY/backend:${{ needs.build-push.outputs.image-tag }}

      - name: Deploy worker Container App
        run: |
          az containerapp update \
            --name $WORKER_APP \
            --resource-group $RESOURCE_GROUP \
            --image $REGISTRY/worker:${{ needs.build-push.outputs.image-tag }}

      - name: Deploy frontend to Static Web Apps
        uses: Azure/static-web-apps-deploy@v1
        with:
          azure_static_web_apps_api_token: ${{ secrets.SWA_DEPLOYMENT_TOKEN }}
          repo_token: ${{ secrets.GITHUB_TOKEN }}
          action: upload
          app_location: ai-style-app/frontend
          output_location: dist

  rollback:
    needs: deploy
    if: failure()
    runs-on: ubuntu-latest
    steps:
      - uses: azure/login@v2
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}
      # Re-pin to previous known-good image tag (stored as a repo variable)
      - name: Rollback backend
        run: |
          az containerapp update \
            --name $BACKEND_APP \
            --resource-group $RESOURCE_GROUP \
            --image $REGISTRY/backend:${{ vars.LAST_GOOD_TAG }}
```

### Required GitHub Secrets

| Secret | How to Obtain |
|---|---|
| `AZURE_CREDENTIALS` | `az ad sp create-for-rbac --sdk-auth --role contributor --scope /subscriptions/<id>/resourceGroups/rg-howdoilook-prod` |
| `ACR_LOGIN_SERVER` | `<registry>.azurecr.io` |
| `ACR_USERNAME` / `ACR_PASSWORD` | ACR admin credentials (or use OIDC) |
| `PROD_DB_CONNECTION_STRING` | PostgreSQL connection string for migration step |
| `SWA_DEPLOYMENT_TOKEN` | From Static Web Apps resource → Manage deployment token |

---

## 8. Database Migration Strategy

**Do not** run `MigrateAsync()` automatically in the production backend. Instead:

1. The `migrate` job in the pipeline runs `dotnet ef database update` before deploying new images.
2. All migrations must be **backward compatible** with the previous image (additive only — no column drops or renames in the same migration as the deploy).
3. Destructive schema changes require a two-phase deploy:
   - Phase 1: Deploy code that works with both old and new schema.
   - Phase 2: Apply destructive migration after all old replicas are gone.
4. Take a manual snapshot of the PostgreSQL instance before every production migration:
   ```bash
   az postgres flexible-server backup create \
     --name pg-howdoilook-prod \
     --resource-group rg-howdoilook-prod \
     --backup-name pre-migration-$(date +%Y%m%d)
   ```

---

## 9. Security Hardening Checklist

### Secrets

- [ ] Rotate Replicate API token (`r8_VnE27...`) — the dev token is committed in `appsettings.Development.json` and must not reach production
- [ ] Generate a new JWT signing key: `openssl rand -base64 32`
- [ ] Store all secrets in Key Vault; no secrets in `appsettings.json` or environment files
- [ ] `.gitignore` must include `appsettings.Development.json` OR scrub tokens before any public push

### Endpoints

- [ ] Remove or strictly env-gate `POST /api/auth/token` (dev-only token issuer)
- [ ] Replace `GET /api/upload/public/{userId}/{fileName}` with Blob SAS URL generation
- [ ] Restrict CORS to production frontend domain(s) only
- [ ] Enable HTTPS-only on Container Apps ingress (default: yes, verify)
- [ ] Set `Strict-Transport-Security` response header

### Infrastructure

- [ ] PostgreSQL: disable public network access; use private endpoint or VNet integration
- [ ] Storage Account: disable public blob access (`AllowBlobPublicAccess = false`); rely on SAS tokens
- [ ] Key Vault: restrict access to Container Apps managed identities only (no broad service principal)
- [ ] Container Apps: use system-assigned managed identity; no connection strings in plain env vars
- [ ] ACR: disable admin account after initial setup; use managed identity pull

### Application

- [ ] Add rate limiting on `POST /api/upload/image` and `POST /api/style` endpoints
- [ ] Validate image content type server-side (magic bytes, not just `Content-Type` header)
- [ ] Set `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` response headers
- [ ] Ensure EF Core queries use parameterized values (default with EF Core — verify no raw SQL)
- [ ] Add request size limits at the Container Apps ingress level (in addition to ASP.NET Core)

---

## 10. Operational Runbook

### 10.1 Job Stuck in "Processing"

**Symptom:** `GET /api/jobs/{id}` returns `status: Processing` for >10 minutes.

**Steps:**
1. Check Application Insights for exceptions from the worker with `StyleJobId` = the stuck job's ID.
2. Check Replicate dashboard: does the prediction exist? What is its status?
3. If Replicate shows `succeeded` but the webhook never fired:
   - Check that `Replicate:WebhookSigningSecret` matches what Replicate shows for the webhook endpoint.
   - Check the backend Container App's Application Insights for `POST /api/webhooks/replicate` requests and any 400 responses with signature failure reasons.
4. If Replicate shows `failed`:
   - Check the prediction's error field in the Replicate dashboard.
   - The worker should have set the job to `Failed` — if it didn't, check worker logs.
5. Manual remediation:
   ```sql
   UPDATE "StyleJobs" SET "Status" = 'Failed', "ErrorMessage" = 'Manual override — stuck', "UpdatedAt" = NOW()
   WHERE "Id" = '<job-id>';
   ```

### 10.2 Webhook Signature Failures (400 responses)

**Symptom:** All Replicate webhooks return `400 Bad Request`.

**Steps:**
1. Check backend logs for the `ReplicateSignatureVerifier` failure reason message.
2. Common causes:
   - `WebhookSigningSecret` mismatch → re-copy from Replicate dashboard, update Key Vault secret, restart backend.
   - Clock skew > 5 minutes → check Container App time sync (usually automatic).
   - Secret has `whsec_` prefix stripped incorrectly → the verifier Base64-decodes after stripping; verify no double-strip.
3. To test signature verification locally against a real Replicate payload, use the Replicate webhook testing tool in the dashboard.

### 10.3 Worker Not Processing Jobs

**Symptom:** Jobs stay in `Queued` status; queue depth grows.

**Steps:**
1. Check worker Container App is running: `az containerapp show --name ca-howdoilook-worker ...`
2. Check worker logs in Application Insights or Log Analytics for connection errors.
3. Check that the Storage Account queue `style-jobs` exists and the connection string is correct.
4. Check the worker's scale rule — if `minReplicas = 0`, the worker scales to zero when the queue is empty. It should scale up within ~30 seconds of a new message.
5. Force a restart: `az containerapp revision restart ...`

### 10.4 Replicate API Errors (402 / 403)

**Symptom:** Jobs fail immediately with `ReplicateApiException` status 402 or 403.

- **402:** Replicate account billing issue — add payment method in Replicate dashboard.
- **403:** API token invalid or revoked — rotate token in Replicate dashboard, update Key Vault secret, restart worker.

### 10.5 Scaling Under Load

- **Backend bottleneck:** Container Apps HTTP scaler handles this automatically. If p99 latency degrades, increase `maxReplicas` or upgrade to a larger CPU/memory profile.
- **Worker bottleneck:** Increase `maxReplicas` on the queue-depth scale rule. Note: Replicate has rate limits per account — more worker replicas won't help if Replicate itself is the bottleneck.
- **Database bottleneck:** Upgrade PostgreSQL SKU. Add read replicas if read traffic is high. Add indexes on `StyleJobs(StyleItemId, CreatedAt)` and `StyleJobs(Status)` for polling queries.

### 10.6 Rollback

```bash
# Find last known-good image tag
az containerapp revision list --name ca-howdoilook-backend --resource-group rg-howdoilook-prod

# Roll back to a specific revision
az containerapp ingress traffic set \
  --name ca-howdoilook-backend \
  --resource-group rg-howdoilook-prod \
  --revision-weight <previous-revision>=100
```

For database rollbacks, restore from the pre-migration snapshot (see §8) and redeploy the previous image.

---

## 11. Go-Live Checklist

### Week 1 — Infrastructure

- [ ] Provision all Azure resources via Bicep (`main.bicep`)
- [ ] Create Key Vault secrets for all values in §4
- [ ] Configure managed identity on both Container Apps; grant Key Vault `Secret User` role
- [ ] Build and push initial images to ACR
- [ ] Verify Container Apps can pull from ACR with managed identity

### Week 2 — Configuration & Security

- [ ] Apply all code changes in §3 (SAS URLs, CORS, remove dev auth endpoint, disable auto-migrate)
- [ ] Complete §9 Security Hardening Checklist
- [ ] Verify webhook URL in Replicate dashboard points to `https://<backend-fqdn>/api/webhooks/replicate`
- [ ] Run EF Core migrations against production database
- [ ] Deploy backend and worker; smoke test `GET /health`

### Week 3 — Frontend & Observability

- [ ] Configure Static Web Apps with production backend URL (`VITE_API_BASE_URL` build variable)
- [ ] Deploy frontend to Static Web Apps; verify CORS
- [ ] Configure Application Insights alerts:
  - Exception rate > 1% → email/Teams alert
  - Queue depth > 50 for > 5 min → PagerDuty / email
  - P99 API latency > 3s → email alert
- [ ] Verify end-to-end flow in production: upload photo → generate → webhook received → result displayed

### Week 4 — Hardening & Documentation

- [ ] Load test: simulate 50 concurrent users uploading and generating
- [ ] Review Application Insights for slow queries; add DB indexes as needed
- [ ] Set up automated nightly database backups via Azure Backup policy
- [ ] Document the production `Replicate:WebhookSigningSecret` rotation procedure
- [ ] Tag `v1.0.0` in Git; update `LAST_GOOD_TAG` repo variable in GitHub Actions

---

*Generated: May 2026 — review before each major infrastructure change.*
