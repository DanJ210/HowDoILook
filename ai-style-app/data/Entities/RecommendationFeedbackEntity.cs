using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiStyleApp.Data.Entities;

[Table("recommendation_feedback")]
public class RecommendationFeedbackEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("analysis_job_id")]
    public Guid AnalysisJobId { get; set; }

    [Column("user_id")]
    [MaxLength(128)]
    public required string UserId { get; set; }

    [Column("selected_style_id")]
    [MaxLength(100)]
    public string? SelectedStyleId { get; set; }

    [Column("rating")]
    public int? Rating { get; set; }

    [Column("feedback_tags_json", TypeName = "jsonb")]
    public string? FeedbackTagsJson { get; set; }

    [Column("comment")]
    [MaxLength(1000)]
    public string? Comment { get; set; }

    [Column("created_at_utc")]
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public FaceAnalysisJobEntity AnalysisJob { get; set; } = null!;
}
