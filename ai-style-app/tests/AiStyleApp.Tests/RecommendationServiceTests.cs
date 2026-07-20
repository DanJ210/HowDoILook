using AiStyleApp.Api.Models;
using AiStyleApp.Api.Services;
using AiStyleApp.Data;
using AiStyleApp.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AiStyleApp.Tests;

public class RecommendationServiceTests
{
    [Fact]
    public async Task CreateAndEnqueueAsync_CreatesPublishFirstRecommendationPost()
    {
        await using var db = CreateDbContext();

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var request = new CreateRecommendationsRequest(
            ImageUrl: "https://example.com/photo.jpg",
            Gender: "female",
            Preferences: null);

        var (analysisJobId, recommendationPostId) = await service.CreateAndEnqueueAsync(request, "user-1");

        Assert.NotEqual(Guid.Empty, analysisJobId);
        Assert.NotEqual(Guid.Empty, recommendationPostId);

        var post = await db.StyleItems.FirstOrDefaultAsync(x => x.Id == recommendationPostId);
        Assert.NotNull(post);
        Assert.True(post!.IsResultPublic);
        Assert.Contains(analysisJobId.ToString(), post.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmitRatingsAsync_PersistsOneFeedbackEntryPerRanking()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded"
        };

        db.FaceAnalysisJobs.Add(analysisJob);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var request = new SubmitRecommendationRatingsRequest(
            AnalysisJobId: analysisJob.Id,
            Rankings: new[]
            {
                new RecommendationRankingInput(Guid.NewGuid(), 1),
                new RecommendationRankingInput(Guid.NewGuid(), 2)
            },
            FeedbackTags: new[] { "greatMatch" },
            Comment: "top 2 look strong");

        await service.SubmitRatingsAsync(analysisJob.Id, request, "user-1");

        var saved = db.RecommendationFeedback
            .Where(x => x.AnalysisJobId == analysisJob.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .ToList();

        Assert.Equal(2, saved.Count);
        Assert.Equal(1, saved[0].Rating);
        Assert.Equal(2, saved[1].Rating);
        Assert.Equal("top 2 look strong", saved[0].Comment);
    }

    [Fact]
    public async Task SubmitRatingsAsync_InvalidRank_ThrowsArgumentException()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded"
        };

