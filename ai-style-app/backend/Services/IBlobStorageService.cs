namespace AiStyleApp.Api.Services;

public interface IBlobStorageService
{
    /// <summary>Uploads a stream and returns the public blob URL.</summary>
    Task<string> UploadAsync(
        Stream data,
        string contentType,
        string userId,
        string extension,
        CancellationToken ct = default);

    /// <summary>Downloads a blob stream and content type for the specified user-scoped blob.</summary>
    Task<(Stream Content, string ContentType)?> DownloadAsync(
        string userId,
        string fileName,
        CancellationToken ct = default);
}
