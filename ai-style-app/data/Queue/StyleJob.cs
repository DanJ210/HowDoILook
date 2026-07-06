namespace AiStyleApp.Data.Queue;

/// <summary>Queue message contract v1 for style generation jobs.</summary>
public record StyleJob(
    Guid JobId,
    Guid StyleItemId,
    string UserId,
    string JobType,
    string Prompt,
    DateTimeOffset EnqueuedAtUtc,
    string CorrelationId,
    int Attempt,
    int SchemaVersion = 1,
    string? ImageUrl = null,
    string? Haircut = null,
    string? HairColor = null,
    string? BeardStyle = null,
    string? BeardColor = null,
    string? Gender = null,
    string? Stage = null
);
