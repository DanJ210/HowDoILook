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

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }
}
