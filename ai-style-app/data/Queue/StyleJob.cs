namespace AiStyleApp.Data.Queue;

/// <summary>Queue message contract for style generation and face-analysis jobs.</summary>
public record StyleJob(
    Guid JobId,
    Guid StyleItemId,
    string UserId,
    string JobType,
    string Prompt,
    DateTimeOffset EnqueuedAtUtc,
    string CorrelationId,
    int Attempt,
    int SchemaVersion = 2,
    string? ImageUrl = null,
    string? Haircut = null,
    string? HairColor = null,
    string? BeardStyle = null,
    string? BeardColor = null,
    string? Gender = null,
    string? Stage = null,
    string? PreferencesJson = null
);
