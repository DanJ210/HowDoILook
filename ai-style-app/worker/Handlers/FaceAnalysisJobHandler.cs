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
    private readonly IWorkerQueuePublisher _queuePublisher;
    private readonly ILogger<FaceAnalysisJobHandler> _logger;
    private readonly IMetricsLogger _metricsLogger;
    private static readonly JsonSerializerOptions RecommendationJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly IReadOnlyDictionary<string, RecommendationStyleTemplate> RecommendationStyleTemplates =
        new Dictionary<string, RecommendationStyleTemplate>(StringComparer.OrdinalIgnoreCase)
        {
            ["textured-crop"] = new("Crew Cut", "No change", null, null),
            ["classic-side-part"] = new("Side-Parted", "No change", null, null),
            ["short-quiff"] = new("Slicked Back", "No change", null, null),
            ["short-boxed-beard"] = new("No change", "No change", "Short boxed beard", null)
        };

    public FaceAnalysisJobHandler(
        AppDbContext db,
        IHttpClientFactory httpClientFactory,
        IFaceAnalysisPipeline pipeline,
        IWorkerQueuePublisher queuePublisher,
        ILogger<FaceAnalysisJobHandler> logger,
        IMetricsLogger metricsLogger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _pipeline = pipeline;
        _queuePublisher = queuePublisher;
        _logger = logger;
        _metricsLogger = metricsLogger;
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

if (!Uri.TryCreate(analysisJob.ImageUrl, UriKind.Absolute, out var uri) ||
    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
{
    await MarkFailedAsync(
        analysisJob,
        "ANALYSIS_IMAGE_UNREACHABLE",
        "Image URL must be an absolute http/https URL.",
        cancellationToken);
    return;
}

            await EnsureImageUrlReachableAsync(analysisJob.ImageUrl, cancellationToken);

            var pipelineResult = await _pipeline.AnalyzeAsync(
                analysisJob.ImageUrl,
                analysisJob.Gender,
                analysisJob.PreferencesJson,
                analysisJob.UserId,
                cancellationToken);

            analysisJob.QualityPassed = pipelineResult.QualityPassed;
            analysisJob.QualityFailureCode = pipelineResult.QualityFailureCode;
            analysisJob.QualityMessage = pipelineResult.QualityMessage;
            analysisJob.FeatureVectorJson = pipelineResult.FeatureVectorJson;
            analysisJob.AnalysisConfidence = pipelineResult.AnalysisConfidence;
            analysisJob.RecommendationsJson = pipelineResult.RecommendationsJson;
            analysisJob.Status = JobStatusSucceeded;
            analysisJob.CompletedAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            await EnqueueRecommendationStyleJobsAsync(analysisJob, pipelineResult, cancellationToken);

            // Log metrics for monitoring and analysis
            var duration = (analysisJob.CompletedAtUtc - analysisJob.StartedAtUtc) ?? TimeSpan.Zero;
            var faceShape = ExtractFaceShape(analysisJob.FeatureVectorJson);
            var recommendationCount = CountRecommendations(analysisJob.RecommendationsJson);
            _metricsLogger.LogAnalysisJobCompleted(
                analysisJob.Id,
                analysisJob.UserId,
                analysisJob.QualityPassed ?? false,
                analysisJob.QualityFailureCode,
                analysisJob.AnalysisConfidence,
                faceShape,
                recommendationCount,
                duration);

            _logger.LogInformation("Face-analysis job {JobId} completed with model-stage pipeline output.", analysisJob.Id);
        }
catch (FaceAnalysisException ex)
{
    analysisJob.QualityPassed = false;
    analysisJob.QualityFailureCode = ex.Code;
    analysisJob.QualityMessage = ex.Message;

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

        // Log metrics for monitoring and analysis
        var duration = (analysisJob.CompletedAtUtc - analysisJob.StartedAtUtc) ?? TimeSpan.Zero;
        _metricsLogger.LogAnalysisJobFailed(
            analysisJob.Id,
            analysisJob.UserId,
            errorCode,
            analysisJob.ErrorMessage ?? errorMessage,
            duration);
    }

    private static string? ExtractFaceShape(string? featureVectorJson)
    {
        if (string.IsNullOrEmpty(featureVectorJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(featureVectorJson);
            // Only try to get property if the root element is an object
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("faceShape", out var shapeElement))
                return shapeElement.GetString();
        }
        catch (JsonException)
        {
            // Fall through to return null
        }

        return null;
    }

    private static int CountRecommendations(string? recommendationsJson)
    {
        if (string.IsNullOrEmpty(recommendationsJson))
            return 0;

        try
        {
            using var doc = JsonDocument.Parse(recommendationsJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return doc.RootElement.GetArrayLength();
            }

            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("topStyles", out var stylesElement) &&
                stylesElement.ValueKind == JsonValueKind.Array)
            {
                return stylesElement.GetArrayLength();
            }
        }
        catch (JsonException)
        {
            // Fall through to return 0
        }

        return 0;
    }

    private async Task EnqueueRecommendationStyleJobsAsync(
        FaceAnalysisJobEntity analysisJob,
        FaceAnalysisPipelineResult pipelineResult,
        CancellationToken cancellationToken)
    {
        var recommendations = ParseRecommendations(pipelineResult.RecommendationsJson);
        if (recommendations.Count == 0)
        {
            _logger.LogInformation(
                "Face-analysis job {JobId} produced no recommendations; skipping style generation fan-out.",
                analysisJob.Id);
            return;
        }

        var experimentApplied = IsExperimentApplied(pipelineResult.FeatureVectorJson);
        var selected = recommendations
            .Take(experimentApplied ? 4 : 1)
            .ToList();

        var existingPrimaryPost = await _db.StyleItems
            .Include(x => x.Jobs)
            .FirstOrDefaultAsync(
                x => x.UserId == analysisJob.UserId
                     && x.IsResultPublic
                     && x.Description.Contains(analysisJob.Id.ToString()),
                cancellationToken);

        var styleItems = new List<StyleItemEntity>(selected.Count);
        var styleJobs = new List<StyleJobEntity>(selected.Count);

        for (var index = 0; index < selected.Count; index++)
        {
            var candidate = selected[index];
            var styleName = string.IsNullOrWhiteSpace(candidate.StyleName)
                ? candidate.StyleId ?? "Recommended Style"
                : candidate.StyleName;
            var template = ResolveTemplateOrThrow(candidate.StyleId, analysisJob.Gender);
            var isPrimary = index == 0;

            StyleItemEntity item;
            if (isPrimary && existingPrimaryPost is not null)
            {
                item = existingPrimaryPost;
                item.Name = $"Recommended: {styleName}";
                item.Description = BuildDescription(analysisJob.Id, styleName, candidate.Score, isPrimary, experimentApplied);
                item.Prompt = BuildPrompt(styleName, candidate.Reasons);
                item.ImageUrl = analysisJob.ImageUrl;
                item.IsResultPublic = true;
                item.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
            else
            {
                item = new StyleItemEntity
                {
                    UserId = analysisJob.UserId,
                    Name = isPrimary
                        ? $"Recommended: {styleName}"
                        : $"Experimental {index}: {styleName}",
                    Description = BuildDescription(analysisJob.Id, styleName, candidate.Score, isPrimary, experimentApplied),
                    Prompt = BuildPrompt(styleName, candidate.Reasons),
                    ImageUrl = analysisJob.ImageUrl,
                    IsResultPublic = isPrimary
                };
            }

            var job = new StyleJobEntity
            {
                UserId = analysisJob.UserId,
                StyleItemId = item.Id,
                JobType = "generate-style",
                Prompt = item.Prompt,
                ImageUrl = analysisJob.ImageUrl,
                CorrelationId = Guid.NewGuid().ToString(),
                Haircut = template.Haircut,
                HairColor = template.HairColor,
                BeardStyle = template.BeardStyle,
                BeardColor = template.BeardColor,
                Gender = analysisJob.Gender,
                PipelineMode = StyleJobRouting.DeterminePipelineMode(
                    template.Haircut,
                    template.HairColor,
                    template.BeardStyle,
                    template.BeardColor,
                    analysisJob.Gender),
                CurrentStage = StyleJobStage.Queued,
                IsBeardStagePending = false
            };

            job.IsBeardStagePending = job.PipelineMode == StyleJobPipelineMode.HairThenBeard;

            _db.StyleJobs.Add(job);
            if (!(isPrimary && existingPrimaryPost is not null))
            {
                styleItems.Add(item);
            }
            styleJobs.Add(job);

            if (isPrimary)
            {
                analysisJob.PrimaryStyleId = candidate.StyleId;
                analysisJob.PrimaryStyleItemId = item.Id;
                analysisJob.PrimaryGenerationJobId = job.Id;
            }
        }

        if (styleItems.Count > 0)
        {
            _db.StyleItems.AddRange(styleItems);
        }
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var job in styleJobs)
        {
            var queueMessage = new StyleJob(
                JobId: job.Id,
                StyleItemId: job.StyleItemId,
                UserId: job.UserId,
                JobType: job.JobType,
                Prompt: job.Prompt,
                EnqueuedAtUtc: DateTimeOffset.UtcNow,
                CorrelationId: job.CorrelationId ?? Guid.NewGuid().ToString(),
                Attempt: 0,
                SchemaVersion: 2,
                ImageUrl: job.ImageUrl,
                Haircut: job.Haircut,
                HairColor: job.HairColor,
                BeardStyle: job.BeardStyle,
                BeardColor: job.BeardColor,
                Gender: job.Gender,
                Stage: null,
                PreferencesJson: analysisJob.PreferencesJson);

            await _queuePublisher.PublishAsync(queueMessage, cancellationToken);
        }

        _logger.LogInformation(
            "Face-analysis job {JobId} enqueued {Count} recommendation style jobs (experimentApplied={ExperimentApplied}).",
            analysisJob.Id,
            styleJobs.Count,
            experimentApplied);
    }

    private static List<RecommendationCandidate> ParseRecommendations(string? recommendationsJson)
    {
        if (string.IsNullOrWhiteSpace(recommendationsJson))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<RecommendationCandidate>>(recommendationsJson, RecommendationJsonOptions);
            return parsed ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool IsExperimentApplied(string? featureVectorJson)
    {
        if (string.IsNullOrWhiteSpace(featureVectorJson))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(featureVectorJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!doc.RootElement.TryGetProperty("experiment", out var experiment)
                || experiment.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return experiment.TryGetProperty("applied", out var applied)
                && applied.ValueKind is JsonValueKind.True or JsonValueKind.False
                && applied.GetBoolean();
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static RecommendationStyleTemplate ResolveTemplateOrThrow(string? styleId, string? gender)
    {
        if (string.IsNullOrWhiteSpace(styleId))
        {
            throw new FaceAnalysisException(
                "ANALYSIS_RECOMMENDATION_TEMPLATE_MISSING",
                "Recommendation candidate is missing styleId required for generation template mapping.");
        }

        if (RecommendationStyleTemplates.TryGetValue(styleId, out var template))
        {
            if (!StyleJobRouting.AllowsBeard(gender))
            {
                return template with { BeardStyle = null, BeardColor = null };
            }

            return template;
        }

        throw new FaceAnalysisException(
            "ANALYSIS_RECOMMENDATION_TEMPLATE_MISSING",
            $"No generation template mapping exists for recommended styleId '{styleId}'.");
    }

    private static string BuildPrompt(string styleName, IReadOnlyList<string>? reasons)
    {
        var reasonText = reasons is { Count: > 0 }
            ? string.Join("; ", reasons)
            : "Personalized fit from face analysis.";

        return $"Apply recommended style '{styleName}'. Rationale: {reasonText}";
    }

    private static string BuildDescription(Guid analysisJobId, string styleName, double score, bool isPrimary, bool experimentApplied)
    {
        var role = isPrimary ? "Primary" : "Experimental";
        return $"{role} recommendation from analysis job {analysisJobId}. Style: {styleName}. Score: {score:F3}. ExperimentApplied={experimentApplied}.";
    }

    private sealed record RecommendationStyleTemplate(
        string Haircut,
        string HairColor,
        string? BeardStyle,
        string? BeardColor);
}
