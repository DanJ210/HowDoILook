using AiStyleApp.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiStyleApp.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<StyleItemEntity> StyleItems => Set<StyleItemEntity>();
    public DbSet<StyleJobEntity> StyleJobs => Set<StyleJobEntity>();
    public DbSet<FaceAnalysisJobEntity> FaceAnalysisJobs => Set<FaceAnalysisJobEntity>();
    public DbSet<RecommendationFeedbackEntity> RecommendationFeedback => Set<RecommendationFeedbackEntity>();
    public DbSet<RecommendationExposureEntity> RecommendationExposures => Set<RecommendationExposureEntity>();
    public DbSet<RecommendationExposureCandidateEntity> RecommendationExposureCandidates => Set<RecommendationExposureCandidateEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<StyleItemEntity>(e =>
        {
            e.HasIndex(x => x.UserId).HasDatabaseName("ix_style_items_user_id");
            e.HasIndex(x => x.CreatedAtUtc).HasDatabaseName("ix_style_items_created_at");
            e.HasIndex(x => x.AnalysisJobId).HasDatabaseName("ix_style_items_analysis_job_id");

            e.HasOne<FaceAnalysisJobEntity>()
             .WithMany()
             .HasForeignKey(x => x.AnalysisJobId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<StyleJobEntity>(e =>
        {
            e.HasIndex(x => x.StyleItemId).HasDatabaseName("ix_style_jobs_style_item_id");
            e.HasIndex(x => x.Status).HasDatabaseName("ix_style_jobs_status");
            e.HasIndex(x => x.UserId).HasDatabaseName("ix_style_jobs_user_id");
            e.HasIndex(x => x.ExternalPredictionId).HasDatabaseName("ix_style_jobs_external_prediction_id");

            e.HasOne(x => x.StyleItem)
             .WithMany(x => x.Jobs)
             .HasForeignKey(x => x.StyleItemId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FaceAnalysisJobEntity>(e =>
        {
            e.HasIndex(x => x.UserId).HasDatabaseName("ix_face_analysis_jobs_user_id");
            e.HasIndex(x => x.Status).HasDatabaseName("ix_face_analysis_jobs_status");
            e.HasIndex(x => x.PrimaryStyleItemId).HasDatabaseName("ix_face_analysis_jobs_primary_style_item_id");
            e.HasIndex(x => x.PrimaryGenerationJobId).HasDatabaseName("ix_face_analysis_jobs_primary_generation_job_id");

            e.HasOne<StyleItemEntity>()
             .WithMany()
             .HasForeignKey(x => x.PrimaryStyleItemId)
             .OnDelete(DeleteBehavior.SetNull);

            e.HasOne<StyleJobEntity>()
             .WithMany()
             .HasForeignKey(x => x.PrimaryGenerationJobId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RecommendationFeedbackEntity>(e =>
        {
            e.HasIndex(x => x.UserId).HasDatabaseName("ix_recommendation_feedback_user_id");
            e.HasIndex(x => x.AnalysisJobId).HasDatabaseName("ix_recommendation_feedback_analysis_job_id");

            e.HasOne(x => x.AnalysisJob)
             .WithMany(x => x.Feedback)
             .HasForeignKey(x => x.AnalysisJobId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecommendationExposureEntity>(e =>
        {
            e.HasIndex(x => x.AnalysisJobId)
             .IsUnique()
             .HasDatabaseName("ux_recommendation_exposures_analysis_job_id");
            e.HasIndex(x => x.UserId).HasDatabaseName("ix_recommendation_exposures_user_id");
            e.HasIndex(x => x.CreatedAtUtc).HasDatabaseName("ix_recommendation_exposures_created_at");
            e.HasIndex(x => x.PrimaryGenerationJobId)
             .HasDatabaseName("ix_recommendation_exposures_primary_generation_job_id");

            e.HasOne(x => x.AnalysisJob)
             .WithOne(x => x.Exposure)
             .HasForeignKey<RecommendationExposureEntity>(x => x.AnalysisJobId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.PrimaryGenerationJob)
             .WithMany()
             .HasForeignKey(x => x.PrimaryGenerationJobId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecommendationExposureCandidateEntity>(e =>
        {
            e.HasIndex(x => new { x.ExposureId, x.StyleId })
             .IsUnique()
             .HasDatabaseName("ux_recommendation_exposure_candidates_exposure_style");
            e.HasIndex(x => x.GenerationJobId)
             .HasDatabaseName("ix_recommendation_exposure_candidates_generation_job_id");

            e.HasOne(x => x.Exposure)
             .WithMany(x => x.Candidates)
             .HasForeignKey(x => x.ExposureId)
             .OnDelete(DeleteBehavior.Cascade);

              e.HasOne(x => x.StyleItem)
               .WithMany()
               .HasForeignKey(x => x.StyleItemId)
               .OnDelete(DeleteBehavior.Restrict);

              e.HasOne(x => x.GenerationJob)
               .WithMany()
               .HasForeignKey(x => x.GenerationJobId)
               .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
