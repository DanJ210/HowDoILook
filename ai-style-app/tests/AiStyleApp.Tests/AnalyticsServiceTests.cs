using AiStyleApp.Backend.Services;
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
    public async Task ExportRecommendationsDataAsync_ArrayRecommendations_ParsesTopStyleAndSelectedRank()
    {
        await using var db = CreateDbContext();
        var analysisJobId = Guid.NewGuid();

        db.FaceAnalysisJobs.Add(new FaceAnalysisJobEntity
        {
            Id = analysisJobId,
            UserId = "user-1",
            ImageUrl = "https://example.com/1.jpg",
            Status = "Succeeded",
            AnalysisConfidence = 0.82,
            RecommendationsJson = "[{\"styleId\":\"short-quiff\",\"styleName\":\"Short Quiff\",\"score\":0.92,\"reasons\":[],\"constraints\":[]},{\"styleId\":\"classic-side-part\",\"styleName\":\"Classic Side Part\",\"score\":0.81,\"reasons\":[],\"constraints\":[]}]",
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Feedback =
            [
                new RecommendationFeedbackEntity
                {
                    UserId = "user-1",
                    SelectedStyleId = "classic-side-part",
                    Rating = 4,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        });
        await db.SaveChangesAsync();

        var service = new AnalyticsService(db, NullLogger<AnalyticsService>.Instance);

        var exportRows = (await service.ExportRecommendationsDataAsync()).ToList();

        var row = Assert.Single(exportRows);
        Assert.Equal("short-quiff", row.TopRecommendationStyleId);
        Assert.Equal(0.92, row.TopRecommendationScore, 3);
        Assert.Equal(2, row.RecommendationCount);
        Assert.Equal("classic-side-part", row.SelectedStyleId);
        Assert.Equal(2, row.RecommendationRank);
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

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }
}
