using System.Text.Json;
using AiStyleApp.Api.Models;
using AiStyleApp.Data;
using AiStyleApp.Data.Entities;
using AiStyleApp.Data.Queue;
using Microsoft.EntityFrameworkCore;

namespace AiStyleApp.Api.Services;

public interface IReplicateWebhookProcessor
{
    Task<ReplicateWebhookProcessResult> ProcessAsync(ReplicateWebhookPayload payload, CancellationToken ct = default);
}

public sealed record ReplicateWebhookProcessResult(
    bool IsKnownPrediction,
    Guid? JobId = null,
    string? UserId = null,
    string? ArchiveImageUrl = null
);

public class ReplicateWebhookProcessor : IReplicateWebhookProcessor
{
    private readonly AppDbContext _db;
    private readonly IQueuePublisher _queue;
    private readonly ILogger<ReplicateWebhookProcessor> _logger;

    public ReplicateWebhookProcessor(
        AppDbContext db,
        IQueuePublisher queue,
        ILogger<ReplicateWebhookProcessor> logger)
    {
        _db = db;
        _queue = queue;
        _logger = logger;
    }

    public async Task<ReplicateWebhookProcessResult> ProcessAsync(ReplicateWebhookPayload payload, CancellationToken ct = default)
    {
        var job = await _db.StyleJobs.FirstOrDefaultAsync(j => j.ExternalPredictionId == payload.Id, ct);
        if (job is null)
        {
            return new ReplicateWebhookProcessResult(false);
        }

        if (JobStatus.IsTerminal(job.Status))
        {
            _logger.LogInformation(
                "Webhook for job {JobId} arrived after terminal state {Status}; ignored.",
                job.Id, job.Status);
            return new ReplicateWebhookProcessResult(true, job.Id, job.UserId);
        }

        var status = MapStatus(payload.Status, job.Status);
        if (status == JobStatus.Succeeded
            && string.Equals(job.CurrentStage, StyleJobStage.Hair, StringComparison.OrdinalIgnoreCase)
            && job.IsBeardStagePending)
        {
            return await QueueBeardStageAsync(job, payload, ct);
        }

        var resultJson = payload.Output is not null
            ? JsonSerializer.Serialize(payload.Output)
            : null;

        job.Status = status;
        job.ResultJson = resultJson;
        job.ErrorCode = payload.Error is not null ? "replicate_error" : null;
        job.ErrorMessage = payload.Error;

        if (status == JobStatus.Processing && job.StartedAtUtc is null)
        {
            job.StartedAtUtc = DateTimeOffset.UtcNow;
        }

        if (JobStatus.IsTerminal(status))
        {
            job.CompletedAtUtc = DateTimeOffset.UtcNow;
            job.IsBeardStagePending = false;
            if (status != JobStatus.Succeeded)
            {
                job.IntermediateImageUrl = null;
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Job {JobId} transitioned to {Status} via Replicate webhook.",
            job.Id, status);

        var archiveImageUrl = status == JobStatus.Succeeded
            ? ExtractOutputUrl(payload.Output)
            : null;

        return new ReplicateWebhookProcessResult(true, job.Id, job.UserId, archiveImageUrl);
    }

    private async Task<ReplicateWebhookProcessResult> QueueBeardStageAsync(
        StyleJobEntity job,
        ReplicateWebhookPayload payload,
        CancellationToken ct)
    {
        var intermediateImageUrl = ExtractOutputUrl(payload.Output);
        if (string.IsNullOrWhiteSpace(intermediateImageUrl))
        {
            job.Status = JobStatus.Failed;
            job.ErrorCode = "replicate_missing_output";
            job.ErrorMessage = "Replicate hair stage succeeded without returning an image URL for beard processing.";
            job.CompletedAtUtc = DateTimeOffset.UtcNow;
            job.IsBeardStagePending = false;
            await _db.SaveChangesAsync(ct);

            return new ReplicateWebhookProcessResult(true, job.Id, job.UserId);
        }

        job.Status = JobStatus.Queued;
        job.ResultJson = null;
        job.ErrorCode = null;
        job.ErrorMessage = null;
        job.CurrentStage = StyleJobStage.Beard;
        job.IntermediateImageUrl = intermediateImageUrl;
        job.ExternalPredictionId = null;
        job.IsBeardStagePending = false;
        await _db.SaveChangesAsync(ct);

        await _queue.PublishAsync(
            new StyleJob(
                JobId: job.Id,
                StyleItemId: job.StyleItemId,
                UserId: job.UserId,
                JobType: job.JobType,
                Prompt: job.Prompt,
                EnqueuedAtUtc: DateTimeOffset.UtcNow,
                CorrelationId: job.CorrelationId ?? Guid.NewGuid().ToString(),
                Attempt: job.AttemptCount,
                SchemaVersion: 2,
                ImageUrl: intermediateImageUrl,
                Haircut: job.Haircut,
                HairColor: job.HairColor,
                BeardStyle: job.BeardStyle,
                BeardColor: job.BeardColor,
                Gender: job.Gender,
                Stage: StyleJobStage.Beard),
            ct);

        _logger.LogInformation(
            "Queued beard stage for job {JobId} using intermediate image {IntermediateImageUrl}.",
            job.Id,
            intermediateImageUrl);

        return new ReplicateWebhookProcessResult(true, job.Id, job.UserId);
    }

    private static string MapStatus(string? replicateStatus, string currentStatus)
        => replicateStatus switch
        {
            "starting" => JobStatus.Processing,
            "processing" => JobStatus.Processing,
            "succeeded" => JobStatus.Succeeded,
            "failed" => JobStatus.Failed,
            "canceled" => JobStatus.Canceled,
            _ => currentStatus
        };

    private static string? ExtractOutputUrl(object? output)
    {
        if (output is not JsonElement outputElement)
        {
            return null;
        }

        if (outputElement.ValueKind == JsonValueKind.Array)
        {
            return outputElement.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString())
                .OfType<string>()
                .FirstOrDefault();
        }

        if (outputElement.ValueKind == JsonValueKind.String)
        {
            return outputElement.GetString();
        }

        return null;
    }
}
