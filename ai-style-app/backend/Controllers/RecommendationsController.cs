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

        var analysisJobId = await _recommendations.CreateAndEnqueueAsync(request, UserId, ct);

        var response = new CreateRecommendationsResponse(
            AnalysisJobId: analysisJobId,
            Status: "Queued",
            StatusEndpoint: Url.Action(nameof(GetById), new { id = analysisJobId }) ?? $"/api/recommendations/jobs/{analysisJobId}");

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
}
