using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using AiStyleApp.Api.Infrastructure;
using Microsoft.Extensions.Options;

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
        var container = _client.GetBlobContainerClient(_containerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: ct);

        // Scope blobs per user to keep storage organised; random name prevents enumeration
        var blobName = $"{userId}/{Guid.NewGuid()}{extension}";
        var blob = container.GetBlobClient(blobName);

        await blob.UploadAsync(data, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: ct);

        return blob.Uri.ToString();
    }
}
