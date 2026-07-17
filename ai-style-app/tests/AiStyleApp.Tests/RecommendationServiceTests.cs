using AiStyleApp.Api.Services;
using AiStyleApp.Data;
using AiStyleApp.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AiStyleApp.Tests;

public class RecommendationServiceTests
{
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
