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
    string Status,
    string StatusEndpoint
);

public record RecommendationQualityGateResponse(
    bool? Passed,
    string? FailureCode,
    string? Message
);

public record RecommendationAnalysisSummaryResponse(
    Dictionary<string, double>? FaceShapeDistribution,
    double? Confidence
);

public record RecommendationItemResponse(
    string StyleId,
    string StyleName,
    double Score,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Constraints
);

public record RecommendationJobStatusResponse(
    Guid AnalysisJobId,
    string Status,
    RecommendationQualityGateResponse QualityGate,
    RecommendationAnalysisSummaryResponse AnalysisSummary,
    IReadOnlyList<RecommendationItemResponse> Recommendations,
    string? ErrorCode,
    string? ErrorMessage
);

public record SubmitRecommendationFeedbackRequest(
    Guid AnalysisJobId,
    string? SelectedStyleId,
    int? Rating,
    IReadOnlyList<string>? FeedbackTags,
    string? Comment
);
