using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiStyleApp.Data.Entities;

[Table("recommendation_exposure_candidates")]
public class RecommendationExposureCandidateEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("exposure_id")]
    public Guid ExposureId { get; set; }

    [Column("style_id")]
    [MaxLength(100)]
    public required string StyleId { get; set; }

    [Column("style_name")]
    [MaxLength(200)]
    public required string StyleName { get; set; }

    [Column("recommendation_rank")]
    public int RecommendationRank { get; set; }

    [Column("ranking_score")]
    public double RankingScore { get; set; }

    [Column("is_primary")]
    public bool IsPrimary { get; set; }

    [Column("was_shown")]
    public bool WasShown { get; set; }

    [Column("shown_order")]
    public int? ShownOrder { get; set; }

    [Column("selection_probability")]
    public double SelectionProbability { get; set; }

    [Column("style_item_id")]
    public Guid? StyleItemId { get; set; }

    [Column("generation_job_id")]
    public Guid? GenerationJobId { get; set; }

    public RecommendationExposureEntity Exposure { get; set; } = null!;
    public StyleItemEntity? StyleItem { get; set; }
    public StyleJobEntity? GenerationJob { get; set; }
}