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
        Assert.StartsWith("user-hash-", result.Experiment.BucketKey, StringComparison.Ordinal);
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
