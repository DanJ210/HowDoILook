# HowDoILook

HowDoILook is the foundation for a real AI grooming platform, built as a scalable, production-grade system rather than a toy project.

## Vision

Build a platform that:
- Analyzes faces accurately
- Recommends styles intelligently
- Generates realistic AI previews
- Handles many users concurrently
- Uses async workers for heavy AI tasks
- Is structured cleanly across frontend, backend, worker, and queue components
- Scales horizontally with multiple API and worker instances
- Can evolve into a commercial product

## Product Direction

HowDoILook is designed to become a trustworthy AI assistant for personal grooming decisions. The long-term objective is to deliver personalized, realistic, and fast style recommendations while maintaining platform reliability, security, and cost-aware scalability.

## Current Architecture

The solution follows an async, service-oriented flow:

1. Frontend (Vue 3 + Vite + TypeScript + Tailwind + Pinia) sends authenticated requests.
2. Backend API (.NET 8) validates requests, stores data, and enqueues style-generation jobs.
3. Queue decouples user-facing latency from heavy AI processing.
4. Worker service (.NET 8) processes queued jobs and calls Replicate for inference.
5. Webhook callback updates job state and results in PostgreSQL.
6. Frontend polls job status and renders progress and final output.

## Repository Structure

- `ai-style-app/frontend` - Vue SPA UI
- `ai-style-app/backend` - ASP.NET Core API
- `ai-style-app/worker` - Background job processor
- `ai-style-app/data` - Shared EF Core data layer, entities, migrations, queue contract
- `ai-style-app/docs` - Architecture, API contracts, and setup docs
- `ai-style-app/infrastructure` - Local infrastructure guidance and environment templates

## Tech Stack

- Frontend: Vue 3, Vite, TypeScript, Tailwind CSS, Pinia
- Backend: ASP.NET Core (.NET 8), JWT auth, Swagger
- Worker: .NET 8 Hosted Service
- Database: PostgreSQL (EF Core + Npgsql)
- Queue: Azure Storage Queue (Azurite locally)
- AI Inference: Replicate API
- Local Infra: Docker Compose

## Scalability Model

- Stateless API instances can be replicated behind a load balancer.
- Worker instances can be scaled out independently for queue throughput.
- Queue-based orchestration smooths traffic spikes and protects API responsiveness.
- PostgreSQL provides durable persistence for styles, jobs, and audit trail data.
- Async processing supports long-running AI tasks without blocking user requests.

## Local Development Quick Start

Prerequisites:
- .NET 8 SDK
- Node.js 20+
- Docker Desktop

Start infrastructure:

```powershell
cd ai-style-app
docker compose up -d
```

Run backend:

```powershell
cd ai-style-app/backend
$env:DOTNET_ENVIRONMENT="Development"; dotnet watch
```

Run worker:

```powershell
cd ai-style-app/worker
$env:DOTNET_ENVIRONMENT="Development"; dotnet watch
```

Run frontend:

```powershell
cd ai-style-app/frontend
npm install
npm run dev
```

Useful URLs:
- Frontend: http://localhost:5173
- API: http://localhost:5000
- Swagger: http://localhost:5000/swagger

## Security and Secrets

Do not commit secrets. Sensitive files are ignored by git, including environment files and development appsettings files. Use local environment variables or secret stores for credentials (JWT keys, database credentials, Replicate token).

## Production Readiness Roadmap

Planned priorities for hardening into a commercial-grade platform:
- Strong identity, RBAC, and tenant isolation
- Observability (structured logs, metrics, distributed tracing)
- Retry, idempotency, and dead-letter handling
- Cost controls and rate limits for AI workloads
- Compliance, privacy, and data retention policies
- CI/CD with automated test gates and environment promotion
- Capacity planning and autoscaling policies

## Status

Core full-stack foundation is implemented:
- API, worker, queue contract, and database schema are in place
- Frontend flow for style generation and job tracking is implemented
- Local infrastructure via Docker Compose is available
- Project is ready for iterative hardening toward production
