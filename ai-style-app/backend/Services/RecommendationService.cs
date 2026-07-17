using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using AiStyleApp.Api.Models;
using AiStyleApp.Data;
using AiStyleApp.Data.Entities;
using AiStyleApp.Data.Queue;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AiStyleApp.Api.Services;

public class RecommendationService : IRecommendationService
{
    private readonly AppDbContext _db;
    private readonly IQueuePublisher _queue;
    private readonly IMetricsLogger _metricsLogger;
    private readonly IConfiguration _configuration;

    public RecommendationService(AppDbContext db, IQueuePublisher queue, IMetricsLogger metricsLogger, IConfiguration configuration)
    {
        _db = db;
        _queue = queue;
        _metricsLogger = metricsLogger;
        _configuration = configuration;
    }

    public async Task<(Guid AnalysisJobId, Guid RecommendationPostId)> CreateAndEnqueueAsync(
        CreateRecommendationsRequest request,
        string userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ImageUrl))
        {
            throw new ArgumentException("ImageUrl is required.", nameof(request));
        }

        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = userId,
            ImageUrl = request.ImageUrl,
            Gender = request.Gender,
            PreferencesJson = request.Preferences is null
                ? null
                : JsonSerializer.Serialize(request.Preferences),
            Status = "Queued"
        };

        var recommendationPost = new StyleItemEntity
        {
            UserId = userId,
            Name = "Recommendation Post (Processing)",
            Description = $"Primary recommendation from analysis job {analysisJob.Id}. Pending analysis.",
            Prompt = "Pending recommendation generation",
            ImageUrl = request.ImageUrl,
            IsResultPublic = true
        };

        _db.FaceAnalysisJobs.Add(analysisJob);
        _db.StyleItems.Add(recommendationPost);
        await _db.SaveChangesAsync(ct);

        var queueMessage = new StyleJob(
            JobId: analysisJob.Id,
            StyleItemId: Guid.Empty,
            UserId: userId,
            JobType: "face-analysis",
            Prompt: "face-analysis",
            EnqueuedAtUtc: DateTimeOffset.UtcNow,
            CorrelationId: Guid.NewGuid().ToString(),
            Attempt: 0,
            SchemaVersion: 2,
            ImageUrl: request.ImageUrl,
            Gender: request.Gender,
            PreferencesJson: analysisJob.PreferencesJson
        );

        await _queue.PublishAsync(queueMessage, ct);

        return (analysisJob.Id, recommendationPost.Id);
    }

    public async Task<RecommendationJobStatusResponse?> GetStatusAsync(
        Guid analysisJobId,
        string userId,
        CancellationToken ct = default)
    {
        var analysisJob = await _db.FaceAnalysisJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == analysisJobId && x.UserId == userId, ct);

        if (analysisJob is null)
        {
            return null;
        }

        var experiment = ParseExperimentFromFeatureVector(analysisJob.FeatureVectorJson)
            ?? BuildExperimentMetadata(userId);

        var recommendations = ParseRecommendations(analysisJob.RecommendationsJson);
        var bestRecommendation = recommendations.FirstOrDefault();
        var faceShape = ParseFaceShape(analysisJob.FeatureVectorJson);

        var linkedStyleItems = await _db.StyleItems
            .AsNoTracking()
            .Include(x => x.Jobs)
            .Where(x => x.UserId == userId && x.Description.Contains(analysisJob.Id.ToString()))
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var primaryStyleItem = linkedStyleItems.FirstOrDefault(x => x.IsResultPublic)
            ?? linkedStyleItems.FirstOrDefault();
        var bestJob = primaryStyleItem?.Jobs
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();

        RecommendationVariantResponse? bestVariant = null;
        if (bestJob is not null)
        {
            bestVariant = new RecommendationVariantResponse(
                GenerationJobId: bestJob.Id,
                Status: bestJob.Status,
                ResultImageUrl: bestJob.ResultImageUrl);
        }

        var experimentalItems = linkedStyleItems
            .Where(x => primaryStyleItem is null || x.Id != primaryStyleItem.Id)
            .Take(3)
            .ToList();

        var feedbackBySelectionId = await _db.RecommendationFeedback
            .AsNoTracking()
            .Where(x => x.AnalysisJobId == analysisJobId)
            .ToDictionaryAsync(
                x => x.SelectedStyleId ?? string.Empty,
                x => x.Rating,
                ct);

        var experimentalVariants = new List<RecommendationExperimentalVariantResponse>();
        for (var i = 0; i < experimentalItems.Count; i++)
        {
            var item = experimentalItems[i];
            var job = item.Jobs.OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();
            if (job is null)
            {
                continue;
            }

            var key = job.Id.ToString();
            var selectedRank = feedbackBySelectionId.TryGetValue(key, out var rank) && rank.HasValue
                ? rank.Value.ToString()
                : null;

            experimentalVariants.Add(new RecommendationExperimentalVariantResponse(
                Slot: i + 1,
                GenerationJobId: job.Id,
                Status: job.Status,
                ResultImageUrl: job.ResultImageUrl,
                SelectedRank: selectedRank));
        }

        var recommendationPostId = primaryStyleItem?.Id;
        var publishStatus = primaryStyleItem is null ? null : "Published";

        return new RecommendationJobStatusResponse(
            AnalysisJobId: analysisJob.Id,
            RecommendationPostId: recommendationPostId,
            PublishStatus: publishStatus,
            Status: analysisJob.Status,
            QualityGate: new RecommendationQualityGateResponse(
                Passed: analysisJob.QualityPassed,
                FailureCode: analysisJob.QualityFailureCode,
                Message: analysisJob.QualityMessage),
            AnalysisSummary: new RecommendationAnalysisSummaryResponse(
                FaceShape: faceShape,
                Confidence: analysisJob.AnalysisConfidence),
            BestRecommendation: bestRecommendation,
            BestVariant: bestVariant,
            ExperimentalVariants: experimentalVariants,
            Recommendations: recommendations,
            Experiment: experiment,
            DebugTelemetry: ParseDebugTelemetry(analysisJob.FeatureVectorJson),
            ErrorCode: analysisJob.ErrorCode,
            ErrorMessage: analysisJob.ErrorMessage);
    }

    public async Task SubmitFeedbackAsync(
        SubmitRecommendationFeedbackRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var analysisJob = await _db.FaceAnalysisJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.AnalysisJobId && x.UserId == userId, ct);

        if (analysisJob is null)
        {
            throw new InvalidOperationException("Analysis job not found.");
        }

        var feedback = new RecommendationFeedbackEntity
        {
            AnalysisJobId = request.AnalysisJobId,
            UserId = userId,
            SelectedStyleId = request.SelectedStyleId,
            Rating = request.Rating,
            FeedbackTagsJson = request.FeedbackTags is null
                ? null
                : JsonSerializer.Serialize(request.FeedbackTags),
            Comment = request.Comment
        };

        _db.RecommendationFeedback.Add(feedback);
        await _db.SaveChangesAsync(ct);

        // Log feedback metrics for monitoring and analysis
        var recommendationRank = GetRecommendationRank(analysisJob.RecommendationsJson, request.SelectedStyleId);
        _metricsLogger.LogRecommendationFeedbackSubmitted(
            request.AnalysisJobId,
            userId,
            request.SelectedStyleId,
            request.Rating,
            feedback.FeedbackTagsJson,
            recommendationRank);
    }

    public async Task SubmitRatingsAsync(
        Guid analysisJobId,
        SubmitRecommendationRatingsRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var analysisJob = await _db.FaceAnalysisJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == analysisJobId && x.UserId == userId, ct);

        if (analysisJob is null)
        {
            throw new InvalidOperationException("Analysis job not found.");
        }

        if (request.Rankings is null || request.Rankings.Count == 0)
        {
            throw new ArgumentException("At least one ranking is required.", nameof(request));
        }

        foreach (var ranking in request.Rankings)
        {
            if (ranking.Rank is < 1 or > 3)
            {
                throw new ArgumentException("Rank must be between 1 and 3.", nameof(request));
            }
        }

        var serializedTags = request.FeedbackTags is null
            ? null
            : JsonSerializer.Serialize(request.FeedbackTags);

        var entries = request.Rankings.Select(ranking => new RecommendationFeedbackEntity
        {
            AnalysisJobId = analysisJobId,
            UserId = userId,
            // Store generationJobId string in selected_style_id for pre-MVP experimentation ranking labels.
            SelectedStyleId = ranking.GenerationJobId.ToString(),
            Rating = ranking.Rank,
            FeedbackTagsJson = serializedTags,
            Comment = request.Comment
        }).ToList();

        _db.RecommendationFeedback.AddRange(entries);
        await _db.SaveChangesAsync(ct);

        foreach (var ranking in request.Rankings)
        {
            _metricsLogger.LogRecommendationFeedbackSubmitted(
                analysisJobId,
                userId,
                ranking.GenerationJobId.ToString(),
                ranking.Rank,
                serializedTags,
                ranking.Rank);
        }
    }

    private static IReadOnlyList<RecommendationItemResponse> ParseRecommendations(string? recommendationsJson)
    {
        if (string.IsNullOrWhiteSpace(recommendationsJson))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<RecommendationItemResponse>>(recommendationsJson);
            return parsed ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static RecommendationDebugTelemetryResponse? ParseDebugTelemetry(string? featureVectorJson)
    {
        if (string.IsNullOrWhiteSpace(featureVectorJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(featureVectorJson);
            var root = doc.RootElement;

            var source = TryGetPropertyIgnoreCase(root, "source", out var sourceNode)
                ? sourceNode.GetString()
                : null;

            int? schemaVersion = null;
            if (TryGetPropertyIgnoreCase(root, "schemaVersion", out var schemaNode) && schemaNode.TryGetInt32(out var parsedSchema))
            {
                schemaVersion = parsedSchema;
            }

            int? imageWidth = null;
            int? imageHeight = null;
            if (TryGetPropertyIgnoreCase(root, "imageInfo", out var imageInfoNode))
            {
                if (TryGetPropertyIgnoreCase(imageInfoNode, "width", out var widthNode) && widthNode.TryGetInt32(out var parsedWidth))
                {
                    imageWidth = parsedWidth;
                }

                if (TryGetPropertyIgnoreCase(imageInfoNode, "height", out var heightNode) && heightNode.TryGetInt32(out var parsedHeight))
                {
                    imageHeight = parsedHeight;
                }
            }

            var stages = new List<RecommendationStageTelemetryResponse>();
            if (TryGetPropertyIgnoreCase(root, "stageTelemetry", out var telemetryNode)
                && telemetryNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var stageNode in telemetryNode.EnumerateArray())
                {
                    var stage = TryGetPropertyIgnoreCase(stageNode, "stage", out var stageNameNode)
                        ? stageNameNode.GetString() ?? "unknown"
                        : "unknown";

                    var model = TryGetPropertyIgnoreCase(stageNode, "model", out var modelNode)
                        ? modelNode.GetString() ?? "unknown"
                        : "unknown";

                    var modelVersion = TryGetPropertyIgnoreCase(stageNode, "modelVersion", out var modelVersionNode)
                        ? modelVersionNode.GetString() ?? "unknown"
                        : "unknown";

                    var durationMs = TryGetPropertyIgnoreCase(stageNode, "durationMs", out var durationNode)
                        ? durationNode.GetDouble()
                        : 0.0;

                    var notes = TryGetPropertyIgnoreCase(stageNode, "notes", out var notesNode)
                        ? notesNode.GetString()
                        : null;

                    Dictionary<string, double>? metrics = null;
                    if (TryGetPropertyIgnoreCase(stageNode, "metrics", out var metricsNode)
                        && metricsNode.ValueKind == JsonValueKind.Object)
                    {
                        metrics = [];
                        foreach (var metric in metricsNode.EnumerateObject())
                        {
                            if (metric.Value.ValueKind == JsonValueKind.Number && metric.Value.TryGetDouble(out var metricValue))
                            {
                                metrics[metric.Name] = metricValue;
                            }
                        }
                    }

                    stages.Add(new RecommendationStageTelemetryResponse(
                        Stage: stage,
                        Model: model,
                        ModelVersion: modelVersion,
                        DurationMs: Math.Round(durationMs, 2),
                        Metrics: metrics,
                        Notes: notes));
                }
            }

            return new RecommendationDebugTelemetryResponse(
                Source: source,
                SchemaVersion: schemaVersion,
                ImageWidth: imageWidth,
                ImageHeight: imageHeight,
                Stages: stages);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? ParseFaceShape(string? featureVectorJson)
    {
        if (string.IsNullOrWhiteSpace(featureVectorJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(featureVectorJson);
            var root = doc.RootElement;

            if (TryGetPropertyIgnoreCase(root, "faceShape", out var shapeNode)
                && shapeNode.ValueKind == JsonValueKind.String)
            {
                return shapeNode.GetString();
            }

            if (TryGetPropertyIgnoreCase(root, "stageTelemetry", out var telemetryNode)
                && telemetryNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var stage in telemetryNode.EnumerateArray())
                {
                    if (!TryGetPropertyIgnoreCase(stage, "stage", out var stageName)
                        || !string.Equals(stageName.GetString(), "landmarks", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (TryGetPropertyIgnoreCase(stage, "notes", out var notes)
                        && notes.ValueKind == JsonValueKind.String)
                    {
                        return notes.GetString();
                    }
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }

        return null;
    }

    private static int? GetRecommendationRank(string? recommendationsJson, string? selectedStyleId)
    {
        if (string.IsNullOrEmpty(recommendationsJson) || string.IsNullOrEmpty(selectedStyleId))
            return null;

        try
        {
            var parsed = JsonSerializer.Deserialize<List<RecommendationItemResponse>>(recommendationsJson);
            if (parsed == null)
                return null;

            for (int i = 0; i < parsed.Count; i++)
            {
                if (parsed[i].StyleId == selectedStyleId)
                    return i + 1; // Rank is 1-based
            }
        }
        catch (JsonException)
        {
            // Fall through to return null
        }

        return null;
    }

    private RecommendationExperimentResponse BuildExperimentMetadata(string userId)
    {
        var enabled = _configuration.GetValue("Features:ExperimentationModeEnabled", false);
        var trafficPercentRaw = _configuration.GetValue("Features:ExperimentationTrafficPercent", 0);
        var trafficPercent = Math.Clamp(trafficPercentRaw, 0, 100);

        var bucket = ComputeStableBucket(userId);
        var applied = enabled && bucket < trafficPercent;
        var bucketKey = $"user-hash-{bucket:00}";

        return new RecommendationExperimentResponse(
            Enabled: enabled,
            TrafficPercent: trafficPercent,
            Applied: applied,
            BucketKey: bucketKey);
    }

    private static RecommendationExperimentResponse? ParseExperimentFromFeatureVector(string? featureVectorJson)
    {
        if (string.IsNullOrWhiteSpace(featureVectorJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(featureVectorJson);
            var root = doc.RootElement;

            if (!TryGetPropertyIgnoreCase(root, "experiment", out var experimentNode)
                || experimentNode.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var enabled = TryGetPropertyIgnoreCase(experimentNode, "enabled", out var enabledNode)
                && enabledNode.ValueKind is JsonValueKind.True or JsonValueKind.False
                && enabledNode.GetBoolean();

            var trafficPercent = 0;
            if (TryGetPropertyIgnoreCase(experimentNode, "trafficPercent", out var trafficNode)
                && trafficNode.TryGetInt32(out var parsedTraffic))
            {
                trafficPercent = Math.Clamp(parsedTraffic, 0, 100);
            }

            var applied = TryGetPropertyIgnoreCase(experimentNode, "applied", out var appliedNode)
                && appliedNode.ValueKind is JsonValueKind.True or JsonValueKind.False
                && appliedNode.GetBoolean();

            var bucketKey = TryGetPropertyIgnoreCase(experimentNode, "bucketKey", out var bucketNode)
                ? bucketNode.GetString() ?? "unknown"
                : "unknown";

            return new RecommendationExperimentResponse(
                Enabled: enabled,
                TrafficPercent: trafficPercent,
                Applied: applied,
                BucketKey: bucketKey);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static int ComputeStableBucket(string userId)
    {
        var value = string.IsNullOrWhiteSpace(userId) ? "anonymous" : userId.Trim();
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        var sample = BitConverter.ToUInt32(hash, 0);
        return (int)(sample % 100);
    }
}
