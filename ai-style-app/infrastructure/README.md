# Infrastructure

## Local Development

Use [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) to emulate Azure Storage Queue and Blob Storage locally.

### Start Azurite (Docker)

```bash
docker run -p 10000:10000 -p 10001:10001 -p 10002:10002 \
  mcr.microsoft.com/azure-storage/azurite \
  azurite --loose --skipApiVersionCheck --blobHost 0.0.0.0 --queueHost 0.0.0.0 --tableHost 0.0.0.0
```

`--skipApiVersionCheck` keeps local Azurite compatible with newer Azure Storage SDK releases used by the app.

Connection string for local emulator:
```
UseDevelopmentStorage=true
```

## Required Azure Resources (Production)

| Resource | Purpose |
|---|---|
| Azure Storage Account | Queue for style-jobs + Blob storage for assets |
| Azure Storage Queue | `style-jobs` queue consumed by the worker |
| Azure Blob Container | `style-assets` for uploaded/processed files |

## Environment Variables

Copy `local.env.example` to `.env` and populate all values.
- Backend reads `Queue__ConnectionString`, `Queue__QueueName`, and `Jwt__Key` from configuration.
- Worker reads `Queue__ConnectionString` and `Queue__QueueName`.
- Use double-underscore (`__`) when mapping env vars to .NET configuration sections.

## Security Notes

- Prefer Managed Identity over connection strings in production.
- Never commit `.env` or `appsettings.Development.json` with real secrets.
