using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiStyleApp.Data.Entities;

[Table("style_jobs")]
public class StyleJobEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("style_item_id")]
    public Guid StyleItemId { get; set; }

    [Column("user_id")]
    [MaxLength(128)]
    public required string UserId { get; set; }

    [Column("job_type")]
    [MaxLength(100)]
    public string JobType { get; set; } = "generate-style";

    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = "Queued";

    [Column("prompt")]
    [MaxLength(4000)]
    public required string Prompt { get; set; }

    [Column("external_prediction_id")]
    [MaxLength(200)]
    public string? ExternalPredictionId { get; set; }

    [Column("result_json", TypeName = "jsonb")]
    public string? ResultJson { get; set; }

    [Column("error_code")]
    [MaxLength(100)]
    public string? ErrorCode { get; set; }

    [Column("error_message")]
    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    [Column("attempt_count")]
    public int AttemptCount { get; set; } = 0;

    [Column("max_attempts")]
    public int MaxAttempts { get; set; } = 3;

    [Column("correlation_id")]
    [MaxLength(100)]
    public string? CorrelationId { get; set; }

    [Column("created_at_utc")]
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    [Column("started_at_utc")]
    public DateTimeOffset? StartedAtUtc { get; set; }

    [Column("completed_at_utc")]
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public StyleItemEntity StyleItem { get; set; } = null!;
}
