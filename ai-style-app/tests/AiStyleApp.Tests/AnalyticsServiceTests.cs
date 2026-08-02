using AiStyleApp.Api.Services;
using AiStyleApp.Data;
using AiStyleApp.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiStyleApp.Tests;

public class AnalyticsServiceTests
{
    [Fact]
    public async Task GetMetricsAsync_NoAnalysisConfidenceRows_ReturnsZeroAverageConfidence()
    {
        await using var db = CreateDbContext();
        db.FaceAnalysisJobs.AddRange(
            new FaceAnalysisJobEntity
            {
                UserId = "user-1",
                ImageUrl = "https://example.com/1.jpg",
                Status = "Failed",
                AnalysisConfidence = null,
                CompletedAtUtc = DateTimeOffset.UtcNow
            },
            new FaceAnalysisJobEntity
            {
                UserId = "user-2",
                ImageUrl = "https://example.com/2.jpg",
                Status = "Succeeded",
                AnalysisConfidence = null,
                RecommendationsJson = "{\"topStyles\":[]}",
                CompletedAtUtc = DateTimeOffset.UtcNow
            });
        await db.SaveChangesAsync();

        var service = new AnalyticsService(db, NullLogger<AnalyticsService>.Instance);

        var metrics = await service.GetMetricsAsync();

        Assert.Equal(0, metrics.AverageConfidence);
        Assert.Equal(2, metrics.TotalAnalyses);
        Assert.Equal(1, metrics.SuccessfulAnalyses);
        Assert.Equal(1, metrics.FailedAnalyses);
    }

    [Fact]
    public async Task GetMetricsAsync_RankOneFeedback_CountsAsPositivePreference()
    {
        await using var db = CreateDbContext();
        db.FaceAnalysisJobs.Add(new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/1.jpg",
            Status = "Succeeded",
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Feedback =
            [
                new RecommendationFeedbackEntity
                {
                    UserId = "user-1",
                    SelectedStyleId = Guid.NewGuid().ToString(),
                    Rating = 1
                }
            ]
        });
        await db.SaveChangesAsync();

        var service = new AnalyticsService(db, NullLogger<AnalyticsService>.Instance);

        var metrics = await service.GetMetricsAsync();

