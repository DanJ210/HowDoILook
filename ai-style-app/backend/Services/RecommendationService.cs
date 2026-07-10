using System.Text.Json;
using AiStyleApp.Api.Models;
using AiStyleApp.Data;
using AiStyleApp.Data.Entities;
using AiStyleApp.Data.Queue;
using Microsoft.EntityFrameworkCore;

namespace AiStyleApp.Api.Services;

public class RecommendationService : IRecommendationService
{
    private readonly AppDbContext _db;
    private readonly IQueuePublisher _queue;

    public RecommendationService(AppDbContext db, IQueuePublisher queue)
    {
        _db = db;
        _queue = queue;
    }

    public async Task<Guid> CreateAndEnqueueAsync(
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

        _db.FaceAnalysisJobs.Add(analysisJob);
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

        return analysisJob.Id;
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

        return new RecommendationJobStatusResponse(
            AnalysisJobId: analysisJob.Id,
            Status: analysisJob.Status,
            QualityGate: new RecommendationQualityGateResponse(
                Passed: analysisJob.QualityPassed,
                FailureCode: analysisJob.QualityFailureCode,
                Message: analysisJob.QualityMessage),
            AnalysisSummary: new RecommendationAnalysisSummaryResponse(
                FaceShapeDistribution: null,
                Confidence: analysisJob.AnalysisConfidence),
            Recommendations: ParseRecommendations(analysisJob.RecommendationsJson),
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
}
