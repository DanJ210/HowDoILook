using System.Globalization;
using System.Text;
using System.Text.Json;
using AiStyleApp.Data;
using AiStyleApp.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiStyleApp.Backend.Services;

public interface IAnalyticsService
{
    Task<IEnumerable<RecommendationDataPoint>> ExportRecommendationsDataAsync(
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null);

    Task<RecommendationMetrics> GetMetricsAsync(
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null);
}

public class AnalyticsService : IAnalyticsService
{
    private readonly AppDbContext _context;
    private readonly ILogger<AnalyticsService> _logger;

    public AnalyticsService(AppDbContext context, ILogger<AnalyticsService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IEnumerable<RecommendationDataPoint>> ExportRecommendationsDataAsync(
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null)
    {
        var query = _context.Set<FaceAnalysisJobEntity>()
            .Include(j => j.Feedback)
            .Where(j => j.Status == "Succeeded" && j.RecommendationsJson != null)
            .AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(j => j.CompletedAtUtc >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(j => j.CompletedAtUtc <= toDate.Value);

        var jobs = await query.ToListAsync();

        var dataPoints = new List<RecommendationDataPoint>();

        foreach (var job in jobs)
        {
            var faceShape = ExtractFaceShape(job.FeatureVectorJson);
            var recommendations = ParseRecommendations(job.RecommendationsJson);
            var feedback = job.Feedback.FirstOrDefault();

            // Create one data point per job (with feedback if available)
            var dataPoint = new RecommendationDataPoint
            {
                AnalysisJobId = job.Id,
                UserId = job.UserId,
                FaceShape = faceShape,
                Gender = job.Gender,
                QualityPassed = job.QualityPassed ?? false,
                AnalysisConfidence = job.AnalysisConfidence ?? 0,
                TopRecommendationStyleId = recommendations.FirstOrDefault()?.StyleId ?? "unknown",
                TopRecommendationScore = recommendations.FirstOrDefault()?.Score ?? 0,
                RecommendationCount = recommendations.Count,
                SelectedStyleId = feedback?.SelectedStyleId,
                FeedbackRating = feedback?.Rating,
                FeedbackTags = feedback?.FeedbackTagsJson,
                AnalysisCompletedAtUtc = job.CompletedAtUtc,
                FeedbackSubmittedAtUtc = feedback?.CreatedAtUtc,
                RecommendationRank = GetRecommendationRank(recommendations, feedback?.SelectedStyleId)
            };

            dataPoints.Add(dataPoint);
        }

        _logger.LogInformation(
            "Exported {Count} recommendation data points (from: {From}, to: {To})",
            dataPoints.Count,
            fromDate?.UtcDateTime,
            toDate?.UtcDateTime);

        return dataPoints;
    }

    public async Task<RecommendationMetrics> GetMetricsAsync(
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null)
    {
        var query = _context.Set<FaceAnalysisJobEntity>()
            .Include(j => j.Feedback)
            .AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(j => j.CompletedAtUtc >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(j => j.CompletedAtUtc <= toDate.Value);

        var jobs = await query.ToListAsync();

        var succeeded = jobs.Count(j => j.Status == "Succeeded");
        var failed = jobs.Count(j => j.Status == "Failed");
        var totalAnalyses = jobs.Count;

        var withFeedback = jobs.Count(j => j.Feedback.Any());
        var withPositiveFeedback = jobs
            .Where(j => j.Feedback.Any())
            .Count(j => j.Feedback.Any(f => f.Rating.HasValue && f.Rating.Value >= 4));

        var confidences = jobs
            .Where(j => j.AnalysisConfidence.HasValue)
            .Select(j => j.AnalysisConfidence.GetValueOrDefault())
            .ToList();

        var avgConfidence = confidences.Count > 0
            ? confidences.Average()
            : 0;

        var faceShapeDistribution = jobs
            .GroupBy(j => ExtractFaceShape(j.FeatureVectorJson))
            .ToDictionary(g => g.Key ?? "Unknown", g => g.Count());

        var topRecommendedStyles = jobs
            .Where(j => j.RecommendationsJson != null)
            .SelectMany(j => ParseRecommendations(j.RecommendationsJson))
            .GroupBy(r => r.StyleId)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .ToDictionary(g => g.Key, g => g.Count());

        var metrics = new RecommendationMetrics
        {
            TotalAnalyses = totalAnalyses,
            SuccessfulAnalyses = succeeded,
            FailedAnalyses = failed,
            SuccessRate = totalAnalyses > 0 ? (double)succeeded / totalAnalyses : 0,
            AnalysesWithFeedback = withFeedback,
            ClickThroughRate = totalAnalyses > 0 ? (double)withFeedback / totalAnalyses : 0,
            PositiveFeedbackRate = withFeedback > 0 ? (double)withPositiveFeedback / withFeedback : 0,
            AverageConfidence = avgConfidence,
            FaceShapeDistribution = faceShapeDistribution,
            TopRecommendedStyles = topRecommendedStyles,
            PeriodStart = fromDate,
            PeriodEnd = toDate,
            ComputedAtUtc = DateTimeOffset.UtcNow
        };

        _logger.LogInformation(
            "Computed metrics: SuccessRate={SuccessRate:P}, CTR={CTR:P}, AvgConfidence={Confidence:F2}",
            metrics.SuccessRate,
            metrics.ClickThroughRate,
            metrics.AverageConfidence);

        return metrics;
    }

    private string? ExtractFaceShape(string? featureVectorJson)
    {
        if (string.IsNullOrEmpty(featureVectorJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(featureVectorJson);
            if (doc.RootElement.TryGetProperty("faceShape", out var shapeElement))
                return shapeElement.GetString();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to extract faceShape from feature vector");
        }

        return null;
    }

    private List<RecommendationEntry> ParseRecommendations(string? recommendationsJson)
    {
        var results = new List<RecommendationEntry>();

        if (string.IsNullOrEmpty(recommendationsJson))
            return results;

        try
        {
            using var doc = JsonDocument.Parse(recommendationsJson);
            var stylesElement = GetRecommendationArrayElement(doc.RootElement);
            if (stylesElement is { ValueKind: JsonValueKind.Array })
            {
                var rank = 1;
                foreach (var styleElement in stylesElement.Value.EnumerateArray())
                {
                    if (styleElement.TryGetProperty("styleId", out var styleIdElement) &&
                        styleElement.TryGetProperty("score", out var scoreElement) &&
                        scoreElement.TryGetDouble(out var score))
                    {
                        results.Add(new RecommendationEntry
                        {
                            StyleId = styleIdElement.GetString() ?? "unknown",
                            Score = score,
                            Rank = rank++
                        });
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse recommendations JSON");
        }

        return results;
    }

    private static JsonElement? GetRecommendationArrayElement(JsonElement rootElement)
    {
        if (rootElement.ValueKind == JsonValueKind.Array)
        {
            return rootElement;
        }

        if (rootElement.ValueKind == JsonValueKind.Object &&
            rootElement.TryGetProperty("topStyles", out var stylesElement) &&
            stylesElement.ValueKind == JsonValueKind.Array)
        {
            return stylesElement;
        }

        return null;
    }

    private int? GetRecommendationRank(List<RecommendationEntry> recommendations, string? selectedStyleId)
    {
        if (string.IsNullOrEmpty(selectedStyleId))
            return null;

        var selected = recommendations.FirstOrDefault(r => r.StyleId == selectedStyleId);
        return selected?.Rank;
    }

    private record RecommendationEntry
    {
        public string StyleId { get; set; } = null!;
        public double Score { get; set; }
        public int Rank { get; set; }
    }
}

public record RecommendationDataPoint
{
    public Guid AnalysisJobId { get; set; }
    public string UserId { get; set; } = null!;
    public string? FaceShape { get; set; }
    public string? Gender { get; set; }
    public bool QualityPassed { get; set; }
    public double AnalysisConfidence { get; set; }
    public string TopRecommendationStyleId { get; set; } = null!;
    public double TopRecommendationScore { get; set; }
    public int RecommendationCount { get; set; }
    public string? SelectedStyleId { get; set; }
    public int? FeedbackRating { get; set; }
    public string? FeedbackTags { get; set; }
    public DateTimeOffset? AnalysisCompletedAtUtc { get; set; }
    public DateTimeOffset? FeedbackSubmittedAtUtc { get; set; }
    public int? RecommendationRank { get; set; }
}

public record RecommendationMetrics
{
    public int TotalAnalyses { get; set; }
    public int SuccessfulAnalyses { get; set; }
    public int FailedAnalyses { get; set; }
    public double SuccessRate { get; set; }
    public int AnalysesWithFeedback { get; set; }
    public double ClickThroughRate { get; set; }
    public double PositiveFeedbackRate { get; set; }
    public double AverageConfidence { get; set; }
    public Dictionary<string, int> FaceShapeDistribution { get; set; } = new();
    public Dictionary<string, int> TopRecommendedStyles { get; set; } = new();
    public DateTimeOffset? PeriodStart { get; set; }
    public DateTimeOffset? PeriodEnd { get; set; }
    public DateTimeOffset ComputedAtUtc { get; set; }
}
