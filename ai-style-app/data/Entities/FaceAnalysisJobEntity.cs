using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiStyleApp.Data.Entities;

[Table("face_analysis_jobs")]
public class FaceAnalysisJobEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("user_id")]
    [MaxLength(128)]
    public required string UserId { get; set; }

    [Column("image_url")]
    [MaxLength(2048)]
    public required string ImageUrl { get; set; }

    [Column("gender")]
    [MaxLength(50)]
    public string? Gender { get; set; }

    [Column("preferences_json", TypeName = "jsonb")]
    public string? PreferencesJson { get; set; }

    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = "Queued";

    [Column("quality_passed")]
    public bool? QualityPassed { get; set; }

    [Column("quality_failure_code")]
    [MaxLength(100)]
    public string? QualityFailureCode { get; set; }

    [Column("quality_message")]
    [MaxLength(1000)]
    public string? QualityMessage { get; set; }

    [Column("feature_vector_json", TypeName = "jsonb")]
    public string? FeatureVectorJson { get; set; }

    [Column("analysis_confidence")]
    public double? AnalysisConfidence { get; set; }

    [Column("recommendations_json", TypeName = "jsonb")]
    public string? RecommendationsJson { get; set; }

    [Column("primary_style_id")]
    [MaxLength(100)]
    public string? PrimaryStyleId { get; set; }

    [Column("primary_style_item_id")]
    public Guid? PrimaryStyleItemId { get; set; }

    [Column("primary_generation_job_id")]
    public Guid? PrimaryGenerationJobId { get; set; }

    [Column("error_code")]
    [MaxLength(100)]
    public string? ErrorCode { get; set; }

    [Column("error_message")]
    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    [Column("created_at_utc")]
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    [Column("started_at_utc")]
    public DateTimeOffset? StartedAtUtc { get; set; }

    [Column("completed_at_utc")]
    public DateTimeOffset? CompletedAtUtc { get; set; }

    [Column("selected_generation_job_id")]
    public Guid? SelectedGenerationJobId { get; set; }

    [Column("selected_at_utc")]
    public DateTimeOffset? SelectedAtUtc { get; set; }

    public ICollection<RecommendationFeedbackEntity> Feedback { get; set; } = new List<RecommendationFeedbackEntity>();
}
