using System.Net;
using System.Text.Json;
using AiStyleApp.Api.Models;
using AiStyleApp.Api.Services;
using AiStyleApp.Data;
using AiStyleApp.Data.Entities;
using AiStyleApp.Data.Queue;
using AiStyleApp.Worker.Handlers;
using AiStyleApp.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiStyleApp.Tests;

[Collection(PostgresReliabilityCollection.Name)]
public class PostgresReliabilityTests
{
    private const string ConnectionVariable = "AISTYLEAPP_POSTGRES_TEST_CONNECTION";

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task WebhookQueueFailure_RollsBackClaimAndReplayCompletesHandoff()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);

        var predictionId = $"hair-{Guid.NewGuid():N}";
        Guid jobId;
        await using (var seedDb = CreateDbContext(connectionString))
        {
            var job = await SeedJobAsync(
                seedDb,
                StyleJobPipelineMode.HairThenBeard,
                StyleJobStage.Hair,
                isBeardStagePending: true);
            job.ExternalPredictionId = predictionId;
            await seedDb.SaveChangesAsync();
            jobId = job.Id;
        }

        var payload = CreateSuccessPayload(predictionId);
        await using (var failingDb = CreateDbContext(connectionString))
        {
            var processor = new ReplicateWebhookProcessor(
                failingDb,
                new RecordingQueuePublisher { FailuresRemaining = 1 },
                NullLogger<ReplicateWebhookProcessor>.Instance);

            await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(payload));
        }

        await using (var verifyRollbackDb = CreateDbContext(connectionString))
        {
            var rolledBack = await verifyRollbackDb.StyleJobs.AsNoTracking().SingleAsync(job => job.Id == jobId);
            Assert.Equal(JobStatus.Processing, rolledBack.Status);
            Assert.Equal(StyleJobStage.Hair, rolledBack.CurrentStage);
            Assert.True(rolledBack.IsBeardStagePending);
            Assert.Equal(predictionId, rolledBack.ExternalPredictionId);
            Assert.Null(rolledBack.IntermediateImageUrl);
        }

        var replayQueue = new RecordingQueuePublisher();
        await using (var replayDb = CreateDbContext(connectionString))
        {
            var processor = new ReplicateWebhookProcessor(
                replayDb,
                replayQueue,
                NullLogger<ReplicateWebhookProcessor>.Instance);

            var result = await processor.ProcessAsync(payload);
            Assert.True(result.IsKnownPrediction);
        }

        await using var verifyReplayDb = CreateDbContext(connectionString);
        var completed = await verifyReplayDb.StyleJobs.AsNoTracking().SingleAsync(job => job.Id == jobId);
        Assert.Equal(JobStatus.Queued, completed.Status);
        Assert.Equal(StyleJobStage.Beard, completed.CurrentStage);
        Assert.False(completed.IsBeardStagePending);
        Assert.Null(completed.ExternalPredictionId);
        Assert.Equal("https://example.com/hair-output.webp", completed.IntermediateImageUrl);
        Assert.Equal(1, replayQueue.PublishCalls);
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task ConcurrentDuplicateHairCallbacks_PublishOneBeardMessage()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);

        var predictionId = $"hair-{Guid.NewGuid():N}";
        Guid jobId;
        await using (var seedDb = CreateDbContext(connectionString))
        {
            var job = await SeedJobAsync(
                seedDb,
                StyleJobPipelineMode.HairThenBeard,
                StyleJobStage.Hair,
                isBeardStagePending: true);
            job.ExternalPredictionId = predictionId;
            await seedDb.SaveChangesAsync();
            jobId = job.Id;
        }

        var queue = new CoordinatedQueuePublisher();
        await using var firstDb = CreateDbContext(connectionString);
        await using var secondDb = CreateDbContext(connectionString);
        var firstProcessor = new ReplicateWebhookProcessor(
            firstDb,
            queue,
            NullLogger<ReplicateWebhookProcessor>.Instance);
        var secondProcessor = new ReplicateWebhookProcessor(
            secondDb,
            queue,
            NullLogger<ReplicateWebhookProcessor>.Instance);
        var payload = CreateSuccessPayload(predictionId);

        var firstCallback = firstProcessor.ProcessAsync(payload);
        await queue.FirstPublishStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var secondCallback = secondProcessor.ProcessAsync(payload);
        await Task.Yield();
        queue.ReleaseFirstPublish.TrySetResult();

        await Task.WhenAll(firstCallback, secondCallback).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, queue.PublishCalls);
        await using var verifyDb = CreateDbContext(connectionString);
        var persisted = await verifyDb.StyleJobs.AsNoTracking().SingleAsync(job => job.Id == jobId);
        Assert.Equal(StyleJobStage.Beard, persisted.CurrentStage);
        Assert.Null(persisted.ExternalPredictionId);
        Assert.Equal("https://example.com/hair-output.webp", persisted.IntermediateImageUrl);
    }

    [PostgresFact]
    [Trait("Category", "Postgres")]
    public async Task WorkerSubmissionFailures_CountAttemptsAndStopAtMaximum()
    {
        var connectionString = RequireConnectionString();
        await MigrateAsync(connectionString);

        StyleJob message;
        Guid jobId;
        await using (var seedDb = CreateDbContext(connectionString))
        {
            var job = await SeedJobAsync(
                seedDb,
                StyleJobPipelineMode.HairOnly,
                StyleJobStage.Queued,
                isBeardStagePending: false);
            job.MaxAttempts = 2;
            await seedDb.SaveChangesAsync();
            jobId = job.Id;
            message = CreateQueueMessage(job);
        }

        var replicate = new ThrowingReplicateWorkerClient();
        await using (var firstAttemptDb = CreateDbContext(connectionString))
        {
            var handler = CreateStyleJobHandler(firstAttemptDb, replicate);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => handler.HandleAsync(JsonSerializer.Serialize(message), CancellationToken.None));
        }

        await using (var verifyRetryDb = CreateDbContext(connectionString))
        {
            var retrying = await verifyRetryDb.StyleJobs.AsNoTracking().SingleAsync(job => job.Id == jobId);
            Assert.Equal(1, retrying.AttemptCount);
            Assert.Equal(JobStatus.Queued, retrying.Status);
            Assert.Null(retrying.ErrorCode);
        }

        await using (var secondAttemptDb = CreateDbContext(connectionString))
        {
            var handler = CreateStyleJobHandler(secondAttemptDb, replicate);
            await handler.HandleAsync(JsonSerializer.Serialize(message), CancellationToken.None);
        }

        await using var verifyFailureDb = CreateDbContext(connectionString);
        var failed = await verifyFailureDb.StyleJobs.AsNoTracking().SingleAsync(job => job.Id == jobId);
        Assert.Equal(2, replicate.HairCalls);
        Assert.Equal(2, failed.AttemptCount);
        Assert.Equal(JobStatus.Failed, failed.Status);
        Assert.Equal("replicate_submission_failed", failed.ErrorCode);
        Assert.NotNull(failed.CompletedAtUtc);
    }

    private static string RequireConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        return !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new InvalidOperationException($"Set {ConnectionVariable} to a disposable migrated PostgreSQL database.");
    }

    private static AppDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new AppDbContext(options);
    }

    private static async Task MigrateAsync(string connectionString)
    {
        await using var db = CreateDbContext(connectionString);
        await db.Database.MigrateAsync();
    }

    private static async Task<StyleJobEntity> SeedJobAsync(
        AppDbContext db,
        string pipelineMode,
        string currentStage,
        bool isBeardStagePending)
    {
        var item = new StyleItemEntity
        {
            UserId = "postgres-test-user",
            Name = "Postgres reliability test",
            Description = "Disposable integration test row",
            Prompt = "prompt",
            ImageUrl = "https://example.com/input.jpg"
        };

        var job = new StyleJobEntity
        {
            StyleItem = item,
            UserId = item.UserId,
            Prompt = item.Prompt,
            ImageUrl = item.ImageUrl,
            CorrelationId = Guid.NewGuid().ToString(),
            Haircut = "Layered",
            HairColor = "Honey Blonde",
            BeardStyle = "Stubble",
            BeardColor = "Dark Brown",
            Gender = "male",
            PipelineMode = pipelineMode,
            CurrentStage = currentStage,
            IsBeardStagePending = isBeardStagePending,
            Status = JobStatus.Processing
        };

        db.StyleJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    private static ReplicateWebhookPayload CreateSuccessPayload(string predictionId)
        => new(
            Id: predictionId,
            Status: "succeeded",
            Error: null,
            Output: JsonDocument.Parse("\"https://example.com/hair-output.webp\"").RootElement.Clone(),
            CompletedAt: DateTimeOffset.UtcNow);

    private static StyleJob CreateQueueMessage(StyleJobEntity job)
        => new(
            JobId: job.Id,
            StyleItemId: job.StyleItemId,
            UserId: job.UserId,
            JobType: job.JobType,
            Prompt: job.Prompt,
            EnqueuedAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: job.CorrelationId!,
            Attempt: 0,
            SchemaVersion: 2,
            ImageUrl: job.ImageUrl,
            Haircut: job.Haircut,
            HairColor: job.HairColor,
            BeardStyle: job.BeardStyle,
            BeardColor: job.BeardColor,
            Gender: job.Gender);

    private static StyleJobHandler CreateStyleJobHandler(
        AppDbContext db,
        IReplicateWorkerClient replicate)
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
            replicate,
            new SuccessfulHttpClientFactory(),
            configuration);
    }

    private class RecordingQueuePublisher : IQueuePublisher
    {
        public int FailuresRemaining { get; set; }
        public int PublishCalls { get; private set; }

        public virtual Task PublishAsync<T>(T message, CancellationToken ct = default)
        {
            PublishCalls++;
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new InvalidOperationException("Queue unavailable.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class CoordinatedQueuePublisher : IQueuePublisher
    {
        private int _publishCalls;

        public int PublishCalls => _publishCalls;
        public TaskCompletionSource FirstPublishStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstPublish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task PublishAsync<T>(T message, CancellationToken ct = default)
        {
            var call = Interlocked.Increment(ref _publishCalls);
            if (call == 1)
            {
                FirstPublishStarted.TrySetResult();
                await ReleaseFirstPublish.Task.WaitAsync(ct);
            }
        }
    }

    private sealed class ThrowingReplicateWorkerClient : IReplicateWorkerClient
    {
        public int HairCalls { get; private set; }

        public Task<string> CreateHairPredictionAsync(
            HaircutStyleInput input,
            string webhookUrl,
            CancellationToken ct = default)
        {
            HairCalls++;
            throw new InvalidOperationException("Replicate unavailable.");
        }

        public Task<string> CreateBeardPredictionAsync(
            BeardStyleInput input,
            string webhookUrl,
            CancellationToken ct = default)
            => throw new InvalidOperationException("Unexpected beard submission.");
    }

    private sealed class SuccessfulHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(new SuccessfulHttpMessageHandler());
    }

    private sealed class SuccessfulHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresReliabilityCollection
{
    public const string Name = "Postgres reliability";
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AISTYLEAPP_POSTGRES_TEST_CONNECTION")))
        {
            Skip = "Set AISTYLEAPP_POSTGRES_TEST_CONNECTION to a disposable migrated PostgreSQL database.";
        }
    }
}