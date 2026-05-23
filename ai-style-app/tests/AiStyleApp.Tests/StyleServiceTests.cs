using AiStyleApp.Api.Models;
using AiStyleApp.Api.Services;
using AiStyleApp.Data;
using AiStyleApp.Data.Queue;
using Microsoft.EntityFrameworkCore;

namespace AiStyleApp.Tests;

public class StyleServiceTests
{
    [Fact]
    public async Task CreateAndEnqueueAsync_WithBeardSelections_PublishesQueueMessageWithBeardFields()
    {
        await using var db = CreateDbContext();
        var queue = new TestQueuePublisher();
        var service = new StyleService(db, queue);

        var request = new GenerateStyleRequest(
            Name: "Beard update",
            Description: "Apply beard style and color",
            Prompt: null,
            ImageUrl: "https://example.com/input.jpg",
            IsResultPublic: true,
            Haircut: "No change",
            HairColor: "No change",
            BeardStyle: "Stubble",
            BeardColor: "Dark Brown",
            Gender: "male");

        var (item, jobId) = await service.CreateAndEnqueueAsync(request, "user-1");

        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.NotEqual(Guid.Empty, jobId);

        var message = Assert.IsType<StyleJob>(queue.LastMessage);
        var persistedJob = await db.StyleJobs.FirstAsync(x => x.Id == jobId);
        Assert.Equal("Stubble", message.BeardStyle);
        Assert.Equal("Dark Brown", message.BeardColor);
        Assert.Equal("No change", message.Haircut);
        Assert.Equal("No change", message.HairColor);
        Assert.Equal("male", message.Gender);
        Assert.Equal(StyleJobPipelineMode.BeardOnly, persistedJob.PipelineMode);
        Assert.Equal(StyleJobStage.Queued, persistedJob.CurrentStage);
        Assert.Equal("Stubble", persistedJob.BeardStyle);
        Assert.Equal("Dark Brown", persistedJob.BeardColor);
    }

    [Fact]
    public async Task CreateAndEnqueueAsync_WithoutMaleGender_IgnoresBeardSelections()
    {
        await using var db = CreateDbContext();
        var queue = new TestQueuePublisher();
        var service = new StyleService(db, queue);

        var request = new GenerateStyleRequest(
            Name: "Mixed edit",
            Description: "Fallback description",
            Prompt: null,
            ImageUrl: "https://example.com/input.jpg",
            IsResultPublic: false,
            Haircut: "Layered",
            HairColor: "Honey Blonde",
            BeardStyle: "Goatee",
            BeardColor: "Black",
            Gender: "none");

        var (item, jobId) = await service.CreateAndEnqueueAsync(request, "user-2");

        var persisted = await db.StyleItems.FirstAsync(x => x.Id == item.Id);
        var message = Assert.IsType<StyleJob>(queue.LastMessage);
        var persistedJob = await db.StyleJobs.FirstAsync(x => x.Id == jobId);

        Assert.Contains("Haircut: Layered", persisted.Prompt);
        Assert.Contains("Hair color: Honey Blonde", persisted.Prompt);
        Assert.DoesNotContain("Beard style:", persisted.Prompt);
        Assert.DoesNotContain("Beard color:", persisted.Prompt);
        Assert.DoesNotContain("Gender:", persisted.Prompt);
        Assert.Null(message.BeardStyle);
        Assert.Null(message.BeardColor);
        Assert.Equal(StyleJobPipelineMode.HairOnly, persistedJob.PipelineMode);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }

    private sealed class TestQueuePublisher : IQueuePublisher
    {
        public object? LastMessage { get; private set; }

        public Task PublishAsync<T>(T message, CancellationToken ct = default)
        {
            LastMessage = message;
            return Task.CompletedTask;
        }
    }
}
