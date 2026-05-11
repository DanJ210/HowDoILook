using AiStyleApp.Api.Services;
using AiStyleApp.Data;
using AiStyleApp.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiStyleApp.Tests;

public class JobServiceTests
{
    [Fact]
    public async Task UpdateVisibilityAsync_UpdatesStyleItemVisibility_ForOwningUser()
    {
        await using var db = CreateDbContext();
        var item = new StyleItemEntity
        {
            UserId = "user-1",
            Name = "Layered look",
            Description = "desc",
            Prompt = "prompt",
            IsResultPublic = false
        };

        var job = new StyleJobEntity
        {
            StyleItem = item,
            UserId = "user-1",
            Prompt = "prompt",
            Status = "Succeeded"
        };

        db.StyleJobs.Add(job);
        await db.SaveChangesAsync();

        var service = new JobService(db);

        var updated = await service.UpdateVisibilityAsync(job.Id, "user-1", true);

        Assert.NotNull(updated);
        Assert.True(updated!.IsResultPublic);

        var persisted = await db.StyleItems.FirstAsync(x => x.Id == item.Id);
        Assert.True(persisted.IsResultPublic);
    }

    [Fact]
    public async Task UpdateVisibilityAsync_ReturnsNull_WhenUserDoesNotOwnJob()
    {
        await using var db = CreateDbContext();
        var item = new StyleItemEntity
        {
            UserId = "owner-user",
            Name = "Bob",
            Description = "desc",
            Prompt = "prompt",
            IsResultPublic = false
        };

        var job = new StyleJobEntity
        {
            StyleItem = item,
            UserId = "owner-user",
            Prompt = "prompt"
        };

        db.StyleJobs.Add(job);
        await db.SaveChangesAsync();

        var service = new JobService(db);

        var updated = await service.UpdateVisibilityAsync(job.Id, "other-user", true);

        Assert.Null(updated);

        var persisted = await db.StyleItems.FirstAsync(x => x.Id == item.Id);
        Assert.False(persisted.IsResultPublic);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }
}
