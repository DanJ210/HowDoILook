namespace AiStyleApp.Api.Models;

public record RecommendationExposureDataPoint
{
    public Guid ExposureId { get; init; }
    public Guid AnalysisJobId { get; init; }
    public required string UserId { get; init; }
    public DateTimeOffset ExposureCreatedAtUtc { get; init; }
    public DateTimeOffset? AnalysisCompletedAtUtc { get; init; }
    public string? FaceShape { get; init; }
    public string? Gender { get; init; }
    public bool QualityPassed { get; init; }
    public double AnalysisConfidence { get; init; }
    public int? TelemetrySchemaVersion { get; init; }
    public string? TelemetrySource { get; init; }
    public required string ExperimentVersion { get; init; }
    public bool ExperimentApplied { get; init; }
    public required string SystemSelectedStyleId { get; init; }
    public Guid PrimaryGenerationJobId { get; init; }
    public int EligibleCandidateCount { get; init; }
    public int ShownCandidateCount { get; init; }
    public required string CandidateStyleId { get; init; }
    public required string CandidateStyleName { get; init; }
    public int RecommendationRank { get; init; }
    public double RankingScore { get; init; }
    public bool IsPrimary { get; init; }
    public bool WasShown { get; init; }
    public int? ShownOrder { get; init; }
    public double SelectionProbability { get; init; }
    public Guid? StyleItemId { get; init; }
    public Guid? GenerationJobId { get; init; }
    public string? GenerationStatus { get; init; }
    public string? GenerationErrorCode { get; init; }
    public DateTimeOffset? GenerationCompletedAtUtc { get; init; }
}

public record RecommendationPreferenceDataPoint
{
    public Guid FeedbackId { get; init; }
    public Guid ExposureId { get; init; }
    public Guid AnalysisJobId { get; init; }
    public required string UserId { get; init; }
    public required string ExperimentVersion { get; init; }
    public required string SystemSelectedStyleId { get; init; }
    public Guid PrimaryGenerationJobId { get; init; }
    public required string CandidateStyleId { get; init; }
    public Guid GenerationJobId { get; init; }
    public bool IsPrimary { get; init; }
    public int ShownOrder { get; init; }
    public double SelectionProbability { get; init; }
    public int SubmittedRank { get; init; }
    public string? FeedbackTagsJson { get; init; }
    public string? Comment { get; init; }
    public DateTimeOffset FeedbackSubmittedAtUtc { get; init; }
}

public record RecommendationCoverageReport
{
    public int MinimumSampleSize { get; init; }
    public int ExposureCount { get; init; }
    public int PreferenceLabelCount { get; init; }
    public IReadOnlyList<StyleCoveragePoint> Styles { get; init; } = [];
    public IReadOnlyList<TelemetryRangeCoveragePoint> AnalysisConfidenceRanges { get; init; } = [];
    public DateTimeOffset? PeriodStart { get; init; }
    public DateTimeOffset? PeriodEnd { get; init; }
    public DateTimeOffset ComputedAtUtc { get; init; }
}

public record StyleCoveragePoint
{
    public required string StyleId { get; init; }
    public int EligibleCount { get; init; }
    public int ShownCount { get; init; }
    public int PrimaryCount { get; init; }
    public int SucceededGenerationCount { get; init; }
    public int PreferenceLabelCount { get; init; }
    public double AverageSelectionProbability { get; init; }
    public bool IsSparse { get; init; }
}

public record TelemetryRangeCoveragePoint
{
    public required string Range { get; init; }
    public double LowerInclusive { get; init; }
    public double? UpperExclusive { get; init; }
    public int ExposureCount { get; init; }
    public int ShownCandidateCount { get; init; }
    public int PreferenceLabelCount { get; init; }
    public bool IsSparse { get; init; }
}