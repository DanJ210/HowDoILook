using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using AiStyleApp.Api.Infrastructure;
using Microsoft.Extensions.Options;
using Azure;

namespace AiStyleApp.Api.Services;

public class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _client;
    private readonly string _containerName;

    public BlobStorageService(IOptions<BlobStorageOptions> options)
    {
        _client = new BlobServiceClient(options.Value.ConnectionString);
        _containerName = options.Value.ContainerName;
    }

    public async Task<string> UploadAsync(
        Stream data,
        string contentType,
        string userId,
        string extension,
        CancellationToken ct = default)
    {
        var blobName = $"{userId}/{Guid.NewGuid()}{extension}";
        return await UploadAsync(data, contentType, blobName, ct);
    }

    public async Task<string> UploadAsync(
        Stream data,
        string contentType,
        string blobPath,
        CancellationToken ct = default)
    {
        var container = _client.GetBlobContainerClient(_containerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: ct);

        var blob = container.GetBlobClient(blobPath);
        await blob.UploadAsync(data, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: ct);

        return blob.Uri.ToString();
    }

    public async Task<(Stream Content, string ContentType)?> DownloadAsync(
        string userId,
        string fileName,
        CancellationToken ct = default)
    {
        var container = _client.GetBlobContainerClient(_containerName);
        var blobName = $"{userId}/{fileName}";
        var blob = container.GetBlobClient(blobName);

        try
        {
            var response = await blob.DownloadStreamingAsync(cancellationToken: ct);
            var contentType = response.Value.Details.ContentType;
            if (string.IsNullOrWhiteSpace(contentType))
            {
                contentType = "application/octet-stream";
            }

            return (response.Value.Content, contentType);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }
}
