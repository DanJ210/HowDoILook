using AiStyleApp.Api.Controllers;
using AiStyleApp.Api.Models;
using AiStyleApp.Api.Services;
using AiStyleApp.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace AiStyleApp.Tests;

public class AnalyticsControllerTests
{
    [Fact]
    public void AnalyticsController_HasAuthorizeAttribute()
    {
        var authorizeAttribute = typeof(AnalyticsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .SingleOrDefault();

        Assert.NotNull(authorizeAttribute);
    }

    [Fact]
    public async Task ExportRecommendationsAsync_InvalidFormat_ReturnsBadRequest()
    {
        var controller = new AnalyticsController(new StubAnalyticsService(), NullLogger<AnalyticsController>.Instance);

        var result = await controller.ExportRecommendationsAsync("tsv");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var responseJson = JsonSerializer.Serialize(badRequest.Value);
        Assert.Contains("Invalid format: tsv", responseJson, StringComparison.Ordinal);
        Assert.Contains("json", responseJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("csv", responseJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportRecommendationsAsync_InvalidDataset_ReturnsBadRequest()
    {
        var controller = new AnalyticsController(new StubAnalyticsService(), NullLogger<AnalyticsController>.Instance);

        var result = await controller.ExportRecommendationsAsync(dataset: "labels");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var responseJson = JsonSerializer.Serialize(badRequest.Value);
        Assert.Contains("Invalid dataset: labels", responseJson, StringComparison.Ordinal);
        Assert.Contains("exposures", responseJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferences", responseJson, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubAnalyticsService : IAnalyticsService
    {
        public Task<IReadOnlyList<RecommendationExposureDataPoint>> ExportExposureOutcomesAsync(
            DateTimeOffset? fromDate = null,
            DateTimeOffset? toDate = null)
            => Task.FromResult<IReadOnlyList<RecommendationExposureDataPoint>>([]);

        public Task<IReadOnlyList<RecommendationPreferenceDataPoint>> ExportPreferenceLabelsAsync(
            DateTimeOffset? fromDate = null,
            DateTimeOffset? toDate = null)
            => Task.FromResult<IReadOnlyList<RecommendationPreferenceDataPoint>>([]);

        public Task<RecommendationCoverageReport> GetCoverageAsync(
            DateTimeOffset? fromDate = null,
            DateTimeOffset? toDate = null)
            => Task.FromResult(new RecommendationCoverageReport());

        public Task<RecommendationMetrics> GetMetricsAsync(
            DateTimeOffset? fromDate = null,
            DateTimeOffset? toDate = null)
            => Task.FromResult(new RecommendationMetrics());
    }
}
