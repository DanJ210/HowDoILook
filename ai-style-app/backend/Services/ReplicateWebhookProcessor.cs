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

        if (!TryMapStatus(payload.Status, out var status))
        {
            _logger.LogWarning(
                "Webhook for job {JobId} used unsupported Replicate status {ReplicateStatus}; ignored.",
                job.Id,
                payload.Status);
            return new ReplicateWebhookProcessResult(true, job.Id, job.UserId);
        }

        if (string.Equals(job.CurrentStage, StyleJobStage.BeardQueued, StringComparison.OrdinalIgnoreCase))
        {
            if (status == JobStatus.Succeeded)
            {
                return await PublishBeardStageAsync(job, ct);
            }

            _logger.LogInformation(
                "Webhook for job {JobId} arrived after its hair-to-beard handoff was accepted; status {Status} ignored.",
                job.Id,
                status);
            return new ReplicateWebhookProcessResult(true, job.Id, job.UserId);
        }

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

        var hairResultJson = payload.Output is not null
            ? JsonSerializer.Serialize(payload.Output)
            : null;
        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;
        var claimed = await TryClaimBeardHandoffAsync(job, intermediateImageUrl, hairResultJson, ct);
        if (!claimed)
        {
            _logger.LogInformation(
                "Hair-to-beard handoff for job {JobId} was already claimed; duplicate webhook ignored.",
                job.Id);
            return new ReplicateWebhookProcessResult(true, job.Id, job.UserId);
        }

        var result = await PublishBeardStageAsync(job, ct);
        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }

        return result;
    }

    private async Task<bool> TryClaimBeardHandoffAsync(
        StyleJobEntity job,
        string intermediateImageUrl,
        string? hairResultJson,
        CancellationToken ct)
    {
        if (_db.Database.IsRelational())
        {
            var updated = await _db.StyleJobs
                .Where(candidate => candidate.Id == job.Id
                    && candidate.ExternalPredictionId == job.ExternalPredictionId
                    && candidate.CurrentStage == StyleJobStage.Hair
                    && candidate.IsBeardStagePending
                    && candidate.Status != JobStatus.Succeeded
                    && candidate.Status != JobStatus.Failed
                    && candidate.Status != JobStatus.TimedOut
                    && candidate.Status != JobStatus.Canceled)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(candidate => candidate.Status, JobStatus.Queued)
                        .SetProperty(candidate => candidate.CurrentStage, StyleJobStage.BeardQueued)
                        .SetProperty(candidate => candidate.IntermediateImageUrl, intermediateImageUrl)
                        .SetProperty(candidate => candidate.ResultJson, hairResultJson)
                        .SetProperty(candidate => candidate.ErrorCode, (string?)null)
                        .SetProperty(candidate => candidate.ErrorMessage, (string?)null)
                        .SetProperty(candidate => candidate.IsBeardStagePending, false),
                    ct);

            if (updated == 0)
            {
                return false;
            }

            _db.Entry(job).State = EntityState.Detached;
        }
        else
        {
            if (!string.Equals(job.CurrentStage, StyleJobStage.Hair, StringComparison.OrdinalIgnoreCase)
                || !job.IsBeardStagePending
                || JobStatus.IsTerminal(job.Status))
            {
                return false;
            }

            job.Status = JobStatus.Queued;
            job.CurrentStage = StyleJobStage.BeardQueued;
            job.IntermediateImageUrl = intermediateImageUrl;
            job.ResultJson = hairResultJson;
            job.ErrorCode = null;
            job.ErrorMessage = null;
            job.IsBeardStagePending = false;
            await _db.SaveChangesAsync(ct);
        }

        job.Status = JobStatus.Queued;
        job.CurrentStage = StyleJobStage.BeardQueued;
        job.IntermediateImageUrl = intermediateImageUrl;
        job.ResultJson = hairResultJson;
        job.ErrorCode = null;
        job.ErrorMessage = null;
        job.IsBeardStagePending = false;
        return true;
    }

    private async Task<ReplicateWebhookProcessResult> PublishBeardStageAsync(
        StyleJobEntity job,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(job.IntermediateImageUrl))
        {
            job.Status = JobStatus.Failed;
            job.ErrorCode = "replicate_missing_intermediate_output";
            job.ErrorMessage = "The accepted hair result is missing, so beard generation cannot continue.";
            job.CompletedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new ReplicateWebhookProcessResult(true, job.Id, job.UserId);
        }

        job.Status = JobStatus.Queued;

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
                ImageUrl: job.IntermediateImageUrl,
                Haircut: job.Haircut,
                HairColor: job.HairColor,
                BeardStyle: job.BeardStyle,
                BeardColor: job.BeardColor,
                Gender: job.Gender,
                Stage: StyleJobStage.Beard),
            ct);

        if (_db.Database.IsRelational())
        {
            await _db.StyleJobs
                .Where(candidate => candidate.Id == job.Id
                    && candidate.CurrentStage == StyleJobStage.BeardQueued)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(candidate => candidate.CurrentStage, StyleJobStage.Beard)
                        .SetProperty(candidate => candidate.ExternalPredictionId, (string?)null),
                    ct);
        }
        else
        {
            job.CurrentStage = StyleJobStage.Beard;
            job.ExternalPredictionId = null;
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Queued beard stage for job {JobId} using intermediate image {IntermediateImageUrl}.",
            job.Id,
            job.IntermediateImageUrl);

        return new ReplicateWebhookProcessResult(true, job.Id, job.UserId);
    }

    private static bool TryMapStatus(string? replicateStatus, out string status)
    {
        status = replicateStatus switch
        {
            "queued" => JobStatus.Processing,
            "starting" => JobStatus.Processing,
            "processing" => JobStatus.Processing,
            "succeeded" => JobStatus.Succeeded,
            "failed" => JobStatus.Failed,
            "canceled" => JobStatus.Canceled,
            _ => string.Empty
        };

        return status.Length > 0;
    }

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
