using AiStyleApp.Api.Models;
using AiStyleApp.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AiStyleApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class JobsController : ControllerBase
{
    private readonly IJobService _jobs;

    public JobsController(IJobService jobs)
    {
        _jobs = jobs;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")
        ?? throw new InvalidOperationException("User identity not found.");

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UserJobSummaryResponse>>> GetAll(CancellationToken ct)
    {
        var jobs = await _jobs.GetAllAsync(UserId, ct);
        return Ok(jobs);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<JobStatusResponse>> GetById(Guid id, CancellationToken ct)
    {
        var job = await _jobs.GetJobStatusAsync(id, UserId, ct);
        if (job is null) return NotFound();
        return Ok(job);
    }
}
