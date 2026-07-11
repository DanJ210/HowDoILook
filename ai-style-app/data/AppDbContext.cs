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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<StyleItemEntity>(e =>
        {
            e.HasIndex(x => x.UserId).HasDatabaseName("ix_style_items_user_id");
            e.HasIndex(x => x.CreatedAtUtc).HasDatabaseName("ix_style_items_created_at");
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
    }
}
