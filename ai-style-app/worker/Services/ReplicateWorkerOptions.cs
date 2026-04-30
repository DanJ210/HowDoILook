namespace AiStyleApp.Worker.Services;

public class ReplicateWorkerOptions
{
    public const string Section = "Replicate";
    public string ApiToken { get; init; } = string.Empty;
    public string ModelVersion { get; init; } = string.Empty;
    public string WebhookBaseUrl { get; init; } = string.Empty;
}
