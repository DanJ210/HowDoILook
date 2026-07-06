using System.Net;
using System.Text.Json;
using AiStyleApp.Data;
using AiStyleApp.Data.Entities;
using AiStyleApp.Data.Queue;
using AiStyleApp.Worker.Handlers;
using AiStyleApp.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiStyleApp.Tests;

public class StyleJobHandlerTests
{
    [Fact]
    public async Task HandleAsync_HairThenBeardJob_SubmitsHairStageFirst()
    {
        await using var db = CreateDbContext();
        var jobEntity = await SeedJobAsync(db,
            pipelineMode: StyleJobPipelineMode.HairThenBeard,
            currentStage: StyleJobStage.Queued,
            isBeardStagePending: true);

        var client = new TestReplicateWorkerClient
        {
            HairPredictionId = "hair-prediction-id"
        };

        var handler = CreateHandler(db, client);
        var message = new StyleJob(
            JobId: jobEntity.Id,
            StyleItemId: jobEntity.StyleItemId,
            UserId: jobEntity.UserId,
            JobType: jobEntity.JobType,
            Prompt: jobEntity.Prompt,
            EnqueuedAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: jobEntity.CorrelationId ?? Guid.NewGuid().ToString(),
            Attempt: 0,
            SchemaVersion: 2,
            ImageUrl: jobEntity.ImageUrl,
            Haircut: jobEntity.Haircut,
            HairColor: jobEntity.HairColor,
            BeardStyle: jobEntity.BeardStyle,
            BeardColor: jobEntity.BeardColor,
            Gender: jobEntity.Gender);

        await handler.HandleAsync(JsonSerializer.Serialize(message), CancellationToken.None);

        var persisted = await db.StyleJobs.FirstAsync(x => x.Id == jobEntity.Id);
        Assert.Equal(1, client.HairCalls);
        Assert.Equal(0, client.BeardCalls);
        Assert.Equal(StyleJobStage.Hair, persisted.CurrentStage);
        Assert.Equal("hair-prediction-id", persisted.ExternalPredictionId);
    }

    [Fact]
    public async Task HandleAsync_BeardStageMessage_SubmitsBeardStage()
    {
        await using var db = CreateDbContext();
        var jobEntity = await SeedJobAsync(db,
            pipelineMode: StyleJobPipelineMode.BeardOnly,
            currentStage: StyleJobStage.Beard,
            isBeardStagePending: false);
        jobEntity.IntermediateImageUrl = "https://example.com/intermediate.webp";
        await db.SaveChangesAsync();

        var client = new TestReplicateWorkerClient
        {
            BeardPredictionId = "beard-prediction-id"
        };

        var handler = CreateHandler(db, client);
        var message = new StyleJob(
            JobId: jobEntity.Id,
            StyleItemId: jobEntity.StyleItemId,
            UserId: jobEntity.UserId,
            JobType: jobEntity.JobType,
            Prompt: jobEntity.Prompt,
            EnqueuedAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: jobEntity.CorrelationId ?? Guid.NewGuid().ToString(),
            Attempt: 1,
            SchemaVersion: 2,
            ImageUrl: "https://example.com/intermediate.webp",
            Haircut: jobEntity.Haircut,
            HairColor: jobEntity.HairColor,
            BeardStyle: jobEntity.BeardStyle,
            BeardColor: jobEntity.BeardColor,
            Gender: jobEntity.Gender,
            Stage: StyleJobStage.Beard);

        await handler.HandleAsync(JsonSerializer.Serialize(message), CancellationToken.None);

        var persisted = await db.StyleJobs.FirstAsync(x => x.Id == jobEntity.Id);
        Assert.Equal(0, client.HairCalls);
        Assert.Equal(1, client.BeardCalls);
        Assert.Contains("Stubble", client.LastBeardInput!.Prompt);
        Assert.Contains("Dark Brown", client.LastBeardInput.Prompt);
        Assert.Equal(StyleJobStage.Beard, persisted.CurrentStage);
        Assert.Equal("beard-prediction-id", persisted.ExternalPredictionId);
    }

    private static StyleJobHandler CreateHandler(AppDbContext db, TestReplicateWorkerClient client)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Replicate:WebhookBaseUrl"] = "https://api.example.com"
            })
            .Build();

        return new StyleJobHandler(
            NullLogger<StyleJobHandler>.Instance,
            db,
            client,
            new TestHttpClientFactory(),
            configuration);
    }

    private static async Task<StyleJobEntity> SeedJobAsync(
        AppDbContext db,
        string pipelineMode,
        string currentStage,
        bool isBeardStagePending)
    {
        var item = new StyleItemEntity
        {
            UserId = "user-1",
            Name = "Style",
            Description = "desc",
            Prompt = "prompt",
            ImageUrl = "https://example.com/input.jpg"
        };

        var job = new StyleJobEntity
        {
            StyleItem = item,
            UserId = "user-1",
            Prompt = "prompt",
            ImageUrl = "https://example.com/input.jpg",
            CorrelationId = Guid.NewGuid().ToString(),
            Haircut = "Layered",
            HairColor = "Honey Blonde",
            BeardStyle = "Stubble",
            BeardColor = "Dark Brown",
            Gender = "male",
            PipelineMode = pipelineMode,
            CurrentStage = currentStage,
            IsBeardStagePending = isBeardStagePending
        };

        db.StyleJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }

    private sealed class TestReplicateWorkerClient : IReplicateWorkerClient
    {
        public int HairCalls { get; private set; }
        public int BeardCalls { get; private set; }
        public string HairPredictionId { get; init; } = "hair-prediction";
        public string BeardPredictionId { get; init; } = "beard-prediction";
        public BeardStyleInput? LastBeardInput { get; private set; }

        public Task<string> CreateHairPredictionAsync(HaircutStyleInput input, string webhookUrl, CancellationToken ct = default)
        {
            HairCalls++;
            return Task.FromResult(HairPredictionId);
        }

        public Task<string> CreateBeardPredictionAsync(BeardStyleInput input, string webhookUrl, CancellationToken ct = default)
        {
            BeardCalls++;
            LastBeardInput = input;
            return Task.FromResult(BeardPredictionId);
        }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(new TestHttpMessageHandler()) { BaseAddress = new Uri("https://example.com") };
    }

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
