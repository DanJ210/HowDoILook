namespace AiStyleApp.Api.Infrastructure;

public class BlobStorageOptions
{
    public const string Section = "BlobStorage";
    public string ConnectionString { get; init; } = string.Empty;
    public string ContainerName { get; init; } = "user-uploads";
}
