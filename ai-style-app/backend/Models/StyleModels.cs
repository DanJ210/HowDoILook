namespace AiStyleApp.Api.Models;

public record StyleItemResponse(
    Guid Id,
    string Name,
    string Description,
    string? ImageUrl,
    bool IsResultPublic,
    DateTimeOffset CreatedAt,
    Guid? LatestJobId,
    string? LatestJobStatus
);

public record CreateStyleItemRequest(
    string Name,
    string Description
);

// ── Generate-style (async) ───────────────────────────────────────────────────

public record GenerateStyleRequest(
    string Name,
    string Description,
    string? Prompt = null,
    string? ImageUrl = null,
    bool IsResultPublic = false,
    string? Haircut = null,
    string? HairColor = null,
    string? BeardStyle = null,
    string? BeardColor = null,
    string? Gender = null
);

public record GenerateStyleResponse(
    Guid JobId,
    Guid StyleItemId,
    string Status,
    string StatusEndpoint
);

public record PublicFeedItemResponse(
    Guid StyleItemId,
    Guid JobId,
    string Name,
    string Description,
    string ResultImageUrl,
    DateTimeOffset PublishedAtUtc
);

public record FeedPageResponse(
    IReadOnlyList<PublicFeedItemResponse> Items,
    bool HasMore
);

public record UserJobSummaryResponse(
    Guid JobId,
    Guid StyleItemId,
    string StyleName,
    string Status,
    string? ResultImageUrl,
    bool IsResultPublic,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc
);

public record UpdateJobVisibilityRequest(
    bool IsResultPublic
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
    string? ResultImageUrl,
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
