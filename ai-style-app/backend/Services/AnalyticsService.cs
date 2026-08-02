using System.Globalization;
using System.Text;
using System.Text.Json;
using AiStyleApp.Api.Models;
using AiStyleApp.Data;
using AiStyleApp.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiStyleApp.Api.Services;

public interface IAnalyticsService
{
    Task<IReadOnlyList<RecommendationExposureDataPoint>> ExportExposureOutcomesAsync(
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null);

    Task<IReadOnlyList<RecommendationPreferenceDataPoint>> ExportPreferenceLabelsAsync(
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null);

    Task<RecommendationCoverageReport> GetCoverageAsync(
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null);

    Task<RecommendationMetrics> GetMetricsAsync(
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null);
}

public class AnalyticsService : IAnalyticsService
{
    private const int MinimumCoverageSampleSize = 20;
    private readonly AppDbContext _context;
    private readonly ILogger<AnalyticsService> _logger;

    public AnalyticsService(AppDbContext context, ILogger<AnalyticsService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RecommendationExposureDataPoint>> ExportExposureOutcomesAsync(
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null)
    {
        var query = _context.RecommendationExposures
            .AsNoTracking()
            .Include(exposure => exposure.AnalysisJob)
            .Include(exposure => exposure.Candidates)
            .AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(exposure => exposure.CreatedAtUtc >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(exposure => exposure.CreatedAtUtc <= toDate.Value);

        var exposures = await query.ToListAsync();
        var generationJobIds = exposures
            .SelectMany(exposure => exposure.Candidates)
            .Where(candidate => candidate.GenerationJobId.HasValue)
            .Select(candidate => candidate.GenerationJobId!.Value)
            .Distinct()
            .ToList();
        var generationJobs = await _context.StyleJobs
            .AsNoTracking()
            .Where(job => generationJobIds.Contains(job.Id))
            .ToDictionaryAsync(job => job.Id);

        var dataPoints = exposures
            .SelectMany(exposure => exposure.Candidates
                .OrderBy(candidate => candidate.RecommendationRank)
                .Select(candidate => CreateExposureDataPoint(exposure, candidate, generationJobs)))
            .ToList();

        _logger.LogInformation(
            "Exported {Count} recommendation exposure/outcome rows (from: {From}, to: {To})",
            dataPoints.Count,
            fromDate?.UtcDateTime,
            toDate?.UtcDateTime);

        return dataPoints;
    }

    public async Task<IReadOnlyList<RecommendationPreferenceDataPoint>> ExportPreferenceLabelsAsync(
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null)
    {
        var query = _context.RecommendationExposures
            .AsNoTracking()
            .Include(exposure => exposure.Candidates)
            .Include(exposure => exposure.AnalysisJob)
                .ThenInclude(analysisJob => analysisJob.Feedback)
            .AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(exposure => exposure.CreatedAtUtc >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(exposure => exposure.CreatedAtUtc <= toDate.Value);

        var exposures = await query.ToListAsync();
        var rows = new List<RecommendationPreferenceDataPoint>();

        foreach (var exposure in exposures)
        {
            var shownCandidates = exposure.Candidates
                .Where(candidate => candidate.WasShown && candidate.GenerationJobId.HasValue && candidate.ShownOrder.HasValue)
                .ToDictionary(candidate => candidate.GenerationJobId!.Value);

            foreach (var feedback in exposure.AnalysisJob.Feedback)
            {
                if (!feedback.Rating.HasValue
                    || !Guid.TryParse(feedback.SelectedStyleId, out var generationJobId)
                    || !shownCandidates.TryGetValue(generationJobId, out var candidate))
                {
                    continue;
                }

                rows.Add(new RecommendationPreferenceDataPoint
                {
                    FeedbackId = feedback.Id,
                    ExposureId = exposure.Id,
                    AnalysisJobId = exposure.AnalysisJobId,
                    UserId = exposure.UserId,
                    ExperimentVersion = exposure.ExperimentVersion,
                    SystemSelectedStyleId = exposure.PrimaryStyleId,
                    PrimaryGenerationJobId = exposure.PrimaryGenerationJobId,
                    CandidateStyleId = candidate.StyleId,
                    GenerationJobId = generationJobId,
                    IsPrimary = candidate.IsPrimary,
                    ShownOrder = candidate.ShownOrder!.Value,
                    SelectionProbability = candidate.SelectionProbability,
                    SubmittedRank = feedback.Rating.Value,
                    FeedbackTagsJson = feedback.FeedbackTagsJson,
                    Comment = feedback.Comment,
                    FeedbackSubmittedAtUtc = feedback.CreatedAtUtc
                });
            }
        }

        _logger.LogInformation(
            "Exported {Count} complete recommendation preference-label rows (from: {From}, to: {To})",
            rows.Count,
            fromDate?.UtcDateTime,
            toDate?.UtcDateTime);

        return rows;
    }

    public async Task<RecommendationCoverageReport> GetCoverageAsync(
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null)
    {
        var query = _context.RecommendationExposures
            .AsNoTracking()
            .Include(exposure => exposure.Candidates)
            .Include(exposure => exposure.AnalysisJob)
                .ThenInclude(analysisJob => analysisJob.Feedback)
            .AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(exposure => exposure.CreatedAtUtc >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(exposure => exposure.CreatedAtUtc <= toDate.Value);

        var exposures = await query.ToListAsync();
        var generationJobIds = exposures
            .SelectMany(exposure => exposure.Candidates)
            .Where(candidate => candidate.GenerationJobId.HasValue)
            .Select(candidate => candidate.GenerationJobId!.Value)
            .Distinct()
            .ToList();
        var succeededGenerationJobIds = (await _context.StyleJobs
            .AsNoTracking()
            .Where(job => generationJobIds.Contains(job.Id) && job.Status == "Succeeded")
            .Select(job => job.Id)
            .ToListAsync())
            .ToHashSet();
        var preferenceGenerationJobIds = GetCompletePreferenceGenerationJobIds(exposures);

        var styleCoverage = exposures
            .SelectMany(exposure => exposure.Candidates)
            .GroupBy(candidate => candidate.StyleId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new StyleCoveragePoint
            {
                StyleId = group.Key,
                EligibleCount = group.Count(),
                ShownCount = group.Count(candidate => candidate.WasShown),
                PrimaryCount = group.Count(candidate => candidate.IsPrimary),
                SucceededGenerationCount = group.Count(candidate =>
                    candidate.GenerationJobId.HasValue
                    && succeededGenerationJobIds.Contains(candidate.GenerationJobId.Value)),
                PreferenceLabelCount = group.Count(candidate =>
                    candidate.GenerationJobId.HasValue
                    && preferenceGenerationJobIds.Contains(candidate.GenerationJobId.Value)),
                AverageSelectionProbability = group.Average(candidate => candidate.SelectionProbability),
                IsSparse = group.Count(candidate => candidate.WasShown) < MinimumCoverageSampleSize
            })
            .OrderBy(point => point.StyleId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var confidenceRanges = new[]
        {
            new ConfidenceRange("0.00-0.60", 0, 0.6),
            new ConfidenceRange("0.60-0.80", 0.6, 0.8),
            new ConfidenceRange("0.80-0.90", 0.8, 0.9),
            new ConfidenceRange("0.90-1.00", 0.9, null)
        };
        var telemetryCoverage = confidenceRanges
            .Select(range => CreateTelemetryRangeCoverage(range, exposures, preferenceGenerationJobIds))
            .ToList();

        return new RecommendationCoverageReport
        {
            MinimumSampleSize = MinimumCoverageSampleSize,
            ExposureCount = exposures.Count,
            PreferenceLabelCount = preferenceGenerationJobIds.Count,
            Styles = styleCoverage,
            AnalysisConfidenceRanges = telemetryCoverage,
            PeriodStart = fromDate,
            PeriodEnd = toDate,
            ComputedAtUtc = DateTimeOffset.UtcNow
        };
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
            .Count(j => j.Feedback.Any(f => f.Rating == 1));

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
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("faceShape", out var shapeElement))
                return shapeElement.GetString();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
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

    private RecommendationExposureDataPoint CreateExposureDataPoint(
        RecommendationExposureEntity exposure,
        RecommendationExposureCandidateEntity candidate,
        IReadOnlyDictionary<Guid, StyleJobEntity> generationJobs)
    {
        StyleJobEntity? generationJob = null;
        if (candidate.GenerationJobId.HasValue)
        {
            generationJobs.TryGetValue(candidate.GenerationJobId.Value, out generationJob);
        }

        return new RecommendationExposureDataPoint
        {
            ExposureId = exposure.Id,
            AnalysisJobId = exposure.AnalysisJobId,
            UserId = exposure.UserId,
            ExposureCreatedAtUtc = exposure.CreatedAtUtc,
            AnalysisCompletedAtUtc = exposure.AnalysisJob.CompletedAtUtc,
            FaceShape = ExtractFaceShape(exposure.AnalysisJob.FeatureVectorJson),
            Gender = exposure.AnalysisJob.Gender,
            QualityPassed = exposure.AnalysisJob.QualityPassed ?? false,
            AnalysisConfidence = exposure.AnalysisJob.AnalysisConfidence ?? 0,
            TelemetrySchemaVersion = exposure.TelemetrySchemaVersion,
            TelemetrySource = exposure.TelemetrySource,
            ExperimentVersion = exposure.ExperimentVersion,
            ExperimentApplied = exposure.ExperimentApplied,
            SystemSelectedStyleId = exposure.PrimaryStyleId,
            PrimaryGenerationJobId = exposure.PrimaryGenerationJobId,
            EligibleCandidateCount = exposure.Candidates.Count,
            ShownCandidateCount = exposure.Candidates.Count(item => item.WasShown),
            CandidateStyleId = candidate.StyleId,
            CandidateStyleName = candidate.StyleName,
            RecommendationRank = candidate.RecommendationRank,
            RankingScore = candidate.RankingScore,
            IsPrimary = candidate.IsPrimary,
            WasShown = candidate.WasShown,
            ShownOrder = candidate.ShownOrder,
            SelectionProbability = candidate.SelectionProbability,
            StyleItemId = candidate.StyleItemId,
            GenerationJobId = candidate.GenerationJobId,
            GenerationStatus = generationJob?.Status,
            GenerationErrorCode = generationJob?.ErrorCode,
            GenerationCompletedAtUtc = generationJob?.CompletedAtUtc
        };
    }

    private static HashSet<Guid> GetCompletePreferenceGenerationJobIds(
        IEnumerable<RecommendationExposureEntity> exposures)
    {
        var result = new HashSet<Guid>();
        foreach (var exposure in exposures)
        {
            var shownGenerationJobIds = exposure.Candidates
                .Where(candidate => candidate.WasShown && candidate.GenerationJobId.HasValue)
                .Select(candidate => candidate.GenerationJobId!.Value)
                .ToHashSet();

            foreach (var feedback in exposure.AnalysisJob.Feedback)
            {
                if (feedback.Rating.HasValue
                    && Guid.TryParse(feedback.SelectedStyleId, out var generationJobId)
                    && shownGenerationJobIds.Contains(generationJobId))
                {
                    result.Add(generationJobId);
                }
            }
        }

        return result;
    }

    private static TelemetryRangeCoveragePoint CreateTelemetryRangeCoverage(
        ConfidenceRange range,
        IReadOnlyCollection<RecommendationExposureEntity> exposures,
        IReadOnlySet<Guid> preferenceGenerationJobIds)
    {
        var matchingExposures = exposures
            .Where(exposure => exposure.AnalysisJob.AnalysisConfidence.HasValue)
            .Where(exposure => exposure.AnalysisJob.AnalysisConfidence!.Value >= range.LowerInclusive)
            .Where(exposure => !range.UpperExclusive.HasValue
                || exposure.AnalysisJob.AnalysisConfidence!.Value < range.UpperExclusive.Value)
            .ToList();
        var matchingCandidates = matchingExposures
            .SelectMany(exposure => exposure.Candidates)
            .ToList();

        return new TelemetryRangeCoveragePoint
        {
            Range = range.Name,
            LowerInclusive = range.LowerInclusive,
            UpperExclusive = range.UpperExclusive,
            ExposureCount = matchingExposures.Count,
            ShownCandidateCount = matchingCandidates.Count(candidate => candidate.WasShown),
            PreferenceLabelCount = matchingCandidates.Count(candidate =>
                candidate.GenerationJobId.HasValue
                && preferenceGenerationJobIds.Contains(candidate.GenerationJobId.Value)),
            IsSparse = matchingExposures.Count < MinimumCoverageSampleSize
        };
    }

    private int? ExtractTelemetrySchemaVersion(string? featureVectorJson)
    {
        if (string.IsNullOrWhiteSpace(featureVectorJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(featureVectorJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("schemaVersion", out var schemaVersionElement) &&
                schemaVersionElement.TryGetInt32(out var schemaVersion))
            {
                return schemaVersion;
            }

            return ExtractTelemetrySourceSchemaVersion(doc.RootElement);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to extract telemetry schema version from feature vector");
        }

        return null;
    }

    private int? ExtractTelemetrySourceSchemaVersion(JsonElement root)
    {
        if (root.TryGetProperty("debugTelemetry", out var debugTelemetry) &&
            debugTelemetry.ValueKind == JsonValueKind.Object &&
            debugTelemetry.TryGetProperty("schemaVersion", out var nestedSchemaVersionElement) &&
            nestedSchemaVersionElement.TryGetInt32(out var nestedSchemaVersion))
        {
            return nestedSchemaVersion;
        }

        return null;
    }

    private string? ExtractTelemetrySource(string? featureVectorJson)
    {
        if (string.IsNullOrWhiteSpace(featureVectorJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(featureVectorJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (doc.RootElement.TryGetProperty("source", out var sourceElement))
            {
                return sourceElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("debugTelemetry", out var debugTelemetry) &&
                debugTelemetry.ValueKind == JsonValueKind.Object &&
                debugTelemetry.TryGetProperty("source", out var nestedSourceElement))
            {
                return nestedSourceElement.GetString();
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to extract telemetry source from feature vector");
        }

        return null;
    }

    private record RecommendationEntry
    {
        public string StyleId { get; set; } = null!;
        public double Score { get; set; }
        public int Rank { get; set; }
    }

    private sealed record ConfidenceRange(string Name, double LowerInclusive, double? UpperExclusive);
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
