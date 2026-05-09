using AiStyleApp.Data;
using Microsoft.EntityFrameworkCore;

namespace AiStyleApp.Api.Services;

public interface IGeneratedImageArchiver
{
    /// <summary>
    /// Downloads the image at <paramref name="replicateImageUrl"/>, stores it permanently in blob
    /// storage, and updates the job's <c>ResultImageUrl</c> column.
    /// </summary>
    Task ArchiveAsync(Guid jobId, string replicateImageUrl, string userId, CancellationToken ct = default);
}

public class GeneratedImageArchiver : IGeneratedImageArchiver
{
    private readonly IBlobStorageService _blobs;
    private readonly AppDbContext _db;
    private readonly HttpClient _http;
    private readonly ILogger<GeneratedImageArchiver> _logger;

    public GeneratedImageArchiver(
        IBlobStorageService blobs,
        AppDbContext db,
        HttpClient http,
        ILogger<GeneratedImageArchiver> logger)
    {
        _blobs = blobs;
        _db = db;
        _http = http;
        _logger = logger;
    }

    public async Task ArchiveAsync(
        Guid jobId,
        string replicateImageUrl,
        string userId,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Archiving generated image for job {JobId} from {Url}",
            jobId, replicateImageUrl);

        using var response = await _http.GetAsync(replicateImageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/webp";
        var ext = contentType switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            _ => ".webp"
        };

        await using var imageStream = await response.Content.ReadAsStreamAsync(ct);
        var blobPath = $"generated/{userId}/{jobId}{ext}";
        var blobUrl = await _blobs.UploadAsync(imageStream, contentType, blobPath, ct);

        var job = await _db.StyleJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is not null)
        {
            job.ResultImageUrl = blobUrl;
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Job {JobId} generated image archived to {BlobUrl}",
            jobId, blobUrl);
    }
}
