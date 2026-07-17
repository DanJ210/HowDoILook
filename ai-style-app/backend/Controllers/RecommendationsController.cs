using AiStyleApp.Api.Models;
using AiStyleApp.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AiStyleApp.Api.Controllers;

[ApiController]
[Route("api/recommendations")]
[Authorize]
public class RecommendationsController : ControllerBase
{
    private readonly IRecommendationService _recommendations;

    public RecommendationsController(IRecommendationService recommendations)
    {
        _recommendations = recommendations;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")
        ?? throw new InvalidOperationException("User identity not found.");

    [HttpPost]
    public async Task<ActionResult<CreateRecommendationsResponse>> Create(
        [FromBody] CreateRecommendationsRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ImageUrl))
        {
            return BadRequest("ImageUrl is required.");
        }

        var (analysisJobId, recommendationPostId) = await _recommendations.CreateAndEnqueueAsync(request, UserId, ct);

        var response = new CreateRecommendationsResponse(
            AnalysisJobId: analysisJobId,
            RecommendationPostId: recommendationPostId,
            Status: "Queued",
            StatusEndpoint: Url.Action(nameof(GetById), new { id = analysisJobId }) ?? $"/api/recommendations/jobs/{analysisJobId}",
            PublicEndpoint: $"/api/style/{recommendationPostId}");

        return AcceptedAtAction(nameof(GetById), new { id = analysisJobId }, response);
    }

    [HttpGet("jobs/{id:guid}")]
    public async Task<ActionResult<RecommendationJobStatusResponse>> GetById(Guid id, CancellationToken ct)
    {
        var status = await _recommendations.GetStatusAsync(id, UserId, ct);
        if (status is null)
        {
            return NotFound();
        }

        return Ok(status);
    }

    [HttpPost("feedback")]
    public async Task<IActionResult> SubmitFeedback(
        [FromBody] SubmitRecommendationFeedbackRequest request,
        CancellationToken ct)
    {
        if (request.AnalysisJobId == Guid.Empty)
        {
            return BadRequest("AnalysisJobId is required.");
        }

        try
        {
            await _recommendations.SubmitFeedbackAsync(request, UserId, ct);
            return Accepted();
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost("jobs/{id:guid}/ratings")]
    public async Task<IActionResult> SubmitRatings(
        Guid id,
        [FromBody] SubmitRecommendationRatingsRequest request,
        CancellationToken ct)
    {
        if (request.AnalysisJobId.HasValue && request.AnalysisJobId.Value != id)
        {
            return BadRequest("AnalysisJobId in body must match route id when provided.");
        }

        if (request.Rankings is null || request.Rankings.Count == 0)
        {
            return BadRequest("At least one ranking is required.");
        }

        try
        {
            await _recommendations.SubmitRatingsAsync(id, request, UserId, ct);
            return Accepted();
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
