using System.Text.Json;
using AiStyleApp.Api.Models;
using AiStyleApp.Api.Services;
using AiStyleApp.Data;
using AiStyleApp.Data.Entities;
using AiStyleApp.Data.Queue;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiStyleApp.Tests;

public class ReplicateWebhookProcessorTests
{
    [Fact]
    public async Task ProcessAsync_HairStageSuccessWithPendingBeard_QueuesBeardStageWithoutArchiving()
    {
        await using var db = CreateDbContext();
        var queue = new TestQueuePublisher();
        var job = await SeedJobAsync(db, pipelineMode: StyleJobPipelineMode.HairThenBeard, currentStage: StyleJobStage.Hair, isBeardStagePending: true);
        job.ExternalPredictionId = "hair-prediction";
        await db.SaveChangesAsync();

        var processor = new ReplicateWebhookProcessor(db, queue, NullLogger<ReplicateWebhookProcessor>.Instance);
        var payload = new ReplicateWebhookPayload(
            Id: "hair-prediction",
            Status: "succeeded",
            Error: null,
            Output: CreateJsonString("https://example.com/hair-output.webp"),
            CompletedAt: DateTimeOffset.UtcNow);

        var result = await processor.ProcessAsync(payload);

        Assert.True(result.IsKnownPrediction);
        Assert.Null(result.ArchiveImageUrl);

        var persisted = await db.StyleJobs.FirstAsync(x => x.Id == job.Id);
        Assert.Equal(JobStatus.Queued, persisted.Status);
        Assert.Equal(StyleJobStage.Beard, persisted.CurrentStage);
        Assert.False(persisted.IsBeardStagePending);
        Assert.Null(persisted.ExternalPredictionId);
        Assert.Equal("https://example.com/hair-output.webp", persisted.IntermediateImageUrl);

        var queuedMessage = Assert.IsType<StyleJob>(queue.LastMessage);
        Assert.Equal(StyleJobStage.Beard, queuedMessage.Stage);
        Assert.Equal("https://example.com/hair-output.webp", queuedMessage.ImageUrl);
    }

    [Fact]
    public async Task ProcessAsync_FinalSuccess_ReturnsArchiveUrlWithoutQueueingAnotherStage()
    {
        await using var db = CreateDbContext();
        var queue = new TestQueuePublisher();
        var job = await SeedJobAsync(db, pipelineMode: StyleJobPipelineMode.HairOnly, currentStage: StyleJobStage.Hair, isBeardStagePending: false);
        job.ExternalPredictionId = "final-prediction";
        await db.SaveChangesAsync();

        var processor = new ReplicateWebhookProcessor(db, queue, NullLogger<ReplicateWebhookProcessor>.Instance);
        var payload = new ReplicateWebhookPayload(
            Id: "final-prediction",
            Status: "succeeded",
            Error: null,
            Output: CreateJsonString("https://example.com/final-output.webp"),
            CompletedAt: DateTimeOffset.UtcNow);

        var result = await processor.ProcessAsync(payload);

        Assert.True(result.IsKnownPrediction);
        Assert.Equal("https://example.com/final-output.webp", result.ArchiveImageUrl);
        Assert.Null(queue.LastMessage);

        var persisted = await db.StyleJobs.FirstAsync(x => x.Id == job.Id);
        Assert.Equal(JobStatus.Succeeded, persisted.Status);
        Assert.False(persisted.IsBeardStagePending);
    }

    [Fact]
    public async Task ProcessAsync_BeardStageFailure_MarksOverallJobFailed()
    {
        await using var db = CreateDbContext();
        var queue = new TestQueuePublisher();
        var job = await SeedJobAsync(db, pipelineMode: StyleJobPipelineMode.BeardOnly, currentStage: StyleJobStage.Beard, isBeardStagePending: false);
        job.ExternalPredictionId = "beard-prediction";
        job.IntermediateImageUrl = "https://example.com/intermediate.webp";
        await db.SaveChangesAsync();

        var processor = new ReplicateWebhookProcessor(db, queue, NullLogger<ReplicateWebhookProcessor>.Instance);
        var payload = new ReplicateWebhookPayload(
            Id: "beard-prediction",
            Status: "failed",
            Error: "stage failed",
            Output: null,
            CompletedAt: DateTimeOffset.UtcNow);

        var result = await processor.ProcessAsync(payload);

        Assert.True(result.IsKnownPrediction);
        Assert.Null(result.ArchiveImageUrl);
        Assert.Null(queue.LastMessage);

        var persisted = await db.StyleJobs.FirstAsync(x => x.Id == job.Id);
        Assert.Equal(JobStatus.Failed, persisted.Status);
        Assert.Equal("replicate_error", persisted.ErrorCode);
        Assert.Equal("stage failed", persisted.ErrorMessage);
        Assert.Null(persisted.IntermediateImageUrl);
    }

    [Fact]
    public async Task ProcessAsync_DuplicateTerminalWebhook_DoesNotRegressStatus()
    {
        await using var db = CreateDbContext();
        var queue = new TestQueuePublisher();
        var completedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);