        db.FaceAnalysisJobs.Add(analysisJob);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var request = new SubmitRecommendationRatingsRequest(
            AnalysisJobId: analysisJob.Id,
            Rankings: new[]
            {
                new RecommendationRankingInput(Guid.NewGuid(), 4)
            },
            FeedbackTags: null,
            Comment: null);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitRatingsAsync(analysisJob.Id, request, "user-1"));
    }

    [Fact]
    public async Task SubmitRatingsAsync_DuplicateGenerationJobId_ThrowsArgumentException()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded"
        };

        db.FaceAnalysisJobs.Add(analysisJob);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var duplicateJobId = Guid.NewGuid();
        var request = new SubmitRecommendationRatingsRequest(
            AnalysisJobId: analysisJob.Id,
            Rankings: new[]
            {
                new RecommendationRankingInput(duplicateJobId, 1),
                new RecommendationRankingInput(duplicateJobId, 2)
            },
            FeedbackTags: null,
            Comment: null);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitRatingsAsync(analysisJob.Id, request, "user-1"));
    }

    [Fact]
    public async Task SubmitRatingsAsync_DuplicateRank_ThrowsArgumentException()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded"
        };

        db.FaceAnalysisJobs.Add(analysisJob);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var request = new SubmitRecommendationRatingsRequest(
            AnalysisJobId: analysisJob.Id,
            Rankings: new[]
            {
                new RecommendationRankingInput(Guid.NewGuid(), 1),
                new RecommendationRankingInput(Guid.NewGuid(), 1)
            },
            FeedbackTags: null,
            Comment: null);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitRatingsAsync(analysisJob.Id, request, "user-1"));
    }

    [Fact]
    public async Task GetStatusAsync_PopulatesBestAndExperimentalVariants_FromLinkedStyleItems()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded",
            QualityPassed = true,
            AnalysisConfidence = 0.82,
            RecommendationsJson = "[{\"styleId\":\"textured-crop\",\"styleName\":\"Textured Crop\",\"score\":0.94,\"reasons\":[],\"constraints\":[]}]",
            FeatureVectorJson = "{\"faceShape\":\"Square\"}"
        };

        db.FaceAnalysisJobs.Add(analysisJob);

        var primaryItem = new StyleItemEntity
        {
            UserId = "user-1",
            Name = "Recommended: Textured Crop",
            Description = $"Primary recommendation from analysis job {analysisJob.Id}",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            IsResultPublic = true
        };

        var primaryJob = new StyleJobEntity
        {
            StyleItemId = primaryItem.Id,
            UserId = "user-1",
            JobType = "generate-style",
            Status = "Succeeded",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            ResultImageUrl = "https://example.com/best.jpg"
        };
        primaryItem.Jobs.Add(primaryJob);

        var experimentalItem = new StyleItemEntity
        {
            UserId = "user-1",
            Name = "Experimental 1: Classic Side Part",
            Description = $"Experimental recommendation from analysis job {analysisJob.Id}",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            IsResultPublic = false
        };

        var experimentalJob = new StyleJobEntity
        {
            StyleItemId = experimentalItem.Id,
            UserId = "user-1",
            JobType = "generate-style",
            Status = "Processing",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl
        };
        experimentalItem.Jobs.Add(experimentalJob);

        db.StyleItems.AddRange(primaryItem, experimentalItem);
        db.RecommendationFeedback.Add(new RecommendationFeedbackEntity
        {
            AnalysisJobId = analysisJob.Id,
            UserId = "user-1",
            SelectedStyleId = experimentalJob.Id.ToString(),
            Rating = 2
        });

        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Features:ExperimentationModeEnabled"] = "true",
            ["Features:ExperimentationTrafficPercent"] = "100"
        }).Build();

        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);
        var result = await service.GetStatusAsync(analysisJob.Id, "user-1");

        Assert.NotNull(result);
        Assert.Equal(primaryItem.Id, result!.RecommendationPostId);
        Assert.Equal("Published", result.PublishStatus);
        Assert.Equal("Square", result.AnalysisSummary.FaceShape);
        Assert.NotNull(result.BestVariant);
        Assert.Equal(primaryJob.Id, result.BestVariant!.GenerationJobId);
        Assert.Single(result.ExperimentalVariants);
        Assert.Equal(experimentalJob.Id, result.ExperimentalVariants[0].GenerationJobId);
        Assert.Equal("2", result.ExperimentalVariants[0].SelectedRank);
    }

    [Fact]
    public async Task GetStatusAsync_DuplicateFeedbackForSameSelection_UsesLatestEntry()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded",
            QualityPassed = true,
            AnalysisConfidence = 0.82,
            RecommendationsJson = "[]",
            FeatureVectorJson = "{\"faceShape\":\"Square\"}"
        };

        db.FaceAnalysisJobs.Add(analysisJob);

        var primaryItem = new StyleItemEntity
        {
            UserId = "user-1",
            Name = "Recommended",
            Description = $"Primary recommendation from analysis job {analysisJob.Id}",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            IsResultPublic = true
        };
        primaryItem.Jobs.Add(new StyleJobEntity
        {
            StyleItemId = primaryItem.Id,
            UserId = "user-1",
            JobType = "generate-style",
            Status = "Succeeded",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            ResultImageUrl = "https://example.com/best.jpg"
        });

        var experimentalItem = new StyleItemEntity
        {
            UserId = "user-1",
            Name = "Experimental",
            Description = $"Experimental recommendation from analysis job {analysisJob.Id}",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            IsResultPublic = false
        };

        var experimentalJob = new StyleJobEntity
        {
            StyleItemId = experimentalItem.Id,
            UserId = "user-1",
            JobType = "generate-style",
            Status = "Succeeded",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            ResultImageUrl = "https://example.com/exp.jpg"
        };
        experimentalItem.Jobs.Add(experimentalJob);

        db.StyleItems.AddRange(primaryItem, experimentalItem);
        db.RecommendationFeedback.AddRange(
            new RecommendationFeedbackEntity
            {
                AnalysisJobId = analysisJob.Id,
                UserId = "user-1",
                SelectedStyleId = experimentalJob.Id.ToString(),
                Rating = 1,
                CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            },
            new RecommendationFeedbackEntity
            {
                AnalysisJobId = analysisJob.Id,
                UserId = "user-1",
                SelectedStyleId = experimentalJob.Id.ToString(),
                Rating = 2,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });

        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Features:ExperimentationModeEnabled"] = "true",
            ["Features:ExperimentationTrafficPercent"] = "100"
        }).Build();

        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);
        var result = await service.GetStatusAsync(analysisJob.Id, "user-1");

        Assert.NotNull(result);
        Assert.Single(result!.ExperimentalVariants);
        Assert.Equal("2", result.ExperimentalVariants[0].SelectedRank);
    }

    [Fact]
    public async Task GetStatusAsync_UsesExperimentMetadataFromFeatureVector_WhenPresent()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded",
            QualityPassed = true,
            AnalysisConfidence = 0.82,
            RecommendationsJson = "[]",
            FeatureVectorJson = """
            {
              "experiment": {
                "enabled": true,
                "trafficPercent": 25,
                "applied": false,
                "bucketKey": "user-hash-91"
              }
            }
            """
        };

        db.FaceAnalysisJobs.Add(analysisJob);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Features:ExperimentationModeEnabled"] = "true",
            ["Features:ExperimentationTrafficPercent"] = "100"
        }).Build();

        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var result = await service.GetStatusAsync(analysisJob.Id, "user-1");

        Assert.NotNull(result);
        Assert.NotNull(result!.Experiment);
        Assert.True(result.Experiment!.Enabled);
        Assert.Equal(25, result.Experiment.TrafficPercent);
        Assert.False(result.Experiment.Applied);
        Assert.Equal("user-hash-91", result.Experiment.BucketKey);
    }

    [Fact]
    public async Task GetStatusAsync_FallsBackToConfigExperimentMetadata_WhenFeatureVectorHasNoExperiment()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded",
            QualityPassed = true,
            AnalysisConfidence = 0.82,
            RecommendationsJson = "[]",
            FeatureVectorJson = "{\"source\":\"worker-v1\"}"
        };

        db.FaceAnalysisJobs.Add(analysisJob);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Features:ExperimentationModeEnabled"] = "true",
            ["Features:ExperimentationTrafficPercent"] = "100"
        }).Build();

        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var result = await service.GetStatusAsync(analysisJob.Id, "user-1");

        Assert.NotNull(result);
        Assert.NotNull(result!.Experiment);
        Assert.True(result.Experiment!.Enabled);
        Assert.Equal(100, result.Experiment.TrafficPercent);
        Assert.True(result.Experiment.Applied);
        Assert.Equal("user-hash-42", result.Experiment.BucketKey);
    }

    [Fact]
    public async Task GetStatusAsync_MalformedFeatureVector_ReturnsNullDebugTelemetry()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded",
            QualityPassed = true,
            AnalysisConfidence = 0.82,
            RecommendationsJson = "[]",
            FeatureVectorJson = "[1,2,3]"
        };

        db.FaceAnalysisJobs.Add(analysisJob);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Features:ExperimentationModeEnabled"] = "true",
            ["Features:ExperimentationTrafficPercent"] = "100"
        }).Build();

        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var result = await service.GetStatusAsync(analysisJob.Id, "user-1");

        Assert.NotNull(result);
        Assert.Null(result!.DebugTelemetry);
        Assert.Equal("Succeeded", result.Status);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }

    private sealed class StubQueuePublisher : IQueuePublisher
    {
        public Task PublishAsync<T>(T message, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class StubMetricsLogger : IMetricsLogger
    {
        public void LogAnalysisJobCompleted(Guid jobId, string userId, bool qualityPassed, string? qualityFailureCode, double? analysisConfidence, string? faceShape, int recommendationCount, TimeSpan duration) { }
        public void LogAnalysisJobFailed(Guid jobId, string userId, string errorCode, string errorMessage, TimeSpan duration) { }
        public void LogRecommendationFeedbackSubmitted(Guid jobId, string userId, string? selectedStyleId, int? rating, string? tags, int? recommendationRank) { }
    }
}
