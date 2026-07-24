namespace AiStyleApp.Api.Models;

public record RecommendationPreferences(
    string? MaintenanceLevel,
    string? StyleVibe,
    bool AllowHairColorChange = true,
    bool AllowBeardSuggestions = true
);

public record CreateRecommendationsRequest(
    string ImageUrl,
    string? Gender,
    RecommendationPreferences? Preferences
);

public record CreateRecommendationsResponse(
    Guid AnalysisJobId,
    Guid? RecommendationPostId,
    string Status,
    string StatusEndpoint,
    string? PublicEndpoint
);

public record RecommendationQualityGateResponse(
    bool? Passed,
    string? FailureCode,
    string? Message
);

public record RecommendationAnalysisSummaryResponse(
    string? FaceShape,
    double? Confidence
);

public record RecommendationStageTelemetryResponse(
    string Stage,
    string Model,
    string ModelVersion,
    double DurationMs,
    Dictionary<string, double>? Metrics,
    string? Notes
);

public record RecommendationDebugTelemetryResponse(
    string? Source,
    int? SchemaVersion,
    int? ImageWidth,
    int? ImageHeight,
    IReadOnlyList<RecommendationStageTelemetryResponse> Stages
);

public record RecommendationItemResponse(
    string StyleId,
    string StyleName,
    double Score,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Constraints
);

public record RecommendationExperimentResponse(
    bool Enabled,
    int TrafficPercent,
    bool Applied,
    string BucketKey
);

public record RecommendationVariantResponse(
    Guid GenerationJobId,
    string Status,
    string? ResultImageUrl
);

public record RecommendationExperimentalVariantResponse(
    int Slot,
    Guid GenerationJobId,
    string Status,
    string? ResultImageUrl,
    string? SelectedRank
);

public record RecommendationJobStatusResponse(
    Guid AnalysisJobId,
    Guid? RecommendationPostId,
    string? PublishStatus,
    string Status,
    RecommendationQualityGateResponse QualityGate,
    RecommendationAnalysisSummaryResponse AnalysisSummary,
    RecommendationItemResponse? BestRecommendation,
    RecommendationVariantResponse? BestVariant,
    IReadOnlyList<RecommendationExperimentalVariantResponse> ExperimentalVariants,
    IReadOnlyList<RecommendationItemResponse> Recommendations,
    RecommendationExperimentResponse Experiment,
    RecommendationDebugTelemetryResponse? DebugTelemetry,
    string? ErrorCode,
    string? ErrorMessage,
    Guid? SelectedGenerationJobId = null,
    DateTimeOffset? SelectedAtUtc = null
);

public record FinalizeRecommendationRequest(
    Guid GenerationJobId
);

public record FinalizeRecommendationResponse(
    Guid AnalysisJobId,
    Guid SelectedGenerationJobId,
    DateTimeOffset SelectedAtUtc,
    bool AlreadyFinalized
);

public record RecommendationRankingInput(
    Guid GenerationJobId,
    int Rank
);

public record SubmitRecommendationRatingsRequest(
    Guid? AnalysisJobId,
    IReadOnlyList<RecommendationRankingInput> Rankings,
    IReadOnlyList<string>? FeedbackTags,
    string? Comment
);
