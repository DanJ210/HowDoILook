using AiStyleApp.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AiStyleApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UploadController : ControllerBase
{
    private static readonly Dictionary<string, string> AllowedTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ".jpg",
            ["image/png"]  = ".png",
            ["image/webp"] = ".webp",
            ["image/gif"]  = ".gif",
        };

    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    private readonly IBlobStorageService _blobs;

    public UploadController(IBlobStorageService blobs) => _blobs = blobs;

    private string UserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue("sub")
        ?? throw new InvalidOperationException("User identity not found.");

    /// <summary>Upload a user photo for style generation. Returns the public blob URL.</summary>
    [HttpPost("image")]
    [RequestSizeLimit(10 * 1024 * 1024 + 4096)]
    [RequestFormLimits(MultipartBodyLengthLimit = 10 * 1024 * 1024 + 4096)]
    public async Task<ActionResult<UploadImageResponse>> UploadImage(
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("No file provided.");

        if (file.Length > MaxFileSizeBytes)
            return BadRequest("File exceeds the 10 MB limit.");

        if (!AllowedTypes.TryGetValue(file.ContentType, out var extension))
            return BadRequest(
                $"Unsupported file type '{file.ContentType}'. Allowed: JPEG, PNG, WebP, GIF.");

        await using var stream = file.OpenReadStream();
        var url = await _blobs.UploadAsync(stream, file.ContentType, UserId, extension, ct);

        return Ok(new UploadImageResponse(url));
    }

    /// <summary>
    /// Public image endpoint for external processors (e.g. Replicate) to fetch uploaded files.
    /// In local development this bridges private Azurite URLs through the backend/ngrok URL.
    /// </summary>
    [AllowAnonymous]
    [AcceptVerbs("GET", "HEAD", Route = "public/{userId}/{fileName}")]
    public async Task<IActionResult> GetPublicImage(string userId, string fileName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(fileName))
            return BadRequest("Invalid image path.");

        var downloaded = await _blobs.DownloadAsync(userId, fileName, ct);
        if (downloaded is null)
            return NotFound();

        if (HttpMethods.IsHead(Request.Method))
            return Ok();

        return File(downloaded.Value.Content, downloaded.Value.ContentType);
    }
}

public record UploadImageResponse(string Url);