        var job = await SeedJobAsync(db, pipelineMode: StyleJobPipelineMode.HairOnly, currentStage: StyleJobStage.Hair, isBeardStagePending: false);
        job.ExternalPredictionId = "terminal-prediction";
        job.Status = JobStatus.Succeeded;
        job.CompletedAtUtc = completedAtUtc;
        job.ResultJson = JsonSerializer.Serialize(new[] { "https://example.com/final-output.webp" });
        await db.SaveChangesAsync();

        var processor = new ReplicateWebhookProcessor(db, queue, NullLogger<ReplicateWebhookProcessor>.Instance);
        var payload = new ReplicateWebhookPayload(
            Id: "terminal-prediction",
            Status: "failed",
            Error: "late failure",
            Output: null,
            CompletedAt: DateTimeOffset.UtcNow);

        var result = await processor.ProcessAsync(payload);

        Assert.True(result.IsKnownPrediction);
        Assert.Equal(job.Id, result.JobId);
        Assert.Null(queue.LastMessage);

        var persisted = await db.StyleJobs.FirstAsync(x => x.Id == job.Id);
        Assert.Equal(JobStatus.Succeeded, persisted.Status);
        Assert.Equal(completedAtUtc, persisted.CompletedAtUtc);
        Assert.Equal(JsonSerializer.Serialize(new[] { "https://example.com/final-output.webp" }), persisted.ResultJson);
        Assert.Null(persisted.ErrorCode);
        Assert.Null(persisted.ErrorMessage);
    }

    [Fact]
    public async Task ProcessAsync_StalePredictionAfterStageTransition_IsIgnoredAsUnknown()
    {
        await using var db = CreateDbContext();
        var queue = new TestQueuePublisher();

        var job = await SeedJobAsync(db, pipelineMode: StyleJobPipelineMode.HairThenBeard, currentStage: StyleJobStage.Beard, isBeardStagePending: false);
        job.ExternalPredictionId = "beard-prediction";
        job.Status = JobStatus.Queued;
        job.IntermediateImageUrl = "https://example.com/intermediate.webp";
        await db.SaveChangesAsync();

        var processor = new ReplicateWebhookProcessor(db, queue, NullLogger<ReplicateWebhookProcessor>.Instance);
        var payload = new ReplicateWebhookPayload(
            Id: "old-hair-prediction",
            Status: "succeeded",
            Error: null,
            Output: CreateJsonString("https://example.com/stale.webp"),
            CompletedAt: DateTimeOffset.UtcNow);

        var result = await processor.ProcessAsync(payload);

        Assert.False(result.IsKnownPrediction);
        Assert.Null(queue.LastMessage);

        var persisted = await db.StyleJobs.FirstAsync(x => x.Id == job.Id);
        Assert.Equal(JobStatus.Queued, persisted.Status);
        Assert.Equal(StyleJobStage.Beard, persisted.CurrentStage);
        Assert.Equal("beard-prediction", persisted.ExternalPredictionId);
        Assert.Equal("https://example.com/intermediate.webp", persisted.IntermediateImageUrl);
    }

    [Fact]
    public async Task ProcessAsync_BeardQueuePublishFails_ReplayResumesHandoff()
    {
        await using var db = CreateDbContext();
        var queue = new TestQueuePublisher { FailuresRemaining = 1 };
        var job = await SeedJobAsync(
            db,
            pipelineMode: StyleJobPipelineMode.HairThenBeard,
            currentStage: StyleJobStage.Hair,
            isBeardStagePending: true);
        job.ExternalPredictionId = "hair-prediction";
        await db.SaveChangesAsync();

        var processor = new ReplicateWebhookProcessor(db, queue, NullLogger<ReplicateWebhookProcessor>.Instance);
        var payload = new ReplicateWebhookPayload(
            Id: "hair-prediction",
            Status: "succeeded",
            Error: null,
            Output: CreateJsonString("https://example.com/hair-output.webp"),
            CompletedAt: DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(payload));

        var pending = await db.StyleJobs.FirstAsync(x => x.Id == job.Id);
        Assert.Equal(JobStatus.Queued, pending.Status);
        Assert.Equal(StyleJobStage.BeardQueued, pending.CurrentStage);
        Assert.Equal("hair-prediction", pending.ExternalPredictionId);
        Assert.Equal("https://example.com/hair-output.webp", pending.IntermediateImageUrl);

        var replay = await processor.ProcessAsync(payload);

        Assert.True(replay.IsKnownPrediction);
        Assert.Equal(2, queue.PublishCalls);
        var persisted = await db.StyleJobs.FirstAsync(x => x.Id == job.Id);
        Assert.Equal(StyleJobStage.Beard, persisted.CurrentStage);
        Assert.Null(persisted.ExternalPredictionId);
        Assert.IsType<StyleJob>(queue.LastMessage);
    }

    [Fact]
    public async Task ProcessAsync_UnknownStatus_DoesNotMutateJob()
    {
        await using var db = CreateDbContext();
        var queue = new TestQueuePublisher();
        var startedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2);
        var job = await SeedJobAsync(
            db,
            pipelineMode: StyleJobPipelineMode.HairOnly,
            currentStage: StyleJobStage.Hair,
            isBeardStagePending: false);
        job.ExternalPredictionId = "active-prediction";
        job.ResultJson = "{\"preserved\":true}";
        job.ErrorCode = "preserved_code";
        job.ErrorMessage = "Preserved message.";
        job.StartedAtUtc = startedAtUtc;
        await db.SaveChangesAsync();

        var processor = new ReplicateWebhookProcessor(db, queue, NullLogger<ReplicateWebhookProcessor>.Instance);
        var payload = new ReplicateWebhookPayload(
            Id: "active-prediction",
            Status: "paused",
            Error: "unexpected payload error",
            Output: CreateJsonString("https://example.com/unexpected.webp"),
            CompletedAt: DateTimeOffset.UtcNow);

        var result = await processor.ProcessAsync(payload);

        Assert.True(result.IsKnownPrediction);
        Assert.Null(queue.LastMessage);
        var persisted = await db.StyleJobs.FirstAsync(x => x.Id == job.Id);
        Assert.Equal(JobStatus.Processing, persisted.Status);
        Assert.Equal("{\"preserved\":true}", persisted.ResultJson);
        Assert.Equal("preserved_code", persisted.ErrorCode);
        Assert.Equal("Preserved message.", persisted.ErrorMessage);
        Assert.Equal(startedAtUtc, persisted.StartedAtUtc);
        Assert.Null(persisted.CompletedAtUtc);
    }

    [Fact]
    public async Task ProcessAsync_QueuedStatus_MapsToProcessing()
    {
        await using var db = CreateDbContext();
        var queue = new TestQueuePublisher();
        var job = await SeedJobAsync(
            db,
            pipelineMode: StyleJobPipelineMode.HairOnly,
            currentStage: StyleJobStage.Hair,
            isBeardStagePending: false);
        job.Status = JobStatus.Queued;
        job.StartedAtUtc = null;
        job.ExternalPredictionId = "queued-prediction";
        await db.SaveChangesAsync();

        var processor = new ReplicateWebhookProcessor(db, queue, NullLogger<ReplicateWebhookProcessor>.Instance);
        var payload = new ReplicateWebhookPayload(
            Id: "queued-prediction",
            Status: "queued",
            Error: null,
            Output: null,
            CompletedAt: null);

        var result = await processor.ProcessAsync(payload);

        Assert.True(result.IsKnownPrediction);
        Assert.Null(queue.LastMessage);
        var persisted = await db.StyleJobs.FirstAsync(x => x.Id == job.Id);
        Assert.Equal(JobStatus.Processing, persisted.Status);
        Assert.NotNull(persisted.StartedAtUtc);
        Assert.Null(persisted.CompletedAtUtc);
    }

    [Fact]
    public async Task ProcessAsync_BeardHandoffPending_LaterFailureDoesNotPublishOrRegressState()
    {
        await using var db = CreateDbContext();
        var queue = new TestQueuePublisher();
        var job = await SeedJobAsync(
            db,
            pipelineMode: StyleJobPipelineMode.HairThenBeard,
            currentStage: StyleJobStage.BeardQueued,
            isBeardStagePending: false);
        job.Status = JobStatus.Queued;
        job.ExternalPredictionId = "hair-prediction";
        job.IntermediateImageUrl = "https://example.com/hair-output.webp";
        await db.SaveChangesAsync();

        var processor = new ReplicateWebhookProcessor(db, queue, NullLogger<ReplicateWebhookProcessor>.Instance);
        var payload = new ReplicateWebhookPayload(
            Id: "hair-prediction",
            Status: "failed",
            Error: "late failure",
            Output: null,
            CompletedAt: DateTimeOffset.UtcNow);

        var result = await processor.ProcessAsync(payload);

        Assert.True(result.IsKnownPrediction);
        Assert.Null(queue.LastMessage);
        var persisted = await db.StyleJobs.FirstAsync(x => x.Id == job.Id);
        Assert.Equal(JobStatus.Queued, persisted.Status);
        Assert.Equal(StyleJobStage.BeardQueued, persisted.CurrentStage);
        Assert.Equal("hair-prediction", persisted.ExternalPredictionId);
        Assert.Equal("https://example.com/hair-output.webp", persisted.IntermediateImageUrl);
        Assert.Null(persisted.ErrorCode);
        Assert.Null(persisted.ErrorMessage);
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

    private static JsonElement CreateJsonString(string value)
        => JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement.Clone();

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
        public int FailuresRemaining { get; set; }
        public int PublishCalls { get; private set; }

        public Task PublishAsync<T>(T message, CancellationToken ct = default)
        {
            PublishCalls++;
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new InvalidOperationException("Queue unavailable.");
            }

            LastMessage = message;
            return Task.CompletedTask;
        }
    }
}
