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
            ImageUrl: "https://example.com/stale-original.webp",
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
        Assert.Equal("https://example.com/intermediate.webp", client.LastBeardInput!.InputImageUrl);
        Assert.Contains("Stubble", client.LastBeardInput!.Prompt);
        Assert.Contains("Dark Brown", client.LastBeardInput.Prompt);
        Assert.Equal(StyleJobStage.Beard, persisted.CurrentStage);
        Assert.Equal("beard-prediction-id", persisted.ExternalPredictionId);
    }

    [Fact]
    public async Task HandleAsync_DuplicateQueueDelivery_DoesNotSubmitAnotherPrediction()
    {
        await using var db = CreateDbContext();
        var jobEntity = await SeedJobAsync(db,
            pipelineMode: StyleJobPipelineMode.HairOnly,
            currentStage: StyleJobStage.Queued,
            isBeardStagePending: false);
        var client = new TestReplicateWorkerClient();
        var handler = CreateHandler(db, client);
        var message = new StyleJob(
            JobId: jobEntity.Id,
            StyleItemId: jobEntity.StyleItemId,
            UserId: jobEntity.UserId,
            JobType: jobEntity.JobType,
            Prompt: jobEntity.Prompt,
            EnqueuedAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: jobEntity.CorrelationId!,
            Attempt: 0,
            ImageUrl: jobEntity.ImageUrl,
            Haircut: jobEntity.Haircut,
            HairColor: jobEntity.HairColor,
            Gender: jobEntity.Gender);
        var body = JsonSerializer.Serialize(message);

        await handler.HandleAsync(body, CancellationToken.None);
        await handler.HandleAsync(body, CancellationToken.None);

        var persisted = await db.StyleJobs.FirstAsync(x => x.Id == jobEntity.Id);
        Assert.Equal(1, client.HairCalls);
        Assert.Equal(1, persisted.AttemptCount);
        Assert.Equal("hair-prediction", persisted.ExternalPredictionId);
    }

    [Fact]
    public async Task HandleAsync_OriginalStageRedeliveredAfterBeardHandoff_DoesNotSubmitPrediction()
    {
        await using var db = CreateDbContext();
        var jobEntity = await SeedJobAsync(db,
            pipelineMode: StyleJobPipelineMode.HairThenBeard,
            currentStage: StyleJobStage.Beard,
            isBeardStagePending: false);
        jobEntity.IntermediateImageUrl = "https://example.com/intermediate.webp";
        await db.SaveChangesAsync();

        var client = new TestReplicateWorkerClient();
        var handler = CreateHandler(db, client);
        var staleInitialMessage = new StyleJob(
            JobId: jobEntity.Id,
            StyleItemId: jobEntity.StyleItemId,
            UserId: jobEntity.UserId,
            JobType: jobEntity.JobType,
            Prompt: jobEntity.Prompt,
            EnqueuedAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: jobEntity.CorrelationId!,
            Attempt: 0,
            ImageUrl: jobEntity.ImageUrl,
            Haircut: jobEntity.Haircut,
            HairColor: jobEntity.HairColor,
            BeardStyle: jobEntity.BeardStyle,
            BeardColor: jobEntity.BeardColor,
            Gender: jobEntity.Gender,
            Stage: null);

        await handler.HandleAsync(JsonSerializer.Serialize(staleInitialMessage), CancellationToken.None);

        var persisted = await db.StyleJobs.FirstAsync(x => x.Id == jobEntity.Id);
        Assert.Equal(0, client.HairCalls);
        Assert.Equal(0, client.BeardCalls);
        Assert.Equal(0, persisted.AttemptCount);
        Assert.Equal(StyleJobStage.Beard, persisted.CurrentStage);
        Assert.Null(persisted.ExternalPredictionId);
    }

    [Fact]
    public async Task HandleAsync_SubmissionFailures_CountEachAttemptAndStopAtMaximum()
    {
        await using var db = CreateDbContext();
        var jobEntity = await SeedJobAsync(db,
            pipelineMode: StyleJobPipelineMode.HairOnly,
            currentStage: StyleJobStage.Queued,
            isBeardStagePending: false);
        jobEntity.MaxAttempts = 2;
        await db.SaveChangesAsync();

        var client = new TestReplicateWorkerClient
        {
            SubmissionException = new InvalidOperationException("Replicate unavailable.")
        };
        var handler = CreateHandler(db, client);
        var message = new StyleJob(
            JobId: jobEntity.Id,
            StyleItemId: jobEntity.StyleItemId,
            UserId: jobEntity.UserId,
            JobType: jobEntity.JobType,
            Prompt: jobEntity.Prompt,
            EnqueuedAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: jobEntity.CorrelationId!,
            Attempt: 0,
            ImageUrl: jobEntity.ImageUrl,
            Haircut: jobEntity.Haircut,
            HairColor: jobEntity.HairColor,
            Gender: jobEntity.Gender);
        var body = JsonSerializer.Serialize(message);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(body, CancellationToken.None));
        var retrying = await db.StyleJobs.FirstAsync(x => x.Id == jobEntity.Id);
        Assert.Equal(1, retrying.AttemptCount);
        Assert.Equal("Queued", retrying.Status);

        await handler.HandleAsync(body, CancellationToken.None);

        var failed = await db.StyleJobs.FirstAsync(x => x.Id == jobEntity.Id);
        Assert.Equal(2, client.HairCalls);
        Assert.Equal(2, failed.AttemptCount);
        Assert.Equal("Failed", failed.Status);
        Assert.Equal("replicate_submission_failed", failed.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_DelayedFailureAfterStageAdvance_DoesNotRegressJob()
    {
        await using var db = CreateDbContext();
        var jobEntity = await SeedJobAsync(db,
            pipelineMode: StyleJobPipelineMode.HairThenBeard,
            currentStage: StyleJobStage.Queued,
            isBeardStagePending: true);
        var client = new TestReplicateWorkerClient
        {
            BeforeSubmissionException = () =>
            {
                jobEntity.CurrentStage = StyleJobStage.Beard;
                jobEntity.Status = "Queued";
                jobEntity.IntermediateImageUrl = "https://example.com/intermediate.webp";
                db.SaveChanges();
            },
            SubmissionException = new InvalidOperationException("Late hair submission failure.")
        };
        var handler = CreateHandler(db, client);
        var message = new StyleJob(
            JobId: jobEntity.Id,
            StyleItemId: jobEntity.StyleItemId,
            UserId: jobEntity.UserId,
            JobType: jobEntity.JobType,
            Prompt: jobEntity.Prompt,
            EnqueuedAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: jobEntity.CorrelationId!,
            Attempt: 0,
            ImageUrl: jobEntity.ImageUrl,
            Haircut: jobEntity.Haircut,
            HairColor: jobEntity.HairColor,
            Gender: jobEntity.Gender);

        await handler.HandleAsync(JsonSerializer.Serialize(message), CancellationToken.None);

        var persisted = await db.StyleJobs.FirstAsync(x => x.Id == jobEntity.Id);
        Assert.Equal(StyleJobStage.Beard, persisted.CurrentStage);
        Assert.Equal("Queued", persisted.Status);
        Assert.Null(persisted.ErrorCode);
        Assert.Null(persisted.ErrorMessage);
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
        public Exception? SubmissionException { get; init; }
        public Action? BeforeSubmissionException { get; init; }
        public BeardStyleInput? LastBeardInput { get; private set; }

        public Task<string> CreateHairPredictionAsync(HaircutStyleInput input, string webhookUrl, CancellationToken ct = default)
        {
            HairCalls++;
            if (SubmissionException is not null)
            {
                BeforeSubmissionException?.Invoke();
                throw SubmissionException;
            }

            return Task.FromResult(HairPredictionId);
        }

        public Task<string> CreateBeardPredictionAsync(BeardStyleInput input, string webhookUrl, CancellationToken ct = default)
        {
            BeardCalls++;
            if (SubmissionException is not null)
            {
                BeforeSubmissionException?.Invoke();
                throw SubmissionException;
            }

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
