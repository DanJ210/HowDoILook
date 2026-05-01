using System.Text.Json;
using AiStyleApp.Data;
using AiStyleApp.Data.Queue;
using AiStyleApp.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AiStyleApp.Worker.Handlers;

public class StyleJobHandler : IMessageHandler
{
    private readonly ILogger<StyleJobHandler> _logger;
    private readonly AppDbContext _db;
    private readonly IReplicateWorkerClient _replicate;
    private readonly string _webhookBaseUrl;

    public StyleJobHandler(
        ILogger<StyleJobHandler> logger,
        AppDbContext db,
        IReplicateWorkerClient replicate,
        IConfiguration config)
    {
        _logger = logger;
        _db = db;
        _replicate = replicate;
        _webhookBaseUrl = config["Replicate:WebhookBaseUrl"]?.TrimEnd('/') ?? string.Empty;
    }

    public async Task HandleAsync(string messageBody, CancellationToken cancellationToken)
    {
        StyleJob? job;
        try
        {
            job = JsonSerializer.Deserialize<StyleJob>(messageBody);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize StyleJob message.");
            return;
        }

        if (job is null)
        {
            _logger.LogWarning("Received null or undeserializable style job message.");
            return;
        }

        _logger.LogInformation(
            "Processing style job {JobId} for item {StyleItemId} (attempt {Attempt}).",
            job.JobId, job.StyleItemId, job.Attempt);

        // Load the persisted job entity
        var entity = await _db.StyleJobs
            .FirstOrDefaultAsync(j => j.Id == job.JobId, cancellationToken);

        if (entity is null)
        {
            _logger.LogWarning("Job entity {JobId} not found in database; skipping.", job.JobId);
            return;
        }

        // Don't reprocess terminal jobs
        if (entity.Status is "Succeeded" or "Failed" or "Canceled" or "TimedOut")
        {
            _logger.LogInformation("Job {JobId} already in terminal state {Status}; skipping.", entity.Id, entity.Status);
            return;
        }

        var nextAttempt = entity.AttemptCount + 1;

        entity.Status = "Processing";
        entity.AttemptCount = nextAttempt;
        entity.StartedAtUtc ??= DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            var webhookUrl = $"{_webhookBaseUrl}/api/webhooks/replicate";
            var predictionId = await _replicate.CreatePredictionAsync(job.Prompt, webhookUrl, job.ImageUrl, cancellationToken);

            entity.ExternalPredictionId = predictionId;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Created Replicate prediction {PredictionId} for job {JobId}.",
                predictionId, entity.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit job {JobId} to Replicate (attempt {Attempt}).", entity.Id, entity.AttemptCount);

            if (entity.AttemptCount >= entity.MaxAttempts)
            {
                entity.Status = "Failed";
                entity.ErrorCode = "replicate_submission_failed";
                entity.ErrorMessage = ex.Message;
                entity.CompletedAtUtc = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogWarning("Job {JobId} marked as Failed after {MaxAttempts} attempts.", entity.Id, entity.MaxAttempts);
            }
            else
            {
                // Re-queue with incremented attempt — leave message on queue by rethrowing
                entity.Status = "Queued";
                await _db.SaveChangesAsync(cancellationToken);
                throw; // Worker's retry logic will re-enqueue
            }
        }
    }
}

