using AiStyleApp.Api.Services;
using AiStyleApp.Data;
using AiStyleApp.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiStyleApp.Tests;

public class StyleServiceTests
{
    [Fact]
    public async Task GetPublicFeedAsync_ExplicitPrimaryLinkage_ExcludesOtherPublicJobs()
    {
        await using var db = CreateDbContext();
        var completedAt = DateTimeOffset.UtcNow;
        var primaryItem = CreatePublicItem("Primary");
        var otherItem = CreatePublicItem("Other");
        var primaryJob = CreateSucceededJob(primaryItem, completedAt);
        var otherJob = CreateSucceededJob(otherItem, completedAt.AddMinutes(1));
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Status = "Succeeded",
            PrimaryStyleItemId = primaryItem.Id,
            PrimaryGenerationJobId = primaryJob.Id
        };

        db.AddRange(primaryJob, otherJob, analysisJob);
        await db.SaveChangesAsync();

        var result = await new StyleService(db).GetPublicFeedAsync(12, null);

        var feedItem = Assert.Single(result.Items);
        Assert.Equal(primaryItem.Id, feedItem.StyleItemId);
        Assert.Equal(primaryJob.Id, feedItem.JobId);
    }

    [Fact]
    public async Task GetPublicFeedAsync_LegacyAnalysisLink_ReturnsLatestPrimaryPostJob()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Status = "Succeeded"
        };
        var primaryItem = CreatePublicItem("Legacy primary");
        primaryItem.Description = $"Primary recommendation from analysis job {analysisJob.Id}.";
        var olderJob = CreateSucceededJob(primaryItem, DateTimeOffset.UtcNow.AddMinutes(-1));
        var latestJob = CreateSucceededJob(primaryItem, DateTimeOffset.UtcNow);

        db.AddRange(olderJob, latestJob, analysisJob);
        await db.SaveChangesAsync();

        var result = await new StyleService(db).GetPublicFeedAsync(12, null);

        var feedItem = Assert.Single(result.Items);
        Assert.Equal(primaryItem.Id, feedItem.StyleItemId);
        Assert.Equal(latestJob.Id, feedItem.JobId);
    }

    private static StyleItemEntity CreatePublicItem(string name) => new()
    {
        UserId = "user-1",
        Name = name,
        Description = "Recommendation",
        Prompt = "prompt",
        IsResultPublic = true
    };

    private static StyleJobEntity CreateSucceededJob(
        StyleItemEntity item,
        DateTimeOffset completedAt) => new()
    {
        StyleItem = item,
        UserId = item.UserId,
        Prompt = item.Prompt,
        Status = "Succeeded",
        ResultImageUrl = "https://example.com/result.jpg",
        CompletedAtUtc = completedAt
    };

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }
}