using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AiStyleApp.Data;

public interface IMetricsLogger
{
    void LogAnalysisJobCompleted(
        Guid jobId,
        string userId,
        bool qualityPassed,
        string? qualityFailureCode,
        double? analysisConfidence,
        string? faceShape,
        int recommendationCount,
        TimeSpan duration);

    void LogAnalysisJobFailed(
        Guid jobId,
        string userId,
        string errorCode,
        string errorMessage,
        TimeSpan duration);

    void LogRecommendationFeedbackSubmitted(
        Guid jobId,
        string userId,
        string? selectedStyleId,
        int? rating,
        string? tags,
        int? recommendationRank);
}

public class MetricsLogger : IMetricsLogger
{
    private readonly ILogger<MetricsLogger> _logger;

    public MetricsLogger(ILogger<MetricsLogger> logger)
    {
        _logger = logger;
    }

    public void LogAnalysisJobCompleted(
        Guid jobId,
        string userId,
        bool qualityPassed,
        string? qualityFailureCode,
        double? analysisConfidence,
        string? faceShape,
        int recommendationCount,
        TimeSpan duration)
    {
        var @event = new
        {
            eventType = "analysis.job.completed",
            timestamp = DateTimeOffset.UtcNow.UtcDateTime,
            jobId = jobId,
            userId = userId,
            qualityPassed = qualityPassed,
            qualityFailureCode = qualityFailureCode,
            analysisConfidence = analysisConfidence,
            faceShape = faceShape,
            recommendationCount = recommendationCount,
            durationMs = (long)duration.TotalMilliseconds,
            status = "success"
        };

        LogStructuredEvent(@event);
    }

    public void LogAnalysisJobFailed(
        Guid jobId,
        string userId,
        string errorCode,
        string errorMessage,
        TimeSpan duration)
    {
        var @event = new
        {
            eventType = "analysis.job.failed",
            timestamp = DateTimeOffset.UtcNow.UtcDateTime,
            jobId = jobId,
            userId = userId,
            errorCode = errorCode,
            errorMessage = errorMessage,
            durationMs = (long)duration.TotalMilliseconds,
            status = "failed"
        };

        LogStructuredEvent(@event);
    }

    public void LogRecommendationFeedbackSubmitted(
        Guid jobId,
        string userId,
        string? selectedStyleId,
        int? rating,
        string? tags,
        int? recommendationRank)
    {
        var @event = new
        {
            eventType = "recommendation.feedback.submitted",
            timestamp = DateTimeOffset.UtcNow.UtcDateTime,
            jobId = jobId,
            userId = userId,
            selectedStyleId = selectedStyleId,
            rating = rating,
            tags = tags,
            recommendationRank = recommendationRank,
            status = "success"
        };

        LogStructuredEvent(@event);
    }

    private void LogStructuredEvent(object @event)
    {
        var json = JsonSerializer.Serialize(@event, new JsonSerializerOptions { WriteIndented = false });
        
        // Log as structured JSON for easy parsing/analysis
        _logger.LogInformation("METRICS_EVENT: {MetricsJson}", json);
    }
}
