using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiStyleApp.Data.Entities;

[Table("recommendation_exposures")]
public class RecommendationExposureEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("analysis_job_id")]
    public Guid AnalysisJobId { get; set; }

    [Column("user_id")]
    [MaxLength(128)]
    public required string UserId { get; set; }

    [Column("experiment_version")]
    [MaxLength(100)]
    public required string ExperimentVersion { get; set; }

    [Column("experiment_applied")]
    public bool ExperimentApplied { get; set; }

    [Column("telemetry_schema_version")]
    public int? TelemetrySchemaVersion { get; set; }

    [Column("telemetry_source")]
    [MaxLength(100)]
    public string? TelemetrySource { get; set; }

    [Column("primary_style_id")]
    [MaxLength(100)]
    public required string PrimaryStyleId { get; set; }

    [Column("primary_generation_job_id")]
    public Guid PrimaryGenerationJobId { get; set; }

    [Column("created_at_utc")]
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public FaceAnalysisJobEntity AnalysisJob { get; set; } = null!;
    public StyleJobEntity PrimaryGenerationJob { get; set; } = null!;
    public ICollection<RecommendationExposureCandidateEntity> Candidates { get; set; } = new List<RecommendationExposureCandidateEntity>();
}