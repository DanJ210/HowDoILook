using AiStyleApp.Backend.Controllers;
using AiStyleApp.Backend.Services;
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

    private sealed class StubAnalyticsService : IAnalyticsService
    {
        public Task<IEnumerable<RecommendationDataPoint>> ExportRecommendationsDataAsync(
            DateTimeOffset? fromDate = null,
            DateTimeOffset? toDate = null)
            => Task.FromResult<IEnumerable<RecommendationDataPoint>>([]);

        public Task<RecommendationMetrics> GetMetricsAsync(
            DateTimeOffset? fromDate = null,
            DateTimeOffset? toDate = null)
            => Task.FromResult(new RecommendationMetrics());
    }
}
