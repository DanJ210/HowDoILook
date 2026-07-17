using AiStyleApp.Api.Models;

namespace AiStyleApp.Api.Services;

public interface IRecommendationService
{
    Task<Guid> CreateAndEnqueueAsync(CreateRecommendationsRequest request, string userId, CancellationToken ct = default);
    Task<RecommendationJobStatusResponse?> GetStatusAsync(Guid analysisJobId, string userId, CancellationToken ct = default);
    Task SubmitFeedbackAsync(SubmitRecommendationFeedbackRequest request, string userId, CancellationToken ct = default);
    Task SubmitRatingsAsync(Guid analysisJobId, SubmitRecommendationRatingsRequest request, string userId, CancellationToken ct = default);
}
