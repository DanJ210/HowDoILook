using AiStyleApp.Api.Models;

namespace AiStyleApp.Api.Services;

public interface IRecommendationService
{
    Task<(Guid AnalysisJobId, Guid RecommendationPostId)> CreateAndEnqueueAsync(CreateRecommendationsRequest request, string userId, CancellationToken ct = default);
    Task<RecommendationJobStatusResponse?> GetStatusAsync(Guid analysisJobId, string userId, CancellationToken ct = default);
    Task SubmitRatingsAsync(Guid analysisJobId, SubmitRecommendationRatingsRequest request, string userId, CancellationToken ct = default);
    Task<FinalizeRecommendationResponse> FinalizeSelectionAsync(Guid analysisJobId, FinalizeRecommendationRequest request, string userId, CancellationToken ct = default);
}
