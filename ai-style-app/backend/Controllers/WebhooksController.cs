using AiStyleApp.Api.Models;
using AiStyleApp.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace AiStyleApp.Api.Controllers;

[ApiController]
[Route("api/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly IJobService _jobs;
    private readonly IReplicateSignatureVerifier _verifier;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        IJobService jobs,
        IReplicateSignatureVerifier verifier,
        IServiceScopeFactory scopeFactory,
        ILogger<WebhooksController> logger)
    {
        _jobs = jobs;
        _verifier = verifier;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [HttpPost("replicate")]
    public async Task<IActionResult> ReplicateWebhook(CancellationToken ct)
    {
        // Read the raw body for HMAC verification
        Request.EnableBuffering();
        using var memory = new MemoryStream();
        await Request.Body.CopyToAsync(memory, ct);
        var rawBodyBytes = memory.ToArray();
        var rawBody = Encoding.UTF8.GetString(rawBodyBytes);
        Request.Body.Position = 0;

        var webhookId = Request.Headers["webhook-id"].FirstOrDefault() ?? string.Empty;
        var webhookTimestamp = Request.Headers["webhook-timestamp"].FirstOrDefault() ?? string.Empty;
        var webhookSignature = Request.Headers["webhook-signature"].FirstOrDefault() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(webhookId)
            || string.IsNullOrWhiteSpace(webhookTimestamp)
            || string.IsNullOrWhiteSpace(webhookSignature))
        {
            _logger.LogWarning("Rejected Replicate webhook: missing verification headers.");
            return Unauthorized("Missing verification headers.");
        }

        var verification = _verifier.Verify(rawBodyBytes, webhookId, webhookTimestamp, webhookSignature);
        if (!verification.IsValid)
        {
            _logger.LogWarning(
                "Rejected Replicate webhook: {FailureReason} WebhookId={WebhookId}, Timestamp={WebhookTimestamp}, SignaturePrefix={SignaturePrefix}, BodyLength={BodyLength}",
                verification.FailureReason,
                webhookId,
                webhookTimestamp,
                webhookSignature.Length > 24 ? webhookSignature[..24] : webhookSignature,
                rawBodyBytes.Length);
            return Unauthorized("Invalid signature.");
        }

        ReplicateWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ReplicateWebhookPayload>(rawBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Replicate webhook payload.");
            return BadRequest("Invalid JSON payload.");
        }

        if (payload is null)
            return BadRequest("Empty payload.");

        var job = await _jobs.GetByExternalIdAsync(payload.Id, ct);
        if (job is null)
        {
            _logger.LogWarning("Received webhook for unknown prediction {PredictionId}.", payload.Id);
            return Ok(); // Idempotent — don't fail for unknown predictions
        }

        // Map Replicate status to our domain status
        var status = payload.Status switch
        {
            "starting"   => JobStatus.Processing,
            "processing" => JobStatus.Processing,
            "succeeded"  => JobStatus.Succeeded,
            "failed"     => JobStatus.Failed,
            "canceled"   => JobStatus.Canceled,
            _            => job.Status // Unknown — keep current
        };

        // Don't regress a terminal state
        if (JobStatus.IsTerminal(job.Status))
        {
            _logger.LogInformation(
                "Webhook for job {JobId} arrived after terminal state {Status}; ignored.",
                job.Id, job.Status);
            return Ok();
        }

        var resultJson = payload.Output is not null
            ? JsonSerializer.Serialize(payload.Output)
            : null;

        await _jobs.UpdateFromWebhookAsync(
            job, status, resultJson,
            payload.Error is not null ? "replicate_error" : null,
            payload.Error,
            ct);

        _logger.LogInformation(
            "Job {JobId} transitioned to {Status} via Replicate webhook.",
            job.Id, status);

        // Fire-and-forget: archive the generated image to permanent blob storage.
        // We do this after returning OK so Replicate isn't kept waiting.
        if (status == JobStatus.Succeeded
            && payload.Output is JsonElement outputElement)
        {
            string? firstUrl = null;

            if (outputElement.ValueKind == JsonValueKind.Array)
            {
                // Legacy flux-dev model — array of URLs
                firstUrl = outputElement.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString())
                    .OfType<string>()
                    .FirstOrDefault();
            }
            else if (outputElement.ValueKind == JsonValueKind.String)
            {
                // flux-kontext change-haircut model — single URI
                firstUrl = outputElement.GetString();
            }

            if (firstUrl is not null)
            {
                var jobId = job.Id;
                var userId = job.UserId;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var archiver = scope.ServiceProvider
                            .GetRequiredService<IGeneratedImageArchiver>();
                        await archiver.ArchiveAsync(jobId, firstUrl, userId, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        // Archive failure is non-fatal: resultJson still holds the Replicate URL
                        _logger.LogError(ex,
                            "Failed to archive generated image for job {JobId}.", jobId);
                    }
                });
            }
        }

        return Ok();
    }
}
