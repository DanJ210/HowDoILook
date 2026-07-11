using System.Text.Json;
using AiStyleApp.Data;
using AiStyleApp.Data.Entities;
using AiStyleApp.Data.Queue;
using Microsoft.EntityFrameworkCore;
using AiStyleApp.Worker.Services;

namespace AiStyleApp.Worker.Handlers;

public class FaceAnalysisJobHandler : IMessageHandler
{
    private const string JobStatusQueued = "Queued";
    private const string JobStatusProcessing = "Processing";
    private const string JobStatusSucceeded = "Succeeded";
    private const string JobStatusFailed = "Failed";

    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFaceAnalysisPipeline _pipeline;
    private readonly ILogger<FaceAnalysisJobHandler> _logger;

    public FaceAnalysisJobHandler(
        AppDbContext db,
        IHttpClientFactory httpClientFactory,
        IFaceAnalysisPipeline pipeline,
        ILogger<FaceAnalysisJobHandler> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _pipeline = pipeline;
        _logger = logger;
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
            _logger.LogError(ex, "Failed to deserialize face-analysis job message.");
            return;
        }

        if (job is null)
        {
            _logger.LogWarning("Received null or undeserializable face-analysis message.");
            return;
        }

        var analysisJob = await _db.FaceAnalysisJobs
            .FirstOrDefaultAsync(x => x.Id == job.JobId, cancellationToken);

        if (analysisJob is null)
        {
            _logger.LogWarning("Face-analysis entity {JobId} not found in database; skipping.", job.JobId);
            return;
        }

        if (analysisJob.Status is JobStatusSucceeded or JobStatusFailed or "Canceled" or "TimedOut")
        {
            _logger.LogInformation("Face-analysis job {JobId} already in terminal state {Status}; skipping.", analysisJob.Id, analysisJob.Status);
            return;
        }

        analysisJob.Status = JobStatusProcessing;
        analysisJob.StartedAtUtc ??= DateTimeOffset.UtcNow;
        analysisJob.ErrorCode = null;
        analysisJob.ErrorMessage = null;
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            if (string.IsNullOrWhiteSpace(analysisJob.ImageUrl))
            {
                await MarkFailedAsync(
                    analysisJob,
                    "ANALYSIS_IMAGE_UNREACHABLE",
                    "Image URL is required for face analysis.",
                    cancellationToken);
                return;
            }

            if (!Uri.TryCreate(analysisJob.ImageUrl, UriKind.Absolute, out _))
            {
                await MarkFailedAsync(
                    analysisJob,
                    "ANALYSIS_IMAGE_UNREACHABLE",
                    "Image URL must be absolute.",
                    cancellationToken);
                return;
            }

            await EnsureImageUrlReachableAsync(analysisJob.ImageUrl, cancellationToken);

            var pipelineResult = await _pipeline.AnalyzeAsync(analysisJob.ImageUrl, analysisJob.Gender, cancellationToken);

            analysisJob.QualityPassed = pipelineResult.QualityPassed;
            analysisJob.QualityFailureCode = pipelineResult.QualityFailureCode;
            analysisJob.QualityMessage = pipelineResult.QualityMessage;
            analysisJob.FeatureVectorJson = pipelineResult.FeatureVectorJson;
            analysisJob.AnalysisConfidence = pipelineResult.AnalysisConfidence;
            analysisJob.RecommendationsJson = pipelineResult.RecommendationsJson;
            analysisJob.Status = JobStatusSucceeded;
            analysisJob.CompletedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Face-analysis job {JobId} completed with model-stage pipeline output.", analysisJob.Id);
        }
        catch (FaceAnalysisException ex)
        {
            _logger.LogWarning(ex, "Face-analysis job {JobId} failed quality/model stage with {ErrorCode}.", analysisJob.Id, ex.Code);
            await MarkFailedAsync(analysisJob, ex.Code, ex.Message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Face-analysis job {JobId} failed with unhandled exception.", analysisJob.Id);
            await MarkFailedAsync(analysisJob, "ANALYSIS_INTERNAL_ERROR", ex.Message, cancellationToken);
        }
    }

    private async Task EnsureImageUrlReachableAsync(string imageUrl, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient();

        using var headRequest = new HttpRequestMessage(HttpMethod.Head, imageUrl);
        using var headResponse = await client.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (headResponse.IsSuccessStatusCode)
        {
            return;
        }

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, imageUrl);
        getRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);

        using var getResponse = await client.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (getResponse.IsSuccessStatusCode || getResponse.StatusCode == System.Net.HttpStatusCode.PartialContent)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Image URL is not reachable (HEAD {(int)headResponse.StatusCode}; GET {(int)getResponse.StatusCode}). URL: {imageUrl}");
    }

    private async Task MarkFailedAsync(
        FaceAnalysisJobEntity analysisJob,
        string errorCode,
        string errorMessage,
        CancellationToken ct)
    {
        analysisJob.Status = JobStatusFailed;
        analysisJob.ErrorCode = errorCode;
        analysisJob.ErrorMessage = errorMessage.Length <= 2000
            ? errorMessage
            : errorMessage[..1997] + "...";
        analysisJob.CompletedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

}
