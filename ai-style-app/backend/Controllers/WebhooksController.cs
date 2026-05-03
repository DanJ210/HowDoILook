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
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        IJobService jobs,
        IReplicateSignatureVerifier verifier,
        ILogger<WebhooksController> logger)
    {
        _jobs = jobs;
        _verifier = verifier;
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

        return Ok();
    }
}
