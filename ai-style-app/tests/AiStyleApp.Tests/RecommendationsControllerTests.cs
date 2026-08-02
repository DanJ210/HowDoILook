using AiStyleApp.Api.Controllers;
using AiStyleApp.Api.Models;
using AiStyleApp.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AiStyleApp.Tests;

public class RecommendationsControllerTests
{
    [Fact]
    public async Task GetById_WhenGenerationAllVariantsFailedSignalPresent_ReturnsStatusPayload()
    {
        var analysisJobId = Guid.NewGuid();
        var service = new StubRecommendationService
        {
            GetStatusAction = (_, _, _) => new RecommendationJobStatusResponse(
                AnalysisJobId: analysisJobId,
                RecommendationPostId: Guid.NewGuid(),
                PublishStatus: "Published",
                Status: "Succeeded",
                QualityGate: new RecommendationQualityGateResponse(true, null, null),
                AnalysisSummary: new RecommendationAnalysisSummaryResponse("Square", 0.91),
                BestRecommendation: null,
                BestVariant: null,
                ExperimentalVariants: [],
                Recommendations: [],
                Experiment: new RecommendationExperimentResponse(true, 100, true, "bucket"),
                DebugTelemetry: null,
                ErrorCode: "GENERATION_ALL_VARIANTS_FAILED",
                ErrorMessage: "Generation finished without any successful variants. Try another photo or run Analyze and Recommend again.",
                SelectedGenerationJobId: null,
                SelectedAtUtc: null)
        };

        var controller = CreateController(service, "user-1");

        var result = await controller.GetById(analysisJobId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<RecommendationJobStatusResponse>(ok.Value);
        Assert.Equal("Succeeded", payload.Status);
        Assert.Equal("GENERATION_ALL_VARIANTS_FAILED", payload.ErrorCode);
        Assert.Contains("without any successful variants", payload.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FinalizeSelection_WhenGenerationJobIdEmpty_ReturnsBadRequest()
    {
        var service = new StubRecommendationService();
        var controller = CreateController(service, "user-1");

        var result = await controller.FinalizeSelection(
            Guid.NewGuid(),
            new FinalizeRecommendationRequest(Guid.Empty),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("GenerationJobId is required.", badRequest.Value);
    }

    [Fact]
    public async Task FinalizeSelection_WhenServiceThrowsInvalidOperation_ReturnsNotFound()
    {
        var service = new StubRecommendationService
        {
            FinalizeAction = (_, _, _, _) => throw new InvalidOperationException("not found")
        };
        var controller = CreateController(service, "user-1");

        var result = await controller.FinalizeSelection(
            Guid.NewGuid(),
            new FinalizeRecommendationRequest(Guid.NewGuid()),
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task FinalizeSelection_WhenServiceThrowsArgumentException_ReturnsBadRequest()
    {
        var service = new StubRecommendationService
        {
            FinalizeAction = (_, _, _, _) => throw new ArgumentException("validation failed")
        };
        var controller = CreateController(service, "user-1");

        var result = await controller.FinalizeSelection(
            Guid.NewGuid(),
            new FinalizeRecommendationRequest(Guid.NewGuid()),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("validation failed", badRequest.Value);
    }

    [Fact]
    public async Task FinalizeSelection_WhenServiceSucceeds_ReturnsOk()
    {
        var analysisJobId = Guid.NewGuid();
        var generationJobId = Guid.NewGuid();
        var selectedAtUtc = DateTimeOffset.UtcNow;

        var service = new StubRecommendationService
        {
            FinalizeAction = (_, _, _, _) =>
                new FinalizeRecommendationResponse(analysisJobId, generationJobId, selectedAtUtc, false)
        };
        var controller = CreateController(service, "user-1");

        var result = await controller.FinalizeSelection(
            analysisJobId,
            new FinalizeRecommendationRequest(generationJobId),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<FinalizeRecommendationResponse>(ok.Value);
        Assert.Equal(analysisJobId, payload.AnalysisJobId);
        Assert.Equal(generationJobId, payload.SelectedGenerationJobId);
        Assert.False(payload.AlreadyFinalized);
    }

    private static RecommendationsController CreateController(IRecommendationService service, string userId)
    {
        var controller = new RecommendationsController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, userId)
                    ], "test"))
                }
            }
        };

        return controller;
    }

    private sealed class StubRecommendationService : IRecommendationService
    {
        public Func<Guid, FinalizeRecommendationRequest, string, CancellationToken, FinalizeRecommendationResponse>? FinalizeAction { get; init; }
        public Func<Guid, string, CancellationToken, RecommendationJobStatusResponse?>? GetStatusAction { get; init; }

        public Task<(Guid AnalysisJobId, Guid RecommendationPostId)> CreateAndEnqueueAsync(CreateRecommendationsRequest request, string userId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<RecommendationJobStatusResponse?> GetStatusAsync(Guid analysisJobId, string userId, CancellationToken ct = default)
        {
            if (GetStatusAction is null)
            {
                throw new NotImplementedException();
            }

            return Task.FromResult(GetStatusAction(analysisJobId, userId, ct));
        }

        public Task SubmitRatingsAsync(Guid analysisJobId, SubmitRecommendationRatingsRequest request, string userId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<FinalizeRecommendationResponse> FinalizeSelectionAsync(Guid analysisJobId, FinalizeRecommendationRequest request, string userId, CancellationToken ct = default)
        {
            if (FinalizeAction is null)
            {
                throw new NotImplementedException();
            }

            return Task.FromResult(FinalizeAction(analysisJobId, request, userId, ct));
        }
    }
}
