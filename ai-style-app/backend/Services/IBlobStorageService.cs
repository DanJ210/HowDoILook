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
}
