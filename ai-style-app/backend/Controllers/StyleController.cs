using AiStyleApp.Api.Models;
using AiStyleApp.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AiStyleApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class StyleController : ControllerBase
{
    private readonly IStyleService _styleService;
    private readonly ILogger<StyleController> _logger;

    public StyleController(IStyleService styleService, ILogger<StyleController> logger)
    {
        _styleService = styleService;
        _logger = logger;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")
        ?? throw new InvalidOperationException("User identity not found.");

    [HttpGet]
    public async Task<ActionResult<IEnumerable<StyleItemResponse>>> GetAll(CancellationToken ct)
    {
        var items = await _styleService.GetAllAsync(UserId, ct);
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StyleItemResponse>> GetById(Guid id, CancellationToken ct)
    {
        var item = await _styleService.GetByIdAsync(id, UserId, ct);
        if (item is null) return NotFound();
        return Ok(item);
    }

    [HttpPost("generate")]
    public async Task<ActionResult<GenerateStyleResponse>> Generate(
        [FromBody] GenerateStyleRequest request,
        CancellationToken ct)
    {
        var (item, jobId) = await _styleService.CreateAndEnqueueAsync(request, UserId, ct);

        var response = new GenerateStyleResponse(
            jobId,
            item.Id,
            JobStatus.Queued,
            Url.Action(nameof(GetById), "Jobs", new { id = jobId }, Request.Scheme)!
        );

        return AcceptedAtAction("GetById", "Jobs", new { id = jobId }, response);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var deleted = await _styleService.DeleteAsync(id, UserId, ct);
        if (!deleted) return NotFound();
        return NoContent();
    }
}

