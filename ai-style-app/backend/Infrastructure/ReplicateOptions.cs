namespace AiStyleApp.Api.Infrastructure;

public class ReplicateOptions
{
    public const string Section = "Replicate";
    public string ApiToken { get; init; } = string.Empty;
    public string WebhookSigningSecret { get; init; } = string.Empty;
}
