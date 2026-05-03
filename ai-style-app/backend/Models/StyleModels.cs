namespace AiStyleApp.Api.Models;

public record StyleItemResponse(
    Guid Id,
    string Name,
    string Description,
    string? ImageUrl,
    DateTimeOffset CreatedAt,
    Guid? LatestJobId
);

public record CreateStyleItemRequest(
    string Name,
    string Description
);

// ── Generate-style (async) ───────────────────────────────────────────────────

public record GenerateStyleRequest(
    string Name,
    string Description,
    string Prompt,
    string? ImageUrl = null
);

public record GenerateStyleResponse(
    Guid JobId,
    Guid StyleItemId,
    string Status,
    string StatusEndpoint
);

// ── Job status / result ──────────────────────────────────────────────────────

public record JobStatusResponse(
    Guid Id,
    Guid StyleItemId,
    string Status,
    string JobType,
    string? ErrorCode,
    string? ErrorMessage,
    string? ResultJson,
    string? ExternalPredictionId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int AttemptCount
);

// ── Replicate webhook ────────────────────────────────────────────────────────

public record ReplicateWebhookPayload(
    string Id,
    string Status,
    string? Error,
    object? Output,
    DateTimeOffset? CompletedAt
);