        Assert.Equal(1, metrics.AnalysesWithFeedback);
        Assert.Equal(1, metrics.PositiveFeedbackRate);
    }

    [Fact]
    public async Task ExportExposureOutcomesAsync_SessionWithoutFeedback_IncludesDecisionAndCandidateOutcomes()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedExposureAsync(db, includeFeedback: false);

        var service = new AnalyticsService(db, NullLogger<AnalyticsService>.Instance);

        var exportRows = await service.ExportExposureOutcomesAsync();

        Assert.Equal(3, exportRows.Count);
        var primary = exportRows.Single(row => row.IsPrimary);
        Assert.Equal(seeded.ExposureId, primary.ExposureId);
        Assert.Equal(seeded.AnalysisJobId, primary.AnalysisJobId);
        Assert.Equal("short-quiff", primary.SystemSelectedStyleId);
        Assert.Equal(seeded.PrimaryGenerationJobId, primary.PrimaryGenerationJobId);
        Assert.Equal(seeded.PrimaryGenerationJobId, primary.GenerationJobId);
        Assert.Equal("Succeeded", primary.GenerationStatus);
        Assert.Equal(2, primary.TelemetrySchemaVersion);
        Assert.Equal("worker-v1-staged-analysis", primary.TelemetrySource);
        Assert.Equal(3, primary.EligibleCandidateCount);
        Assert.Equal(2, primary.ShownCandidateCount);

        var unshown = exportRows.Single(row => !row.WasShown);
        Assert.Equal("textured-crop", unshown.CandidateStyleId);
        Assert.Null(unshown.GenerationJobId);
        Assert.Null(unshown.GenerationStatus);
    }

    [Fact]
    public async Task ExportPreferenceLabelsAsync_OnlyIncludesFeedbackLinkedToShownCandidates()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedExposureAsync(db, includeFeedback: true);

        var service = new AnalyticsService(db, NullLogger<AnalyticsService>.Instance);

        var exportRows = await service.ExportPreferenceLabelsAsync();

        var row = Assert.Single(exportRows);
        Assert.Equal(seeded.ExposureId, row.ExposureId);
        Assert.Equal(seeded.ExperimentalGenerationJobId, row.GenerationJobId);
        Assert.Equal("classic-side-part", row.CandidateStyleId);
        Assert.False(row.IsPrimary);
        Assert.Equal(2, row.ShownOrder);
        Assert.Equal(1, row.SubmittedRank);
        Assert.Equal(0.5, row.SelectionProbability);
    }

    [Fact]
    public async Task GetCoverageAsync_ReportsStyleAndContinuousConfidenceSupport()
    {
        await using var db = CreateDbContext();
        await SeedExposureAsync(db, includeFeedback: true);

        var service = new AnalyticsService(db, NullLogger<AnalyticsService>.Instance);

        var coverage = await service.GetCoverageAsync();

        Assert.Equal(1, coverage.ExposureCount);
        Assert.Equal(1, coverage.PreferenceLabelCount);
        Assert.Equal(20, coverage.MinimumSampleSize);

        var primaryStyle = coverage.Styles.Single(style => style.StyleId == "short-quiff");
        Assert.Equal(1, primaryStyle.EligibleCount);
        Assert.Equal(1, primaryStyle.ShownCount);
        Assert.Equal(1, primaryStyle.SucceededGenerationCount);
        Assert.True(primaryStyle.IsSparse);

        var confidenceRange = coverage.AnalysisConfidenceRanges.Single(range => range.Range == "0.80-0.90");
        Assert.Equal(1, confidenceRange.ExposureCount);
        Assert.Equal(2, confidenceRange.ShownCandidateCount);
        Assert.Equal(1, confidenceRange.PreferenceLabelCount);
        Assert.True(confidenceRange.IsSparse);
    }

    [Fact]
    public async Task GetMetricsAsync_ArrayRecommendations_CountsTopRecommendedStyles()
    {
        await using var db = CreateDbContext();
        db.FaceAnalysisJobs.Add(new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/1.jpg",
            Status = "Succeeded",
            AnalysisConfidence = 0.82,
            RecommendationsJson = "[{\"styleId\":\"short-quiff\",\"styleName\":\"Short Quiff\",\"score\":0.92,\"reasons\":[],\"constraints\":[]},{\"styleId\":\"classic-side-part\",\"styleName\":\"Classic Side Part\",\"score\":0.81,\"reasons\":[],\"constraints\":[]}]",
            CompletedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new AnalyticsService(db, NullLogger<AnalyticsService>.Instance);

        var metrics = await service.GetMetricsAsync();

        Assert.Equal(1, metrics.TopRecommendedStyles["short-quiff"]);
        Assert.Equal(1, metrics.TopRecommendedStyles["classic-side-part"]);
    }

    [Fact]
    public async Task GetMetricsAsync_NonObjectFeatureVector_DoesNotThrowAndCountsUnknownFaceShape()
    {
        await using var db = CreateDbContext();
        db.FaceAnalysisJobs.Add(new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/1.jpg",
            Status = "Succeeded",
            AnalysisConfidence = 0.82,
            FeatureVectorJson = "[1,2,3]",
            RecommendationsJson = "[{\"styleId\":\"short-quiff\",\"styleName\":\"Short Quiff\",\"score\":0.92,\"reasons\":[],\"constraints\":[]}]",
            CompletedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new AnalyticsService(db, NullLogger<AnalyticsService>.Instance);

        var metrics = await service.GetMetricsAsync();

        Assert.Equal(1, metrics.FaceShapeDistribution["Unknown"]);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }

    private static async Task<SeededExposure> SeedExposureAsync(AppDbContext db, bool includeFeedback)
    {
        var analysisJobId = Guid.NewGuid();
        var exposureId = Guid.NewGuid();
        var primaryStyleItemId = Guid.NewGuid();
        var experimentalStyleItemId = Guid.NewGuid();
        var primaryGenerationJobId = Guid.NewGuid();
        var experimentalGenerationJobId = Guid.NewGuid();

        db.StyleItems.AddRange(
            new StyleItemEntity
            {
                Id = primaryStyleItemId,
                UserId = "user-1",
                Name = "Recommended: Short Quiff",
                Description = $"Primary recommendation from analysis job {analysisJobId}.",
                Prompt = "prompt",
                ImageUrl = "https://example.com/1.jpg",
                IsResultPublic = true,
                AnalysisJobId = analysisJobId
            },
            new StyleItemEntity
            {
                Id = experimentalStyleItemId,
                UserId = "user-1",
                Name = "Experimental 1: Classic Side Part",
                Description = $"Experimental recommendation from analysis job {analysisJobId}.",
                Prompt = "prompt",
                ImageUrl = "https://example.com/1.jpg",
                IsResultPublic = false,
                AnalysisJobId = analysisJobId
            });
        db.StyleJobs.AddRange(
            new StyleJobEntity
            {
                Id = primaryGenerationJobId,
                StyleItemId = primaryStyleItemId,
                UserId = "user-1",
                Prompt = "prompt",
                Status = "Succeeded",
                CompletedAtUtc = DateTimeOffset.UtcNow
            },
            new StyleJobEntity
            {
                Id = experimentalGenerationJobId,
                StyleItemId = experimentalStyleItemId,
                UserId = "user-1",
                Prompt = "prompt",
                Status = "Failed",
                ErrorCode = "GENERATION_FAILED",
                CompletedAtUtc = DateTimeOffset.UtcNow
            });

        var feedback = new List<RecommendationFeedbackEntity>();
        if (includeFeedback)
        {
            feedback.AddRange(
                new RecommendationFeedbackEntity
                {
                    UserId = "user-1",
                    SelectedStyleId = experimentalGenerationJobId.ToString(),
                    Rating = 1,
                    FeedbackTagsJson = "[\"preferred\"]"
                },
                new RecommendationFeedbackEntity
                {
                    UserId = "user-1",
                    SelectedStyleId = Guid.NewGuid().ToString(),
                    Rating = 2
                });
        }

        db.FaceAnalysisJobs.Add(new FaceAnalysisJobEntity
        {
            Id = analysisJobId,
            UserId = "user-1",
            ImageUrl = "https://example.com/1.jpg",
            Status = "Succeeded",
            QualityPassed = true,
            AnalysisConfidence = 0.82,
            FeatureVectorJson = "{\"faceShape\":\"Square\",\"schemaVersion\":2,\"source\":\"worker-v1-staged-analysis\"}",
            RecommendationsJson = "[{\"styleId\":\"short-quiff\",\"score\":0.92},{\"styleId\":\"classic-side-part\",\"score\":0.81},{\"styleId\":\"textured-crop\",\"score\":0.74}]",
            PrimaryStyleId = "short-quiff",
            PrimaryStyleItemId = primaryStyleItemId,
            PrimaryGenerationJobId = primaryGenerationJobId,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Feedback = feedback,
            Exposure = new RecommendationExposureEntity
            {
                Id = exposureId,
                UserId = "user-1",
                ExperimentVersion = "controlled-exploration-v1",
                ExperimentApplied = true,
                TelemetrySchemaVersion = 2,
                TelemetrySource = "worker-v1-staged-analysis",
                PrimaryStyleId = "short-quiff",
                PrimaryGenerationJobId = primaryGenerationJobId,
                Candidates =
                [
                    new RecommendationExposureCandidateEntity
                    {
                        StyleId = "short-quiff",
                        StyleName = "Short Quiff",
                        RecommendationRank = 1,
                        RankingScore = 0.92,
                        IsPrimary = true,
                        WasShown = true,
                        ShownOrder = 1,
                        SelectionProbability = 1,
                        StyleItemId = primaryStyleItemId,
                        GenerationJobId = primaryGenerationJobId
                    },
                    new RecommendationExposureCandidateEntity
                    {
                        StyleId = "classic-side-part",
                        StyleName = "Classic Side Part",
                        RecommendationRank = 2,
                        RankingScore = 0.81,
                        WasShown = true,
                        ShownOrder = 2,
                        SelectionProbability = 0.5,
                        StyleItemId = experimentalStyleItemId,
                        GenerationJobId = experimentalGenerationJobId
                    },
                    new RecommendationExposureCandidateEntity
                    {
                        StyleId = "textured-crop",
                        StyleName = "Textured Crop",
                        RecommendationRank = 3,
                        RankingScore = 0.74,
                        WasShown = false,
                        SelectionProbability = 0.5
                    }
                ]
            }
        });
        await db.SaveChangesAsync();

        return new SeededExposure(
            analysisJobId,
            exposureId,
            primaryGenerationJobId,
            experimentalGenerationJobId);
    }

    private sealed record SeededExposure(
        Guid AnalysisJobId,
        Guid ExposureId,
        Guid PrimaryGenerationJobId,
        Guid ExperimentalGenerationJobId);
}
